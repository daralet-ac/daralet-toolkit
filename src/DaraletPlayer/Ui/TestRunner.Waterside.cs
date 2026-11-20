using System;
using System.Collections.Generic;
using ACE.DatLoader.FileTypes;
using Decal.Adapter;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The test step gowaterside N: a teleport to dry ground at the edge of a broad stretch of water within N landblocks,
    /// facing across it (the user's call, November 2026: a river bank looking out over the water, for retail and plugin
    /// shots). Water is the terrain types 0x10 to 0x14; a bank vertex is dry with water next to it, scored by how much
    /// water lies within four vertices (96 m), the nearest of the best taken.
    /// </summary>
    internal sealed partial class TestRunner {
        /// <summary>The heading gowaterside found, for the step facesaved once the teleport's done.</summary>
        private double pendingFace;

        private void GoWaterside(int radius) {
            var cells = UBService.CellDat;
            var region = UBService.PortalDat?.ReadFromDat<RegionDesc>(0x13000000);

            if (cells == null || region == null) {
                Result("gowaterside: no dats");
                return;
            }

            var table = region.LandDefs.LandHeightTable;
            var home = unchecked((uint)CoreManager.Current.Actions.Landcell) >> 16;
            var hx = (int)(home >> 8);
            var hy = (int)(home & 0xFF);
            var side = (radius * 2 + 1) * 8 + 1;
            var wet = new bool[side, side];
            var height = new float[side, side];
            var known = new bool[side, side];

            for (var dx = -radius; dx <= radius; dx++) {
                for (var dy = -radius; dy <= radius; dy++) {
                    var bx = hx + dx;
                    var by = hy + dy;

                    if (bx < 0 || by < 0 || bx > 0xFE || by > 0xFE) {
                        continue;
                    }

                    var land = cells.ReadFromDat<CellLandblock>((uint)(bx << 8 | by) << 16 | 0xFFFF);

                    if (land == null || land.Terrain.Count < 81) {
                        continue;
                    }

                    for (var x = 0; x < 9; x++) {
                        for (var y = 0; y < 9; y++) {
                            var gx = (dx + radius) * 8 + x;
                            var gy = (dy + radius) * 8 + y;
                            var type = (land.Terrain[x * 9 + y] >> 2) & 0x1F;
                            wet[gx, gy] = type >= 0x10 && type <= 0x14;
                            height[gx, gy] = table[land.Height[x * 9 + y]];
                            known[gx, gy] = true;
                        }
                    }
                }
            }

            var you = (X: radius * 8 + CoreManager.Current.Actions.LocationX / 24,
                Y: radius * 8 + CoreManager.Current.Actions.LocationY / 24);
            var best = (Score: -1.0, X: 0, Y: 0, WaterX: 0.0, WaterY: 0.0);

            for (var x = 4; x < side - 4; x++) {
                for (var y = 4; y < side - 4; y++) {
                    if (!known[x, y] || wet[x, y] || !(wet[x + 1, y] || wet[x - 1, y] || wet[x, y + 1] || wet[x, y - 1])) {
                        continue;
                    }

                    double count = 0, sx = 0, sy = 0;

                    for (var u = -4; u <= 4; u++) {
                        for (var v = -4; v <= 4; v++) {
                            if (wet[x + u, y + v]) {
                                count++;
                                sx += u;
                                sy += v;
                            }
                        }
                    }

                    // most water, then nearest you
                    var far = Math.Sqrt((x - you.X) * (x - you.X) + (y - you.Y) * (y - you.Y));
                    var score = count * 10 - far;

                    if (count >= 30 && score > best.Score) {
                        best = (score, x, y, sx / count, sy / count);
                    }
                }
            }

            if (best.Score < 0) {
                Result($"gowaterside {radius}: no broad water found");
                return;
            }

            var blockX = hx - radius + best.X / 8;
            var blockY = hy - radius + best.Y / 8;
            var vx = best.X % 8;
            var vy = best.Y % 8;
            var cell = (uint)(blockX << 8 | blockY) << 16 | (uint)(1 + vx * 8 + vy);
            var place = $"0x{cell:X8} [{vx * 24f:0.000} {vy * 24f:0.000} {height[best.X, best.Y] + 1:0.000}] 1 0 0 0";
            // toward the water: 0 north, 90 east, as the game's headings
            var heading = (Math.Atan2(best.WaterX, best.WaterY) * 180 / Math.PI + 360) % 360;
            pendingFace = heading;
            Result($"gowaterside {radius}: {place}, facing {heading:0}");
            noteChatUntil = DateTime.Now.AddSeconds(2);
            CoreManager.Current.Actions.InvokeChatParser("/teleloc " + place);
        }
    }
}
