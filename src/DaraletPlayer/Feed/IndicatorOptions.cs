using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The Indicators window's own choices (Ui/IndicatorsPanel.cs), in settings.json as "indicators", and the game's
    /// indicators as layout 0x21000071 has them: element, the panel a click opens (the ids in the panel frame's layout
    /// 0x2100006E, each matched to its panel's class by element type), place, and the pictures for the state the game
    /// draws it in (UIElement.m_state).
    /// </summary>
    [DataContract]
    public class IndicatorOptions {
        // the game's panel ids, as the panel frame's layout 0x2100006E gives them
        public const uint CharacterInfoPanel = 0x3;
        public const uint PositiveEffectsPanel = 0x4;
        public const uint NegativeEffectsPanel = 0x5;
        public const uint LinkStatusPanel = 0x8;
        public const uint MiniGamePanel = 0x9;
        public const uint VitaePanel = 0xF;

        public const string Link = "link";
        public const string Buffs = "buffs";
        public const string Debuffs = "debuffs";
        public const string Vitae = "vitae";
        public const string Burden = "burden";
        public const string MiniGame = "minigame";
        public const string LogOut = "logout";

        /// <summary>The game draws an indicator with nothing to say (no buffs, no vitae lost) in this state, dimmed.</summary>
        public const uint DimState = 0xD;

        // the link indicator's states, as gmUIElement_LinkStatusIndicator::SetLinkState (0x4E7270) sets them
        public const uint LinkGood = 0x11;
        public const uint LinkFair = 0x12;
        public const uint LinkPoor = 0x13;
        public const uint LinkLost = 0x14;

        // the burden pack's states
        public const uint BurdenFine = 0xE;
        public const uint BurdenOver = 0xF;
        public const uint BurdenVeryOver = 0x10;

        public static readonly IReadOnlyList<IndicatorInfo> All = new[] {
            new IndicatorInfo(Link, "Link", 0x100000F8, LinkStatusPanel, 5),
            new IndicatorInfo(Buffs, "Buffs", 0x100000F5, PositiveEffectsPanel, 25),
            new IndicatorInfo(Debuffs, "Debuffs", 0x100000F6, NegativeEffectsPanel, 45),
            new IndicatorInfo(Vitae, "Vitae", 0x100000F4, VitaePanel, 65),
            new IndicatorInfo(Burden, "Burden", 0x100000F7, CharacterInfoPanel, 85),
            new IndicatorInfo(MiniGame, "Chess", 0x100000F3, MiniGamePanel, 105),
            new IndicatorInfo(LogOut, "Log out", 0x100000FA, 0, 125),
        };

        /// <summary>The X that logs out, at the end.</summary>
        [DataMember(Name = "showLogOut")] public bool ShowLogOut;
        /// <summary>One under another, rather than in a row as the game has them.</summary>
        [DataMember(Name = "vertical")] public bool Vertical;

        public IndicatorOptions() {
            SetDefaults();
        }

        /// <summary>The picture for an indicator in the state the game draws it in (null when that can't be told).</summary>
        public static string Art(string key, uint? state) {
            switch (key) {
                case Link:
                    return state == LinkFair ? "indicator-link-fair"
                        : state == LinkPoor || state == LinkLost ? "indicator-link-poor" : "indicator-link-good";
                case Burden:
                    return state == BurdenVeryOver ? "toolbar-character-very-overburdened"
                        : state == BurdenOver ? "toolbar-character-overburdened" : "toolbar-character";
                case LogOut:
                    return "indicator-logout";
                default:
                    return $"indicator-{key}" + (state == DimState ? "-none" : "");
            }
        }

        /// <summary>The link's state in a word.</summary>
        public static string LinkWord(uint? state) {
            switch (state) {
                case LinkFair:
                    return "fair";
                case LinkPoor:
                    return "poor";
                case LinkLost:
                    return "lost";
                case LinkGood:
                    return "good";
                default:
                    return "unknown";
            }
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            ShowLogOut = true;
        }
    }
}
