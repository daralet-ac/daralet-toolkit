using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The Toolbar window's own choices (Ui/ToolbarPanel.cs), in settings.json as "toolbar", and the game toolbar's panel
    /// buttons as layout 0x21000016 has them: element, the panel id its property 0x10000029 carries (what
    /// CM_UI::SendNotice_SetPanelVisibility takes), its place along the top row, and its pictures (Resources/ui, the
    /// game's states 1 and 6).
    /// </summary>
    [DataContract]
    public class ToolbarOptions {
        // the game's panel ids
        public const uint InventoryPanel = 0x7;
        public const uint OptionsPanel = 0xA;
        public const uint AttributesPanel = 0xB;
        public const uint SocialPanel = 0xC;
        public const uint SpellbookPanel = 0xD;
        public const uint MapPanel = 0x10;
        public const uint ContractsPanel = 0x19;

        /// <summary>The top row's buttons, left to right, the backpack (bigger, at the right end) last.</summary>
        public static readonly IReadOnlyList<ToolbarButtonInfo> Buttons = new[] {
            new ToolbarButtonInfo("social", "Social", 0x10000197, SocialPanel, 55, "toolbar-social"),
            new ToolbarButtonInfo("spellbook", "Spells", 0x10000198, SpellbookPanel, 85, "toolbar-spellbook"),
            new ToolbarButtonInfo("attributes", "Character", 0x10000199, AttributesPanel, 115, "toolbar-attributes"),
            new ToolbarButtonInfo("quests", "Quests", 0x1000055A, ContractsPanel, 145, "toolbar-quests"),
            new ToolbarButtonInfo("map", "Map", 0x1000019A, MapPanel, 175, "toolbar-map"),
            new ToolbarButtonInfo("options", "Options", 0x1000019B, OptionsPanel, 204, "toolbar-options"),
            new ToolbarButtonInfo("inventory", "Inventory", 0x100001B1, InventoryPanel, 238, "toolbar-backpack"),
        };

        /// <summary>The selected object's strip under the buttons: its name, health or mana, Use and Appraise.</summary>
        [DataMember(Name = "showSelection")] public bool ShowSelection;

        public ToolbarOptions() {
            SetDefaults();
        }

        /// <summary>The combat button's picture for the client's COMBAT_MODE (PowerBarOptions): peace for anything else.</summary>
        public static string CombatArt(int mode) {
            switch (mode) {
                case PowerBarOptions.Melee:
                    return "toolbar-combat-melee";
                case PowerBarOptions.Missile:
                    return "toolbar-combat-missile";
                case PowerBarOptions.Magic:
                    return "toolbar-combat-magic";
                default:
                    return "toolbar-combat-peace";
            }
        }

        /// <summary>The combat mode's name, for the Modern look and the tooltip.</summary>
        public static string CombatName(int mode) {
            switch (mode) {
                case PowerBarOptions.Melee:
                    return "Melee";
                case PowerBarOptions.Missile:
                    return "Missile";
                case PowerBarOptions.Magic:
                    return "Magic";
                default:
                    return "Peace";
            }
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            ShowSelection = true;
        }
    }
}
