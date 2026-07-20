using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;               // Базовое ядро эффектов Unity
using UnityEngine.Rendering.HighDefinition; // Полный доступ к классам HDRP!

namespace SWPT_PhotographerLighting
{
    public static class HdrpLightingManager
    {
        private static Color originalAmbientColor = Color.clear;
        private static bool isOriginalAmbientSaved = false;

        private static Dictionary<Light, float> originalLightIntensities = new Dictionary<Light, float>();
        private static Dictionary<Volume, float> originalVolumeWeights = new Dictionary<Volume, float>();
        private static Dictionary<string, float> originalProfileSettings = new Dictionary<string, float>();

        // Локальный автономный справочник ламп Диорамы и костей персонажей
        private static readonly HashSet<string> CharacterLightRegistry = new HashSet<string>
        {
            "Player", "Kira", "Genesis8Female", "hip", "pelvis", "head",
            "Point Light (1)", "Point Light (5)", "Point Light (9)"
        };

        // Сброс кэша при загрузке новой локации
        public static void ResetCacheForNewScene()
        {
            isOriginalAmbientSaved = false;
            originalLightIntensities.Clear();
            originalVolumeWeights.Clear();
            originalProfileSettings.Clear();

            // Даем сцене 2 секунды на инициализацию и запускаем автоматическое затемнение
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

        // Функция плавного нелинейного ухода во тьму (до 0.8) и линейного разгона (после 0.8)
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

        // Точечный гаситель ламп подсветки персонажей по справочнику Диорамы
        private static void ApplyCharacterLights()
        {
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

        // Чистый и ультимативный взломщик HDRP-эффектов через прямое подключение .dll
        private static void ApplyHdrpVolumePostProcessing()
        {
            var logger = BepInEx.Logging.Logger.CreateLogSource("Dark_HDRP_Killer");
            float mult = CalculatePhotoMultiplier(Plugin.AmbientMultiplier.Value);

            // Чистим кэш Volume-компонентов от уничтоженных при смене сцены объектов
            var aliveVolumes = new Dictionary<Volume, float>();
            foreach (var kvp in originalVolumeWeights)
            {
                if (kvp.Key != null) aliveVolumes[kvp.Key] = kvp.Value;
            }
            originalVolumeWeights = aliveVolumes;

            // Находим строго все Volume на сцене (работает моментально)
            Volume[] allVolumes = GameObject.FindObjectsOfType<Volume>();

            foreach (Volume volume in allVolumes)
            {
                if (volume == null || volume.profile == null) continue;

                // 1. Управляем общим весом влияния Volume на сцену
                if (!originalVolumeWeights.ContainsKey(volume))
                {
                    originalVolumeWeights[volume] = volume.weight;
                }
                volume.weight = originalVolumeWeights[volume] * mult;

                // 2. ЧИСТЫЙ ПЕРЕХВАТ ЭФФЕКТОВ HDRP БЕЗ РЕФЛЕКСИИ

                // БЛОКИРУЕМ АВТОЭКСПОЗИЦИЮ ("НОЧНОЕ ЗРЕНИЕ")
                if (volume.profile.TryGet<Exposure>(out var exposure))
                {
                    string cacheKeyMode = volume.GetInstanceID() + "_ExpMode";
                    if (!originalProfileSettings.ContainsKey(cacheKeyMode))
                    {
                        // Сохраняем оригинальный режим (enum ExposureMode)
                        originalProfileSettings[cacheKeyMode] = (float)exposure.mode.value;
                    }

                    // Если ползунок темноты опущен ниже 60%, замораживаем чувствительность камеры
                    if (mult < 0.6f)
                    {
                        exposure.mode.value = ExposureMode.Fixed; // Фиксируем экспозицию! Камера ослепнет и перестанет высветлять серым
                        logger.LogInfo($"[HDRP] Экспозиция на {volume.gameObject.name} принудительно зафиксирована.");
                    }
                    else
                    {
                        exposure.mode.value = (ExposureMode)originalProfileSettings[cacheKeyMode]; // Возврат к ванилле игры
                    }

                    // Плавно приглушаем фиксированное значение яркости, если оно используется
                    string cacheKeyFixed = volume.GetInstanceID() + "_ExpFixed";
                    if (!originalProfileSettings.ContainsKey(cacheKeyFixed))
                    {
                        originalProfileSettings[cacheKeyFixed] = exposure.fixedExposure.value;
                    }
                    exposure.fixedExposure.value = originalProfileSettings[cacheKeyFixed] * mult;
                }

                // ГАСИМ ФОНОВОЕ НЕБО (ГРАДИЕНТ)
                if (volume.profile.TryGet<GradientSky>(out var gradientSky))
                {
                    string cacheKey = volume.GetInstanceID() + "_GradientSky";
                    if (!originalProfileSettings.ContainsKey(cacheKey))
                    {
                        originalProfileSettings[cacheKey] = gradientSky.multiplier.value;
                    }

                    gradientSky.multiplier.value = originalProfileSettings[cacheKey] * mult;
                    gradientSky.active = mult > 0.01f; // Полностью выключаем при нуле
                    logger.LogInfo($"[HDRP] Небо GradientSky на {volume.gameObject.name} скручено до {gradientSky.multiplier.value}");
                }

                // ГАСИМ НЕПРЯМОЕ (ФОНОВОЕ) ОСВЕЩЕНИЕ ЛОКАЦИИ
                if (volume.profile.TryGet<IndirectLightingController>(out var indirectLight))
                {
                    string cacheKeyDiffuse = volume.GetInstanceID() + "_IndirectDiff";
                    if (!originalProfileSettings.ContainsKey(cacheKeyDiffuse))
                    {
                        originalProfileSettings[cacheKeyDiffuse] = indirectLight.indirectDiffuseIntensity.value;
                    }

                    indirectLight.indirectDiffuseIntensity.value = originalProfileSettings[cacheKeyDiffuse] * mult;
                    indirectLight.active = mult > 0.01f;
                    logger.LogInfo($"[HDRP] Фоновое освещение IndirectLightingController скручено до {indirectLight.indirectDiffuseIntensity.value}");
                }

                // ГАСИМ ГЛОБАЛЬНЫЙ ТУМАН HDRP
                if (volume.profile.TryGet<Fog>(out var fog))
                {
                    fog.active = mult > 0.01f;
                    // Просто отключаем туман в полной темноте, чтобы он не светился серым
                }
            }
            // Мгновенно заставляем HDRP-рендерер пересчитать все источники и карты освещения на сцене
            DynamicGI.UpdateEnvironment();
        }
    }
}
