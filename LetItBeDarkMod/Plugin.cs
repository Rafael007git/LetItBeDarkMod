using BepInEx;
using BepInEx.Configuration;
using System.Collections.Generic;
using UnityEngine;

namespace SWPT_PhotographerLighting
{
    [BepInPlugin("com.photomode.advancedlighting", "Let It Be Dark", "1.2.5")]
    public class Plugin : BaseUnityPlugin
    {
        public static ConfigEntry<float> AmbientMultiplier;
        public static ConfigEntry<float> CharacterLightMultiplier;
        public static ConfigEntry<KeyCode> RefreshKey;

        private static Color originalAmbientColor = Color.clear;
        private static bool isOriginalAmbientSaved = false;

        private static Dictionary<Light, float> originalLightIntensities = new Dictionary<Light, float>();
        private static Dictionary<Component, float> originalVolumeWeights = new Dictionary<Component, float>();

        private static readonly HashSet<string> CharacterLightRegistry = new HashSet<string>
        {
            "Player", "Kira", "Genesis8Female", "hip", "pelvis", "head",
            "Point Light (1)", "Point Light (5)", "Point Light (9)"
        };

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

            // Мгновенное обновление при движении ползунков в меню F1
            AmbientMultiplier.SettingChanged += (sender, args) => UpdateAllLighting();
            CharacterLightMultiplier.SettingChanged += (sender, args) => UpdateAllLighting();

            StaticLogger.LogInfo("Let It Be Dark v1.2.5 successfully loaded!");
        }

        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(RefreshKey.Value))
            {
                StaticLogger.LogInfo("[Let It Be Dark] Ручное обновление по F10...");
                UpdateAllLighting();
            }
        }

        // НАДЕЖНЫЙ И ЧИСТЫЙ МЕТОД: Вызывается Unity автоматически при загрузке новой карты.
        // Больше никаких ошибок компиляции с делегатами SceneManager!
        private void OnLevelWasLoaded(int level)
        {
            if (StaticLogger != null) StaticLogger.LogInfo($"[Let It Be Dark] Новая сцена загружена (Индекс: {level}). Сбрасываем кэш света.");

            isOriginalAmbientSaved = false;
            originalLightIntensities.Clear();
            originalVolumeWeights.Clear();

            StaticCoroutine.Start(DelayLightingUpdate());
        }

        private static System.Collections.IEnumerator DelayLightingUpdate()
        {
            yield return new WaitForSeconds(2.0f);
            UpdateAllLighting();
        }

        public static void UpdateAllLighting()
        {
            ApplyCustomAmbient();
            ApplyCharacterLights();
            ApplyVolumePostProcessing();
        }

        private static float CalculatePhotoMultiplier(float configValue)
        {
            float x = Mathf.Clamp01(configValue);
            if (x <= 0.8f) return (x / 0.8f) * (x / 0.8f);
            return 1.0f + ((x - 0.8f) / 0.2f) * 2.0f;
        }

        public static void ApplyCustomAmbient()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

            if (!isOriginalAmbientSaved)
            {
                originalAmbientColor = (RenderSettings.ambientLight == Color.clear) ? new Color(0.2f, 0.2f, 0.2f, 1f) : RenderSettings.ambientLight;
                isOriginalAmbientSaved = true;
            }

            float mult = CalculatePhotoMultiplier(AmbientMultiplier.Value);
            RenderSettings.ambientLight = originalAmbientColor * mult;
            RenderSettings.ambientIntensity = mult;
            RenderSettings.reflectionIntensity = mult;
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

        public static void ApplyVolumePostProcessing()
        {
            float mult = CalculatePhotoMultiplier(AmbientMultiplier.Value);

            var aliveVolumes = new Dictionary<Component, float>();
            foreach (var kvp in originalVolumeWeights)
            {
                if (kvp.Key != null) aliveVolumes[kvp.Key] = kvp.Value;
            }
            originalVolumeWeights = aliveVolumes;

            Component[] allComponents = GameObject.FindObjectsOfType<Component>();
            int volumeCount = 0;

            foreach (var comp in allComponents)
            {
                if (comp == null) continue;

                string typeName = comp.GetType().Name;

                if (typeName == "Volume")
                {
                    volumeCount++;
                    var weightProperty = comp.GetType().GetProperty("weight");
                    if (weightProperty != null)
                    {
                        if (!originalVolumeWeights.ContainsKey(comp))
                        {
                            originalVolumeWeights[comp] = (float)weightProperty.GetValue(comp);
                        }

                        float newWeight = originalVolumeWeights[comp] * mult;
                        weightProperty.SetValue(comp, newWeight);
                    }
                }
            }

            if (volumeCount > 0)
            {
                DynamicGI.UpdateEnvironment();
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
