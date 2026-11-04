using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The "Property Descriptions:" block the server puts at the top of a game appraisal's long description
    /// (AppraiseInfo in daralet-ACE: a "~ Name: text" line per property, then a blank line), split into each property's
    /// description and the rest of the text. The Appraisal window shows each one as the tooltip of the line naming it.
    /// </summary>
    public class PropertyDescriptions {
        private const string Header = "Property Descriptions:";

        /// <summary>Each property's name and its description, in the appraisal's order.</summary>
        public readonly List<KeyValuePair<string, string>> Entries = new List<KeyValuePair<string, string>>();

        /// <summary>The long description without the block: roll ranges and the item's own description.</summary>
        public string Rest = "";

        public static PropertyDescriptions Parse(string longDescription) {
            var parsed = new PropertyDescriptions();
            var lines = (longDescription ?? "").Replace("\r\n", "\n").Split('\n');
            var start = 0;

            while (start < lines.Length && lines[start].Trim().Length == 0) {
                start++;
            }

            if (start >= lines.Length || lines[start].Trim() != Header) {
                parsed.Rest = (longDescription ?? "").Trim();
                return parsed;
            }

            var i = start + 1;

            for (; i < lines.Length && lines[i].Trim().Length > 0; i++) {
                var line = lines[i].Trim();

                if (!line.StartsWith("~")) {
                    break;
                }

                line = line.Substring(1).Trim();
                var colon = line.IndexOf(": ", StringComparison.Ordinal);

                if (colon > 0) {
                    parsed.Entries.Add(new KeyValuePair<string, string>(line.Substring(0, colon), line.Substring(colon + 2).Trim()));
                }
            }

            parsed.Rest = string.Join("\n", lines.Skip(i)).Trim();
            return parsed;
        }

        /// <summary>
        /// The descriptions of the properties a line names: each of its comma-separated pieces that is a property's name,
        /// with a numeral after it ("Mighty Thews II") or an amount ("Biting Strike: +3%").
        /// </summary>
        public List<KeyValuePair<string, string>> For(string lineText) {
            var found = new List<KeyValuePair<string, string>>();

            if (string.IsNullOrEmpty(lineText)) {
                return found;
            }

            foreach (var piece in lineText.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries)) {
                foreach (var entry in Entries) {
                    if (Names(piece.Trim(), entry.Key) && !found.Contains(entry)) {
                        found.Add(entry);
                    }
                }
            }

            return found;
        }

        /// <summary>The descriptions no line names, so they can be shown some other way.</summary>
        public List<KeyValuePair<string, string>> NotIn(IEnumerable<string> lineTexts) {
            var named = new HashSet<string>(lineTexts.SelectMany(t => For(t)).Select(e => e.Key));
            return Entries.Where(e => !named.Contains(e.Key)).ToList();
        }

        private static bool Names(string piece, string name) {
            return Regex.IsMatch(piece, "^" + Regex.Escape(name) + @"( [IVXLC]+)?(:.*)?$", RegexOptions.IgnoreCase);
        }
    }
}
