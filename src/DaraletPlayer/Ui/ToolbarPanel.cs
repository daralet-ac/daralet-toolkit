using System;
using System.Collections.Generic;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Toolbar window (toolbar): the game toolbar's buttons in a window of their own, in place of the game's toolbar
    /// (TBAR, element 0x10000603, its frame from layout 0x21000070 round layout 0x21000016). The combat button shows the
    /// combat mode and toggles it; Social, Spells, Character, Quests, Map, Options and the backpack open the game's panel
    /// (ClientElements.SetGamePanel, by the panel id each of the game's buttons carries), or the plugin's window where
    /// Client UI Override's toolbar buttons say so (Inventory, Quests, Social, Map), lit while it's open; and the strip
    /// under them is the selection's: its name, its health, a stack's split amount, Use (the hand) and Appraise (the
    /// magnifier), what the game's own do (ClientToolbar). The game's shortcut bar goes with its toolbar: the plugin's
    /// hotbars are for that. Retail lays it out as the game does, in its art; Modern is a row of themed buttons.
    /// </summary>
    internal sealed class ToolbarPanel : GamePanel {
        /// <summary>The game's toolbar, as Decal's UIElementType TBar names it.</summary>
        public const uint GameToolbar = 0x10000603;

        // the game's layout 0x21000016, in its pixels
        private static readonly Vector2 Whole = new Vector2(301, 58);
        private static readonly Vector2 CombatSize = new Vector2(55, 58);
        private static readonly Vector2 ButtonSize = new Vector2(34, 27);
        private static readonly Vector2 BackpackSize = new Vector2(63, 58);
        private static readonly Vector2 UseAt = new Vector2(55, 27);
        private static readonly Vector2 UseSize = new Vector2(23, 31);
        private static readonly Vector2 StripAt = new Vector2(78, 27);
        private static readonly Vector2 StripSize = new Vector2(140, 31);
        private static readonly Vector2 AppraiseAt = new Vector2(218, 27);
        private static readonly Vector2 AppraiseSize = new Vector2(22, 31);

        // packed by hand: ImGui.GetColorU32 crashes before ImGui has a context
        private static readonly uint TextColor = PackedColor.Pack(255, 255, 255);
        private static readonly uint TextOutline = PackedColor.Pack(0, 0, 0, 230);

        private static readonly Vector2[] Outline = {
            new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1),
        };

        private readonly PlayerFeed feed;

        /// <summary>The plugin's window a button opens instead of the game's panel, while its setting says so, by key.</summary>
        private readonly Dictionary<string, (Func<bool> Instead, FloatingWindow Window)> windows;

        public ToolbarPanel(Settings settings, PlayerFeed feed,
            Dictionary<string, (Func<bool> Instead, FloatingWindow Window)> windows)
            : base("toolbar", "Toolbar", settings, new[] { GameToolbar }, new Vector2(490, 500)) {
            this.feed = feed;
            this.windows = windows;
        }

        private ToolbarOptions Data => settings.Data.Toolbar ?? (settings.Data.Toolbar = new ToolbarOptions());

        protected override string ReplaceTip => "Hides the game's toolbar (its buttons, the selection strip and its "
            + "shortcut bar) and shows the plugin's Toolbar window in its place. The plugin's hotbars stand in for the "
            + "shortcut bar.";

        protected override string WindowTip => "Shows the toolbar's buttons in a window of their own: combat mode, Social, "
            + "Spells, Character, Quests, Map, Options and your pack, and the selected object's strip.";

        protected override void RenderContents() {
            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                Widgets.TextDisabled("Not in the world yet.");
                return;
            }

            var openPanel = ClientElements.OpenGamePanel();

            if (Retail) {
                RenderRetail(openPanel);
            }
            else {
                RenderModern(openPanel);
            }
        }

        // ---- what the buttons do ----

        private bool IsOpen(ToolbarButtonInfo button, uint openPanel) {
            return windows.TryGetValue(button.Key, out var instead) && instead.Instead()
                ? instead.Window.Visible
                : openPanel == button.Panel;
        }

        private void Press(ToolbarButtonInfo button, uint openPanel) {
            if (windows.TryGetValue(button.Key, out var instead) && instead.Instead()) {
                instead.Window.Visible = !instead.Window.Visible;

                if (instead.Window.Visible) {
                    instead.Window.BringToFront();
                }

                return;
            }

            var open = openPanel == button.Panel;
            ClientElements.SetGamePanel(button.Panel, !open);
            PluginCore.LogFile($"[toolbar] {button.Label}: the game's panel 0x{button.Panel:X} {(open ? "closed" : "opened")}");
        }

        private string ButtonTip(ToolbarButtonInfo button) {
            var instead = windows.TryGetValue(button.Key, out var window) && window.Instead();
            return $"{button.Label}: opens or closes {(instead ? "Daralet Toolkit's " + button.Label + " window" : "the game's "
                + button.Label.ToLowerInvariant() + " panel")}, as the game's toolbar button does."
                + (button.Panel == ToolbarOptions.InventoryPanel ? " Items dropped here go in your main pack." : "");
        }

        private static string CombatTip(int mode) {
            return $"Combat mode: {ToolbarOptions.CombatName(mode)}. Click to go into combat (the mode your wielded weapon "
                + "suits) or back to peace, as the game's combat button and key do.";
        }

        /// <summary>Items let go over the backpack go in your main pack, from the plugin's lists or the game's own pack.</summary>
        private unsafe void BackpackDrop() {
            if (ImGui.BeginDragDropTarget()) {
                var payload = ImGui.AcceptDragDropPayload(InventoryBrowser.DragType);

                if (payload.NativePtr != null && payload.IsDelivery()) {
                    ToolbarButtons.PutInMainPack(InventoryBrowser.DraggedItem);
                }

                ImGui.EndDragDropTarget();
            }

            var caught = GameDrag.CaughtOverLastItem();

            if (caught) {
                GameDrag.TakeCaught();
            }

            if ((caught || GameDrag.ReleasedOverLastItem()) && SpellDrag.Spell == 0) {
                var id = CoreManager.Current.Actions.CurrentSelection;
                PluginCore.LogFile($"[toolbar] 0x{id:X8} let go over the backpack: into the main pack");
                ToolbarButtons.PutInMainPack(id);
            }
        }

        private void Use(int selection) {
            ClientToolbar.Use(selection);
        }

        /// <summary>
        /// The magnifier, in the plugin's appraisal window for what's selected: an item in the Appraisal window while
        /// Plugin appraisal is on; a creature, NPC or player in Creature Appraisal while it Replaces the game's
        /// (ClientExamine.TakeOver, which the game's own appraisals go through, but which a call straight to the game's
        /// notice, as this makes, skips: the first build's opened the game's panel for them); anything else in the
        /// game's own appraisal panel.
        /// </summary>
        private void Appraise(WorldObject selected, int selection) {
            if (settings.Data.AppraiseInPlugin && selected != null && IsItem(selected)) {
                Appraisal.Show((uint)selection);
            }
            else if (!ClientExamine.TryTakeOver(selection)) {
                ClientToolbar.Examine(selection);
            }
        }

        private static bool IsItem(WorldObject item) {
            switch (item.ObjectClass) {
                case ObjectClass.Monster:
                case ObjectClass.Player:
                case ObjectClass.Npc:
                case ObjectClass.Vendor:
                case ObjectClass.Door:
                case ObjectClass.Portal:
                case ObjectClass.Lifestone:
                case ObjectClass.Corpse:
                case ObjectClass.Housing:
                case ObjectClass.Unknown:
                    return false;
                default:
                    return true;
            }
        }

        // ---- the game's layout ----

        private void RenderRetail(uint openPanel) {
            var scale = Widgets.Scaled(100) / 100;
            var origin = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var mode = ClientCombat.Mode;

            // the combat button
            Place(origin, Vector2.Zero, CombatSize, scale, "##toolbar-combat", out var min, out var max);
            var pressed = ImGui.IsItemActive();

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                ClientToolbar.ToggleCombatMode();
            }

            Widgets.Tip(CombatTip(mode));
            Art(drawList, ToolbarOptions.CombatArt(mode) + (pressed ? "-pressed" : ""), min, max,
                ToolbarOptions.CombatName(mode));

            // the panel buttons, the backpack last and bigger
            foreach (var button in ToolbarOptions.Buttons) {
                var backpack = button.Panel == ToolbarOptions.InventoryPanel;
                Place(origin, new Vector2(button.X, 0), backpack ? BackpackSize : ButtonSize, scale,
                    $"##toolbar-{button.Key}", out min, out max);

                if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                    Press(button, openPanel);
                }

                Widgets.Tip(ButtonTip(button));

                if (backpack) {
                    BackpackDrop();
                }

                var open = IsOpen(button, openPanel);
                Art(drawList, open ? button.OpenArt : button.Art, min, max, button.Label);
            }

            if (Data.ShowSelection) {
                RenderRetailStrip(origin, scale, drawList);
            }

            // back to the top, then past the whole toolbar: a Dummy after, or ImGui asserts on the cursor put back
            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(Whole * scale);
        }

        /// <summary>The selection strip: Use, the name with its health or a stack's amount, Appraise.</summary>
        private void RenderRetailStrip(Vector2 origin, float scale, ImDrawListPtr drawList) {
            var selection = CoreManager.Current.Actions.CurrentSelection;
            var selected = selection == 0 ? null : CoreManager.Current.WorldFilter[selection];

            ImGui.BeginDisabled(selection == 0);
            Place(origin, UseAt, UseSize, scale, "##toolbar-use", out var min, out var max);
            var pressed = ImGui.IsItemActive();

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                Use(selection);
            }

            ImGui.EndDisabled();
            Widgets.Tip(selection == 0 ? "Use: select something first." : $"Use {NameOf(selection, selected) ?? "it"}, as the "
                + "game's hand does.");
            Art(drawList, "toolbar-use" + (selection == 0 ? "-disabled" : pressed ? "-pressed" : ""), min, max, "Use");

            ImGui.BeginDisabled(selection == 0);
            Place(origin, AppraiseAt, AppraiseSize, scale, "##toolbar-appraise", out min, out max);
            pressed = ImGui.IsItemActive();

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                Appraise(selected, selection);
            }

            ImGui.EndDisabled();
            Widgets.Tip(selection == 0 ? "Appraise: select something first." : $"Appraise "
                + $"{NameOf(selection, selected) ?? "it"}, as the game's magnifier does.");
            Art(drawList, "toolbar-appraise" + (pressed ? "-pressed" : ""), min, max, "?");

            // the strip itself: its background, the name on top, and under it the health or the stack's amount
            var stripMin = origin + StripAt * scale;
            var stripMax = stripMin + StripSize * scale;
            ItemIcons.DrawArtBox(drawList, "toolbar-strip", stripMin, stripMax);
            ImGui.SetCursorScreenPos(stripMin);
            RenderStrip(selection, selected, StripSize.X * scale, StripSize.Y * scale, true);
        }

        /// <summary>
        /// Puts an invisible button at a place in the game's layout, scaled. Its corners come back for its picture.
        /// </summary>
        private static void Place(Vector2 origin, Vector2 at, Vector2 size, float scale, string id, out Vector2 min,
            out Vector2 max) {
            min = origin + at * scale;
            max = min + size * scale;
            ImGui.SetCursorScreenPos(min);
            ImGui.InvisibleButton(id, size * scale);
        }

        /// <summary>The game's picture for a button, or its name on a themed button where there's none.</summary>
        private static void Art(ImDrawListPtr drawList, string art, Vector2 min, Vector2 max, string fallback) {
            if (ItemIcons.DrawArtBox(drawList, art, min, max)) {
                return;
            }

            drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.Button), ImGui.GetStyle().FrameRounding);
            Centred(drawList, fallback, min, max - min);
        }

        // ---- the strip, both looks ----

        /// <summary>
        /// The selection's name, then its health (a creature or player whose health the client's been told) or, for a stack
        /// you carry, a slider for how many the next drag takes (the game's stack bar).
        /// </summary>
        /// <summary>
        /// The selection's name: Decal's, else the open vendor's item's (Decal's WorldFilter hasn't got a vendor's items, so a
        /// vendor item picked in the Vendor window showed as its hex id); null when neither knows it.
        /// </summary>
        private static string NameOf(int selection, WorldObject selected) {
            return selected?.Name ?? VendorStock.Find(selection)?.Name;
        }

        private void RenderStrip(int selection, WorldObject selected, float width, float height, bool retail) {
            var drawList = ImGui.GetWindowDrawList();
            var min = ImGui.GetCursorScreenPos();
            var half = (float)Math.Round(height / 2);
            var name = selection == 0 ? "Nothing selected" : NameOf(selection, selected) ?? $"0x{selection:X8}";

            // the name, cut to fit
            drawList.PushClipRect(min, min + new Vector2(width, half), true);
            var nameSize = ImGui.CalcTextSize(name);
            DrawOutlined(drawList, name, new Vector2((float)Math.Round(min.X + Math.Max(2, (width - nameSize.X) / 2)),
                (float)Math.Round(min.Y + (half - nameSize.Y) / 2)));
            drawList.PopClipRect();

            var lower = new Vector2(min.X, min.Y + half);
            var lowerSize = new Vector2(width, height - half);
            var health = selection == 0 ? null : feed.ClientHealth((uint)selection);

            if (health.HasValue) {
                var fraction = PowerBarOptions.Clamp(health.Value);
                var barMax = lower + lowerSize;

                if (!retail || !DrawRetailMeter(drawList, lower, barMax, fraction)) {
                    drawList.AddRectFilled(lower, barMax, ImGui.GetColorU32(ImGuiCol.FrameBg));
                    drawList.AddRectFilled(lower, new Vector2(lower.X + lowerSize.X * fraction, barMax.Y),
                        ImGui.GetColorU32(Colors.Health));
                }

                var text = $"{(int)Math.Round(fraction * 100)}%";
                var textSize = ImGui.CalcTextSize(text);
                DrawOutlined(drawList, text, lower + (lowerSize - textSize) / 2);
            }
            else if (selection != 0 && StackSplit.CanSplit(selection) && HotbarItems.IsMine(selection)) {
                RenderStackSlider(selection, lower, lowerSize);
            }

            ImGui.SetCursorScreenPos(min);
            ImGui.Dummy(new Vector2(width, height));
        }

        /// <summary>The game's health meter (0x100001A1): the dark bar, the red one cut off at the health.</summary>
        private static bool DrawRetailMeter(ImDrawListPtr drawList, Vector2 min, Vector2 max, float fraction) {
            if (!ItemIcons.DrawArtBox(drawList, "selection-health-empty", min, max)) {
                return false;
            }

            var cut = (float)Math.Round(min.X + (max.X - min.X) * fraction);

            if (cut > min.X) {
                drawList.PushClipRect(min, new Vector2(cut, max.Y), true);
                ItemIcons.DrawArtBox(drawList, "selection-health-full", min, max);
                drawList.PopClipRect();
            }

            return true;
        }

        /// <summary>How many of the selected stack the next drag carries: the game's stack bar (StackSplit.SetAmount).</summary>
        private static void RenderStackSlider(int selection, Vector2 at, Vector2 size) {
            var stack = StackSplit.StackOf(selection);
            var amount = CoreManager.Current.Actions.SelectedStackCount;
            amount = amount <= 0 || amount > stack ? stack : amount;

            ImGui.SetCursorScreenPos(at);
            ImGui.SetNextItemWidth(size.X);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, Math.Max(0, (size.Y - ImGui.GetFontSize()) / 2)));

            if (Sliders.SliderInt("##toolbar-stack", ref amount, 1, stack, $"{amount} of {stack}")) {
                StackSplit.SetAmount(selection, amount);
            }

            ImGui.PopStyleVar();
            Widgets.Tip("How many of the stack your next drag of it carries, as the game's stack bar sets it. Ctrl+click "
                + "to type a number.");
        }

        // ---- the plugin's look ----

        private void RenderModern(uint openPanel) {
            var mode = ClientCombat.Mode;

            if (Form.LitButton($"{ToolbarOptions.CombatName(mode)}##toolbar-combat", mode != PowerBarOptions.NonCombat
                && mode != 0)) {
                ClientToolbar.ToggleCombatMode();
            }

            Widgets.Tip(CombatTip(mode));

            foreach (var button in ToolbarOptions.Buttons) {
                ImGui.SameLine();

                if (Form.LitButton($"{button.Label}##toolbar-{button.Key}", IsOpen(button, openPanel))) {
                    Press(button, openPanel);
                }

                Widgets.Tip(ButtonTip(button));

                if (button.Panel == ToolbarOptions.InventoryPanel) {
                    BackpackDrop();
                }
            }

            if (!Data.ShowSelection) {
                return;
            }

            var selection = CoreManager.Current.Actions.CurrentSelection;
            var selected = selection == 0 ? null : CoreManager.Current.WorldFilter[selection];
            var style = ImGui.GetStyle();

            ImGui.BeginDisabled(selection == 0);

            if (ImGui.Button("Use##toolbar-use")) {
                Use(selection);
            }

            ImGui.EndDisabled();
            Widgets.Tip(selection == 0 ? "Use: select something first." : $"Use {NameOf(selection, selected) ?? "it"}, as the "
                + "game's hand does.");

            ImGui.SameLine();
            var appraiseWidth = ImGui.CalcTextSize("Appraise").X + style.FramePadding.X * 2;
            var stripWidth = Math.Max(Widgets.Scaled(120), ImGui.GetContentRegionAvail().X - appraiseWidth - style.ItemSpacing.X);
            RenderStrip(selection, selected, stripWidth, ImGui.GetFrameHeight() * 2, false);

            ImGui.SameLine();
            ImGui.BeginDisabled(selection == 0);

            if (ImGui.Button("Appraise##toolbar-appraise")) {
                Appraise(selected, selection);
            }

            ImGui.EndDisabled();
            Widgets.Tip(selection == 0 ? "Appraise: select something first." : $"Appraise "
                + $"{NameOf(selection, selected) ?? "it"}, as the game's magnifier does.");
        }

        private static void Centred(ImDrawListPtr drawList, string text, Vector2 min, Vector2 size) {
            var textSize = ImGui.CalcTextSize(text);
            DrawOutlined(drawList, text, new Vector2((float)Math.Round(min.X + (size.X - textSize.X) / 2),
                (float)Math.Round(min.Y + (size.Y - textSize.Y) / 2)));
        }

        private static void DrawOutlined(ImDrawListPtr drawList, string text, Vector2 at) {
            at = new Vector2((float)Math.Round(at.X), (float)Math.Round(at.Y));

            foreach (var offset in Outline) {
                drawList.AddText(at + offset, TextOutline, text);
            }

            drawList.AddText(at, TextColor, text);
        }

        protected override void RenderOwnMenu() {
            var data = Data;
            ImGui.Separator();

            if (ImGui.MenuItem("Selection strip", null, data.ShowSelection)) {
                data.ShowSelection = !data.ShowSelection;
                settings.Changed();
            }
        }

        protected override void RenderOwnSettings() {
            var data = Data;
            var first = true;

            if (Form.FlowCheck(ref first, "Selection strip##toolbar", ref data.ShowSelection, "Shows the selected object's "
                + "strip: its name, its health or a stack's amount, Use and Appraise.")) {
                settings.Changed();
            }

            Form.Hint("Inventory, Quests, Social and Map open the plugin's windows where Client UI Override's checkboxes "
                + "of those names are ticked, and the game's panels where they aren't.");
        }
    }
}
