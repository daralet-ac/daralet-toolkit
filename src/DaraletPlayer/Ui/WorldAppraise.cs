using System;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A right-click on something in the 3D world appraises it, as a right-click on an object in the plugin's windows does
    /// (RightClick, the same setting; with the menu key held the click is left to the game). A plugin can't ask the game
    /// what's under the mouse, so it's asked as WorldDrop asks it: the selection is cleared and the game is posted a left
    /// click on the spot, which selects whatever is there; whatever is selected within SelectWait is appraised
    /// (Appraisal.Object). Nothing there, and the old selection is put back.
    ///
    /// The mouse is read from Windows on Decal's RenderFrame (the button, GetAsyncKeyState; where it is, GetCursorPos),
    /// since ImGui only runs while some plugin window draws. Only a quick click that barely moved counts: holding the right
    /// button turns the camera. It's left alone over a plugin window (ImGui's WantCaptureMouse) or a piece of the game's
    /// UI that's showing (ClientElements.GameUiPieces, its panels, chat, toolbar, radar...), where the posted left click
    /// would press a button; and when the left button went down lately, since the posted click straight after the
    /// player's own would be a double-click, which uses what's there (opens a door, talks to an NPC). Other plugins'
    /// Decal views aren't known to it. `[world-appraise]` lines in log.txt.
    /// </summary>
    internal sealed class WorldAppraise : IDisposable {
        private const int VK_LBUTTON = 0x01;
        private const int VK_RBUTTON = 0x02;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;
        private const int MK_LBUTTON = 0x0001;

        /// <summary>How far the mouse may move between press and release for a click, not a turn of the camera.</summary>
        private const float ClickSlop = 5;

        /// <summary>The longest press that counts as a click: the game may hold the cursor still while the camera turns.</summary>
        private static readonly TimeSpan ClickLongest = TimeSpan.FromMilliseconds(350);

        /// <summary>How long the game has to select what was clicked.</summary>
        private static readonly TimeSpan SelectWait = TimeSpan.FromMilliseconds(300);

        private readonly Settings settings;
        private bool disposed;

        private bool rightWasDown;
        private bool leftWasDown;

        /// <summary>Where and when the right button went down over the world, or null.</summary>
        private Vector2? pressedAt;
        private DateTime pressedTime;

        /// <summary>When the left button last went down, or a click was posted: the next one waits a double-click's time.</summary>
        private DateTime lastLeftAt = DateTime.MinValue;

        /// <summary>When the click was posted, while waiting for the game to select something; null otherwise.</summary>
        private DateTime? clickedAt;
        private int selectedBefore;
        private bool sawCleared;

        [StructLayout(LayoutKind.Sequential)]
        private struct Point {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out Point point);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref Point point);

        [DllImport("user32.dll")]
        private static extern uint GetDoubleClickTime();

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public WorldAppraise(Settings settings) {
            this.settings = settings;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        public void Dispose() {
            disposed = true;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (disposed) {
                    return;
                }

                if (clickedAt.HasValue) {
                    Finish();
                }

                Watch();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void Watch() {
            var left = Hotkeys.IsDown(VK_LBUTTON);
            var right = Hotkeys.IsDown(VK_RBUTTON);

            if (left && !leftWasDown) {
                lastLeftAt = DateTime.UtcNow;
            }

            var pressed = right && !rightWasDown;
            var released = !right && rightWasDown;
            leftWasDown = left;
            rightWasDown = right;

            if (pressed) {
                pressedAt = MayStart() && TryGetMouse(out var at) && OverWorld(at) ? at : (Vector2?)null;
                pressedTime = DateTime.UtcNow;
                return;
            }

            if (!released || !pressedAt.HasValue) {
                return;
            }

            var from = pressedAt.Value;
            pressedAt = null;

            if (DateTime.UtcNow - pressedTime > ClickLongest || !TryGetMouse(out var spot)
                || Vector2.Distance(from, spot) > ClickSlop || left || !MayStart() || !OverWorld(spot)) {
                return;
            }

            // the posted click straight after a left click of the player's would be a double-click: it'd use the thing
            if (DateTime.UtcNow - lastLeftAt < TimeSpan.FromMilliseconds(GetDoubleClickTime() + 50)) {
                PluginCore.LogFile("[world-appraise] left alone: the left button went down too lately");
                return;
            }

            Post(spot);
        }

        /// <summary>Whether a right-click in the world may appraise now.</summary>
        private bool MayStart() {
            return !clickedAt.HasValue && ImGui.GetCurrentContext() != IntPtr.Zero && settings.Data.RightClickAppraises
                && RightClick.Appraises && CoreManager.Current.CharacterFilter.LoginStatus == 3 && Hotkeys.GameInFront()
                && !ItemUser.Targeting && !GameDrag.Dragging && !WorldDrop.Waiting && !ClientElements.Broken
                && !ImGui.GetIO().WantCaptureMouse;
        }

        /// <summary>A spot on the 3D view: in the game's window and on none of the game's own UI that's showing.</summary>
        private static bool OverWorld(Vector2 at) {
            var size = ImGui.GetIO().DisplaySize;

            if (at.X < 0 || at.Y < 0 || at.X >= size.X || at.Y >= size.Y) {
                return false;
            }

            return !ClientElements.GameUiPieces.Any(id => ClientElements.IsOver(id, at)) && !ClientElements.Broken;
        }

        /// <summary>The mouse in the game window's own coordinates, as ImGui's are.</summary>
        private static bool TryGetMouse(out Vector2 at) {
            at = Vector2.Zero;

            if (!GetCursorPos(out var point) || !ScreenToClient(CoreManager.Current.Decal.Hwnd, ref point)) {
                return false;
            }

            at = new Vector2(point.X, point.Y);
            return true;
        }

        private void Post(Vector2 at) {
            var actions = CoreManager.Current.Actions;
            selectedBefore = actions.CurrentSelection;

            // cleared, so the click's own selection is what tells something was there, even one already selected
            actions.SelectItem(0);
            sawCleared = actions.CurrentSelection == 0;
            clickedAt = DateTime.UtcNow;
            lastLeftAt = DateTime.UtcNow;

            PluginCore.LogFile($"[world-appraise] right-click at {at.X:0},{at.Y:0}: selection was 0x{selectedBefore:X8}");

            var spot = new IntPtr(((int)at.Y << 16) | ((int)at.X & 0xFFFF));
            var window = CoreManager.Current.Decal.Hwnd;
            PostMessage(window, WM_LBUTTONDOWN, new IntPtr(MK_LBUTTON), spot);
            PostMessage(window, WM_LBUTTONUP, IntPtr.Zero, spot);
        }

        /// <summary>Once the click has had time to select something, appraises it, or puts the old selection back.</summary>
        private void Finish() {
            var actions = CoreManager.Current.Actions;
            var target = actions.CurrentSelection;

            if (target == 0) {
                sawCleared = true;
            }

            // the old selection, never cleared: not something the click picked
            if (target != 0 && target == selectedBefore && !sawCleared) {
                target = 0;
            }

            if (target == 0 && DateTime.UtcNow - clickedAt.Value < SelectWait) {
                return;
            }

            clickedAt = null;

            if (target == 0) {
                if (selectedBefore != 0 && actions.CurrentSelection == 0) {
                    actions.SelectItem(selectedBefore);
                }

                PluginCore.LogFile("[world-appraise] nothing there");
                return;
            }

            PluginCore.LogFile($"[world-appraise] appraising 0x{target:X8}");
            Appraisal.Object(target);
        }
    }
}
