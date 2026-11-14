using System;
using System.Collections.Generic;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Spell bar window (spell-bar): the game's eight tabs of favourite spells, in place of its spell bar in magic
    /// combat. The game's is gmSpellcastingUI (0x10000061, layout 0x21000010) in the Combat floaty (0x100006B5), which
    /// also holds the power bar for melee and missile (PowerBarPanel), so the floaty is hidden only in magic, looked at
    /// every frame, and shown again only in a combat mode; GameElementHider hands it between the two panels. Everything is
    /// the game's own bar (ClientSpellBar): its tabs and their spells in its order, the tab open and the spell selected, so
    /// the game's keys (cast, next and previous spell and tab) work on what the window shows; a click selects (or casts,
    /// with Click casts), a double-click and Cast cast as the game's Cast button does, spells dragged in from the game's
    /// spellbook go in at the slot let go over, and slots move and come out from their right-click menu or by dragging
    /// within the window. Retail draws layout 0x21000010's tabs and Cast button (Resources/ui spell-tab*, cast-*), Modern
    /// ImGui's. Its choices are SpellBarOptions.
    /// </summary>
    internal sealed class SpellBarPanel : GamePanel {
        /// <summary>The game's combat panel, which holds its spell bar.</summary>
        public const uint GameCombat = PowerBarPanel.GameCombat;

        private const string SlotDragType = "DAP_SPELL_BAR";
        private const string MenuId = "spell-bar-slot-menu";

        // the game's sizes: tabs 47x20, the Cast button 32 + 15 + 32 by 32
        private static readonly Vector2 RetailTab = new Vector2(47, 20);
        private static readonly Vector2 RetailCast = new Vector2(79, 32);
        private const float CastCap = 32;

        // packed by hand: ImGui.GetColorU32 crashes before ImGui has a context
        private static readonly uint TextColor = PackedColor.Pack(255, 255, 255);
        private static readonly uint TextOutline = PackedColor.Pack(0, 0, 0, 230);
        private static readonly uint SlotBackground = PackedColor.Pack(0, 0, 0, 110);
        private static readonly uint SelectedFrame = PackedColor.Pack(240, 200, 80);

        private static readonly Vector2[] Outline = {
            new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1),
        };

        private readonly ItemIcons icons;
        private readonly List<int> spells = new List<int>();

        /// <summary>The slot dragged within the window: its tab and spell, or 0.</summary>
        private int draggedTab;
        private int draggedSpell;

        /// <summary>The slot whose right-click menu is open: its tab, spell and place.</summary>
        private int menuTab;
        private int menuSpell;
        private int menuIndex;
        private bool openMenu;

        /// <summary>The row's width while its grip is dragged, in pixels at 100%, before it's a whole number of slots.</summary>
        private float? dragWidth;

        public SpellBarPanel(Settings settings, ItemIcons icons)
            : base("spell-bar", "Spell bar", settings, new[] { GameCombat }, new Vector2(250, 360), inUse: InUse,
                hideWhen: () => ClientCombat.Mode == PowerBarOptions.Magic, checkEvery: TimeSpan.Zero,
                showAgain: (id, wasShown) => wasShown && PowerBarOptions.IsCombatMode(ClientCombat.Mode)) {
            this.icons = icons;

            // a spell bar out of magic is in the way: Only when needed to start with
            if (!Data.WindowSetUp) {
                Data.WindowSetUp = true;
                settings.Window(Key).OnlyInUse = true;
                settings.Changed();
            }
        }

        public override string SettingsTab => GamePanels.CombatBarsTab;

        private SpellBarOptions Data => settings.Data.SpellBar ?? (settings.Data.SpellBar = new SpellBarOptions());

        protected override string ReplaceTip => "Hides the game's spell bar in magic combat and shows the plugin's Spell "
            + "bar window in its place: the same eight tabs of spells, kept in step with the game's, so your cast and "
            + "next-spell keys still work.";

        protected override string WindowTip => "Shows your spell bar's eight tabs in a window of their own, in the game's "
            + "art or the plugin's theme: click a spell to pick it, double-click or Cast to cast it.";

        /// <summary>Magic combat, else nothing to show.</summary>
        private static object InUse() {
            var mode = ClientCombat.Mode;
            return mode == PowerBarOptions.Magic ? (object)mode : null;
        }

        protected override void RenderContents() {
            var tab = ClientSpellBar.OpenTab();

            if (tab < 0) {
                Widgets.TextDisabled(ClientSpellBar.Broken ? "The game's spell bar can't be read (see log.txt)."
                    : "Not in the world yet.");
                return;
            }

            if (ClientCombat.Mode != PowerBarOptions.Magic) {
                Widgets.TextDisabled("Only in magic combat.");
                return;
            }

            var data = Data;
            var retail = Retail;
            RenderTabs(tab, retail);

            if (!ClientSpellBar.TryReadTab(tab, spells, out var selected)) {
                return;
            }

            ImGui.BeginGroup();
            ImGui.BeginGroup();
            RenderSlots(tab, selected, data);

            if (data.ShowName) {
                Widgets.Text(selected != 0 ? Spells.Name(selected) : spells.Count == 0
                    ? "Drag spells here from your spellbook." : "No spell picked.");
            }

            ImGui.EndGroup();

            if (data.ShowCast) {
                ImGui.SameLine();
                RenderCast(tab, selected, retail);
            }

            ImGui.EndGroup();
            ResizeRow(data);
            RenderMenu();

            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && !ImGui.IsMouseReleased(ImGuiMouseButton.Left)) {
                draggedSpell = 0;
            }
        }

        /// <summary>The grip at the right: dragging it fits more or fewer spells to a row (Per row), a slot at a time.</summary>
        private void ResizeRow(SpellBarOptions data) {
            var moved = WidthGrip("Drag to fit more or fewer spells to a row.");

            if (moved == 0) {
                dragWidth = null;
                return;
            }

            // the row's width in pixels at 100%, kept between frames: a slot is the icon and a pixel's gap
            var slot = data.ClampedIconSize + 1;
            dragWidth = (dragWidth ?? data.ClampedPerRow * slot) + moved;
            var perRow = (int)Math.Round(Math.Max(SpellBarOptions.MinPerRow,
                Math.Min(SpellBarOptions.MaxPerRow, dragWidth.Value / slot)));

            if (perRow != data.PerRow) {
                data.PerRow = perRow;
                settings.Changed();
            }
        }

        /// <summary>I to VIII: a click opens that tab in the game's bar too. A slot dragged onto one is put at its end.</summary>
        private void RenderTabs(int open, bool retail) {
            var size = retail ? new Vector2(Widgets.Scaled(RetailTab.X), Widgets.Scaled(RetailTab.Y))
                : new Vector2(Widgets.Scaled(RetailTab.X), ImGui.GetFrameHeight());
            size.Y = Math.Max(size.Y, ImGui.GetTextLineHeight());
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(retail ? 0 : 2, ImGui.GetStyle().ItemSpacing.Y));

            for (var i = 0; i < ClientSpellBar.Tabs; i++) {
                if (i > 0) {
                    ImGui.SameLine();
                }

                var min = ImGui.GetCursorScreenPos();

                if (ImGui.InvisibleButton($"##spell-tab-{i}", size) && i != open) {
                    ClientSpellBar.Open(i);
                }

                Widgets.Tip($"Tab {SpellBarOptions.TabName(i)} of your spell bar. Drag a spell here to put it at the end.");
                DropOnTab(i);

                var isOpen = i == open;
                var drawList = ImGui.GetWindowDrawList();

                if (!retail || !ItemIcons.DrawArtBox(drawList, isOpen ? "spell-tab-open" : "spell-tab", min, min + size)) {
                    var fill = isOpen ? ImGuiCol.TabActive : ImGui.IsItemHovered() ? ImGuiCol.TabHovered : ImGuiCol.Tab;
                    drawList.AddRectFilled(min, min + size, ImGui.GetColorU32(fill), ImGui.GetStyle().TabRounding);
                }

                Centred(drawList, SpellBarOptions.TabName(i), min, size);
            }

            ImGui.PopStyleVar();
        }

        private void DropOnTab(int tab) {
            var spell = DroppedSpell(out var fromTab);

            if (spell != 0 && fromTab != tab) {
                ClientSpellBar.Add(tab, spell, int.MaxValue);
            }
        }

        /// <summary>The open tab's spells, so many to a row, and one more slot to drop on at the end.</summary>
        private void RenderSlots(int tab, int selected, SpellBarOptions data) {
            var size = Widgets.Scaled(data.ClampedIconSize);
            var perRow = data.ClampedPerRow;
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(1, 1));

            for (var i = 0; i <= spells.Count; i++) {
                if (i % perRow != 0) {
                    ImGui.SameLine();
                }

                if (i == spells.Count) {
                    RenderEndSlot(tab, i, size);
                }
                else {
                    RenderSlot(tab, i, spells[i], spells[i] == selected, size, data.ClickCasts);
                }
            }

            // a whole row's width even with few spells, so the window keeps the width its grip was dragged to
            ImGui.Dummy(new Vector2(perRow * (size + 1) - 1, 0));
            ImGui.PopStyleVar();
        }

        private void RenderSlot(int tab, int index, int spell, bool selected, float size, bool clickCasts) {
            var min = ImGui.GetCursorScreenPos();
            var max = min + new Vector2(size, size);
            ImGui.InvisibleButton($"##spell-slot-{index}", new Vector2(size, size));

            if (ImGui.IsItemHovered() && Widgets.DoubleClicked()) {
                ClientSpellBar.Cast(tab, spell);
            }
            else if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                if (clickCasts) {
                    ClientSpellBar.Cast(tab, spell);
                }
                else if (!selected) {
                    ClientSpellBar.Select(tab, spell);
                }
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
                menuTab = tab;
                menuSpell = spell;
                menuIndex = index;
                openMenu = true;
            }

            SpellTip(spell, clickCasts);
            DragSource(tab, spell, size);
            DropTarget(tab, index);

            var drawList = ImGui.GetWindowDrawList();
            drawList.AddRectFilled(min, max, SlotBackground);

            if (!icons.DrawAt(drawList, Spells.Icon(spell), min, size)) {
                Centred(drawList, "?", min, new Vector2(size, size));
            }

            if (selected && (!Retail || !ItemIcons.DrawArtBox(drawList, "selected", min, max))) {
                drawList.AddRect(min, max, SelectedFrame, 0, ImDrawFlags.None, 2);
            }

            if (ImGui.IsItemHovered()) {
                drawList.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.ButtonHovered));
            }
        }

        /// <summary>An empty slot after the last spell: one dropped there goes at the end.</summary>
        private void RenderEndSlot(int tab, int index, float size) {
            var min = ImGui.GetCursorScreenPos();
            ImGui.InvisibleButton("##spell-slot-end", new Vector2(size, size));
            Widgets.Tip("Drag a spell here from your spellbook to put it on this tab.");
            DropTarget(tab, index);

            var drawList = ImGui.GetWindowDrawList();

            if (!Retail || !ItemIcons.DrawArtBox(drawList, "empty-slot", min, min + new Vector2(size, size))) {
                drawList.AddRect(min, min + new Vector2(size, size), ImGui.GetColorU32(ImGuiCol.Border));
            }
        }

        private static void SpellTip(int spell, bool clickCasts) {
            if (!ImGui.IsItemHovered()) {
                return;
            }

            var info = Spells.Info(spell);

            Widgets.BeginTooltip();
            Widgets.Text(info?.Name ?? Spells.Name(spell));

            if (!string.IsNullOrEmpty(info?.School)) {
                Widgets.TextDisabled(info.School);
            }

            if (!string.IsNullOrEmpty(info?.Description)) {
                ImGui.PushTextWrapPos(ImGui.GetFontSize() * 22);
                Widgets.Text(info.Description);
                ImGui.PopTextWrapPos();
            }

            Widgets.TextDisabled(clickCasts ? "Click to cast. Right-click to move or remove it."
                : "Click to pick it for the Cast button and your cast key; double-click to cast. Right-click to move or "
                + "remove it.");
            ImGui.EndTooltip();
        }

        /// <summary>A slot dragged to another place in this tab, or onto another tab.</summary>
        private void DragSource(int tab, int spell, float size) {
            if (!ImGui.BeginDragDropSource(ImGuiDragDropFlags.SourceNoPreviewTooltip)) {
                return;
            }

            draggedTab = tab;
            draggedSpell = spell;
            ImGui.SetDragDropPayload(SlotDragType, IntPtr.Zero, 0);
            icons.DrawAt(ImGui.GetForegroundDrawList(), Spells.Icon(spell), ImGui.GetMousePos() - new Vector2(size / 2, size / 2),
                size);
            ImGui.EndDragDropSource();
        }

        /// <summary>A slot as a place to let go: a spell from the game's spellbook or from this window goes in here.</summary>
        private void DropTarget(int tab, int index) {
            var spell = DroppedSpell(out var fromTab);

            if (spell == 0) {
                return;
            }

            PutAt(tab, spell, index);
        }

        /// <summary>
        /// A spell put at a place in the open tab: one already in it is moved there (taken out first, so a later place is one
        /// less), any other added.
        /// </summary>
        private void PutAt(int tab, int spell, int index) {
            var at = spells.IndexOf(spell);

            if (at < 0) {
                ClientSpellBar.Add(tab, spell, index);
            }
            else if (at != index) {
                ClientSpellBar.Move(tab, spell, at < index ? index - 1 : index);
            }
        }

        /// <summary>
        /// A spell let go over the item drawn just before this: one dragged within the window (and the tab it came from), or
        /// one from the game (SpellDrag, the tab -1). 0 for none.
        /// </summary>
        private unsafe int DroppedSpell(out int fromTab) {
            fromTab = -1;

            if (ImGui.BeginDragDropTarget()) {
                var payload = ImGui.AcceptDragDropPayload(SlotDragType);
                var delivered = payload.NativePtr != null && draggedSpell != 0;

                // a spell from the plugin's Spellbook window
                var fromBook = ImGui.AcceptDragDropPayload(SpellPayload.Type);
                var bookSpell = fromBook.NativePtr != null ? SpellPayload.Spell : 0;
                ImGui.EndDragDropTarget();

                if (bookSpell != 0) {
                    PluginCore.LogFile($"[spell-bar] spell {bookSpell} {Spells.Name(bookSpell)} dragged from the Spellbook");
                    return bookSpell;
                }

                if (delivered) {
                    fromTab = draggedTab;
                    var spell = draggedSpell;
                    draggedSpell = 0;
                    PluginCore.LogFile($"[spell-bar] slot dragged: spell {spell} {Spells.Name(spell)} from tab {fromTab}");
                    return spell;
                }
            }

            // a spell from the game's spellbook, its release kept from the game (which ImGui may not have heard either)
            var caught = GameDrag.CaughtOverLastItem();

            if (caught) {
                GameDrag.TakeCaught();
            }

            var fromGame = SpellDrag.Spell;

            if (fromGame == 0 || (!caught && !GameDrag.ReleasedOverLastItem())) {
                if (fromGame != 0 && GameDrag.OverLastItem()) {
                    var drawList = ImGui.GetWindowDrawList();
                    ItemIcons.DrawArtBox(drawList, "drop-ring", ImGui.GetItemRectMin(), ImGui.GetItemRectMax());
                }

                return 0;
            }

            PluginCore.LogFile($"[spell-bar] spell {fromGame} {Spells.Name(fromGame)} dragged in from the game");
            return fromGame;
        }

        private void RenderMenu() {
            if (openMenu) {
                openMenu = false;
                ImGui.OpenPopup(MenuId);
            }

            if (!ImGui.BeginPopup(MenuId)) {
                return;
            }

            Widgets.TextDisabled(Spells.Name(menuSpell));
            ImGui.Separator();

            if (ImGui.MenuItem("Cast")) {
                ClientSpellBar.Cast(menuTab, menuSpell);
            }

            if (ImGui.MenuItem("Move left", null, false, menuIndex > 0)) {
                ClientSpellBar.Move(menuTab, menuSpell, menuIndex - 1);
            }

            if (ImGui.MenuItem("Move right", null, false, menuIndex < spells.Count - 1)) {
                ClientSpellBar.Move(menuTab, menuSpell, menuIndex + 1);
            }

            if (ImGui.BeginMenu("Copy to tab")) {
                for (var i = 0; i < ClientSpellBar.Tabs; i++) {
                    if (i != menuTab && ImGui.MenuItem(SpellBarOptions.TabName(i))) {
                        ClientSpellBar.Add(i, menuSpell, int.MaxValue);
                    }
                }

                ImGui.EndMenu();
            }

            if (ImGui.MenuItem("Remove from this tab")) {
                ClientSpellBar.Remove(menuTab, menuSpell);
            }

            ImGui.EndPopup();
        }

        /// <summary>The Cast button: casts the picked spell, as the game's does. Greyed out with none picked.</summary>
        private void RenderCast(int tab, int selected, bool retail) {
            var size = retail ? new Vector2(Widgets.Scaled(RetailCast.X), Widgets.Scaled(RetailCast.Y))
                : new Vector2(Widgets.Scaled(RetailCast.X), ImGui.GetFrameHeight());
            var min = ImGui.GetCursorScreenPos();
            var enabled = selected != 0;

            ImGui.BeginDisabled(!enabled);

            if (ImGui.InvisibleButton("##spell-cast", size)) {
                ClientSpellBar.Cast(tab, selected);
            }

            ImGui.EndDisabled();
            Widgets.Tip(enabled ? $"Casts {Spells.Name(selected)}, as the game's Cast button and your cast key do."
                : "Pick a spell first.");

            var drawList = ImGui.GetWindowDrawList();
            var pressed = ImGui.IsItemActive();

            if (!retail || !DrawRetailCast(drawList, min, size, !enabled ? "-disabled" : pressed ? "-pressed" : "")) {
                var fill = !enabled ? ImGuiCol.FrameBg : pressed ? ImGuiCol.ButtonActive
                    : ImGui.IsItemHovered() ? ImGuiCol.ButtonHovered : ImGuiCol.Button;
                drawList.AddRectFilled(min, min + size, ImGui.GetColorU32(fill), ImGui.GetStyle().FrameRounding);
            }

            Centred(drawList, "Cast", min, size);
        }

        /// <summary>The game's Cast button: its two ends at their own shape, the middle stretched between them.</summary>
        private static bool DrawRetailCast(ImDrawListPtr drawList, Vector2 min, Vector2 size, string state) {
            var cap = Math.Min((float)Math.Round(CastCap * size.Y / RetailCast.Y), size.X / 2);
            var max = min + size;

            return ItemIcons.DrawArtBox(drawList, "cast-left" + state, min, new Vector2(min.X + cap, max.Y))
                && ItemIcons.DrawArtBox(drawList, "cast-middle" + state, new Vector2(min.X + cap, min.Y),
                    new Vector2(max.X - cap, max.Y))
                && ItemIcons.DrawArtBox(drawList, "cast-right" + state, new Vector2(max.X - cap, min.Y), max);
        }

        private static void Centred(ImDrawListPtr drawList, string text, Vector2 min, Vector2 size) {
            var textSize = ImGui.CalcTextSize(text);
            var at = new Vector2((float)Math.Round(min.X + (size.X - textSize.X) / 2),
                (float)Math.Round(min.Y + (size.Y - textSize.Y) / 2));

            foreach (var offset in Outline) {
                drawList.AddText(at + offset, TextOutline, text);
            }

            drawList.AddText(at, TextColor, text);
        }

        protected override void RenderOwnMenu() {
            var data = Data;
            var changed = false;
            ImGui.Separator();

            changed |= MenuCheck("Click casts", ref data.ClickCasts);
            changed |= MenuCheck("Spell name", ref data.ShowName);
            changed |= MenuCheck("Cast button", ref data.ShowCast);

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

            changed |= Form.FlowCheck(ref first, "Click casts##spell-bar", ref data.ClickCasts, "A click on a spell casts "
                + "it. Off, a click picks it for the Cast button and your cast key, as in the game, and a double-click casts.");
            changed |= Form.FlowCheck(ref first, "Spell name##spell-bar", ref data.ShowName,
                "Writes the picked spell's name under the icons.");
            changed |= Form.FlowCheck(ref first, "Cast button##spell-bar", ref data.ShowCast,
                "Shows the Cast button beside the icons.");

            if (Form.BeginForm("spell-bar-form")) {
                Form.Label("Per row", "How many spells to a row before the next.");
                var perRow = data.ClampedPerRow;

                if (Form.Slider("##spell-bar-per-row", ref perRow, SpellBarOptions.MinPerRow, SpellBarOptions.MaxPerRow,
                    "%d")) {
                    data.PerRow = perRow;
                    changed = true;
                }

                Form.Label("Icon size", "Each spell's icon, in pixels at 100% text. The game's are 32.");
                var iconSize = data.ClampedIconSize;

                if (Form.Slider("##spell-bar-icon-size", ref iconSize, SpellBarOptions.MinIconSize,
                    SpellBarOptions.MaxIconSize, "%d px")) {
                    data.IconSize = iconSize;
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
