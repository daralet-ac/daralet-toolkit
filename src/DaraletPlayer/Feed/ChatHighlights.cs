using System;
using System.Collections.Generic;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The words that light a line up (ChatSettings.Highlights, your name with HighlightName), and the words that keep a
    /// line out of a window (ChatWindowSettings.HideWords): whole words, case ignored, comma-separated.
    /// </summary>
    public static class ChatHighlights {
        public static List<string> Words(string list) {
            return (list ?? "").Split(',')
                .Select(w => w.Trim())
                .Where(w => w.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Whether any of the words is in the text as a word of its own (not inside a longer one).</summary>
        public static bool AnyIn(string text, IReadOnlyList<string> words) {
            if (string.IsNullOrEmpty(text) || words == null) {
                return false;
            }

            foreach (var word in words) {
                var at = 0;

                while ((at = text.IndexOf(word, at, StringComparison.OrdinalIgnoreCase)) >= 0) {
                    var before = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
                    var after = at + word.Length >= text.Length || !char.IsLetterOrDigit(text[at + word.Length]);

                    if (before && after) {
                        return true;
                    }

                    at += word.Length;
                }
            }

            return false;
        }
    }
}
