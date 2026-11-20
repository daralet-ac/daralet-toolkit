namespace DaraletPlayer.Feed {
    /// <summary>
    /// Ready-made draw distances (the user's call, November 2026). The game's own landscape is kept low and the plugin
    /// draws the land past it in full detail (far land), billboards
    /// standing on it: at the Holtburg overlook, landscape 5 with far land to 24 left 110 MB more free than landscape 11
    /// and looked all but the same, and landscape 4 the most free of all (1,765 MB). Scenery is kept at 1 or 2, the
    /// game's trees being the cost of both kinds. Each keeps 1.4 GB of the game's memory spare by the estimate, for
    /// teleports (GraphicsCost). Retail is the game's own. The rest of the draw distance (Detail, Far scenery, Fog) is
    /// left as it is.
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

            /// <summary>Retail: the plugin's draw distance and far land off.</summary>
            public bool Off;
        }

        public static readonly Preset[] All = {
            new Preset { Name = "Retail", Tip = "The game's own draw distance.", Off = true },
            new Preset {
                Name = "Performance", Scenery = 1, Ground = 1, Landscape = 5, FarLand = 19,
                Tip = "The game's land to 5 landblocks, the plugin's to 24; the game's trees to 1.",
            },
            new Preset {
                Name = "Balanced", Scenery = 2, Ground = 2, Landscape = 6, FarLand = 18,
                Tip = "The game's land to 6 landblocks, the plugin's to 24; the game's trees to 2.",
            },
            new Preset {
                Name = "Quality", Scenery = 2, Ground = 4, Landscape = 8, FarLand = 24,
                Tip = "The game's land to 8 landblocks, in full detail to 4, the plugin's to 32; the game's trees to 2.",
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
            o.FarTrees = 2;
            o.Billboards = true;
            o.FarBuildings = true;
            o.FarLand = preset.FarLand;
            // the takeover of the game's own rings stays an experiment: in test runs with it on the game crashed about
            // every ten teleports, without it none in 29 (November 2026)
            o.FarLandNear = false;
        }

        /// <summary>The preset these settings are, or null for Custom.</summary>
        public static Preset Matching(DrawDistanceOptions o) {
            foreach (var preset in All) {
                if (preset.Off ? !o.On && o.ClampedFarLand == 0
                    : o.On && o.ClampedSceneryRadius == preset.Scenery && o.ClampedSceneryDensity == 100
                    && o.ClampedGroundRadius == preset.Ground && o.ClampedLandscapeRadius == preset.Landscape
                    && o.ClampedFarTrees == 2 && o.Billboards && o.FarBuildings
                    && o.ClampedFarLand == preset.FarLand && !o.FarLandNear) {
                    return preset;
                }
            }

            return null;
        }
    }
}
