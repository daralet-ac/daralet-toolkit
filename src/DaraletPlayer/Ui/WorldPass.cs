using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Decal.Adapter;
using Microsoft.DirectX.Direct3D;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A place inside the game's own 3D frame to draw the plugin's world pieces (billboards, grass): after its solid
    /// world and before its see-through things. Drawn on Decal's RenderFrame, after the whole world, billboards came out
    /// in front of the lifestone's crystal (seen in the game, November 2026): the game draws see-through things last,
    /// sorted, without writing depth, so nothing behind them is hidden by them. SmartBox::RenderNormalMode (0x453B40)
    /// draws the world, then calls D3DPolyRender::FlushAlphaList (0x59E3F0, static cdecl, one int, 0 here) at 0x453C2B,
    /// which draws that see-through list; that call is pointed here (AcClient.Hook, as the fog's), which draws each
    /// subscriber's piece and then the game's list. Taken out when the last subscriber goes (a hook left in after the
    /// plugin's gone would crash the game).
    /// <para>
    /// **Only in while a subscriber has something to draw** (each says so, `wanted`, looked at on every RenderFrame):
    /// every graphics piece subscribes when it's made, so before this the hook was in for everyone, every graphics
    /// setting off. Done while hunting the teleport crashes (November 21, 2026), when chains of crash-prone teleports
    /// seemed to show the hook raising them (minimal mode 1 crash in 50 teleports, with this hook alone 3 in 49); a
    /// longer run the same afternoon, the bare profile with and without it, came out level (5 in 70, 6 in 67), so the
    /// crashes are the game's own and this only keeps a patch out of its frame that nothing uses.
    /// </para>
    /// </summary>
    internal static class WorldPass {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FlushCall(int flags);

        private static readonly List<(Action<Device> Draw, Func<bool> Wanted)> drawers =
            new List<(Action<Device>, Func<bool>)>();

        private static AcClient.Hook hook;
        private static bool watching;

        /// <summary>
        /// The game's other flushes of its see-through list, inside the world (each drawing it only when the list is three
        /// quarters full, but PView::DrawCells always, with the camera in a building): DrawBlock after each cell
        /// (0x5A2B17), DrawBuilding (0x5A041B) and PView::DrawCells before the indoor cells (0x5A5982). Hooked so the
        /// game's lamps are whole for what they draw (GameLampSwitch.BeginSeeThrough), as for the last flush, and counted.
        /// </summary>
        private static readonly int[] EarlyFlushCalls = { 0x5A2B17, 0x5A041B, 0x5A5982 };

        private static readonly List<AcClient.Hook> earlyHooks = new List<AcClient.Hook>();
        private static FlushCall earlyFlushCall;

        /// <summary>The see-through list's two counts (a ushort each) and its size (3000 entries each; 0x7E6550).</summary>
        private const int AlphaCountAt = 0x8729BC;
        private const int AlphaCount2At = 0x8729C0;
        private const float AlphaListSize = 3000;

        /// <summary>
        /// The fraction of the list the game's own guard waits for (DrawBlock's, the float at 0x821EE0): the early
        /// flushes draw only at that, so a full list can't drop what comes next.
        /// </summary>
        private const float OverflowGuard = 0.75f;

        /// <summary>
        /// The game's early flushes wait until the list is three quarters full (OverflowGuard), as DrawBlock's does,
        /// instead of drawing whatever is queued: DrawBuilding flushes before every building, so in a town nearly every
        /// see-through thing was drawn before the plugin's passes (the log: none left at the last flush, hundreds of
        /// early draws a second, Hebian-to), under the night's darkening and the billboards. Drawn at the end, they're
        /// tested against the whole world's depth, which is also right for one in front of a building drawn after it.
        /// A test run's `set alphadefer 0` puts the game's way back, for comparing.
        /// </summary>
        public static bool DeferEarly = true;

        // the flushes that drew before the world was done, and the most queued at the last flush, for the log line
        private static int earlyDraws;
        private static int mostQueued;
        private static DateTime nextReport = DateTime.MinValue;

        // both kept while the hook is in, so they're never collected while the client can call them
        private static FlushCall flushCall;
        private static FlushCall gameFlush;
        private static bool failedToHook;

        /// <summary>A test run's `set worldpasshook 2`: the hook only calls the game's own, nothing else.</summary>
        public static bool Bare;

        /// <summary>A test run's `set worldpasshook 3`: bare, through FpuProbe's native stub instead of AcClient.Hook.</summary>
        public static bool ProbeFpu;

        /// <summary>Whether the hook's in: subscribers draw from it, not from RenderFrame.</summary>
        public static bool Hooked => hook != null;

        /// <summary>
        /// A piece to draw before the game's see-through things, each frame the world's drawn, while `wanted` says it has
        /// something to draw. Until the hook's in (the next RenderFrame), the piece draws on RenderFrame as it does when
        /// the hook can't be set (`Hooked`).
        /// </summary>
        public static void Add(Action<Device> draw, Func<bool> wanted) {
            drawers.Add((draw, wanted));

            if (!watching) {
                CoreManager.Current.RenderFrame += Core_RenderFrame;
                watching = true;
            }
        }

        /// <summary>Takes the hook out and puts it in again, as ProbeFpu now says (a test step).</summary>
        public static void Rehook() {
            Unhook();
            failedToHook = false;

            if (AnyWanted()) {
                Hook();
            }
        }

        public static void Remove(Action<Device> draw) {
            drawers.RemoveAll(d => d.Draw == draw);

            if (drawers.Count == 0) {
                if (watching) {
                    CoreManager.Current.RenderFrame -= Core_RenderFrame;
                    watching = false;
                }

                Unhook();
            }
        }

        private static bool AnyWanted() {
            foreach (var drawer in drawers) {
                if (drawer.Wanted()) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>The hook in while anything wants drawing in the game's frame, out otherwise (see the class).</summary>
        private static void Core_RenderFrame(object sender, EventArgs e) {
            try {
                var wanted = AnyWanted();

                if (wanted && hook == null && !FpuProbe.Installed && !failedToHook) {
                    Hook();
                }
                else if (!wanted && (hook != null || FpuProbe.Installed)) {
                    Unhook();
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile("[world-pass] " + ex.Message);
            }
        }

        private static void Hook() {
            try {
                var entry = (int)AcClient.Entrypoint.D3DPolyRender__FlushAlphaList;
                gameFlush = Marshal.GetDelegateForFunctionPointer<FlushCall>((IntPtr)entry);
                flushCall = Flush;

                if (ProbeFpu) {
                    if (FpuProbe.Install((int)AcClient.Call.D3DPolyRender__FlushAlphaList_0x00453C2B,
                        Marshal.GetFunctionPointerForDelegate(flushCall))) {
                        return;
                    }

                    failedToHook = true;
                    flushCall = null;
                    return;
                }

                var hooked = new AcClient.Hook(entry, (int)AcClient.Call.D3DPolyRender__FlushAlphaList_0x00453C2B);

                if (!hooked.Setup(flushCall)) {
                    PluginCore.LogFile("[world-pass] couldn't hook the see-through pass at 0x00453C2B: drawn after it");
                    failedToHook = true;
                    flushCall = null;
                    return;
                }

                hook = hooked;
                PluginCore.LogFile("[world-pass] drawing before the game's see-through things");
                HookEarlyFlushes(entry);
            }
            catch (Exception ex) {
                failedToHook = true;
                PluginCore.LogFile("[world-pass] " + ex);
            }
        }

        private static void Unhook() {
            if (FpuProbe.Installed) {
                FpuProbe.Uninstall();
                flushCall = null;
                return;
            }

            if (hook == null) {
                return;
            }

            try {
                hook.Remove();
                PluginCore.LogFile("[world-pass] hook taken out");
            }
            catch (Exception ex) {
                PluginCore.LogFile("[world-pass] " + ex);
            }

            UnhookEarlyFlushes();
            hook = null;
            flushCall = null;
        }

        private static void HookEarlyFlushes(int entry) {
            earlyFlushCall = EarlyFlush;
            var hooked = 0;

            foreach (var call in EarlyFlushCalls) {
                try {
                    var early = new AcClient.Hook(entry, call);

                    if (early.Setup(earlyFlushCall)) {
                        earlyHooks.Add(early);
                        hooked++;
                    }
                    else {
                        PluginCore.LogFile($"[world-pass] couldn't hook the game's early see-through flush at 0x{call:X8}");
                    }
                }
                catch (Exception ex) {
                    PluginCore.LogFile($"[world-pass] early flush at 0x{call:X8}: {ex.Message}");
                }
            }

            PluginCore.LogFile($"[world-pass] {hooked} of the game's early see-through flushes hooked");
        }

        private static void UnhookEarlyFlushes() {
            foreach (var early in earlyHooks) {
                try {
                    early.Remove();
                }
                catch (Exception ex) {
                    PluginCore.LogFile("[world-pass] " + ex.Message);
                }
            }

            earlyHooks.Clear();
            earlyFlushCall = null;
        }

        /// <summary>
        /// One of the game's flushes inside the world: the lamps whole for what it draws, then the game's own. The
        /// fraction is a float's bits, handed on as they are; the list is drawn when either count reaches that much of its
        /// size (FlushAlphaList's own test), which is counted for the log.
        /// </summary>
        private static unsafe void EarlyFlush(int fraction) {
            if (Bare) {
                gameFlush(fraction);
                return;
            }

            // DrawBlock calls this after every cell, hundreds of times a frame, and the game draws nothing until the list
            // is full enough: only then are the lamps swapped
            var draws = true;

            try {
                var share = BitConverter.ToSingle(BitConverter.GetBytes(fraction), 0);

                // a flush of whatever's queued (DrawBuilding's, PView::DrawCells') made an overflow guard (DeferEarly)
                if (DeferEarly && share < OverflowGuard) {
                    share = OverflowGuard;
                    fraction = BitConverter.ToInt32(BitConverter.GetBytes(OverflowGuard), 0);
                }

                var queued = Math.Max(*(ushort*)AlphaCountAt, *(ushort*)AlphaCount2At);
                draws = queued > 0 && queued >= share * AlphaListSize;

                if (draws) {
                    earlyDraws++;
                }
            }
            catch (Exception) {
                // drawn with the lamps swapped, to be safe
            }

            if (!draws) {
                gameFlush(fraction);
                return;
            }

            GameLampSwitch.BeginSeeThrough();

            try {
                gameFlush(fraction);
            }
            finally {
                GameLampSwitch.EndSeeThrough();
            }
        }

        /// <summary>How full the see-through list got and how often the game drew it early, every 10 seconds.</summary>
        private static unsafe void NoteQueued() {
            try {
                mostQueued = Math.Max(mostQueued, Math.Max(*(ushort*)AlphaCountAt, *(ushort*)AlphaCount2At));

                if (DateTime.UtcNow < nextReport) {
                    return;
                }

                if (nextReport != DateTime.MinValue) {
                    PluginCore.LogFile($"[world-pass] the game's see-through list: up to {mostQueued} of "
                        + $"{AlphaListSize:0} at the last flush, {earlyDraws} drawn early in 10 s");
                }

                nextReport = DateTime.UtcNow.AddSeconds(10);
                mostQueued = 0;
                earlyDraws = 0;
            }
            catch (Exception) {
                // only for the log line
            }
        }

        private static void Flush(int flags) {
            if (Bare) {
                FpuProbe.Note();
                gameFlush(flags);
                return;
            }

            try {
                // the world's own draws are all in: what follows isn't to cast the sun's shadows
                ShadowCasters.EndOfWorld();

                var device = UBService.Huds.D3Ddevice;

                if (device != null && CoreManager.Current.CharacterFilter.LoginStatus == 3) {
                    DrawProbe.Ours = true;

                    try {
                        foreach (var drawer in drawers.ToArray()) {
                            // timed under the drawer's class name, apart from its RenderFrame work (Ui/HitchLog.cs)
                            using (HitchLog.Measure(drawer.Draw.Method.DeclaringType?.Name ?? "world pass")) {
                                drawer.Draw(device);
                            }
                        }
                    }
                    finally {
                        DrawProbe.Ours = false;
                    }
                }
            }
            catch (Exception ex) {
                // nothing may escape into the game's frame
                PluginCore.LogFile("[world-pass] " + ex.Message);
            }

            DrawProbe.SeeThrough();
            NoteQueued();

            // the game's see-through things (glows, crystals, lamp halos) are drawn now, after the plugin's lamp light,
            // with whatever lights are on: the game's own lamps whole for them (GameLampSwitch.BeginSeeThrough)
            GameLampSwitch.BeginSeeThrough();

            try {
                gameFlush(flags);
            }
            finally {
                GameLampSwitch.EndSeeThrough();
            }
        }
    }
}
