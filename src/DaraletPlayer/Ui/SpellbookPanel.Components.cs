using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using ImGuiNET;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Components page: the spell components you carry, how many of each, and a Buy to box beside each, the level the
    /// game's /fillcomps buys up to at a vendor (ClientComponents, as the game's own components page sets it). Every
    /// component comes from the portal dat's spell component table (name, icon), by weenie class id; you carry what Decal's
    /// WorldFilter has of that class (ObjectClass SpellComponent, stacks added up). Without All components only those you
    /// carry, have a level for, or the picked spell takes show; those the picked spell takes and you're out of are red.
    /// </summary>
    internal sealed partial class SpellbookPanel {
        /// <summary>A Buy to box being typed in: its number, sent once the box is let go of.</summary>
        private readonly Dictionary<uint, int> typing = new Dictionary<uint, int>();

        private void RenderComponents(SpellbookOptions data, bool retail) {
            var all = data.AllComponents;

            if (Form.Check("All components##spellbook", ref all, "Lists every spell component, not only those you carry, "
                + "have a buy level for, or the picked spell takes.")) {
                data.AllComponents = all;
                settings.Changed();
            }

            Form.Hint("Buy to: how many /fillcomps buys up to at a vendor (0 for none), as the game's components page sets.");

            var spell = Spells.Info(picked);
            var needed = new HashSet<uint>(spell?.Components ?? new List<uint>());
            var rows = ComponentRow.Build(ComponentTable(), ClientComponents.Wcid, CarriedComponents(),
                wcid => ClientComponents.Level(wcid) ?? 0, needed, all);

            if (ClientComponents.Broken) {
                Widgets.TextColored(Colors.Danger, "Buy levels are off: the game's couldn't be used (see log.txt).");
            }

            if (rows.Count == 0) {
                Widgets.TextDisabled(all ? "The spell component table can't be read." : "You carry no spell components.");
                return;
            }

            if (Widgets.BeginChild("spellbook-components", new Vector2(0, 0), false)
                && ImGui.BeginTable("spellbook-components-table", 4, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg)) {
                var iconSize = Math.Max(Widgets.Scaled(RowHeight), ImGui.GetFrameHeight());
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, iconSize);
                ImGui.TableSetupColumn("Component", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Held", ImGuiTableColumnFlags.WidthFixed, Widgets.Scaled(50));
                ImGui.TableSetupColumn("Buy to", ImGuiTableColumnFlags.WidthFixed, Widgets.Scaled(64));
                ImGui.TableHeadersRow();

                foreach (var row in rows) {
                    RenderComponentRow(row, iconSize, spell);
                }

                ImGui.EndTable();
            }

            Widgets.EndChild();
        }

        private void RenderComponentRow(ComponentRow row, float iconSize, SpellInfo spell) {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            var icon = row.Icon == 0 ? null : new IconLayers(0, row.Icon < 0x01000000 ? row.Icon | 0x06000000 : row.Icon, 0);
            icons.DrawAt(ImGui.GetWindowDrawList(), icon, ImGui.GetCursorScreenPos(), iconSize);
            ImGui.Dummy(new Vector2(iconSize, iconSize));

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            var missing = row.Needed && row.Carried == 0;
            Widgets.TextColored(missing ? Colors.Danger : row.Needed ? Colors.Good : Colors.Text, row.Name);
            Widgets.Tip(row.Needed ? $"{spell?.Name} takes it." + (missing ? " You have none." : "") : row.Name);

            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            Widgets.Text(row.Carried.ToString());

            ImGui.TableNextColumn();
            var level = typing.TryGetValue(row.Wcid, out var typed) ? typed : row.BuyLevel;
            ImGui.SetNextItemWidth(-1);
            ImGui.BeginDisabled(ClientComponents.Broken);

            if (ImGui.InputInt($"##buy-{row.Wcid}", ref level, 0, 0)) {
                typing[row.Wcid] = Math.Max(0, Math.Min(10000, level));
            }

            // sent once, when the box is let go of, not for every key typed
            if (ImGui.IsItemDeactivatedAfterEdit() && typing.TryGetValue(row.Wcid, out var amount)) {
                ClientComponents.SetLevel(row.Wcid, amount);
                typing.Remove(row.Wcid);
            }
            else if (!ImGui.IsItemActive()) {
                typing.Remove(row.Wcid);
            }

            ImGui.EndDisabled();
            Widgets.Tip($"/fillcomps at a vendor buys {row.Name} up to this many (0 for none). Saved with your character, "
                + "as the game's components page saves it.");
        }

        /// <summary>The portal dat's spell components: id, name, icon and category; none when it can't be read.</summary>
        private static IEnumerable<(uint Id, string Name, uint Icon, uint Category)> ComponentTable() {
            Dictionary<uint, ACE.DatLoader.Entity.SpellComponentBase> table = null;

            try {
                table = UBService.PortalDat?.SpellComponentsTable?.SpellComponents;
            }
            catch {
                // UtilityBelt without its portal dat: nothing below
            }

            if (table == null) {
                yield break;
            }

            foreach (var pair in table) {
                yield return (pair.Key, pair.Value.Name, pair.Value.Icon, pair.Value.Category);
            }
        }

        /// <summary>How many of each spell component you carry, by weenie class id, stacks added up.</summary>
        private static Dictionary<uint, int> CarriedComponents() {
            var carried = new Dictionary<uint, int>();

            foreach (var item in CoreManager.Current.WorldFilter.GetInventory()) {
                if (item.ObjectClass != ObjectClass.SpellComponent) {
                    continue;
                }

                var wcid = (uint)item.Type;
                var count = StackText.FromDecal(item.Values(LongValueKey.StackCount, 1));
                carried[wcid] = (carried.TryGetValue(wcid, out var had) ? had : 0) + count;
            }

            return carried;
        }
    }
}
