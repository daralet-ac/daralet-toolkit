using System;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Who a chat window's typing goes to (its talk focus, as the game's chat options box picks one): the command each puts
    /// in front of what you type. A line you start with / or @ is a command of its own and goes as typed, whatever the focus.
    /// </summary>
    public class ChatTalk {
        public const string Say = "say";
        public const string TellSelected = "tellselected";
        public const string Reply = "reply";
        public const string Custom = "custom";

        /// <summary>Its name in settings.json: never change one, or players lose their choice.</summary>
        public string Key;
        public string Label;
        /// <summary>A few letters for the button beside the typing box.</summary>
        public string Short;
        /// <summary>What goes in front of the words, or null for Say, Tell and Custom, which work theirs out.</summary>
        public string Prefix;
        public string Tip;

        public static readonly ChatTalk[] All = {
            new ChatTalk { Key = Say, Label = "Say", Short = "Say",
                Tip = "What you type is said out loud, for those near you, as typing in the game's chat bar does." },
            new ChatTalk { Key = TellSelected, Label = "Tell your selection", Short = "Tell",
                Tip = "A tell to whoever you have selected: /tell <their name>, <what you typed>. Select a player first." },
            new ChatTalk { Key = Reply, Label = "Reply", Short = "Reply",
                Tip = "A tell back to whoever last sent you one: /tell <their name>, <what you typed>." },
            new ChatTalk { Key = "fellowship", Label = "Fellowship", Short = "Fellow", Prefix = "/f ",
                Tip = "To your fellowship: /f <what you typed>." },
            new ChatTalk { Key = "allegiance", Label = "Allegiance", Short = "Alleg", Prefix = "/a ",
                Tip = "To your whole allegiance: /a <what you typed>." },
            new ChatTalk { Key = "patron", Label = "Patron", Short = "Patron", Prefix = "/p ",
                Tip = "To your patron: /p <what you typed>." },
            new ChatTalk { Key = "vassals", Label = "Vassals", Short = "Vassals", Prefix = "/v ",
                Tip = "To your vassals: /v <what you typed>." },
            new ChatTalk { Key = "covassals", Label = "Covassals", Short = "Covass", Prefix = "/c ",
                Tip = "To your covassals, your patron's other vassals: /c <what you typed>." },
            new ChatTalk { Key = "monarch", Label = "Monarch", Short = "Monarch", Prefix = "/m ",
                Tip = "To your monarch: /m <what you typed>." },
            new ChatTalk { Key = "general", Label = "General", Short = "General", Prefix = "/general ",
                Tip = "To the General channel: /general <what you typed>." },
            new ChatTalk { Key = "trade", Label = "Trade", Short = "Trade", Prefix = "/trade ",
                Tip = "To the Trade channel: /trade <what you typed>." },
            new ChatTalk { Key = "lfg", Label = "LFG", Short = "LFG", Prefix = "/lfg ",
                Tip = "To the LFG channel: /lfg <what you typed>." },
            new ChatTalk { Key = "roleplay", Label = "Roleplay", Short = "RP", Prefix = "/roleplay ",
                Tip = "To the Roleplay channel: /roleplay <what you typed>." },
            new ChatTalk { Key = "society", Label = "Society", Short = "Society", Prefix = "/society ",
                Tip = "To your society's channel: /society <what you typed>." },
            new ChatTalk { Key = "olthoi", Label = "Olthoi", Short = "Olthoi", Prefix = "/olthoi ",
                Tip = "To the Olthoi channel: /olthoi <what you typed>." },
            new ChatTalk { Key = Custom, Label = "Your own...", Short = "Own",
                Tip = "Whatever you set in front of it, in this window's options: \"/e \" to emote, \"/tell Bob, \" to "
                    + "keep telling one person, or a channel the list doesn't have." },
        };

        public static ChatTalk For(string key) {
            foreach (var talk in All) {
                if (talk.Key == key) {
                    return talk;
                }
            }

            return All[0];
        }

        /// <summary>
        /// What to send for a line typed with this focus, or null with the reason in problem: a tell with nobody to send it
        /// to. selected is your selection's name when it's a player, reply who last sent you a tell, customPrefix the
        /// window's own (Custom).
        /// </summary>
        public static string Command(string key, string typed, string selected, string reply, string customPrefix,
            out string problem) {
            problem = null;
            var text = (typed ?? "").Trim();

            if (text.Length == 0) {
                return null;
            }

            // a command, or something for another plugin: as typed
            if (text.StartsWith("/", StringComparison.Ordinal) || text.StartsWith("@", StringComparison.Ordinal)) {
                return text;
            }

            switch (key) {
                case TellSelected:
                    if (string.IsNullOrEmpty(selected)) {
                        problem = "Select a player to send a tell to.";
                        return null;
                    }

                    return $"/tell {selected}, {text}";
                case Reply:
                    if (string.IsNullOrEmpty(reply)) {
                        problem = "Nobody has sent you a tell yet to reply to.";
                        return null;
                    }

                    return $"/tell {reply}, {text}";
                case Custom: {
                    // "/e" means "/e ", and "/tell Bob," "/tell Bob, "
                    var own = customPrefix ?? "";
                    return own.Length > 0 && !char.IsWhiteSpace(own[own.Length - 1]) ? $"{own} {text}" : own + text;
                }
            }

            var prefix = For(key).Prefix;
            return prefix == null ? text : prefix + text;
        }
    }
}
