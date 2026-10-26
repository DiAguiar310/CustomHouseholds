using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using CustomHouseholds.Systems;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace CustomHouseholds
{
    public class Mod : IMod
    {
        public static ILog log = LogManager.GetLogger($"{nameof(CustomHouseholds)}.{nameof(Mod)}").SetShowsErrorsInUI(false);

        public static Setting m_Setting;

        public void OnLoad(UpdateSystem updateSystem)
        {
            log.Info(nameof(OnLoad));

            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out var asset))
            {
                log.Info($"Current mod asset at {asset.path}");
            }

            m_Setting = new Setting(this);
            m_Setting.RegisterInOptionsUI();
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(m_Setting));
            GameManager.instance.localizationManager.AddSource("pt-BR", new LocalePT(m_Setting));

            AssetDatabase.global.LoadSettings(nameof(CustomHouseholds), m_Setting, new Setting(this));

            HouseholdOverrides.Load();

            updateSystem.UpdateAfter<HouseholdOverrideSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<HouseholdPanelUISystem>(SystemUpdatePhase.UIUpdate);
        }

        public void OnDispose()
        {
            log.Info(nameof(OnDispose));
            if (m_Setting != null)
            {
                m_Setting.UnregisterInOptionsUI();
                m_Setting = null;
            }
        }
    }
}
