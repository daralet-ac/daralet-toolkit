namespace DaraletPlayer.Feed {
    /// <summary>One page of a book, sent on its own (BookPageDataResponse, game event 0x00B8).</summary>
    public sealed class BookPageUpdate {
        public uint BookId { get; set; }
        public int Index { get; set; }
        public BookPage Page { get; set; }
    }
}
