using System;
using System.Numerics;
using DaraletPlayer.Map;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The round Retail radar in the game's own art, from its layout 0x21000074 (gmRadarUI, element type 0x10000010, root
    /// 0x100006D3, 120x140): the disc 0x1000003F, 120x120 (06004CC1 in Resources/ui as radar-disc: a gold rim 10 pixels
    /// wide round a dark middle), and the compass tokens N 0x10000040 (060011FB, green), E 0x10000041 (06001938), S
    /// 0x10000042 (0600193A) and W 0x10000043 (0600193C), each 10x9 on the rim (N at 55,1), which
    /// gmRadarUI::UpdateCompassTokens (0x4D9CC0) moves round it as you turn. The strip under it for the coordinates
    /// (0x1000003E, 06004CC0) and the lock and move buttons aren't used: the window has its own.
    /// </summary>
    internal sealed partial class RadarPanel {
        /// <summary>The disc's size in the game, half of it: the rim's outside.</summary>
        private const float DiscOuter = 60;

        /// <summary>The dark middle's radius inside the rim, where the map and the blips go.</summary>
        private const float DiscInner = 50;

        /// <summary>How far out the tokens' middles sit (N's is 4.5 below the disc's top edge, plus its 1).</summary>
        private const float TokenRing = 54.5f;

        private static readonly Vector2 TokenSize = new Vector2(10, 9);

        private static readonly (string Art, Vector2 Way)[] Tokens = {
            ("radar-north", new Vector2(0, 1)),
            ("radar-east", new Vector2(1, 0)),
            ("radar-south", new Vector2(0, -1)),
            ("radar-west", new Vector2(-1, 0)),
        };

        /// <summary>The game's compass tokens on the disc's rim, scaled with it, turning with the radar.</summary>
        private static void DrawArtCompass(ImDrawListPtr drawList, Vector2 mid, float side, double heading, bool turn) {
            var scale = side / (DiscOuter * 2);
            var size = TokenSize * scale;

            foreach (var (art, way) in Tokens) {
                var dir = RadarMath.ToRadar(way, 1, heading, turn);
                var at = mid + dir * (TokenRing * scale) - size / 2;
                at = new Vector2((float)Math.Round(at.X), (float)Math.Round(at.Y));
                ItemIcons.DrawArtSmooth(drawList, art, at, at + size);
            }
        }
    }
}
