using System;
using System.Collections.Generic;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The window: the game's warning, its row of slots (wrapping to the width, empty ones after what's on, as the game's
    /// shows them), Salvage and Clear on the right, the game's close button at the top right, and under them what it'll make (SalvagePanel.Summary.cs). Anything held
    /// over a slot gets the green ring where it can go on and the red circle where it can't, from the plugin's lists
    /// (an item, a pack, the main pack) or the game's own pack (GameDrag, the game's selection); ImGui hovers nothing
    /// during a drag from the game, so that's the mouse against the slot's square.
    /// </summary>
    internal sealed partial class SalvagePanel {
        private const float SlotSize = 32;
        private const float ButtonWidth = 80;

        /// <summary>The selection's frame at 40% over the slot under the mouse, as the Vendor window's.</summary>
        private const uint HoverTint = 0x66FFFFFF;

        private void RenderWindow() {
            boxMin = ImGui.GetWindowPos();
            boxMax = boxMin + ImGui.GetWindowSize();
            boxFrame = ImGui.GetFrameCount();

            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                Widgets.TextDisabled("Not logged in.");
                return;
            }

            var supported = feed.Supports(PlayerFeed.KindSalvage) && feed.Supports(PlayerFeed.KindDescribe);

            Widgets.TextColored(Colors.Warning, "WARNING: Items in this panel will be destroyed!");
            RenderCloseIcon();

            if (!supported) {
                Widgets.TextColored(Colors.Danger, "This server is too old for salvaging from here.");
            }

            RenderAddButtons(supported);

            var spacing = ImGui.GetStyle().ItemSpacing.X;
            var buttons = Widgets.Scaled(ButtonWidth);
            var scrollbar = ImGui.GetStyle().ScrollbarSize;
            var slotsWidth = Math.Max(Widgets.Scaled(SlotSize),
                ImGui.GetContentRegionAvail().X - buttons - spacing - scrollbar);
            var size = Widgets.Scaled(SlotSize);
            var perRow = Math.Max(1, (int)(slotsWidth / size));
            var rows = Math.Max(1, (queued.Count + perRow) / perRow);
            // four rows at most, the rest scrolled to; room kept for the scroll bar either way, so the slots never move
            var height = Math.Min(rows, 4) * size;

            // Salvage and Clear centred on the slots, whichever's taller: the other is moved down by half the difference
            var buttonsHeight = 2 * Widgets.Scaled(22) + ImGui.GetStyle().ItemSpacing.Y;
            var top = ImGui.GetCursorPosY();
            var slotsTop = top + Math.Max(0, (buttonsHeight - height) / 2);
            var buttonsTop = top + Math.Max(0, (height - buttonsHeight) / 2);

            ImGui.SetCursorPosY(slotsTop);

            if (Widgets.BeginChild("salvage-slots", new Vector2(perRow * size + scrollbar, height), false)) {
                RenderSlots(perRow, rows);
            }

            Widgets.EndChild();
            DropTarget();

            ImGui.SameLine();
            ImGui.SetCursorPosY(buttonsTop);
            ImGui.BeginGroup();
            RenderButtons(supported);
            ImGui.EndGroup();

            ImGui.Spacing();
            RenderSummary();
            RenderResults();
        }

        /// <summary>The quick adds, a row above the slots (SalvagePanel.Adds.cs).</summary>
        private void RenderAddButtons(bool supported) {
            var first = true;
            ImGui.BeginDisabled(!supported || CheckingRules || Sending);
            var rulesLabel = CheckingRules ? $"Checking {RulesProgress}...##salvage-by-rules"
                : "Add by rules##salvage-by-rules";

            if (Form.FlowSmallButton(ref first, rulesLabel, "Adds everything you carry that your active loot profile says "
                + "to Salvage: the first of its rules that matches an item decides, as in looting, so a rule above that "
                + "keeps the item keeps it. Asks the server about every item first. Items marked for sale are left out.")) {
                AddByRules();
            }

            if (Form.FlowSmallButton(ref first, "Add all packs##salvage-all-packs", "Adds every item in your main pack and "
                + "side packs that can be salvaged, but those in packs tagged Keep (the pack's Tags) and those marked "
                + "for sale. Check the list before you press Salvage.")) {
                AddAllPacks();
            }

            ImGui.EndDisabled();
        }

        /// <summary>
        /// The game's close button for the panel (template 0x1000047F in layout 0x21000040, 22x20: 060012AA, pressed
        /// 060012A9, `panel-close`), at the right end of the warning's line, as the game's sits in the panel's top right.
        /// </summary>
        private void RenderCloseIcon() {
            var size = new Vector2(Widgets.Scaled(22), Widgets.Scaled(20));
            var right = ImGui.GetWindowContentRegionMax().X;
            ImGui.SameLine();
            ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), right - size.X));
            var clicked = ImGui.InvisibleButton("##salvage-close", size);
            var min = ImGui.GetItemRectMin();
            var art = ImGui.IsItemActive() ? "panel-close-pressed" : "panel-close";

            if (!ItemIcons.DrawArtBox(ImGui.GetWindowDrawList(), art, min, min + size)) {
                ImGui.GetWindowDrawList().AddText(min, ImGui.GetColorU32(ImGuiCol.Text), "X");
            }

            Widgets.Tip("Closes the panel, taking everything off it (nothing's salvaged), and the game's salvage panel "
                + "with it. Use your Ust to open it again.");

            if (clicked) {
                Window.Close();
            }
        }

        private void RenderButtons(bool supported) {
            var retail = true;
            var confirmed = Confirmed().Count;
            var checking = Checking();
            var hasUst = Carried.Items().Exists(Carried.IsUst);
            var enabled = supported && hasUst && confirmed > 0 && checking == 0 && !Sending;
            var label = Sending ? "Salvaging..." : "Salvage";

            if (GamePanelArt.Button("salvage-go", label, new Vector2(Widgets.Scaled(ButtonWidth), Widgets.Scaled(22)),
                "vendor-button", retail, enabled)) {
                SalvageAll();
            }

            Widgets.Tip(!hasUst ? "You need an Ust in your packs to salvage."
                : checking > 0 ? $"Waiting for the server to describe {Words.Count(checking, "item")}."
                : "Salvages everything on the panel with the Ust in your packs (@playerui salvage, the server's own "
                    + "salvaging). New salvage tops up the stacks you carry first, and new stacks go in your salvage "
                    + "crate if you carry one. The items are destroyed.");

            if (GamePanelArt.Button("salvage-clear", "Clear", new Vector2(Widgets.Scaled(ButtonWidth), Widgets.Scaled(22)),
                "vendor-button", retail, queued.Count > 0 && !Sending)) {
                queued.Clear();
            }

            Widgets.Tip("Takes everything off the panel. Nothing's salvaged.");
        }

        private void RenderSlots(int perRow, int rows) {
            var size = Widgets.Scaled(SlotSize);
            var slots = perRow * rows;
            var origin = ImGui.GetCursorPos();
            int? remove = null;

            for (var i = 0; i < slots; i++) {
                ImGui.SetCursorPos(origin + new Vector2(i % perRow * size, i / perRow * size));
                var id = i < queued.Count ? queued[i] : 0;

                if (Slot(i, id, size)) {
                    remove = id;
                }
            }

            if (remove.HasValue) {
                Remove(remove.Value);
            }
        }

        /// <summary>One square: the empty slot, the item's icon and mark, its tooltip and menu. True when it's clicked off.</summary>
        private unsafe bool Slot(int index, int id, float size) {
            var min = ImGui.GetCursorScreenPos();
            var max = min + new Vector2(size, size);
            var clicked = ImGui.InvisibleButton($"##salvage-slot-{index}", new Vector2(size, size));
            var hovered = ImGui.IsItemHovered();
            var drawList = ImGui.GetWindowDrawList();
            var item = id == 0 ? null : CoreManager.Current.WorldFilter[id];
            var takeOff = false;

            if (id != 0 && RightClick.BeginItemMenu($"salvage-slot-menu-{index}",
                () => Appraisal.Show(unchecked((uint)id)))) {
                if (ImGui.MenuItem("Take off the panel")) {
                    takeOff = true;
                }

                Appraisal.MenuItems(unchecked((uint)id));
                ImGui.EndPopup();
            }

            if (!ItemIcons.DrawArtBox(drawList, ItemIcons.ArtEmptySlot, min, max)) {
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBg));
            }

            if (item != null) {
                var described = Described(id);
                var waiting = described == null;
                // one the server hasn't answered for yet is drawn faded, as it might still come off
                icons.DrawAt(drawList, ItemIcons.Of(item), min, size, waiting ? 0x99FFFFFF : 0xFFFFFFFF);

                if (described != null) {
                    ItemIcons.DrawMark(drawList, RuleFor(described), min, size);
                }

                if (hovered) {
                    ShowTooltip(id, item.Name);
                }
            }

            if (hovered && !ItemIcons.DrawArtBox(drawList, ItemIcons.ArtSelected, min, max, HoverTint)) {
                drawList.AddRect(min, max, ImGui.GetColorU32(ImGuiCol.ButtonHovered));
            }

            DrawDropMark(min, max);
            // a click takes it off, as on the game's panel
            return takeOff || (clicked && id != 0 && ImGui.GetDragDropPayload().NativePtr == null);
        }

        private CorpseItemInfo Described(int id) {
            return feed.Described.TryGetValue(unchecked((uint)id), out var described) ? described : null;
        }

        private LootRule RuleFor(CorpseItemInfo described) {
            return Loot.Decide(settings.Data.Loot.ActiveFor(CoreManager.Current.CharacterFilter.Name), described).Rule;
        }

        private unsafe void ShowTooltip(int id, string name) {
            if (ImGui.GetDragDropPayload().NativePtr != null) {
                return;
            }

            var guid = unchecked((uint)id);
            var described = Described(id);
            List<ItemLine> lines;
            LootRule rule = null;

            if (described != null) {
                lines = ItemLines.Build(described);
                rule = RuleFor(described);
            }
            else {
                lines = new List<ItemLine> {
                    new ItemLine(name, ItemLineStyle.Title),
                    new ItemLine(feed.DescribeWaitText(guid), ItemLineStyle.Detail),
                };
            }

            var entry = EntryOf(id);

            if (entry != null && entry.Material != 0) {
                var units = SalvageMath.Units(entry.Workmanship);
                var unitText = units.Min == units.Max ? $"{units.Min}" : $"{units.Min}-{units.Max}";
                lines.Add(new ItemLine("Salvage", ItemLineStyle.Heading));
                lines.Add(new ItemLine($"Makes {unitText} units of W{SalvageMath.Tier(entry.Workmanship)} "
                    + $"{MaterialNames.Of(entry.Material) ?? "salvage"}", ItemLineStyle.Detail));
            }

            if (VendorSale.IsMarked(settings, id)) {
                lines.Add(new ItemLine("Marked for sale.", ItemLineStyle.Detail));
            }

            lines.Add(new ItemLine("Click to take it off the panel; right-click for more.", ItemLineStyle.Detail));
            ItemTooltip.Show(lines, rule: rule);
        }

        // ---- drops ----

        /// <summary>
        /// The drag under way: null for none (or one that isn't an item: a spell), else whether it can go on the panel.
        /// </summary>
        private unsafe bool? DragVerdict() {
            int id;
            var payload = ImGui.GetDragDropPayload();

            if (payload.NativePtr != null) {
                if (payload.IsDataType(InventoryBrowser.MainPackDragType)) {
                    id = CoreManager.Current.CharacterFilter.Id;
                }
                else if (payload.IsDataType(InventoryBrowser.DragType)) {
                    id = InventoryBrowser.DraggedItem;
                }
                else {
                    return null;
                }
            }
            else if (GameDrag.Dragging && SpellDrag.Spell == 0) {
                id = CoreManager.Current.Actions.CurrentSelection;
            }
            else {
                return null;
            }

            return id != 0 && Verdict(id).Ok;
        }

        private void DrawDropMark(Vector2 min, Vector2 max) {
            var verdict = DragVerdict();

            if (verdict.HasValue && ImGui.IsMouseHoveringRect(min, max, false)) {
                icons.DrawDropArt(ImGui.GetWindowDrawList(), min, max, verdict.Value);
            }
        }

        /// <summary>What's let go over the slots goes on: from the plugin's lists, or kept from the game (GameDrag).</summary>
        private unsafe void DropTarget() {
            if (ImGui.BeginDragDropTarget()) {
                var item = ImGui.AcceptDragDropPayload(InventoryBrowser.DragType);

                if (item.NativePtr != null && item.IsDelivery()) {
                    Add(InventoryBrowser.DraggedItem);
                }

                var mainPack = ImGui.AcceptDragDropPayload(InventoryBrowser.MainPackDragType);

                if (mainPack.NativePtr != null && mainPack.IsDelivery()) {
                    Add(CoreManager.Current.CharacterFilter.Id);
                }

                ImGui.EndDragDropTarget();
            }

            var caught = GameDrag.CaughtOverLastItem();

            if (caught) {
                GameDrag.TakeCaught();
            }

            if ((caught || GameDrag.ReleasedOverLastItem()) && SpellDrag.Spell == 0) {
                Add(CoreManager.Current.Actions.CurrentSelection);
            }
        }
    }
}
