using System;

namespace DaraletPlayer.Ui {
    /// <summary>One of the game's world draws, noted to be made again from the sun (ShadowCasters).</summary>
    internal sealed class ShadowDraw {
        public IntPtr Stream;
        public int Offset;
        public IntPtr Indices;
        public IntPtr Texture;
        public IntPtr Declaration;
        public bool AlphaTest;

        /// <summary>The World matrix, row by row, as Direct3D has it.</summary>
        public float[] World;

        public int Type;
        public int BaseVertex;
        public int MinIndex;
        public int Vertices;
        public int StartIndex;
        public int Count;

        /// <summary>What it is, for keeping (ShadowCasters.Kept.cs): the mesh, and the mesh where it's placed.</summary>
        public ShadowCasters.MeshKey Mesh;
        public ShadowCasters.CasterKey Key;

        /// <summary>The frame it was first noted in and last noted in, and when it was last noted (seconds).</summary>
        public int FirstFrame;
        public int SeenFrame;
        public double SeenAt;
    }
}
