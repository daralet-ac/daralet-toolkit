namespace DaraletPlayer.Feed {
    /// <summary>One of the game's indicators (IndicatorOptions.All).</summary>
    public class IndicatorInfo {
        public IndicatorInfo(string key, string label, uint element, uint panel, int x) {
            Key = key;
            Label = label;
            Element = element;
            Panel = panel;
            X = x;
        }

        /// <summary>A name of its own, for ImGui ids and pictures.</summary>
        public string Key { get; }

        /// <summary>What it's called, for the Modern look and the tooltip.</summary>
        public string Label { get; }

        /// <summary>The game's element in layout 0x21000071.</summary>
        public uint Element { get; }

        /// <summary>The game's panel a click opens (SendNotice_SetPanelVisibility's id), or 0 for none (the X).</summary>
        public uint Panel { get; }

        /// <summary>Where it is along the bar, in the game's pixels.</summary>
        public int X { get; }
    }
}
