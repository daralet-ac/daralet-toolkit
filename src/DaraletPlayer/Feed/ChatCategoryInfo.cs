using System;
using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>One of the chat windows' categories: its name in settings.json, and how it's shown and explained.</summary>
    public class ChatCategoryInfo {
        public ChatCategory Category;
        /// <summary>Its name in settings.json: never change one, or players lose their choices.</summary>
        public string Key;
        public string Label;
        public string Tip;
        /// <summary>
        /// A chat channel (General, Trade...), whose lines take the channel's colour whatever their chat type.
        /// </summary>
        public bool Channel;
        /// <summary>What a line starts with in that channel ("[General]"), for lines whose chat type doesn't say.</summary>
        public string[] Prefixes;
        public uint Color;

        public static readonly ChatCategoryInfo[] All = {
            new ChatCategoryInfo { Category = ChatCategory.Gameplay, Key = "gameplay", Label = "Gameplay",
                Tip = "The game telling you things: system messages, levels and skills raised, appraisals, crafting and "
                    + "salvaging, recalls, world broadcasts, who's come online." },
            new ChatCategoryInfo { Category = ChatCategory.Alerts, Key = "alerts", Label = "Help and admin",
                Tip = "Help requests, abuse reports and admin channels. The game always shows these in its main window, so "
                    + "the main chat window always does too." },
            new ChatCategoryInfo { Category = ChatCategory.AreaChat, Key = "area", Label = "Area chat",
                Tip = "What people and creatures near you say, and their emotes." },
            new ChatCategoryInfo { Category = ChatCategory.Tells, Key = "tells", Label = "Tells",
                Tip = "Tells to you and from you, and admins' tells." },
            new ChatCategoryInfo { Category = ChatCategory.Combat, Key = "combat", Label = "Combat",
                Tip = "Your attacks, attacks on you, and what you evade, block and resist." },
            new ChatCategoryInfo { Category = ChatCategory.Magic, Key = "magic", Label = "Magic",
                Tip = "Spells: casting them, their words, and what they do." },
            new ChatCategoryInfo { Category = ChatCategory.Allegiance, Key = "allegiance", Label = "Allegiance",
                Tip = "Allegiance chat, and your patron's, vassals', covassals' and monarch's.",
                Prefixes = new[] { "[Allegiance]" } },
            new ChatCategoryInfo { Category = ChatCategory.Fellowship, Key = "fellowship", Label = "Fellowship",
                Tip = "Your fellowship's chat and its news: who joined, who left." },
            new ChatCategoryInfo { Category = ChatCategory.Errors, Key = "errors", Label = "Errors",
                Tip = "What the game says went wrong." },
            new ChatCategoryInfo { Category = ChatCategory.General, Key = "general", Label = "General", Channel = true,
                Tip = "The General chat channel.", Prefixes = new[] { "[General]" },
                Color = PackedColor.Pack((byte)140, (byte)215, (byte)255) },
            new ChatCategoryInfo { Category = ChatCategory.Trade, Key = "trade", Label = "Trade", Channel = true,
                Tip = "The Trade chat channel.", Prefixes = new[] { "[Trade]" },
                Color = PackedColor.Pack((byte)240, (byte)195, (byte)120) },
            new ChatCategoryInfo { Category = ChatCategory.Lfg, Key = "lfg", Label = "LFG", Channel = true,
                Tip = "The LFG chat channel, where /lfgroup and /lfmore post too.", Prefixes = new[] { "[LFG]" },
                Color = PackedColor.Pack((byte)170, (byte)230, (byte)150) },
            new ChatCategoryInfo { Category = ChatCategory.Roleplay, Key = "roleplay", Label = "Roleplay", Channel = true,
                Tip = "The Roleplay chat channel.", Prefixes = new[] { "[Roleplay]" },
                Color = PackedColor.Pack((byte)215, (byte)180, (byte)255) },
            new ChatCategoryInfo { Category = ChatCategory.Society, Key = "society", Label = "Society", Channel = true,
                Tip = "Your society's chat channel.",
                Prefixes = new[] { "[Society]", "[Celestial Hand]", "[Eldrytch Web]", "[Radiant Blood]" },
                Color = PackedColor.Pack((byte)255, (byte)170, (byte)200) },
            new ChatCategoryInfo { Category = ChatCategory.Olthoi, Key = "olthoi", Label = "Olthoi", Channel = true,
                Tip = "The Olthoi chat channel.", Prefixes = new[] { "[Olthoi]" },
                Color = PackedColor.Pack((byte)200, (byte)200, (byte)140) },
        };

        public static ChatCategoryInfo For(ChatCategory category) {
            return All[(int)category];
        }

        public static ChatCategoryInfo ForKey(string key) {
            foreach (var info in All) {
                if (info.Key == key) {
                    return info;
                }
            }

            return null;
        }

        /// <summary>
        /// The category of a line: by the channel it says it's in ("[Trade] Bob says..."), else by its chat type, which Decal
        /// passes as the line's colour, as the game's chat options sort them (ChatTypeInfo).
        /// </summary>
        public static ChatCategory Of(int chatType, string text) {
            text = text ?? "";

            if (text.StartsWith("[", StringComparison.Ordinal)) {
                foreach (var info in All) {
                    if (info.Prefixes == null) {
                        continue;
                    }

                    foreach (var prefix in info.Prefixes) {
                        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                            return info.Category;
                        }
                    }
                }
            }

            return ChatTypeInfo.For(chatType).Category;
        }

        /// <summary>The categories a new main window shows: all but combat and magic, which have a tab of their own.</summary>
        public static List<string> MainDefaults() {
            var keys = new List<string>();

            foreach (var info in All) {
                if (info.Category != ChatCategory.Combat && info.Category != ChatCategory.Magic) {
                    keys.Add(info.Key);
                }
            }

            return keys;
        }
    }
}
