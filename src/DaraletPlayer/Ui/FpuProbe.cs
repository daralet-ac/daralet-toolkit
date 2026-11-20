using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A test run's look at the processor's floating-point settings round WorldPass's hook (`set worldpasshook 3`): the
    /// teleport crashes came with the hook in even when it did nothing but call the game's own function (November 2026),
    /// and one guess is that the switch into .NET changes the x87 control word or the SSE state the game's loading relies
    /// on. Checking that from .NET alone can't work (the switch could change and put back what's read), so the game's call
    /// at 0x453C2B is pointed at a native stub of our own, which stores the x87 control word (fnstcw) and MXCSR (stmxcsr)
    /// just before calling the plugin's managed function and just after it returns, then goes back to the game. The
    /// managed side reads them every call, with its own reading from inside .NET, and logs each new combination.
    /// `[fpu]` lines in log.txt. Test only.
    /// </summary>
    internal static unsafe class FpuProbe {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void ReadCall(IntPtr into);

        [DllImport("kernel32.dll")]
        private static extern IntPtr VirtualAlloc(IntPtr address, UIntPtr size, uint type, uint protect);

        [DllImport("kernel32.dll")]
        private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protect, out uint old);

        // the stub and its data, made once and never freed (a game frame may still be in it)
        private static byte* page;
        private static uint* data;
        private static ReadCall read;
        private static readonly HashSet<string> seen = new HashSet<string>();
        private static int callSite;
        private static int originalRel;
        private static bool installed;
        private static long calls;

        public static bool Installed => installed;

        /// <summary>Points the call at `site` (an E8 rel32) at the stub, which calls `managed` (cdecl, one int).</summary>
        public static bool Install(int site, IntPtr managed) {
            if (installed) {
                return true;
            }

            var at = (byte*)site;

            if (at[0] != 0xE8) {
                PluginCore.LogFile($"[fpu] 0x{site:X8} isn't a call: left alone");
                return false;
            }

            if (page == null) {
                page = (byte*)VirtualAlloc(IntPtr.Zero, (UIntPtr)4096, 0x3000, 0x40);
                data = (uint*)(page + 2048);
                var reader = page + 1024;
                // the reader: fnstcw [eax], stmxcsr [eax+4], with eax the pointer passed (cdecl)
                var r = new byte[] { 0x8B, 0x44, 0x24, 0x04, 0xD9, 0x38, 0x0F, 0xAE, 0x58, 0x04, 0xC3 };
                Marshal.Copy(r, 0, (IntPtr)reader, r.Length);
                read = Marshal.GetDelegateForFunctionPointer<ReadCall>((IntPtr)reader);
            }

            var code = new List<byte>();
            // fnstcw [data+0]; stmxcsr [data+4]
            code.AddRange(new byte[] { 0xD9, 0x3D }); code.AddRange(BitConverter.GetBytes((uint)data));
            code.AddRange(new byte[] { 0x0F, 0xAE, 0x1D }); code.AddRange(BitConverter.GetBytes((uint)(data + 1)));
            // push dword [esp+4]: the game's argument again, for the managed function
            code.AddRange(new byte[] { 0xFF, 0x74, 0x24, 0x04 });
            // call managed (rel32 from the end of this instruction)
            var callAt = (uint)page + (uint)code.Count;
            code.Add(0xE8); code.AddRange(BitConverter.GetBytes((uint)managed - (callAt + 5)));
            // add esp,4 (cdecl: the caller tidies)
            code.AddRange(new byte[] { 0x83, 0xC4, 0x04 });
            // fnstcw [data+8]; stmxcsr [data+12]
            code.AddRange(new byte[] { 0xD9, 0x3D }); code.AddRange(BitConverter.GetBytes((uint)(data + 2)));
            code.AddRange(new byte[] { 0x0F, 0xAE, 0x1D }); code.AddRange(BitConverter.GetBytes((uint)(data + 3)));
            code.Add(0xC3);
            Marshal.Copy(code.ToArray(), 0, (IntPtr)page, code.Count);

            callSite = site;
            originalRel = *(int*)(at + 1);
            VirtualProtect((IntPtr)at, (UIntPtr)5, 0x40, out var old);
            *(int*)(at + 1) = (int)((uint)page - (uint)(site + 5));
            VirtualProtect((IntPtr)at, (UIntPtr)5, old, out _);
            installed = true;
            PluginCore.LogFile($"[fpu] probing round 0x{site:X8}");
            return true;
        }

        public static void Uninstall() {
            if (!installed) {
                return;
            }

            var at = (byte*)callSite;
            VirtualProtect((IntPtr)at, (UIntPtr)5, 0x40, out var old);
            *(int*)(at + 1) = originalRel;
            VirtualProtect((IntPtr)at, (UIntPtr)5, old, out _);
            installed = false;
            PluginCore.LogFile($"[fpu] probe taken out after {calls} calls");
        }

        /// <summary>From the managed function, each call: what the stub saw before it, against .NET's own view.</summary>
        public static void Note() {
            if (!installed) {
                return;
            }

            calls++;
            var inside = stackalloc uint[2];
            read((IntPtr)inside);
            var beforeX87 = data[0] & 0xFFFF;
            var beforeSse = data[1];
            var key = $"before x87 {beforeX87:X4} sse {beforeSse:X8}; inside .NET x87 {inside[0] & 0xFFFF:X4} sse "
                + $"{inside[1]:X8}; last call's after x87 "
                + (calls == 1 ? "none" : $"{data[2] & 0xFFFF:X4} sse {data[3]:X8}");

            if (seen.Count < 40 && seen.Add(key)) {
                PluginCore.LogFile($"[fpu] call {calls}: {key}");
            }
        }
    }
}
