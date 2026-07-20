using BepInEx;
using BepInEx.Configuration;
using UnityEngine;

namespace SWPT_PhotographerLighting
{
    [BepInPlugin("com.photomode.advancedlighting", "Let It Be Dark", "1.2.7")]
    public class Plugin : BaseUnityPlugin
    {
        public static ConfigEntry<float> AmbientMultiplier;
        public static ConfigEntry<float> CharacterLightMultiplier;
        public static ConfigEntry<KeyCode> RefreshKey;

        public static BepInEx.Logging.ManualLogSource StaticLogger;

        private void Awake()
        {
            StaticLogger = Logger;

            AmbientMultiplier = Config.Bind(
                "1. Environment", "AmbientMultiplier", 0.40f,
                new ConfigDescription("Глобальный свет локации (0.0 = Тьма, 0.8 = 100% Оригинал, 1.0 = Овердрайв)", new AcceptableValueRange<float>(0f, 1f))
            );

            CharacterLightMultiplier = Config.Bind(
                "2. Characters", "CharacterLightMultiplier", 0.80f,
                new ConfigDescription("Встроенный свет персонажей (0.0 = Тьма, 0.8 = 100% Оригинал, 1.0 = Овердрайв)", new AcceptableValueRange<float>(0f, 1f))
            );

            RefreshKey = Config.Bind(
                "3. Controls", "RefreshKey", KeyCode.F10, "Мгновенное обновление сцены без открытия меню"
            );

            // Живые слайдеры: мгновенно обновляем свет при движении ползунков в меню BepInEx Configuration Manager (F1)
            AmbientMultiplier.SettingChanged += (sender, args) => HdrpLightingManager.UpdateAllLighting();
            CharacterLightMultiplier.SettingChanged += (sender, args) => HdrpLightingManager.UpdateAllLighting();

            StaticLogger.LogInfo("Let It Be Dark v1.2.7 (HDRP Split Version) successfully initialized!");
        }

        private void Update()
        {
            // Считываем горячую клавишу для принудительного обновления графики
            if (UnityEngine.Input.GetKeyDown(RefreshKey.Value))
            {
                StaticLogger.LogInfo("[Let It Be Dark] Ручное обновление освещения по горячей клавише F10...");
                HdrpLightingManager.UpdateAllLighting();
            }
        }

        // Автоматический сброс кэша и перерасчет света при переходе на новую карту/сцену
        private void OnLevelWasLoaded(int level)
        {
            if (StaticLogger != null) StaticLogger.LogInfo($"[Let It Be Dark] Обнаружена смена локации (Индекс сцены: {level}). Очищаем кэш.");
            HdrpLightingManager.ResetCacheForNewScene();
        }
    }
}
