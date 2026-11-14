using System;
using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// What a vendor charges and pays, by daralet-ACE's own sums (Vendor.GetSellCost and GetBuyCost): you pay
    /// max(1, ceil(SellPrice × value − 0.1)) for each one bought, and are paid max(1, floor(BuyPrice × value + 0.1)) for
    /// each item sold, value being the item's whole Value (a stack's, for a stack). Promissory notes go at 1.15 and 1
    /// (1 and 1 at a vendor with another currency, which this doesn't know). Decal's Vendor.SellRate and BuyRate are the
    /// vendor's SellPrice and BuyPrice. And the kinds of items its Items tab sorts them into, by ItemType bit.
    /// </summary>
    public static class VendorPrices {
        public const int PromissoryNote = 0x40000;

        /// <summary>The kinds the Items tab offers, by ItemType bit, as the game's menu names them.</summary>
        public static readonly IReadOnlyList<(int Bit, string Name)> Kinds = new List<(int, string)> {
            (0x1, "Melee Weapons"), (0x100, "Missile Weapons"), (0x8000, "Casters"), (0x2, "Armor"), (0x4, "Clothing"),
            (0x8, "Jewelry"), (0x20, "Food"), (0x800, "Gems"), (0x1000, "Spell Components"), (0x80000, "Mana Stones"),
            (0x2000, "Writables"), (0x4000, "Keys"), (0x200, "Containers"), (PromissoryNote, "Trade Notes"),
            (0x400000, "Cooking"), (0x800000, "Alchemy"), (0x4000000, "Alchemy"), (0x2000000, "Fletching"),
            (0x8000000, "Fletching"), (0x20000000, "Tinkering Tools"), (0x40000000, "Salvage"), (0x100000, "Services"),
            (0x80, "Miscellaneous"),
        };

        /// <summary>What you pay for one.</summary>
        public static long BuyCost(int value, int itemType, double sellRate) {
            var rate = itemType == PromissoryNote ? 1.15 : sellRate;
            return Math.Max(1, (long)Math.Ceiling((float)rate * value - 0.1));
        }

        /// <summary>What you're paid for an item (a whole stack).</summary>
        public static long SellPayout(int value, int itemType, double buyRate) {
            var rate = itemType == PromissoryNote ? 1.0 : buyRate;
            return Math.Max(1, (long)Math.Floor((float)rate * value + 0.1));
        }

        /// <summary>The Items tab's name for an item's kind: the first of Kinds it has, else Miscellaneous.</summary>
        public static string KindOf(int itemType) {
            foreach (var kind in Kinds) {
                if ((itemType & kind.Bit) != 0) {
                    return kind.Name;
                }
            }

            return "Miscellaneous";
        }

        /// <summary>Pyreals as the game writes them: 11,787p.</summary>
        public static string Pyreals(long amount) {
            return amount.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + "p";
        }
    }
}
