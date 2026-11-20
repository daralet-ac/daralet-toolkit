using System;
using System.Runtime.InteropServices;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's Direct3D device and address space watched alongside the frame rate (November 2026). With scenery at
    /// 14 landblocks the screen went black three times, each about 15 seconds after the last ring loaded: a flat 9 fps
    /// (110 ms every frame) for about 35 seconds, then back by itself, with memory to spare and whatever Detail was.
    /// That looks like a lost device (the game drawing nothing and waiting about 100 ms before trying to reset it), and a
    /// 32-bit Direct3D 9 game loses its device when it can't fit its graphics into its address space, which can be in
    /// pieces while plenty is free in all. So each second the device's state (Device.CheckCooperativeLevel) is noted, a
    /// change of it going in log.txt, and every 10 seconds the address space's free room and largest free piece
    /// (VirtualQuery over the whole of it). `[device]` lines in log.txt. Untested in the game when written.
    /// </summary>
    internal sealed partial class FrameRateWindow {
        private const int DeviceLost = unchecked((int)0x88760868);
        private const int DeviceNotReset = unchecked((int)0x88760869);
        private const int DriverInternalError = unchecked((int)0x88760827);
        private const uint MemFree = 0x10000;

        private int lastDeviceState;
        private bool deviceUnreadable;
        private double nextSpaceLine;
        private bool? wasInFront;

        /// <summary>
        /// The device's state, logged when it changes; whether the game's window is in front, logged when it changes
        /// (the game slows itself to about 9 fps behind another window, the same flat 110 ms a frame first taken for the
        /// black screen); and the address space every 10 seconds.
        /// </summary>
        private void WatchDevice(double now) {
            try {
                var game = GetAncestor(Decal.Adapter.CoreManager.Current.Decal.Hwnd, 2);
                var inFront = GetForegroundWindow() == game;

                if (wasInFront != inFront) {
                    PluginCore.LogFile(inFront ? "[device] the game's window is in front"
                        : "[device] the game's window is behind another (the game slows itself there)");
                    wasInFront = inFront;
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile("[device] " + ex.Message);
            }

            if (!deviceUnreadable) {
                try {
                    var device = UBService.Huds.D3Ddevice;

                    if (device != null) {
                        device.CheckCooperativeLevel(out var state);

                        if (state != lastDeviceState) {
                            PluginCore.LogFile($"[device] Direct3D {DeviceStateName(state)} (was "
                                + $"{DeviceStateName(lastDeviceState)})");
                            lastDeviceState = state;
                        }
                    }
                }
                catch (Exception ex) {
                    deviceUnreadable = true;
                    PluginCore.LogFile("[device] can't read the device's state: " + ex.Message);
                }
            }

            if (now >= nextSpaceLine) {
                nextSpaceLine = now + 10;

                try {
                    PluginCore.LogFile("[device] " + AddressSpace());
                }
                catch (Exception ex) {
                    // never at the frame rate log's cost: it's written after this
                    PluginCore.LogFile("[device] can't read the address space: " + ex.Message);
                }
            }
        }

        private static string DeviceStateName(int state) {
            switch (state) {
                case 0:
                    return "fine";
                case DeviceLost:
                    return "device lost";
                case DeviceNotReset:
                    return "device lost, waiting to be reset";
                case DriverInternalError:
                    return "driver internal error";
                default:
                    return $"0x{state:X8}";
            }
        }

        /// <summary>"address space: 1323 MB free, largest piece 210 MB, 412 pieces of 1 MB or more".</summary>
        private static string AddressSpace() {
            var (free, largest, pieces) = FreePieces();
            return $"address space: {free / (1024 * 1024)} MB free, largest piece {largest / (1024 * 1024)} MB, "
                + $"{pieces} pieces of 1 MB or more";
        }

        /// <summary>The address space's free room, its largest piece, both in bytes, and its pieces of 1 MB or more.</summary>
        internal static (long Free, long Largest, int Pieces) FreePieces() {
            long free = 0;
            long largest = 0;
            var pieces = 0;
            var at = IntPtr.Zero;
            var size = (UIntPtr)Marshal.SizeOf<MemoryBasicInformation>();

            // a 32-bit process, large address aware: up to 4 GB to walk
            while (VirtualQuery(at, out var region, size) != UIntPtr.Zero) {
                var length = (long)region.RegionSize.ToUInt64();

                if (region.State == MemFree) {
                    free += length;
                    largest = Math.Max(largest, length);

                    if (length >= 1024 * 1024) {
                        pieces++;
                    }
                }

                // a 32-bit IntPtr is signed: addresses past 2 GB come out negative, and making one of them from a long
                // threw OverflowException every 10 seconds (the first build), so they go through uint
                var next = (long)(uint)region.BaseAddress.ToInt32() + length;

                if (length <= 0 || next >= 0x100000000L) {
                    break;
                }

                at = new IntPtr(unchecked((int)(uint)next));
            }

            return (free, largest, pieces);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryBasicInformation {
            public IntPtr BaseAddress;
            public IntPtr AllocationBase;
            public uint AllocationProtect;
            public UIntPtr RegionSize;
            public uint State;
            public uint Protect;
            public uint Type;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetAncestor(IntPtr window, uint flags);

        [DllImport("kernel32.dll")]
        private static extern UIntPtr VirtualQuery(IntPtr address, out MemoryBasicInformation buffer, UIntPtr length);
    }
}
