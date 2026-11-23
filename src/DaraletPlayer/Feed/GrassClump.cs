namespace DaraletPlayer.Feed {
    /// <summary>One clump of grass or reeds (Feed/GrassLayout.cs), in its landblock's meters.</summary>
    public struct GrassClump {
        public GrassClump(float x, float y, float z, float yaw, float scale, int terrainType, bool reeds,
            float wildness = 0.5f, float rank = 0, uint variety = 0) {
            X = x;
            Y = y;
            Z = z;
            Yaw = yaw;
            Scale = scale;
            TerrainType = terrainType;
            Reeds = reeds;
            Wildness = wildness;
            Rank = rank;
            Variety = variety;
        }

        public float X { get; }

        public float Y { get; }

        /// <summary>The ground's height under it.</summary>
        public float Z { get; }

        public float Yaw { get; }

        public float Scale { get; }

        /// <summary>The terrain it grows from, whose texture colours it.</summary>
        public int TerrainType { get; }

        public bool Reeds { get; }

        /// <summary>0 kept (near a town) to 1 wild (Feed/Wildness.cs): which pictures it's drawn with.</summary>
        public float Wildness { get; }

        /// <summary>
        /// 0 to 1, fixed for the clump: far off only those under a share are drawn (Ui/Grass.cs thins the grass with
        /// distance by drawing a chunk's lowest ranks), so the same ones always stay.
        /// </summary>
        public float Rank { get; }

        /// <summary>A hash for which picture of its kind it's drawn with (Feed/GrassPicture.cs), fixed for the clump.</summary>
        public uint Variety { get; }
    }
}
