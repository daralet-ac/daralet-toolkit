using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>The triangles of a far tree model that share one texture (FarTreeModels).</summary>
    internal sealed class FarTreePart {
        /// <summary>The surface's texture, shared with other models; its Texture null until it's made.</summary>
        public FarTreeTexture Surface;

        /// <summary>The first index in the model's index buffer, and how many triangles from there.</summary>
        public int StartIndex;

        public int Triangles;

        public Texture Texture => Surface?.Texture;

        /// <summary>Cut out by alpha, as leaves are.</summary>
        public bool CutOut => Surface != null && Surface.CutOut;
    }
}
