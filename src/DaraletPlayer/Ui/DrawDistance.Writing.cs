using System;
using System.Collections.Generic;
using System.Diagnostics;
using DaraletPlayer.Feed;
using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The billboards' vertex buffer written a slice a frame, once their placing is done (DrawDistance.Placing.cs). It was
    /// written whole in the frame the placing ended: 258,000 billboards, about 99 MB, written in 430 ms, a hang at every
    /// landblock crossed (the user's "lag spikes while running around", November 2026: 450 ms frames in the frame rate log
    /// at each `[billboards]` build line, and the far land, grass and placing all within their budgets). Now the billboards
    /// are sorted into their models' runs first (a counting sort by run, so each slice is one stretch of the buffer, locked
    /// alone), and written as many a frame as fit in WritingBudgetMs, the old billboards, 3D trees and far buildings drawn
    /// till the new are whole (they share one home, so they swap together, in SwapBillboards).
    /// </summary>
    internal sealed partial class DrawDistance {
        private const double WritingBudgetMs = 4;

        /// <summary>How many billboards are written between looks at the clock.</summary>
        private const int WritesBetweenLooks = 256;

        private bool writing;
        private VertexBuffer writeBuffer;
        private int writeAt;
        private int writeTotal;
        private int writeFarLandCount;
        private string writePlacingLine;
        private readonly WorkTimer writeTimer = new WorkTimer();
        private readonly List<(Texture Texture, int Start, int Triangles)> writeBatches =
            new List<(Texture, int, int)>();
        private readonly List<(Texture Texture, int Start, int Triangles)> writeFarBatches =
            new List<(Texture, int, int)>();

        /// <summary>
        /// The billboards in the buffer's order, kept and reused (a large object; a new one each crossing is what a 32-bit
        /// game doesn't easily get back).
        /// </summary>
        private (int Run, float X, float Y, float Z, float Yaw, float Scale)[] writeOrder =
            new (int, float, float, float, float, float)[0];

        /// <summary>Every block placed: the runs laid out, the billboards sorted into them, and the buffer made.</summary>
        private void FinishPlacing(Device device) {
            writePlacingLine = "placing " + placeTimer.Finish("");

            if (placeObjects == 0) {
                SwapBillboards(device, null, 0);
                return;
            }

            // each model's run in the buffer, the game's camera's first, then the far land's; starts in billboards
            var runStart = new int[placeRuns.Count];
            var start = 0;
            writeBatches.Clear();
            writeFarBatches.Clear();
            writeFarLandCount = 0;

            foreach (var far in new[] { false, true }) {
                for (var run = 0; run < placeRuns.Count; run++) {
                    if (placeRuns[run].Far != far) {
                        continue;
                    }

                    var count = placeRunCounts[run];
                    runStart[run] = start;
                    (far ? writeFarBatches : writeBatches).Add((placeRuns[run].Model.Texture, start * VerticesABillboard,
                        count * VerticesABillboard / 3));
                    start += count;
                    writeFarLandCount += far ? count : 0;
                }
            }

            if (writeOrder.Length < start || writeOrder.Length > 50000 && start < writeOrder.Length / 2) {
                writeOrder = new (int, float, float, float, float, float)[start];
            }

            foreach (var place in billboardPlaces) {
                writeOrder[runStart[place.Run]++] = place;
            }

            billboardPlaces.Clear();

            // a list grown past its use keeps its room: let a big one go, so a far view seen once isn't held for good
            if (billboardPlaces.Capacity > 50000 && start < billboardPlaces.Capacity / 2) {
                billboardPlaces.Capacity = 0;
            }

            writeBuffer = new VertexBuffer(typeof(CustomVertex.PositionNormalTextured), start * VerticesABillboard, device,
                Usage.WriteOnly, BillboardFormat, ScenePool.Buffers);
            writeTotal = start;
            writeAt = 0;
            writing = true;
        }

        /// <summary>Each frame: as many billboards written as fit in the budget, one stretch of the buffer locked.</summary>
        private unsafe void WriteSome(Device device) {
            var clock = Stopwatch.StartNew();
            var from = writeAt;
            var stride = VerticesABillboard * sizeof(CustomVertex.PositionNormalTextured);
            var stream = writeBuffer.Lock(from * stride, (writeTotal - from) * stride, LockFlags.None);

            try {
                var vertices = (CustomVertex.PositionNormalTextured*)stream.InternalDataPointer;

                while (writeAt < writeTotal) {
                    var end = Math.Min(writeTotal, writeAt + WritesBetweenLooks);

                    for (; writeAt < end; writeAt++) {
                        var (run, x, y, z, yaw, scale) = writeOrder[writeAt];
                        WriteCrossedQuads(vertices + (writeAt - from) * VerticesABillboard, placeRuns[run].Model, x, y, z,
                            yaw, scale);
                    }

                    if (clock.Elapsed.TotalMilliseconds >= WritingBudgetMs) {
                        break;
                    }
                }
            }
            finally {
                writeBuffer.Unlock();
            }

            writeTimer.Add(writeAt - from, clock.Elapsed);

            if (writeAt >= writeTotal) {
                var buffer = writeBuffer;
                writeBuffer = null;
                writing = false;
                SwapBillboards(device, buffer, writeTotal);
            }
        }

        /// <summary>
        /// The new far scenery in place of the old: the old billboards, trees and far buildings let go, the 3D trees'
        /// instances built, and the written buffer and its runs taken on.
        /// </summary>
        private void SwapBillboards(Device device, VertexBuffer buffer, int objects) {
            // how long each step takes, in the log line, to see which holds the game up at a landblock crossing
            var stepClock = Stopwatch.StartNew();
            var steps = new List<string> { writePlacingLine };
            ClearBillboards();
            billboardHome = placeHome;
            BuildFarTrees(device, placeFarByModel, placeFarLandByModel);
            placeByModel = null;
            placeFarByModel = null;
            placeFarLandByModel = null;
            placeBlocks = null;
            steps.Add($"3D trees {stepClock.ElapsedMilliseconds} ms");
            ForgetScenerySeen();
            var modelsGone = billboardModels.KeepOnly(usedModels) + farTreeModels.KeepOnly(usedModels);

            if (modelsGone > 0) {
                PluginCore.LogFile($"[billboards] {modelsGone} models no longer in sight let go");
            }

            if (buffer == null) {
                billboardPlaces.Clear();
                PluginCore.LogFile("[billboards] nothing to draw");
                LoadPieces.Ready(LoadPieces.Trees);
                return;
            }

            billboardBuffer = buffer;
            billboardBatches.AddRange(writeBatches);
            farLandBatches.AddRange(writeFarBatches);
            writeBatches.Clear();
            writeFarBatches.Clear();
            farLandBillboardCount = writeFarLandCount;
            billboardCount = objects;
            steps.Add("writing " + writeTimer.Finish(""));

            if (farBuildingCount > 0) {
                PluginCore.LogFile($"[far-buildings] {farBuildingCount} buildings and placed objects past ring "
                    + $"{patchedRadius}");
            }

            PluginCore.LogFile($"[billboards] {objects} billboards ({farLandBillboardCount} on the far land) in "
                + $"{billboardBatches.Count + farLandBatches.Count} runs, "
                + $"{objects * VerticesABillboard / 3} triangles, {FreeMegabytes()} MB free, "
                + $"plugin {GC.GetTotalMemory(false) >> 20} MB, {blockScenery.Count} blocks' scenery kept; "
                + string.Join(", ", steps));
            TeleportTimeline.Note("billboards");
            LoadPieces.Ready(LoadPieces.Trees);
        }

        /// <summary>A write part way dropped (a new set of blocks, the billboards off, a teleport, a device reset).</summary>
        private void AbortWriting() {
            if (!writing && writeBuffer == null) {
                return;
            }

            writing = false;
            writeBuffer?.Dispose();
            writeBuffer = null;
            writeBatches.Clear();
            writeFarBatches.Clear();
            writeTimer.Finish("");
            placeFarByModel = null;
            placeFarLandByModel = null;
        }
    }
}
