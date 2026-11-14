using System.Collections.Generic;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The item tooltip over a vendor slot, as the bank's and packs' have it, with what it costs or pays and what a click
    /// does at the end. Every item is described by the server (PlayerFeed.Describe): yours (the sell list's) like any in
    /// your packs, the vendor's stock on a server whose describe takes it ("vendordescribe"). On an older server the
    /// stock shows what the client has (VendorStockItem: its value, burden and stack). Decal's WorldFilter hasn't got a
    /// vendor's items, so this works from VendorStockItem, not a WorldObject.
    /// </summary>
    internal sealed partial class VendorPanel {
        private enum SlotKind {
            Stock,
            Buying,
            Selling,
        }

        private unsafe void ShowTooltip(VendorStockItem item, int count, SlotKind kind, string how) {
            // nothing but the icon follows the mouse while an item is dragged
            if (ImGui.GetDragDropPayload().NativePtr != null) {
                return;
            }

            var guid = unchecked((uint)item.Id);
            List<ItemLine> lines;
            LootRule rule = null;
            var describable = HotbarItems.IsMine(item.Id) || feed.HasFeature(PlayerFeed.FeatureVendorDescribe);

            if (describable) {
                feed.Describe(guid);
            }

            if (feed.Described.TryGetValue(guid, out var described)) {
                lines = ItemLines.Build(described);
                rule = Loot.Decide(settings.Data.Loot.ActiveFor(CoreManager.Current.CharacterFilter.Name), described).Rule;
            }
            else {
                var stackText = item.StackCount > 1 ? $"   Stack: {item.StackCount:N0}" : "";
                lines = new List<ItemLine> {
                    new ItemLine(item.Name, ItemLineStyle.Title),
                    new ItemLine(item.Kind, ItemLineStyle.Detail),
                    new ItemLine($"Value: {item.Value:N0}   Burden: {item.Burden:N0}{stackText}", ItemLineStyle.Detail),
                };

                if (describable) {
                    lines.Add(new ItemLine(feed.DescribeWaitText(guid), ItemLineStyle.Detail));
                }
            }

            // a Use and Description only for what Decal has (yours): it has none of a vendor's items
            ItemUse.With(lines, guid);
            lines.Add(new ItemLine(PriceLine(item, count, kind), ItemLineStyle.Heading));

            if (how != null) {
                lines.Add(new ItemLine(how, ItemLineStyle.Detail));
            }

            ItemTooltip.Show(lines, rule: rule);
        }

        /// <summary>"Costs 1,234p", "3 cost 3,702p", "The vendor pays 120p": the server's sums, as the lists add them up.</summary>
        private static string PriceLine(VendorStockItem item, int count, SlotKind kind) {
            var vendor = CoreManager.Current.WorldFilter.OpenVendor;
            var many = System.Math.Max(1, count);
            var worth = Worth(item, many, vendor, kind == SlotKind.Selling);

            if (kind == SlotKind.Selling) {
                return $"The vendor pays {VendorPrices.Pyreals(worth)}";
            }

            return many > 1 ? $"{many} cost {VendorPrices.Pyreals(worth)}" : $"Costs {VendorPrices.Pyreals(worth)}";
        }
    }
}
