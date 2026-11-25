using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>A scenery model's billboard (BillboardModels): its picture, and the model's size and offsets in meters.</summary>
    internal sealed class BillboardModel {
        public BillboardModel(Texture texture, float width, float height, float bottom, float middle) {
            Texture = texture;
            Width = width;
            Height = height;
            Bottom = bottom;
            Middle = middle;
        }

        /// <summary>Null until BillboardModels.Collect has made it from the worker's picture.</summary>
        public Texture Texture { get; set; }

        public float Width { get; }

        public float Height { get; }

        /// <summary>How far below (negative) or above its origin the model's lowest point is, before scaling.</summary>
        public float Bottom { get; }

        /// <summary>The middle of the picture across, from the model's origin.</summary>
        public float Middle { get; }
    }
}
