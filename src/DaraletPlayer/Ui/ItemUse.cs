using System.Collections.Generic;
using System.Linq;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// An item's Use text and long description, for the Appraisal window and the item tooltips. The server's description of
    /// an item doesn't carry them: they come only with the game's own appraisal, which fills them in on Decal's copy of the
    /// item, so the first time one is wanted the appraisal is asked for (Actions.RequestId; the game's panel stays shut, as
    /// the client shows only appraisals it started). The tooltips show them for what you can't wear or wield, and for
    /// trinkets: gear's would make them too long. The Appraisal window shows every item's (AppraisalLines).
    /// </summary>
    internal static class ItemUse {
        /// <summary>The trinket and sigil slots: things worn for what they do, which their Use text explains.</summary>
        private const uint TrinketSlots = EquipSlots.TrinketOne | EquipSlots.SigilOne | EquipSlots.SigilTwo | EquipSlots.SigilThree;

        /// <summary>
        /// The items an appraisal's been asked for, once each: hovering over a bank of items asks for each only the first time,
        /// and one the server never answers isn't asked about again and again.
        /// </summary>
        private static readonly HashSet<int> asked = new HashSet<int>();

        /// <summary>Whether the item gets a Use and Description: it isn't worn or wielded, or it's a trinket.</summary>
        public static bool Shows(WorldObject item) {
            if (item == null) {
                return false;
            }

            var slots = unchecked((uint)item.Values(LongValueKey.EquipableSlots, 0));
            return slots == 0 || (slots & ~TrinketSlots) == 0;
        }

        /// <summary>Asks for the item's appraisal if it gets a Use and Description and Decal hasn't got them yet.</summary>
        /// <param name="gear">Gear too: the Appraisal window shows every item's.</param>
        public static void Request(uint guid, bool gear = false) {
            var item = CoreManager.Current.WorldFilter[unchecked((int)guid)];

            if (item == null || item.HasIdData || !(gear || Shows(item)) || !asked.Add(item.Id)) {
                return;
            }

            CoreManager.Current.Actions.RequestId(item.Id);
        }

        /// <summary>
        /// The item's Use and Description as tooltip lines, each its own section; nothing for an item that has neither or
        /// doesn't get them. Asks for the appraisal the first time.
        /// </summary>
        /// <param name="waiting">Whether to say so, until the appraisal comes back.</param>
        public static List<ItemLine> Lines(uint guid, bool waiting = false) {
            var lines = new List<ItemLine>();
            var item = CoreManager.Current.WorldFilter[unchecked((int)guid)];

            if (!Shows(item)) {
                return lines;
            }

            Request(guid);

            var use = item.Values(StringValueKey.UsageInstructions, "").Trim();
            var description = item.Values(StringValueKey.FullDescription, "").Trim();

            if (use.Length > 0) {
                lines.Add(new ItemLine("Use", ItemLineStyle.Heading));
                lines.Add(new ItemLine(use, ItemLineStyle.Text));
            }

            if (description.Length > 0) {
                lines.Add(new ItemLine("Description", ItemLineStyle.Heading));
                lines.Add(new ItemLine(description, ItemLineStyle.Detail));
            }

            if (lines.Count == 0 && waiting && !item.HasIdData) {
                lines.Add(new ItemLine("Getting the description...", ItemLineStyle.Detail));
            }

            return lines;
        }

        /// <summary>
        /// For the Appraisal window: any item's Use and Description, gear's too, where the game's appraisal explains each
        /// property (weapon and armor mods in the Use text, "Property Descriptions:" atop the description). Those
        /// descriptions come back on their own, for the lines naming the properties to show on hover; any no line names
        /// stay in the Description. Too long for the item tooltips, which keep to Lines.
        /// </summary>
        /// <param name="named">The texts of the lines shown above, to find which descriptions they name.</param>
        public static List<ItemLine> AppraisalLines(uint guid, IEnumerable<string> named, out PropertyDescriptions properties) {
            var lines = new List<ItemLine>();
            var item = CoreManager.Current.WorldFilter[unchecked((int)guid)];
            properties = PropertyDescriptions.Parse("");

            if (item == null) {
                return lines;
            }

            Request(guid, gear: true);

            var use = item.Values(StringValueKey.UsageInstructions, "").Trim();
            properties = PropertyDescriptions.Parse(item.Values(StringValueKey.FullDescription, ""));

            var description = properties.Rest;
            var unnamed = properties.NotIn(named).Select(e => $"{e.Key}: {e.Value}").ToList();

            if (unnamed.Count > 0) {
                description = (string.Join("\n", unnamed) + "\n\n" + description).Trim();
            }

            if (use.Length > 0) {
                lines.Add(new ItemLine("Use", ItemLineStyle.Heading));
                lines.Add(new ItemLine(use, ItemLineStyle.Text));
            }

            if (description.Length > 0) {
                lines.Add(new ItemLine("Description", ItemLineStyle.Heading));
                lines.Add(new ItemLine(description, ItemLineStyle.Detail));
            }

            if (lines.Count == 0 && !item.HasIdData) {
                lines.Add(new ItemLine("Getting the description...", ItemLineStyle.Detail));
            }

            return lines;
        }

        /// <summary>The lines with the item's Use and Description after them.</summary>
        public static List<ItemLine> With(List<ItemLine> lines, uint guid) {
            lines.AddRange(Lines(guid));
            return lines;
        }
    }
}
