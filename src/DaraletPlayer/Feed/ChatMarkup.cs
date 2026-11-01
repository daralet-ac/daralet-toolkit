using System;
using System.Collections.Generic;
using System.Text;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Reads a chat line as the game hands it to Decal into pieces to draw: plain text and links, then (MarkItems) what may
    /// be linked items in what a player said. The game
    /// marks a name with a link, "&lt;Tell:IIDString:guid:Name&gt;Name&lt;\Tell&gt;", and plugins add their own the same way
    /// (closing with &lt;/Tell&gt; as often as not); a click on one is handed to Decal's ChatNameClicked with its id and the
    /// text after it.
    /// </summary>
    public static class ChatMarkup {
        private const string LinkStart = "<Tell:IIDString:";

        public static List<ChatSegment> Parse(string text) {
            var segments = new List<ChatSegment>();
            text = text ?? "";
            var at = 0;

            while (at < text.Length) {
                var start = text.IndexOf(LinkStart, at, StringComparison.OrdinalIgnoreCase);

                if (start < 0 || !TryReadLink(text, start, out var link, out var end)) {
                    // no link, or one that isn't whole: the rest is text (a broken link's text as it is)
                    var stop = start < 0 ? text.Length : start + LinkStart.Length;
                    AddText(segments, text.Substring(at, stop - at));
                    at = stop;
                    continue;
                }

                AddText(segments, text.Substring(at, start - at));
                segments.Add(link);
                at = end;
            }

            return Merge(segments);
        }

        /// <summary>The line as words alone, its links as their text: for copying, searching and saving.</summary>
        public static string PlainText(IEnumerable<ChatSegment> segments) {
            var text = new StringBuilder();

            foreach (var segment in segments) {
                text.Append(segment.Text);
            }

            return text.ToString();
        }

        /// <summary>The first name linked in a line (a tell's sender, a speaker), or null.</summary>
        public static string FirstName(IEnumerable<ChatSegment> segments) {
            foreach (var segment in segments) {
                if (segment.Kind == ChatSegmentKind.Link && LooksLikeName(segment.LinkText)) {
                    return segment.LinkText;
                }
            }

            return null;
        }

        /// <summary>
        /// Whether a link's text is a character's name rather than a plugin's own words ("openurl|...", "lfg|3"): names are
        /// letters, spaces, ' and -, and a staff character's starts with a +.
        /// </summary>
        public static bool LooksLikeName(string text) {
            if (string.IsNullOrEmpty(text) || text.Length > 40) {
                return false;
            }

            for (var i = 0; i < text.Length; i++) {
                var letter = text[i];

                if (!(char.IsLetter(letter) || letter == ' ' || letter == '\'' || letter == '-' || (letter == '+' && i == 0))) {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// A link starting at start: its id, the text after it, and the words it shows, up to its closing tag.
        /// </summary>
        private static bool TryReadLink(string text, int start, out ChatSegment link, out int end) {
            link = null;
            end = start;

            var idStart = start + LinkStart.Length;
            var idEnd = text.IndexOf(':', idStart);
            var close = text.IndexOf('>', idStart);

            if (idEnd < 0 || close < 0 || idEnd > close
                || !long.TryParse(text.Substring(idStart, idEnd - idStart), out var id)) {
                return false;
            }

            var shownStart = close + 1;
            var tagEnd = FindClosing(text, shownStart, out var closingLength);

            if (tagEnd < 0) {
                return false;
            }

            link = new ChatSegment {
                Kind = ChatSegmentKind.Link,
                LinkId = unchecked((int)id),
                LinkText = text.Substring(idEnd + 1, close - idEnd - 1),
                Text = text.Substring(shownStart, tagEnd - shownStart),
            };

            end = tagEnd + closingLength;
            return true;
        }

        private static int FindClosing(string text, int from, out int length) {
            var back = text.IndexOf("<\\Tell>", from, StringComparison.OrdinalIgnoreCase);
            var slash = text.IndexOf("</Tell>", from, StringComparison.OrdinalIgnoreCase);
            length = 7;

            if (back < 0) {
                return slash;
            }

            return slash < 0 ? back : Math.Min(back, slash);
        }

        private static void AddText(List<ChatSegment> segments, string text) {
            if (text.Length > 0) {
                segments.Add(ChatSegment.Plain(text));
            }
        }

        /// <summary>
        /// Who said a line: the words before its first quote say so ("[General] Bob says, ", "You tell Ann, "). you is your
        /// own name, for your own lines. Null for a line that isn't someone speaking.
        /// </summary>
        public static string Speaker(IReadOnlyList<ChatSegment> segments, string you) {
            var before = new StringBuilder();
            string linked = null;

            foreach (var segment in segments) {
                var quote = segment.Kind == ChatSegmentKind.Plain ? segment.Text.IndexOf('"') : -1;

                if (quote >= 0) {
                    before.Append(segment.Text, 0, quote);
                    return SpeakerFrom(before.ToString(), linked, you);
                }

                if (segment.Kind == ChatSegmentKind.Link && linked == null && LooksLikeName(segment.LinkText)) {
                    linked = segment.LinkText;
                }

                before.Append(segment.Text);
            }

            return null;
        }

        private static string SpeakerFrom(string before, string linked, string you) {
            // a channel's name in front
            if (before.StartsWith("[", StringComparison.Ordinal)) {
                var close = before.IndexOf("] ", StringComparison.Ordinal);
                before = close >= 0 ? before.Substring(close + 2) : before;
            }

            if (before.StartsWith("You ", StringComparison.Ordinal)) {
                return string.IsNullOrEmpty(you) ? null : you;
            }

            if (linked != null) {
                return linked;
            }

            // a line may come without the name linked
            var verb = before.IndexOf(" says", StringComparison.Ordinal);
            verb = verb >= 0 ? verb : before.IndexOf(" tells you", StringComparison.Ordinal);
            return verb > 0 ? before.Substring(0, verb) : null;
        }

        /// <summary>
        /// A line's pieces with each "[...]" in what the speaker said pulled out as a piece of its own, which is a linked item
        /// if the server says so. Not before the first quote: "[General]" starts a line, and isn't anyone's item.
        /// </summary>
        public static List<ChatSegment> MarkItems(List<ChatSegment> segments, string speaker) {
            if (string.IsNullOrEmpty(speaker)) {
                return segments;
            }

            var marked = new List<ChatSegment>();
            var quoted = false;

            foreach (var segment in segments) {
                if (segment.Kind != ChatSegmentKind.Plain) {
                    marked.Add(segment);
                    continue;
                }

                var from = 0;

                if (!quoted) {
                    var quote = segment.Text.IndexOf('"');

                    if (quote < 0) {
                        marked.Add(segment);
                        continue;
                    }

                    quoted = true;
                    from = quote + 1;
                }

                AddItems(marked, segment.Text, from, speaker);
            }

            return marked;
        }

        private static void AddItems(List<ChatSegment> marked, string text, int from, string speaker) {
            var at = 0;

            foreach (System.Text.RegularExpressions.Match match in ChatItemLink.Pattern.Matches(text, from)) {
                if (match.Index > at) {
                    marked.Add(ChatSegment.Plain(text.Substring(at, match.Index - at)));
                }

                var name = match.Groups["name"].Value;
                marked.Add(new ChatSegment {
                    Kind = ChatSegmentKind.Item,
                    Text = "[" + name + "]",
                    ItemKey = ChatItemLink.Key(speaker, name),
                });

                at = match.Index + match.Length;
            }

            if (at < text.Length) {
                marked.Add(ChatSegment.Plain(text.Substring(at)));
            }
        }

        /// <summary>Plain pieces next to each other as one.</summary>
        private static List<ChatSegment> Merge(List<ChatSegment> segments) {
            var merged = new List<ChatSegment>();

            foreach (var segment in segments) {
                var last = merged.Count > 0 ? merged[merged.Count - 1] : null;

                if (last != null && last.Kind == ChatSegmentKind.Plain && segment.Kind == ChatSegmentKind.Plain) {
                    last.Text += segment.Text;
                }
                else {
                    merged.Add(segment);
                }
            }

            return merged;
        }
    }
}
