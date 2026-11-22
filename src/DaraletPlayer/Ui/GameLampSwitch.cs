using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's own lamp lighting dimmed or switched off, so the plugin's lamp pass can light with shadows instead
    /// (LampLightOptions.Replace; the user's call, November 2026: lamps should light a room realistically, two at once,
    /// and spill out of doors and windows but not through walls). The game lights objects and indoor cells per vertex
    /// with Direct3D's fixed lights (Render::config_hardware_light, 0x59BD40), its lamps as point and spot lights, the
    /// sun and moon as a directional one; nothing blocks them, so a lantern upstairs lit the floor below through the
    /// boards. The device's LightEnable (IDirect3DDevice9 vtable 53) is pointed here while on: a point or spot light
    /// (D3DLIGHT9's Type, 1 or 2) at the place of a lamp the plugin lights with a shadow map (Places) gets the game's
    /// share of it: none is enabled as off, part has its colours scaled by the share first. SetLight (vtable 51) is
    /// pointed here too and keeps what the game set for each light, so the scaling is always from the game's own and
    /// never builds on itself. So a lamp passing between the game and the plugin crossfades: switched straight from one
    /// to the other, its light on characters and furniture jumped between the game's look and the plugin's as you walked
    /// past lamps (the user, November 2026: "a lot of flickering with object lighting, while moving around"). Any other
    /// light, and a directional one, passes. One vtable for every device of a kind, so patched once, put back when off
    /// and at shutdown. `[lamps]` lines in log.txt.
    /// </summary>
    internal static unsafe class GameLampSwitch {
        private const int SetLightSlot = 51;
        private const int GetLightSlot = 52;
        private const int LightEnableSlot = 53;

        /// <summary>D3DLIGHT9's size, and where its colours (Diffuse, Specular, Ambient, four floats each) and place are.</summary>
        private const int LightSize = 104;
        private const int ColoursAt = 4;
        private const int PlaceAt = 52;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int LightEnableCall(IntPtr device, int index, int enable);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int LightCall(IntPtr device, int index, byte* light);

        [DllImport("kernel32.dll")]
        private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protect, out uint old);

        // kept while the slots point at them
        private static LightEnableCall hook;
        private static LightCall setHook;
        private static LightEnableCall original;
        private static LightCall originalSet;
        private static LightCall getLight;
        private static IntPtr* table;
        private static IntPtr originalFunction;
        private static IntPtr originalSetFunction;
        private static int refused;
        private static int dimmed;
        private static int kept;
        private static int pointsLogged;
        private static bool logged;

        /// <summary>Each light as the game last set it, by index: what a dimmed one is scaled from.</summary>
        private static readonly Dictionary<int, byte[]> gameSet = new Dictionary<int, byte[]>();

        /// <summary>Indexes whose light the device holds scaled, to be set back as the game had it.</summary>
        private static readonly HashSet<int> scaled = new HashSet<int>();

        /// <summary>
        /// The lamps the plugin lights with shadow maps, in the device's world (east, height, north), and how much of each
        /// the game still gives (0 none, 1 all of it), set each frame by DepthShading; any other lamp is the game's.
        /// </summary>
        public static (Microsoft.DirectX.Vector3 Place, float GameShare)[] Places =
            new (Microsoft.DirectX.Vector3, float)[0];

        public static bool On => table != null;

        /// <summary>The game's share of a lamp at a place: 1 for one the plugin doesn't light.</summary>
        private static float GameShare(float x, float y, float z) {
            var places = Places;

            for (var i = 0; i < places.Length; i++) {
                var dx = places[i].Place.X - x;
                var dy = places[i].Place.Y - y;
                var dz = places[i].Place.Z - z;

                if (dx * dx + dy * dy + dz * dz < 0.25f) {
                    return places[i].GameShare;
                }
            }

            return 1;
        }

        /// <summary>The switch put in or taken out to follow what's wanted; the device's COM pointer given.</summary>
        public static void Follow(IntPtr device, bool wanted) {
            if (wanted == On || device == IntPtr.Zero) {
                return;
            }

            try {
                if (wanted) {
                    var vtable = *(IntPtr**)device;
                    original = Marshal.GetDelegateForFunctionPointer<LightEnableCall>(vtable[LightEnableSlot]);
                    originalSet = Marshal.GetDelegateForFunctionPointer<LightCall>(vtable[SetLightSlot]);
                    getLight = Marshal.GetDelegateForFunctionPointer<LightCall>(vtable[GetLightSlot]);
                    originalFunction = vtable[LightEnableSlot];
                    originalSetFunction = vtable[SetLightSlot];
                    hook = LightEnable;
                    setHook = SetLight;
                    gameSet.Clear();
                    scaled.Clear();
                    Write(&vtable[SetLightSlot], Marshal.GetFunctionPointerForDelegate(setHook));
                    Write(&vtable[LightEnableSlot], Marshal.GetFunctionPointerForDelegate(hook));
                    table = vtable;
                    PluginCore.LogFile("[lamps] the game's own lamp lighting handed over (point and spot lights)");
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
            Write(&table[SetLightSlot], originalSetFunction);
            table = null;
            PluginCore.LogFile($"[lamps] the game's own lamp lighting back ({refused} lights kept off, {dimmed} dimmed, "
                + $"{kept} left to the game)");
        }

        private static void Write(IntPtr* at, IntPtr value) {
            VirtualProtect((IntPtr)at, (UIntPtr)4, 0x04, out var old);
            *at = value;
            VirtualProtect((IntPtr)at, (UIntPtr)4, old, out _);
        }

        private static int SetLight(IntPtr device, int index, byte* light) {
            try {
                if (light != null) {
                    if (!gameSet.TryGetValue(index, out var copy)) {
                        copy = new byte[LightSize];
                        gameSet[index] = copy;
                    }

                    Marshal.Copy((IntPtr)light, copy, 0, LightSize);
                    scaled.Remove(index);
                }
            }
            catch (Exception) {
                // nothing may escape into the game's frame
            }

            return originalSet(device, index, light);
        }

        private static int LightEnable(IntPtr device, int index, int enable) {
            if (enable != 0) {
                try {
                    var light = stackalloc byte[LightSize];

                    if (gameSet.TryGetValue(index, out var game)) {
                        Marshal.Copy(game, 0, (IntPtr)light, LightSize);
                    }
                    else if (getLight(device, index, light) < 0) {
                        return original(device, index, enable);
                    }

                    var type = *(int*)light;

                    if (!logged) {
                        logged = true;
                        PluginCore.LogFile($"[lamps] first light the game enabled: {index}, type {type}");
                    }

                    var lamp = type == 1 || type == 2;
                    var place = (float*)(light + PlaceAt);
                    var share = lamp ? GameShare(place[0], place[1], place[2]) : 1;

                    if (lamp && pointsLogged < 4) {
                        pointsLogged++;
                        PluginCore.LogFile($"[lamps] the game lights a lamp at {place[0]:0.0} {place[1]:0.0} "
                            + $"{place[2]:0.0}; the game's share {share:0.##}");
                    }

                    if (share <= 0.01f) {
                        refused++;
                        return original(device, index, 0);
                    }

                    if (share < 0.99f) {
                        // the game's colours times its share, from what the game itself set (never from a scaled one)
                        var colours = (float*)(light + ColoursAt);

                        for (var c = 0; c < 12; c++) {
                            colours[c] *= share;
                        }

                        originalSet(device, index, light);
                        scaled.Add(index);
                        dimmed++;
                    }
                    else {
                        if (scaled.Remove(index) && game != null) {
                            // whole again: the device still held the scaled one
                            fixed (byte* whole = game) {
                                originalSet(device, index, whole);
                            }
                        }

                        kept += lamp ? 1 : 0;
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
