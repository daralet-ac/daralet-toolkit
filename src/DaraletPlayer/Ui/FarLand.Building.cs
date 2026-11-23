using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using DaraletPlayer.Feed;
using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The far land's buffers written a slice a frame (the user's eye, November 2026: "a very jumpy hang whenever I walk
    /// over a landblock boundary"). Each crossing rebuilt all 4,216 blocks' vertices in one frame, about 180 ms, though
    /// only 66 blocks were new. Now the blocks to write are listed in the buffers' order when a build starts, new
    /// buffers are made, and each frame writes as many blocks as fit in BuildBudgetMs (locking only the part still to
    /// write); the old buffers stay drawn (shifted to where you are, as ever) until the new ones are whole, then swap.
    /// A device reset, a teleport or the land being switched off drops a build part way (AbortBuild).
    /// </summary>
    internal sealed partial class FarLand {
        private const double BuildBudgetMs = 4;

        private VertexBuffer pendingVertices;
        private IndexBuffer pendingIndices;
        private List<(uint Block, LandBlock Land, int Tier, bool Near)> pendingOrder;
        private int pendingAt;
        private readonly int[,] pendingRuns = new int[2, TierCount];
        private HashSet<uint> pendingNear;
        private int pendingNearTriangles;
        private uint pendingHome;
        private bool pendingDefaultPool;
        private Stopwatch pendingClock;
        private readonly WorkTimer buildTimer = new WorkTimer();

        /// <summary>Whether a build is under way (its buffers being written).</summary>
        private bool Building => pendingOrder != null;

        /// <summary>
        /// A build started for the blocks wanted now: listed in the buffers' order (the near blocks, the game's coarse
        /// rings drawn with its camera, then the far ones, a run a tier within each), and the new buffers made.
        /// </summary>
        private void StartBuild(Device device) {
            AbortBuild();
            var order = new List<(uint Block, LandBlock Land, int Tier, bool Near)>();
            var near = new HashSet<uint>();
            System.Array.Clear(pendingRuns, 0, pendingRuns.Length);

            for (var pass = 0; pass < 2 * TierCount; pass++) {
                var nearPass = pass < TierCount;
                var tierPass = pass % TierCount;

                foreach (var kv in blocks) {
                    var land = kv.Value;

                    if (land.Heights == null || marginOnly.Contains(kv.Key) || wantedNear.Contains(kv.Key) != nearPass
                        || (drawnTier.TryGetValue(kv.Key, out var t) ? t : MidTier) != tierPass) {
                        continue;
                    }

                    order.Add((kv.Key, land, tierPass, nearPass));
                    pendingRuns[nearPass ? 0 : 1, tierPass] += IndicesABlock / 3;

                    if (nearPass) {
                        near.Add(kv.Key);
                    }
                }
            }

            if (order.Count == 0) {
                Clear();
                return;
            }

            // in the card's memory alone with Video memory only (ScenePool), let go before a device reset and built again
            pendingDefaultPool = ScenePool.Buffers == Pool.Default;
            pendingVertices = new VertexBuffer(typeof(LandVertex), order.Count * VerticesABlock, device, Usage.WriteOnly,
                Format, ScenePool.Buffers);
            pendingIndices = new IndexBuffer(typeof(int), order.Count * IndicesABlock, device, Usage.WriteOnly,
                ScenePool.Buffers);
            pendingNear = near;
            pendingNearTriangles = near.Count * IndicesABlock / 3;
            pendingHome = wantedHome;
            pendingOrder = order;
            pendingAt = 0;
            pendingClock = Stopwatch.StartNew();
        }

        /// <summary>Each frame: as many blocks written as fit in the budget, and the buffers swapped in once whole.</summary>
        private unsafe void ContinueBuild() {
            if (!Building) {
                return;
            }

            var clock = Stopwatch.StartNew();
            var table = region.LandDefs.LandHeightTable;
            var hx = (int)(pendingHome >> 8);
            var hy = (int)(pendingHome & 0xFF);
            var from = pendingAt;
            var left = pendingOrder.Count - from;
            var vertexSize = Marshal.SizeOf<LandVertex>();
            var vertexStream = pendingVertices.Lock(from * VerticesABlock * vertexSize, left * VerticesABlock * vertexSize,
                LockFlags.None);
            var indexStream = pendingIndices.Lock(from * IndicesABlock * sizeof(int), left * IndicesABlock * sizeof(int),
                LockFlags.None);
            var written = 0;

            try {
                var vertexAt = (LandVertex*)vertexStream.InternalDataPointer;
                var indexAt = (int*)indexStream.InternalDataPointer;

                while (pendingAt < pendingOrder.Count && (written == 0 || clock.Elapsed.TotalMilliseconds < BuildBudgetMs)) {
                    var (block, land, tier, _) = pendingOrder[pendingAt];
                    blockVertices.Clear();
                    blockIndices.Clear();
                    AddBlock(blockVertices, blockIndices, block, land, table, ((int)(block >> 8) - hx) * BlockSize,
                        ((int)(block & 0xFF) - hy) * BlockSize, tier);
                    var baseVertex = pendingAt * VerticesABlock;

                    for (var i = 0; i < VerticesABlock; i++) {
                        vertexAt[written * VerticesABlock + i] = blockVertices[i];
                    }

                    for (var i = 0; i < IndicesABlock; i++) {
                        indexAt[written * IndicesABlock + i] = baseVertex + blockIndices[i];
                    }

                    pendingAt++;
                    written++;
                }
            }
            finally {
                pendingVertices.Unlock();
                pendingIndices.Unlock();
            }

            buildTimer.Add(written, clock.Elapsed);

            if (pendingAt >= pendingOrder.Count) {
                FinishBuild();
            }
        }

        /// <summary>The new buffers in place of the old, and what's drawn from them with them.</summary>
        private void FinishBuild() {
            var count = pendingOrder.Count;
            vertices?.Dispose();
            indices?.Dispose();
            vertices = pendingVertices;
            indices = pendingIndices;
            buffersInDefaultPool = pendingDefaultPool;
            vertexCount = count * VerticesABlock;
            triangleCount = count * IndicesABlock / 3;
            builtHome = pendingHome;
            builtNear = pendingNear;
            nearTriangleCount = pendingNearTriangles;
            System.Array.Copy(pendingRuns, runTriangles, pendingRuns.Length);
            pendingVertices = null;
            pendingIndices = null;
            pendingOrder = null;
            var timing = buildTimer.Finish("written") ?? "";
            PluginCore.LogFile($"[far-land] {(atlasReady ? "textured" : "untextured")}: built {count} blocks "
                + $"({builtNear.Count} near; by tier {runTriangles[0, 0] + runTriangles[1, 0]}, "
                + $"{runTriangles[0, 1] + runTriangles[1, 1]}, {runTriangles[0, 2] + runTriangles[1, 2]} triangles) "
                + $"round {builtHome:X4}: {triangleCount} triangles, {MegabytesHeld:0.0} MB, {timing}, "
                + $"{pendingClock.ElapsedMilliseconds} ms from start, {DrawDistance.FreeMegabytes()} MB free");
            TeleportTimeline.Note("far land");

            // the loading bar waits for it with its pictures: built without, it's drawn in plain colours till they're in
            if (atlasReady) {
                LoadPieces.Ready(LoadPieces.FarLand);
            }
        }

        /// <summary>A build part way dropped, its buffers let go (a device reset loses them anyway).</summary>
        private void AbortBuild() {
            pendingVertices?.Dispose();
            pendingVertices = null;
            pendingIndices?.Dispose();
            pendingIndices = null;
            pendingOrder = null;
        }
    }
}
