namespace DaraletPlayer.Feed {
    /// <summary>
    /// Ready-made lighting (the user's call, November 2026: presets for the Lighting section as for the landscape, and
    /// one at the top setting both), by the same names as GraphicsPresets so the top row can set both at once. Each sets
    /// what costs frame rate and memory: whether the shading, sun shadows and lamp light are on, the occlusion, how far
    /// the sun's shadows and the lamps reach, and how many lamps cast shadows. How the light looks is the player's and
    /// left as it is: the sun shadows' darkness and softness, the lamps' brightness and reach, the night. Sun shadows
    /// stay at 400 m or under: past that the far map's texels pass 40 cm (Ui/DepthShading.Shadows.cs). Retail is the
    /// game's own: all three off.
    /// </summary>
    public static class LightingPresets {
        public sealed class Preset {
            public string Name;
            public string Tip;

            /// <summary>Retail: shading, sun shadows and lamp light off.</summary>
            public bool Off;

            public int Occlusion;
            public int SunDistance;
            public int LampDistance;
            public int ShadowedLamps;
        }

        public static readonly Preset[] All = {
            new Preset { Name = "Retail", Tip = "The game's own lighting.", Off = true },
            new Preset {
                Name = "Performance", Occlusion = 40, SunDistance = 120, LampDistance = 100, ShadowedLamps = 4,
                Tip = "Light occlusion, sun shadows to 120 m, lamps lit to 100 m, the 4 nearest with shadows.",
            },
            new Preset {
                Name = "Balanced", Occlusion = 60, SunDistance = 250, LampDistance = 200, ShadowedLamps = 8,
                Tip = "Occlusion, sun shadows to 250 m, lamps lit to 200 m, the 8 nearest with shadows.",
            },
            new Preset {
                Name = "Quality", Occlusion = 70, SunDistance = 400, LampDistance = 300, ShadowedLamps = 12,
                Tip = "Deeper occlusion, sun shadows to 400 m, lamps lit to 300 m, the 12 nearest with shadows (as many "
                    + "as your card takes).",
            },
        };

        /// <summary>Puts a preset's settings on.</summary>
        public static void Apply(Preset preset, ShadingOptions shading, SunShadowOptions sun, LampLightOptions lamps) {
            if (preset.Off) {
                shading.On = false;
                sun.On = false;
                lamps.On = false;
                return;
            }

            shading.On = true;
            shading.Occlusion = preset.Occlusion;
            sun.On = true;
            sun.Distance = preset.SunDistance;
            lamps.On = true;
            lamps.Shadows = true;
            lamps.FireLight = true;
            lamps.Distance = preset.LampDistance;
            lamps.ShadowedLamps = preset.ShadowedLamps;
        }

        /// <summary>The preset these settings are, or null for Custom.</summary>
        public static Preset Matching(ShadingOptions shading, SunShadowOptions sun, LampLightOptions lamps) {
            foreach (var preset in All) {
                if (preset.Off ? !shading.On && !sun.On && !lamps.On
                    : shading.On && shading.ClampedOcclusion == preset.Occlusion && sun.On
                    && sun.ClampedDistance == preset.SunDistance && lamps.On && lamps.Shadows && lamps.FireLight
                    && lamps.ClampedDistance == preset.LampDistance && lamps.ClampedShadowedLamps == preset.ShadowedLamps) {
                    return preset;
                }
            }

            return null;
        }
    }
}
