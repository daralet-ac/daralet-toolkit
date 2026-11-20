using System;
using System.Runtime.InteropServices;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's address space watched while the draw distance is raised. acclient is a 32-bit program (large address
    /// aware, so 4 GB on 64-bit Windows), and doesn't check its allocations: a teleport with the landscape at 24
    /// landblocks and scenery at 5 crashed it in CPolygon::make_plane (0x539153) reading a vertex through a null array,
    /// building a new block's polygons (November 2026; out of memory is the likely reason, not confirmed). So:
    /// <para>
    /// With under 512 MB free, the landscape and scenery go back to the game's own (memoryCapped), and chat says so.
    /// What was wanted then is remembered (ranOutAt), and nothing as costly is tried again this session.
    /// </para>
    /// <para>
    /// A change that would draw more (by Cost, a rough count of landblocks of scenery) isn't made with under 1 GB free
    /// (raiseRefused): loading it could run out partway, before the once-a-second look above sees it. Seen in the game:
    /// scenery 10 at 20% density left about 1.5 GB, 16 at 20% about 0.8 GB, and 16 at 53% ran out.
    /// </para>
    /// Any change of the settings tries again (Soon), so turning something down is the way back. The free room goes
    /// in log.txt with every change of radius.
    /// </summary>
    internal sealed partial class DrawDistance {
        /// <summary>Less free address space than this and the far landscape and scenery go.</summary>
        private const long LowMemoryMegabytes = 512;

        /// <summary>Less than this and nothing that draws more is loaded.</summary>
        private const long RaiseMemoryMegabytes = 1400;

        /// <summary>
        /// The landscape's own cost next to a landblock of scenery: about 1 MB a block, measured in the game (November
        /// 2026: landscape 21 to 24, 552 more blocks, took 564 MB). It was 0.02, a sixth of that, and the estimate let
        /// landscape 24 with scenery 3 at 100% load at login, which left 765 MB free and crashed the game (0x58712F).
        /// </summary>
        private const double LandscapeBlockCost = 0.12;

        /// <summary>
        /// A block in full detail without its trees (Ground detail past Scenery), next to one with them: its 64 cells and
        /// finer ground, buildings and placed objects. Measured in the game (November 2026): Ground detail 4 to 8 with
        /// Scenery 4, 208 more blocks, took 336 MB, 1.6 MB a block.
        /// </summary>
        private const double GroundBlockCost = 0.2;

        /// <summary>
        /// About how much memory a unit of Cost takes, from the game (November 2026): scenery 5 to 16 landblocks at 20%
        /// density (cost 72 to 266) took about 1.3 GB, nearly 7 MB a unit; this is a little over, to be safe. The
        /// "loaded cost" lines in log.txt, each a few seconds after a load, say what each really took.
        /// </summary>
        private const double MegabytesPerCost = 8;

        /// <summary>
        /// The most the learned rate goes to: 8 at first, but the full-detail rings at Drudge Temple took 12 to 15 MB a unit,
        /// the guard counted 8, and the game was left too little for a teleport (November 2026).
        /// </summary>
        private const double MaxLearnedPerCost = 16;

        /// <summary>
        /// The MB a unit the guard goes by: MegabytesPerCost at first, then learned from each load up of 10 units or more
        /// (its use a quarter over what was measured, half old and half new, between 2 and MaxLearnedPerCost). The fixed 8 dated from
        /// before the billboards were made lean: after it, a step up measured 1.8 to 2.4 MB a unit while the guard
        /// counted 8, and refused at Holtburg settings that fit with a gigabyte to spare (November 2026).
        /// </summary>
        private double megabytesPerCost = MegabytesPerCost;

        /// <summary>
        /// The costliest settings loaded since the last teleport (or start): going back up to them needs little more
        /// memory, the game keeping what they read. Forgotten in portal space, the next place's being another matter.
        /// </summary>
        private double highestCostHere;

        /// <summary>Why the game's cache is to be trimmed once loading has settled, or null (GameCache).</summary>
        private string cacheFlushWanted;

        private DateTime lastCacheFlush = DateTime.MinValue;

        /// <summary>Whether the game's cache is trimmed by itself (off; the test step flushcache still does it).</summary>
        public static bool AutoFlushCache;

        /// <summary>
        /// The game's free objects let go (GameCache) once a load has settled, after a teleport or the draw distance
        /// lowered, or when memory runs low; at most once every 30 seconds, never in portal space or mid-load.
        /// </summary>
        private void MaybeFlushCache() {
            // off: it never gave back a megabyte, and a teleport 8 seconds after one crashed the game in AnimData::UnPack
            // (0x526A66), unpacking data already let go, the same function as an earlier crash (0x526A8A; November 2026)
            if (!AutoFlushCache) {
                cacheFlushWanted = null;
                return;
            }

            var now = DateTime.UtcNow;

            if (cacheFlushWanted == null && FreeMegabytes() is var free && free >= 0
                && free < HeadroomMegabytes + 200 && (now - lastCacheFlush).TotalSeconds >= 60) {
                cacheFlushWanted = "memory low";
            }

            if (cacheFlushWanted == null || inPortalSpace || !LoadSettled() || (now - lastCacheFlush).TotalSeconds < 30) {
                return;
            }

            try {
                GameCache.FlushFree(cacheFlushWanted);
            }
            catch (Exception ex) {
                PluginCore.LogFile("[cache] " + ex.Message);
            }

            cacheFlushWanted = null;
            lastCacheFlush = now;
        }

        /// <summary>
        /// What has to be left free after a load, by the estimate: the game needs room for everything else, and a teleport
        /// loads a whole new place. It was 768: with 1.1 GB free at Holtburg (Scenery 2, Ground 6, Landscape 18) a teleport
        /// to Drudge Cave ran the game out of memory (November 2026). 1.4 GB then; 1.1 GB, once a teleport let go of
        /// the far scenery and the full-detail rings, crashed a teleport from Drudge Temple with 1.19 GB free.
        /// </summary>
        private const long HeadroomMegabytes = 1400;

        /// <summary>When to note what the last load took (5 seconds after it), and what was free just before it.</summary>
        private DateTime measureLoadAt = DateTime.MaxValue;
        private long freeBeforeLoad;
        private double costBeforeLoad;

        private bool memoryCapped;
        private bool memoryWarned;
        private long cappedFree;

        /// <summary>Why the last raise wasn't made, or null.</summary>
        private string raiseRefused;

        /// <summary>The Cost of what was wanted when memory ran out; nothing as costly is tried again. 0 for never.</summary>
        private double ranOutAt;

        /// <summary>The Cost of the scenery and landscape loaded now: none until the first is.</summary>
        private double loadedCost;

        /// <summary>
        /// The scenery, density and landscape settings loaded now, and the ones loaded before them: when memory runs
        /// out, the Options page offers to go back to those before (FallBack).
        /// </summary>
        private (int Scenery, int Density, int Landscape)? loadedSettings;
        private (int Scenery, int Density, int Landscape)? settingsBefore;

        /// <summary>
        /// The settings (scenery, density, landscape) to go back to after running out of memory: the last loaded before
        /// the ones that ran out. Null with nothing to offer (memory hasn't run out, or nothing loaded before).
        /// </summary>
        public (int Scenery, int Density, int Landscape)? FallBack => MemoryProblem != null ? settingsBefore : null;

        /// <summary>The far settings now loaded, noted, so the ones before can be gone back to.</summary>
        private void NoteLoaded(int scenery, int density, int landscape, double cost) {
            var now = (scenery, density, landscape);

            if (loadedSettings != now) {
                settingsBefore = loadedSettings;
                loadedSettings = now;
            }

            loadedCost = cost;
        }

        /// <summary>Megabytes of the game's address space still free, or -1 if Windows won't say.</summary>
        internal static long FreeMegabytes() {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            return GlobalMemoryStatusEx(ref status) ? (long)(status.AvailVirtual / (1024 * 1024)) : -1;
        }

        /// <summary>
        /// Roughly how much a setting costs, in landblocks of full scenery: the blocks with scenery at their density,
        /// and the landscape's blocks at a fraction of one each.
        /// </summary>
        private static double Cost(int sceneryRadius, int densityPercent, int groundRadius, int landscapeRadius) {
            var scenery = 2.0 * sceneryRadius + 1;
            var ground = 2.0 * Math.Max(sceneryRadius, groundRadius) + 1;
            var land = 2.0 * landscapeRadius + 1;
            return scenery * scenery * densityPercent / 100 + (ground * ground - scenery * scenery) * GroundBlockCost
                + land * land * LandscapeBlockCost;
        }

        /// <summary>The memory message for the Options page, or null.</summary>
        private string MemoryProblem => memoryCapped
            ? $"The game ran low on memory ({cappedFree} MB left), so landscape and scenery are back to the game's own. "
                + "Turn Scenery, Scenery density or Landscape down to try again; restarting the game frees memory."
            : raiseRefused;

        /// <summary>Whether the far landscape and scenery have to go for want of memory; looked at once a second.</summary>
        private bool LowOnMemory(bool on, double wanted) {
            if (!on) {
                memoryCapped = false;
                memoryWarned = false;
                return false;
            }

            if (memoryCapped) {
                return true;
            }

            var free = FreeMegabytes();

            if (free < 0) {
                return false;
            }

            if (!memoryWarned && free < RaiseMemoryMegabytes) {
                memoryWarned = true;
                PluginCore.LogFile($"[draw-distance] {free} MB of the game's address space left");
            }

            if (free >= LowMemoryMegabytes) {
                return false;
            }

            memoryCapped = true;
            cappedFree = free;
            ranOutAt = ranOutAt > 0 ? Math.Min(ranOutAt, wanted) : wanted;
            PluginCore.LogFile($"[draw-distance] only {free} MB left: landscape and scenery put back to the game's "
                + $"(cost {wanted:0} ran out)");
            PluginCore.Chat("Draw distance: the game is running low on memory, so the landscape and scenery are back to "
                + "the game's own. Turn something down to try again; restarting the game frees memory.");
            return true;
        }

        /// <summary>Whether settings costing this may be loaded now: always if no more than now, else if memory allows.</summary>
        private bool MayLoad(double wanted) {
            highestCostHere = Math.Max(highestCostHere, loadedCost);

            if (wanted <= loadedCost + 0.5) {
                raiseRefused = null;
                return true;
            }

            string why = null;
            var free = FreeMegabytes();

            // back up to settings already loaded here: the game kept what they read, so lowering freed nothing and going
            // back takes little more (November 2026: Balanced to Performance took free memory down, not up, and the
            // guard then refused Balanced again for want of memory it was in fact holding)
            if (wanted <= highestCostHere + 0.5 && free >= LowMemoryMegabytes + 256) {
                raiseRefused = null;
                return true;
            }

            if (ranOutAt > 0 && wanted >= ranOutAt * 0.9) {
                why = "That's about as much as ran the game out of memory before: turn Scenery, Scenery density or "
                    + "Landscape down. Restarting the game lets you try again.";
            }
            else if (free >= 0 && free < RaiseMemoryMegabytes) {
                why = $"Only {free} MB of the game's memory is left, too little to draw further safely: turn something "
                    + "else down first, or restart the game.";
            }
            else if (free >= 0 && free - NeedsMegabytes(wanted) < HeadroomMegabytes) {
                // at login nothing is loaded yet and there's plenty free, so only an estimate catches settings that
                // can never fit: saved at scenery 16 at 53%, they crashed the game as it logged in (November 2026)
                why = $"These settings would need about {NeedsMegabytes(wanted):0} MB more memory, and the game has "
                    + $"{free} MB free (it can use 4 GB at most): lower Scenery density, Scenery or Landscape.";
            }

            if (why != null && why != raiseRefused) {
                PluginCore.LogFile($"[draw-distance] not loading cost {wanted:0} (now {loadedCost:0}, {free} MB free, "
                    + $"about {NeedsMegabytes(wanted):0} MB more needed)");
            }

            raiseRefused = why;
            return why == null;
        }

        /// <summary>About how much more memory loading this would take than what's loaded now.</summary>
        private double NeedsMegabytes(double wanted) {
            return Math.Max(0, wanted - loadedCost) * megabytesPerCost;
        }

        /// <summary>A load is starting: what's free before it, so its cost can be measured a few seconds on.</summary>
        private void LoadStarting(double costBefore) {
            freeBeforeLoad = FreeMegabytes();
            costBeforeLoad = costBefore;
            measureLoadAt = DateTime.UtcNow.AddSeconds(5);
            lastLoadAt = DateTime.UtcNow;
        }

        /// <summary>
        /// What the last load took, in log.txt once it's had a few seconds, to check MegabytesPerCost against:
        /// "loaded cost 266 (from 72): 1890 MB free, from 2177, 1.5 MB a unit".
        /// </summary>
        private void MeasureLoad() {
            if (DateTime.UtcNow < measureLoadAt) {
                return;
            }

            measureLoadAt = DateTime.MaxValue;
            var free = FreeMegabytes();
            var units = loadedCost - costBeforeLoad;
            var rate = Math.Abs(units) >= 1 ? $", {(freeBeforeLoad - free) / units:0.0} MB a unit" : "";
            PluginCore.LogFile($"[draw-distance] loaded cost {loadedCost:0} (from {costBeforeLoad:0}): {free} MB free, "
                + $"from {freeBeforeLoad}{rate}");

            var used = freeBeforeLoad - free;

            if (units >= 10 && used > 0 && free >= 0) {
                var learned = Math.Max(2, Math.Min(MaxLearnedPerCost, used / units * 1.25));
                megabytesPerCost = (megabytesPerCost + learned) / 2;
                PluginCore.LogFile($"[draw-distance] the memory guard now counts {megabytesPerCost:0.0} MB a unit");
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatusEx {
            public uint Length;
            public uint MemoryLoad;
            public ulong TotalPhys;
            public ulong AvailPhys;
            public ulong TotalPageFile;
            public ulong AvailPageFile;
            public ulong TotalVirtual;
            public ulong AvailVirtual;
            public ulong AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);
    }
}
