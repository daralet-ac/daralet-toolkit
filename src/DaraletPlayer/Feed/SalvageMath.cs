using System;
using System.Collections.Generic;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>One item on the Salvage window, as the sums below need it: kept free of Decal so tests can build one.</summary>
    public class SalvageEntry {
        public int Id;
        /// <summary>MaterialType: what it salvages into.</summary>
        public int Material;
        /// <summary>The item's workmanship (the server's, else Decal's), 0 when not known.</summary>
        public double Workmanship;
        public int Value;
        public int Burden;
    }

    /// <summary>What salvaging one material at one tier gives, for the Salvage window's summary.</summary>
    public class SalvageGroup {
        public int Material;
        public int Tier;
        public int Items;
        /// <summary>The fewest and most units salvaging these could give (an odd workmanship rounds up half the time).</summary>
        public int MinUnits;
        public int MaxUnits;
        /// <summary>The units of this material and tier you carry already, which the new salvage tops up first.</summary>
        public int Carried;
        /// <summary>How many stacks those are.</summary>
        public int CarriedStacks;
        public int Value;

        /// <summary>The new stacks it could need once the ones carried are topped up, at the most units.</summary>
        public int NewStacks => SalvageMath.NewStacks(Carried, CarriedStacks, MaxUnits);
    }

    /// <summary>
    /// daralet-ACE's salvaging sums (Player.HandleSalvaging, GetStructure, SalvageStacks), for the Salvage window to say
    /// what salvaging will make before it's done. The server rolls the units itself; these only tell.
    /// </summary>
    public static class SalvageMath {
        /// <summary>SalvageStacks.BaseWcid: a stack's weenie is BaseWcid + material * 10 + tier - 1.</summary>
        public const int BaseWcid = 1059000;
        public const int MinTier = 1;
        public const int MaxTier = 10;
        /// <summary>SalvageStacks.MaxStackSize: the most units a stack holds.</summary>
        public const int MaxStackSize = 1000;

        /// <summary>MaterialType.Teak, the highest material with salvage.</summary>
        private const int HighestMaterial = 77;

        /// <summary>
        /// The units an item gives (GetStructure): 1 + workmanship / 2 rounded down, an odd workmanship having an even
        /// chance of one more. Its fewest and most.
        /// </summary>
        public static (int Min, int Max) Units(double workmanship) {
            var whole = (int)(workmanship <= 0 ? 1 : workmanship);
            var min = 1 + whole / 2;
            return (min, whole % 2 == 1 ? min + 1 : min);
        }

        /// <summary>The stack's tier (SalvageStacks.GetTier): the workmanship rounded, held between 1 and 10.</summary>
        public static int Tier(double workmanship) {
            var rounded = (int)Math.Round(workmanship <= 0 ? 1 : workmanship, MidpointRounding.ToEven);
            return Math.Max(MinTier, Math.Min(MaxTier, rounded));
        }

        /// <summary>
        /// Whether a weenie is one of the salvage stacks (SalvageStacks.IsStackWcid, for any material): Decal's
        /// ObjectClass doesn't call these stacks Salvage, so they're told by their weenie.
        /// </summary>
        public static bool IsStackWcid(int wcid) {
            return wcid >= BaseWcid + 10 && wcid < BaseWcid + (HighestMaterial + 1) * 10
                && MaterialNames.Of((wcid - BaseWcid) / 10) != null;
        }

        /// <summary>The weenie of the stack a material and tier go into (SalvageStacks.GetWcid), as Decal's Type gives it.</summary>
        public static int StackWcid(int material, int tier) {
            return BaseWcid + material * 10 + Math.Max(MinTier, Math.Min(MaxTier, tier)) - 1;
        }

        /// <summary>
        /// The new stacks units need once the stacks carried (stacks of them, carried units in all) are filled up to
        /// MaxStackSize, as Player.CountNewSalvageStacks counts them.
        /// </summary>
        public static int NewStacks(int carried, int stacks, int units) {
            if (units <= 0) {
                return 0;
            }

            var room = Math.Max(0, stacks * MaxStackSize - carried);
            var left = units - room;
            return left <= 0 ? 0 : (left + MaxStackSize - 1) / MaxStackSize;
        }

        /// <summary>
        /// The entries by material and tier, the units and stacks of each carried looked up by the stack's weenie
        /// (carried), in material name order then tier.
        /// </summary>
        public static List<SalvageGroup> Groups(IEnumerable<SalvageEntry> entries, Func<int, (int Units, int Stacks)> carried) {
            var groups = new List<SalvageGroup>();

            foreach (var g in entries.Where(e => e.Material != 0).GroupBy(e => (e.Material, Tier: Tier(e.Workmanship)))) {
                var have = carried?.Invoke(StackWcid(g.Key.Material, g.Key.Tier)) ?? (0, 0);
                groups.Add(new SalvageGroup {
                    Material = g.Key.Material,
                    Tier = g.Key.Tier,
                    Items = g.Count(),
                    MinUnits = g.Sum(e => Units(e.Workmanship).Min),
                    MaxUnits = g.Sum(e => Units(e.Workmanship).Max),
                    Carried = have.Units,
                    CarriedStacks = have.Stacks,
                    Value = g.Sum(e => e.Value),
                });
            }

            return groups
                .OrderBy(g => MaterialNames.Of(g.Material) ?? "")
                .ThenBy(g => g.Tier)
                .ToList();
        }
    }
}
