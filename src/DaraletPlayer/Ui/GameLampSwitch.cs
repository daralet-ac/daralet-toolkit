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
    /// (D3DLIGHT9's Type, 1 or 2, read with GetLight, vtable 52) at the place of a lamp the plugin lights with a shadow
    /// map (Places) is enabled as off; any other, and a directional one, passes: switching every lamp off left those
    /// past the six nearest you lighting nothing. One vtable
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
        private static int kept;
        private static int pointsLogged;

        /// <summary>
        /// Where the lamps the plugin lights with shadow maps are, in the device's world (east, height, north), set each
        /// frame by DepthShading; the game's own light of a lamp anywhere else is left on.
        /// </summary>
        public static Microsoft.DirectX.Vector3[] Places = new Microsoft.DirectX.Vector3[0];

        private static bool Handled(float x, float y, float z) {
            var places = Places;

            for (var i = 0; i < places.Length; i++) {
                var dx = places[i].X - x;
                var dy = places[i].Y - y;
                var dz = places[i].Z - z;

                if (dx * dx + dy * dy + dz * dz < 0.25f) {
                    return true;
                }
            }

            return false;
        }
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
            PluginCore.LogFile($"[lamps] the game's own lamp lighting back ({refused} lights kept off, {kept} left to the game)");
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

                        // only the lamps the plugin lights with a shadow map (by place, D3DLIGHT9's Position at +52): the
                        // rest, past the six nearest you, are left to the game, or a dungeon's far lamps lit nothing
                        if ((type == 1 || type == 2) && pointsLogged < 4) {
                            pointsLogged++;
                            PluginCore.LogFile($"[lamps] the game lights a lamp at {*(float*)(light + 52):0.0} "
                                + $"{*(float*)(light + 56):0.0} {*(float*)(light + 60):0.0}; one of ours: "
                                + Handled(*(float*)(light + 52), *(float*)(light + 56), *(float*)(light + 60)));
                        }

                        if ((type == 1 || type == 2) && Handled(*(float*)(light + 52), *(float*)(light + 56),
                            *(float*)(light + 60))) {
                            refused++;
                            return original(device, index, 0);
                        }

                        kept += type == 1 || type == 2 ? 1 : 0;
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
