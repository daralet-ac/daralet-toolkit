using System.Numerics;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Something on the Radar window, as values read from Decal (never its WorldObject, which mustn't be kept from one
    /// frame to the next).
    /// </summary>
    internal class RadarBlip {
        public int Id;
        public string Name;
        public RadarKind Kind;
        /// <summary>Where it is in the world, in meters (RadarMath.WorldMeters).</summary>
        public Vector2 At;
        /// <summary>Its height in its landblock, in meters, for telling the floors of a dungeon apart.</summary>
        public float Z;
        /// <summary>The radar colour the server gave it (1 to 10, ClientRadar), or 0 for none.</summary>
        public int ServerColor;
    }
}
