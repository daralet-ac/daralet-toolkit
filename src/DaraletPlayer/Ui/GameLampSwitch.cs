using System;
using System.Runtime.InteropServices;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's own lamp lighting switched off, so the plugin's lamp pass can light with shadows instead
    /// (LampLightOptions.Replace; the user's call, November 2026: lamps should light a room realistically, two at once,
    /// and spill out of doors and windows but not through walls). The game lights objects and indoor cells per vertex
    /// with Direct3D's fixed lights (Render::config_hardware_light, 0x59BD40), its lamps as point and spot lights, the
    /// sun and moon as a directional one; nothing blocks them, so a lantern upstairs lit the floor below through the
    /// boards. The device's LightEnable (IDirect3DDevice9 vtable 53) is pointed here while on: a point or spot light
    /// (D3DLIGHT9's Type, 1 or 2, read with GetLight, vtable 52) is enabled as off, a directional one passes. One vtable
    /// for every device of a kind, so patched once, put back when off and at shutdown. `[lamps]` lines in log.txt.
    /// Untested in the game when written.
    /// </summary>
    internal static unsafe class GameLampSwitch {
        private const int GetLightSlot = 52;
        private const int LightEnableSlot = 53;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int LightEnableCall(IntPtr device, int index, int enable);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetLightCall(IntPtr device, int index, byte* light);

        [DllImport("kernel32.dll")]
        private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protect, out uint old);

        // kept while the slot points at it
        private static LightEnableCall hook;
        private static LightEnableCall original;
        private static GetLightCall getLight;
        private static IntPtr* table;
        private static IntPtr originalFunction;
        private static int refused;
        private static bool logged;

        public static bool On => table != null;

        /// <summary>The switch put in or taken out to follow what's wanted; the device's COM pointer given.</summary>
        public static void Follow(IntPtr device, bool wanted) {
            if (wanted == On || device == IntPtr.Zero) {
                return;
            }

            try {
                if (wanted) {
                    var vtable = *(IntPtr**)device;
                    original = Marshal.GetDelegateForFunctionPointer<LightEnableCall>(vtable[LightEnableSlot]);
                    getLight = Marshal.GetDelegateForFunctionPointer<GetLightCall>(vtable[GetLightSlot]);
                    originalFunction = vtable[LightEnableSlot];
                    hook = LightEnable;
                    Write(&vtable[LightEnableSlot], Marshal.GetFunctionPointerForDelegate(hook));
                    table = vtable;
                    PluginCore.LogFile("[lamps] the game's own lamp lighting switched off (point and spot lights)");
                }
                else {
                    Release();
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile("[lamps] " + ex.Message);
            }
        }

        public static void Release() {
            if (table == null) {
                return;
            }

            Write(&table[LightEnableSlot], originalFunction);
            table = null;
            PluginCore.LogFile($"[lamps] the game's own lamp lighting back ({refused} lights kept off)");
        }

        private static void Write(IntPtr* at, IntPtr value) {
            VirtualProtect((IntPtr)at, (UIntPtr)4, 0x04, out var old);
            *at = value;
            VirtualProtect((IntPtr)at, (UIntPtr)4, old, out _);
        }

        private static int LightEnable(IntPtr device, int index, int enable) {
            if (enable != 0) {
                try {
                    // D3DLIGHT9: Type first (1 point, 2 spot, 3 directional); 104 bytes
                    var light = stackalloc byte[104];

                    if (getLight(device, index, light) >= 0) {
                        var type = *(int*)light;

                        if (!logged) {
                            logged = true;
                            PluginCore.LogFile($"[lamps] first light the game enabled: {index}, type {type}");
                        }

                        if (type == 1 || type == 2) {
                            refused++;
                            return original(device, index, 0);
                        }
                    }
                }
                catch (Exception) {
                    // nothing may escape into the game's frame: the light as the game asked
                }
            }

            return original(device, index, enable);
        }
    }
}
