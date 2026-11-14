namespace DaraletPlayer.Feed {
    /// <summary>
    /// One row of the Options panel's Config page: its label, the client's preference global(s) (the address of the
    /// preference's name, gmClient::InitUIPreferences), and for a slider the words at its ends. A light whose preference
    /// is a Disable... one is lit while it's off (Inverted).
    /// </summary>
    public class ConfigOption {
        public ConfigOption(ConfigKind kind, string label, uint name, string tooltip, uint sliderName = 0,
            bool inverted = false, string low = null, string high = null) {
            Kind = kind;
            Label = label;
            Name = name;
            Tooltip = tooltip;
            SliderName = sliderName;
            Inverted = inverted;
            Low = low;
            High = high;
        }

        public ConfigKind Kind { get; }
        public string Label { get; }

        /// <summary>The preference: the light's for a LightSlider, the slider's otherwise.</summary>
        public uint Name { get; }

        /// <summary>A LightSlider's slider preference.</summary>
        public uint SliderName { get; }

        public bool Inverted { get; }
        public string Low { get; }
        public string High { get; }
        public string Tooltip { get; }

        /// <summary>The float preference a slider moves, if it has one.</summary>
        public uint FloatName => Kind == ConfigKind.LightSlider ? SliderName : Kind == ConfigKind.Slider ? Name : 0;

        /// <summary>The bool preference a light shows, if it has one.</summary>
        public uint BoolName => Kind == ConfigKind.Light || Kind == ConfigKind.LightSlider ? Name : 0;
    }
}
