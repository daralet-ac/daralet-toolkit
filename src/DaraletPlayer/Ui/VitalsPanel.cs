using System;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Vitals window (vitals, hidden to start): your health, stamina and mana, in place of the game's vitals panel
    /// (element 0x100005FA, Decal's UIElementType Vitals, layout 0x2100006C, or 0x100006D5, the bars in a row, layout
    /// 0x21000075; the meters 0x100000E6, 0x100000EC and 0x100000EE in both). The numbers are Decal's Actions.Vital,
    /// which reads them from the client as its own panel does, so they include buffs; it reads the game's memory, so not before login is over (FloatingWindow stays hidden till then).
    /// Retail draws the meters' own art (ItemIcons.DrawVitalBar), Modern ImGui's bar in the vital's colour. Its choices are
    /// VitalsOptions.
    /// </summary>
    internal sealed class VitalsPanel : GamePanel {
        /// <summary>The game's vitals panel, as Decal's UIElementType names it.</summary>
        public const uint GameVitals = 0x100005FA;

        /// <summary>
        /// The game's other vitals panel, the three bars in a row (460x26, layout 0x21000075, the main UI's 0x100006D5),
        /// the one showing when the game's vitals are laid out side by side, which the first build left alone.
        /// </summary>
        public const uint GameVitalsInARow = 0x100006D5;

        /// <summary>The game's meters are 16 pixels high.</summary>
        private const float RetailBarHeight = 16;

        private static readonly (string Value, string Label)[] TextChoices = {
            (VitalsOptions.TextNumbers, "Numbers"),
            (VitalsOptions.TextPercent, "Percent"),
            (VitalsOptions.TextBoth, "Both"),
            (VitalsOptions.TextNone, "None"),
        };

        /// <summary>Bar width while the grip's dragged, unrounded.</summary>
        private float? dragWidth;

        public VitalsPanel(Settings settings)
            : base("vitals", "Vitals", settings, new[] { GameVitals, GameVitalsInARow }, new Vector2(20, 60)) {
        }

        public override string SettingsTab => GamePanels.VitalsTab;

        private VitalsOptions Data => settings.Data.Vitals ?? (settings.Data.Vitals = new VitalsOptions());

        protected override string ReplaceTip => "Hides the game's vitals panel (health, stamina and mana) and shows the "
            + "plugin's Vitals window in its place.";

        protected override string WindowTip => "Shows your health, stamina and mana in a window of their own, in the "
            + "game's art or the plugin's theme.";

        protected override void RenderContents() {
            // Decal's hooks read the game's memory, which isn't there until login is over
            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                Widgets.TextDisabled("Not in the world yet.");
                return;
            }

            var data = Data;
            var vitals = new (bool Shown, Vector4 Color, VitalType Current, VitalType Max)[] {
                (data.ShowHealth, Colors.Health, VitalType.CurrentHealth, VitalType.MaximumHealth),
                (data.ShowStamina, Colors.Stamina, VitalType.CurrentStamina, VitalType.MaximumStamina),
                (data.ShowMana, Colors.Mana, VitalType.CurrentMana, VitalType.MaximumMana),
            };

            var actions = CoreManager.Current.Actions;
            var retail = Retail;
            var width = Widgets.Scaled(data.ClampedBarWidth);
            var height = retail ? Widgets.Scaled(RetailBarHeight) : 0;
            var any = false;
            var shown = 0;
            ImGui.BeginGroup();

            foreach (var vital in vitals) {
                if (!vital.Shown) {
                    continue;
                }

                var current = (uint)Math.Max(0, actions.Vital[vital.Current]);
                var max = (uint)Math.Max(0, actions.Vital[vital.Max]);

                if (any && data.SideBySide) {
                    ImGui.SameLine();
                }

                var fraction = max == 0 ? 0f : Math.Min(1f, (float)current / max);
                Widgets.Bar(fraction, vital.Color, VitalsOptions.Format(current, max, data.Text), width, height, retail);
                any = true;
                shown++;
            }

            if (!any) {
                Widgets.TextDisabled("No vitals ticked: right-click to pick some.");
            }

            ImGui.EndGroup();

            if (any) {
                ResizeBars(data, data.SideBySide ? shown : 1);
            }
        }

        /// <summary>
        /// The grip at the right: dragging it makes the bars wider or narrower (Bar width). Side by side, the drag is
        /// shared among the bars in the row, so the grip stays under the mouse.
        /// </summary>
        private void ResizeBars(VitalsOptions data, int inRow) {
            var moved = WidthGrip("Drag to make the bars wider or narrower.");

            if (moved == 0) {
                dragWidth = null;
                return;
            }

            // kept as a fraction between frames, or slow drags would round away to nothing
            dragWidth = (dragWidth ?? data.ClampedBarWidth) + moved / Math.Max(1, inRow);
            var barWidth = (int)Math.Round(Math.Max(VitalsOptions.MinBarWidth,
                Math.Min(VitalsOptions.MaxBarWidth, dragWidth.Value)));

            if (barWidth != data.BarWidth) {
                data.BarWidth = barWidth;
                settings.Changed();
            }
        }

        protected override void RenderOwnMenu() {
            var data = Data;
            var changed = false;
            ImGui.Separator();

            changed |= MenuCheck("Health", ref data.ShowHealth);
            changed |= MenuCheck("Stamina", ref data.ShowStamina);
            changed |= MenuCheck("Mana", ref data.ShowMana);
            changed |= MenuCheck("Side by side", ref data.SideBySide);

            if (ImGui.BeginMenu("Text")) {
                foreach (var choice in TextChoices) {
                    if (ImGui.MenuItem(choice.Label, null, data.Text == choice.Value)) {
                        data.Text = choice.Value;
                        changed = true;
                    }
                }

                ImGui.EndMenu();
            }

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

            changed |= Form.FlowCheck(ref first, "Health##vitals", ref data.ShowHealth, "Shows your health bar.");
            changed |= Form.FlowCheck(ref first, "Stamina##vitals", ref data.ShowStamina, "Shows your stamina bar.");
            changed |= Form.FlowCheck(ref first, "Mana##vitals", ref data.ShowMana, "Shows your mana bar.");
            changed |= Form.FlowCheck(ref first, "Side by side##vitals", ref data.SideBySide,
                "Puts the bars in a row, rather than one under another as the game has them.");
            changed |= Form.FlowCheck(ref first, "Bold numbers##vitals", ref settings.Data.BoldVitalText,
                Widgets.BoldVitalsTip);

            if (Form.BeginForm("vitals-form")) {
                Form.Label("Bar width", "How wide each bar is, in pixels at 100% text. The game's are 150.");
                var width = data.ClampedBarWidth;

                if (Form.Slider("##vitals-bar-width", ref width, VitalsOptions.MinBarWidth, VitalsOptions.MaxBarWidth,
                    "%d px")) {
                    data.BarWidth = width;
                    changed = true;
                }

                Form.Label("Text", "What's written on each bar: current / max, a percent, both, or nothing.");
                var text = data.Text;

                if (Form.Segmented("vitals-text", TextChoices, ref text)) {
                    data.Text = text;
                    changed = true;
                }

                ImGui.EndTable();
            }

            if (changed) {
                settings.Changed();
            }
        }
    }
}
