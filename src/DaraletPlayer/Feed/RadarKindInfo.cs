using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// A kind of blip on the Radar window: its key in settings.json, its name in the settings, its colour to start, its
    /// shape, and whether it's shown to start. The colours to start are the client's own blip colours where it has one for
    /// the kind (gmRadarUI::GetBlipColor, 0x4D8350: the table at 0x81C69C, in RadarColor's order, Blue, Gold, Yellow,
    /// White, Red, Purple, Pink, Green, Cyan, Bright green); its colours for a kind by its flags are set up at run time, so
    /// those are near the game's by eye.
    /// </summary>
    public class RadarKindInfo {
        public static readonly IReadOnlyList<RadarKindInfo> All = new[] {
            new RadarKindInfo(RadarKind.Monster, "monster", "Monsters", PackedColor.Pack(1f, 0.67f, 0f, 1f),
                RadarShape.Dot, true),
            new RadarKindInfo(RadarKind.Npc, "npc", "NPCs and vendors", PackedColor.Pack(1f, 1f, 0.5f, 1f),
                RadarShape.Dot, true),
            new RadarKindInfo(RadarKind.Player, "player", "Players", PackedColor.Pack(1f, 1f, 1f, 1f),
                RadarShape.Square, true),
            new RadarKindInfo(RadarKind.PlayerKiller, "pk", "Player killers", PackedColor.Pack(1f, 0.25f, 0.39f, 1f),
                RadarShape.Square, true),
            new RadarKindInfo(RadarKind.PlayerKillerLite, "pkLite", "PK Lite players",
                PackedColor.Pack(1f, 0.66f, 0.75f, 1f),
                RadarShape.Square, true),
            new RadarKindInfo(RadarKind.Admin, "admin", "Admins", PackedColor.Pack(0f, 1f, 0f, 1f),
                RadarShape.Square, true),
            new RadarKindInfo(RadarKind.Fellow, "fellow", "Fellows", PackedColor.Pack(0f, 1f, 1f, 1f),
                RadarShape.Diamond, true),
            new RadarKindInfo(RadarKind.Portal, "portal", "Portals", PackedColor.Pack(0.75f, 0.39f, 1f, 1f),
                RadarShape.Ring, true),
            new RadarKindInfo(RadarKind.Lifestone, "lifestone", "Lifestones", PackedColor.Pack(0.25f, 0.66f, 1f, 1f),
                RadarShape.Cross, true),
            new RadarKindInfo(RadarKind.Corpse, "corpse", "Corpses", PackedColor.Pack(0.7f, 0.7f, 0.7f, 1f),
                RadarShape.X, false),
            new RadarKindInfo(RadarKind.Door, "door", "Doors", PackedColor.Pack(0.8f, 0.6f, 0.4f, 1f),
                RadarShape.Square, false),
            new RadarKindInfo(RadarKind.Chest, "chest", "Chests", PackedColor.Pack(0f, 0.75f, 0.4f, 1f),
                RadarShape.Square, false),
        };

        /// <summary>
        /// The client's colours for the server's RadarBlipColor, 1 to 10 (index 0 unused), as gmRadarUI::GetBlipColor's
        /// switch (its jump table at 0x4D878C) picks them from the table at 0x81C69C: Blue, Gold, White, Purple, Red, Pink,
        /// Green, Yellow, Cyan, Bright green.
        /// </summary>
        public static readonly uint[] ServerColors = {
            0,
            PackedColor.Pack(0.25f, 0.66f, 1f, 1f),
            PackedColor.Pack(1f, 0.67f, 0f, 1f),
            PackedColor.Pack(1f, 1f, 1f, 1f),
            PackedColor.Pack(0.75f, 0.39f, 1f, 1f),
            PackedColor.Pack(1f, 0.25f, 0.39f, 1f),
            PackedColor.Pack(1f, 0.66f, 0.75f, 1f),
            PackedColor.Pack(0f, 0.5f, 0.25f, 1f),
            PackedColor.Pack(1f, 1f, 0.5f, 1f),
            PackedColor.Pack(0f, 1f, 1f, 1f),
            PackedColor.Pack(0f, 1f, 0f, 1f),
        };

        /// <summary>The client's colour for a server RadarBlipColor, or null for none.</summary>
        public static uint? ServerColor(int blipColor) {
            return blipColor >= 1 && blipColor < ServerColors.Length ? ServerColors[blipColor] : (uint?)null;
        }

        private RadarKindInfo(RadarKind kind, string key, string label, uint color, RadarShape shape, bool shown) {
            Kind = kind;
            Key = key;
            Label = label;
            Color = color;
            Shape = shape;
            Shown = shown;
        }

        public RadarKind Kind { get; }
        public string Key { get; }
        public string Label { get; }
        public uint Color { get; }
        public RadarShape Shape { get; }
        public bool Shown { get; }

        public static RadarKindInfo For(RadarKind kind) {
            foreach (var info in All) {
                if (info.Kind == kind) {
                    return info;
                }
            }

            return All[0];
        }
    }
}
