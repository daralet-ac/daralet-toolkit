using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's own lamp lighting dimmed while the plugin lights those lamps with shadows (LampLightOptions.Shadows;
    /// the user's call, November 2026: lamps should light a room realistically, two at once, and spill out of doors and
    /// windows but not through walls). The game lights objects and indoor cells per vertex with Direct3D's fixed lights,
    /// its lamps as point and spot lights, the sun and moon as a directional one; nothing blocks them, so a lantern
    /// upstairs lit the floor below through the boards.
    /// <para>
    /// How the game sets its lights: Render::enable_active_lights (0x54CC90) puts each of its eight light slots' light
    /// on the device with SetLight (IDirect3DDevice9 vtable 51) every time, but switches a slot on or off through
    /// RenderDeviceD3D::SetFFLightEnable (0x5A4230), which calls LightEnable (vtable 53) only when that slot's state
    /// changes, remembering it at +0x4C9 of the RenderDeviceD3D (the pointer at 0x870340). The first build of this
    /// dimmed lamps in LightEnable and switched some slots off there: the game, still taking them for on, never asked
    /// again, so whatever light it put in such a slot later (another lamp, the sun) stayed dark, and which slots were
    /// caught changed as you moved. Hebian-to's crystals went see-through and every lamp in view dark at one step, and
    /// lit again at the next (the user's screenshots, November 2026). So the dimming is all in SetLight now: a point or
    /// spot light (D3DLIGHT9's Type, 1 or 2) at the place of a lamp the plugin lights (Places) has its colours scaled
    /// by the game's share, to black for none; no slot is ever switched, and the slots' states are put back to what
    /// the game remembers when this starts and stops (Resync), in case an earlier build left some off.
    /// </para>
    /// <para>
    /// The game draws its see-through things (crystals, lifestones, halos) from its alpha list with whatever lights
    /// are on at the time (D3DPolyRender::FlushAlphaList, 0x59E3F0, sets only each one's matrix and material), and they
    /// come after the plugin's lamp light, which can't light them. So around every flush of that list (WorldPass) the
    /// lamps go back to the game's own (BeginSeeThrough), and after it to the plugin's share (EndSeeThrough): dimmed
    /// with the rest, the spire's glow and the crystals went dark (the user's screenshots, November 2026).
    /// </para>
    /// One vtable for every device of a kind, so patched once, put back when off and at shutdown. `[lamps]` lines in
    /// log.txt.
    /// </summary>
    internal static unsafe class GameLampSwitch {
        private const int SetLightSlot = 51;
        private const int LightEnableSlot = 53;

        /// <summary>D3DLIGHT9's size, and where its colours (Diffuse, Specular, Ambient, four floats each) and place are.</summary>
        private const int LightSize = 104;
        private const int ColoursAt = 4;
        private const int PlaceAt = 52;

        /// <summary>
        /// The game's RenderDeviceD3D (the pointer at this address), its Direct3D device (+0x468) and the on or off it
        /// last set each of its eight light slots to (+0x4C9, a byte each).
        /// </summary>
        private const uint RenderDeviceAt = 0x870340;
        private const int Direct3DAt = 0x468;
        private const int SlotStatesAt = 0x4C9;
        private const int LightSlots = 8;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int LightEnableCall(IntPtr device, int index, int enable);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int LightCall(IntPtr device, int index, byte* light);

        [DllImport("kernel32.dll")]
        private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protect, out uint old);

        // kept while the slot points at it
        private static LightCall setHook;
        private static LightCall originalSet;
        private static IntPtr* table;
        private static IntPtr originalSetFunction;
        private static IntPtr lastDevice;
        private static int dimmed;
        private static int darkened;
        private static int kept;
        private static int pointsLogged;
        private static int seeThroughDepth;

        /// <summary>Each light as the game last set it, by index: what a dimmed one is scaled from.</summary>
        private static readonly Dictionary<int, byte[]> gameSet = new Dictionary<int, byte[]>();

        /// <summary>Indexes whose light the device holds scaled.</summary>
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
                    originalSet = Marshal.GetDelegateForFunctionPointer<LightCall>(vtable[SetLightSlot]);
                    originalSetFunction = vtable[SetLightSlot];
                    setHook = SetLight;
                    gameSet.Clear();
                    scaled.Clear();
                    Write(&vtable[SetLightSlot], Marshal.GetFunctionPointerForDelegate(setHook));
                    table = vtable;
                    var resynced = Resync();
                    PluginCore.LogFile("[lamps] the game's own lamp lighting dimmed by the plugin's share (point and spot "
                        + $"lights, in SetLight); {resynced} light slots put back as the game has them");
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

            Write(&table[SetLightSlot], originalSetFunction);
            table = null;
            seeThroughDepth = 0;
            var resynced = Resync();
            PluginCore.LogFile($"[lamps] the game's own lamp lighting back ({darkened} lights set dark, {dimmed} dimmed, "
                + $"{kept} left to the game; {resynced} light slots put back as the game has them)");
        }

        /// <summary>
        /// Each of the game's eight light slots switched on or off on the device as the game remembers it, so none is
        /// left off behind its back (an earlier build of this switched slots off in LightEnable). How many it set.
        /// </summary>
        private static int Resync() {
            try {
                var renderDevice = *(byte**)RenderDeviceAt;

                if (renderDevice == null) {
                    return 0;
                }

                var direct3D = *(IntPtr*)(renderDevice + Direct3DAt);

                if (direct3D == IntPtr.Zero) {
                    return 0;
                }

                var enable = Marshal.GetDelegateForFunctionPointer<LightEnableCall>((*(IntPtr**)direct3D)[LightEnableSlot]);

                for (var slot = 0; slot < LightSlots; slot++) {
                    enable(direct3D, slot, renderDevice[SlotStatesAt + slot] != 0 ? 1 : 0);
                }

                return LightSlots;
            }
            catch (Exception ex) {
                PluginCore.LogFile("[lamps] couldn't put the light slots back: " + ex.Message);
                return 0;
            }
        }

        /// <summary>
        /// The game's see-through things are about to be drawn (WorldPass): its lamps whole for them, since they come
        /// after the plugin's lamp light and are drawn with whatever lights are on. Nested calls count.
        /// </summary>
        public static void BeginSeeThrough() {
            if (table == null) {
                return;
            }

            seeThroughDepth++;

            if (seeThroughDepth != 1 || lastDevice == IntPtr.Zero) {
                return;
            }

            try {
                foreach (var index in scaled) {
                    if (gameSet.TryGetValue(index, out var game)) {
                        fixed (byte* whole = game) {
                            originalSet(lastDevice, index, whole);
                        }
                    }
                }
            }
            catch (Exception) {
                // nothing may escape into the game's frame
            }
        }

        /// <summary>The see-through things are drawn: the lamps the plugin lights back to the game's share of them.</summary>
        public static void EndSeeThrough() {
            if (table == null || seeThroughDepth == 0) {
                return;
            }

            seeThroughDepth--;

            if (seeThroughDepth != 0 || lastDevice == IntPtr.Zero) {
                return;
            }

            try {
                var light = stackalloc byte[LightSize];

                foreach (var index in scaled) {
                    if (gameSet.TryGetValue(index, out var game)) {
                        Marshal.Copy(game, 0, (IntPtr)light, LightSize);
                        var place = (float*)(light + PlaceAt);
                        Scale(light, GameShare(place[0], place[1], place[2]));
                        originalSet(lastDevice, index, light);
                    }
                }
            }
            catch (Exception) {
                // nothing may escape into the game's frame
            }
        }

        private static void Write(IntPtr* at, IntPtr value) {
            VirtualProtect((IntPtr)at, (UIntPtr)4, 0x04, out var old);
            *at = value;
            VirtualProtect((IntPtr)at, (UIntPtr)4, old, out _);
        }

        private static void Scale(byte* light, float share) {
            var colours = (float*)(light + ColoursAt);

            for (var c = 0; c < 12; c++) {
                colours[c] *= share;
            }
        }

        private static int SetLight(IntPtr device, int index, byte* light) {
            try {
                if (light != null) {
                    lastDevice = device;

                    if (!gameSet.TryGetValue(index, out var copy)) {
                        copy = new byte[LightSize];
                        gameSet[index] = copy;
                    }

                    Marshal.Copy((IntPtr)light, copy, 0, LightSize);
                    var type = *(int*)light;
                    var place = (float*)(light + PlaceAt);
                    var lamp = type == 1 || type == 2;
                    // see-through things keep the game's lamps whole (BeginSeeThrough)
                    var share = lamp && seeThroughDepth == 0 ? GameShare(place[0], place[1], place[2]) : 1;

                    if (lamp && pointsLogged < 4) {
                        pointsLogged++;
                        PluginCore.LogFile($"[lamps] the game lights a lamp at {place[0]:0.0} {place[1]:0.0} "
                            + $"{place[2]:0.0}; the game's share {share:0.##}");
                    }

                    if (share < 0.99f) {
                        // the game's colours times its share, to black for none: the slot stays on, as the game
                        // believes it is (see the class)
                        var scaledLight = stackalloc byte[LightSize];
                        Marshal.Copy(copy, 0, (IntPtr)scaledLight, LightSize);
                        Scale(scaledLight, share);
                        scaled.Add(index);

                        if (share <= 0.01f) {
                            darkened++;
                        }
                        else {
                            dimmed++;
                        }

                        return originalSet(device, index, scaledLight);
                    }

                    scaled.Remove(index);
                    kept += lamp ? 1 : 0;
                }
            }
            catch (Exception) {
                // nothing may escape into the game's frame: the light as the game asked
            }

            return originalSet(device, index, light);
        }
    }
}
