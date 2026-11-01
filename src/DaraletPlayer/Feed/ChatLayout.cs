using System;
using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// A chat line wrapped to a width: its pieces (the time, text, links) broken at spaces into runs on rows, a word too long
    /// for a row broken where it reaches the edge, and rows after the first started at the indent, so a wrapped line reads as
    /// one. Widths come from the font's own advances, passed in, so this needs no ImGui and is worked out once per line,
    /// width and font, not every frame.
    /// </summary>
    public class ChatLayout {
        public readonly List<ChatRun> Runs = new List<ChatRun>();
        public int Rows = 1;

        public static ChatLayout Wrap(IReadOnlyList<string> pieces, float width, Func<char, float> advance, float indent = 0) {
            var layout = new ChatLayout();
            width = Math.Max(1, width);

            // an indent taking most of a row would leave words a letter a row
            if (indent > width / 2) {
                indent = 0;
            }

            var x = 0f;
            var row = 0;

            for (var piece = 0; piece < pieces.Count; piece++) {
                var text = pieces[piece] ?? "";
                var at = 0;

                while (at < text.Length) {
                    var letter = text[at];
                    var rowStart = row == 0 ? 0 : indent;

                    if (letter == '\n') {
                        row++;
                        x = indent;
                        at++;
                        continue;
                    }

                    if (letter == '\r') {
                        at++;
                        continue;
                    }

                    var end = at;

                    if (letter == ' ') {
                        while (end < text.Length && text[end] == ' ') {
                            end++;
                        }

                        // a row a line wraps onto doesn't start with the space it wrapped at
                        if (!(row > 0 && x <= rowStart)) {
                            layout.Place(piece, text.Substring(at, end - at), x, row, Measure(text, at, end, advance));
                            x += Measure(text, at, end, advance);
                        }

                        at = end;
                        continue;
                    }

                    while (end < text.Length && text[end] != ' ' && text[end] != '\n' && text[end] != '\r') {
                        end++;
                    }

                    var wordWidth = Measure(text, at, end, advance);

                    if (x + wordWidth > width && x > rowStart) {
                        row++;
                        x = indent;
                        rowStart = indent;
                    }

                    // longer than a whole row: as much as fits, then the rest on the next
                    while (wordWidth > width - x && end - at > 1) {
                        var fits = at;
                        var used = 0f;

                        while (fits < end && used + advance(text[fits]) <= width - x) {
                            used += advance(text[fits]);
                            fits++;
                        }

                        if (fits == at) {
                            used = advance(text[at]);
                            fits = at + 1;
                        }

                        layout.Place(piece, text.Substring(at, fits - at), x, row, used);
                        at = fits;
                        row++;
                        x = indent;
                        wordWidth = Measure(text, at, end, advance);
                    }

                    if (at < end) {
                        layout.Place(piece, text.Substring(at, end - at), x, row, wordWidth);
                        x += wordWidth;
                    }

                    at = end;
                }
            }

            layout.Rows = row + 1;
            return layout;
        }

        private static float Measure(string text, int start, int end, Func<char, float> advance) {
            var width = 0f;

            for (var i = start; i < end; i++) {
                width += advance(text[i]);
            }

            return width;
        }

        /// <summary>Adds a run, joined onto the last when it carries straight on from it.</summary>
        private void Place(int piece, string text, float x, int row, float width) {
            if (Runs.Count > 0) {
                var last = Runs[Runs.Count - 1];

                if (last.Piece == piece && last.Row == row && Math.Abs(last.X + last.Width - x) < 0.01f) {
                    last.Text += text;
                    last.Width += width;
                    Runs[Runs.Count - 1] = last;
                    return;
                }
            }

            Runs.Add(new ChatRun { Piece = piece, Text = text, X = x, Row = row, Width = width });
        }
    }
}
