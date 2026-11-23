using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Grass and reeds (Ui/Grass.cs), in settings.json as "grass". Off to start; each piece its own setting.
    /// </summary>
    [DataContract]
    public class GrassOptions {
        public const int MinDistance = 15;
        public const int MaxDistance = 200;

        [DataMember(Name = "on")] public bool On;

        /// <summary>How thickly it grows, in percent.</summary>
        [DataMember(Name = "density")] public int Density;

        /// <summary>How far out from you it grows, in meters.</summary>
        [DataMember(Name = "distance")] public int Distance;

        /// <summary>How tall the grass grows, in percent of the usual (half a meter).</summary>
        [DataMember(Name = "height")] public int Height;

        /// <summary>
        /// How tall the reeds grow, in percent of the usual (a meter and a half): its own setting, since with the grass's
        /// they stood over 2 m in the wilds, a wall across the marsh (the user's screenshot, November 2026).
        /// </summary>
        [DataMember(Name = "reedHeight")] public int ReedHeight;

        /// <summary>Reeds on marsh.</summary>
        [DataMember(Name = "reeds")] public bool Reeds;

        /// <summary>Blades bending gently, the tips more than the base.</summary>
        [DataMember(Name = "wind")] public bool Wind;

        /// <summary>Short, kept grass near towns and outposts, taller and fuller out in the wilds (Feed/Wildness.cs).</summary>
        [DataMember(Name = "keptNearTowns")] public bool KeptNearTowns;

        public GrassOptions() {
            SetDefaults();
        }

        public int ClampedDensity => Density < 1 ? 1 : Density > 100 ? 100 : Density;

        public int ClampedDistance => Distance < MinDistance ? MinDistance : Distance > MaxDistance ? MaxDistance : Distance;

        public int ClampedHeight => Height < 25 ? 25 : Height > 250 ? 250 : Height;

        public int ClampedReedHeight => ReedHeight < 25 ? 25 : ReedHeight > 250 ? 250 : ReedHeight;

        public string Summary() {
            return !On ? "grass off"
                : $"grass {ClampedDensity}% to {ClampedDistance} m, {ClampedHeight}% tall"
                + (Reeds ? $", reeds {ClampedReedHeight}% tall" : "") + (Wind ? ", wind" : "") + (KeptNearTowns ? ", kept near towns" : "");
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            On = false;
            Density = 50;
            Distance = 40;
            Height = 100;
            ReedHeight = 80;
            Reeds = true;
            Wind = true;
            KeptNearTowns = true;
        }
    }
}
