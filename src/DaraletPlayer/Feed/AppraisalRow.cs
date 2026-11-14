namespace DaraletPlayer.Feed {
    /// <summary>One line of a creature's or player's appraisal: what it is, its value, and whether a spell has changed it.</summary>
    public class AppraisalRow {
        public AppraisalRow(string label, string value, int effect = 0, string tip = null) {
            Label = label;
            Value = value;
            Effect = effect;
            Tip = tip;
        }

        public string Label { get; }
        public string Value { get; }

        /// <summary>1 raised by a spell (the game shows it green), -1 lowered (red), 0 neither.</summary>
        public int Effect { get; }

        /// <summary>More about it, on hover; null for nothing.</summary>
        public string Tip { get; }
    }
}
