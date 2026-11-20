using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Light from the game's own lights, per pixel, with shadows (Ui/DepthShading.Lamps.cs), in settings.json as
    /// "lampLight". Off to start; an experiment (the user's call, November 2026: lighting and shadows from other than
    /// the sun).
    /// </summary>
    [DataContract]
    public class LampLightOptions {
        [DataMember(Name = "on")] public bool On;

        /// <summary>How bright, in percent.</summary>
        [DataMember(Name = "strength")] public int Strength;

        /// <summary>
        /// Whether walls and things block a lamp's light: the plugin then lights the lamps instead of the game
        /// (Ui/GameLampSwitch.cs), each with its own shadow map, so their light stops at walls and floors and comes out
        /// through doors and windows. Its "replace" switch, once of its own, is left unread.
        /// </summary>
        [DataMember(Name = "shadows")] public bool Shadows;

        /// <summary>
        /// How far the lamps reach, in percent of the game's own (1.5 times their falloff): lighting with shadows,
        /// further is safe, and light comes out of a door (the Holtburg tavern's lamps reach 4.5 to 7.5 m, hung 9 m in).
        /// </summary>
        [DataMember(Name = "reach")] public int Reach;

        /// <summary>Fires the game gives no light (a town's bonfire) lit by the plugin (Ui/FireLights.cs).</summary>
        [DataMember(Name = "fireLight")] public bool FireLight;

        public int ClampedReach => Reach < 100 ? 100 : Reach > 300 ? 300 : Reach;

        /// <summary>
        /// How much darker everything is at night, in percent (0 off): ground, objects, characters and sky, outdoors,
        /// so the game's lights stand out (the user's call, November 2026: more immersive nights). By the sun's height.
        /// </summary>
        [DataMember(Name = "nightDarkness")] public int NightDarkness;

        /// <summary>How much stronger the lamps are at night, in percent of their own (100 the same).</summary>
        [DataMember(Name = "nightGlow")] public int NightGlow;

        public const int MostNightDarkness = 80;

        public int ClampedNightDarkness => NightDarkness < 0 ? 0
            : NightDarkness > MostNightDarkness ? MostNightDarkness : NightDarkness;

        public int ClampedNightGlow => NightGlow < 100 ? 100 : NightGlow > 400 ? 400 : NightGlow;

        public LampLightOptions() {
            SetDefaults();
        }

        public int ClampedStrength => Strength < 0 ? 0 : Strength > 200 ? 200 : Strength;

        public string Summary() {
            return On ? $"lamps {ClampedStrength}%" + (Shadows ? " with shadows" : "") + $", reach {ClampedReach}%"
                + (ClampedNightDarkness > 0 ? $", nights {ClampedNightDarkness}% darker, lamps {ClampedNightGlow}%" : "")
                : "lamps off";
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            On = false;
            Strength = 60;
            Shadows = true;
            NightDarkness = 0;
            NightGlow = 200;
            Reach = 150;
            FireLight = true;
        }
    }
}
