using System;
using System.Collections.Generic;
using System.Numerics;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Where a number goes over someone's head. The game leaves its camera on the Direct3D device, so a place in the world
    /// can be worked out on screen, and the numbers are drawn there like the rest: ImGui's, on top of everything, so a wall
    /// doesn't hide one. Decal's own 3D text was tried first: the world's lighting darkened it, and it refused to fade.
    /// </summary>
    internal sealed class CombatHeadText {
        /// <summary>Decal doesn't say how tall anything is, so this is a person's, and Height is there for the rest.</summary>
        private const float HeadMeters = 1.8f;

        /// <summary>Further from you than this, the device's camera isn't the one looking at you (a game panel's, say).</summary>
        private const float MostFromCamera = 40;

        /// <summary>Lines of a kind and a name this close together are one swing's. A second swing takes longer than this.</summary>
        private const int BurstMilliseconds = 100;

        private readonly Settings settings;
        private readonly HashSet<int> burstUsed = new HashSet<int>();
        private readonly float[] read = new float[16];
        private readonly float[] view = new float[16];
        private readonly float[] projection = new float[16];
        private readonly Dictionary<int, (float X, float Y, float Z)> places = new Dictionary<int, (float, float, float)>();
        private float portX;
        private float portY;
        private float portWidth;
        private float portHeight;
        private bool saidCamera;
        private bool saidNoCamera;
        private int last;
        private string burstTarget;
        private CombatKind burstKind;
        private int burstAt;

        public CombatHeadText(Settings settings) {
            this.settings = settings;
        }

        public bool Failed { get; private set; }

        /// <summary>A camera's been seen, so places can be worked out. Until then numbers use the fixed places on screen.</summary>
        public bool HasCamera { get; private set; }

        /// <summary>Who a line of combat floats over, or 0 to use the fixed places on screen.</summary>
        public int TargetFor(CombatLine line) {
            if (Failed) {
                return 0;
            }

            var core = CoreManager.Current;
            var around = new List<(int, double)>();

            if (line.Target != null) {
                var self = core.CharacterFilter.Id;

                foreach (WorldObject one in core.WorldFilter.GetByName(line.Target)) {
                    around.Add((one.Id, core.WorldFilter.Distance(self, one.Id)));
                }
            }

            var yours = CombatKindInfo.For(line.Kind).Yours;
            var now = Environment.TickCount;
            var inBurst = yours && line.Target != null && line.Target == burstTarget && line.Kind == burstKind
                && unchecked(now - burstAt) <= BurstMilliseconds;
            // one swing that hits several of a name (a cleave) writes a line for each at once: they can't all be one creature's
            var over = inBurst ? CombatTargets.NearestNotUsed(around, burstUsed) : 0;
            var spread = over != 0;

            if (!inBurst) {
                burstUsed.Clear();
                burstTarget = line.Target;
                burstKind = line.Kind;
            }

            burstAt = now;

            if (!spread) {
                over = CombatTargets.Pick(!yours && line.Target == null, line.Target, core.CharacterFilter.Id, last,
                    core.Actions.CurrentSelection, around);
            }

            if (over == 0 || core.WorldFilter[over] == null) {
                return 0;
            }

            burstUsed.Add(over);

            if (yours && !spread && over != core.CharacterFilter.Id) {
                last = over;
            }

            return over;
        }

        /// <summary>Who a sample number floats over: your selection, or you.</summary>
        public int TargetForSample(bool yours) {
            if (Failed) {
                return 0;
            }

            var core = CoreManager.Current;
            var selected = core.Actions.CurrentSelection;
            return yours && selected != 0 && core.WorldFilter[selected] != null ? selected : core.CharacterFilter.Id;
        }

        /// <summary>Reads the camera, once a frame while any number's floating over someone.</summary>
        public void BeginFrame(IReadOnlyList<CombatFloat> floats) {
            if (Failed) {
                return;
            }

            if (!AnyOver(floats)) {
                places.Clear();
                return;
            }

            try {
                var actions = CoreManager.Current.Actions;
                var device = UBService.Huds.D3Ddevice;
                var transforms = device.Transform;
                var meX = (float)actions.LocationX;
                var meY = (float)actions.LocationY;
                var meZ = (float)actions.LocationZ;
                ToArray(transforms.View, read);
                var swapped = CombatProjection.IsSwapped(read, meX, meY, meZ);

                if (swapped) {
                    CombatProjection.Unswap(read);
                }

                var distance = CombatProjection.DistanceFromCamera(read, meX, meZ, meY);

                // only an upright camera says what the projection is
                if (distance > MostFromCamera || (swapped && !HasCamera)) {
                    if (!HasCamera && !saidNoCamera) {
                        saidNoCamera = true;
                        PluginCore.LogFile($"[combat] The device's camera is {distance:F0}m from you, so it's not used");
                    }

                    return;
                }

                Array.Copy(read, view, read.Length);

                if (!swapped) {
                    ToArray(transforms.Projection, projection);
                    var port = device.Viewport;
                    portX = port.X;
                    portY = port.Y;
                    portWidth = port.Width;
                    portHeight = port.Height;
                }

                HasCamera = true;

                if (!saidCamera) {
                    saidCamera = true;
                    PluginCore.LogFile($"[combat] Numbers go over heads, by the game's camera, {distance:F1}m from you");
                }
            }
            catch (Exception ex) {
                Failed = true;
                HasCamera = false;
                PluginCore.LogFile($"[combat] Couldn't read the game's camera, so the numbers go to their places on screen: {ex}");
                PluginCore.Chat("Floating combat text can't go over heads here, so it's using its places on screen.");
            }
        }

        /// <summary>
        /// Where over this one's head is on screen, or false if that's behind the camera. One that's gone (a creature killed)
        /// keeps its last place, for the rest of its numbers' time.
        /// </summary>
        public bool TryPlace(int id, out Vector2 place) {
            place = default;
            var core = CoreManager.Current;
            var actions = core.Actions;
            var meX = (float)actions.LocationX;
            var meY = (float)actions.LocationY;
            var meZ = (float)actions.LocationZ;

            if (id == core.CharacterFilter.Id) {
                places[id] = (meX, meY, meZ);
            }
            else {
                var one = core.WorldFilter[id];

                if (one != null) {
                    var raw = one.RawCoordinates();
                    var cell = unchecked((uint)one.Values(LongValueKey.Landblock, 0));
                    // the game draws from your landblock, and one next door has its own zero
                    var (east, north) = cell == 0
                        ? (0f, 0f)
                        : CombatProjection.LandblockOffset(cell, unchecked((uint)actions.Landcell));
                    places[id] = ((float)raw.X + east, (float)raw.Y + north, (float)raw.Z);
                }
            }

            if (!places.TryGetValue(id, out var at)) {
                return false;
            }

            var height = HeadMeters + settings.Data.CombatTextHeadHeight / 10f;

            if (!CombatProjection.ToScreen(view, projection, portX, portY, portWidth, portHeight, at.X, at.Z + height, at.Y,
                out var x, out var y)) {
                return false;
            }

            place = new Vector2(x, y);
            return true;
        }

        private static bool AnyOver(IReadOnlyList<CombatFloat> floats) {
            for (var i = 0; i < floats.Count; i++) {
                if (floats[i].Over != 0) {
                    return true;
                }
            }

            return false;
        }

        private static float[] ToArray(Microsoft.DirectX.Matrix m, float[] into) {
            into[0] = m.M11;
            into[1] = m.M12;
            into[2] = m.M13;
            into[3] = m.M14;
            into[4] = m.M21;
            into[5] = m.M22;
            into[6] = m.M23;
            into[7] = m.M24;
            into[8] = m.M31;
            into[9] = m.M32;
            into[10] = m.M33;
            into[11] = m.M34;
            into[12] = m.M41;
            into[13] = m.M42;
            into[14] = m.M43;
            into[15] = m.M44;
            return into;
        }
    }
}
