using System.Collections.Generic;
using UnityEngine;

namespace SWPT_PhotographerLighting
{
    public static class HdrpLightingManager
    {
        private static Color originalAmbientColor = Color.clear;
        private static bool isOriginalAmbientSaved = false;

        private static Dictionary<Light, float> originalLightIntensities = new Dictionary<Light, float>();
        private static Dictionary<Component, float> originalVolumeWeights = new Dictionary<Component, float>();
        private static Dictionary<string, float> originalProfileSettings = new Dictionary<string, float>();

        // Наш локальный автономный справочник ламп Диорамы и костей персонажей
        private static readonly HashSet<string> CharacterLightRegistry = new HashSet<string>
        {
            "Player", "Kira", "Genesis8Female", "hip", "pelvis", "head",
            "Point Light (1)", "Point Light (5)", "Point Light (9)"
        };

        // Полный сброс кэша интенсивностей при загрузке новой сцены
        public static void ResetCacheForNewScene()
        {
            isOriginalAmbientSaved = false;
            originalLightIntensities.Clear();
            originalVolumeWeights.Clear();
            originalProfileSettings.Clear();

            // Даем игре 2.0 секунды на прогрузку геометрии и запускаем автоматическое затемнение
            StaticCoroutine.Start(DelayLightingUpdate());
        }

        private static System.Collections.IEnumerator DelayLightingUpdate()
        {
            yield return new WaitForSeconds(2.0f);
            if (Plugin.StaticLogger != null) Plugin.StaticLogger.LogInfo("[Let It Be Dark] Автоматическое применение темноты на новой сцене...");
            UpdateAllLighting();
        }

        // Главный триггер перерасчета всей графики
        public static void UpdateAllLighting()
        {
            ApplyCustomAmbient();
            ApplyCharacterLights();
            ApplyHdrpVolumePostProcessing();
        }

        // Математическая функция плавного нелинейного ухода во тьму (до 0.8) и линейного разгона (после 0.8)
        private static float CalculatePhotoMultiplier(float configValue)
        {
            float x = Mathf.Clamp01(configValue);
            if (x <= 0.8f)
            {
                float t = x / 0.8f;
                return t * t; // Квадратичное затухание для глубоких теней
            }
            else
            {
                float t = (x - 0.8f) / 0.2f;
                return 1.0f + t * 2.0f; // Линейный уход в овердрайв до 300% яркости
            }
        }

        // Стандартный амбиент Unity (на случай, если игра частично использует его)
        private static void ApplyCustomAmbient()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;

            if (!isOriginalAmbientSaved)
            {
                originalAmbientColor = (RenderSettings.ambientLight == Color.clear) ? new Color(0.2f, 0.2f, 0.2f, 1f) : RenderSettings.ambientLight;
                isOriginalAmbientSaved = true;
            }

            float mult = CalculatePhotoMultiplier(Plugin.AmbientMultiplier.Value);
            RenderSettings.ambientLight = originalAmbientColor * mult;
            RenderSettings.ambientIntensity = mult;
            RenderSettings.reflectionIntensity = mult;
        }

        // Точечный гаситель ламп подсветки персонажей по справочнику
        private static void ApplyCharacterLights()
        {
            // Чистим кэш от уничтоженных ламп при смене сцены
            var aliveLights = new Dictionary<Light, float>();
            foreach (var kvp in originalLightIntensities)
            {
                if (kvp.Key != null) aliveLights[kvp.Key] = kvp.Value;
            }
            originalLightIntensities = aliveLights;

            Light[] allLights = GameObject.FindObjectsOfType<Light>();
            float finalCharMult = CalculatePhotoMultiplier(Plugin.CharacterLightMultiplier.Value);

            foreach (Light light in allLights)
            {
                if (light == null) continue;

                string lightName = light.gameObject.name;
                bool isCharacterLight = CharacterLightRegistry.Contains(lightName);

                // Подстраховка по родительским корневым узлам
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

        // Ультимативный взломщик HDRP-эффектов (Физическое небо, Скайбоксы, Экспозиция, Туман)
        private static void ApplyHdrpVolumePostProcessing()
        {
            var logger = BepInEx.Logging.Logger.CreateLogSource("Dark_HDRP_Killer");
            float mult = CalculatePhotoMultiplier(Plugin.AmbientMultiplier.Value);

            // Чистим кэш Volume-компонентов
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

                if (comp.GetType().Name == "Volume")
                {
                    volumeCount++;

                    // 1. Скручиваем базовый вес влияния данного объема на сцену
                    var weightProperty = comp.GetType().GetProperty("weight");
                    if (weightProperty != null)
                    {
                        if (!originalVolumeWeights.ContainsKey(comp))
                        {
                            originalVolumeWeights[comp] = (float)weightProperty.GetValue(comp);
                        }
                        weightProperty.SetValue(comp, originalVolumeWeights[comp] * mult);
                    }

                    // 2. Пробиваемся внутрь профиля Volume (обход защиты HDRP через рефлексию)
                    var profileProperty = comp.GetType().GetProperty("profile");
                    if (profileProperty != null)
                    {
                        object profileObj = profileProperty.GetValue(comp);
                        if (profileObj != null)
                        {
                            // Извлекаем приватный массив эффектов 'components'
                            var componentsField = profileObj.GetType().GetField("components", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (componentsField != null)
                            {
                                var componentList = componentsField.GetValue(profileObj) as System.Collections.IEnumerable;
                                if (componentList != null)
                                {
                                    foreach (object effect in componentList)
                                    {
                                        if (effect == null) continue;
                                        string effectName = effect.GetType().Name;

                                        // Проверяем, отвечает ли этот HDRP-эффект за фоновую заливку светом
                                        bool isHdrpLightingEffect = effectName.Contains("Sky") ||
                                                                    effectName.Contains("Exposure") ||
                                                                    effectName.Contains("Fog") ||
                                                                    effectName.Contains("IndirectLightingController") ||
                                                                    effectName.Contains("AmbientOcclusion");

                                        if (isHdrpLightingEffect)
                                        {
                                            // В HDRP интенсивность неба и тумана хранится в полях 'multiplier' или 'intensity'
                                            var multiplierField = effect.GetType().GetField("multiplier");
                                            if (multiplierField == null) multiplierField = effect.GetType().GetField("intensity");

                                            if (multiplierField != null)
                                            {
                                                object paramObj = multiplierField.GetValue(effect);
                                                if (paramObj != null)
                                                {
                                                    // Все параметры HDRP обернуты в класс VolumeParameter, вытаскиваем свойство 'value'
                                                    var valueProp = paramObj.GetType().GetProperty("value");
                                                    if (valueProp != null)
                                                    {
                                                        string cacheKey = comp.GetInstanceID() + "_" + effectName;
                                                        if (!originalProfileSettings.ContainsKey(cacheKey))
                                                        {
                                                            originalProfileSettings[cacheKey] = (float)valueProp.GetValue(paramObj);
                                                        }

                                                        // Физически душим яркость неба/экспозиции HDRP под наш слайдер!
                                                        float newValue = originalProfileSettings[cacheKey] * mult;
                                                        valueProp.SetValue(paramObj, newValue);
                                                        logger.LogInfo($"[HDRP] Компонент {effectName}: яркость успешно скручена до {newValue}");
                                                    }
                                                }
                                            }

                                            // Если слайдер убран в абсолютный ноль, принудительно отключаем эффект
                                            var activeField = effect.GetType().GetField("active");
                                            if (activeField != null)
                                            {
                                                activeField.SetValue(effect, mult > 0.01f);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }

            if (volumeCount > 0)
            {
                // Заставляем графический движок Unity HDRP принудительно пересчитать освещение локации
                DynamicGI.UpdateEnvironment();
            }
        }
    }
}
