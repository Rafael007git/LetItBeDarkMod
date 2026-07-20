using BepInEx;
using BepInEx.Configuration;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SWPT_PhotographerLighting
{
    [BepInPlugin("com.photomode.advancedlighting", "Let It Be Dark", "1.2.1")]
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

        private void Awake()
        {
            AmbientMultiplier = Config.Bind(
                "1. Environment", "AmbientMultiplier", 0.40f,
                new ConfigDescription("Глобальный свет локации (0.0 = Тьма, 0.4 = 25%, 0.8 = 100% Оригинал, 1.0 = 300% Максимум)", new AcceptableValueRange<float>(0f, 1f))
            );

            CharacterLightMultiplier = Config.Bind(
                "2. Characters", "CharacterLightMultiplier", 0.80f,
                new ConfigDescription("Встроенный свет персонажей (0.0 = Выключен, 0.4 = 25%, 0.8 = 100% Оригинал, 1.0 = 300% Максимум)", new AcceptableValueRange<float>(0f, 1f))
            );

            RefreshKey = Config.Bind(
                "3. Controls", "RefreshKey", KeyCode.F10, "Мгновенное обновление сцены без открытия меню"
            );

            // Привязываем метод без слова static — теперь делегат UnityAction его примет идеально
            SceneManager.sceneLoaded += OnSceneLoaded;

            Logger.LogInfo("Let It Be Dark v1.2.1 successfully loaded and compiled!");
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void Update()
        {
            // ЖЕСТКИЙ ФИКС: Используем UnityEngine.Input явно, чтобы избежать ошибки CS0103
            if (UnityEngine.Input.GetKeyDown(RefreshKey.Value))
            {
                Logger.LogInfo("[Let It Be Dark] Ручное обновление освещения по горячей клавише...");
                UpdateAllLighting();
            }
        }

        // ЖЕСТКИЙ ФИКС: Убрали static, чтобы метод соответствовал сигнатуре UnityAction
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "MainMenu") return;

            isOriginalAmbientSaved = false;
            originalLightIntensities.Clear();

            StaticCoroutine.Start(DelayLightingUpdate());
        }

        private static System.Collections.IEnumerator DelayLightingUpdate()
        {
            yield return new WaitForSeconds(1.5f);
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
            if (x <= 0.8f)
            {
                float t = x / 0.8f;
                return t * t;
            }
            else
            {
                float t = (x - 0.8f) / 0.2f;
                return 1.0f + t * 2.0f;
            }
        }

        public static void ApplyCustomAmbient()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

            if (!isOriginalAmbientSaved)
            {
                originalAmbientColor = (RenderSettings.ambientLight == Color.clear) ? new Color(0.2f, 0.2f, 0.2f, 1f) : RenderSettings.ambientLight;
                isOriginalAmbientSaved = true;
            }

            RenderSettings.ambientLight = originalAmbientColor * CalculatePhotoMultiplier(AmbientMultiplier.Value);
        }

        public static void ApplyCharacterLights()
        {
            var aliveLights = new Dictionary<Light, float>();
            foreach (var kvp in originalLightIntensities)
            {
                if (kvp.Key != null) aliveLights[kvp.Key] = kvp.Value;
            }
            originalLightIntensities = aliveLights;

            Light[] allLights = GameObject.FindObjectsOfType<Light>();
            float finalCharMult = CalculatePhotoMultiplier(CharacterLightMultiplier.Value);

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
                    if (!originalLightIntensities.ContainsKey(light))
                    {
                        originalLightIntensities[light] = light.intensity;
                    }

                    light.intensity = originalLightIntensities[light] * finalCharMult;
                }
            }
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
