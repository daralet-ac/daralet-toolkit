using System;
using System.Collections.Generic;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Loot marks on the Vendor window's slots (SettingsData.VendorLootMarks, its Loot marks box), as the Inventory
    /// window's Loot highlights: each item the server describes (PlayerFeed.Describe: yours, and the vendor's stock on a
    /// server that describes it, "vendordescribe") is run through the active loot profile (Loot.Decide), and the rule
    /// that decides it, if it has a mark, is drawn over its icon (ItemIcons.DrawMark). Only the slots in sight ask, a few
    /// at a time. What each decided is kept until new descriptions come, the profile changes, or two seconds pass (a rule
    /// edited meanwhile).
    /// </summary>
    internal sealed partial class VendorPanel {
        private const string MarksLabel = "Loot highlights";

        private static readonly TimeSpan MarksKept = TimeSpan.FromSeconds(2);

        private readonly Dictionary<uint, LootRule> marks = new Dictionary<uint, LootRule>();
        private DateTime marksFor;
        private DateTime marksAt;
        private LootList marksList;

        /// <summary>The Loot marks box, beside the vendor's name: the game's light in Retail, a checkbox in Modern.</summary>
        private void RenderMarksBox(bool retail) {
            if (GamePanelArt.Light("vendor-loot-marks", MarksLabel, ref settings.Data.VendorLootMarks, retail)) {
                settings.Changed();
            }

            Widgets.Tip("Marks the vendor's items, and the items on your lists, as your active loot profile's rules do: the "
                + "first rule that matches decides, and its frame and symbol go on the icon.");
        }

        /// <summary>How wide the Loot marks box is, for the row it sits in.</summary>
        private static float MarksBoxWidth(bool retail) {
            var label = ImGui.CalcTextSize(MarksLabel).X;
            return retail ? Widgets.Scaled(13) + Widgets.Scaled(8) + label
                : ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + label;
        }

        /// <summary>The rule that marks this item, or null for none (marks off, not described yet, no rule, no mark).</summary>
        private LootRule MarkFor(VendorStockItem item) {
            if (!settings.Data.VendorLootMarks || item == null) {
                return null;
            }

            var list = settings.Data.Loot.ActiveFor(CoreManager.Current.CharacterFilter.Name);

            if (list != marksList || feed.DescribedUpdatedAt != marksFor || DateTime.Now - marksAt > MarksKept) {
                marks.Clear();
                marksList = list;
                marksFor = feed.DescribedUpdatedAt;
                marksAt = DateTime.Now;
            }

            var guid = unchecked((uint)item.Id);

            if (marks.TryGetValue(guid, out var known)) {
                return known;
            }

            if (!feed.Described.TryGetValue(guid, out var described)) {
                if (HotbarItems.IsMine(item.Id) || feed.HasFeature(PlayerFeed.FeatureVendorDescribe)) {
                    feed.Describe(guid);
                }

                return null;
            }

            var rule = Loot.Decide(list, described).Rule;
            var mark = LootMarks.Marks(rule) ? rule : null;
            marks[guid] = mark;
            return mark;
        }
    }
}
