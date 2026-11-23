using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace SWPT_PhotographerLighting
{
    public enum LightControlMode
    {
        AutoExposureAdaptation, // Естественное затухание с сохранением автоматической адаптации камеры
        FixedExposureOverride   // Жесткая фиксация яркости с блокировкой логарифмической адаптации EV
    }

    [BepInPlugin("com.lorifel007.letitbedark", "Let It Be Dark", "1.0.1")]
    public class Plugin : BaseUnityPlugin
    {
        public static ConfigEntry<bool> EnvironmentLightingEnabled;
        public static ConfigEntry<LightControlMode> SelectedMode;
        public static ConfigEntry<float> AmbientMultiplier;
        public static ConfigEntry<float> CharacterLightMultiplier;

        public static BepInEx.Logging.ManualLogSource StaticLogger;

        private void Awake()
        {
            StaticLogger = Logger;

            // 1. Главный выключатель контроля окружения
            EnvironmentLightingEnabled = Config.Bind(
                "1. Environment", "EnableEnvironmentControl", true,
                "Enable or disable mod control over scene ambient and volumetric lighting. If disabled, environment reverts to vanilla."
            );

            // 2. Выбор технического режима экспозиции
            SelectedMode = Config.Bind(
                "1. Environment", "DarkeningMode", LightControlMode.AutoExposureAdaptation,
                "Select HDRP Exposure processing mode: AutoExposureAdaptation (preserves camera adaptation) or FixedExposureOverride (locks EV calculation)."
            );

            // 3. Слайдер интенсивности окружения
            AmbientMultiplier = Config.Bind(
                "1. Environment", "AmbientMultiplier", 0.40f,
                new ConfigDescription("Global environment intensity multiplier (0.0 = Complete Dark, 0.8 = 100% Vanilla, 1.0 = Overdrive).", new AcceptableValueRange<float>(0f, 1f))
            );

            // 4. Слайдер интенсивности ламп персонажей (работает независимо)
            CharacterLightMultiplier = Config.Bind(
                "2. Characters", "CharacterLightMultiplier", 0.80f,
                new ConfigDescription("Intensity multiplier for built-in character lights. Always active. (0.0 = Disabled, 0.8 = 100% Vanilla).", new AcceptableValueRange<float>(0f, 1f))
            );

            // Мгновенная реакция рендерера на изменение слайдеров в меню BepInEx (F1)
            EnvironmentLightingEnabled.SettingChanged += (sender, args) => HdrpLightingManager.UpdateAllLighting();
            SelectedMode.SettingChanged += (sender, args) => HdrpLightingManager.UpdateAllLighting();
            AmbientMultiplier.SettingChanged += (sender, args) => HdrpLightingManager.UpdateAllLighting();
            CharacterLightMultiplier.SettingChanged += (sender, args) => HdrpLightingManager.UpdateAllLighting();

            StaticLogger.LogInfo("Let It Be Dark v1.0.0 (Release Build) successfully initialized!");
        }

        // Автоматический сброс графического кэша при переходе между игровыми сценами
        private void OnLevelWasLoaded(int level)
        {
            HdrpLightingManager.ResetCacheForNewScene();
        }
    }
}
