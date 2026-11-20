using System;
using System.Collections.Generic;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using DaraletPlayer.Feed;
using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Far buildings (DrawDistanceOptions.FarBuildings; the user's call, November 2026: a town two landblocks off vanished
    /// with a step back). The game makes a landblock's buildings and the dats' placed objects (walls, statues, signs) only
    /// while the block is in full detail (CLandBlock::init_buildings and init_static_objs, from grab_visible_cells), as it
    /// does its trees. Past that ring they're drawn here with the far trees' machinery (DrawDistance.FarTrees.cs, the
    /// game's own models, one instanced draw a model), from each block's LandblockInfo (cell dat 0xXXYYFFFE: Buildings,
    /// each a model and a frame, and Objects, the placed ones), out to the landscape's edge. Each is turned only about the
    /// vertical (yaw from its quaternion, as the far trees turn: x' = x cos - y sin), and stood on the ground as the game
    /// draws it there, raised or lowered by how far the coarse ground is from the true ground under its origin. Models under
    /// BillboardModels.MinHeight (small clutter) are left out, as for trees. Untested in the game when written.
    /// </summary>
    internal sealed partial class DrawDistance {
        private readonly Dictionary<uint, List<(uint Model, float X, float Y, float Z, float Yaw)>> blockBuildings =
            new Dictionary<uint, List<(uint, float, float, float, float)>>();

        private int farBuildingCount;

        /// <summary>A block's buildings and placed objects, in its own meters.</summary>
        private static List<(uint Model, float X, float Y, float Z, float Yaw)> BuildingsOf(DatDatabase cells,
            uint landblock) {
            var found = new List<(uint, float, float, float, float)>();
            var info = cells.ReadFromDat<LandblockInfo>((landblock & 0xFFFF0000) | 0xFFFE);

            if (info == null) {
                return found;
            }

            foreach (var building in info.Buildings) {
                found.Add(Placed(building.ModelId, building.Frame));
            }

            foreach (var placed in info.Objects) {
                found.Add(Placed(placed.Id, placed.Frame));
            }

            return found;
        }

        private static (uint, float, float, float, float) Placed(uint model, ACE.DatLoader.Entity.Frame frame) {
            var q = frame.Orientation;
            var yaw = (float)(2 * Math.Atan2(q.Z, q.W));
            return (model, frame.Origin.X, frame.Origin.Y, frame.Origin.Z, yaw);
        }

        /// <summary>One block's buildings into the far trees' instances.</summary>
        private void AddFarBuildings(uint block, int step, IReadOnlyList<byte> heights, float offsetX, float offsetY,
            DatDatabase portal, Device device, Dictionary<FarTreeModel, List<FarTreeInstance>> byModel) {
            if (!blockBuildings.TryGetValue(block, out var buildings)) {
                return;
            }

            foreach (var b in buildings) {
                var model = farTreeModels.Get(b.Model, portal, device);
                usedModels.Add(b.Model);

                if (model == null) {
                    continue;
                }

                var z = b.Z;

                if (heights != null && step > 1) {
                    var table = region.LandDefs.LandHeightTable;
                    z += SceneryLayout.Ground(block << 16, heights, table, b.X, b.Y, step).Z
                        - SceneryLayout.Ground(block << 16, heights, table, b.X, b.Y, 1).Z;
                }

                if (!byModel.TryGetValue(model, out var copies)) {
                    copies = new List<FarTreeInstance>();
                    byModel[model] = copies;
                }

                copies.Add(FarTree(offsetX + b.X, offsetY + b.Y, z, b.Yaw, 1));
                farBuildingCount++;
            }
        }
    }
}
