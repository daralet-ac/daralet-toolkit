using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using Microsoft.DirectX.Direct3D;
using DxTexture = Microsoft.DirectX.Direct3D.Texture;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The far land's block pictures baked on threads of their own (the user's call, November 2026: a new area still
    /// took a while to load). Baked on the game's thread within 4 ms a frame, 3.9 seconds of work for 4,784 blocks took
    /// 10.2 seconds after a teleport, and the far land was the last thing in. A block's picture is pure arithmetic on
    /// tables made once at the start (the terrains' tiles, the alpha shapes) and its own heights and terrain, so up to
    /// Workers threads (below normal priority, leaving the game's own threads a core) make each picture, its smaller
    /// copies and their R5G6B5 pixels; the game's thread only copies finished ones into the atlases (Direct3D's calls
    /// stay on it), within UploadBudgetMs a frame. No more than MostInFlight blocks are out at once, so finished
    /// pictures never pile up in memory. A new set of blocks (a teleport, a device reset, the landscape stepping)
    /// raises bakeGeneration, and anything baked for an older one is dropped.
    /// </summary>
    internal sealed partial class FarLand {
        private const int MostInFlight = 192;
        private const double UploadBudgetMs = 4;

        private static readonly int Workers = Math.Max(1, Math.Min(3, Environment.ProcessorCount - 2));

        private readonly ConcurrentQueue<(uint Block, int Tier, LandBlock Land, int Generation)> bakeJobs =
            new ConcurrentQueue<(uint, int, LandBlock, int)>();

        private readonly ConcurrentQueue<(uint Block, int Tier, ushort[][] Levels, int Generation)> bakedBlocks =
            new ConcurrentQueue<(uint, int, ushort[][], int)>();

        /// <summary>Blocks handed to the workers and not yet copied in or dropped.</summary>
        private int bakesInFlight;

        private int workersRunning;
        private int bakeGeneration;
        private long bakeWorkTicks;
        private int bakeErrorLogged;

        /// <summary>Whether any block is still waiting to be baked, being baked, or waiting to be copied in.</summary>
        private bool BakesPending => toBake.Count > 0 || Volatile.Read(ref bakesInFlight) > 0;

        /// <summary>
        /// Every bake queued or under way forgotten (a new set of blocks, a teleport, a device reset): what the workers
        /// finish for it is dropped.
        /// </summary>
        private void ForgetBakes() {
            toBake.Clear();
            Interlocked.Increment(ref bakeGeneration);

            while (bakeJobs.TryDequeue(out _)) {
                Interlocked.Decrement(ref bakesInFlight);
            }

            while (bakedBlocks.TryDequeue(out _)) {
                Interlocked.Decrement(ref bakesInFlight);
            }
        }

        /// <summary>
        /// Each frame: more blocks handed to the workers, and those they've finished copied into the atlases, within the
        /// frame's budget.
        /// </summary>
        private void BakeSome(Device device) {
            if (atlases[MidTier] == null) {
                return;
            }

            var generation = Volatile.Read(ref bakeGeneration);

            while (toBake.Count > 0 && Volatile.Read(ref bakesInFlight) < MostInFlight) {
                var (block, tier) = toBake.Dequeue();

                if (!blocks.TryGetValue(block, out var land) || land.Heights == null) {
                    continue;
                }

                Interlocked.Increment(ref bakesInFlight);
                bakeJobs.Enqueue((block, tier, land, generation));
            }

            StartBakeWorkers();

            var clock = Stopwatch.StartNew();
            var thisFrame = 0;

            while (clock.Elapsed.TotalMilliseconds < UploadBudgetMs && bakedBlocks.TryDequeue(out var done)) {
                Interlocked.Decrement(ref bakesInFlight);

                if (done.Generation != generation || done.Levels == null) {
                    continue;
                }

                CopyIn(device, done.Block, done.Tier, done.Levels);
                thisFrame++;
            }

            bakeTimer.Add(thisFrame, clock.Elapsed);

            if (!BakesPending) {
                atlasReady = true;

                if (bakeTimer.Finish("[far-land] block pictures copied in:") is string line) {
                    var work = Interlocked.Exchange(ref bakeWorkTicks, 0) * 1000.0 / Stopwatch.Frequency;
                    PluginCore.LogFile($"{line}, baked in {work:0} ms of work on {Workers} threads");
                }
            }
        }

        private void StartBakeWorkers() {
            while (!bakeJobs.IsEmpty && Volatile.Read(ref workersRunning) < Workers) {
                Interlocked.Increment(ref workersRunning);
                var thread = new Thread(BakeWorker) {
                    IsBackground = true, Name = "Daralet far land pictures", Priority = ThreadPriority.BelowNormal,
                };
                thread.Start();
            }
        }

        /// <summary>A worker: blocks taken until none are left, each one's pictures made, or nothing for an old set.</summary>
        private void BakeWorker() {
            try {
                while (bakeJobs.TryDequeue(out var job)) {
                    ushort[][] levels = null;

                    if (job.Generation == Volatile.Read(ref bakeGeneration) && !disposed) {
                        try {
                            var clock = Stopwatch.StartNew();
                            levels = BakeLevels(job.Land, job.Tier);
                            Interlocked.Add(ref bakeWorkTicks, clock.ElapsedTicks);
                        }
                        catch (Exception ex) {
                            if (Interlocked.Exchange(ref bakeErrorLogged, 1) == 0) {
                                PluginCore.LogFile($"[far-land] baking {job.Block:X4} failed: {ex}");
                            }
                        }
                    }

                    // always an answer, so the count of blocks out comes back down
                    bakedBlocks.Enqueue((job.Block, job.Tier, levels, job.Generation));
                }
            }
            finally {
                Interlocked.Decrement(ref workersRunning);
            }
        }

        /// <summary>A block's picture in a tier and each smaller copy, in R5G6B5, ready to copy in (any thread).</summary>
        private ushort[][] BakeLevels(LandBlock land, int tier) {
            var levels = MipChainOf(BakePixels(land, tier), TierBlockPixels[tier]);
            var packed = new ushort[levels.Count][];

            for (var level = 0; level < levels.Count; level++) {
                var pixels = levels[level];
                var into = new ushort[pixels.Length];

                for (var i = 0; i < pixels.Length; i++) {
                    var p = pixels[i];
                    into[i] = (ushort)(((p >> 19) & 0x1F) << 11 | ((p >> 10) & 0x3F) << 5 | ((p >> 3) & 0x1F));
                }

                packed[level] = into;
            }

            return packed;
        }

        /// <summary>A baked block copied into its tier's atlas, level by level, through the staging texture.</summary>
        private void CopyIn(Device device, uint block, int tier, ushort[][] levels) {
            var blockPixels = TierBlockPixels[tier];
            var slot = SlotOf(block, tier);
            var slotX = slot / TierSlots[tier];
            var slotY = slot % TierSlots[tier];

            for (var level = 0; level < TierLevels[tier] && level < levels.Length; level++) {
                var size = blockPixels >> level;
                FillStaging(stagings[tier], level, levels[level], size);

                using (var source = stagings[tier].GetSurfaceLevel(level))
                using (var target = atlases[tier].GetSurfaceLevel(level)) {
                    device.UpdateSurface(source, new Rectangle(0, 0, size, size), target,
                        new Point(slotX * size, slotY * size));
                }
            }

            slotHolds[tier][slot] = block;

            if (bakesLogged[tier] < 2) {
                bakesLogged[tier]++;
                PluginCore.LogFile($"[far-land] baked {block:X4} {TierNames[tier]} in slot {slotX},{slotY}");
            }
        }

        /// <summary>A level of a staging texture filled from R5G6B5 pixels, row by row to its pitch.</summary>
        private static unsafe void FillStaging(DxTexture into, int level, ushort[] pixels, int size) {
            var stream = into.LockRectangle(level, LockFlags.None, out var pitch);

            try {
                var at = (byte*)stream.InternalDataPointer;

                fixed (ushort* from = pixels) {
                    for (var y = 0; y < size; y++) {
                        Buffer.MemoryCopy(from + y * size, at + y * pitch, pitch, size * 2);
                    }
                }
            }
            finally {
                into.UnlockRectangle(level);
            }
        }
    }
}
