using System;
using ACE.DatLoader.FileTypes;
using DaraletPlayer.Feed;
using Decal.Adapter;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The test step gohighest N: a teleport to the highest ground within N landblocks of you, from the cell dat's heights
    /// through the region's height table (the user's call, November 2026: mountain tops for retail and plugin shots,
    /// which the map's places don't name). The place is a terrain vertex; you land a meter over it.
    /// </summary>
    internal sealed partial class TestRunner {
        private void GoHighest(int radius) {
            var cells = UBService.CellDat;
            var region = UBService.PortalDat?.ReadFromDat<RegionDesc>(0x13000000);

            if (cells == null || region == null) {
                Result("gohighest: no dats");
                return;
            }

            var table = region.LandDefs.LandHeightTable;
            var home = unchecked((uint)CoreManager.Current.Actions.Landcell) >> 16;
            var hx = (int)(home >> 8);
            var hy = (int)(home & 0xFF);
            var best = float.MinValue;
            uint bestBlock = 0;
            var bestX = 0;
            var bestY = 0;

            for (var dx = -radius; dx <= radius; dx++) {
                for (var dy = -radius; dy <= radius; dy++) {
                    var bx = hx + dx;
                    var by = hy + dy;

                    if (bx < 0 || by < 0 || bx > 0xFE || by > 0xFE) {
                        continue;
                    }

                    var block = (uint)(bx << 8 | by);
                    var land = cells.ReadFromDat<CellLandblock>(block << 16 | 0xFFFF);

                    if (land == null || land.Height.Count < 81) {
                        continue;
                    }

                    // the block's own vertices, not its edges, which belong as much to the next
                    for (var x = 1; x < 8; x++) {
                        for (var y = 1; y < 8; y++) {
                            var z = table[land.Height[x * 9 + y]];

                            if (z > best) {
                                best = z;
                                bestBlock = block;
                                bestX = x;
                                bestY = y;
                            }
                        }
                    }
                }
            }

            if (best == float.MinValue) {
                Result("gohighest: nothing found");
                return;
            }

            // the outdoor cell the vertex is the south-west corner of, numbered from 1, x major
            var cell = bestBlock << 16 | (uint)(1 + bestX * 8 + bestY);
            var place = $"0x{cell:X8} [{bestX * 24f:0.000} {bestY * 24f:0.000} {best + 1:0.000}] 1 0 0 0";
            Result($"gohighest {radius}: {best:0} m at {place}");
            noteChatUntil = DateTime.Now.AddSeconds(2);
            CoreManager.Current.Actions.InvokeChatParser("/teleloc " + place);
        }
    }
}
