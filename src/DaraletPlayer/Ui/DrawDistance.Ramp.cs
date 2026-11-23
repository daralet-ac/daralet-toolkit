using System;
using System.Collections.Generic;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Decal.Adapter.Wrappers;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The draw distance raised a step at a time (Feed/DrawDistanceRamp.cs, which says why): at login, after a teleport,
    /// and whenever a setting goes up. In portal space it goes back to the game's own (Decal's ChangePortalMode), so the
    /// new place loads at the game's size, and steps up once you're there. `[draw-distance]` lines in log.txt.
    /// Untested in the game when written.
    /// </summary>
    internal sealed partial class DrawDistance {
        /// <summary>Whether you're in portal space: the far settings wait until you're out.</summary>
        private bool inPortal;

        /// <summary>In portal space, whether or not the draw distance is taken down for it (PortalReload).</summary>
        private bool inPortalSpace;

        /// <summary>
        /// In portal space, or just in (logged in or out of a portal, ArrivalQuietSeconds ago at most): the game is loading
        /// the place, and nothing of the plugin's should load beside it (FarLand waits).
        /// </summary>
        public bool Arriving => inPortalSpace
            || (DateTime.UtcNow - arrivedAt).TotalSeconds < DrawDistanceRamp.ArrivalQuietSeconds(arrivedByPortal);

        /// <summary>Arriving, for what isn't this class's (the water map, the dats' lights, grass), set each frame.</summary>
        public static bool QuietNow;

        /// <summary>When the next step up may be taken.</summary>
        private DateTime nextStepAt = DateTime.MinValue;

        /// <summary>When you last logged in or came out of a portal: steps are small for a while after.</summary>
        private DateTime arrivedAt = DateTime.UtcNow;

        /// <summary>Whether the last arrival was out of portal space (a shorter wait) or a login or the plugin starting.</summary>
        private bool arrivedByPortal;

        private bool wasLoggedIn;

        /// <summary>When the last step up was taken, and when the game last took over HitchMs over a frame.</summary>
        private DateTime lastStepAt = DateTime.MinValue;
        private DateTime lastHitchAt = DateTime.MinValue;
        private readonly System.Diagnostics.Stopwatch frameClock = System.Diagnostics.Stopwatch.StartNew();
        private double lastFrameMs = -1;

        /// <summary>
        /// Each of Decal's RenderFrames is one of the game's frames: a slow one means it's still loading (or behind
        /// another window), and the next step waits for a second without one.
        /// </summary>
        private void NoteFrame() {
            NoteFrameTime();
        }

        /// <summary>
        /// When the last landscape load began (a whole reload or a refresh in place); at first, when this copy of the
        /// plugin started, as a hot reload follows the old copy's own reload at shutdown.
        /// </summary>
        private DateTime lastLoadAt = DateTime.UtcNow;

        /// <summary>How long after a load the next may begin, at least.</summary>
        private const double LoadQuietSeconds = 5;

        /// <summary>
        /// Whether a landscape load may begin: LoadQuietSeconds since the last, and a second with no slow frame. Every
        /// black screen (the game drawing nothing, its landscape empty, a crash in make_plane later) came from two loads
        /// close together: brought down in place then reloaded the next frame; and after a hot reload, the old copy's
        /// reload at shutdown then the new copy's for a density it took for changed (November 2026: "0 in full detail"
        /// at every look after, the screenshot all black).
        /// </summary>
        private bool LoadSettled() {
            var now = DateTime.UtcNow;
            return (now - lastLoadAt).TotalSeconds >= LoadQuietSeconds
                && (now - lastHitchAt).TotalSeconds >= DrawDistanceRamp.SettledSeconds;
        }

        private void NoteFrameTime() {
            var ms = frameClock.Elapsed.TotalMilliseconds;

            if (lastFrameMs >= 0 && ms - lastFrameMs > DrawDistanceRamp.HitchMs) {
                lastHitchAt = DateTime.UtcNow;
            }

            lastFrameMs = ms;
        }

        private void ListenForPortals() {
            CoreManager.Current.CharacterFilter.ChangePortalMode += CharacterFilter_ChangePortalMode;
        }

        private void StopListeningForPortals() {
            CoreManager.Current.CharacterFilter.ChangePortalMode -= CharacterFilter_ChangePortalMode;
        }

        /// <summary>
        /// Whether portal space takes the draw distance down (off; a test run's `set portalreload 1` puts it on). It was
        /// the way at first, so a teleport wouldn't load a far landscape from cold; but that reload comes in the same
        /// second as the game's own load of where you're going, and the two crashed the game (0x58712F, 0x539153 in
        /// make_plane) on one teleport in four. Left as it is, ten teleports at Scenery 2, Ground 6, Landscape 18 between
        /// Holtburg and Yaraq came through clean (November 2026, after the billboards stopped wasting memory).
        /// </summary>
        internal static bool PortalReload = false;

        /// <summary>A test run's `set portalrings 0`: full detail left as it is for a teleport's load, to compare.</summary>
        internal static bool NoPortalRings;

        /// <summary>The full-detail rings a teleport may load with: the presets' 2 are left as they are.</summary>
        private const int PortalRings = 2;

        private void CharacterFilter_ChangePortalMode(object sender, ChangePortalModeEventArgs e) {
            try {
                // the far scenery let go for the teleport, so the game has its memory for the new place; made again
                // once you're out (UpdateBillboards waits while in portal space)
                inPortalSpace = e.Type == PortalEventType.EnterPortal;

                if (inPortalSpace) {
                    TeleportTimeline.Entered();
                }
                else {
                    var o = Options;
                    var pieces = new List<string>();

                    if (o.ClampedFarLand > 0) {
                        pieces.Add("far land");
                    }

                    if (o.On && (o.Billboards || o.ClampedFarTrees > 0 || o.FarBuildings)) {
                        pieces.Add("billboards");
                    }

                    if (o.On) {
                        pieces.Add("full detail");
                    }

                    TeleportTimeline.Left(pieces);
                    ExpectPieces();
                }

                if (inPortalSpace) {
                    ReleaseFarScenery();
                    highestCostHere = 0;

                    // the full-detail rings back to the game's for the game's own load of the new place: a code patch,
                    // no reload, so nothing to collide with it. Out of a dungeon nothing outdoors is loaded, and with the
                    // rings at 6 that whole cold load crashed the game in its unpacking (0x526A8A, November 2026, with
                    // 2.5 GB free); the rings step back up in place once you're there, as at login
                    // down only past PortalRings (2), and only to it: the presets' 2 are left for the load, so nothing
                    // steps back up after; the patch to 1 and the steps and reloads after it, in the minute after
                    // arriving, were followed by crashes when the next teleport came soon (November 2026)
                    if (!NoPortalRings && patchedRadius > PortalRings && !broken && PatchRings(PortalRings)) {
                        PluginCore.LogFile($"[draw-distance] portal space: full detail {patchedRadius} -> "
                            + $"{PortalRings} for the game's load, no reload");
                        patchedRadius = PortalRings;
                    }
                }
                else {
                    // arrived: the same gentle start as at login before the rings step back up
                    cacheFlushWanted = "a teleport";
                    arrivedAt = DateTime.UtcNow;
                    arrivedByPortal = true;
                    nextStepAt = arrivedAt.AddSeconds(DrawDistanceRamp.CautiousWaitSeconds);
                    Soon();
                }

                if (!PortalReload) {
                    PluginCore.LogFile("[draw-distance] portal space: left as it is (portal reload off)");
                    return;
                }

                inPortal = e.Type == PortalEventType.EnterPortal;

                // out of portal space, the place you've come to loads at the game's size first, then steps up, gently
                // while the world streams in (DrawDistanceRamp.Cautious...)
                arrivedAt = DateTime.UtcNow;
                arrivedByPortal = true;
                nextStepAt = arrivedAt.AddSeconds(DrawDistanceRamp.CautiousWaitSeconds);
                PluginCore.LogFile(inPortal ? "[draw-distance] portal space: the game's own until you're out"
                    : "[draw-distance] out of portal space: stepping up again");
                Soon();
            }
            catch (Exception ex) {
                PluginCore.LogFile("[draw-distance] " + ex);
            }
        }

        /// <summary>
        /// This look's scenery and landscape radius on the way to the targets: down at once, up a step at a time, no
        /// sooner than StepSeconds after the last.
        /// </summary>
        private (int Scenery, int Landscape) NextStep(int scenery, int targetScenery, int landscape,
            int targetLandscape) {
            var goingUp = targetScenery > scenery || targetLandscape > landscape;
            var now = DateTime.UtcNow;

            // nothing goes up during a teleport: the game is loading the new place
            if (goingUp && inPortalSpace) {
                return (Math.Min(scenery, targetScenery), Math.Min(landscape, targetLandscape));
            }

            // login: the moment the character's in, the same gentle start as out of a portal
            var loggedIn = CoreManager.Current.CharacterFilter.LoginStatus == 3;

            if (loggedIn && !wasLoggedIn) {
                arrivedAt = now;
                arrivedByPortal = false;
                nextStepAt = now.AddSeconds(DrawDistanceRamp.CautiousWaitSeconds);
                PluginCore.LogFile("[draw-distance] logged in: stepping up gently");
            }

            wasLoggedIn = loggedIn;

            // just in (logged in, or out of a portal): nothing changes either way while the game's own load runs. The cut
            // from the game's landscape 15 to a lower one went through 4 seconds after login, a reload in the middle of
            // the game's own, and 10 seconds later the game crashed emptying its cache (0x58712F, November 2026)
            if (loggedIn && !inPortalSpace
                && (now - arrivedAt).TotalSeconds < DrawDistanceRamp.ArrivalQuietSeconds(arrivedByPortal)) {
                return (scenery, landscape);
            }

            var cautious = (now - arrivedAt).TotalSeconds < DrawDistanceRamp.CautiousSeconds;

            // the last step's loading done: its time gone by, and a second without a slow frame
            var settled = now >= nextStepAt && DrawDistanceRamp.MayStep(
                (now - lastStepAt).TotalSeconds, (now - lastHitchAt).TotalSeconds);

            if (goingUp && !settled) {
                return (Math.Min(scenery, targetScenery), Math.Min(landscape, targetLandscape));
            }

            var next = (
                DrawDistanceRamp.Next(scenery, targetScenery,
                    cautious ? DrawDistanceRamp.CautiousSceneryStep : DrawDistanceRamp.SceneryStep),
                DrawDistanceRamp.Next(landscape, targetLandscape,
                    cautious ? DrawDistanceRamp.CautiousLandscapeStep : DrawDistanceRamp.LandscapeStep));
            next = DrawDistanceRamp.OneAtATime(scenery, landscape, next.Item1, next.Item2);

            if (next.Item1 > scenery || next.Item2 > landscape) {
                lastStepAt = now;
                nextStepAt = now.AddSeconds(DrawDistanceRamp.StepSeconds);

                if (next.Item1 != targetScenery || next.Item2 != targetLandscape) {
                    PluginCore.LogFile($"[draw-distance] stepping up: scenery {next.Item1} of {targetScenery}, "
                        + $"landscape {next.Item2} of {targetLandscape}");
                }
            }

            return next;
        }
    }
}
