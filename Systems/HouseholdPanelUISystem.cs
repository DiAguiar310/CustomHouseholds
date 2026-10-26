using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Colossal.Serialization.Entities;
using Colossal.UI.Binding;
using Game;
using Game.Buildings;
using Game.Prefabs;
using Game.SceneFlow;
using Game.UI;
using Game.UI.InGame;
using Unity.Entities;
using cohtml.Net;

namespace CustomHouseholds.Systems
{
    public partial class HouseholdPanelUISystem : UISystemBase
    {
        private const string kGroup = "CustomHouseholds";
        private const string kPanelScriptResource = "CustomHouseholds.UI.panel.js";

        private SelectedInfoUISystem m_SelectedInfo;
        private PrefabSystem m_PrefabSystem;
        private string m_PanelScript;

        private bool m_Visible;
        private string m_Name = "";
        private int m_Level = -1;
        private int m_Current;
        private bool m_Overridden;
        private int m_Original = -1;

        private Entity m_LastEntity = Entity.Null;
        private Entity m_SelectedPrefab = Entity.Null;
        private string m_SelectedKey;
        private bool m_Injected;
        private int m_InjectCounter = 60;
        private string m_LastInjectPayload;

        private static readonly string kAutoTestPath = Path.Combine(HouseholdOverrides.DataFolder, "autotest.txt");
        private int m_AutoTestStage;
        private DateTime m_AutoTestNext;

        [UnityEngine.Scripting.Preserve]
        protected override void OnCreate()
        {
            base.OnCreate();

            m_SelectedInfo = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
            m_PrefabSystem = World.GetOrCreateSystemManaged<PrefabSystem>();
            m_PanelScript = LoadPanelScript();

            try
            {
                m_SelectedInfo.AddMiddleSection(new HouseholdOverrideSection());
                Mod.log.Info("UI section registered into the selected info panel.");
            }
            catch (Exception e)
            {
                Mod.log.Warn($"Failed to register UI section: {e.Message}");
            }

            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "visible", (Func<bool>)(() => m_Visible), (IWriter<bool>)null, (EqualityComparer<bool>)null));
            AddUpdateBinding(new GetterValueBinding<string>(kGroup, "name", (Func<string>)(() => m_Name), (IWriter<string>)null, (EqualityComparer<string>)null));
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "level", (Func<int>)(() => m_Level), (IWriter<int>)null, (EqualityComparer<int>)null));
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "current", (Func<int>)(() => m_Current), (IWriter<int>)null, (EqualityComparer<int>)null));
            AddUpdateBinding(new GetterValueBinding<bool>(kGroup, "overridden", (Func<bool>)(() => m_Overridden), (IWriter<bool>)null, (EqualityComparer<bool>)null));
            AddUpdateBinding(new GetterValueBinding<int>(kGroup, "original", (Func<int>)(() => m_Original), (IWriter<int>)null, (EqualityComparer<int>)null));

            AddBinding(new TriggerBinding<int>(kGroup, "apply", (Action<int>)ApplyFromUI, (IReader<int>)null));
            AddBinding(new TriggerBinding(kGroup, "reset", (Action)ResetFromUI));
            AddBinding(new TriggerBinding<string>(kGroup, "injected", (Action<string>)OnInjected, (IReader<string>)null));
            AddBinding(new TriggerBinding<string>(kGroup, "jsdbg", (Action<string>)OnJsDiag, (IReader<string>)null));
            AddBinding(new TriggerBinding<string>(kGroup, "jserr", (Action<string>)OnJsError, (IReader<string>)null));

            InjectPanel();
        }

        [UnityEngine.Scripting.Preserve]
        protected override void OnUpdate()
        {
            base.OnUpdate();

            if (++m_InjectCounter >= 240)
            {
                m_InjectCounter = 0;
                InjectPanel();
            }

            RefreshSelection();
            RunAutoTest();
        }

        protected override void OnGameLoadingComplete(Purpose purpose, GameMode mode)
        {
            base.OnGameLoadingComplete(purpose, mode);
            m_LastEntity = Entity.Null;
            m_Injected = false;
            m_InjectCounter = 240;
            ClearSelection();

            m_AutoTestStage = (mode == GameMode.Game && File.Exists(kAutoTestPath)) ? 1 : 0;
            m_AutoTestNext = DateTime.UtcNow.AddSeconds(20);
            if (m_AutoTestStage == 1)
            {
                Mod.log.Info("AUTOTEST: armed; will select a residential building 20s after load.");
            }
        }

        private void RefreshSelection()
        {
            Entity entity = m_SelectedInfo != null ? m_SelectedInfo.selectedEntity : Entity.Null;
            if (entity == m_LastEntity && m_SelectedKey != null)
            {
                if (m_SelectedPrefab != Entity.Null && EntityManager.Exists(m_SelectedPrefab)
                    && EntityManager.HasComponent<BuildingPropertyData>(m_SelectedPrefab))
                {
                    m_Current = EntityManager.GetComponentData<BuildingPropertyData>(m_SelectedPrefab).m_ResidentialProperties;
                }

                return;
            }

            m_LastEntity = entity;

            if (entity == Entity.Null || !EntityManager.Exists(entity)
                || !EntityManager.HasComponent<Building>(entity)
                || !EntityManager.HasComponent<PrefabRef>(entity))
            {
                ClearSelection();
                return;
            }

            Entity prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
            if (prefab == Entity.Null || !EntityManager.Exists(prefab)
                || !EntityManager.HasComponent<BuildingPropertyData>(prefab))
            {
                ClearSelection();
                return;
            }

            var data = EntityManager.GetComponentData<BuildingPropertyData>(prefab);
            if (data.m_ResidentialProperties <= 0)
            {
                ClearSelection();
                return;
            }

            string name;
            try
            {
                name = m_PrefabSystem.GetPrefabName(prefab);
            }
            catch
            {
                ClearSelection();
                return;
            }

            if (string.IsNullOrEmpty(name))
            {
                ClearSelection();
                return;
            }

            int level = EntityManager.HasComponent<SpawnableBuildingData>(prefab)
                ? EntityManager.GetComponentData<SpawnableBuildingData>(prefab).m_Level
                : -1;

            m_SelectedPrefab = prefab;
            m_SelectedKey = $"{name}#{level}";
            m_Visible = true;
            m_Name = name;
            m_Level = level;
            m_Current = data.m_ResidentialProperties;

            Mod.log.Info($"UI selection: '{m_SelectedKey}' current={m_Current} households.");

            if (HouseholdOverrides.Entries.TryGetValue(m_SelectedKey, out var entry))
            {
                m_Overridden = true;
                m_Original = entry.captured ? entry.originalCount : -1;
            }
            else
            {
                m_Overridden = false;
                m_Original = -1;
            }
        }

        private void ClearSelection()
        {
            if (m_Visible)
            {
                Mod.log.Info("UI selection cleared.");
            }

            m_Visible = false;
            m_SelectedPrefab = Entity.Null;
            m_SelectedKey = null;
            m_Name = "";
            m_Level = -1;
            m_Current = 0;
            m_Overridden = false;
            m_Original = -1;
        }

        private void ApplyFromUI(int households)
        {
            if (string.IsNullOrEmpty(m_SelectedKey) || m_SelectedPrefab == Entity.Null)
            {
                return;
            }

            if (households <= 0 || households > 10000)
            {
                Mod.log.Warn($"UI: ignoring invalid household count {households} for '{m_SelectedKey}'.");
                return;
            }

            if (!EntityManager.Exists(m_SelectedPrefab) || !EntityManager.HasComponent<BuildingPropertyData>(m_SelectedPrefab))
            {
                return;
            }

            var data = EntityManager.GetComponentData<BuildingPropertyData>(m_SelectedPrefab);
            int previous = data.m_ResidentialProperties;
            if (previous <= 0)
            {
                return;
            }

            HouseholdOverrides.SetOverride(m_Name, m_Level, households, previous, data.m_SpaceMultiplier);

            RWHCompat.EnsureResolved();
            RWHCompat.EnsureTag(EntityManager, m_SelectedPrefab);

            if (data.m_SpaceMultiplier == 0f)
            {
                data.m_SpaceMultiplier = 1f;
            }

            float ratio = households / (float)previous;
            data.m_SpaceMultiplier /= ratio;
            data.m_ResidentialProperties = households;
            EntityManager.SetComponentData(m_SelectedPrefab, data);

            m_Current = households;
            m_Overridden = true;
            if (HouseholdOverrides.Entries.TryGetValue(m_SelectedKey, out var entry) && entry.captured)
            {
                m_Original = entry.originalCount;
            }

            Mod.log.Info($"UI override '{m_SelectedKey}': {previous} -> {households} households.");
        }

        private void ResetFromUI()
        {
            if (string.IsNullOrEmpty(m_SelectedKey) || m_SelectedPrefab == Entity.Null)
            {
                return;
            }

            var restore = HouseholdOverrides.RemoveOverride(m_Name, m_Level);
            if (restore == null)
            {
                m_Overridden = false;
                m_Original = -1;
                return;
            }

            if (EntityManager.Exists(m_SelectedPrefab) && EntityManager.HasComponent<BuildingPropertyData>(m_SelectedPrefab))
            {
                var data = EntityManager.GetComponentData<BuildingPropertyData>(m_SelectedPrefab);
                data.m_ResidentialProperties = restore.originalCount;
                data.m_SpaceMultiplier = restore.originalSpaceMultiplier;
                EntityManager.SetComponentData(m_SelectedPrefab, data);
                m_Current = restore.originalCount;
            }

            RWHCompat.RemoveTag(EntityManager, m_SelectedPrefab);

            m_Overridden = false;
            m_Original = -1;
            Mod.log.Info($"UI override '{m_SelectedKey}' removed; restored to {restore.originalCount} households (original).");
        }

        private void OnInjected(string payload)
        {
            m_Injected = true;
            bool changed = payload != m_LastInjectPayload;
            m_LastInjectPayload = payload;
            if (changed)
            {
                Mod.log.Info($"UI panel injected and ready ({payload}).");
            }
        }

        private void RunAutoTest()
        {
            if (m_AutoTestStage != 1 && m_AutoTestStage != 2)
            {
                return;
            }

            if (DateTime.UtcNow < m_AutoTestNext)
            {
                return;
            }

            if (m_AutoTestStage == 1)
            {
                m_AutoTestStage = 2;
                m_AutoTestNext = DateTime.UtcNow.AddSeconds(20);

                Entity building = FindResidentialBuilding();
                if (building == Entity.Null)
                {
                    m_AutoTestStage = 3;
                    Mod.log.Warn("AUTOTEST: no residential building found.");
                    return;
                }

                m_SelectedInfo.SetSelection(building);
                Mod.log.Info($"AUTOTEST: selected entity {building.Index}:{building.Version}.");
            }
            else
            {
                m_AutoTestStage = 3;
                m_SelectedInfo.SetSelection(Entity.Null);
                Mod.log.Info("AUTOTEST: selection cleared; test finished.");
            }
        }

        private Entity FindResidentialBuilding()
        {
            EntityQuery query = EntityManager.CreateEntityQuery(typeof(Building), typeof(PrefabRef));
            try
            {
                using (var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp))
                {
                    for (int i = 0; i < entities.Length; i++)
                    {
                        Entity entity = entities[i];
                        Entity prefab = EntityManager.GetComponentData<PrefabRef>(entity).m_Prefab;
                        if (prefab == Entity.Null || !EntityManager.Exists(prefab)
                            || !EntityManager.HasComponent<BuildingPropertyData>(prefab))
                        {
                            continue;
                        }

                        if (EntityManager.GetComponentData<BuildingPropertyData>(prefab).m_ResidentialProperties > 0)
                        {
                            return entity;
                        }
                    }
                }
            }
            finally
            {
                query.Dispose();
            }

            return Entity.Null;
        }

        private void OnJsDiag(string payload)
        {
            Mod.log.Info($"UI diag: {payload}");
        }

        private void OnJsError(string payload)
        {
            Mod.log.Warn($"UI JS error: {payload}");
        }

        private void InjectPanel()
        {
            try
            {
                var userInterface = GameManager.instance != null ? GameManager.instance.userInterface : null;
                if (userInterface == null || userInterface.view == null)
                {
                    return;
                }

                var uiView = userInterface.view.View;
                if (uiView == null || string.IsNullOrEmpty(m_PanelScript))
                {
                    return;
                }

                uiView.ExecuteScript(m_PanelScript);
            }
            catch (Exception e)
            {
                Mod.log.Warn($"Failed to inject UI panel script: {e.Message}");
            }
        }

        private static string LoadPanelScript()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream(kPanelScriptResource);
                if (stream == null)
                {
                    Mod.log.Warn($"UI panel script resource '{kPanelScriptResource}' not found.");
                    return "";
                }

                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (Exception e)
            {
                Mod.log.Warn($"Failed to read UI panel script: {e.Message}");
                return "";
            }
        }

        [UnityEngine.Scripting.Preserve]
        public HouseholdPanelUISystem()
        {
        }
    }
}
