using Game.Agents;
using Game.Buildings;
using Game.Companies;
using Game.Prefabs;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;

namespace CustomHouseholds.Systems
{
    public struct EvictExcessJob : IJobChunk
    {
        public EntityCommandBuffer ecb;

        public EntityTypeHandle entityTypeHandle;

        public BufferTypeHandle<Renter> renterTypeHandle;

        [ReadOnly]
        public ComponentTypeHandle<PrefabRef> prefabRefTypeHandle;

        [ReadOnly]
        public ComponentLookup<BuildingPropertyData> propertyDataLookup;

        [ReadOnly]
        public ComponentLookup<CommercialProperty> commercialPropertyLookup;

        [ReadOnly]
        public ComponentLookup<WorkProvider> workProviderLookup;

        [ReadOnly]
        public ComponentLookup<PropertyOnMarket> propertyOnMarketLookup;

        [ReadOnly]
        public NativeParallelHashSet<Entity> overriddenPrefabs;

        public EntityArchetype rentEventArchetype;

        public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
        {
            var entities = chunk.GetNativeArray(entityTypeHandle);
            var rentersAccessor = chunk.GetBufferAccessor(ref renterTypeHandle);
            var prefabRefs = chunk.GetNativeArray(ref prefabRefTypeHandle);

            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                var prefab = prefabRefs[i].m_Prefab;

                if (!overriddenPrefabs.Contains(prefab))
                {
                    continue;
                }

                if (!propertyDataLookup.TryGetComponent(prefab, out var propertyData))
                {
                    continue;
                }

                var renters = rentersAccessor[i];
                int count = commercialPropertyLookup.HasComponent(entity) ? renters.Length - 1 : renters.Length;

                if (count > propertyData.m_ResidentialProperties)
                {
                    Evict(count - propertyData.m_ResidentialProperties, entity, renters);
                    var rentersUpdated = ecb.CreateEntity(rentEventArchetype);
                    ecb.SetComponent(rentersUpdated, new RentersUpdated(entity));
                }
                else if (count < propertyData.m_ResidentialProperties && propertyOnMarketLookup.HasComponent(entity))
                {
                    var rentersUpdated = ecb.CreateEntity(rentEventArchetype);
                    ecb.SetComponent(rentersUpdated, new RentersUpdated(entity));
                }
            }
        }

        private void Evict(int extraHouseholds, Entity property, DynamicBuffer<Renter> renters)
        {
            for (int i = 0; i < extraHouseholds && i < renters.Length; i++)
            {
                var renter = renters[i].m_Renter;

                if (workProviderLookup.HasComponent(renter))
                {
                    continue;
                }

                ecb.AddComponent(renter, new PropertySeeker
                {
                    m_BestProperty = Entity.Null,
                    m_BestPropertyScore = float.NegativeInfinity,
                });
                ecb.RemoveComponent<PropertyRenter>(renter);
            }
        }
    }
}
