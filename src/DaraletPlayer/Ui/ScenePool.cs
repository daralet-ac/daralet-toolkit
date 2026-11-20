using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Where the far scenery's buffers and pictures go: Direct3D's default pool, video memory only. The managed pool keeps
    /// a copy of each in system memory as well as on the graphics card, and that copy is in the game's 4 GB (about 35 MB
    /// at Holtburg). The default pool loses everything at a device reset, so whatever's there is let go before one
    /// (DrawDistance.VideoMemory.cs, FarLand) and made again after. It was a setting, Video memory, until November 2026
    /// (the user's call: always on, since it only saves memory).
    /// </summary>
    internal static class ScenePool {
        public static Pool Buffers => Pool.Default;
    }
}
