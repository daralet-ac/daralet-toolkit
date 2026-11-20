using System;
using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// What each graphics setting costs, in memory (MB of the game's address space) and frame time (ms), over the game's
    /// own (the user's call, November 2026: a cost beside each setting and two budget bars, so the balance between them
    /// can be seen). Estimates from what was measured in the game, landblock by landblock: the draw distance's at a desert
    /// overlook and the Holtburg plateau (November 2026), so a dense forest costs more than they say. Those marked Rough
    /// weren't measured on their own.
    /// </summary>
    public static class GraphicsCost {
        /// <summary>The game's own full-detail radius: what's loaded with nothing of the plugin's.</summary>
        private const int GameScenery = 1;

        // a landblock of landscape past the game's: ~1 MB (21 to 24: 552 blocks, 564 MB); frame time all but none
        // (24 against the game's 15: 150 fps to 141)
        private const double LandscapeMb = 1.0;
        private const double LandscapeMs = 0.0003;

        // a block in full detail without trees: 1.8 MB, 0.013 ms (Ground 4 to 12 at Scenery 4: 132 fps to 67, 1 GB)
        private const double GroundMb = 1.8;
        private const double GroundMs = 0.013;

        // a block of the game's trees at full density: ~8 MB (scenery 5 to 16 at 20%), ~0.2 ms (ring 2 in a forest, 16
        // blocks, 150 fps to 107; ring 3, 24 blocks, to 60)
        private const double SceneryMb = 8;
        private const double SceneryMs = 0.2;

        /// <summary>One cost: what it is, memory and frame time, and whether it's a rough guess.</summary>
        public struct Item {
            public string Name;
            public double Mb;
            public double Ms;
            public bool Rough;

            public Item(string name, double mb, double ms, bool rough = false) {
                Name = name;
                Mb = mb;
                Ms = ms;
                Rough = rough;
            }
        }

        private static double Blocks(int radius) {
            var side = 2.0 * Math.Max(0, radius) + 1;
            return side * side;
        }

        public static Item Landscape(int radius, int gameRadius) {
            var more = Math.Max(0, Blocks(radius) - Blocks(gameRadius));
            return new Item("Landscape", more * LandscapeMb, more * LandscapeMs);
        }

        public static Item Ground(int ground, int scenery) {
            var more = Math.Max(0, Blocks(Math.Max(ground, scenery)) - Blocks(Math.Max(scenery, GameScenery)));
            return new Item("Ground detail", more * GroundMb, more * GroundMs);
        }

        public static Item Scenery(int scenery, int density) {
            // the game's own ring is there anyway, at full density; thinner, even that costs less
            var share = Math.Max(0, Math.Min(100, density)) / 100.0;
            var blocks = Blocks(scenery) * share - Blocks(GameScenery);
            return new Item("Scenery", blocks * SceneryMb, blocks * SceneryMs);
        }

        /// <summary>The game's own trees thinned, as part of Scenery's cost: shown on its own row.</summary>
        public static Item Density(int scenery, int density) {
            var full = Scenery(scenery, 100);
            var thinned = Scenery(scenery, density);
            return new Item("Scenery density", thinned.Mb - full.Mb, thinned.Ms - full.Ms);
        }

        /// <summary>3D trees: nothing measurable at the desert overlook; at Holtburg, rings 3 to 6, a few fps.</summary>
        public static Item FarTrees(int rings) {
            return new Item("3D trees", rings * 5, rings * 0.1);
        }

        /// <summary>Billboards: 18,000 at landscape 15 took 115 MB; the frame rate didn't move.</summary>
        public static Item Billboards(bool on, int landscape, int scenery) {
            if (!on) {
                return new Item("Billboards", 0, 0);
            }

            var blocks = Math.Max(0, Blocks(landscape) - Blocks(scenery));
            return new Item("Billboards", blocks * 0.12, 0.15);
        }

        public static Item FarBuildings(bool on, int landscape) {
            // 15 to 30 MB at landscape 18 (desert); its frame time didn't show
            return new Item("Far buildings", on ? Blocks(landscape) * 0.02 : 0, on ? 0.1 : 0);
        }

        /// <summary>
        /// Far land (Ui/FarLand.cs), the blocks from the game's last ring out: its geometry in the game's memory, about
        /// 6.5 KB a block (1,960 blocks were 10.4 MB, the lists to build it a little more); its pictures are on the
        /// graphics card. The frame rate barely moved (90 fps against 93 at landscape 5, rings to 24).
        /// </summary>
        public static Item FarLand(int rings, int landscape) {
            if (rings <= 0) {
                return new Item("Far land", 0, 0);
            }

            var blocks = Blocks(landscape + rings) - Blocks(landscape - 1);
            return new Item("Far land", blocks * 0.007, 0.4);
        }

        /// <summary>
        /// Billboards standing on the far land: 133,000 of them past landscape 5 took about 50 MB with video memory on
        /// (fresh restarts, 1,786 MB free without, 1,736 with), the frame rate unmoved.
        /// </summary>
        public static Item FarLandBillboards(bool billboards, int rings, int landscape) {
            if (!billboards || rings <= 0) {
                return new Item("Billboards on far land", 0, 0);
            }

            var blocks = Blocks(landscape + rings) - Blocks(landscape);
            return new Item("Billboards on far land", blocks * 0.025, 0.2);
        }

        // the screen passes, measured on and off at the desert overlook (2560x1440, 83 fps): each within the noise
        // (about 0.3 ms), their textures in video memory, not the game's address space. Sun shadows cost with the draws
        // they copy: 107 fps to 93 among Holtburg's trees, 1.4 ms
        public static Item SunShadows(bool on) => new Item("Sun shadows", on ? 5 : 0, on ? 1.4 : 0);
        public static Item Shading(bool on) => new Item("Shading", on ? 2 : 0, on ? 0.3 : 0);
        public static Item Water(bool on) => new Item("Water", on ? 3 : 0, on ? 0.3 : 0);
        public static Item Lamps(bool on, bool shadows) =>
            new Item("Lamp light", on ? 2 : 0, on ? (shadows ? 0.5 : 0.3) : 0, rough: true);
        public static Item Grass(bool on) => new Item("Grass", on ? 10 : 0, on ? 1.0 : 0, rough: true);
        public static Item AntiAlias(bool on) => new Item("Anti-aliasing", on ? 2 : 0, on ? 0.3 : 0);
        public static Item Sharpen(int amount) => new Item("Sharpen", amount > 0 ? 2 : 0, amount > 0 ? 0.1 : 0);
        public static Item Glow(int amount) => new Item("Glow", amount > 0 ? 2 : 0, amount > 0 ? 0.2 : 0);

        /// <summary>The draw distance's costs for its settings, the landscape the game's when theirs is 0.</summary>
        public static List<Item> DrawDistance(DrawDistanceOptions o, int gameLandscape) {
            var items = new List<Item>();
            var landscape = o.ClampedLandscapeRadius > 0 && o.On ? o.ClampedLandscapeRadius : gameLandscape;

            // far land works with the draw distance off, as the fog does
            if (o.ClampedFarLand > 0) {
                items.Add(FarLand(o.ClampedFarLand, landscape));
            }

            if (!o.On) {
                return items;
            }

            items.Add(FarLandBillboards(o.Billboards, o.ClampedFarLand, landscape));
            items.Add(Landscape(landscape, gameLandscape));
            items.Add(Ground(o.ClampedGroundRadius, o.ClampedSceneryRadius));
            items.Add(Scenery(o.ClampedSceneryRadius, o.ClampedSceneryDensity));
            items.Add(FarTrees(o.ClampedFarTrees));
            items.Add(Billboards(o.Billboards, landscape, o.ClampedSceneryRadius));
            items.Add(FarBuildings(o.FarBuildings, landscape));
            return items;
        }

        public static (double Mb, double Ms) Sum(IEnumerable<Item> items) {
            double mb = 0, ms = 0;

            foreach (var item in items) {
                mb += item.Mb;
                ms += item.Ms;
            }

            return (mb, ms);
        }

        /// <summary>"+290 MB · +1.9 ms", "~" in front of a rough one, empty for nothing.</summary>
        public static string Label(Item item) {
            if (Math.Abs(item.Mb) < 0.5 && Math.Abs(item.Ms) < 0.05) {
                return "";
            }

            var rough = item.Rough ? "~" : "";
            return $"{rough}{Signed(item.Mb, "0")} MB · {Signed(item.Ms, "0.0")} ms";
        }

        private static string Signed(double value, string format) {
            return (value >= 0 ? "+" : "") + value.ToString(format);
        }
    }
}
