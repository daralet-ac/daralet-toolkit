using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The game's character options (its PlayerOption, the client's PlayerModule options) as its Options panel lists
    /// them: gmCharacterSettingsUI::InitOptions (0x4A05B0) adds six headed groups of toggles (PlayerOptionPage::AddHeader,
    /// AddToggleOption), in this order; the labels and tooltips are the game's own, from client_local_English.dat's
    /// string table 0x23000003; the headings as the game shows them (its screenshots). Option 52 is past retail's last
    /// (TotalNumberOfPlayerOptions): Daralet's PK death messages.
    /// </summary>
    public static class GameOptions {
        public static readonly IReadOnlyList<(string Title, IReadOnlyList<GameOption> Options)> Groups = new[] {
            ("User Interface Behavior", (IReadOnlyList<GameOption>)new[] {
                new GameOption(7, "Keep Combat Targets in View",
                    "When enabled the camera will rotate to face the currently selected target when you are in combat."),
                new GameOption(34, "Salvage Multiple Materials at Once", "When enabled you can drag items of multiple "
                    + "material types into the salvaging window at the same time. Each material type will be separately "
                    + "salvaged."),
                new GameOption(41, "Use Main Pack as Default for Picking Up Items", "When this option is enabled, items "
                    + "that you pick up will always try to fit into your main pack first. When not enabled, items will try "
                    + "to fit into whatever pack you have opened first."),
            }),
            ("User Interface Display", new[] {
                new GameOption(14, "Vivid Targeting Indicator", "Enable this option to apply a targeting indicator around "
                    + "selected objects and monsters for better visual reference."),
                new GameOption(8, "Display 3D Tooltips", "Toggles the tooltip messages on and off. This affects only those "
                    + "tooltips displayed in the 3D view."),
                new GameOption(20, "Show Coordinates By the Radar",
                    "When this option is enabled, your current coordinates are shown near the radar display."),
                new GameOption(19, "Side By Side Vitals",
                    "When this option is enabled, your vitals will be displayed side by side instead of stacked."),
                new GameOption(21, "Display Spell Durations", "When this option is enabled, you will see the time remaining "
                    + "for all the spells in your spell effect panel."),
                new GameOption(4, "Disable Most Weather Effects", "Turns off most weather effects. Turning off weather "
                    + "effects can help improve game performance."),
                new GameOption(48, "Disable Distance Fog",
                    "When this option is enabled, the landscape will no longer use distance fog."),
                new GameOption(5, "Always Daylight Outdoors", "Increases the ambient light when outside at night."),
                new GameOption(22, "Disable House Restriction Effects", "Disables the particle effects that are displayed "
                    + "when you collide with a house barrier. Turning off this option may improve game performance."),
                new GameOption(26, "Use Crafting Chance of Success Dialog", "When enabled your craft attempts will first "
                    + "display a dialog with your percentage chance of succeeding. Only after accepting this dialog will a "
                    + "craft be attempted."),
                new GameOption(45, "Confirm Use of Rare Gems", "When enabled, this option causes any one-use rare gem to "
                    + "trigger a confirmation dialog. This helps prevent the accidental use of one-use rare gems."),
                new GameOption(33, "Display Timestamps", "Attaches a time stamp to all displayed text."),
                new GameOption(44, "Filter Language",
                    "When this option is enabled, vulgar language will display as *** in your chat windows."),
                new GameOption(47, "Show Your Helm or Head Gear",
                    "When this option is enabled, your helm or head gear is visible."),
                new GameOption(50, "Show Your Cloak", "When this option is enabled, your cloak is visible."),
            }),
            ("Grouping", new[] {
                new GameOption(1, "Ignore Allegiance Requests", "When this option is enabled, you will automatically ignore "
                    + "invitations to join an allegiance, thus avoiding the display of the normal dialog box."),
                new GameOption(2, "Ignore Fellowship Requests", "When this option is enabled, you will automatically ignore "
                    + "invitations to join a fellowship, thus avoiding the display of the normal dialog box."),
                new GameOption(24, "Show Allegiance Logons",
                    "When enabled you will get messages when your Monarch, Patron, or Vassals log in or log out."),
                new GameOption(15, "Share Fellowship Experience and Luminance", "This only affects the creation of new "
                    + "fellowships. When checked, experience earned while in a fellowship will be shared with other "
                    + "fellowship members."),
                new GameOption(17, "Share Fellowship Loot", "This only affects the creation of new fellowships. When "
                    + "checked anyone in your fellowship can loot things killed by a member of the fellowship."),
                new GameOption(18, "Automatically Accept Fellowship Requests", "When this option is enabled, you will "
                    + "automatically accept any fellowship requests. Note that Accept Fellowship Requests must be on before "
                    + "this will work."),
            }),
            ("Other Players", new[] {
                new GameOption(16, "Accept Corpse Looting Permissions", "When checked your character will accept "
                    + "permission to loot other peoples corpses. The corpse owner must first grant permission."),
                new GameOption(9, "Attempt to Deceive Other Players", "When enabled your character will attempt to use the "
                    + "Deception skill to hide your player information from other players."),
                new GameOption(6, "Let Other Players Give You Items",
                    "When this option is checked, other players can give you items without your consent."),
                new GameOption(3, "Ignore All Trade Requests",
                    "When checked you will ignore attempts by other players to enter secure trade with you."),
                new GameOption(23, "Drag Item to Player Opens Trade", "When this option is enabled, dragging an item to "
                    + "another player will open secure trade. When this option is disabled, dragging an item to another "
                    + "player will give it to them."),
                new GameOption(28, "Allow Others to See Your Date of Birth",
                    "When this option is enabled, other players can see the date your character was created."),
                new GameOption(29, "Allow Others to See Your Age",
                    "When this option is enabled, other players can see how long your character has been in the world."),
                new GameOption(30, "Allow Others to See Your Chess Rank",
                    "When this option is enabled, other players can see your chess ranking."),
                new GameOption(31, "Allow Others to See Your Fishing Skill",
                    "When this option is enabled, other players can see your fishing skill."),
                new GameOption(32, "Allow Others to See Your Number of Deaths",
                    "When this option is enabled, other players can see how many times you have died."),
                new GameOption(40, "Allow Others to See Your Number of Titles",
                    "When this option is enabled, other players can see how many character titles you possess."),
            }),
            ("Character Behavior", new[] {
                new GameOption(10, "Run as Default Movement",
                    "When this option is enabled, the forward command will cause your avatar to run instead of walk."),
                new GameOption(12, "Advanced Combat Interface", "Enabling Advanced Combat disables the combat panel, and "
                    + "only displays the Power/Accuracy bar when in combat mode."),
                new GameOption(13, "Auto Target", "When this option is enabled, in combat you will automatically target the "
                    + "next closest enemy once your current enemy dies."),
                new GameOption(0, "Automatically Repeat Attacks", "When in melee or missile combat modes, this option will "
                    + "automatically begin another attack when your previous attack finishes."),
                new GameOption(25, "Use Charge Attack", "When enabled you will do a short high-speed sprint when melee "
                    + "attacking at range. If you do not reach the target during this sprint you will stop."),
                new GameOption(42, "Lead Missile Targets", "When this option is enabled, you will lead your targets "
                    + "(aiming where you believe they will be shortly) when attacking with a missile weapon like a bow."),
                new GameOption(43, "Use Fast Missiles", "When this option is enabled, any missiles that you fire (like "
                    + "arrows, quarrels, and atlatl darts) will travel faster than normal."),
            }),
            ("Chat", new[] {
                new GameOption(11, "Stay in Chat Mode After Sending a Message", "Normally pressing return from chat mode "
                    + "will return you to gameplay mode. With this option enabled it will leave you in chat mode ready to "
                    + "type your next message."),
                new GameOption(27, "Listen to Allegiance Chat",
                    "When this option is enabled, you can hear the chat channel for your allegiance."),
                new GameOption(35, "Listen to General Chat",
                    "When this option is enabled, you can hear and speak on the global General chat channel."),
                new GameOption(36, "Listen to Trade Chat",
                    "When this option is enabled, you can hear and speak on the global Trade chat channel."),
                new GameOption(37, "Listen to LFG Chat",
                    "When this option is enabled, you can hear and speak on the global Looking For Group chat channel."),
                new GameOption(38, "Listen to Roleplay Chat",
                    "When this option is enabled, you can hear and speak on the global Roleplay chat channel."),
                new GameOption(46, "Listen to Society Chat",
                    "When this option is enabled, you can hear and speak on the global Society chat channel."),
                new GameOption(52, "Listen to PK death messages.", "When this option is enabled, you can hear the death "
                    + "messages when a PK player dies to another PK player."),
            }),
        };
    }
}
