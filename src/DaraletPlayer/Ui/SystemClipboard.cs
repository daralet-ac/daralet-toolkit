using System;
using System.Runtime.InteropServices;
using System.Threading;
using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Text onto Windows' clipboard, straight through Windows: ImGui.SetClipboardText left it empty in the game, and can't
    /// say so. Another program can hold the clipboard for a moment, so it tries a few times.
    /// </summary>
    internal static class SystemClipboard {
        private const uint CF_UNICODETEXT = 13;
        private const uint GMEM_MOVEABLE = 0x0002;
        private const int Tries = 4;

        /// <summary>False if it couldn't, with a line in log.txt.</summary>
        public static bool Set(string text) {
            try {
                if (!Open()) {
                    PluginCore.LogFile($"[clipboard] Couldn't open the clipboard: {Marshal.GetLastWin32Error()}");
                    return false;
                }

                try {
                    var put = Put(text);

                    if (put) {
                        PluginCore.LogFile($"[clipboard] Copied {text.Length} characters");
                    }

                    return put;
                }
                finally {
                    CloseClipboard();
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[clipboard] {ex}");
                return false;
            }
        }

        /// <summary>The text on the clipboard, or "" for none or if it couldn't be read, with a line in log.txt.</summary>
        public static string Get() {
            try {
                if (!Open()) {
                    PluginCore.LogFile($"[clipboard] Couldn't open the clipboard: {Marshal.GetLastWin32Error()}");
                    return "";
                }

                try {
                    var memory = GetClipboardData(CF_UNICODETEXT);
                    var place = memory == IntPtr.Zero ? IntPtr.Zero : GlobalLock(memory);

                    if (place == IntPtr.Zero) {
                        return "";
                    }

                    var text = Marshal.PtrToStringUni(place);
                    GlobalUnlock(memory);
                    return text ?? "";
                }
                finally {
                    CloseClipboard();
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[clipboard] {ex}");
                return "";
            }
        }

        private static bool Open() {
            var window = CoreManager.Current.Decal.Hwnd;

            for (var i = 0; i < Tries; i++) {
                if (OpenClipboard(window)) {
                    return true;
                }

                Thread.Sleep(10);
            }

            return false;
        }

        private static bool Put(string text) {
            if (!EmptyClipboard()) {
                PluginCore.LogFile($"[clipboard] Couldn't empty the clipboard: {Marshal.GetLastWin32Error()}");
                return false;
            }

            var chars = text.ToCharArray();
            var memory = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)((chars.Length + 1) * 2));

            if (memory == IntPtr.Zero) {
                PluginCore.LogFile($"[clipboard] Couldn't get memory: {Marshal.GetLastWin32Error()}");
                return false;
            }

            var place = GlobalLock(memory);

            if (place == IntPtr.Zero) {
                PluginCore.LogFile($"[clipboard] Couldn't lock the memory: {Marshal.GetLastWin32Error()}");
                GlobalFree(memory);
                return false;
            }

            Marshal.Copy(chars, 0, place, chars.Length);
            Marshal.WriteInt16(place, chars.Length * 2, 0);
            GlobalUnlock(memory);

            // once this works the memory is Windows' to free, not ours
            if (SetClipboardData(CF_UNICODETEXT, memory) == IntPtr.Zero) {
                PluginCore.LogFile($"[clipboard] Couldn't set the clipboard: {Marshal.GetLastWin32Error()}");
                GlobalFree(memory);
                return false;
            }

            return true;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool OpenClipboard(IntPtr window);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool CloseClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EmptyClipboard();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetClipboardData(uint format, IntPtr memory);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetClipboardData(uint format);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr memory);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalFree(IntPtr memory);
    }
}
