using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Shadows cast by the sun (Ui/ShadowCasters.cs, Ui/DepthShading.Shadows.cs), in settings.json as "sunShadows". Off to
    /// start; an experiment (the user's call, November 2026: "especially sun light").
    /// </summary>
    [DataContract]
    public class SunShadowOptions {
        public const int MinDistance = 20;
        public const int MaxDistance = 400;

        [DataMember(Name = "on")] public bool On;

        /// <summary>
        /// How dark, in percent of the way from lit down to the game's own ambient light (100: a shadow is just what the
        /// game would light without the sun).
        /// </summary>
        [DataMember(Name = "strength")] public int Strength;

        /// <summary>How far from you things cast shadows, in meters (the far map; the near one is the nearest 40).</summary>
        [DataMember(Name = "distance")] public int Distance;

        /// <summary>How soft the edges are, in centimeters across (0 for a texel or so; 10 to start, the user's pick).</summary>
        [DataMember(Name = "softness")] public int Softness;

        public SunShadowOptions() {
            SetDefaults();
        }

        public int ClampedStrength => Strength < 0 ? 0 : Strength > 100 ? 100 : Strength;

        public const int MaxSoftness = 20;

        public int ClampedSoftness => Softness < 0 ? 0 : Softness > MaxSoftness ? MaxSoftness : Softness;

        /// <summary>
        /// How soft your character's own shadow is, in centimeters across: its close map is 8 mm a texel, and trees'
        /// soft edges blunted it (the user's call, November 2026: characters want other blurring than trees).
        /// </summary>
        [DataMember(Name = "characterSoftness")] public int CharacterSoftness;

        public int ClampedCharacterSoftness => CharacterSoftness < 0 ? 0
            : CharacterSoftness > MaxSoftness ? MaxSoftness : CharacterSoftness;

        public int ClampedDistance => Distance < MinDistance ? MinDistance : Distance > MaxDistance ? MaxDistance : Distance;

        public string Summary() {
            return On ? $"sun shadows {ClampedStrength}% to {ClampedDistance} m, soft {ClampedSoftness} cm, character {ClampedCharacterSoftness} cm"
                : "sun shadows off";
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            On = false;
            Strength = 100;
            Distance = 150;
            Softness = 10;
            CharacterSoftness = 2;
        }
    }
}
