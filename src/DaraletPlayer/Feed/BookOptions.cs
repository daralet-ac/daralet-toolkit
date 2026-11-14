using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>The Book window's own choices (Ui/BookPanel.cs), in settings.json as "book".</summary>
    [DataContract]
    public class BookOptions {
        public const int MinFontSize = 9;
        public const int MaxFontSize = 40;
        public const int DefaultFontSize = 17;

        /// <summary>
        /// How tall a page's words are, in pixels, at the window's normal Size: a font made at that size (ChatFonts), so
        /// it's sharp.
        /// </summary>
        [DataMember(Name = "fontSize")] public int FontSize;

        public BookOptions() {
            SetDefaults();
        }

        public int ClampedFontSize => FontSize < MinFontSize ? MinFontSize : FontSize > MaxFontSize ? MaxFontSize : FontSize;

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            FontSize = DefaultFontSize;
        }
    }
}
