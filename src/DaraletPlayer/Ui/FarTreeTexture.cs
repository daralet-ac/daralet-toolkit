using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A surface's texture for the far trees, shared by every model part that uses it (FarTreeModels): null until the
    /// worker has decoded it and FarTreeModels.Collect has made it, and for good if it couldn't be.
    /// </summary>
    internal sealed class FarTreeTexture {
        public Texture Texture;

        /// <summary>Cut out by alpha, as leaves are.</summary>
        public bool CutOut;
    }
}
