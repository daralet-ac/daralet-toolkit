using System.Collections.Generic;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// A book or note opened for reading, from the server's BookDataResponse (game event 0x00B4): its pages, and how many
    /// it may have. BookPageDataResponse (0x00B8) fills in or replaces one page.
    /// </summary>
    public sealed class BookData {
        public uint BookId { get; set; }
        public int MaxPages { get; set; }
        public int MaxCharsPerPage { get; set; }
        public List<BookPage> Pages { get; } = new List<BookPage>();
        public string Inscription { get; set; } = "";
        public uint ScribeId { get; set; }
        public string ScribeName { get; set; } = "";

        /// <summary>
        /// Whether a character could write in it, as the server would let them (daralet-ACE's Book.ModifyPage and
        /// Player.HandleActionBookAddPage): room for another page, or a page that's theirs or anyone's.
        /// </summary>
        public bool WritableBy(uint character) {
            return Pages.Count < MaxPages || Pages.Any(p => p.IgnoreAuthor || p.AuthorId == character);
        }

        /// <summary>Takes in a page the server sent on its own; false for another book or a page past the end.</summary>
        public bool Apply(BookPageUpdate update) {
            if (update == null || update.BookId != BookId || update.Index < 0) {
                return false;
            }

            if (update.Index < Pages.Count) {
                Pages[update.Index] = update.Page;
                return true;
            }

            if (update.Index == Pages.Count) {
                Pages.Add(update.Page);
                return true;
            }

            return false;
        }
    }
}
