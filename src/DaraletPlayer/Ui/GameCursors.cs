using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's own mouse cursors for moving and sizing a window, from its floaty frames' edges (the panel frame's
    /// layout: each edge and corner element has a Cursor media): 06006119 the four arrows (move), 06005E66 up and down,
    /// 06006126 and 06006127 the diagonals, 06006128 left and right, all 32x32 with the point in the middle. Made into
    /// Windows cursors once (cursor-*.png in Resources/ui), for CursorKeeper; zero for one that couldn't be made, which
    /// then falls back to Windows' own. Never destroyed: Windows may still be showing one, and a hot reload's new copy
    /// makes its own five.
    /// </summary>
    internal static class GameCursors {
        public const string Move = "cursor-move";
        public const string SizeNS = "cursor-ns";
        public const string SizeWE = "cursor-ew";
        public const string SizeNWSE = "cursor-nwse";
        public const string SizeNESW = "cursor-nesw";

        private static readonly Dictionary<string, IntPtr> made = new Dictionary<string, IntPtr>();

        [StructLayout(LayoutKind.Sequential)]
        private struct ICONINFO {
            public bool Icon;
            public int HotspotX;
            public int HotspotY;
            public IntPtr Mask;
            public IntPtr Color;
        }

        [DllImport("user32.dll")]
        private static extern bool GetIconInfo(IntPtr icon, out ICONINFO info);

        [DllImport("user32.dll")]
        private static extern IntPtr CreateIconIndirect(ref ICONINFO info);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr icon);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr gdiObject);

        /// <summary>The cursor by name (Move...), made the first time it's asked for.</summary>
        public static IntPtr Get(string name) {
            if (made.TryGetValue(name, out var cursor)) {
                return cursor;
            }

            cursor = Make(name);
            made[name] = cursor;
            return cursor;
        }

        private static IntPtr Make(string name) {
            try {
                using (var stream = typeof(GameCursors).Assembly.GetManifestResourceStream(
                    $"DaraletPlayer.Resources.ui.{name}.png")) {
                    if (stream == null) {
                        return IntPtr.Zero;
                    }

                    using (var bitmap = new Bitmap(stream)) {
                        // an icon from the picture, its alpha kept, then a cursor from its bitmaps with the point set
                        var icon = bitmap.GetHicon();

                        try {
                            if (!GetIconInfo(icon, out var info)) {
                                return IntPtr.Zero;
                            }

                            info.Icon = false;
                            info.HotspotX = bitmap.Width / 2;
                            info.HotspotY = bitmap.Height / 2;
                            var cursor = CreateIconIndirect(ref info);
                            DeleteObject(info.Mask);
                            DeleteObject(info.Color);
                            return cursor;
                        }
                        finally {
                            DestroyIcon(icon);
                        }
                    }
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[cursor] couldn't make {name}: {ex.Message}");
                return IntPtr.Zero;
            }
        }
    }
}
