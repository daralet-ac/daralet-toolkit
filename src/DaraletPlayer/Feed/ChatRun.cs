namespace DaraletPlayer.Feed {
    /// <summary>A stretch of one piece of a chat line on one row, where ChatLayout put it.</summary>
    public struct ChatRun {
        /// <summary>Which of the pieces it's from.</summary>
        public int Piece;
        public string Text;
        public float X;
        public int Row;
        public float Width;
    }
}
