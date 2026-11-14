using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>The Radar window's own choices (Ui/RadarPanel.cs), in settings.json as "radar".</summary>
    [DataContract]
    public class RadarOptions {
        public const string Circle = "circle";
        public const string Square = "square";

        public const int MaxFloorsBelow = 8;
        public const int MaxFloorsAbove = 3;

        public const float MinBlipScale = 0.5f;
        public const float MaxBlipScale = 3f;

        /// <summary>Round, as the game's, or square.</summary>
        [DataMember(Name = "shape")] public string Shape;
        /// <summary>Meters from the middle to the edge outdoors (zoom), up to the game's 75.</summary>
        [DataMember(Name = "outdoorRange")] public float OutdoorRange;
        /// <summary>Meters from the middle to the edge indoors, up to the game's 25.</summary>
        [DataMember(Name = "indoorRange")] public float IndoorRange;
        /// <summary>The way you face is up, as the game's; off, north is.</summary>
        [DataMember(Name = "turn")] public bool Turn;
        [DataMember(Name = "compass")] public bool Compass;
        [DataMember(Name = "coordinates")] public bool Coordinates;
        /// <summary>Faint rings at a third and two thirds of the range.</summary>
        [DataMember(Name = "rings")] public bool Rings;
        /// <summary>Indoors, the dungeon map under the blips (the Dungeon window's, drawn from the dats).</summary>
        [DataMember(Name = "dungeonMap")] public bool DungeonMap;
        /// <summary>How solid the dungeon map under the blips is, 0 to 1.</summary>
        [DataMember(Name = "dungeonOpacity")] public float DungeonOpacity;
        /// <summary>How many floors below yours the dungeon map draws, dimmer the deeper.</summary>
        [DataMember(Name = "floorsBelow")] public int FloorsBelow;
        /// <summary>How many floors above yours the dungeon map draws, as a faint outline.</summary>
        [DataMember(Name = "floorsAbove")] public int FloorsAbove;
        /// <summary>Every floor of the dungeon, whatever FloorsBelow and FloorsAbove say.</summary>
        [DataMember(Name = "allFloors")] public bool AllFloors;
        /// <summary>The rooms you've been in, in their own colour (the Dungeon window's).</summary>
        [DataMember(Name = "showVisited")] public bool ShowVisited;
        /// <summary>Indoors, blips more than half a floor above or below you are drawn faint.</summary>
        [DataMember(Name = "dimOtherFloors")] public bool DimOtherFloors;
        /// <summary>
        /// An object the server gave a radar colour of its own (a portal device's purple) is drawn in it, as on the game's
        /// radar; off, every blip takes its kind's colour.
        /// </summary>
        [DataMember(Name = "serverColors")] public bool ServerColors;
        /// <summary>
        /// The ring round your selection in white; off, in the blip's own colour, as the game's radar draws it.
        /// </summary>
        [DataMember(Name = "whiteSelection")] public bool WhiteSelection;
        /// <summary>How big the blips are, times the usual.</summary>
        [DataMember(Name = "blipScale")] public float BlipScale;
        /// <summary>Each kind shown or not, by RadarKindInfo.Key; a kind not here takes its own Shown.</summary>
        [DataMember(Name = "kinds")] public Dictionary<string, bool> Kinds;
        /// <summary>Each kind's colour, packed as ImGui's, by RadarKindInfo.Key; a kind not here takes its own.</summary>
        [DataMember(Name = "colors")] public Dictionary<string, uint> Colors;

        public RadarOptions() {
            SetDefaults();
        }

        public bool IsSquare => Shape == Square;

        public float ClampedBlipScale => Math.Max(MinBlipScale, Math.Min(MaxBlipScale,
            float.IsNaN(BlipScale) || BlipScale <= 0 ? 1 : BlipScale));

        public int ClampedFloorsBelow => Math.Max(0, Math.Min(MaxFloorsBelow, FloorsBelow));

        public int ClampedFloorsAbove => Math.Max(0, Math.Min(MaxFloorsAbove, FloorsAbove));

        public bool Shows(RadarKindInfo kind) {
            return Kinds != null && Kinds.TryGetValue(kind.Key, out var shown) ? shown : kind.Shown;
        }

        public uint ColorOf(RadarKindInfo kind) {
            return Colors != null && Colors.TryGetValue(kind.Key, out var color) ? color : kind.Color;
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            Shape = Circle;
            OutdoorRange = Map.RadarMath.OutdoorRange;
            IndoorRange = Map.RadarMath.IndoorRange;
            Turn = true;
            Compass = true;
            Coordinates = true;
            DungeonMap = true;
            DungeonOpacity = 0.6f;
            // fewer than the Dungeon window's 4: a radar is small, and the floors below crowd it
            FloorsBelow = 1;
            FloorsAbove = 1;
            ShowVisited = true;
            DimOtherFloors = true;
            BlipScale = 1;
            ServerColors = true;
            Kinds = new Dictionary<string, bool>();
            Colors = new Dictionary<string, uint>();
        }
    }
}
