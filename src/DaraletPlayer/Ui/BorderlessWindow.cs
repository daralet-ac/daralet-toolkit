using System;
using System.Runtime.InteropServices;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Takes the title bar and border off the game's window (SettingsData.BorderlessWindow), a Windows style and nothing to
    /// do with the game: its picture stays the same size, so the game has nothing to rebuild. Looked at once a second, in
    /// case the game puts them back (a change of resolution). Waits for login, when the character's settings profile is in
    /// use, and puts them back when it's turned off or the plugin shuts down, or a reload's new copy would have nothing to
    /// put back. A game in true fullscreen has no border, so nothing happens. `[borderless]` lines in log.txt.
    /// </summary>
    internal sealed class BorderlessWindow : IDisposable {
        private const int GWL_STYLE = -16;
        private const int GWL_EXSTYLE = -20;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const uint GA_ROOT = 2;

        private static readonly TimeSpan LookEvery = TimeSpan.FromSeconds(1);

        private readonly Settings settings;
        private DateTime lookAt;
        private uint? oldStyle;
        private uint oldExtendedStyle;
        private bool disposed;

        public BorderlessWindow(Settings settings) {
            this.settings = settings;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        private static IntPtr Window => GetAncestor(CoreManager.Current.Decal.Hwnd, GA_ROOT);

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (disposed || CoreManager.Current.CharacterFilter.LoginStatus != 3 || DateTime.UtcNow < lookAt) {
                    return;
                }

                lookAt = DateTime.UtcNow + LookEvery;

                if (settings.Data.BorderlessWindow) {
                    TakeOff();
                }
                else {
                    PutBack();
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void TakeOff() {
            var window = Window;

            // a minimized window has no picture to keep the size of
            if (window == IntPtr.Zero || IsIconic(window)) {
                return;
            }

            var style = (uint)GetWindowLong(window, GWL_STYLE);

            if (!BorderlessPlacement.HasBorder(style) || !TryGetPicture(window, out var picture)
                || !TryGetScreen(window, out var screen)) {
                return;
            }

            var extendedStyle = (uint)GetWindowLong(window, GWL_EXSTYLE);
            oldStyle = style;
            oldExtendedStyle = extendedStyle;

            var width = picture.Right - picture.Left;
            var height = picture.Bottom - picture.Top;
            var (x, y) = BorderlessPlacement.Place(picture.Left, picture.Top, width, height, screen.Left, screen.Top,
                screen.Right - screen.Left, screen.Bottom - screen.Top);

            SetWindowLong(window, GWL_STYLE, (int)BorderlessPlacement.WithoutBorder(style));
            SetWindowLong(window, GWL_EXSTYLE, (int)BorderlessPlacement.WithoutEdges(extendedStyle));
            SetWindowPos(window, IntPtr.Zero, x, y, width, height,
                SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            PluginCore.LogFile($"[borderless] border off, {width}x{height} at {x},{y}");
        }

        /// <summary>The border back, the picture staying where it is: the border goes round it, the title bar kept on screen.</summary>
        private void PutBack() {
            if (!oldStyle.HasValue) {
                return;
            }

            var style = oldStyle.Value;
            oldStyle = null;
            var window = Window;

            if (window == IntPtr.Zero || !TryGetPicture(window, out var box)) {
                return;
            }

            SetWindowLong(window, GWL_STYLE, (int)style);
            SetWindowLong(window, GWL_EXSTYLE, (int)oldExtendedStyle);
            AdjustWindowRectEx(ref box, style, false, oldExtendedStyle);
            var y = box.Top;

            if (TryGetScreen(window, out var screen) && y < screen.Top) {
                y = screen.Top;
            }

            SetWindowPos(window, IntPtr.Zero, box.Left, y, box.Right - box.Left, box.Bottom - box.Top,
                SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
            PluginCore.LogFile("[borderless] border back");
        }

        /// <summary>Moves the window, keeping its size: with no title bar there's nothing to drag it by.</summary>
        private void Move(bool centre) {
            var window = Window;

            if (window == IntPtr.Zero || !TryGetPicture(window, out var picture) || !TryGetScreen(window, out var screen)) {
                return;
            }

            var (x, y) = centre
                ? BorderlessPlacement.Centre(picture.Right - picture.Left, picture.Bottom - picture.Top, screen.Left,
                    screen.Top, screen.Right - screen.Left, screen.Bottom - screen.Top)
                : (screen.Left, screen.Top);
            SetWindowPos(window, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOOWNERZORDER | SWP_NOACTIVATE);
        }

        /// <summary>The picture's box on screen.</summary>
        private static bool TryGetPicture(IntPtr window, out RECT box) {
            box = default;

            if (!GetClientRect(window, out var inside)) {
                return false;
            }

            var corner = new POINT();

            if (!ClientToScreen(window, ref corner)) {
                return false;
            }

            box = new RECT {
                Left = corner.X,
                Top = corner.Y,
                Right = corner.X + inside.Right,
                Bottom = corner.Y + inside.Bottom,
            };
            return true;
        }

        /// <summary>The whole of the monitor the window's most on, taskbar included.</summary>
        private static bool TryGetScreen(IntPtr window, out RECT screen) {
            screen = default;
            var info = new MONITORINFO { Size = Marshal.SizeOf(typeof(MONITORINFO)) };

            if (!GetMonitorInfo(MonitorFromWindow(window, MONITOR_DEFAULTTONEAREST), ref info)) {
                return false;
            }

            screen = info.Monitor;
            return true;
        }

        /// <summary>The Config tab's Game window section.</summary>
        public void Render() {
            Form.Section("Game window");

            if (!Form.BeginForm("game-window")) {
                return;
            }

            Form.Label("Border", "The title bar and frame round the game's window.");

            if (Form.Check("Borderless window", ref settings.Data.BorderlessWindow, "Takes the title bar and border off the "
                + "game's window, within a second. A window as big as your screen then fills it exactly: set the game's "
                + "resolution to your screen's, in windowed mode, for borderless fullscreen. A game in true fullscreen has "
                + "no border to take off. Without a title bar you can't drag the window: use the buttons below, or turn "
                + "this off, drag it, and turn it on again.")) {
                settings.Changed();
            }

            if (settings.Data.BorderlessWindow) {
                Form.Label("Move", "Moves the window on the screen it's on, keeping its size.");
                var first = true;

                if (Form.FlowSmallButton(ref first, "Centre##borderless-centre", "Centres the game's window on your screen.")) {
                    Move(centre: true);
                }

                if (Form.FlowSmallButton(ref first, "Top left##borderless-corner",
                    "Moves the game's window to the top left corner of your screen.")) {
                    Move(centre: false);
                }
            }

            ImGui.EndTable();
        }

        public void Dispose() {
            disposed = true;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            PutBack();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO {
            public int Size;
            public RECT Monitor;
            public RECT Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int SetWindowLong(IntPtr window, int index, int value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr window, out RECT box);

        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr window, ref POINT point);

        [DllImport("user32.dll")]
        private static extern bool AdjustWindowRectEx(ref RECT box, uint style, bool menu, uint extendedStyle);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    }
}
