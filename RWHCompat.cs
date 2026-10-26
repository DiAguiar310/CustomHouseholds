using System;
using Unity.Entities;

namespace CustomHouseholds
{
    public static class RWHCompat
    {
        private const string HouseholdTagTypeName = "RealisticWorkplacesAndHouseholds.Components.ABCHouseholdOverride";

        private static bool s_NotResolvedLogged;
        private static ComponentType s_HouseholdTag;

        public static bool HasTagComponent { get; private set; }

        public static void EnsureResolved()
        {
            if (HasTagComponent)
            {
                return;
            }

            try
            {
                Type tagType = ResolveType(HouseholdTagTypeName);
                if (tagType == null)
                {
                    if (!s_NotResolvedLogged)
                    {
                        s_NotResolvedLogged = true;
                        Mod.log.Info("Realistic Workplaces and Households not detected (yet); will keep checking. Overrides are applied directly meanwhile.");
                    }

                    return;
                }

                TypeIndex index = TypeManager.GetTypeIndex(tagType);
                if (index == TypeIndex.Null)
                {
                    if (!s_NotResolvedLogged)
                    {
                        s_NotResolvedLogged = true;
                        Mod.log.Info("RWH detected; waiting for its ABCHouseholdOverride component type to register before tagging overrides.");
                    }

                    return;
                }

                s_HouseholdTag = ComponentType.FromTypeIndex(index);
                HasTagComponent = true;
                Mod.log.Info("RWH detected; overridden prefabs will be tagged with ABCHouseholdOverride so RWH leaves them alone.");
            }
            catch (Exception e)
            {
                if (!s_NotResolvedLogged)
                {
                    s_NotResolvedLogged = true;
                    Mod.log.Warn($"Failed to resolve RWH compat tag: {e.Message}");
                }
            }
        }

        public static void EnsureTag(EntityManager entityManager, Entity prefab)
        {
            if (!HasTagComponent)
            {
                return;
            }

            if (!entityManager.HasComponent(prefab, s_HouseholdTag))
            {
                entityManager.AddComponent(prefab, s_HouseholdTag);
            }
        }

        public static void RemoveTag(EntityManager entityManager, Entity prefab)
        {
            if (!HasTagComponent)
            {
                return;
            }

            if (entityManager.HasComponent(prefab, s_HouseholdTag))
            {
                entityManager.RemoveComponent(prefab, s_HouseholdTag);
            }
        }

        private static Type ResolveType(string fullName)
        {
            Type type = Type.GetType(fullName + ", RWH", false);
            if (type != null)
            {
                return type;
            }

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(fullName, false);
                }
                catch
                {
                    type = null;
                }

                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
