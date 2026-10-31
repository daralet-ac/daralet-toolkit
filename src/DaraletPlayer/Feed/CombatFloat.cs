namespace DaraletPlayer.Feed {
    /// <summary>A piece of floating combat text on its way up the screen.</summary>
    public class CombatFloat {
        public string Text;
        public bool Yours;
        public bool Big;
        public uint Color;
        /// <summary>Seconds on Combat floats' clock when it appeared.</summary>
        public double Start;
        /// <summary>Which of the columns it rises in (-2 to 2), so numbers arriving together don't land on each other.</summary>
        public int Lane;
        /// <summary>The object it floats over (CombatHeadText), or 0 for its fixed place on screen.</summary>
        public int Over;
    }
}
