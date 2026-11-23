using System;
using System.Collections.Generic;
using System.Diagnostics;
using DaraletPlayer.Feed;
using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The far scenery's placing, a slice a frame (the user's eye, November 2026: a jumpy hang at every landblock edge).
    /// Each build placed every far object of every block (about 182,000 billboards, 5,000 3D trees and 1,700 far
    /// buildings) in one frame: 123 ms at a crossing, and 1,358 ms the first time in an area, where each model's picture
    /// is made as it's first met. Now the blocks are placed as many a frame as fit in PlacingBudgetMs, the old billboards,
    /// trees and far buildings drawn meanwhile (they share one home, so they swap together, in FinishBillboards), and a
    /// new set of blocks (another crossing) starts the placing again.
    /// </summary>
    internal sealed partial class DrawDistance {
        private const double PlacingBudgetMs = 4;

        private bool placing;
        private List<uint> placeBlocks;
        private int placeAt;
        private uint placeHome;
        private bool placeShow;
        private int placeObjects;
        private Dictionary<(BillboardModel Model, bool Far), int> placeByModel;
        private Dictionary<FarTreeModel, List<FarTreeInstance>> placeFarByModel;
        private Dictionary<FarTreeModel, List<FarTreeInstance>> placeFarLandByModel;
        private readonly WorkTimer placeTimer = new WorkTimer();

        /// <summary>A placing started for the blocks worked out round pendingHome.</summary>
        private void BeginPlacing() {
            AbortPlacing();
            billboardsToBuild = false;
            usedModels.Clear();
            billboardPlaces.Clear();
            placeBlocks = billboardBlocks;
            placeAt = 0;
            placeHome = pendingHome;
            placeShow = Options.Billboards;
            placeObjects = 0;
            placeByModel = new Dictionary<(BillboardModel Model, bool Far), int>();
            placeFarByModel = new Dictionary<FarTreeModel, List<FarTreeInstance>>();
            placeFarLandByModel = new Dictionary<FarTreeModel, List<FarTreeInstance>>();
            farBuildingCount = 0;
            placing = true;
        }

        /// <summary>Each frame: as many blocks placed as fit in the budget, and the buffers built once all are.</summary>
        private void PlaceSome(Device device, ACE.DatLoader.DatDatabase portal) {
            var clock = Stopwatch.StartNew();
            var hx = (int)(placeHome >> 8);
            var hy = (int)(placeHome & 0xFF);
            var done = 0;

            while (placeAt < placeBlocks.Count && (done == 0 || clock.Elapsed.TotalMilliseconds < PlacingBudgetMs)) {
                PlaceBlock(placeBlocks[placeAt], hx, hy, device, portal);
                placeAt++;
                done++;
            }

            placeTimer.Add(done, clock.Elapsed);

            if (placeAt >= placeBlocks.Count) {
                placing = false;
                FinishBillboards(device, portal);
            }
        }

        /// <summary>One block's far objects: 3D trees, billboards and far buildings, each by its model.</summary>
        private void PlaceBlock(uint block, int hx, int hy, Device device, ACE.DatLoader.DatDatabase portal) {
            if (!blockScenery.TryGetValue(block, out var placed)) {
                return;
            }

            var offsetX = ((int)(block >> 8) - hx) * SceneryLayout.BlockSize;
            var offsetY = ((int)(block & 0xFF) - hy) * SceneryLayout.BlockSize;

            // stood on the ground as the game draws this block, in less detail the further out it is: on the true
            // ground, trees on a hump floated over the flattened ridge the game draws
            var ring = Math.Max(Math.Abs((int)(block >> 8) - hx), Math.Abs((int)(block & 0xFF) - hy));
            // past the game's landscape, on the far land, which is drawn in full detail (Ui/FarLand.cs)
            var beyond = ring > billboardLandscape;
            // and with the far land taking over the game's coarse rings, on its full-detail ground there too
            var step = beyond || FarLandTakesOver ? 1 : SceneryLayout.DetailStep(ring, patchedRadius);
            blockHeights.TryGetValue(block, out var heights);

            var farRing = !beyond && IsFarTreeRing(ring);

            // on the far land too, drawn in its pass with its camera: with a low landscape, far towns were gone
            if (Options.FarBuildings && ring > patchedRadius) {
                AddFarBuildings(block, step, heights, offsetX, offsetY, portal, device,
                    beyond ? placeFarLandByModel : placeFarByModel);
            }

            foreach (var p in placed) {
                if (farRing) {
                    // the game's own model, drawn by the plugin (DrawDistance.FarTrees.cs)
                    var tree = farTreeModels.Get(p.Model, portal, device);
                    usedModels.Add(p.Model);

                    if (tree == null) {
                        continue;
                    }

                    if (!placeFarByModel.TryGetValue(tree, out var copies)) {
                        copies = new List<FarTreeInstance>();
                        placeFarByModel[tree] = copies;
                    }

                    var treeZ = heights == null || step == 1 ? p.Z : SceneryLayout.Ground(block << 16, heights,
                        region.LandDefs.LandHeightTable, p.X, p.Y, step).Z;
                    copies.Add(FarTree(offsetX + p.X, offsetY + p.Y, treeZ, p.Yaw, p.Scale));
                    continue;
                }

                if (!placeShow) {
                    continue;
                }

                var model = billboardModels.Get(p.Model, portal, device);
                usedModels.Add(p.Model);

                if (model == null) {
                    continue;
                }

                var z = heights == null || step == 1 ? p.Z : SceneryLayout.Ground(block << 16, heights,
                    region.LandDefs.LandHeightTable, p.X, p.Y, step).Z;
                placeByModel.TryGetValue((model, beyond), out var count);
                placeByModel[(model, beyond)] = count + 1;
                billboardPlaces.Add((model, offsetX + p.X, offsetY + p.Y, z, p.Yaw, p.Scale, beyond));
                placeObjects++;
            }
        }

        /// <summary>A placing part way dropped (a new set of blocks, the billboards off, a teleport, a device reset).</summary>
        private void AbortPlacing() {
            placing = false;
            placeBlocks = null;
            placeByModel = null;
            placeFarByModel = null;
            placeFarLandByModel = null;
            billboardPlaces.Clear();
            placeTimer.Finish("");
        }
    }
}
