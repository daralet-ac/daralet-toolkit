using System;
using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// What the panel will make, by the server's sums (SalvageMath): a row per material and tier, with its items, the units
    /// they'll make (a range: an odd workmanship rounds up half the time), the stack of it you carry, and what the items
    /// would fetch from a vendor; then the totals, the new stacks it needs against your free pack slots (the server
    /// refuses the lot without room), and warnings: no Ust, items your loot profile keeps, items marked for sale.
    /// </summary>
    internal sealed partial class SalvagePanel {
        /// <summary>A main pack holds 102 items, which the client isn't always told.</summary>
        private const int DefaultMainPackSlots = 102;

        /// <summary>What the sums need of a queued item: its material, workmanship, value and burden.</summary>
        private SalvageEntry EntryOf(int id) {
            var item = CoreManager.Current.WorldFilter[id];

            if (item == null) {
                return null;
            }

            var described = Described(id);
            var material = item.Values(LongValueKey.Material, 0);

            if (material == 0 && described?.Material != null) {
                material = MaterialNames.Number(described.Material);
            }

            return new SalvageEntry {
                Id = id,
                Material = material,
                Workmanship = described?.Workmanship ?? item.Values(DoubleValueKey.SalvageWorkmanship, 0.0),
                Value = item.Values(LongValueKey.Value, 0),
                Burden = item.Values(LongValueKey.Burden, 0),
            };
        }

        /// <summary>The units and stacks you carry of a salvage stack's weenie.</summary>
        private static Func<int, (int Units, int Stacks)> CarriedStacks() {
            var byWcid = Carried.Items()
                .Where(o => SalvageMath.IsStackWcid(o.Type))
                .GroupBy(o => o.Type)
                .ToDictionary(g => g.Key, g => (g.Sum(o => Math.Max(1, o.Values(LongValueKey.StackCount, 1))), g.Count()));
            return wcid => byWcid.TryGetValue(wcid, out var have) ? have : (0, 0);
        }

        /// <summary>The empty slots in your main pack and side packs, the salvage crate's included.</summary>
        private static int FreeSlots() {
            var world = CoreManager.Current.WorldFilter;
            var me = CoreManager.Current.CharacterFilter.Id;
            var byContainer = world.GetAll().ToLookup(o => o.Values(LongValueKey.Container, 0));
            var mainSlots = world[me]?.Values(LongValueKey.ItemSlots, 0) ?? 0;
            var inMain = byContainer[me].Count(o => o.ObjectClass != ObjectClass.Container
                && o.Values(LongValueKey.EquippedSlots, 0) == 0);
            var free = Math.Max(0, (mainSlots > 0 ? mainSlots : DefaultMainPackSlots) - inMain);

            foreach (var pack in byContainer[me].Where(o => o.ObjectClass == ObjectClass.Container)) {
                free += Math.Max(0, pack.Values(LongValueKey.ItemSlots, 0) - byContainer[pack.Id].Count());
            }

            return free;
        }

        private void RenderSummary() {
            if (queued.Count == 0) {
                Widgets.TextDisabled("Drag items here to salvage them: from your packs, the Inventory window or the game's "
                    + "own pack. Drop a whole pack (the main pack too) to put on everything in it that can be salvaged. "
                    + "Retained items never go on.");
                return;
            }

            var entries = queued.Select(EntryOf).Where(e => e != null).ToList();
            var groups = SalvageMath.Groups(entries, CarriedStacks());
            RenderGroups(groups);

            var minUnits = groups.Sum(g => g.MinUnits);
            var maxUnits = groups.Sum(g => g.MaxUnits);
            var units = minUnits == maxUnits ? $"{minUnits:N0}" : $"{minUnits:N0}-{maxUnits:N0}";
            var value = entries.Sum(e => (long)e.Value);
            var burden = entries.Sum(e => (long)e.Burden);
            Widgets.Text($"{Words.Count(entries.Count, "item")}, {units} units of salvage");
            ImGui.SameLine();
            Widgets.TextDisabled($"(value {value:N0}, burden {burden:N0})");
            Widgets.Tip("Value is what the items are worth, which a vendor pays a share of: what's given up by salvaging "
                + "them. Burden is what you'll stop carrying, less the salvage's own.");

            var newStacks = groups.Sum(g => g.NewStacks);
            var free = FreeSlots();

            if (newStacks > 0) {
                var color = newStacks > free ? Colors.Danger : Colors.Detail;
                Widgets.TextColored(color, $"Needs {Words.Count(newStacks, "new stack")}; you have "
                    + $"{Words.Count(free, "free slot")}.");
                Widgets.Tip("New salvage tops up the stacks of it you carry first (1,000 units a stack); what's left makes "
                    + "new stacks. The server salvages nothing without room for them all.");
            }

            RenderWarnings();
        }

        private void RenderGroups(List<SalvageGroup> groups) {
            if (groups.Count == 0) {
                return;
            }

            var flags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV;

            if (!ImGui.BeginTable("salvage-groups", 5, flags)) {
                return;
            }

            ImGui.TableSetupColumn("Material");
            ImGui.TableSetupColumn("Items");
            ImGui.TableSetupColumn("Units");
            ImGui.TableSetupColumn("You carry");
            ImGui.TableSetupColumn("Value");
            Widgets.HeadersRow(
                "What the items salvage into, and the stack's tier: their workmanship, rounded.",
                "How many items of it are on the panel.",
                "The units of salvage they'll make: 1 + half the workmanship each, an odd workmanship rounding up half "
                    + "the time.",
                "The units of that salvage you carry already, which the new salvage joins.",
                "What the items are worth.");

            foreach (var group in groups) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                Widgets.Text($"{MaterialNames.Of(group.Material) ?? "?"} W{group.Tier}");
                ImGui.TableNextColumn();
                Widgets.Text($"{group.Items}");
                ImGui.TableNextColumn();
                Widgets.Text(group.MinUnits == group.MaxUnits ? $"{group.MinUnits:N0}"
                    : $"{group.MinUnits:N0}-{group.MaxUnits:N0}");
                ImGui.TableNextColumn();

                if (group.Carried > 0) {
                    Widgets.Text($"{group.Carried:N0}");
                }
                else {
                    Widgets.TextDisabled("new");
                }

                ImGui.TableNextColumn();
                Widgets.Text($"{group.Value:N0}");
            }

            ImGui.EndTable();
        }

        private void RenderWarnings() {
            if (!Carried.Items().Exists(Carried.IsUst)) {
                Widgets.TextColored(Colors.Danger, "You don't carry an Ust, which salvaging needs.");
            }

            var checking = Checking();

            if (checking > 0) {
                Widgets.TextDisabled($"Asking the server about {Words.Count(checking, "item")}...");
            }

            var kept = queued
                .Select(id => (Id: id, Described: Described(id)))
                .Where(x => x.Described != null && RuleFor(x.Described)?.Outcome == LootOutcome.Keep)
                .ToList();

            if (kept.Count > 0) {
                Widgets.TextColored(Colors.Warning, $"Your loot profile keeps {Words.Count(kept.Count, "item")} on the "
                    + "panel.");
                Widgets.Tip("The first rule of your active loot profile that matches each says Keep: "
                    + string.Join(", ", kept.Take(8).Select(x => CoreManager.Current.WorldFilter[x.Id]?.Name ?? "?"))
                    + (kept.Count > 8 ? "..." : "."));
            }

            var forSale = queued.Count(id => VendorSale.IsMarked(settings, id));

            if (forSale > 0) {
                Widgets.TextColored(Colors.Warning, $"{Words.Count(forSale, "item")} on the panel marked for sale.");
            }
        }
    }
}
