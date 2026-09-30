using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using System;
using System.Collections;
using System.Collections.Generic;

namespace BetterFlashlights
{
    public class CachedFlashlight
    {
        public Light lightComponent;
        public bool isPlayerLight;

        public Color originalColor;
        public float originalRange;
        public float originalIntensity;
        public float originalSpotAngle;
        public Texture originalCookie;
        public bool hasSavedDefaults = false;
        public bool wasModifiedByMod = false;
    }

    [BepInPlugin("com.custom.flashlightmodifier", "Better Flashlights", "1.0.1")]
    public class FlashlightPlugin : BaseUnityPlugin
    {
        public static FlashlightPlugin Instance;

        public static ConfigEntry<bool> ModEnabled;
        public static ConfigEntry<bool> ModifyBotLights;
        public static ConfigEntry<float> FlashlightIntensity, FlashlightRange, FlashlightSpotAngle;
        public static ConfigEntry<float> FlashlightKelvin;

        public static ConfigEntry<bool> EnableFlickering;
        public static ConfigEntry<float> FlickeringChance;
        public static ConfigEntry<float> FlickeringSpeed;

        public static Texture2D customBeamCookie = null;
        private Coroutine scanCoroutine = null;

        private readonly string[] tarkovDeviceNames = new string[]
        {
            "klesh", "2p", "x400", "gtl", "las", "tac", "tbl", "dbal", "raptar",
            "wmx", "baldr", "mawl", "peq", "ngal", "perst", "k-2iks", "xc1", "kr-2", "m600"
        };

        private readonly List<CachedFlashlight> cachedTarkovLights = new List<CachedFlashlight>();
        private Camera mainCamCache = null;

        private void Awake()
        {
            Instance = this;

            ModEnabled = Config.Bind("1. General", "Enable Mod", true, "Enable mod");
            ModifyBotLights = Config.Bind("1. General", "Apply to Bots", true, "If enabled, changes bot flashlights. If disabled, bots keep default game flashlights.");

            FlashlightIntensity = Config.Bind("2. Flashlight Settings", "Intensity", 15.0f, new ConfigDescription("Intensity", new AcceptableValueRange<float>(1f, 50f)));
            FlashlightRange = Config.Bind("2. Flashlight Settings", "Range (Meters)", 70.0f, new ConfigDescription("Range", new AcceptableValueRange<float>(10f, 200f)));
            FlashlightSpotAngle = Config.Bind("2. Flashlight Settings", "Spot Angle", 35.0f, new ConfigDescription("Angle of the beam", new AcceptableValueRange<float>(21.0f, 120f)));

            FlashlightKelvin = Config.Bind("3. LED Temperature", "Color Temperature (Kelvin)", 6500f, new ConfigDescription("2000K = Warm, 12000K = ABI Ultra Cool", new AcceptableValueRange<float>(2000f, 12000f)));

            EnableFlickering = Config.Bind("4. Flickering Simulation", "Enable Auto Flickering", false, "Toggle custom flickering");
            FlickeringChance = Config.Bind("4. Flickering Simulation", "Flicker Intensity", 25.0f, new ConfigDescription("How deep the light drops are", new AcceptableValueRange<float>(5f, 60f)));
            FlickeringSpeed = Config.Bind("4. Flickering Simulation", "Flicker Speed", 15.0f, new ConfigDescription("How fast it fluctuates", new AcceptableValueRange<float>(5f, 50f)));

            CreateProceduralCookie();

            if (ModEnabled.Value)
            {
                scanCoroutine = StartCoroutine(ScanRoutine());
            }
        }

        private void CreateProceduralCookie()
        {
            int res = 64;
            if (customBeamCookie != null) Destroy(customBeamCookie);

            customBeamCookie = new Texture2D(res, res, TextureFormat.Alpha8, false)
            {
                name = "BetterFlashlightsCleanCookie",
                wrapMode = TextureWrapMode.Clamp
            };

            Color[] pixels = new Color[res * res];
            float center = res / 2f;
            float maxDist = res / 2f;

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                    float alpha = Mathf.Clamp01(1f - (dist / maxDist));
                    alpha = Mathf.Pow(alpha, 1.5f);
                    pixels[y * res + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            customBeamCookie.SetPixels(pixels);
            customBeamCookie.Apply();
        }

        public static Color KelvinToRGB(float kelvin)
        {
            float temp = kelvin / 100f;
            float r, g, b;

            if (temp <= 66f) r = 255f;
            else { r = temp - 60f; r = 329.698727446f * Mathf.Pow(r, -0.1332047592f); }

            if (temp <= 66f) { g = temp; g = 99.4708025861f * Mathf.Log(g) - 161.1195681661f; }
            else { g = temp - 60f; g = 288.1221695283f * Mathf.Pow(g, -0.0755148492f); }

            if (temp >= 66f) b = 255f;
            else if (temp <= 19f) b = 0f;
            else { b = temp - 10f; b = 138.5177312231f * Mathf.Log(b) - 305.0447927307f; }

            return new Color(Mathf.Clamp01(r / 255f), Mathf.Clamp01(g / 255f), Mathf.Clamp01(b / 255f));
        }

        private IEnumerator ScanRoutine()
        {
            WaitForSeconds waitTime = new WaitForSeconds(4.0f);
            while (true)
            {
                if (!ModEnabled.Value) { yield return waitTime; continue; }

                if (mainCamCache == null) mainCamCache = Camera.main;

                Light[] allLights = Resources.FindObjectsOfTypeAll<Light>();
                List<CachedFlashlight> newScanList = new List<CachedFlashlight>();

                foreach (Light light in allLights)
                {
                    if (light == null || light.type != LightType.Spot) continue;

                    if (IsTarkovFlashlight(light))
                    {
                        CachedFlashlight existing = cachedTarkovLights.Find(c => c.lightComponent == light);
                        if (existing != null)
                        {
                            newScanList.Add(existing);
                        }
                        else
                        {
                            bool isPlayer = IsPlayerLight(light);
                            CachedFlashlight newLight = new CachedFlashlight
                            {
                                lightComponent = light,
                                isPlayerLight = isPlayer
                            };

                            newLight.originalColor = light.color;
                            newLight.originalRange = light.range;
                            newLight.originalIntensity = light.intensity;
                            newLight.originalSpotAngle = light.spotAngle;
                            newLight.originalCookie = light.cookie;
                            newLight.hasSavedDefaults = true;

                            newScanList.Add(newLight);
                        }
                    }
                }

                cachedTarkovLights.Clear();
                cachedTarkovLights.AddRange(newScanList);
                yield return waitTime;
            }
        }

        private void Update()
        {
            if (!ModEnabled.Value || cachedTarkovLights.Count == 0) return;

            for (int i = cachedTarkovLights.Count - 1; i >= 0; i--)
            {
                if (cachedTarkovLights[i] == null || cachedTarkovLights[i].lightComponent == null)
                {
                    cachedTarkovLights.RemoveAt(i);
                }
            }

            float baseIntensity = FlashlightIntensity.Value;
            float currentFlickerModifier = 1f;

            if (EnableFlickering.Value)
            {
                float targetFlickerStrength = (FlickeringChance.Value / 100f);
                float waveTime = Time.time * FlickeringSpeed.Value;

                float wave1 = Mathf.Sin(waveTime);
                float wave2 = Mathf.Cos(waveTime * 1.45f);
                float wave3 = Mathf.Sin(waveTime * 0.35f);

                float combinedNoise = (wave1 + wave2 + wave3) / 3f;

                if (combinedNoise < 0f)
                {
                    currentFlickerModifier = Mathf.Lerp(1f, 1f - targetFlickerStrength, Mathf.Abs(combinedNoise));
                }
            }

            Color calculatedColor = KelvinToRGB(FlashlightKelvin.Value);

            for (int i = 0; i < cachedTarkovLights.Count; i++)
            {
                var cachedLight = cachedTarkovLights[i];
                if (cachedLight == null) continue;

                Light light = cachedLight.lightComponent;
                if (light != null)
                {
                    // ΦΙΛΤΡΟ GAMEOBJECT: Αν το όνομα περιέχει laser, point ή impact, είναι το light εφέ του laser. Skip!
                    string lightObjName = light.gameObject.name.ToLower();
                    if (lightObjName.Contains("laser") || lightObjName.Contains("point") || lightObjName.Contains("impact") || lightObjName.Contains("red_dot"))
                    {
                        continue;
                    }

                    if (cachedLight.isPlayerLight)
                    {
                        if (light.isActiveAndEnabled)
                        {
                            ApplyPlayerFlashlightSettings(light, baseIntensity * currentFlickerModifier, calculatedColor);
                        }
                    }
                    else
                    {
                        if (!ModifyBotLights.Value)
                        {
                            if (cachedLight.wasModifiedByMod && cachedLight.hasSavedDefaults)
                            {
                                light.color = cachedLight.originalColor;
                                light.range = cachedLight.originalRange;
                                light.intensity = cachedLight.originalIntensity;
                                light.spotAngle = cachedLight.originalSpotAngle;
                                light.cookie = cachedLight.originalCookie;
                                cachedLight.wasModifiedByMod = false;
                            }
                            continue;
                        }
                        if (light.isActiveAndEnabled)
                        {
                            if (light.cookie != null && light.cookie != customBeamCookie)
                            {
                                string cookieName = light.cookie.name.ToLower();
                                if (cookieName.Contains("laser") || cookieName.Contains("dot") ||
                                cookieName.Contains("point") || cookieName.Contains("ir") ||
                                cookieName.Contains("red") || cookieName.Contains("green"))
                                {
                                    continue;
                                }
                            }
                            float distToCam = (mainCamCache != null) ? Vector3.Distance(light.transform.position, mainCamCache.transform.position) : 100f;
                            LightShadows shadowType = (distToCam < 40f) ? LightShadows.Hard : LightShadows.None;
                            ApplyBotFlashlightSettings(light, baseIntensity, calculatedColor, shadowType);
                            cachedLight.wasModifiedByMod = true;
                        }
                    }
                }
            }
        }
        private bool IsPlayerLight(Light light)
        {
            if (light == null) return false;
            if (light.gameObject.layer == 24) return true;
            if (mainCamCache != null && Vector3.Distance(light.transform.position, mainCamCache.transform.position) < 1.2f)
            {
                return true;
            }
            Transform current = light.transform;
            while (current != null)
            {
                if (current.gameObject.layer == 24) return true;
                string parentName = current.name.ToLower();
                if (parentName.Contains("observed") || parentName.Contains("bot") || parentName.Contains("client") || parentName.Contains("corpse"))
                {
                    return false;
                }
                if (parentName.Contains("fps") || parentName.Contains("camera") || parentName.Contains("localplayer") || parentName.Contains("player"))
                {
                    return true;
                }
                current = current.parent;
            }
            return false;
        }
        private void ApplyPlayerFlashlightSettings(Light light, float intensityToApply, Color targetColor)
        {
            if (light.color != targetColor) light.color = targetColor;
            if (!Mathf.Approximately(light.range, FlashlightRange.Value)) light.range = FlashlightRange.Value;
            float targetAngle = Mathf.Clamp(FlashlightSpotAngle.Value, 21.0f, 120.0f);
            if (!Mathf.Approximately(light.spotAngle, targetAngle)) light.spotAngle = targetAngle;
            light.intensity = intensityToApply;
            if (light.cookie != customBeamCookie) light.cookie = customBeamCookie;
            if (light.shadows != LightShadows.Soft) light.shadows = LightShadows.Soft;
            if (!Mathf.Approximately(light.shadowStrength, 1.0f)) light.shadowStrength = 1.0f;
            if (light.renderMode != LightRenderMode.ForcePixel) light.renderMode = LightRenderMode.ForcePixel;
        }
        private void ApplyBotFlashlightSettings(Light light, float intensityToApply, Color targetColor, LightShadows shadowType)
        {
            if (light.color != targetColor) light.color = targetColor;
            if (!Mathf.Approximately(light.range, FlashlightRange.Value)) light.range = FlashlightRange.Value;
            float targetAngle = Mathf.Clamp(FlashlightSpotAngle.Value, 21.0f, 120.0f);
            if (!Mathf.Approximately(light.spotAngle, targetAngle)) light.spotAngle = targetAngle;
            if (!Mathf.Approximately(light.intensity, intensityToApply)) light.intensity = intensityToApply;
            if (light.cookie != customBeamCookie) light.cookie = customBeamCookie;
            if (light.shadows != shadowType) light.shadows = shadowType;
            if (!Mathf.Approximately(light.shadowStrength, 1.0f)) light.shadowStrength = 1.0f;
            if (light.renderMode != LightRenderMode.ForcePixel) light.renderMode = LightRenderMode.ForcePixel;
        }
        public bool IsTarkovFlashlight(Light light)
        {
            if (light == null) return false;
            Transform current = light.transform;
            bool hasValidDeviceKeyword = false;
            bool isAttachedToEntityOrWeapon = false;
            while (current != null)
            {
                string objName = current.name.ToLower();
                if (objName.Contains("station") || objName.Contains("lamp") || objName.Contains("factory") ||
                objName.Contains("light_root") || objName.Contains("ambient") || objName.Contains("ceiling") ||
                objName.Contains("hanging") || objName.Contains("pillar") || objName.Contains("static"))
                {
                    return false;
                }
                foreach (string deviceKeyword in tarkovDeviceNames)
                {
                    if (objName.Contains(deviceKeyword))
                    {
                        if (objName.Contains("laser") && !objName.Contains("light") && !objName.Contains("klesh") && !objName.Contains("x400") && !objName.Contains("baldr") && !objName.Contains("dbal"))
                        {
                            return false;
                        }
                        hasValidDeviceKeyword = true;
                    }
                }
                if (objName.Contains("weapon") || objName.Contains("firearm") || objName.Contains("item") ||
                objName.Contains("player") || objName.Contains("bot") || objName.Contains("hand") ||
                objName.Contains("equipment") || objName.Contains("mod") || objName.Contains("controller"))
                {
                    isAttachedToEntityOrWeapon = true;
                }
                current = current.parent;
            }
            return hasValidDeviceKeyword && isAttachedToEntityOrWeapon;
        }
        private void OnDestroy()
        {
            if (scanCoroutine != null) StopCoroutine(scanCoroutine);
            foreach (var cached in cachedTarkovLights)
            {
                if (cached != null && cached.lightComponent != null && cached.hasSavedDefaults)
                {
                    cached.lightComponent.color = cached.originalColor;
                    cached.lightComponent.range = cached.originalRange;
                    cached.lightComponent.intensity = cached.originalIntensity;
                    cached.lightComponent.spotAngle = cached.originalSpotAngle;
                    cached.lightComponent.cookie = cached.originalCookie;
                }
            }
            cachedTarkovLights.Clear();
        }
    }
}
