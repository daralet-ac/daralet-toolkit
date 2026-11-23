using System;
using System.Collections.Generic;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using Decal.Adapter;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The lights the dats give the world's placed objects outdoors (lamp posts, braziers), which the game doesn't light
    /// with: Hebian-to's lamp posts glow, but neither of its light lists had them (seen in the game, November 2026). A
    /// placed object (a landblock's LandblockInfo, cell dat 0xXXYYFFFE: its Objects and Buildings) is a Setup, and a
    /// Setup carries Lights: a place in the model, a colour, an intensity and a falloff, as the game's own lamps have.
    /// So for the 3x3 landblocks round you each is turned and placed as its object, and joins the lamp pass's lights
    /// (with LampLightOptions.FireLight), unless the game already has a light there. Read once a landblock, on the
    /// game's thread (the dat readers aren't safe on another), Setups' lights kept by id. Surfaces painted with fire
    /// (a fireplace's embers) are lights too (PlacedLights.Fires.cs). Untested in the game when written.
    /// </summary>
    internal static partial class PlacedLights {
        private static readonly Dictionary<uint, List<PlacedLight>> byBlock = new Dictionary<uint, List<PlacedLight>>();
        private static readonly Dictionary<uint, SetupModel> setups = new Dictionary<uint, SetupModel>();
        private static bool failed;
        private static int loggedBlocks;

        /// <summary>The placed objects' lights round your landblock (0xXXYY), added to the game's.</summary>
        public static void AddTo(List<GameLight> lights, uint landblock) {
            if (failed || DrawDistance.QuietNow) {
                return;
            }

            try {
                var x0 = (int)(landblock >> 8);
                var y0 = (int)(landblock & 0xFF);
                var seconds = (System.Environment.TickCount & 0x3FFFFFFF) / 1000f;

                for (var dx = -1; dx <= 1; dx++) {
                    for (var dy = -1; dy <= 1; dy++) {
                        var x = x0 + dx;
                        var y = y0 + dy;

                        if (x < 0 || y < 0 || x > 0xFE || y > 0xFE) {
                            continue;
                        }

                        LightsOf((uint)((x << 8) | y));
                    }
                }

                // the blocks' indoor cells, a few a frame, into the lists just made
                ReadSomeCells();

                for (var dx = -1; dx <= 1; dx++) {
                    for (var dy = -1; dy <= 1; dy++) {
                        var x = x0 + dx;
                        var y = y0 + dy;

                        if (x < 0 || y < 0 || x > 0xFE || y > 0xFE) {
                            continue;
                        }

                        foreach (var l in LightsOf((uint)((x << 8) | y))) {
                            var (east, north) = RenderOrigin.InFrame(l.Block, l.X, l.Y);

                            if (GameHasOne(lights, east, north, l.Z)) {
                                continue;
                            }

                            var flicker = l.Fire ? FireLights.Flicker(l.Seed, seconds) : 1;
                            lights.Add(new GameLight {
                                Type = 0, Cell = l.Block << 16, East = east, North = north, Height = l.Z,
                                Red = l.Fire ? FireLights.Red * flicker : ((l.Colour >> 16) & 0xFF) / 255f,
                                Green = l.Fire ? FireLights.Green * flicker : ((l.Colour >> 8) & 0xFF) / 255f,
                                Blue = l.Fire ? FireLights.Blue * flicker : (l.Colour & 0xFF) / 255f,
                                Intensity = l.Intensity, Falloff = l.Falloff,
                            });
                        }
                    }
                }
            }
            catch (Exception ex) {
                failed = true;
                PluginCore.LogFile("[lamps] placed objects' lights stopped for the session: " + ex.Message);
            }
        }

        private static bool GameHasOne(List<GameLight> lights, float east, float north, float height) {
            foreach (var g in lights) {
                var dx = g.East - east;
                var dy = g.North - north;
                var dz = g.Height - height;

                if (dx * dx + dy * dy + dz * dz < 1) {
                    return true;
                }
            }

            return false;
        }

        private static List<PlacedLight> LightsOf(uint block) {
            if (byBlock.TryGetValue(block, out var known)) {
                return known;
            }

            var found = new List<PlacedLight>();
            byBlock[block] = found;
            var cells = UBService.CellDat;
            var portal = UBService.PortalDat;

            if (cells == null || portal == null) {
                byBlock.Remove(block);
                return found;
            }

            var info = cells.ReadFromDat<LandblockInfo>((block << 16) | 0xFFFE);

            if (info == null) {
                return found;
            }

            foreach (var placed in info.Objects) {
                AddLights(found, block, portal, placed.Id, placed.Frame);
                AddFires(found, block, portal, placed.Id, placed.Frame);
            }

            var fromObjects = found.Count;

            foreach (var building in info.Buildings) {
                AddLights(found, block, portal, building.ModelId, building.Frame);
                AddFires(found, block, portal, building.ModelId, building.Frame);
            }

            QueueCells(block, info.NumCells);

            if (found.Count > 0 && loggedBlocks++ < 5) {
                PluginCore.LogFile($"[lamps] {found.Count} lights of placed objects in landblock {block:X4}, {fromObjects} of them objects', the rest buildings'");

                foreach (var l in found.GetRange(0, Math.Min(8, found.Count))) {
                    PluginCore.LogFile($"[lamps]   at {l.X:0.0} {l.Y:0.0} {l.Z:0.0}, colour {l.Colour:X8}, "
                        + $"intensity {l.Intensity:0.##}, falloff {l.Falloff:0.#}{(l.Fire ? ", fire" : "")}");
                }
            }

            return found;
        }

        private static void AddLights(List<PlacedLight> found, uint block, DatDatabase portal, uint model,
            ACE.DatLoader.Entity.Frame frame) {
            if ((model >> 24) != 0x02) {
                return;
            }

            if (!setups.TryGetValue(model, out var setup)) {
                setup = portal.ReadFromDat<SetupModel>(model);
                setups[model] = setup;
            }

            if (setup?.Lights == null) {
                return;
            }

            foreach (var light in setup.Lights.Values) {
                // the light's place in the model turned by the object's quaternion (v + 2w(q x v) + 2 q x (q x v))
                var v = light.ViewerSpaceLocation.Origin;
                var q = frame.Orientation;
                var tx = 2 * (q.Y * v.Z - q.Z * v.Y);
                var ty = 2 * (q.Z * v.X - q.X * v.Z);
                var tz = 2 * (q.X * v.Y - q.Y * v.X);
                var x = v.X + q.W * tx + (q.Y * tz - q.Z * ty);
                var y = v.Y + q.W * ty + (q.Z * tx - q.X * tz);
                var z = v.Z + q.W * tz + (q.X * ty - q.Y * tx);
                found.Add(new PlacedLight {
                    Block = block, X = frame.Origin.X + x, Y = frame.Origin.Y + y, Z = frame.Origin.Z + z,
                    Colour = light.Color, Intensity = light.Intensity, Falloff = light.Falloff,
                });
            }
        }

        /// <summary>Let go at shutdown (a hot reload keeps every build's statics).</summary>
        public static void Clear() {
            byBlock.Clear();
            setups.Clear();
            ClearFires();
        }
    }
}
