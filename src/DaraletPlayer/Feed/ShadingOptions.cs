using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Shading worked out from the game's depth buffer (Ui/DepthShading.cs), in settings.json as "shading". Off to start.
    /// </summary>
    [DataContract]
    public class ShadingOptions {
        /// <summary>Whether any of it is drawn.</summary>
        [DataMember(Name = "on")] public bool On;

        /// <summary>How dark ambient occlusion makes corners and the ground under things, in percent; 0 for none.</summary>
        [DataMember(Name = "occlusion")] public int Occlusion;

        public ShadingOptions() {
            SetDefaults();
        }

        public int ClampedOcclusion => Occlusion < 0 ? 0 : Occlusion > 100 ? 100 : Occlusion;

        /// <summary>The settings in a line, for the Frame Rate window's marks.</summary>
        public string Summary() {
            return On ? $"shading: occlusion {ClampedOcclusion}%" : "shading off";
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            On = false;
            Occlusion = 60;
        }
    }
}
