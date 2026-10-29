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

                // what ImGui asked for in the frame it last drew: the plain arrow is the game's to draw
                var cursor = Cursor(ImGui.GetMouseCursor());

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

        /// <summary>The system cursor for one of ImGui's, or zero for the arrow and anything else left to the game.</summary>
        private static IntPtr Cursor(ImGuiMouseCursor wanted) {
            if (cursors.TryGetValue(wanted, out var known)) {
                return known;
            }

            int id;

            switch (wanted) {
                case ImGuiMouseCursor.TextInput: id = IDC_IBEAM; break;
                case ImGuiMouseCursor.ResizeAll: id = IDC_SIZEALL; break;
                case ImGuiMouseCursor.ResizeEW: id = IDC_SIZEWE; break;
                case ImGuiMouseCursor.ResizeNS: id = IDC_SIZENS; break;
                case ImGuiMouseCursor.ResizeNESW: id = IDC_SIZENESW; break;
                case ImGuiMouseCursor.ResizeNWSE: id = IDC_SIZENWSE; break;
                case ImGuiMouseCursor.Hand: id = IDC_HAND; break;
                case ImGuiMouseCursor.NotAllowed: id = IDC_NO; break;
                default: return IntPtr.Zero;
            }

            var cursor = LoadCursor(IntPtr.Zero, new IntPtr(id));
            cursors[wanted] = cursor;
            return cursor;
        }
    }
}
