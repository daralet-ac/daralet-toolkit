using System;
using System.Runtime.InteropServices;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's own object cache trimmed (November 2026: lowering the draw distance took free memory down, not up). The
    /// client keeps every data object it has let go of on a free list in its cache, in case it's wanted again, and only
    /// trims a list past its own limit and age (DBOCache::UseTime, 0x416AC0). DBCache::FlushFreeObjects (0x4144E0,
    /// thiscall on the DBCache at [0x837BAC], one argument; ret 4) empties them: with 0, every object cache in the
    /// DBCache's table (0x81860C), each through its own FlushFreeObjects (vtable +0x28, DBOCache's at 0x416B10). The game
    /// calls it so itself, three times in a clean-up routine (0x411BD0). Only objects nothing holds are on those lists,
    /// so anything wanted again is read again from the dats. `[cache]` lines in log.txt.
    /// </summary>
    internal static class GameCache {
        private const uint DBCachePointer = 0x837BAC;
        private const uint FlushFreeObjectsAddress = 0x4144E0;

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void FlushCall(IntPtr cache, uint objectType);

        private static FlushCall flush;

        /// <summary>Every free object the game keeps let go of; the MB of the game's address space it gave back.</summary>
        public static unsafe long FlushFree(string why) {
            var cache = *(IntPtr*)DBCachePointer;

            if (cache == IntPtr.Zero) {
                return 0;
            }

            flush = flush ?? Marshal.GetDelegateForFunctionPointer<FlushCall>((IntPtr)FlushFreeObjectsAddress);
            var before = DrawDistance.FreeMegabytes();
            flush(cache, 0);
            var after = DrawDistance.FreeMegabytes();
            PluginCore.LogFile($"[cache] the game's free objects let go ({why}): {before} MB free before, {after} after");
            return after - before;
        }
    }
}
