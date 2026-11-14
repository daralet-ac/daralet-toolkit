namespace DaraletPlayer.Feed {
    /// <summary>One of the game's character options (GameOptions): its PlayerOption number, and the game's label and tooltip.</summary>
    public class GameOption {
        public GameOption(uint option, string label, string tooltip) {
            Option = option;
            Label = label;
            Tooltip = tooltip;
        }

        /// <summary>The client's PlayerOption (0 AutoRepeatAttack...), what PlayerModule::SetOption takes.</summary>
        public uint Option { get; }

        public string Label { get; }

        public string Tooltip { get; }
    }
}
