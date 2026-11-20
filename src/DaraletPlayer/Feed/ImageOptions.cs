using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The 3D world's picture made cleaner (Ui/DepthShading.Image.cs), in settings.json as "image": anti-aliasing and
    /// sharpening, over the world only, before the interface. Off to start; fidelity, not a new look (the user's call,
    /// November 2026: enhance Asheron's Call's own art).
    /// </summary>
    [DataContract]
    public class ImageOptions {
        /// <summary>The world's jagged edges smoothed (FXAA).</summary>
        [DataMember(Name = "antiAlias")] public bool AntiAlias;

        /// <summary>How much soft texture detail is sharpened back, in percent; 0 for none.</summary>
        [DataMember(Name = "sharpen")] public int Sharpen;

        /// <summary>
        /// How strongly glowing things (fires, lanterns, crystals, portals: bright and strongly coloured) glow, in percent;
        /// 0 for none (Ui/DepthShading.Glow.cs).
        /// </summary>
        [DataMember(Name = "glow")] public int Glow;

        public ImageOptions() {
            SetDefaults();
        }

        public int ClampedSharpen => Sharpen < 0 ? 0 : Sharpen > 100 ? 100 : Sharpen;

        public int ClampedGlow => Glow < 0 ? 0 : Glow > 200 ? 200 : Glow;

        /// <summary>Whether any pass is wanted.</summary>
        public bool Any => AntiAlias || ClampedSharpen > 0 || ClampedGlow > 0;

        public string Summary() {
            if (!Any) {
                return "image as the game's";
            }

            var parts = new System.Collections.Generic.List<string>();

            if (AntiAlias) {
                parts.Add("anti-aliased");
            }

            if (ClampedSharpen > 0) {
                parts.Add($"sharpened {ClampedSharpen}%");
            }

            if (ClampedGlow > 0) {
                parts.Add($"glow {ClampedGlow}%");
            }

            return string.Join(", ", parts);
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            AntiAlias = false;
            Sharpen = 0;
            Glow = 0;
        }
    }
}
