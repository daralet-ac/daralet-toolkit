using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Gives the game back a key's release that UtilityBelt.Service kept from it. UB keeps every key message from the game
    /// while ImGui wants the keyboard (HudManager.WindowMessage: WM_KEYDOWN to WM_CHAR while io.WantCaptureKeyboard), and
    /// ImGui wants it whenever an item of any plugin window is active: the mouse held down on a window, a button, a slider
    /// or a hotbar slot, or a text box typed in. A key that went down before that and came up during it went down for the
    /// game and never came up: let go of the forward key while clicking a window and the character ran on by itself, and
    /// turning or backing up didn't stop it, only the stop key (or forward again). So every key the game saw go down is
    /// watched, and one Windows says is up whose release the game didn't get is sent to the game again, once ImGui has
    /// let go of the keyboard.
    /// </summary>
    internal static class KeyReleases {
        private const int WM_KILLFOCUS = 0x0008;
        private const int WM_ACTIVATEAPP = 0x001C;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;

        /// <summary>How long a key must have been up before its release counts as missed, not still on its way.</summary>
        private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(150);

        /// <summary>How long to wait for a release sent again to come round before sending it once more.</summary>
        private static readonly TimeSpan Resend = TimeSpan.FromMilliseconds(250);

        private const int MaxTries = 3;
        private const int MaxLogged = 20;

        /// <summary>The keys the game was told went down and hasn't been told came up, by virtual key code.</summary>
        private static readonly Dictionary<int, Held> held = new Dictionary<int, Held>();
        private static readonly List<int> done = new List<int>();
        private static bool started;
        private static int logged;

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private class Held {
            /// <summary>The scan code and extended-key bit from the key's WM_KEYDOWN (bits 16-24), for its release.</summary>
            public int ScanBits;
            public DateTime? UpSince;
            public DateTime SentAt;
            public int Tries;
        }

        public static void Start() {
            if (started) {
                return;
            }

            started = true;
            CoreManager.Current.WindowMessage += Core_WindowMessage;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        public static void Stop() {
            if (!started) {
                return;
            }

            started = false;
            CoreManager.Current.WindowMessage -= Core_WindowMessage;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            held.Clear();
        }

        /// <summary>
        /// Whether UB will keep a key message from the game: its own rule, read the same way. Decided here rather than by
        /// whether this handler sees the message, since which of Decal's handlers runs first isn't promised.
        /// </summary>
        private static bool KeyboardTaken() {
            return ImGui.GetCurrentContext() != IntPtr.Zero && ImGui.GetIO().WantCaptureKeyboard;
        }

        private static void Core_WindowMessage(object sender, WindowMessageEventArgs e) {
            try {
                switch (e.Msg) {
                    case WM_KEYDOWN: {
                        var key = (int)((long)e.WParam & 0xFF);

                        // only a key held down now: a key message posted by another program, with no key behind it, is left
                        // to it, or its next WM_KEYUP would be sent for it
                        if (e.Eat || KeyboardTaken() || !Hotkeys.IsDown(key)) {
                            return;
                        }

                        if (!held.TryGetValue(key, out var state)) {
                            state = new Held();
                            held[key] = state;
                        }

                        state.ScanBits = (int)((long)e.LParam & 0x01FF0000);
                        state.UpSince = null;
                        state.Tries = 0;
                        return;
                    }

                    case WM_KEYUP:
                        if (!e.Eat && !KeyboardTaken()) {
                            held.Remove((int)((long)e.WParam & 0xFF));
                        }

                        return;

                    // the game's window losing the keyboard is the game's to deal with, and keys let go of in another
                    // window aren't its
                    case WM_KILLFOCUS:
                        held.Clear();
                        return;

                    case WM_ACTIVATEAPP:
                        if ((long)e.WParam == 0) {
                            held.Clear();
                        }

                        return;
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private static void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (held.Count == 0) {
                    return;
                }

                if (!Hotkeys.GameInFront()) {
                    held.Clear();
                    return;
                }

                var now = DateTime.UtcNow;
                done.Clear();

                foreach (var pair in held) {
                    var key = pair.Key;
                    var state = pair.Value;

                    if (Hotkeys.IsDown(key)) {
                        state.UpSince = null;
                        continue;
                    }

                    // a release still in the game's message queue: give it time to arrive
                    if (state.UpSince == null) {
                        state.UpSince = now;
                    }

                    if (now - state.UpSince.Value < Settle || now - state.SentAt < Resend) {
                        continue;
                    }

                    // sent now, UB would keep it from the game again
                    if (KeyboardTaken()) {
                        continue;
                    }

                    if (state.Tries >= MaxTries) {
                        done.Add(key);
                        Log($"[keys] gave up giving the game the release of key 0x{key:X2}");
                        continue;
                    }

                    state.Tries++;
                    state.SentAt = now;
                    SendRelease(key, state.ScanBits);
                }

                foreach (var key in done) {
                    held.Remove(key);
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>Posts the game a WM_KEYUP for the key: repeat count 1, the key's scan code, previously down, released.</summary>
        private static void SendRelease(int key, int scanBits) {
            var lParam = unchecked((int)(1u | (uint)scanBits | (1u << 30) | (1u << 31)));
            var window = CoreManager.Current.Decal.Hwnd;
            var sent = PostMessage(window, WM_KEYUP, new IntPtr(key), new IntPtr(lParam));
            Log($"[keys] the game missed the release of key 0x{key:X2} (a plugin window had the keyboard): "
                + (sent ? "sent it again" : "couldn't send it"));
        }

        private static void Log(string text) {
            if (logged >= MaxLogged) {
                return;
            }

            logged++;
            PluginCore.LogFile(text);
        }
    }
}
