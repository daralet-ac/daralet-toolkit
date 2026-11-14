using System;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Power bar window (power-bar): the power bar of melee combat, the accuracy bar of missile, the desired power the
    /// game's slider sets, and the High, Medium and Low buttons, in place of the game's combat panel in those modes. The
    /// game's is the Combat floaty, element 0x100006B5 (Decal's UIElementType Combat; its layout 0x21000073, a 610x90
    /// frame), which holds gmCombatUI (0x1000005C, layout 0x21000007) for melee and missile and the spell bar
    /// (0x10000061) for magic, so it's hidden only in melee and missile, looked at every frame (the game shows it again
    /// at every change of mode), and shown again only in a combat mode (the game hides it in peace). Everything is the
    /// client's own combat system (ClientCombat): the bar's level, the desired power, which height is picked, and pressing
    /// and letting go of a height, as the game's buttons do. Retail draws gmCombatUI's art (Resources/ui power-* and
    /// attack-height-*), Modern ImGui's. Its choices are PowerBarOptions.
    /// </summary>
    internal sealed class PowerBarPanel : GamePanel {
        /// <summary>The game's combat panel, as Decal's UIElementType names it.</summary>
        public const uint GameCombat = 0x100006B5;

        /// <summary>The virtual key of the left mouse button, for a press the window never saw let go.</summary>
        private const int LeftButton = 0x01;

        // the game's sizes, in layout 0x21000007: the bar 14 high in 60x13 tiles, the thumb 12x14, the buttons 72x19
        private const float RetailBarHeight = 14;
        private static readonly Vector2 Tile = new Vector2(60, 13);
        private static readonly Vector2 Thumb = new Vector2(12, 14);
        private static readonly Vector2 RetailButton = new Vector2(72, 19);

        // packed by hand: ImGui.GetColorU32 crashes before ImGui has a context
        private static readonly uint TextColor = PackedColor.Pack(255, 255, 255);
        private static readonly uint TextOutline = PackedColor.Pack(0, 0, 0, 230);
        private static readonly uint ModernFill = PackedColor.Pack(200, 60, 40);
        private static readonly uint ModernDesired = PackedColor.Pack(90, 220, 90);

        private static readonly Vector2[] Outline = {
            new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1),
        };

        /// <summary>The height whose button is held, 0 for none: let go of, even if the window goes before the mouse does.</summary>
        private int heldHeight;

        /// <summary>The bar's width while its grip is dragged, in pixels at 100%, before rounding; null otherwise.</summary>
        private float? dragWidth;

        public PowerBarPanel(Settings settings)
            : base("power-bar", "Power bar", settings, new[] { GameCombat }, new Vector2(250, 440), inUse: InUse,
                hideWhen: () => PowerBarOptions.IsAttackMode(ClientCombat.Mode), checkEvery: TimeSpan.Zero,
                showAgain: (id, wasShown) => wasShown && PowerBarOptions.IsCombatMode(ClientCombat.Mode)) {
            // a power bar out of combat is in the way: Only when needed to start with
            if (!Data.WindowSetUp) {
                Data.WindowSetUp = true;
                settings.Window(Key).OnlyInUse = true;
                settings.Changed();
            }

            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        public override string SettingsTab => GamePanels.CombatBarsTab;

        private PowerBarOptions Data => settings.Data.PowerBar ?? (settings.Data.PowerBar = new PowerBarOptions());

        protected override string ReplaceTip => "Hides the game's combat panel in melee and missile combat (its power or "
            + "accuracy bar and its High, Medium and Low buttons) and shows the plugin's Power bar window in its place. "
            + "In magic the game's spell bar shows as ever.";

        protected override string WindowTip => "Shows the power bar (melee) or accuracy bar (missile), the desired power, "
            + "and the High, Medium and Low attack buttons in a window of their own, in the game's art or the plugin's "
            + "theme.";

        /// <summary>The combat mode while it's melee or missile, else nothing to show.</summary>
        private static object InUse() {
            var mode = ClientCombat.Mode;
            return PowerBarOptions.IsAttackMode(mode) ? (object)mode : null;
        }

        /// <summary>A held height button is let go of when the mouse is, wherever it is, or the attack would never end.</summary>
        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (heldHeight != 0 && !Hotkeys.IsDown(LeftButton)) {
                    Release();
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void Release() {
            var height = heldHeight;
            heldHeight = 0;
            ClientCombat.ReleaseHeight(height);
        }

        protected override void RenderContents() {
            if (!ClientCombat.TryRead(out var combat)) {
                Widgets.TextDisabled(ClientCombat.Broken ? "The game's combat system can't be read (see log.txt)."
                    : "Not in the world yet.");
                return;
            }

            if (!PowerBarOptions.IsAttackMode(combat.Mode)) {
                Widgets.TextDisabled("Only in melee and missile combat.");
                return;
            }

            var data = Data;
            var retail = Retail;
            var width = Widgets.Scaled(data.ClampedBarWidth);
            var height = Math.Max(retail ? Widgets.Scaled(RetailBarHeight) : ImGui.GetFrameHeight(),
                ImGui.GetTextLineHeight());

            ImGui.BeginGroup();
            ImGui.BeginGroup();
            RenderBar(combat, width, height, retail, data.ShowText);
            ImGui.EndGroup();

            if (data.ShowHeights) {
                if (!data.HeightsInRow) {
                    ImGui.SameLine();
                }

                RenderHeights(combat, retail, data.HeightsInRow ? width : 0);
            }

            ImGui.EndGroup();
            ResizeBar(data);
        }

        /// <summary>The grip at the right: dragging it makes the bar wider or narrower (Bar width).</summary>
        private void ResizeBar(PowerBarOptions data) {
            var moved = WidthGrip("Drag to make the bar wider or narrower.");

            if (moved == 0) {
                dragWidth = null;
                return;
            }

            // kept as a fraction between frames, or slow drags would round away to nothing
            dragWidth = (dragWidth ?? data.ClampedBarWidth) + moved;
            var width = (int)Math.Round(Math.Max(PowerBarOptions.MinBarWidth,
                Math.Min(PowerBarOptions.MaxBarWidth, dragWidth.Value)));

            if (width != data.BarWidth) {
                data.BarWidth = width;
                settings.Changed();
            }
        }

        /// <summary>
        /// The bar: filled to its level, the desired power marked, which a click or drag along it sets.
        /// </summary>
        private void RenderBar(CombatSnapshot combat, float width, float height, bool retail, bool showText) {
            var min = ImGui.GetCursorScreenPos();
            var max = min + new Vector2(width, height);
            ImGui.InvisibleButton("##power-bar", new Vector2(width, height));

            if (ImGui.IsItemActive()) {
                var wanted = PowerBarOptions.Clamp((ImGui.GetIO().MousePos.X - min.X) / width);

                if (Math.Abs(wanted - combat.Desired) > 0.001f && ClientCombat.SetDesiredPower(wanted)) {
                    combat.Desired = wanted;
                }
            }

            var label = PowerBarOptions.LabelFor(combat.Mode);
            Widgets.Tip($"{PowerBarOptions.Text(combat.Mode, combat.Level)}. The green mark is the desired "
                + $"{label.ToLowerInvariant()}, {(int)Math.Round(combat.Desired * 100)}%: no attack goes past it. Click or "
                + "drag along the bar to set it, as with the game's slider."
                + (combat.Advanced ? " Advanced combat is on, so attacks build to it by themselves." : ""));

            var drawList = ImGui.GetWindowDrawList();
            var cut = (float)Math.Round(min.X + width * combat.Level);
            var markX = (float)Math.Round(min.X + width * combat.Desired);

            if (!retail || !DrawRetailBar(drawList, min, max, cut, markX)) {
                DrawModernBar(drawList, min, max, cut, markX);
            }

            if (showText) {
                var text = PowerBarOptions.Text(combat.Mode, combat.Level);
                var size = ImGui.CalcTextSize(text);
                DrawOutlined(drawList, text, new Vector2((float)Math.Round(min.X + (width - size.X) / 2),
                    (float)Math.Round(min.Y + (height - size.Y) / 2)));
            }
        }

        /// <summary>gmCombatUI's slider: the grey track, the red fill cut off at the level, the green thumb.</summary>
        private static bool DrawRetailBar(ImDrawListPtr drawList, Vector2 min, Vector2 max, float cut, float markX) {
            var tileWidth = Tile.X * (max.Y - min.Y) / Tile.Y;

            if (!ItemIcons.DrawArtTiled(drawList, "power-track", min, max, tileWidth)) {
                return false;
            }

            if (cut > min.X) {
                ItemIcons.DrawArtTiled(drawList, "power-fill", min, new Vector2(cut, max.Y), tileWidth);
            }

            var thumb = new Vector2(Thumb.X * (max.Y - min.Y) / Thumb.Y, max.Y - min.Y);
            var left = Math.Max(min.X, Math.Min(max.X - thumb.X, markX - thumb.X / 2));
            ItemIcons.DrawArtBox(drawList, "power-thumb", new Vector2(left, min.Y), new Vector2(left + thumb.X, max.Y));
            return true;
        }

        /// <summary>A frame-coloured track, a red fill, and a green line with a notch for the desired power.</summary>
        private static void DrawModernBar(ImDrawListPtr drawList, Vector2 min, Vector2 max, float cut, float markX) {
            var rounding = ImGui.GetStyle().FrameRounding;
            drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBg), rounding);

            if (cut > min.X) {
                drawList.AddRectFilled(min, new Vector2(cut, max.Y), ModernFill, rounding);
            }

            var notch = Math.Max(3, (max.Y - min.Y) / 4);
            drawList.AddLine(new Vector2(markX, min.Y), new Vector2(markX, max.Y), ModernDesired, 2);
            drawList.AddTriangleFilled(new Vector2(markX - notch, min.Y), new Vector2(markX + notch, min.Y),
                new Vector2(markX, min.Y + notch), ModernDesired);
        }

        /// <summary>
        /// High, Medium and Low: held to build an attack at that height and let go of to make it, as the game's are. Stacked
        /// beside the bar as the game has them, High on top, or in a row of this width (0 for stacked), Low on the left and
        /// High on the right (the user's call).
        /// </summary>
        private void RenderHeights(CombatSnapshot combat, bool retail, float rowWidth) {
            var spacing = ImGui.GetStyle().ItemSpacing;
            var size = retail ? new Vector2(Widgets.Scaled(RetailButton.X), Widgets.Scaled(RetailButton.Y))
                : new Vector2(Widgets.Scaled(RetailButton.X), ImGui.GetFrameHeight());

            if (rowWidth > 0) {
                size.X = Math.Max(size.X, (rowWidth - spacing.X * (PowerBarOptions.Heights.Length - 1))
                    / PowerBarOptions.Heights.Length);
            }

            size.Y = Math.Max(size.Y, ImGui.GetTextLineHeight());
            ImGui.BeginGroup();
            // the game's stack its buttons with no gap between
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, rowWidth > 0 ? spacing : new Vector2(spacing.X, 0));

            var heights = PowerBarOptions.HeightsInOrder(rowWidth > 0);

            for (var i = 0; i < heights.Length; i++) {
                if (i > 0 && rowWidth > 0) {
                    ImGui.SameLine();
                }

                RenderHeight(heights[i], combat, retail, size);
            }

            ImGui.PopStyleVar();
            ImGui.EndGroup();
        }

        private void RenderHeight(int height, CombatSnapshot combat, bool retail, Vector2 size) {
            var name = PowerBarOptions.HeightName(height);
            var min = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton($"##attack-height-{height}", size);

            if (ImGui.IsItemActivated()) {
                // a press still held on another button (a missed release) ends first
                if (heldHeight != 0) {
                    Release();
                }

                if (ClientCombat.PressHeight(height)) {
                    heldHeight = height;
                }
            }

            if (ImGui.IsItemDeactivated() && heldHeight == height) {
                Release();
            }

            Widgets.Tip($"Hold to build an attack aimed {name.ToLowerInvariant()}, and let go to make it, as the game's "
                + $"{name} button does. The one marked is the height your attack keys use.");

            var chosen = combat.Height == height;
            var pressed = ImGui.IsItemActive();
            var hovered = ImGui.IsItemHovered();
            var drawList = ImGui.GetWindowDrawList();
            var max = min + size;
            var art = "attack-height" + (chosen ? "-chosen" : "") + (pressed ? "-pressed" : "");

            if (!retail || !ItemIcons.DrawArtBox(drawList, art, min, max)) {
                var rounding = ImGui.GetStyle().FrameRounding;
                var fill = pressed ? ImGuiCol.ButtonActive : hovered ? ImGuiCol.ButtonHovered : ImGuiCol.Button;
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(fill), rounding);

                if (chosen) {
                    drawList.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.CheckMark), rounding, ImDrawFlags.None, 2);
                }
            }

            var textSize = ImGui.CalcTextSize(name);
            DrawOutlined(drawList, name, new Vector2((float)Math.Round(min.X + (size.X - textSize.X) / 2),
                (float)Math.Round(min.Y + (size.Y - textSize.Y) / 2)));
        }

        private static void DrawOutlined(ImDrawListPtr drawList, string text, Vector2 at) {
            foreach (var offset in Outline) {
                drawList.AddText(at + offset, TextOutline, text);
            }

            drawList.AddText(at, TextColor, text);
        }

        protected override void RenderOwnMenu() {
            var data = Data;
            var changed = false;
            ImGui.Separator();

            changed |= MenuCheck("High, Medium, Low", ref data.ShowHeights);
            changed |= MenuCheck("Buttons in a row", ref data.HeightsInRow);
            changed |= MenuCheck("Text on the bar", ref data.ShowText);

            if (changed) {
                settings.Changed();
            }
        }

        private static bool MenuCheck(string label, ref bool value) {
            if (!ImGui.MenuItem(label, null, value)) {
                return false;
            }

            value = !value;
            return true;
        }

        protected override void RenderOwnSettings() {
            var data = Data;
            var first = true;
            var changed = false;

            changed |= Form.FlowCheck(ref first, "High, Medium, Low##power-bar", ref data.ShowHeights,
                "Shows the attack height buttons: hold one to build an attack at that height, let go to make it.");
            changed |= Form.FlowCheck(ref first, "Buttons in a row##power-bar", ref data.HeightsInRow,
                "Puts the buttons in a row under the bar, rather than stacked beside it as the game has them.");
            changed |= Form.FlowCheck(ref first, "Text on the bar##power-bar", ref data.ShowText,
                "Writes \"Power 73%\" (or Accuracy, in missile combat) on the bar.");

            if (Form.BeginForm("power-bar-form")) {
                Form.Label("Bar width", "How wide the bar is, in pixels at 100% text.");
                var width = data.ClampedBarWidth;

                if (Form.Slider("##power-bar-width", ref width, PowerBarOptions.MinBarWidth, PowerBarOptions.MaxBarWidth,
                    "%d px")) {
                    data.BarWidth = width;
                    changed = true;
                }

                ImGui.EndTable();
            }

            if (changed) {
                settings.Changed();
            }
        }

        public override void Dispose() {
            CoreManager.Current.RenderFrame -= Core_RenderFrame;

            try {
                // an attack held from the window ends with it
                if (heldHeight != 0) {
                    Release();
                }
            }
            finally {
                base.Dispose();
            }
        }
    }
}
