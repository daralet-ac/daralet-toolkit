using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Keeps ImGui's mouse cursor (the resize arrows on a window's edge and corner, a divider's, the text box's I-beam)
    /// on screen while the mouse is over it. Windows asks which cursor to show with WM_SETCURSOR on every move, the game
    /// answers with its own, and UtilityBelt.Service leaves that message out of what it gives ImGui
    /// (HudManager.WindowMessage), whose own code only sets a cursor when it changes: the resize arrow flashed for a frame
    /// and was gone. So while ImGui wants something other than the plain arrow, the plugin answers it and keeps the
    /// message from the game.
    /// </summary>
    internal static class CursorKeeper {
        private const int WM_SETCURSOR = 0x0020;
        private const int HTCLIENT = 1;

        // the system cursors ImGui's own Windows code picks for each (imgui_impl_win32)
        private const int IDC_IBEAM = 32513;
        private const int IDC_SIZENWSE = 32642;
        private const int IDC_SIZENESW = 32643;
        private const int IDC_SIZEWE = 32644;
        private const int IDC_SIZENS = 32645;
        private const int IDC_SIZEALL = 32646;
        private const int IDC_NO = 32648;
        private const int IDC_HAND = 32649;

        private static readonly Dictionary<ImGuiMouseCursor, IntPtr> cursors = new Dictionary<ImGuiMouseCursor, IntPtr>();
        private static bool started;

        [DllImport("user32.dll")]
        private static extern IntPtr LoadCursor(IntPtr instance, IntPtr cursorName);

        [DllImport("user32.dll")]
        private static extern IntPtr SetCursor(IntPtr cursor);

        public static void Start() {
            if (started) {
                return;
            }

            started = true;
            CoreManager.Current.WindowMessage += Core_WindowMessage;
        }

        public static void Stop() {
            if (!started) {
                return;
            }

            started = false;
            CoreManager.Current.WindowMessage -= Core_WindowMessage;
        }

        private static void Core_WindowMessage(object sender, WindowMessageEventArgs e) {
            try {
                // only over the game's own area (not its frame's edges, which Windows resizes the game by)
                if (e.Msg != WM_SETCURSOR || ((long)e.LParam & 0xFFFF) != HTCLIENT
                    || ImGui.GetCurrentContext() == IntPtr.Zero) {
                    return;
                }

                // a window's top edge (FloatingWindow.EdgeDrag) first, then what ImGui asked for in the frame it last
                // drew: the plain arrow is the game's to draw
                var cursor = FloatingWindow.WantsMoveCursor ? Game(GameCursors.Move, IDC_SIZEALL)
                    : Cursor(ImGui.GetMouseCursor());

                if (cursor == IntPtr.Zero) {
                    return;
                }

                SetCursor(cursor);
                e.Eat = true;
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>
        /// Puts the cursor on again while the left button is held on something that moves or sizes a window (a top edge,
        /// one of ImGui's resize edges), from every FloatingWindow's draw. Windows sends no WM_SETCURSOR while the button
        /// is held, and ImGui's own code sets Windows' arrow whenever its cursor changes, which it does as the press
        /// starts: the move cursor was gone the moment a drag began, and stayed gone till it ended.
        /// </summary>
        public static void KeepWhileHeld() {
            if (!started || !ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                return;
            }

            var cursor = FloatingWindow.WantsMoveCursor ? Game(GameCursors.Move, IDC_SIZEALL)
                : Cursor(ImGui.GetMouseCursor());

            if (cursor != IntPtr.Zero) {
                SetCursor(cursor);
            }
        }

        /// <summary>The system cursor for one of ImGui's, or zero for the arrow and anything else left to the game.</summary>
        private static IntPtr Cursor(ImGuiMouseCursor wanted) {
            if (cursors.TryGetValue(wanted, out var known)) {
                return known;
            }

            IntPtr cursor;

            // moving and sizing use the game's own arrows (GameCursors), as its panels' edges do
            switch (wanted) {
                case ImGuiMouseCursor.TextInput: cursor = SystemCursor(IDC_IBEAM); break;
                case ImGuiMouseCursor.ResizeAll: cursor = Game(GameCursors.Move, IDC_SIZEALL); break;
                case ImGuiMouseCursor.ResizeEW: cursor = Game(GameCursors.SizeWE, IDC_SIZEWE); break;
                case ImGuiMouseCursor.ResizeNS: cursor = Game(GameCursors.SizeNS, IDC_SIZENS); break;
                case ImGuiMouseCursor.ResizeNESW: cursor = Game(GameCursors.SizeNESW, IDC_SIZENESW); break;
                case ImGuiMouseCursor.ResizeNWSE: cursor = Game(GameCursors.SizeNWSE, IDC_SIZENWSE); break;
                case ImGuiMouseCursor.Hand: cursor = SystemCursor(IDC_HAND); break;
                case ImGuiMouseCursor.NotAllowed: cursor = SystemCursor(IDC_NO); break;
                default: return IntPtr.Zero;
            }

            cursors[wanted] = cursor;
            return cursor;
        }

        /// <summary>One of the game's cursors, or Windows' own if it couldn't be made.</summary>
        private static IntPtr Game(string name, int fallback) {
            var cursor = GameCursors.Get(name);
            return cursor != IntPtr.Zero ? cursor : SystemCursor(fallback);
        }

        private static IntPtr SystemCursor(int id) {
            return LoadCursor(IntPtr.Zero, new IntPtr(id));
        }
    }
}
