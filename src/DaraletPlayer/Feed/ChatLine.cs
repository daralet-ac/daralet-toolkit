using System;
using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>A line of chat as the plugin's chat windows keep it.</summary>
    public class ChatLine {
        /// <summary>Counts up from 1 for the session: newer lines have bigger ids.</summary>
        public long Id;
        public DateTime At;
        /// <summary>The game's chat type, which Decal passes as the colour (ChatTypeInfo).</summary>
        public int Type;
        public ChatCategory Category;
        /// <summary>The line as Decal had it, links and all.</summary>
        public string Raw;
        public List<ChatSegment> Segments;
        /// <summary>The words alone, links as their text.</summary>
        public string Text;
        /// <summary>
        /// Whoever the line is from or to, by the first name linked in it (a tell's sender, a speaker), or null.
        /// </summary>
        public string Name;
        /// <summary>A tell to you: Name is who to reply to.</summary>
        public bool TellToYou;
        /// <summary>A tell you sent: Name is who it went to.</summary>
        public bool TellFromYou;
        /// <summary>It mentions a word the highlights are set to look for (your name, say).</summary>
        public bool Highlighted;

        /// <summary>Reads a line Decal passed to ChatBoxMessage. you is your character's name, for items you linked.</summary>
        public static ChatLine Read(long id, string raw, int type, DateTime at, string you = null) {
            raw = (raw ?? "").TrimEnd('\r', '\n', ' ');
            var segments = ChatMarkup.Parse(raw);
            var text = ChatMarkup.PlainText(segments);
            segments = ChatMarkup.MarkItems(segments, ChatMarkup.Speaker(segments, you));
            var line = new ChatLine {
                Id = id,
                At = at,
                Type = type,
                Raw = raw,
                Segments = segments,
                Text = text,
                Category = ChatCategoryInfo.Of(type, text),
                Name = ChatMarkup.FirstName(segments),
            };

            line.TellToYou = text.IndexOf(" tells you, ", StringComparison.Ordinal) > 0
                && (type == 3 || type == 0x1F);
            line.TellFromYou = type == 4 && text.StartsWith("You tell ", StringComparison.Ordinal);

            // a tell's line may come without the name linked
            if (line.Name == null && line.TellToYou) {
                line.Name = text.Substring(0, text.IndexOf(" tells you, ", StringComparison.Ordinal)).Trim();
            }
            else if (line.Name == null && line.TellFromYou) {
                var comma = text.IndexOf(", ", StringComparison.Ordinal);
                line.Name = comma > 9 ? text.Substring(9, comma - 9).Trim() : null;
            }

            return line;
        }
    }
}
