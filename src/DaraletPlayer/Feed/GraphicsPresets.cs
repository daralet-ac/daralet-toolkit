namespace DaraletPlayer.Feed {
    /// <summary>
    /// Ready-made draw distances (the user's call, November 2026). The game's own landscape is kept low and the plugin
    /// draws the land past it in full detail (far land), billboards
    /// standing on it: at the Holtburg overlook, landscape 5 with far land to 24 left 110 MB more free than landscape 11
    /// and looked all but the same, and landscape 4 the most free of all (1,765 MB). Scenery and Ground detail stay at
    /// the game's own 1 in every preset, the plugin's 3D trees standing in past it: on tours of fresh outdoor places
    /// (November 2026, test runs) Quality's Scenery 2 and Ground 4 crashed the game every 10 teleports, each alone about
    /// as soon, where the plugin's drawing and lighting over Scenery 1 went 45 (bare: 26); Landscape 8 alone did little
    /// harm. Each keeps 1.4 GB of the game's memory spare by the estimate, for teleports (GraphicsCost). Retail is the
    /// game's own. The rest of the draw distance (Fog) is left as it is.
    /// </summary>
    public static class GraphicsPresets {
        public sealed class Preset {
            public string Name;
            public string Tip;
            public int Scenery;
            public int Ground;
            public int Landscape;

            /// <summary>Rings of far land past Landscape.</summary>
            public int FarLand;

            /// <summary>Full detail inside Landscape: far land in place of the game's coarse rings (FarLandNear).</summary>
            public bool Near;

            /// <summary>Rings of the plugin's own 3D trees past Scenery.</summary>
            public int FarTrees = 2;

            /// <summary>Retail: the plugin's draw distance and far land off.</summary>
            public bool Off;
        }

        public static readonly Preset[] All = {
            new Preset { Name = "Retail", Tip = "The game's own draw distance.", Off = true },
            new Preset {
                Name = "Performance", Scenery = 1, Ground = 1, Landscape = 5, FarLand = 19,
                Tip = "The game's land to 5 landblocks, the plugin's to 24; the plugin's trees past the game's.",
            },
            new Preset {
                Name = "Balanced", Scenery = 1, Ground = 1, Landscape = 6, FarLand = 18, FarTrees = 3,
                Tip = "The game's land to 6 landblocks, the plugin's to 24; the plugin's trees 3 rings past the game's.",
            },
            new Preset {
                Name = "Quality", Scenery = 1, Ground = 1, Landscape = 8, FarLand = 24, Near = true, FarTrees = 4,
                Tip = "The game's land to 8 landblocks, the plugin's full detail past its nearest to 32; the plugin's "
                    + "trees 4 rings past the game's.",
            },
        };

        /// <summary>Puts a preset's settings on (3D trees, billboards, far buildings and far land with it).</summary>
        public static void Apply(Preset preset, DrawDistanceOptions o) {
            if (preset.Off) {
                o.On = false;
                o.FarLand = 0;
                return;
            }

            o.On = true;
            o.SceneryRadius = preset.Scenery;
            o.SceneryDensity = 100;
            o.GroundRadius = preset.Ground;
            o.LandscapeRadius = preset.Landscape;
            o.FarTrees = preset.FarTrees;
            o.Billboards = true;
            o.FarBuildings = true;
            o.FarLand = preset.FarLand;
            // the takeover of the game's own rings: Quality only (the user's call, November 2026), after test runs had kept
            // it out of every preset (crashes about every ten teleports, later found with the Detail setting at 1000 m on)
            o.FarLandNear = preset.Near;
        }

        /// <summary>The preset these settings are, or null for Custom.</summary>
        public static Preset Matching(DrawDistanceOptions o) {
            foreach (var preset in All) {
                if (preset.Off ? !o.On && o.ClampedFarLand == 0
                    : o.On && o.ClampedSceneryRadius == preset.Scenery && o.ClampedSceneryDensity == 100
                    && o.ClampedGroundRadius == preset.Ground && o.ClampedLandscapeRadius == preset.Landscape
                    && o.ClampedFarTrees == preset.FarTrees && o.Billboards && o.FarBuildings
                    && o.ClampedFarLand == preset.FarLand && o.FarLandNear == preset.Near) {
                    return preset;
                }
            }

            return null;
        }
    }
}
