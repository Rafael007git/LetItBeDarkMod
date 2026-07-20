using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace SWPT_PhotographerLighting
{
    public enum LightControlMode
    {
        NaturalWithAutoExposure, // Естественный (с ночным зрением)
        AbsoluteDarkFixed        // Абсолютная тьма (фиксированный)
    }

    [BepInPlugin("com.lorifel007.letitbedark", "Let It Be Dark", "1.0.0")]
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

            // 1. Главный выключатель для фонового света
            EnvironmentLightingEnabled = Config.Bind(
                "1. Environment", "EnableEnvironmentControl", true,
                "Включить управление фоновым светом локации (если выключено, фон вернется к оригиналу)"
            );

            // 2. Выбор режима затемнения
            SelectedMode = Config.Bind(
                "1. Environment", "DarkeningMode", LightControlMode.NaturalWithAutoExposure,
                "Режим работы: Natural (мягкий, с адаптацией глаза) или Absolute (жесткая фиксация тьмы)"
            );

            AmbientMultiplier = Config.Bind(
                "1. Environment", "AmbientMultiplier", 0.40f,
                new ConfigDescription("Яркость фона (0.0 = Макс. Темнота, 0.8 = 100% Оригинал, 1.0 = Овердрайв)", new AcceptableValueRange<float>(0f, 1f))
            );

            // Скелетные огни персонажей
            CharacterLightMultiplier = Config.Bind(
                "2. Characters", "CharacterLightMultiplier", 0.80f,
                new ConfigDescription("Встроенный свет персонажей (Работает ВСЕГДА. 0.0 = Выключен, 0.8 = 100% Оригинал)", new AcceptableValueRange<float>(0f, 1f))
            );

            // МГНОВЕННАЯ СВЯЗЬ: Обновляем свет строго в момент движения ползунков в меню F1
            EnvironmentLightingEnabled.SettingChanged += (sender, args) => HdrpLightingManager.UpdateAllLighting();
            SelectedMode.SettingChanged += (sender, args) => HdrpLightingManager.UpdateAllLighting();
            AmbientMultiplier.SettingChanged += (sender, args) => HdrpLightingManager.UpdateAllLighting();
            CharacterLightMultiplier.SettingChanged += (sender, args) => HdrpLightingManager.UpdateAllLighting();

            StaticLogger.LogInfo("Let It Be Dark v1.3.1 (Event-Driven Clean Build) successfully initialized!");
        }

        // Автоматический сброс кэша и перерасчет света при переходе на новую карту/сцену
        private void OnLevelWasLoaded(int level)
        {
            HdrpLightingManager.ResetCacheForNewScene();
        }
    }
}
