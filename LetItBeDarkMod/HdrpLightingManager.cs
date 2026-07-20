using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

namespace SWPT_PhotographerLighting
{
    public static class HdrpLightingManager
    {
        private static Color originalAmbientColor = Color.clear;
        private static bool isOriginalAmbientSaved = false;

        private static Dictionary<Light, float> originalLightIntensities = new Dictionary<Light, float>();
        private static Dictionary<Volume, float> originalVolumeWeights = new Dictionary<Volume, float>();
        private static Dictionary<string, float> originalProfileSettings = new Dictionary<string, float>();
        private static Dictionary<string, object> originalEnumSettings = new Dictionary<string, object>();

        private static readonly HashSet<string> CharacterLightRegistry = new HashSet<string>
        {
            "Player", "Kira", "Genesis8Female", "hip", "pelvis", "head",
            "Point Light (1)", "Point Light (5)", "Point Light (9)"
        };

        public static void ResetCacheForNewScene()
        {
            isOriginalAmbientSaved = false;
            originalLightIntensities.Clear();
            originalVolumeWeights.Clear();
            originalProfileSettings.Clear();
            originalEnumSettings.Clear();

            StaticCoroutine.Start(DelayLightingUpdate());
        }

        private static System.Collections.IEnumerator DelayLightingUpdate()
        {
            yield return new WaitForSeconds(2.0f);
            UpdateAllLighting();
        }

        public static void UpdateAllLighting()
        {
            // Скелетный свет персонажей работает ВСЕГДА, независимо от флага окружения
            ApplyCharacterLights();

            // Фоновое освещение подчиняется флагу Enabled
            if (Plugin.EnvironmentLightingEnabled.Value)
            {
                ApplyCustomAmbient();
                ApplyHdrpVolumePostProcessing();
            }
            else
            {
                RestoreOriginalEnvironment();
            }
        }

        private static float CalculatePhotoMultiplier(float configValue)
        {
            float x = Mathf.Clamp01(configValue);
            if (x <= 0.8f) return (x / 0.8f) * (x / 0.8f);
            return 1.0f + ((x - 0.8f) / 0.2f) * 2.0f;
        }

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

        private static void ApplyHdrpVolumePostProcessing()
        {
            float mult = CalculatePhotoMultiplier(Plugin.AmbientMultiplier.Value);
            Volume[] allVolumes = GameObject.FindObjectsOfType<Volume>();

            foreach (Volume volume in allVolumes)
            {
                if (volume == null || volume.profile == null) continue;

                // Вес объема скручиваем только в жестком режиме, в естественном режиме оставляем вес оригинальным
                if (!originalVolumeWeights.ContainsKey(volume))
                {
                    originalVolumeWeights[volume] = volume.weight;
                }

                if (Plugin.SelectedMode.Value == LightControlMode.FixedExposureOverride)
                    volume.weight = originalVolumeWeights[volume] * mult;
                else
                    volume.weight = originalVolumeWeights[volume]; // В естественном режиме сохраняем полный вес профиля

                // 1. УПРАВЛЕНИЕ ЭКСПОЗИЦИЕЙ И НОЧНЫМ ЗРЕНИЕМ
                if (volume.profile.TryGet<Exposure>(out var exposure))
                {
                    string modeKey = volume.GetInstanceID() + "_Mode";
                    if (!originalEnumSettings.ContainsKey(modeKey)) originalEnumSettings[modeKey] = exposure.mode.value;

                    string fixedKey = volume.GetInstanceID() + "_Fixed";
                    if (!originalProfileSettings.ContainsKey(fixedKey)) originalProfileSettings[fixedKey] = exposure.fixedExposure.value;

                    // РЕЖИМ 1: Естественный (Ваш любимый вариант с авто-экспозицией)
                    if (Plugin.SelectedMode.Value == LightControlMode.AutoExposureAdaptation)
                    {
                        exposure.mode.value = (ExposureMode)originalEnumSettings[modeKey]; // Возвращаем авто-режим игры

                        // Мягко смещаем компенсацию, чтобы картинка была темнее, но адаптация глаза работала
                        exposure.fixedExposure.value = originalProfileSettings[fixedKey] + (1f - mult) * 3f;
                    }
                    // РЕЖИМ 2: Абсолютная тьма (Фиксированный, с исправленной шкалой EV!)
                    else
                    {
                        exposure.mode.value = ExposureMode.Fixed;

                        // ПРАВИЛЬНАЯ МАТЕМАТИКА EV: В HDRP чем ВЫШЕ число fixedExposure, тем ТЕМНЕЕ экран.
                        // При mult = 1.0 (оригинал) берется оригинальное значение. При mult = 0.0 (тьма) значение уходит вверх на +12 EV, ослепляя камеру во тьму.
                        exposure.fixedExposure.value = originalProfileSettings[fixedKey] + (1f - mult) * 12f;
                    }
                }

                // 2. ГАСИМ НЕБО GRADIENT SKY
                if (volume.profile.TryGet<GradientSky>(out var gradientSky))
                {
                    string skyKey = volume.GetInstanceID() + "_GradSky";
                    if (!originalProfileSettings.ContainsKey(skyKey)) originalProfileSettings[skyKey] = gradientSky.multiplier.value;

                    gradientSky.multiplier.value = originalProfileSettings[skyKey] * mult;
                    gradientSky.active = mult > 0.01f;
                }

                // 3. ГАСИМ ИНДЕНТИВНОСТЬ INDIRECT LIGHT
                if (volume.profile.TryGet<IndirectLightingController>(out var indirectLight))
                {
                    string indKey = volume.GetInstanceID() + "_Indirect";
                    if (!originalProfileSettings.ContainsKey(indKey)) originalProfileSettings[indKey] = indirectLight.indirectDiffuseIntensity.value;

                    indirectLight.indirectDiffuseIntensity.value = originalProfileSettings[indKey] * mult;
                    indirectLight.active = mult > 0.01f;
                }

                // 4. ГАСИМ ТУМАН (Только в режиме абсолютной тьмы, чтобы не светился серым)
                if (volume.profile.TryGet<Fog>(out var fog))
                {
                    if (Plugin.SelectedMode.Value == LightControlMode.FixedExposureOverride)
                        fog.active = mult > 0.01f;
                    else
                        fog.active = true; // В естественном режиме туман плавно подстроится сам
                }
            }

            DynamicGI.UpdateEnvironment();
        }

        // МЕТОД ОТКАТА: Возвращает исходное фоновое освещение игры, если флаг Enabled выключен
        private static void RestoreOriginalEnvironment()
        {
            if (isOriginalAmbientSaved)
            {
                RenderSettings.ambientLight = originalAmbientColor;
                RenderSettings.ambientIntensity = 1f;
                RenderSettings.reflectionIntensity = 1f;
            }

            Volume[] allVolumes = GameObject.FindObjectsOfType<Volume>();
            foreach (Volume volume in allVolumes)
            {
                if (volume == null || volume.profile == null) continue;

                if (originalVolumeWeights.ContainsKey(volume))
                    volume.weight = originalVolumeWeights[volume];

                if (volume.profile.TryGet<Exposure>(out var exposure))
                {
                    string modeKey = volume.GetInstanceID() + "_Mode";
                    if (originalEnumSettings.ContainsKey(modeKey)) exposure.mode.value = (ExposureMode)originalEnumSettings[modeKey];

                    string fixedKey = volume.GetInstanceID() + "_Fixed";
                    if (originalProfileSettings.ContainsKey(fixedKey)) exposure.fixedExposure.value = originalProfileSettings[fixedKey];
                }

                if (volume.profile.TryGet<GradientSky>(out var gradientSky))
                {
                    string skyKey = volume.GetInstanceID() + "_GradSky";
                    if (originalProfileSettings.ContainsKey(skyKey)) gradientSky.multiplier.value = originalProfileSettings[skyKey];
                    gradientSky.active = true;
                }

                if (volume.profile.TryGet<IndirectLightingController>(out var indirectLight))
                {
                    string indKey = volume.GetInstanceID() + "_Indirect";
                    if (originalProfileSettings.ContainsKey(indKey)) indirectLight.indirectDiffuseIntensity.value = originalProfileSettings[indKey];
                    indirectLight.active = true;
                }

                if (volume.profile.TryGet<Fog>(out var fog)) fog.active = true;
            }

            DynamicGI.UpdateEnvironment();
        }
    }
}
