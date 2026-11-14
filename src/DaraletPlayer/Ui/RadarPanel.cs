using System;
using System.Numerics;
using DaraletPlayer.Feed;
using DaraletPlayer.Map;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Radar window (radar, hidden to start): what's around you, in place of the game's radar (element 0x100006D2,
    /// Decal's UIElementType Radar). Round or square, sized by its edges and kept square (FloatingWindow.KeepSquare), and
    /// zoomed with the mouse wheel, never further out than the game's own radar reaches: CPlayerSystem::GetRadarRadius
    /// (0x55E5A0), 75 meters outdoors and 25 indoors, measured flat (ObjectIsWithinRadarRange, 0x560CE0, leaves height out).
    /// Indoors it draws the Dungeon window's map of the place under the blips (DungeonView.DrawUnderlay), cut to the
    /// circle when it's round. Every kind of blip is shown or not, and coloured, on its own (RadarOptions).
    /// </summary>
    /// <remarks>The blips are in <c>RadarPanel.Blips.cs</c>, the settings and menu in
    /// <c>RadarPanel.Settings.cs</c>.</remarks>
    internal sealed partial class RadarPanel : GamePanel {
        /// <summary>The game's radar, as Decal's UIElementType names it.</summary>
        public const uint GameRadar = 0x100006D2;

        private static readonly uint RetailBack = MapView.Col(0f, 0f, 0f, 0.7f);
        private static readonly uint RetailRim = MapView.Col(0.78f, 0.64f, 0.34f, 1f);
        private static readonly uint RetailLines = MapView.Col(0.78f, 0.64f, 0.34f, 0.25f);
        /// <summary>Green, as the game's own N token (060011FB) is.</summary>
        private static readonly Vector4 NorthColor = new Vector4(0.2f, 1f, 0.2f, 1f);
        private static readonly Vector4 LetterColor = new Vector4(0.95f, 0.85f, 0.6f, 1f);

        private readonly PlayerFeed feed;
        private readonly DungeonWindow dungeonWindow;
        private readonly SelfPosition self;
        private readonly DungeonView underlay = new DungeonView();

        public RadarPanel(Settings settings, PlayerFeed feed, DungeonWindow dungeonWindow)
            : base("radar", "Radar", settings, new[] { GameRadar }, new Vector2(640, 20),
                defaultSize: new Vector2(200, 200), minSize: new Vector2(90, 90)) {
            this.feed = feed;
            this.dungeonWindow = dungeonWindow;
            self = new SelfPosition(feed);
            Window.KeepSquare = true;
            // the wheel zooms; and a scroll bar showing at some sizes narrowed the radar, which then fit, so the bar went
            // and came back each frame: the window flickered
            Window.ExtraFlags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse;
        }

        private RadarOptions Data => settings.Data.Radar ?? (settings.Data.Radar = new RadarOptions());

        /// <summary>Its settings are the World tab's, beside the Dungeon window's (WorldTab.RadarSettings).</summary>
        public override string SettingsTab => WorldTab.SettingsTabName;

        protected override string ReplaceTip => "Hides the game's radar and shows the plugin's Radar window in its place.";

        protected override string WindowTip => "What's around you, round or square: monsters, NPCs, players, fellows, "
            + "portals and more, each in a colour of your own, with the dungeon map under them indoors. Scroll over it to "
            + "zoom (never further out than the game's own radar: 75 meters outdoors, 25 indoors), drag its edges to size "
            + "it, click a blip to select it.";

        private static bool LoggedIn => CoreManager.Current.CharacterFilter.LoginStatus == 3;

        protected override void RenderContents() {
            // Decal's hooks read the game's memory, which isn't there until login is over
            if (!LoggedIn) {
                Widgets.TextDisabled("Not in the world yet.");
                return;
            }

            var data = Data;
            var actions = CoreManager.Current.Actions;
            var landcell = unchecked((uint)actions.Landcell);

            if (landcell == 0) {
                return;
            }

            var indoors = !Navigation.IsOutdoors(landcell);
            var range = RadarMath.ClampRange(indoors ? data.IndoorRange : data.OutdoorRange, indoors);
            var avail = ImGui.GetContentRegionAvail();
            var side = (float)Math.Floor(Math.Min(avail.X, avail.Y));

            if (side < 40) {
                return;
            }

            var start = ImGui.GetCursorScreenPos();
            var origin = start + new Vector2((float)Math.Floor((avail.X - side) / 2), 0);
            var box = new Vector2(side, side);
            var mid = origin + box / 2;
            var round = !data.IsSquare;
            // the game's own disc, rim and all, for a round Retail radar; what's on it stays inside the rim
            var art = round && Retail && ItemIcons.CanDrawArt;
            var radius = art ? side / 2 * DiscInner / DiscOuter : side / 2 - 1;
            var heading = actions.Heading;
            var selfLocal = new Vector3((float)actions.LocationX, (float)actions.LocationY, (float)actions.LocationZ);
            var selfWorld = RadarMath.WorldMeters(landcell, actions.LocationX, actions.LocationY);

            ImGui.SetCursorScreenPos(origin);
            ImGui.InvisibleButton("radar", box, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
            var hovered = ImGui.IsItemHovered();

            if (hovered && ImGui.GetIO().MouseWheel != 0) {
                range = Zoom(data, indoors, range, ImGui.GetIO().MouseWheel);
            }

            var drawList = ImGui.GetWindowDrawList();
            drawList.PushClipRect(origin, origin + box, true);
            DrawBackground(drawList, origin, box, mid, radius, round, art);

            if (indoors && data.DungeonMap) {
                DrawDungeon(drawList, landcell, selfLocal, heading, data, mid, radius - (art ? 0 : 1), radius / range,
                    round);
            }

            DrawGuides(drawList, mid, radius, round, art, data);

            RefreshBlips(landcell, selfWorld, range, data);
            var hover = DrawBlips(drawList, mid, radius, range, selfWorld, selfLocal.Z, heading, indoors, data,
                hovered ? ImGui.GetIO().MousePos : (Vector2?)null);

            MapView.SelfMarker(drawList, mid, data.Turn ? 0 : heading);

            if (data.Compass) {
                if (art) {
                    DrawArtCompass(drawList, mid, side, heading, data.Turn);
                }
                else {
                    DrawCompass(drawList, mid, radius, round, heading, data.Turn);
                }
            }

            DrawLabels(drawList, origin, box, range);
            drawList.PopClipRect();

            HandleClicks(hovered, hover);

            if (hovered && hover != null) {
                BlipTooltip(hover, selfWorld);
            }
            else if (hovered) {
                Widgets.Tip($"{range:0} meters to the edge. Scroll to zoom: as far out as the game's own radar, "
                    + $"{RadarMath.MaxRange(indoors):0} meters {(indoors ? "indoors" : "outdoors")}. Right-click for "
                    + "its settings.");
            }
        }

        /// <summary>The mouse wheel's zoom, kept for indoors and outdoors apart, each held to the game's limit.</summary>
        private float Zoom(RadarOptions data, bool indoors, float range, float wheel) {
            var zoomed = RadarMath.ClampRange(range * (wheel > 0 ? 0.8f : 1.25f), indoors);

            if (indoors) {
                data.IndoorRange = zoomed;
            }
            else {
                data.OutdoorRange = zoomed;
            }

            settings.Changed();
            return zoomed;
        }

        private void DrawBackground(ImDrawListPtr drawList, Vector2 origin, Vector2 box, Vector2 mid, float radius,
            bool round, bool art) {
            if (art && ItemIcons.DrawArtSmooth(drawList, "radar-disc", origin, origin + box)) {
                return;
            }

            var back = Retail ? RetailBack : ImGui.GetColorU32(ImGuiCol.FrameBg);
            var rim = Retail ? RetailRim : ImGui.GetColorU32(ImGuiCol.Border);

            if (round) {
                drawList.AddCircleFilled(mid, radius, back, 64);
                drawList.AddCircle(mid, radius, rim, 64, 2);
            }
            else {
                drawList.AddRectFilled(mid - new Vector2(radius), mid + new Vector2(radius), back);
                drawList.AddRect(mid - new Vector2(radius), mid + new Vector2(radius), rim, 0, ImDrawFlags.None, 2);
            }
        }

        /// <summary>Indoors, the dungeon map under the blips, centred on you, turning with the radar.</summary>
        private void DrawDungeon(ImDrawListPtr drawList, uint landcell, Vector3 selfLocal, double heading,
            RadarOptions data, Vector2 mid, float mapRadius, float pixelsPerMeter, bool round) {
            var layout = dungeonWindow.LayoutFor(landcell);

            if (layout == null || layout.IsEmpty) {
                return;
            }

            // the map is drawn inside the rim, so the rim stays whole
            var half = new Vector2(mapRadius);
            underlay.DrawUnderlay(drawList, new DungeonScene {
                Layout = layout,
                Self = selfLocal,
                Heading = heading,
                Turn = data.Turn,
                AllFloors = data.AllFloors,
                FloorsBelow = data.ClampedFloorsBelow,
                FloorsAbove = data.ClampedFloorsAbove,
                Visited = data.ShowVisited ? dungeonWindow.VisitedIn(layout.Landblock) : null,
                VisitedColor = settings.Data.DungeonVisitedColor,
                Opacity = Math.Max(0, Math.Min(1, data.DungeonOpacity)),
            }, mid - half, half * 2, new Vector2(selfLocal.X, selfLocal.Y), pixelsPerMeter, round);
        }

        /// <summary>
        /// The cross through the middle (not on the game's disc, which has none), and the rings at a third and two thirds
        /// of the range.
        /// </summary>
        private void DrawGuides(ImDrawListPtr drawList, Vector2 mid, float radius, bool round, bool art,
            RadarOptions data) {
            var lines = Retail ? RetailLines : PackedColor.Fade(ImGui.GetColorU32(ImGuiCol.Border), 0.5f);

            if (!art) {
                drawList.AddLine(mid - new Vector2(radius, 0), mid + new Vector2(radius, 0), lines);
                drawList.AddLine(mid - new Vector2(0, radius), mid + new Vector2(0, radius), lines);
            }

            if (data.Rings) {
                drawList.AddCircle(mid, radius / 3, lines, 48);
                drawList.AddCircle(mid, radius * 2 / 3, lines, 48);
            }

            // a square radar's corners are further than the game's radar reaches: nothing's shown past this
            if (!round) {
                drawList.AddCircle(mid, radius, lines, 64);
            }
        }

        /// <summary>
        /// N, E, S and W round the edge, turning with the radar when it turns: north in green, as the game's own N.
        /// </summary>
        private static void DrawCompass(ImDrawListPtr drawList, Vector2 mid, float radius, bool round, double heading,
            bool turn) {
            var letters = new (string Letter, Vector2 Way)[] {
                ("N", new Vector2(0, 1)), ("E", new Vector2(1, 0)), ("S", new Vector2(0, -1)), ("W", new Vector2(-1, 0)),
            };
            // a little bigger than the other labels, and bold, so they read over the map and the blips
            var fontSize = Widgets.SquareFontSize() * 1.2f;

            foreach (var (letter, way) in letters) {
                var dir = RadarMath.ToRadar(way, 1, heading, turn);

                // to the square's edge, not the circle's, on a square radar
                if (!round) {
                    dir /= Math.Max(Math.Abs(dir.X), Math.Abs(dir.Y));
                }

                var size = ImGui.CalcTextSize(letter) * (fontSize / ImGui.GetFontSize());
                var pad = Math.Max(size.X, size.Y) * 0.5f + 3;
                var at = mid + dir * (radius - pad) - size / 2;
                BoldLetter(drawList, letter, at, fontSize, letter == "N" ? NorthColor : LetterColor);
            }
        }

        /// <summary>
        /// A compass letter drawn heavy: a two-pixel black outline all round, then the letter twice a pixel apart (ImGui
        /// has no bold font).
        /// </summary>
        private static void BoldLetter(ImDrawListPtr drawList, string letter, Vector2 at, float fontSize, Vector4 color) {
            var spot = new Vector2((float)Math.Round(at.X), (float)Math.Round(at.Y));
            var font = ImGui.GetFont();
            var black = ImGui.GetColorU32(new Vector4(0, 0, 0, 0.9f));

            for (var x = -2; x <= 3; x++) {
                for (var y = -2; y <= 2; y++) {
                    if (Math.Abs(x) + Math.Abs(y) > 0 && Math.Abs(x - 0.5f) + Math.Abs(y) <= 3.5f) {
                        drawList.AddText(font, fontSize, spot + new Vector2(x, y), black, letter);
                    }
                }
            }

            var fill = ImGui.GetColorU32(color);
            drawList.AddText(font, fontSize, spot, fill, letter);
            drawList.AddText(font, fontSize, spot + new Vector2(1, 0), fill, letter);
        }

        /// <summary>The range in the top left corner, and your coordinates along the bottom.</summary>
        private void DrawLabels(ImDrawListPtr drawList, Vector2 origin, Vector2 box, float range) {
            var fontSize = Widgets.SquareFontSize();
            var scale = fontSize / ImGui.GetFontSize();
            ItemIcons.OutlinedText(drawList, $"{range:0} m", origin + new Vector2(3, 2), LetterColor);

            if (!Data.Coordinates || !self.TryGet(out var x, out var y, out _)) {
                return;
            }

            var text = MapProjection.Format(x, y);
            var size = ImGui.CalcTextSize(text) * scale;
            ItemIcons.OutlinedText(drawList, text, origin + new Vector2((box.X - size.X) / 2, box.Y - size.Y - 3),
                new Vector4(1, 1, 1, 1));
        }
    }
}
