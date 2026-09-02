using BepInEx;
using BepInEx.Configuration;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace BetterFlashlights
{
    [BepInPlugin("com.custom.flashlightmodifier", "Better Flashlights", "1.0.0")]
    public class FlashlightPlugin : BaseUnityPlugin
    {
        public static ConfigEntry<bool> ModEnabled;
        public static ConfigEntry<float> FlashlightIntensity, FlashlightRange, FlashlightSpotAngle, RedColor, GreenColor, BlueColor;

        public static Texture2D customBeamCookie = null;

        private void Awake()
        {
            ModEnabled = Config.Bind("1. General", "Enable Mod", true, "Enable mod");
            FlashlightIntensity = Config.Bind("2. Flashlight Settings", "Intensity", 15.0f, new ConfigDescription("Intensity", new AcceptableValueRange<float>(1f, 50f)));
            FlashlightRange = Config.Bind("2. Flashlight Settings", "Range (Meters)", 70.0f, new ConfigDescription("Range", new AcceptableValueRange<float>(10f, 200f)));
            FlashlightSpotAngle = Config.Bind("2. Flashlight Settings", "Spot Angle", 35.0f, new ConfigDescription("Angle", new AcceptableValueRange<float>(5.0f, 120f)));
            BlueColor = Config.Bind("3. LED Color (RGB)", "Blue (B)", 1.0f, new ConfigDescription("Amount of Blue", new AcceptableValueRange<float>(0f, 1f)));
            GreenColor = Config.Bind("3. LED Color (RGB)", "Green (G)", 0.88f, new ConfigDescription("Amount of Green", new AcceptableValueRange<float>(0f, 1f)));
            RedColor = Config.Bind("3. LED Color (RGB)", "Red (R)", 0.75f, new ConfigDescription("Amount of Red", new AcceptableValueRange<float>(0f, 1f)));

            CreateProceduralCookie();
        }

        private void CreateProceduralCookie()
        {
            int res = 64;
            customBeamCookie = new Texture2D(res, res, TextureFormat.Alpha8, false);
            Color[] pixels = new Color[res * res];
            Vector2 center = new Vector2(res / 2f, res / 2f);
            float maxDist = res / 2f;

            for (int y = 0; y < res; y++)
            {
                for (int x = 0; x < res; x++)
                {
                    float dist = Vector2.Distance(new Vector2(x, y), center);
                    float alpha = Mathf.Clamp01(1f - (dist / maxDist));
                    alpha = Mathf.Pow(alpha, 1.5f);
                    pixels[y * res + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            customBeamCookie.SetPixels(pixels);
            customBeamCookie.Apply();
        }

        private void Update()
        {
            if (!ModEnabled.Value) return;

            Camera mainCam = Camera.main;
            if (mainCam == null) return;

            Light[] activeLights = FindObjectsOfType<Light>();

            foreach (Light light in activeLights)
            {
                if (light == null || !light.isActiveAndEnabled || light.type != LightType.Spot || light.name.ToLower().Contains("laser")) continue;

                float distanceToCam = Vector3.Distance(light.transform.position, mainCam.transform.position);

                if (distanceToCam >= 0.22f && distanceToCam < 1.2f)
                {
                    light.color = new Color(RedColor.Value, GreenColor.Value, BlueColor.Value);
                    light.range = FlashlightRange.Value;
                    light.spotAngle = Mathf.Clamp(FlashlightSpotAngle.Value, 5.0f, 120.0f);
                    light.intensity = FlashlightIntensity.Value;

                    if (light.cookie != customBeamCookie)
                    {
                        light.cookie = customBeamCookie;
                    }

                    light.shadows = LightShadows.Soft;
                    light.shadowStrength = 1.0f;
                    light.shadowBias = 0.01f;
                    light.renderMode = LightRenderMode.ForcePixel;
                }
            }
        }
    }
}
