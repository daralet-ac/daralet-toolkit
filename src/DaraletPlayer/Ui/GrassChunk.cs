using System;
using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// One 24-meter cell's grass (Ui/Grass.Chunks.cs): its clumps, lowest rank first, in a vertex buffer of their own in
    /// the block's meters, made once and kept while you're near. Null buffer for a cell with no grass, so it isn't asked
    /// for again.
    /// </summary>
    internal sealed class GrassChunk : IDisposable {
        public GrassChunk(uint block, int cellX, int cellY) {
            Block = block;
            CellX = cellX;
            CellY = cellY;
        }

        /// <summary>The landblock, 0xXXYY.</summary>
        public uint Block { get; }

        public int CellX { get; }

        public int CellY { get; }

        public VertexBuffer Buffer { get; set; }

        /// <summary>The clumps it kept: those whose rank is under KeptShare, lowest first.</summary>
        public int Clumps { get; set; }

        /// <summary>Every clump the cell grows, kept or not: the draw's share is of these.</summary>
        public int Total { get; set; }

        /// <summary>
        /// The share of the cell's clumps kept, by its distance when made: a far chunk only ever draws a few, so it keeps no
        /// more, and it's made again with more as you come near (Ui/Grass.Chunks.cs).
        /// </summary>
        public float KeptShare { get; set; } = 1;

        /// <summary>The lowest and highest ground under its clumps, for the view test.</summary>
        public float LowestZ { get; set; }

        public float HighestZ { get; set; }

        /// <summary>Set each frame it's drawn: how far its nearest edge is from you.</summary>
        public float Distance { get; set; }

        public static long KeyOf(uint block, int cellX, int cellY) {
            return (long)block << 8 | (long)cellX << 3 | (uint)cellY;
        }

        public long Key => KeyOf(Block, CellX, CellY);

        public void Dispose() {
            Buffer?.Dispose();
            Buffer = null;
        }
    }
}
