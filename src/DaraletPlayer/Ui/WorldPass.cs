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
    /// subscriber's piece and then the game's list. Hooked while anything's subscribed; taken out when the last goes
    /// (a hook left in after the plugin's gone would crash the game). Untested in the game when written.
    /// </summary>
    internal static class WorldPass {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FlushCall(int flags);

        private static readonly List<Action<Device>> drawers = new List<Action<Device>>();
        private static AcClient.Hook hook;

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

        /// <summary>A piece to draw before the game's see-through things, each frame the world's drawn.</summary>
        public static void Add(Action<Device> draw) {
            drawers.Add(draw);

            if (hook == null && !failedToHook) {
                Hook();
            }
        }

        /// <summary>Takes the hook out and puts it in again, as ProbeFpu now says (a test step).</summary>
        public static void Rehook() {
            Unhook();
            failedToHook = false;

            if (drawers.Count > 0) {
                Hook();
            }
        }

        public static void Remove(Action<Device> draw) {
            drawers.Remove(draw);

            if (drawers.Count == 0) {
                Unhook();
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

            hook = null;
            flushCall = null;
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
                        foreach (var draw in drawers.ToArray()) {
                            draw(device);
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
            gameFlush(flags);
        }
    }
}
