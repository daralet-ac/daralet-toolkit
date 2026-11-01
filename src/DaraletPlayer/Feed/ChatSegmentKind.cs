namespace DaraletPlayer.Feed {
    public enum ChatSegmentKind {
        Plain,
        /// <summary>A clickable link the game or a plugin put in: a player's name, or a plugin's own link.</summary>
        Link,
        /// <summary>"[Name]" in what a player said: an item they linked, if the server says so (ChatItemLink).</summary>
        Item,
    }
}
