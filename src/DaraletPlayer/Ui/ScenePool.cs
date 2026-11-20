using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Where the far scenery's buffers and pictures go (DrawDistanceOptions.VideoMemory; the user's call, November 2026: a
    /// setting of its own). Direct3D's managed pool keeps a copy of each in system memory as well as on the graphics card,
    /// and that copy is in the game's 4 GB; the default pool keeps only the card's, but loses it at a device reset, so
    /// whatever's there is let go before one (DrawDistance.VideoMemory.cs) and made again after.
    /// </summary>
    internal static class ScenePool {
        /// <summary>Set by DrawDistance from the setting, before anything's made.</summary>
        public static bool VideoOnly;

        public static Pool Buffers => VideoOnly ? Pool.Default : Pool.Managed;
    }
}
