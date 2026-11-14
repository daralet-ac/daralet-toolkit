using System.IO;
using System.Text;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Reads books and notes out of the server's game events (0xF7B0), in daralet-ACE's order:
    /// BookDataResponse (0x00B4, GameEventBookDataResponse: the book's id, its most pages twice, its most letters a page,
    /// the page count and each page, then the inscription, the scribe's id and name), which the server sends when a book
    /// is used, every page's words in it; and BookPageDataResponse (0x00B8: the book, the page's number, then the page).
    /// A page is its author's id, name and account (the account is only filled in for staff), then a dword of flags:
    /// 0xFFFF0002 says two more follow, whether the words are there and whether anyone may change it; an older form
    /// is the "words are there" dword alone. Then the words, when they're there.
    /// </summary>
    public static class BookMessages {
        public const uint BookDataResponse = 0x00B4;
        public const uint BookPageDataResponse = 0x00B8;

        private static readonly Encoding Text = Encoding.GetEncoding(1252);

        /// <summary>A whole book, or null for any other message or one too short to be one.</summary>
        public static BookData ReadBook(byte[] message) {
            return Read(message, BookDataResponse, reader => {
                var book = new BookData { BookId = reader.ReadUInt32(), MaxPages = reader.ReadInt32() };
                reader.ReadInt32(); // the page count, which the server sends as the most pages again
                book.MaxCharsPerPage = reader.ReadInt32();

                for (var count = reader.ReadInt32(); count > 0; count--) {
                    book.Pages.Add(ReadPage(reader));
                }

                book.Inscription = ReadString(reader);
                book.ScribeId = reader.ReadUInt32();
                book.ScribeName = ReadString(reader);
                return book;
            });
        }

        /// <summary>One page sent on its own, or null for any other message.</summary>
        public static BookPageUpdate ReadPage(byte[] message) {
            return Read(message, BookPageDataResponse, reader => new BookPageUpdate {
                BookId = reader.ReadUInt32(),
                Index = reader.ReadInt32(),
                Page = ReadPage(reader),
            });
        }

        private static T Read<T>(byte[] message, uint eventType, System.Func<BinaryReader, T> body) where T : class {
            if (message == null || message.Length < 20) {
                return null;
            }

            try {
                using (var reader = new BinaryReader(new MemoryStream(message))) {
                    if (reader.ReadUInt32() != CooldownMessages.GameEvent) {
                        return null;
                    }

                    reader.ReadUInt32(); // the character
                    reader.ReadUInt32(); // the event's sequence number

                    return reader.ReadUInt32() == eventType ? body(reader) : null;
                }
            }
            catch (EndOfStreamException) {
                return null;
            }
        }

        private static BookPage ReadPage(BinaryReader reader) {
            var page = new BookPage { AuthorId = reader.ReadUInt32(), AuthorName = ReadString(reader) };
            ReadString(reader); // the author's account, made up for anyone but staff
            var flags = reader.ReadUInt32();
            bool textIncluded;

            if ((flags & 0xFFFF0000) == 0xFFFF0000) {
                textIncluded = reader.ReadUInt32() != 0;
                page.IgnoreAuthor = reader.ReadUInt32() != 0;
            }
            else {
                textIncluded = flags != 0;
            }

            if (textIncluded) {
                page.Text = ReadString(reader);
            }

            return page;
        }

        /// <summary>A String16L: its length, its bytes (code page 1252), padded so the whole is a multiple of 4.</summary>
        private static string ReadString(BinaryReader reader) {
            int length = reader.ReadUInt16();
            var bytes = reader.ReadBytes(length);

            if (bytes.Length < length) {
                throw new EndOfStreamException();
            }

            reader.ReadBytes((4 - (2 + length) % 4) % 4);
            return Text.GetString(bytes);
        }
    }
}
