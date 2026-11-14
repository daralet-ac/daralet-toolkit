namespace DaraletPlayer.Feed {
    /// <summary>One page of a book or note, as the server sends it (daralet-ACE's PropertiesBookPageData).</summary>
    public sealed class BookPage {
        public uint AuthorId { get; set; }
        public string AuthorName { get; set; } = "";

        /// <summary>Anyone may change the page, not only its author.</summary>
        public bool IgnoreAuthor { get; set; }

        /// <summary>The page's words, or null when the message didn't carry them (asked for on their own, 0x00AE).</summary>
        public string Text { get; set; }
    }
}
