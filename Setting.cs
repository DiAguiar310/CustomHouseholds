using Colossal;
using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI;
using Game.UI.Widgets;
using System.Collections.Generic;

namespace CustomHouseholds
{
    [FileLocation(nameof(CustomHouseholds))]
    [SettingsUIGroupOrder(kGeneralGroup, kMaintenanceGroup)]
    [SettingsUIShowGroupName(kGeneralGroup, kMaintenanceGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";

        public const string kGeneralGroup = "General";
        public const string kMaintenanceGroup = "Maintenance";

        public Setting(IMod mod) : base(mod)
        {
        }

        [SettingsUISection(kSection, kGeneralGroup)]
        public bool EvictExcessImmediately { get; set; } = true;

        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(kSection, kMaintenanceGroup)]
        public bool ReloadOverrides
        {
            set
            {
                HouseholdOverrides.Load();
                Mod.log.Info("Overrides reloaded from file.");
            }
        }

        [SettingsUIButton]
        [SettingsUISection(kSection, kMaintenanceGroup)]
        public bool DumpPrefabList
        {
            set
            {
                HouseholdOverrides.DumpRequested = true;
                Mod.log.Info("Prefab list export requested.");
            }
        }

        [SettingsUIButton]
        [SettingsUIConfirmation]
        [SettingsUISection(kSection, kMaintenanceGroup)]
        public bool ClearAllOverrides
        {
            set
            {
                HouseholdOverrides.ClearAll();
            }
        }

        public override void SetDefaults()
        {
            EvictExcessImmediately = true;
        }
    }

    public class LocaleEN : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocaleEN(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Custom Households" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Main" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup), "General" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kMaintenanceGroup), "Maintenance" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EvictExcessImmediately)), "Evict excess households immediately" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EvictExcessImmediately)), "When an override reduces the household count below the current number of residents, evict the excess households right away. They will look for a new home." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ReloadOverrides)), "Reload overrides from file" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ReloadOverrides)), "Re-reads overrides.json from the mod data folder. Use this after editing the file by hand (ModsData/CustomHouseholds/overrides.json)." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ReloadOverrides)), "Reload the override list from overrides.json now?" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.DumpPrefabList)), "Export building prefab list" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.DumpPrefabList)), "Writes prefab_list.txt (name#level = current households) to the mod data folder, so you can hand-edit overrides.json." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ClearAllOverrides)), "Clear all overrides" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ClearAllOverrides)), "Removes every household override and restores the original values." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ClearAllOverrides)), "This will remove ALL overrides and restore original household counts. Continue?" },
            };
        }

        public void Unload()
        {
        }
    }

    public class LocalePT : IDictionarySource
    {
        private readonly Setting m_Setting;

        public LocalePT(Setting setting)
        {
            m_Setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(IList<IDictionaryEntryError> errors, Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                { m_Setting.GetSettingsLocaleID(), "Custom Households" },
                { m_Setting.GetOptionTabLocaleID(Setting.kSection), "Principal" },

                { m_Setting.GetOptionGroupLocaleID(Setting.kGeneralGroup), "Geral" },
                { m_Setting.GetOptionGroupLocaleID(Setting.kMaintenanceGroup), "Manutenção" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.EvictExcessImmediately)), "Despejar famílias excedentes imediatamente" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.EvictExcessImmediately)), "Quando um override reduz a quantidade de famílias abaixo do número atual de moradores, despeja o excesso na hora. Eles vão procurar uma nova casa." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ReloadOverrides)), "Recarregar overrides do arquivo" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ReloadOverrides)), "Lê novamente o overrides.json da pasta de dados do mod. Use depois de editar o arquivo à mão (ModsData/CustomHouseholds/overrides.json)." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ReloadOverrides)), "Recarregar a lista de overrides do overrides.json agora?" },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.DumpPrefabList)), "Exportar lista de prédios" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.DumpPrefabList)), "Escreve prefab_list.txt (nome#nível = famílias atuais) na pasta de dados do mod, para você editar o overrides.json à mão." },

                { m_Setting.GetOptionLabelLocaleID(nameof(Setting.ClearAllOverrides)), "Limpar todos os overrides" },
                { m_Setting.GetOptionDescLocaleID(nameof(Setting.ClearAllOverrides)), "Remove todos os overrides de famílias e restaura os valores originais." },
                { m_Setting.GetOptionWarningLocaleID(nameof(Setting.ClearAllOverrides)), "Isso vai remover TODOS os overrides e restaurar as contagens originais. Continuar?" },
            };
        }

        public void Unload()
        {
        }
    }
}
