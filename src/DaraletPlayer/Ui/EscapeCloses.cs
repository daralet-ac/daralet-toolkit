using System;
using System.Linq;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Escape closes the plugin's panels (the /dap window, Inventory, Bank, Character, Quests) when nothing else wanted it:
    /// not while typing in chat or a text box, with a menu open, a tool up or a key being set. The windows that follow what
    /// you're doing (target, fellowship, loot, hotbars, map) stay. SettingsData.EscapeClosesWindows turns it off.
    /// The key is read from Windows on Decal's RenderFrame, like HideGameUi's, and acted on only once an ImGui frame has run
    /// since the press: what uses Escape (ItemUser, the hotbars' key capture, HideGameUi's) reads it in that frame, in
    /// whatever order the windows draw, and says so with Taken.
    /// </summary>
    internal sealed class EscapeCloses : IDisposable {
        private static int takenFrame = int.MinValue;

        private readonly Settings settings;
        private readonly Func<bool> mainVisible;
        private readonly Action hideMain;
        private readonly FloatingWindow[] windows;

        private bool wasDown;

        /// <summary>Escape would have been free last frame: a chat bar or text box Escape has just left was still active then.</summary>
        private bool wasFree;

        /// <summary>The ImGui frame a free press was seen in, or -1 for none waiting.</summary>
        private int pressedFrame = -1;

        private bool disposed;

        /// <param name="mainVisible">Whether the /dap window is shown, and below, hiding it.</param>
        public EscapeCloses(Settings settings, Func<bool> mainVisible, Action hideMain, params FloatingWindow[] windows) {
            this.settings = settings;
            this.mainVisible = mainVisible;
            this.hideMain = hideMain;
            this.windows = windows;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        /// <summary>This frame's Escape did something else (put a tool down, ended a key capture): nothing closes for it.</summary>
        public static void Taken() {
            takenFrame = ImGui.GetFrameCount();
        }

        private bool AnyOpen => mainVisible() || windows.Any(w => w.Shown);

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                // GetFrameCount needs ImGui's context, which UtilityBelt makes once the game's graphics device is up
                if (disposed || !settings.Data.EscapeClosesWindows || CoreManager.Current.CharacterFilter.LoginStatus != 3
                    || ImGui.GetCurrentContext() == IntPtr.Zero || !AnyOpen) {
                    wasDown = false;
                    wasFree = false;
                    pressedFrame = -1;
                    return;
                }

                var frame = ImGui.GetFrameCount();
                var down = Hotkeys.IsDown(Keys.Escape);
                var free = !ItemUser.Targeting && !ImGui.IsPopupOpen("", ImGuiPopupFlags.AnyPopup) && Hotkeys.ForTheGame();

                if (down && !wasDown) {
                    pressedFrame = free && wasFree ? frame : -1;
                }

                wasDown = down;
                wasFree = free;

                if (pressedFrame < 0 || frame <= pressedFrame) {
                    return;
                }

                // one frame early too: a window drawn before this RenderFrame may have read the press first
                var taken = takenFrame >= pressedFrame - 1;
                pressedFrame = -1;

                if (!taken) {
                    CloseAll();
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void CloseAll() {
            if (mainVisible()) {
                hideMain();
            }

            foreach (var window in windows) {
                if (window.Shown) {
                    window.Dismiss();
                }
            }
        }

        public void Dispose() {
            disposed = true;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
        }
    }
}
