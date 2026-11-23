using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Casters kept from frame to frame. The game draws only what's in the camera's view, so a tree just off the screen
    /// cast no shadow, and turning the camera a little put its shadow on the ground or took it away (the user's
    /// screenshots, November 2026: "I turn slightly to the right and the tree shadow turns on"). So a draw is kept, holding
    /// its buffers, for KeepSeconds after it was last seen, while it's within reach of the maps, and replayed with this
    /// frame's. A draw is known by its mesh (buffers and range) and where it's placed; something that moves (a creature,
    /// your own character, a swinging part) shows as its mesh drawn again just beside where it was the frame before and
    /// not there any more, and such a mesh is only ever kept while it's still being drawn, or it would leave shadows
    /// behind it. Everything kept is let go before a device reset (ReleaseAll) and when the device's world moves to
    /// another landblock (Origin).
    /// </summary>
    internal static partial class ShadowCasters {
        /// <summary>How long a caster out of view is kept, in seconds.</summary>
        private const double KeepSeconds = 30;

        /// <summary>A mesh drawn again this near where it was the frame before, and not there any more, has moved.</summary>
        private const float MovedWithin = 1;

        /// <summary>How often the meshes seen moving are forgotten, in seconds (a buffer may hold something else by then).</summary>
        private const double ForgetMovingSeconds = 60;

        private static readonly Dictionary<CasterKey, int> byKey = new Dictionary<CasterKey, int>();
        private static readonly HashSet<MeshKey> moving = new HashSet<MeshKey>();
        private static readonly Dictionary<MeshKey, List<ShadowDraw>> fresh = new Dictionary<MeshKey, List<ShadowDraw>>();
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static int frame;
        private static bool notedSince;
        private static double forgetMovingAt = ForgetMovingSeconds;
        private static float keepX;
        private static float keepZ;
        private static float keepReachSquared = float.MaxValue;
        private static uint origin;

        /// <summary>A mesh: its buffers and the range of them drawn.</summary>
        public struct MeshKey : IEquatable<MeshKey> {
            public IntPtr Stream;
            public IntPtr Indices;
            public int Offset;
            public int BaseVertex;
            public int StartIndex;
            public int Count;

            public bool Equals(MeshKey o) {
                return Stream == o.Stream && Indices == o.Indices && Offset == o.Offset && BaseVertex == o.BaseVertex
                    && StartIndex == o.StartIndex && Count == o.Count;
            }

            public override bool Equals(object obj) {
                return obj is MeshKey o && Equals(o);
            }

            public override int GetHashCode() {
                unchecked {
                    var h = Stream.GetHashCode();
                    h = h * 31 + Indices.GetHashCode();
                    h = h * 31 + Offset;
                    h = h * 31 + BaseVertex;
                    h = h * 31 + StartIndex;
                    return h * 31 + Count;
                }
            }
        }

        /// <summary>A mesh where it's placed: its place to 5 cm and its turn, roughly.</summary>
        public struct CasterKey : IEquatable<CasterKey> {
            public MeshKey Mesh;
            public int X;
            public int Y;
            public int Z;
            public int Turn;

            public bool Equals(CasterKey o) {
                return X == o.X && Y == o.Y && Z == o.Z && Turn == o.Turn && Mesh.Equals(o.Mesh);
            }

            public override bool Equals(object obj) {
                return obj is CasterKey o && Equals(o);
            }

            public override int GetHashCode() {
                unchecked {
                    var h = Mesh.GetHashCode();
                    h = h * 31 + X;
                    h = h * 31 + Y;
                    h = h * 31 + Z;
                    return h * 31 + Turn;
                }
            }
        }

        /// <summary>
        /// Where casters are kept: within reach (meters across the ground) of a place, the device's x and z; further
        /// ones out of view are let go.
        /// </summary>
        public static void SetKeepArea(float x, float z, float reach) {
            keepX = x;
            keepZ = z;
            keepReachSquared = reach * reach;
        }

        /// <summary>
        /// The landblock the device's world is measured from this frame (RenderOrigin): when it changes, every place kept
        /// from before is off by the landblocks between, so all but this frame's draws are let go.
        /// </summary>
        public static void Origin(uint landblock) {
            PurgeWritten();

            if (landblock == origin) {
                return;
            }

            origin = landblock;

            for (var i = count - 1; i >= 0; i--) {
                if (pool[i].SeenFrame != frame) {
                    RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// The record for a draw just seen: the one kept for it (its old references let go, the caller fills it), or a new
        /// one; null when there's no room.
        /// </summary>
        private static unsafe ShadowDraw Record(IntPtr stream, int offset, IntPtr indices, int baseVertex, int startIndex,
            int primitives, float* world) {
            var mesh = new MeshKey {
                Stream = stream, Indices = indices, Offset = offset, BaseVertex = baseVertex, StartIndex = startIndex,
                Count = primitives,
            };
            var key = new CasterKey {
                Mesh = mesh,
                X = (int)Math.Round(world[12] * 20),
                Y = (int)Math.Round(world[13] * 20),
                Z = (int)Math.Round(world[14] * 20),
                Turn = (int)Math.Round(world[0] * 64) * 1000 + (int)Math.Round(world[2] * 64),
            };
            ShadowDraw draw;

            if (byKey.TryGetValue(key, out var at)) {
                draw = pool[at];
                Release(draw.Stream);
                Release(draw.Indices);
                Release(draw.Declaration);
                Release(draw.Texture);
            }
            else {
                if (count >= MaxDraws) {
                    return null;
                }

                if (count == pool.Count) {
                    pool.Add(new ShadowDraw { World = new float[16] });
                }

                draw = pool[count];
                byKey[key] = count;
                count++;
                draw.Mesh = mesh;
                draw.Key = key;
                draw.FirstFrame = frame;
            }

            draw.SeenFrame = frame;
            draw.SeenAt = clock.Elapsed.TotalSeconds;
            notedSince = true;
            return draw;
        }

        /// <summary>
        /// The frame's replays are done: the meshes seen moving found, and what's no longer wanted let go (moving and
        /// out of view, out of view too long, or out of reach). Once a frame, however often it's called.
        /// </summary>
        public static void EndFrame() {
            if (!notedSince) {
                return;
            }

            notedSince = false;
            var last = frame;
            frame++;
            FindMoving(last);
            var now = clock.Elapsed.TotalSeconds;

            for (var i = count - 1; i >= 0; i--) {
                var d = pool[i];

                if (d.SeenFrame == last) {
                    continue;
                }

                var dx = d.World[12] - keepX;
                var dz = d.World[14] - keepZ;

                if (moving.Contains(d.Mesh) || now - d.SeenAt > KeepSeconds || dx * dx + dz * dz > keepReachSquared) {
                    RemoveAt(i);
                }
            }

            if (now >= forgetMovingAt) {
                forgetMovingAt = now + ForgetMovingSeconds;
                moving.Clear();
            }
        }

        /// <summary>
        /// A mesh is moving when a draw of it seen the frame before last wasn't seen last frame, and last frame drew it
        /// for the first time within MovedWithin of there.
        /// </summary>
        private static void FindMoving(int last) {
            foreach (var list in fresh.Values) {
                list.Clear();
            }

            var any = false;

            for (var i = 0; i < count; i++) {
                var d = pool[i];

                if (d.FirstFrame != last || moving.Contains(d.Mesh)) {
                    continue;
                }

                if (!fresh.TryGetValue(d.Mesh, out var list)) {
                    list = new List<ShadowDraw>();
                    fresh[d.Mesh] = list;
                }

                list.Add(d);
                any = true;
            }

            if (!any) {
                if (fresh.Count > 256) {
                    fresh.Clear();
                }

                return;
            }

            var within = MovedWithin * MovedWithin;

            for (var i = 0; i < count; i++) {
                var d = pool[i];

                if (d.SeenFrame != last - 1 || !fresh.TryGetValue(d.Mesh, out var list) || list.Count == 0) {
                    continue;
                }

                foreach (var n in list) {
                    var dx = n.World[12] - d.World[12];
                    var dy = n.World[13] - d.World[13];
                    var dz = n.World[14] - d.World[14];

                    if (dx * dx + dy * dy + dz * dz <= within) {
                        moving.Add(d.Mesh);
                        break;
                    }
                }
            }

            if (fresh.Count > 256) {
                fresh.Clear();
            }
        }

        /// <summary>One kept caster let go, the last record moved into its place.</summary>
        private static void RemoveAt(int i) {
            var d = pool[i];
            Release(d.Stream);
            Release(d.Indices);
            Release(d.Declaration);
            Release(d.Texture);
            byKey.Remove(d.Key);
            var lastIndex = count - 1;

            if (i != lastIndex) {
                var moved = pool[lastIndex];
                pool[i] = moved;
                pool[lastIndex] = d;
                byKey[moved.Key] = i;
            }

            d.Stream = IntPtr.Zero;
            d.Indices = IntPtr.Zero;
            d.Declaration = IntPtr.Zero;
            d.Texture = IntPtr.Zero;
            count--;
        }
    }
}
