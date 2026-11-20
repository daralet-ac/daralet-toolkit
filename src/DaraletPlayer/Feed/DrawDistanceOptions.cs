using System;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Draw distance past the game's own settings (Ui/DrawDistance.cs), in settings.json as "drawDistance". Off to start;
    /// each value's 0 (or the game's 1 for scenery) leaves the game's own.
    /// </summary>
    [DataContract]
    public class DrawDistanceOptions {
        /// <summary>The game's own scenery radius: only the landblock you're in and the eight round it have trees.</summary>
        public const int GameSceneryRadius = 1;
        /// <summary>
        /// 5 at first; raised to see how far it goes with scenery density down (the user's call, November 2026). The
        /// rings patched are R, R+1 and R+3, each a signed byte in a cmp, so up to 124 would fit; the landscape's own
        /// radius (MaxLandscapeRadius) is the real limit, and memory before it (DrawDistance.Memory.cs).
        /// </summary>
        public const int MaxSceneryRadius = 16;

        /// <summary>The game's Landscape Draw Distance choices reach a radius of 3, 5, 8, 11 or 15 landblocks.</summary>
        public const int MinLandscapeRadius = 3;
        public const int MaxLandscapeRadius = 24;


        /// <summary>Whether any of this is used: off, the game's own settings are, as if the plugin weren't there.</summary>
        [DataMember(Name = "on")] public bool On;

        /// <summary>
        /// How many landblocks out from yours are drawn in full detail, with their trees, bushes and rocks: the game's is
        /// 1 (yours and the eight round it). The rings past it, in less detail and with no scenery, move out with it.
        /// </summary>
        [DataMember(Name = "sceneryRadius")] public int SceneryRadius;

        /// <summary>How many landblocks out the landscape is drawn; 0 for the game's Landscape Draw Distance.</summary>
        [DataMember(Name = "landscapeRadius")] public int LandscapeRadius;

        /// <summary>
        /// The percent of the game's trees, bushes and rocks made (Ui/DrawDistance.Density.cs), for frame rate: 100 for
        /// all of them, the game's. Buildings and placed objects always stay.
        /// </summary>
        [DataMember(Name = "sceneryDensity")] public int SceneryDensity;

        public const int MinSceneryDensity = 10;

        /// <summary>
        /// Flat pictures of the trees past the scenery radius, out to the landscape's edge (Ui/DrawDistance.Billboards.cs).
        /// </summary>
        [DataMember(Name = "billboards")] public bool Billboards;

        /// <summary>
        /// Rings of landblocks past the real scenery whose trees the plugin draws itself as the game's own 3D models, one draw
        /// a model (Ui/DrawDistance.FarTrees.cs), before the billboards take over; 0 for none.
        /// </summary>
        [DataMember(Name = "farTrees")] public int FarTrees;

        public const int MaxFarTrees = 8;

        public int ClampedFarTrees => FarTrees < 0 ? 0 : FarTrees > MaxFarTrees ? MaxFarTrees : FarTrees;

        /// <summary>
        /// Buildings and the dats' placed objects past the real scenery, out to the landscape's edge, drawn by the plugin as
        /// the game's own 3D models (Ui/DrawDistance.FarBuildings.cs): the game makes them only for full-detail blocks.
        /// </summary>
        [DataMember(Name = "farBuildings")] public bool FarBuildings;

        /// <summary>
        /// Rings of landblocks past the game's landscape whose ground the plugin draws itself, at full detail, where the
        /// game drew nothing (Ui/FarLand.cs); 0 for none. Works with Draw Distance off, as the fog does.
        /// </summary>
        [DataMember(Name = "farLand")] public int FarLand;

        public const int MaxFarLand = 32;

        /// <summary>
        /// The far land also takes over the game's own low-detail rings (Ui/FarLand.Takeover.cs): the game skips those
        /// blocks and the plugin draws them in full detail.
        /// </summary>
        [DataMember(Name = "farLandNear")] public bool FarLandNear;

        public int ClampedFarLand =>FarLand < 0 ? 0 : FarLand > MaxFarLand ? MaxFarLand : FarLand;

        /// <summary>Whether anything stands for the trees past the real scenery: billboards, 3D trees or both.</summary>
        public bool FarScenery => Billboards || ClampedFarTrees > 0 || FarBuildings;

        /// <summary>
        /// How much of the game's distance fog is drawn, in percent (Ui/DrawDistance.Fog.cs): lower pushes it further out.
        /// 100 for the game's own.
        /// </summary>
        [DataMember(Name = "fog")] public int Fog;

        public const int MinFog = 10;

        public int ClampedFog => Fog <= 0 || Fog >= 100 ? 100 : Fog < MinFog ? MinFog : Fog;

        // Settings that came and went in November 2026, still in a settings.json saved then and read by nothing (the
        // reader skips names it doesn't know): "modelDetail"..., three detail bands, and "levelDistance1"..., three
        // model level distances, which "detailFalloff" replaced, then that too (with Far scenery on, a tree's one real
        // model came back as its plainest, so it changed nothing; wanted again once far trees can be billboards);
        // "sceneryCutOff", "farThinFrom", "farThinPercent", "slowModelsFrom" and "slowModelsEvery", whose hooks only
        // ever caught the sky (thinning it turned it black), never a tree; and "detailDistance", the game's Degrade
        // Distance raised to 1000 m, which changed nothing to see and crashed teleports. Also "lowDetailDistance" (Far
        // scenery: the game's trees reach 1 or 2 landblocks now, 3D trees and billboards past them) and "videoMemory"
        // (always on). CLAUDE.md has how each worked.

        public int ClampedSceneryDensity => SceneryDensity <= 0 || SceneryDensity >= 100 ? 100
            : SceneryDensity < MinSceneryDensity ? MinSceneryDensity : SceneryDensity;

        public DrawDistanceOptions() {
            SetDefaults();
        }

        public int ClampedSceneryRadius => SceneryRadius < GameSceneryRadius ? GameSceneryRadius
            : SceneryRadius > MaxSceneryRadius ? MaxSceneryRadius : SceneryRadius;

        /// <summary>
        /// How many landblocks out the ground is in full detail (Ui/DrawDistance.Ground.cs): roads, shores and water edges
        /// drawn right, without the game's trees past Scenery. Never less than Scenery; 0 for the same.
        /// </summary>
        [DataMember(Name = "groundRadius")] public int GroundRadius;

        public int ClampedGroundRadius => Math.Max(ClampedSceneryRadius, Math.Min(MaxSceneryRadius, GroundRadius));

        /// <summary>A copy, for the Graphics tab to work out what a change would cost before it's made.</summary>
        public DrawDistanceOptions Copy() => (DrawDistanceOptions)MemberwiseClone();

        /// <summary>The landscape radius wanted, or 0 for the game's own.</summary>
        public int ClampedLandscapeRadius => LandscapeRadius <= 0 ? 0
            : LandscapeRadius < MinLandscapeRadius ? MinLandscapeRadius
            : LandscapeRadius > MaxLandscapeRadius ? MaxLandscapeRadius : LandscapeRadius;

        /// <summary>The settings in a line, for the Frame Rate window's marks: "Scenery 5, landscape 24, ...".</summary>
        public string Summary() {
            var farLand = ClampedFarLand > 0
                ? $", far land {ClampedFarLand}" + (FarLandNear ? " with the near rings" : "") : "";

            if (!On) {
                return "Draw distance off" + farLand + (ClampedFog < 100 ? $", fog {ClampedFog}%" : "");
            }

            return $"Scenery {ClampedSceneryRadius} at {ClampedSceneryDensity}%, "
                + (ClampedGroundRadius > ClampedSceneryRadius ? $"ground {ClampedGroundRadius}, " : "") + $"landscape "
                + (ClampedLandscapeRadius == 0 ? "game's" : ClampedLandscapeRadius.ToString())
                + (Billboards ? ", billboards" : "")
                + (ClampedFarTrees > 0 ? $", 3D trees {ClampedFarTrees} rings" : "")
                + (FarBuildings ? ", far buildings" : "")
                + (ClampedFog < 100 ? $", fog {ClampedFog}%" : "") + farLand;
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            On = false;
            SceneryRadius = GameSceneryRadius;
            GroundRadius = 0;
            LandscapeRadius = 0;
            SceneryDensity = 100;
            Billboards = false;
            FarTrees = 0;
            FarBuildings = false;
            Fog = 100;
            FarLand = 0;
            FarLandNear = false;
        }
    }
}
