using System.Collections.Generic;
using System.IO;
using Colossal.Serialization.Entities;
using Game;
using Game.Buildings;
using Game.Common;
using Game.Prefabs;
using Game.SceneFlow;
using Game.Simulation;
using Game.Tools;
using Unity.Collections;
using Unity.Entities;
using UnityEngine.Scripting;

namespace CustomHouseholds.Systems
{
    public partial class HouseholdOverrideSystem : GameSystemBase
    {
        private PrefabSystem m_PrefabSystem;
        private CitySystem m_CitySystem;
        private EndFrameBarrier m_EndFrameBarrier;
        private EntityQuery m_ResolveQuery;
        private EntityQuery m_BuildingsQuery;
        private EntityArchetype m_RentEventArchetype;

        private readonly Dictionary<string, Entity> m_Resolved = new Dictionary<string, Entity>();
        private readonly HashSet<string> m_LoggedIssues = new HashSet<string>();
        private int m_SeenVersion = -1;
        private int m_RwhRetryCounter;
        private bool m_ResolvePrefabs;
        private bool m_PendingInitialEvict;
        private bool m_EvictNeeded;
        private bool m_AutoDumpIfMissing;

        [Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_CitySystem = World.GetOrCreateSystemManaged<CitySystem>();
            m_EndFrameBarrier = World.GetOrCreateSystemManaged<EndFrameBarrier>();

            m_ResolveQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<PrefabData>(),
                    ComponentType.ReadOnly<BuildingPropertyData>(),
                },
                None = new[]
                {
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Temp>(),
                },
            });

            m_BuildingsQuery = GetEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<Building>(),
                    ComponentType.ReadOnly<ResidentialProperty>(),
                    ComponentType.ReadOnly<PrefabRef>(),
                    ComponentType.ReadOnly<Renter>(),
                },
                None = new[]
                {
                    ComponentType.Exclude<Deleted>(),
                    ComponentType.Exclude<Temp>(),
                },
            });

            m_RentEventArchetype = EntityManager.CreateArchetype(
                ComponentType.ReadWrite<Event>(),
                ComponentType.ReadWrite<RentersUpdated>());

            RWHCompat.EnsureResolved();
        }

        public override int GetUpdateInterval(SystemUpdatePhase phase)
        {
            return 16;
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_Resolved.Clear();
            m_LoggedIssues.Clear();
            m_SeenVersion = HouseholdOverrides.Version;

            if (mode == GameMode.Game)
            {
                m_ResolvePrefabs = true;
                m_PendingInitialEvict = true;
                m_AutoDumpIfMissing = true;
            }
        }

        [Preserve]
        protected override void OnUpdate()
        {
            PollOverridesFile();
            TryResolveRwh();

            if (m_SeenVersion != HouseholdOverrides.Version)
            {
                m_SeenVersion = HouseholdOverrides.Version;
                m_ResolvePrefabs = true;

                if (HouseholdOverrides.Entries.Count > 0)
                {
                    m_EvictNeeded = true;
                }
            }

            if (m_ResolvePrefabs)
            {
                m_ResolvePrefabs = false;
                RebuildResolvedPrefabs();
            }

            ProcessPendingRestores();

            if (HouseholdOverrides.DumpRequested)
            {
                HouseholdOverrides.DumpRequested = false;
                DumpPrefabList();
            }

            if (m_AutoDumpIfMissing)
            {
                m_AutoDumpIfMissing = false;
                if (!File.Exists(HouseholdOverrides.PrefabListPath))
                {
                    Mod.log.Info("prefab_list.txt not found; exporting automatically.");
                    DumpPrefabList();
                }
            }

            ApplyOverrides();

            if (m_PendingInitialEvict)
            {
                m_PendingInitialEvict = false;
                m_EvictNeeded = true;
            }

            if (m_EvictNeeded)
            {
                TryScheduleEviction();
            }
        }

        private void PollOverridesFile()
        {
            try
            {
                if (!File.Exists(HouseholdOverrides.FilePath))
                {
                    return;
                }

                long ticks = File.GetLastWriteTimeUtc(HouseholdOverrides.FilePath).Ticks;
                if (ticks == HouseholdOverrides.LastSeenWriteTicks)
                {
                    return;
                }

                HouseholdOverrides.Load();
                Mod.log.Info("overrides.json changed on disk; reloaded automatically.");
            }
            catch (System.Exception e)
            {
                Mod.log.Warn($"Failed to poll overrides.json: {e.Message}");
            }
        }

        private void TryResolveRwh()
        {
            if (RWHCompat.HasTagComponent)
            {
                return;
            }

            if (++m_RwhRetryCounter < 8)
            {
                return;
            }

            m_RwhRetryCounter = 0;
            RWHCompat.EnsureResolved();
        }

        private void RebuildResolvedPrefabs()
        {
            RWHCompat.EnsureResolved();
            m_Resolved.Clear();

            if (m_ResolveQuery.IsEmptyIgnoreFilter)
            {
                return;
            }

            var entities = m_ResolveQuery.ToEntityArray(Allocator.Temp);
            try
            {
                foreach (var entity in entities)
                {
                    string name;
                    try
                    {
                        name = m_PrefabSystem.GetPrefabName(entity);
                    }
                    catch
                    {
                        continue;
                    }

                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    int level = EntityManager.HasComponent<SpawnableBuildingData>(entity)
                        ? EntityManager.GetComponentData<SpawnableBuildingData>(entity).m_Level
                        : -1;

                    m_Resolved[$"{name}#{level}"] = entity;
                }
            }
            finally
            {
                entities.Dispose();
            }

            Mod.log.Info($"Prefab index rebuilt: {m_Resolved.Count} building prefabs indexed.");
        }

        private bool TryGetPropertyData(Entity prefab, out BuildingPropertyData data)
        {
            if (EntityManager.HasComponent<BuildingPropertyData>(prefab))
            {
                data = EntityManager.GetComponentData<BuildingPropertyData>(prefab);
                return true;
            }

            data = default;
            return false;
        }

        private void ApplyOverrides()
        {
            foreach (var entry in HouseholdOverrides.Entries.Values)
            {
                if (entry.households <= 0)
                {
                    continue;
                }

                string key = entry.GetKey();

                if (!m_Resolved.TryGetValue(key, out var prefab) || prefab == Entity.Null)
                {
                    if (m_LoggedIssues.Add(key))
                    {
                        Mod.log.Warn($"Override '{key}' does not match any loaded prefab (yet); it will be retried.");
                    }

                    continue;
                }

                RWHCompat.EnsureTag(EntityManager, prefab);

                if (!TryGetPropertyData(prefab, out var data))
                {
                    if (m_LoggedIssues.Add(key))
                    {
                        Mod.log.Warn($"Prefab '{key}' has no BuildingPropertyData; skipping.");
                    }

                    continue;
                }

                if (data.m_ResidentialProperties <= 0)
                {
                    if (m_LoggedIssues.Add(key))
                    {
                        Mod.log.Warn($"Prefab '{key}' is not residential; skipping.");
                    }

                    continue;
                }

                m_LoggedIssues.Remove(key);

                int previous = data.m_ResidentialProperties;
                if (previous == entry.households)
                {
                    continue;
                }

                if (!entry.captured)
                {
                    if (HouseholdOverrides.TryGetKnownOriginal(key, out var knownCount, out var knownSpace))
                    {
                        entry.originalCount = knownCount;
                        entry.originalSpaceMultiplier = knownSpace;
                    }
                    else
                    {
                        entry.originalCount = previous;
                        entry.originalSpaceMultiplier = data.m_SpaceMultiplier == 0f ? 1f : data.m_SpaceMultiplier;
                        HouseholdOverrides.RememberOriginal(key, previous, data.m_SpaceMultiplier);
                    }

                    entry.captured = true;
                    HouseholdOverrides.Save();
                }

                if (data.m_SpaceMultiplier == 0f)
                {
                    data.m_SpaceMultiplier = 1f;
                }

                float ratio = entry.households / (float)previous;
                data.m_SpaceMultiplier /= ratio;
                data.m_ResidentialProperties = entry.households;
                EntityManager.SetComponentData(prefab, data);

                if (entry.households < previous)
                {
                    m_EvictNeeded = true;
                }

                Mod.log.Info($"Applied override '{key}': {previous} -> {entry.households} households.");
            }
        }

        private void ProcessPendingRestores()
        {
            if (HouseholdOverrides.PendingRestores.Count == 0)
            {
                return;
            }

            foreach (var restore in HouseholdOverrides.PendingRestores)
            {
                string key = restore.GetKey();

                if (!m_Resolved.TryGetValue(key, out var prefab) || prefab == Entity.Null)
                {
                    Mod.log.Warn($"Cannot restore '{key}': prefab not found.");
                    continue;
                }

                RWHCompat.RemoveTag(EntityManager, prefab);

                if (!TryGetPropertyData(prefab, out var data))
                {
                    continue;
                }

                data.m_ResidentialProperties = restore.originalCount;
                data.m_SpaceMultiplier = restore.originalSpaceMultiplier;
                EntityManager.SetComponentData(prefab, data);
                Mod.log.Info($"Restored override '{key}' to {restore.originalCount} households.");
            }

            HouseholdOverrides.PendingRestores.Clear();
            m_EvictNeeded = HouseholdOverrides.Entries.Count > 0;
        }

        private void DumpPrefabList()
        {
            if (m_Resolved.Count == 0)
            {
                RebuildResolvedPrefabs();
            }

            try
            {
                Directory.CreateDirectory(HouseholdOverrides.DataFolder);
                using var writer = new StreamWriter(HouseholdOverrides.PrefabListPath, false);

                writer.WriteLine("# CustomHouseholds - predios residenciais (nome#nivel = familias atuais)");
                writer.WriteLine("# Para alterar quantidades, edite overrides.json nesta pasta. Ex.: { \"Nome do Prefab#2\": 10 }");
                writer.WriteLine("# O mod recarrega o overrides.json automaticamente alguns segundos apos salvar.");
                writer.WriteLine();

                foreach (var pair in m_Resolved)
                {
                    if (!TryGetPropertyData(pair.Value, out var data))
                    {
                        continue;
                    }

                    if (data.m_ResidentialProperties <= 0)
                    {
                        continue;
                    }

                    writer.WriteLine($"{pair.Key} = {data.m_ResidentialProperties}");
                }

                Mod.log.Info($"Prefab list written to {HouseholdOverrides.PrefabListPath}");
            }
            catch (System.Exception e)
            {
                Mod.log.Error($"Failed to write prefab list: {e}");
            }
        }

        private void TryScheduleEviction()
        {
            if (Mod.m_Setting != null && !Mod.m_Setting.EvictExcessImmediately)
            {
                m_EvictNeeded = false;
                return;
            }

            if (HouseholdOverrides.Entries.Count == 0 || m_Resolved.Count == 0)
            {
                m_EvictNeeded = false;
                return;
            }

            var city = m_CitySystem.City;
            if (city == Entity.Null || !EntityManager.Exists(city))
            {
                return;
            }

            m_EvictNeeded = false;

            var prefabs = new NativeParallelHashSet<Entity>(m_Resolved.Count, Allocator.TempJob);
            foreach (var entry in HouseholdOverrides.Entries.Values)
            {
                if (m_Resolved.TryGetValue(entry.GetKey(), out var prefab) && prefab != Entity.Null)
                {
                    prefabs.Add(prefab);
                }
            }

            if (prefabs.IsEmpty)
            {
                prefabs.Dispose();
                return;
            }

            var ecb = m_EndFrameBarrier.CreateCommandBuffer();
            var job = new EvictExcessJob
            {
                ecb = ecb,
                entityTypeHandle = GetEntityTypeHandle(),
                renterTypeHandle = GetBufferTypeHandle<Renter>(false),
                prefabRefTypeHandle = GetComponentTypeHandle<PrefabRef>(true),
                propertyDataLookup = GetComponentLookup<BuildingPropertyData>(true),
                commercialPropertyLookup = GetComponentLookup<CommercialProperty>(true),
                workProviderLookup = GetComponentLookup<Game.Companies.WorkProvider>(true),
                propertyOnMarketLookup = GetComponentLookup<PropertyOnMarket>(true),
                overriddenPrefabs = prefabs,
                rentEventArchetype = m_RentEventArchetype,
            };

            Dependency = job.Schedule(m_BuildingsQuery, Dependency);
            m_EndFrameBarrier.AddJobHandleForProducer(Dependency);
            prefabs.Dispose(Dependency);
            Mod.log.Info("Eviction pass scheduled for overridden prefabs.");
        }
    }
}
