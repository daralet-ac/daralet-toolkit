namespace DaraletPlayer.Feed {
    /// <summary>One of the game toolbar's panel buttons (ToolbarOptions.Buttons).</summary>
    public class ToolbarButtonInfo {
        public ToolbarButtonInfo(string key, string label, uint element, uint panel, int x, string art) {
            Key = key;
            Label = label;
            Element = element;
            Panel = panel;
            X = x;
            Art = art;
        }

        /// <summary>A name of its own, for ImGui ids.</summary>
        public string Key { get; }

        /// <summary>Its name, for the Modern look and the tooltip.</summary>
        public string Label { get; }

        /// <summary>The game's element in layout 0x21000016.</summary>
        public uint Element { get; }

        /// <summary>The game's panel it opens (its property 0x10000029).</summary>
        public uint Panel { get; }

        /// <summary>Where it starts along the top row, in the game's pixels.</summary>
        public int X { get; }

        /// <summary>Its picture, and this with "-open" while its panel is.</summary>
        public string Art { get; }

        public string OpenArt => Art + "-open";
    }
}
