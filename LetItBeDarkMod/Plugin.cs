using BepInEx;
using BepInEx.Configuration;
using System.Collections.Generic;
using UnityEngine;

namespace SWPT_PhotographerLighting
{
    [BepInPlugin("com.photomode.advancedlighting", "Let It Be Dark", "0.0.1")]
    public class Plugin : BaseUnityPlugin
    {
        public static ConfigEntry<float> AmbientMultiplier;
        public static ConfigEntry<float> CharacterLightMultiplier;
        public static ConfigEntry<KeyCode> RefreshKey;

        private static Color originalAmbientColor = Color.clear;
        private static bool isOriginalAmbientSaved = false;

        private static Dictionary<Light, float> originalLightIntensities = new Dictionary<Light, float>();

        // Локальный автономный справочник костей и ламп
        private static readonly HashSet<string> CharacterLightRegistry = new HashSet<string>
        {
            "Player", "Kira", "Genesis8Female", "hip", "pelvis", "head",
            "Point Light (1)", "Point Light (5)", "Point Light (9)"
        };

        // Статический источник логов для безопасного вызова из любых методов
        private static BepInEx.Logging.ManualLogSource StaticLogger;

        private void Awake()
        {
            StaticLogger = Logger;

            AmbientMultiplier = Config.Bind(
                "1. Environment", "AmbientMultiplier", 0.40f,
                new ConfigDescription("Глобальный свет локации", new AcceptableValueRange<float>(0f, 1f))
            );

            CharacterLightMultiplier = Config.Bind(
                "2. Characters", "CharacterLightMultiplier", 0.80f,
                new ConfigDescription("Встроенный свет персонажей", new AcceptableValueRange<float>(0f, 1f))
            );

            RefreshKey = Config.Bind(
                "3. Controls", "RefreshKey", KeyCode.F10, "Мгновенное обновление сцены"
            );

            // ЖЕЛЕЗНЫЙ ФИКС: Строгое привязывание статического делегата с полными пространствами имен Unity
            UnityEngine.SceneManagement.SceneManager.sceneLoaded += OnSceneLoaded;

            StaticLogger.LogInfo("Let It Be Dark v1.2.3 successfully loaded and patched!");
        }

        private void OnDestroy()
        {
            UnityEngine.SceneManagement.SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(RefreshKey.Value))
            {
                StaticLogger.LogInfo("[Let It Be Dark] Нажата кнопка F10. Запуск диагностики...");
                UpdateAllLighting();
            }
        }

        // ЖЕЛЕЗНЫЙ ФИКС SIGNATURE MATCHING: Метод статический, а типы прописаны абсолютно строго
        private static void OnSceneLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode)
        {
            if (StaticLogger != null) StaticLogger.LogInfo($"[Let It Be Dark] Сцена загружена: '{scene.name}'. Сбрасываем кэш света.");
            if (scene.name == "MainMenu") return;

            isOriginalAmbientSaved = false;
            originalLightIntensities.Clear();

            StaticCoroutine.Start(DelayLightingUpdate());
        }

        private static System.Collections.IEnumerator DelayLightingUpdate()
        {
            yield return new WaitForSeconds(2.0f); // Задержка 2 сек, пока сцена инициализирует графику
            if (StaticLogger != null) StaticLogger.LogInfo("[Let It Be Dark] Автоматическое обновление после загрузки сцены...");
            UpdateAllLighting();
        }

        public static void UpdateAllLighting()
        {
            ApplyCustomAmbient();
            ApplyCharacterLights();
        }

        private static float CalculatePhotoMultiplier(float configValue)
        {
            float x = Mathf.Clamp01(configValue);
            if (x <= 0.8f) return (x / 0.8f) * (x / 0.8f);
            return 1.0f + ((x - 0.8f) / 0.2f) * 2.0f;
        }

        public static void ApplyCustomAmbient()
        {
            var logger = BepInEx.Logging.Logger.CreateLogSource("Dark_Ambient");

            // Переключаем режим в плоский цвет
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

            if (!isOriginalAmbientSaved)
            {
                originalAmbientColor = (RenderSettings.ambientLight == Color.clear) ? new Color(0.2f, 0.2f, 0.2f, 1f) : RenderSettings.ambientLight;
                isOriginalAmbientSaved = true;
                logger.LogInfo($"Закеширован оригинальный AmbientColor: {originalAmbientColor}");
            }

            float mult = CalculatePhotoMultiplier(AmbientMultiplier.Value);
            RenderSettings.ambientLight = originalAmbientColor * mult;

            // --- НАШЕ РЕШЕНИЕ ДЛЯ РЕЖИМА SKYBOX ---
            // Намертво душим интенсивность свечения неба. 
            // Если мультипликатор равен 0 (тьма), интенсивность неба станет 0, и сцена железно погрузится во мрак!
            RenderSettings.ambientIntensity = mult;

            // На всякий случай также приглушаем глобальные отражения неба, чтобы не было "стеклянного" блеска в темноте
            RenderSettings.reflectionIntensity = mult;

            logger.LogInfo($"Установлен новый цвет ambientLight: {RenderSettings.ambientLight} | Интенсивность неба: {RenderSettings.ambientIntensity}");
        }


        public static void ApplyCharacterLights()
        {
            var logger = BepInEx.Logging.Logger.CreateLogSource("Dark_Lights");

            Light[] allLights = GameObject.FindObjectsOfType<Light>();
            logger.LogInfo($"Всего физических источников света на сцене найдено: {allLights.Length}");

            float finalCharMult = CalculatePhotoMultiplier(CharacterLightMultiplier.Value);
            int matchedLightsCount = 0;

            foreach (Light light in allLights)
            {
                if (light == null) continue;

                string lightName = light.gameObject.name;
                bool isCharacterLight = CharacterLightRegistry.Contains(lightName);

                if (!isCharacterLight && light.transform.parent != null)
                {
                    string parentName = light.transform.parent.name;
                    if (CharacterLightRegistry.Contains(parentName) || parentName.ToLower().Contains("kira") || parentName.ToLower().Contains("player"))
                    {
                        isCharacterLight = true;
                    }
                }

                if (isCharacterLight)
                {
                    matchedLightsCount++;
                    if (!originalLightIntensities.ContainsKey(light))
                    {
                        originalLightIntensities[light] = light.intensity;
                    }

                    light.intensity = originalLightIntensities[light] * finalCharMult;
                    logger.LogInfo($"Изменена лампа персонажа: '{lightName}' | Новая Интенсивность: {light.intensity} (Было: {originalLightIntensities[light]})");
                }
            }

            logger.LogInfo($"Изменено ламп персонажей под конфиг: {matchedLightsCount}");
        }
    }

    public class StaticCoroutine : MonoBehaviour
    {
        private static StaticCoroutine instance;
        public static void Start(System.Collections.IEnumerator routine)
        {
            if (instance == null)
            {
                GameObject obj = new GameObject("StaticCoroutineRunner");
                instance = obj.AddComponent<StaticCoroutine>();
                DontDestroyOnLoad(obj);
            }
            instance.StartCoroutine(routine);
        }
    }
}
