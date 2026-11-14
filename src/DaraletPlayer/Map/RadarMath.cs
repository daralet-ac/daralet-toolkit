using System;
using System.Collections.Generic;
using System.Numerics;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Map {
    /// <summary>
    /// The Radar window's sums, kept apart from Decal so they can be tested: its range limits, where something is from you
    /// on it, which kind of blip a player is, and clipping the dungeon map to a round radar.
    /// </summary>
    public static class RadarMath {
        /// <summary>
        /// The game's radar range outdoors, in meters: CPlayerSystem::GetRadarRadius (0x55E5A0) gives 75 (0x7CD6C0) unless
        /// you're indoors (the cell's low word 0x100 or more).
        /// </summary>
        public const float OutdoorRange = 75;

        /// <summary>The game's radar range indoors, in meters (0x7CD6C4).</summary>
        public const float IndoorRange = 25;

        /// <summary>The closest the radar zooms in, in meters from the middle to the edge.</summary>
        public const float MinRange = 5;

        /// <summary>Meters along a landblock's side.</summary>
        public const float LandblockSize = 192;

        // ObjectDescriptionFlag bits (daralet-ACE's libs/entity/Enum), Decal's LongValueKey.Behavior
        public const int FlagPlayerKiller = 0x20;
        public const int FlagHiddenAdmin = 0x40;
        public const int FlagUiHidden = 0x80;
        public const int FlagVendor = 0x200;
        public const int FlagDoor = 0x1000;
        public const int FlagCorpse = 0x2000;
        public const int FlagLifestone = 0x4000;
        public const int FlagPortal = 0x40000;

        // ItemType bits (daralet-ACE's libs/entity/Enum), Decal's LongValueKey.Category
        public const int ItemTypePortal = 0x10000;
        public const int ItemTypeLifestone = 0x10000000;
        public const int FlagAdmin = 0x100000;
        public const int FlagPkLite = 0x2000000;

        /// <summary>The most the radar can show from the middle to its edge, indoors or out.</summary>
        public static float MaxRange(bool indoors) => indoors ? IndoorRange : OutdoorRange;

        /// <summary>A range held between the closest zoom and the game's own limit.</summary>
        public static float ClampRange(float range, bool indoors) {
            if (float.IsNaN(range) || range <= 0) {
                return MaxRange(indoors);
            }

            return Math.Max(MinRange, Math.Min(MaxRange(indoors), range));
        }

        /// <summary>
        /// A place in one frame for the whole world, in meters: a landblock's place in the world (its cell id's top two
        /// bytes, 192 meters each) plus where it is in that landblock (Decal's LocationX/Y, RawCoordinates).
        /// </summary>
        public static Vector2 WorldMeters(uint cell, double x, double y) {
            return new Vector2(((cell >> 24) & 0xFF) * LandblockSize + (float)x,
                ((cell >> 16) & 0xFF) * LandblockSize + (float)y);
        }

        /// <summary>
        /// Where an offset from you (meters, X east, Y north) lands on the radar, on the screen's axes (Y down), as a
        /// fraction of the range: (0, -1) is the top edge's middle. Turned, the way you face is up, as the game's radar has
        /// it; else north is.
        /// </summary>
        public static Vector2 ToRadar(Vector2 offset, float range, double headingDegrees, bool turn) {
            var angle = turn ? headingDegrees * Math.PI / 180 : 0;
            var cos = (float)Math.Cos(angle);
            var sin = (float)Math.Sin(angle);
            // turned counter-clockwise by the heading, so the way you face (clockwise from north) points up
            var x = offset.X * cos - offset.Y * sin;
            var y = offset.X * sin + offset.Y * cos;
            return new Vector2(x, -y) / range;
        }

        /// <summary>
        /// Which kind of blip a player is, by the flags their description carries: an admin, a player killer, a PK Lite
        /// player, a fellow, or any other.
        /// </summary>
        public static RadarKind PlayerKind(int behavior, bool fellow) {
            if ((behavior & FlagAdmin) != 0 && (behavior & FlagHiddenAdmin) == 0) {
                return RadarKind.Admin;
            }

            if (fellow) {
                return RadarKind.Fellow;
            }

            if ((behavior & FlagPlayerKiller) != 0) {
                return RadarKind.PlayerKiller;
            }

            return (behavior & FlagPkLite) != 0 ? RadarKind.PlayerKillerLite : RadarKind.Player;
        }

        /// <summary>
        /// The kind the object's own flags say it is, as the game's radar reads them (gmRadarUI::GetBlipColor tests the
        /// Portal bit first), or its ItemType (Decal's LongValueKey.Category): a portal, lifestone, door or corpse; null
        /// when neither says, for Decal's ObjectClass to decide. Decal's ObjectClass alone showed portals yellow.
        /// </summary>
        public static RadarKind? KindByFlags(int behavior, int itemType) {
            if ((behavior & FlagPortal) != 0 || (itemType & ItemTypePortal) != 0) {
                return RadarKind.Portal;
            }

            if ((behavior & FlagLifestone) != 0 || (itemType & ItemTypeLifestone) != 0) {
                return RadarKind.Lifestone;
            }

            if ((behavior & FlagCorpse) != 0) {
                return RadarKind.Corpse;
            }

            return (behavior & FlagDoor) != 0 ? RadarKind.Door : (RadarKind?)null;
        }

        /// <summary>Whether the game keeps something off its radar and every other display (UiHidden).</summary>
        public static bool Hidden(int behavior) => (behavior & FlagUiHidden) != 0;

        /// <summary>
        /// A convex polygon cut to a circle, the circle taken as a regular polygon of this many sides (Sutherland-Hodgman,
        /// one side at a time): a convex polygon cut by a convex one stays convex. Returns the points left, which may be
        /// none.
        /// </summary>
        public static List<Vector2> ClipToCircle(IReadOnlyList<Vector2> polygon, Vector2 middle, float radius,
            int sides = 48) {
            var output = new List<Vector2>(polygon);

            for (var i = 0; i < sides && output.Count > 0; i++) {
                var a1 = 2 * Math.PI * i / sides;
                var a2 = 2 * Math.PI * (i + 1) / sides;
                var edgeA = middle + new Vector2((float)Math.Cos(a1), (float)Math.Sin(a1)) * radius;
                var edgeB = middle + new Vector2((float)Math.Cos(a2), (float)Math.Sin(a2)) * radius;
                output = ClipToEdge(output, edgeA, edgeB, middle);
            }

            return output;
        }

        /// <summary>The part of a polygon on the inside of one edge (the side the middle is on).</summary>
        private static List<Vector2> ClipToEdge(List<Vector2> input, Vector2 a, Vector2 b, Vector2 middle) {
            var output = new List<Vector2>(input.Count + 2);
            var insideSign = Math.Sign(Cross(b - a, middle - a));

            bool Inside(Vector2 p) => Math.Sign(Cross(b - a, p - a)) != -insideSign;

            for (var i = 0; i < input.Count; i++) {
                var current = input[i];
                var previous = input[(i + input.Count - 1) % input.Count];
                var currentIn = Inside(current);
                var previousIn = Inside(previous);

                if (currentIn) {
                    if (!previousIn) {
                        output.Add(Intersect(previous, current, a, b));
                    }

                    output.Add(current);
                }
                else if (previousIn) {
                    output.Add(Intersect(previous, current, a, b));
                }
            }

            return output;
        }

        private static float Cross(Vector2 u, Vector2 v) => u.X * v.Y - u.Y * v.X;

        private static Vector2 Intersect(Vector2 p, Vector2 q, Vector2 a, Vector2 b) {
            var r = q - p;
            var s = b - a;
            var denominator = Cross(r, s);

            if (Math.Abs(denominator) < 1e-9f) {
                return q;
            }

            var t = Cross(a - p, s) / denominator;
            return p + r * t;
        }

        /// <summary>A line cut to a circle; false when none of it is inside.</summary>
        public static bool ClipToCircle(ref Vector2 a, ref Vector2 b, Vector2 middle, float radius) {
            var d = b - a;
            var f = a - middle;
            var aa = Vector2.Dot(d, d);
            var r2 = radius * radius;

            if (aa < 1e-9f) {
                return f.LengthSquared() <= r2;
            }

            var bb = 2 * Vector2.Dot(f, d);
            var cc = f.LengthSquared() - r2;
            var discriminant = bb * bb - 4 * aa * cc;

            if (discriminant <= 0) {
                return false;
            }

            var root = (float)Math.Sqrt(discriminant);
            var t1 = Math.Max(0, (-bb - root) / (2 * aa));
            var t2 = Math.Min(1, (-bb + root) / (2 * aa));

            if (t1 >= t2) {
                return false;
            }

            var start = a + d * t1;
            b = a + d * t2;
            a = start;
            return true;
        }
    }
}
