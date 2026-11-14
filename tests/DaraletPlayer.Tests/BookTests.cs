using System.IO;
using System.Text;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class BookTests {
        private const uint Me = 0x50000001;
        private const uint BookId = 0x80001234;

        /// <summary>A String16L as daralet-ACE writes one: its length, code page 1252, padded to a multiple of 4.</summary>
        private static void WriteString(BinaryWriter writer, string text) {
            writer.Write((ushort)text.Length);
            writer.Write(Encoding.GetEncoding(1252).GetBytes(text));

            for (var pad = (4 - (2 + text.Length) % 4) % 4; pad > 0; pad--) {
                writer.Write((byte)0);
            }
        }

        private static void WritePage(BinaryWriter writer, uint author, string name, bool ignoreAuthor, string text) {
            writer.Write(author);
            WriteString(writer, name);
            WriteString(writer, "beer good");
            writer.Write(0xFFFF0002);
            writer.Write(text != null ? 1 : 0);
            writer.Write(ignoreAuthor ? 1 : 0);

            if (text != null) {
                WriteString(writer, text);
            }
        }

        private static byte[] Event(uint eventType, System.Action<BinaryWriter> body) {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream)) {
                writer.Write(CooldownMessages.GameEvent);
                writer.Write(Me);
                writer.Write(3u);
                writer.Write(eventType);
                body(writer);
                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>GameEventBookDataResponse, as the server sends it when a book is used.</summary>
        private static byte[] Book(int maxPages, params (uint Author, string Name, bool Ignore, string Text)[] pages) {
            return Event(BookMessages.BookDataResponse, writer => {
                writer.Write(BookId);
                writer.Write(maxPages);
                writer.Write(maxPages);
                writer.Write(1000);
                writer.Write(pages.Length);

                foreach (var page in pages) {
                    WritePage(writer, page.Author, page.Name, page.Ignore, page.Text);
                }

                WriteString(writer, "To my friend");
                writer.Write(0x50000099u);
                WriteString(writer, "Scribe");
            });
        }

        [TestMethod]
        public void ABookGivesEveryPageAndItsWords() {
            var book = BookMessages.ReadBook(Book(2,
                (0x50000099, "Scribe", true, "Like a challenge eh? The Undead Encampment at 25.4S 3.4E..."),
                (0x50000099, "Scribe", false, "Bring an extra backpack!")));

            Assert.IsNotNull(book);
            Assert.AreEqual(BookId, book.BookId);
            Assert.AreEqual(2, book.MaxPages);
            Assert.AreEqual(1000, book.MaxCharsPerPage);
            Assert.AreEqual(2, book.Pages.Count);
            Assert.AreEqual("Like a challenge eh? The Undead Encampment at 25.4S 3.4E...", book.Pages[0].Text);
            Assert.IsTrue(book.Pages[0].IgnoreAuthor);
            Assert.AreEqual("Bring an extra backpack!", book.Pages[1].Text);
            Assert.AreEqual("Scribe", book.Pages[1].AuthorName);
            Assert.AreEqual("To my friend", book.Inscription);
            Assert.AreEqual("Scribe", book.ScribeName);
        }

        [TestMethod]
        public void APageWithoutWordsIsReadPastToTheNext() {
            var book = BookMessages.ReadBook(Book(3, (1, "A", false, null), (1, "A", false, "second")));

            Assert.IsNull(book.Pages[0].Text);
            Assert.AreEqual("second", book.Pages[1].Text);
            Assert.AreEqual("Scribe", book.ScribeName);
        }

        [TestMethod]
        public void APageSentOnItsOwnReplacesOrAddsOne() {
            var book = BookMessages.ReadBook(Book(3, (1, "A", false, "old")));
            var update = BookMessages.ReadPage(Event(BookMessages.BookPageDataResponse, writer => {
                writer.Write(BookId);
                writer.Write(0);
                WritePage(writer, 1, "A", false, "new");
            }));

            Assert.IsTrue(book.Apply(update));
            Assert.AreEqual("new", book.Pages[0].Text);

            update.Index = 5;
            Assert.IsFalse(book.Apply(update), "a page past the end");
            update.Index = 1;
            Assert.IsTrue(book.Apply(update));
            Assert.AreEqual(2, book.Pages.Count);
        }

        [TestMethod]
        public void OnlyABookWithRoomOrAPageOfYoursCanBeWrittenIn() {
            var full = BookMessages.ReadBook(Book(1, (0x50000099, "Scribe", false, "theirs")));
            var room = BookMessages.ReadBook(Book(2, (0x50000099, "Scribe", false, "theirs")));
            var anyone = BookMessages.ReadBook(Book(1, (0x50000099, "Scribe", true, "anyone's")));
            var mine = BookMessages.ReadBook(Book(1, (Me, "Me", false, "mine")));

            Assert.IsFalse(full.WritableBy(Me));
            Assert.IsTrue(room.WritableBy(Me));
            Assert.IsTrue(anyone.WritableBy(Me));
            Assert.IsTrue(mine.WritableBy(Me));
        }

        [TestMethod]
        public void OtherMessagesAndShortOnesAreNotBooks() {
            Assert.IsNull(BookMessages.ReadBook(Event(0x0029, writer => writer.Write(1u))));
            Assert.IsNull(BookMessages.ReadBook(Event(BookMessages.BookDataResponse, writer => writer.Write(BookId))));
            Assert.IsNull(BookMessages.ReadPage(Book(1, (1, "A", false, "x"))));
            Assert.IsNull(BookMessages.ReadBook(null));
        }
    }
}
