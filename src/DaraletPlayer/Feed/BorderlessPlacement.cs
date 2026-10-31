namespace DaraletPlayer.Feed {
    /// <summary>The sums for taking the game window's border off (BorderlessWindow), without Windows in them.</summary>
    public static class BorderlessPlacement {
        public const uint Caption = 0x00C00000;
        public const uint ThickFrame = 0x00040000;

        // WS_EX_DLGMODALFRAME, WS_EX_WINDOWEDGE, WS_EX_CLIENTEDGE and WS_EX_STATICEDGE
        public const uint ExtendedEdges = 0x00000001 | 0x00000100 | 0x00000200 | 0x00020000;

        public static bool HasBorder(uint style) {
            return (style & (Caption | ThickFrame)) != 0;
        }

        public static uint WithoutBorder(uint style) {
            return style & ~(Caption | ThickFrame);
        }

        public static uint WithoutEdges(uint extendedStyle) {
            return extendedStyle & ~ExtendedEdges;
        }

        /// <summary>
        /// Where the window goes with its border off. The picture stays the same size, so the game has nothing to rebuild.
        /// One as big as the screen goes exactly over it, which is what most people want this for; any other stays where
        /// its picture was, kept on screen where it fits, since it's lost its title bar to drag it back by.
        /// </summary>
        public static (int X, int Y) Place(int x, int y, int width, int height, int screenX, int screenY, int screenWidth,
            int screenHeight) {
            return (Along(x, width, screenX, screenWidth), Along(y, height, screenY, screenHeight));
        }

        /// <summary>Where a window of this size is centred on the screen.</summary>
        public static (int X, int Y) Centre(int width, int height, int screenX, int screenY, int screenWidth, int screenHeight) {
            return (screenX + (screenWidth - width) / 2, screenY + (screenHeight - height) / 2);
        }

        private static int Along(int at, int size, int screenAt, int screenSize) {
            if (size >= screenSize) {
                return screenAt;
            }

            if (at < screenAt) {
                return screenAt;
            }

            return at + size > screenAt + screenSize ? screenAt + screenSize - size : at;
        }
    }
}
