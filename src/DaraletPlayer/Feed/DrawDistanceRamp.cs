using System;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Raising the draw distance a step at a time (Ui/DrawDistance.cs). Loading a lot the game has never read crashed it,
    /// with memory to spare: at login, on a teleport, and on going from scenery 1 straight to 16, each time in its own
    /// code reading the dats (CCellStruct::UnPack's CPolygon::UnPack at 0x5393C6 and 0x5393E4 running past its data,
    /// CPolygon::make_plane at 0x539153; November 2026). Raised 5, 7, 10, 15, 16, a step every few seconds, it held:
    /// each step only reads one more ring, the rest still in the game's cache. So a raise goes up a few rings a step,
    /// and a lowering goes all the way at once. Kept apart so it can be tested.
    /// </summary>
    public static class DrawDistanceRamp {
        /// <summary>
        /// Scenery rings added a step. 2 every 4 seconds held; the user found 1 to 13 in 25 seconds too slow, and 3 a
        /// second crashed the game at login on the step from 10 to 13 (288 blocks of trees, a second after the last
        /// step): "OUT OF MEMORY: Failed to allocate -1178753584 bytes", a size gone wrong, likely one load running into
        /// the last (November 2026). So 2, as soon as the last step's loading is done (MayStep); then 1, since scenery 1 to 3
        /// with landscape 15 to 18 in the same step crashed the game in make_plane (0x539153) three times (twice at login,
        /// once mid-session), a couple of seconds after it.
        /// </summary>
        public const int SceneryStep = 1;

        /// <summary>
        /// Landscape rings added a step: all of them, in one reload. It was 3, but each step is a whole reload, and the fifth
        /// in 12 seconds (5 to 18 after a teleport) crashed the game at 0x58712F, in the game's emptying of a hash table it
        /// had left inconsistent, with 2.4 GB free (November 2026); every crash there came after two reloads close together.
        /// </summary>
        public const int LandscapeStep = DrawDistanceOptions.MaxLandscapeRadius;

        /// <summary>
        /// One thing raised a step, never both: with the landscape and the scenery both going up, the landscape goes first
        /// and the scenery waits for a later step (the crashes under SceneryStep came from both in one step).
        /// </summary>
        public static (int Scenery, int Landscape) OneAtATime(int scenery, int landscape, int nextScenery,
            int nextLandscape) {
            return nextScenery > scenery && nextLandscape > landscape ? (scenery, nextLandscape)
                : (nextScenery, nextLandscape);
        }

        /// <summary>
        /// Right after login or a portal the game is still streaming the world in: twice in a row a login stepped from
        /// scenery 1 to 3 (and landscape 15 to 18) two and a half seconds in, and crashed in CPolygon::make_plane
        /// (0x539153) a second later (November 2026). So no step for CautiousWaitSeconds after either (8 at first, 4 once a step only ever raised one of the two), and small steps
        /// (CautiousSceneryStep, CautiousLandscapeStep) until CautiousSeconds have gone by.
        /// </summary>
        public const double CautiousWaitSeconds = 4;

        /// <summary>Seconds after logging in or leaving portal space before the draw distance changes at all, down too.</summary>
        public const double ArrivalQuietSeconds = 12;

        public const double CautiousSeconds = 38;

        public const int CautiousSceneryStep = 1;

        public const int CautiousLandscapeStep = DrawDistanceOptions.MaxLandscapeRadius;

        /// <summary>The least seconds between steps.</summary>
        public const double StepSeconds = 1.5;

        /// <summary>A frame this slow is the game still loading.</summary>
        public const double HitchMs = 100;

        /// <summary>How long the game has to run with no frame that slow before the next step: the last one's done.</summary>
        public const double SettledSeconds = 1;

        /// <summary>
        /// Whether the next step may be taken: StepSeconds since the last, and SettledSeconds with no hitch, so a step
        /// never starts while the last is still loading. Behind another window the game runs at about 9 fps, every frame
        /// a "hitch", so stepping waits until it's in front again.
        /// </summary>
        public static bool MayStep(double secondsSinceStep, double secondsSinceHitch) {
            return secondsSinceStep >= StepSeconds && secondsSinceHitch >= SettledSeconds;
        }

        /// <summary>
        /// How many landblocks the scenery's full detail has to stop short of the landscape's edge. The game never draws
        /// its outer ring in full detail (its own least landscape is 3 with full detail out to 1): a full-detail block's
        /// edges are fitted to its neighbours' (CLandBlockStruct::AdjPlanes, CPolygon::make_plane at 0x5320F6), and past
        /// the edge there's no neighbour. Landscape 10 with scenery 14 crashed the game there (0x539153), with 1.7 GB
        /// free, November 2026.
        /// </summary>
        public const int SceneryInsideLandscape = 2;

        /// <summary>The scenery radius the landscape allows: at least two short of its edge, and never under the game's 1.</summary>
        public static int SceneryFor(int scenery, int landscape) {
            return Math.Max(1, Math.Min(scenery, landscape - SceneryInsideLandscape));
        }

        /// <summary>The next value on the way to the target: straight there going down, up by at most step going up.</summary>
        public static int Next(int current, int target, int step) {
            return target <= current ? target : Math.Min(target, current + Math.Max(1, step));
        }
    }
}
