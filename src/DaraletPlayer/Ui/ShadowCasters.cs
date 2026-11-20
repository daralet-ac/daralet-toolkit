using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// What casts the sun's shadows: the game's own draws of its world, noted as it makes them (the user's call, November
    /// 2026: "especially sun light"). The plugin has none of the game's meshes, but a probe of a frame (Ui/DrawProbe.cs)
    /// showed the world drawn as about 8,300 DrawIndexedPrimitive calls from managed vertex buffers of 44-byte vertices
    /// (position, normal, colour, two texture coordinates), each placed by SetTransform(World), with the fixed-function
    /// pipeline; managed buffers outlive the frame, so each draw can be made again from the sun (DepthShading.Shadows.cs).
    /// <para>
    /// While sun shadows are on, the device's BeginScene (vtable 41; one vtable for every device in the process) is
    /// pointed here, and its SetTransform and DrawIndexedPrimitive (44, 82) at native stubs (ShadowStubs) that copy the
    /// World matrix and check the distance without leaving native code. From BeginScene to the game's see-through list
    /// (WorldPass), a DrawIndexedPrimitive whose World matrix places it near you (SetArea) comes here and has the rest
    /// of what it needs asked of the device (its vertex buffer, offset and stride, its index buffer, vertex declaration,
    /// texture and alpha test: Get calls, vtable 101, 105, 88, 64, 58), and one from a managed buffer of 44-byte vertices
    /// is noted. Every call the game makes into a hook crosses into the plugin, and the first build hooked eight
    /// (SetRenderState, SetTexture, SetVertexDeclaration, SetStreamSource and SetIndices too, to follow the state):
    /// tens of thousands a frame, about 14 fps at Holtburg with no shadow map made at all (seen in the game). The few
    /// hundred draws near you are all that need the rest. `[sun-shadows]` lines in log.txt.
    /// </para>
    /// </summary>
    internal static unsafe class ShadowCasters {
        public const int MaxDraws = 20000;

        private const int BeginSceneSlot = 41;
        private const int SetTransformSlot = 44;
        private const int GetRenderStateSlot = 58;
        private const int GetTextureSlot = 64;
        private const int SetTextureSlot = 65;
        private const int DrawIndexedPrimitiveSlot = 82;
        private const int SetVertexDeclarationSlot = 87;
        private const int GetVertexDeclarationSlot = 88;
        private const int SetStreamSourceSlot = 100;
        private const int GetStreamSourceSlot = 101;
        private const int SetIndicesSlot = 104;
        private const int GetIndicesSlot = 105;
        private const int VertexBufferGetDescSlot = 13;
        private const int AlphaTestEnable = 15;
        private const int ZWriteEnable = 14;
        private const int VertexStride = 44;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int OneCall(IntPtr device);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int TransformCall(IntPtr device, int state, float* matrix);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int TextureCall(IntPtr device, int stage, IntPtr texture);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int DrawIndexedCall(IntPtr device, int type, int baseVertex, int minIndex, int vertices,
            int startIndex, int count);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int PointerCall(IntPtr device, IntPtr thing);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        public delegate int StreamCall(IntPtr device, int stream, IntPtr buffer, int offset, int stride);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetStateCall(IntPtr device, int state, int* value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetTextureCall(IntPtr device, int stage, IntPtr* texture);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetPointerCall(IntPtr device, IntPtr* thing);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetStreamCall(IntPtr device, int stream, IntPtr* buffer, int* offset, int* stride);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetDescCall(IntPtr buffer, int* desc);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int RefCall(IntPtr thing);

        [DllImport("kernel32.dll")]
        private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protect, out uint old);

        private static readonly Dictionary<int, IntPtr> originals = new Dictionary<int, IntPtr>();

        // kept while patched, so they're never collected while the device can call them
        private static readonly List<Delegate> kept = new List<Delegate>();

        /// <summary>Whether each vertex buffer seen is a managed one, by its pointer (forgotten every so often).</summary>
        private static readonly Dictionary<IntPtr, bool> managed = new Dictionary<IntPtr, bool>();

        /// <summary>Each kind of object's Release, by its vtable: a Get call hands back a reference to let go.</summary>
        private static readonly Dictionary<IntPtr, RefCall> releases = new Dictionary<IntPtr, RefCall>();

        /// <summary>Records of this frame's draws, the first `count` in use; kept and filled again each frame.</summary>
        private static readonly List<ShadowDraw> pool = new List<ShadowDraw>();

        private static IntPtr* vtable;
        private static DateTime forgetBuffers;
        private static int count;

        private static OneCall beginScene;
        private static DrawIndexedCall drawIndexed;
        private static TextureCall setTexture;
        private static PointerCall setDeclaration;
        private static StreamCall setStream;
        private static PointerCall setIndices;
        private static GetStateCall getRenderState;
        private static GetTextureCall getTexture;
        private static GetPointerCall getDeclaration;
        private static GetStreamCall getStream;
        private static GetPointerCall getIndices;

        public static bool Hooked => vtable != null;

        /// <summary>The device's own calls, past the hooks, for the replay.</summary>
        public static DrawIndexedCall GameDrawIndexed => drawIndexed;

        public static StreamCall GameSetStream => setStream;

        public static PointerCall GameSetIndices => setIndices;

        public static TextureCall GameSetTexture => setTexture;

        public static PointerCall GameSetDeclaration => setDeclaration;

        /// <summary>How many draws this frame has noted so far (At gives each).</summary>
        public static int Count => count;

        public static void Hook(Device device) {
            if (Hooked) {
                return;
            }

            vtable = *(IntPtr**)(IntPtr)device.UnmanagedComPointer;
            setTexture = Function<TextureCall>(SetTextureSlot);
            setDeclaration = Function<PointerCall>(SetVertexDeclarationSlot);
            setStream = Function<StreamCall>(SetStreamSourceSlot);
            setIndices = Function<PointerCall>(SetIndicesSlot);
            getRenderState = Function<GetStateCall>(GetRenderStateSlot);
            getTexture = Function<GetTextureCall>(GetTextureSlot);
            getDeclaration = Function<GetPointerCall>(GetVertexDeclarationSlot);
            getStream = Function<GetStreamCall>(GetStreamSourceSlot);
            getIndices = Function<GetPointerCall>(GetIndicesSlot);

            if (!ShadowStubs.Make()) {
                PluginCore.LogFile("[sun-shadows] Windows gave no page for the stubs: no shadows");
                vtable = null;
                return;
            }

            // SetTransform and DrawIndexedPrimitive go through the native stubs (ShadowStubs), which only call the C#
            // hook for a draw near you
            drawIndexed = Function<DrawIndexedCall>(DrawIndexedPrimitiveSlot);
            DrawIndexedCall ours = DrawIndexed;
            kept.Add(ours);
            ShadowStubs.Point(vtable[SetTransformSlot], vtable[DrawIndexedPrimitiveSlot],
                Marshal.GetFunctionPointerForDelegate(ours));
            Patch<OneCall>(BeginSceneSlot, BeginScene, out beginScene);
            originals[SetTransformSlot] = vtable[SetTransformSlot];
            originals[DrawIndexedPrimitiveSlot] = vtable[DrawIndexedPrimitiveSlot];
            Write(SetTransformSlot, ShadowStubs.SetTransform);
            Write(DrawIndexedPrimitiveSlot, ShadowStubs.DrawIndexed);
            PluginCore.LogFile("[sun-shadows] noting the game's draws");
        }

        /// <summary>
        /// The device's own calls put back. A hook left in after the plugin's gone would crash the game: called at
        /// shutdown, and whenever sun shadows go off.
        /// </summary>
        public static void Unhook() {
            if (!Hooked) {
                return;
            }

            foreach (var slot in originals) {
                Write(slot.Key, slot.Value);
            }

            originals.Clear();
            vtable = null;
            ShadowStubs.Recording = false;
            ReleaseNoted();
            kept.Clear();
            managed.Clear();
            releases.Clear();
            PluginCore.LogFile("[sun-shadows] the game's draws no longer noted");
        }

        /// <summary>The game's see-through list is starting (WorldPass): the world's draws are all in.</summary>
        public static void EndOfWorld() {
            if (Hooked) {
                ShadowStubs.Recording = false;
            }
        }

        public static ShadowDraw At(int index) {
            return pool[index];
        }

        /// <summary>
        /// Only draws placed within reach of a place (the device's x and z, meters) are noted: a far one could only
        /// cast a shadow outside the maps, and noting tens of thousands a frame cost the most (seen in the game).
        /// </summary>
        public static void SetArea(float x, float z, float reach) {
            if (Hooked) {
                ShadowStubs.SetArea(x, z, reach * reach);
            }
        }

        private static T Function<T>(int slot) where T : Delegate {
            return Marshal.GetDelegateForFunctionPointer<T>(vtable[slot]);
        }

        private static void Patch<T>(int slot, T ours, out T original) where T : Delegate {
            originals[slot] = vtable[slot];
            original = Marshal.GetDelegateForFunctionPointer<T>(vtable[slot]);
            kept.Add(ours);
            Write(slot, Marshal.GetFunctionPointerForDelegate(ours));
        }

        private static void Write(int slot, IntPtr value) {
            var at = (IntPtr)(vtable + slot);
            VirtualProtect(at, (UIntPtr)4, 0x04, out var old);
            vtable[slot] = value;
            VirtualProtect(at, (UIntPtr)4, old, out _);
        }

        private static int BeginScene(IntPtr device) {
            // each frame's draws start afresh; the records are kept and filled again, not made anew
            ReleaseNoted();
            ShadowStubs.Recording = true;

            if (DateTime.UtcNow >= forgetBuffers) {
                forgetBuffers = DateTime.UtcNow.AddSeconds(10);
                managed.Clear();
            }

            return beginScene(device);
        }

        /// <summary>
        /// A draw noted, if it's near enough, holding its buffers, declaration and texture until the frame's replays are done
        /// (ReleaseNoted; the references the device's Get calls hand back: no AddRef of our own).
        /// </summary>
        private static int DrawIndexed(IntPtr device, int type, int baseVertex, int minIndex, int vertices,
            int startIndex, int primitives) {
            try {
                // only a draw near you, while the world is drawn, reaches here (ShadowStubs)
                if (!DrawProbe.Ours && count < MaxDraws) {
                    TryNote(device, type, baseVertex, minIndex, vertices, startIndex, primitives);
                }
            }
            catch {
                // nothing may escape into the game's frame
            }

            return drawIndexed(device, type, baseVertex, minIndex, vertices, startIndex, primitives);
        }

        private static void TryNote(IntPtr device, int type, int baseVertex, int minIndex, int vertices, int startIndex,
            int primitives) {
            IntPtr stream;
            int offset;
            int stride;
            getStream(device, 0, &stream, &offset, &stride);

            if (stream == IntPtr.Zero) {
                return;
            }

            if (stride != VertexStride || !IsManaged(stream)) {
                // counted for the lamps' report: what of the world the maps can't be drawn from
                if (stride != VertexStride) {
                    OtherStride++;
                    LastOtherStride = stride;
                }
                else {
                    NotManaged++;
                }

                Release(stream);
                return;
            }

            // the sky (its dome and the cloud layer, drawn round the camera, so always near) writes no depth, as the
            // see-through things don't: left out, or the clouds' shadows drifted across the ground (seen in the game)
            int writesDepth;
            getRenderState(device, ZWriteEnable, &writesDepth);

            if (writesDepth == 0) {
                Release(stream);
                return;
            }

            IntPtr indices;
            IntPtr declaration;
            IntPtr texture;
            int alphaTest;
            getIndices(device, &indices);
            getDeclaration(device, &declaration);
            getTexture(device, 0, &texture);
            getRenderState(device, AlphaTestEnable, &alphaTest);

            if (indices == IntPtr.Zero || declaration == IntPtr.Zero) {
                Release(stream);
                Release(indices);
                Release(declaration);
                Release(texture);
                return;
            }

            // the references the Get calls handed back are kept until the frame's replays are done (ReleaseNoted, at
            // the next BeginScene): the game can let a buffer go within the frame (something leaving), and a replay of a
            // draw holding none crashed the game in DrawInto (an AccessViolationException, November 2026)

            if (count == pool.Count) {
                pool.Add(new ShadowDraw { World = new float[16] });
            }

            var draw = pool[count++];
            draw.Stream = stream;
            draw.Offset = offset;
            draw.Indices = indices;
            draw.Texture = texture;
            draw.Declaration = declaration;
            draw.AlphaTest = alphaTest != 0;
            var world = ShadowStubs.World;

            for (var i = 0; i < 16; i++) {
                draw.World[i] = world[i];
            }
            draw.Type = type;
            draw.BaseVertex = baseVertex;
            draw.MinIndex = minIndex;
            draw.Vertices = vertices;
            draw.StartIndex = startIndex;
            draw.Count = primitives;
        }

        /// <summary>Near draws left out since last read: another vertex size (the last one's), or not a managed buffer.</summary>
        public static int OtherStride;
        public static int LastOtherStride;
        public static int NotManaged;

        private static bool IsManaged(IntPtr buffer) {
            if (managed.TryGetValue(buffer, out var known)) {
                return known;
            }

            var table = *(IntPtr**)buffer;
            var desc = stackalloc int[6];
            Marshal.GetDelegateForFunctionPointer<GetDescCall>(table[VertexBufferGetDescSlot])(buffer, desc);
            var isManaged = desc[3] == 1;
            managed[buffer] = isManaged;
            return isManaged;
        }

        /// <summary>
        /// The references every noted draw holds let go, and the records emptied: once the frame's replays are done
        /// (DepthShading), at the next BeginScene, and before a device reset, which fails while anything holds a
        /// default-pool resource (some of the game's textures and index buffers are): the game said "Could not initialize
        /// Direct3D" and stopped (seen in the game, November 2026, when they were held till the next frame).
        /// </summary>
        public static void ReleaseNoted() {
            for (var i = 0; i < count; i++) {
                var draw = pool[i];
                Release(draw.Stream);
                Release(draw.Indices);
                Release(draw.Declaration);
                Release(draw.Texture);
            }

            count = 0;
        }

        /// <summary>A reference a Get call handed back let go, through that kind's own Release (kept by vtable).</summary>
        private static void Release(IntPtr thing) {
            if (thing == IntPtr.Zero) {
                return;
            }

            var table = *(IntPtr*)thing;

            if (!releases.TryGetValue(table, out var release)) {
                release = Marshal.GetDelegateForFunctionPointer<RefCall>(((IntPtr*)table)[2]);
                releases[table] = release;
            }

            release(thing);
        }
    }
}
