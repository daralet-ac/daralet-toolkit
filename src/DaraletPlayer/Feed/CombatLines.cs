using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Reads combat out of chat lines. Chat is the one place all of it comes through: the client writes its own line for the
    /// server's attack notifications ("You %s %s for %d point%s of %sdamage!" in acclient.exe), and the server writes the
    /// rest itself (spells, glancing blows and adrenaline, in Player_Combat.cs and SpellProjectile_DamageTarget.cs in
    /// daralet-ACE). A line can start with any number of short sentences ("Critical hit! Overpower! You..."), so each
    /// pattern starts at the line's start or after a sentence's end, and names can't hold a "!".
    /// </summary>
    public static class CombatLines {
        // the game event kills and deaths come in: their wording is too varied to read from chat (Strings.cs in daralet-ACE)
        private const uint GameEvent = 0xF7B0;
        private const uint VictimNotification = 0x01AC;
        private const uint KillerNotification = 0x01AD;

        private const string Start = @"(?:^|[.!]\s+)";
        private const string Who = @"(?<who>[^!]+?)";
        private const string Target = @"(?<target>[^!]+?)";
        private const string Points = @"(?<n>\d+) points?";
        private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;

        /// <summary>In order: the first that matches decides, so "You..." comes before anything that may start with a name.</summary>
        private static readonly (Regex Pattern, CombatKind Kind, string Outcome)[] Patterns = {
            (Line($"You evaded {Who}!?$"), CombatKind.YouAvoid, "Evaded"),
            (Line($"You evade the attack by {Who}$"), CombatKind.YouAvoid, "Evaded"),
            (Line($"You blocked {Who}'s attack"), CombatKind.YouAvoid, "Blocked"),
            (Line($"You parried {Who}'s attack"), CombatKind.YouAvoid, "Parried"),
            (Line($"You resist the spell cast by {Who}$"), CombatKind.YouAvoid, "Resisted"),

            (Line($"{Target} evaded your attack"), CombatKind.YourMiss, "Evaded"),
            (Line($"{Target} blocked your attack"), CombatKind.YourMiss, "Blocked"),
            (Line($"{Target} parried your attack"), CombatKind.YourMiss, "Parried"),
            (Line($"{Target} resists your spell"), CombatKind.YourMiss, "Resisted"),

            (Line($@"You \S+ {Target} for {Points} of periodic [^.!]*damage"), CombatKind.YouPeriodic, null),
            (Line($@"You \S+ {Target} for {Points} of (?:\w+ )?damage"), CombatKind.YouHit, null),
            (Line($@"You follow up your .+ with a quick riposte, dealing (?<n>\d+) (?:.* to {Target}!?$)?"), CombatKind.YouHit, null),
            (Line(@"You deflect (?<n>\d+) damage back"), CombatKind.YouHit, null),
            (Line($@"You \S+ {Target} for {Points} with "), CombatKind.YouSpell, null),
            (Line($@"With .+ you drain {Points} of \w+ from {Target}[.!]?$"), CombatKind.YouSpell, null),
            (Line($@"With Surge of Affliction you \S+ {Points} of health from {Target}[.!]?$"), CombatKind.YouSpell, null),

            (Line($@"You (?:\w+ )?heal {Target} for (?<n>\d+) \w+ points"), CombatKind.Healing, null),
            (Line($@"With .+ you restore {Points} of "), CombatKind.Healing, null),
            (Line($@"You cast .+ and restore {Points} of your "), CombatKind.Healing, null),
            (Line($@"You gain {Points} of "), CombatKind.Healing, null),
            (Line($@"You receive {Points} of periodic (?:healing|stamina|mana)"), CombatKind.Healing, null),
            (Line($@"{Who} (?:\w+ )?heals you for (?<n>\d+) \w+ points"), CombatKind.Healing, null),
            (Line($@"{Who} casts .+ and restores {Points} of your "), CombatKind.Healing, null),
            (Line($@"{Who} restores {Points} of (?:Health|Stamina|Mana)"), CombatKind.Healing, null),

            (Line($@"You cast .+ and drain {Points} of your "), CombatKind.SpellOnYou, null),
            (Line($@"You receive {Points} of periodic harm"), CombatKind.SpellOnYou, null),
            (Line($@"{Who} \S+ you for {Points} with "), CombatKind.SpellOnYou, null),
            (Line($@"{Who} casts .+ and drains {Points} of your "), CombatKind.SpellOnYou, null),
            (Line($@"{Who} you(?:r [^.!]+?)? for {Points} of (?:\w+ )?damage"), CombatKind.HitYou, null),
        };

        private static Regex Line(string pattern) {
            return new Regex(Start + pattern, Options);
        }

        /// <summary>
        /// The line as combat, or null for anything else. Players' own words are never combat, whatever they say, so nobody
        /// can put a number in your floating text or log with a tell.
        /// </summary>
        /// <param name="chatType">The line's chat type, as Decal's e.Color has it.</param>
        public static CombatLine Read(string text, int chatType, DateTime at) {
            if (string.IsNullOrEmpty(text) || IsPlayerChat(chatType) || text.IndexOf('"') >= 0) {
                return null;
            }

            var line = text.TrimEnd('\r', '\n', ' ');

            foreach (var (pattern, kind, outcome) in Patterns) {
                var match = pattern.Match(line);

                if (!match.Success) {
                    continue;
                }

                var amount = match.Groups["n"];
                var target = match.Groups["target"];
                return new CombatLine {
                    Kind = kind,
                    Target = target.Success ? target.Value : null,
                    Text = line,
                    Amount = amount.Success && int.TryParse(amount.Value, out var points) ? points : (int?)null,
                    Outcome = outcome,
                    Critical = line.IndexOf("critical hit!", 0, match.Index + 1, StringComparison.OrdinalIgnoreCase) >= 0,
                    At = at,
                };
            }

            return null;
        }

        /// <summary>A line for a kill or death the server's said happened: its text is exact, so nothing's read from it.</summary>
        public static CombatLine ForDeathMessage(string text, CombatKind kind, DateTime at) {
            return new CombatLine {
                Kind = kind,
                Text = text.TrimEnd('\r', '\n', ' '),
                Outcome = kind == CombatKind.Kill ? "Killed" : "You died",
                At = at,
            };
        }

        /// <summary>
        /// The kill or death in a message from the server, or false for any other: 0xF7B0, your guid, a sequence number, the
        /// event's own number, then the text, a 16-bit length and its letters (GameEventKillerNotification in daralet-ACE).
        /// </summary>
        public static bool TryReadDeathMessage(byte[] message, out string text, out CombatKind kind) {
            text = null;
            kind = CombatKind.Kill;

            if (message == null || message.Length < 18) {
                return false;
            }

            try {
                using (var reader = new BinaryReader(new MemoryStream(message))) {
                    if (reader.ReadUInt32() != GameEvent) {
                        return false;
                    }

                    reader.ReadUInt32(); // your guid
                    reader.ReadUInt32(); // the event's sequence number
                    var type = reader.ReadUInt32();

                    if (type != KillerNotification && type != VictimNotification) {
                        return false;
                    }

                    var length = reader.ReadUInt16();
                    var letters = reader.ReadBytes(length);

                    if (letters.Length != length) {
                        return false;
                    }

                    text = Encoding.GetEncoding(1252).GetString(letters);
                    kind = type == KillerNotification ? CombatKind.Kill : CombatKind.Death;
                    return true;
                }
            }
            catch (EndOfStreamException) {
                return false;
            }
        }

        /// <summary>Speech, tells, channels, social, emotes, allegiance and fellowship chat: what other players wrote.</summary>
        private static bool IsPlayerChat(int chatType) {
            switch (chatType) {
                case 2:
                case 3:
                case 4:
                case 8:
                case 9:
                case 10:
                case 11:
                case 12:
                case 18:
                case 19:
                    return true;
                default:
                    return false;
            }
        }
    }
}
