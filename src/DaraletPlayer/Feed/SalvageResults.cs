using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DaraletPlayer.Feed {
    /// <summary>One item salvaged, as the server's line about it says.</summary>
    public class SalvagedItem {
        public int Material;
        public string Item;
        public int Tier;
        public int Units;
        /// <summary>Added to a stack you carried already (" -> combined with ...").</summary>
        public bool Combined;
        /// <summary>Its new stack went in your salvage crate (" -> moved to Salvage Crate").</summary>
        public bool InCrate;
    }

    /// <summary>What a run of the Salvage window's salvaging made: each item, and the units by material and tier.</summary>
    public class SalvageReport {
        public readonly List<SalvagedItem> Items = new List<SalvagedItem>();
        public int BezelFragments;
        /// <summary>The server's refusal, "You do not have enough pack space...", or null.</summary>
        public string Refused;
        public DateTime Started;

        public int Units => Items.Sum(i => i.Units);

        /// <summary>Units by material and tier, in material name order then tier.</summary>
        public List<(int Material, int Tier, int Items, int Units)> Groups() {
            return Items
                .GroupBy(i => (i.Material, i.Tier))
                .Select(g => (g.Key.Material, g.Key.Tier, g.Count(), g.Sum(i => i.Units)))
                .OrderBy(g => MaterialNames.Of(g.Item1) ?? "")
                .ThenBy(g => g.Item2)
                .ToList();
        }

        /// <summary>Takes in a chat line if it's one of salvaging's; true when it was.</summary>
        public bool Read(string line) {
            if (SalvageResults.TryParse(line, out var item)) {
                Items.Add(item);
                return true;
            }

            var bezels = SalvageResults.Bezels(line);

            if (bezels > 0) {
                BezelFragments += bezels;
                return true;
            }

            if (SalvageResults.IsRefusal(line)) {
                Refused = line.Trim();
                return true;
            }

            return false;
        }
    }

    /// <summary>
    /// The lines daralet-ACE's Player.HandleSalvaging writes in chat (Broadcast), one an item salvaged:
    /// "Black Garnet Gold Ring (W5 | 3 units)", with " -> combined with Black Garnet Salvage (W5 | 120 units)" or
    /// " -> moved to Salvage Crate" after; "You obtain 2 Bezel Fragments." for sockets; and, when it salvages nothing,
    /// "You do not have enough pack space for the salvage. You need 3 free slots." The material is MaterialType's name
    /// spaced out, which MaterialNames spells the same; it's told from the item's name by being the longest known one in
    /// front. A player squelching Salvaging gets none of the item lines.
    /// </summary>
    public static class SalvageResults {
        private static readonly Regex ItemLine = new Regex(
            @"^(?<rest>.+?) \(W(?<tier>\d+) \| (?<units>\d+) units?\)(?<suffix>.*)$", RegexOptions.Compiled);

        private static readonly Regex BezelLine = new Regex(@"^You obtain (?<count>\d+) Bezel Fragments?\.?$",
            RegexOptions.Compiled);

        public static bool TryParse(string line, out SalvagedItem item) {
            item = null;

            if (string.IsNullOrEmpty(line)) {
                return false;
            }

            var match = ItemLine.Match(line.Trim());

            if (!match.Success) {
                return false;
            }

            var rest = match.Groups["rest"].Value;
            var suffix = match.Groups["suffix"].Value.Trim();

            if (suffix.Length > 0 && !suffix.StartsWith("->", StringComparison.Ordinal)) {
                return false;
            }

            var material = 0;
            var name = rest;

            for (var words = rest.Split(' ').Length - 1; words >= 1 && material == 0; words--) {
                var front = string.Join(" ", rest.Split(' ').Take(words));
                material = MaterialNames.Number(front);

                if (material != 0) {
                    name = rest.Substring(front.Length).Trim();
                }
            }

            if (material == 0) {
                return false;
            }

            item = new SalvagedItem {
                Material = material,
                Item = name,
                Tier = int.Parse(match.Groups["tier"].Value),
                Units = int.Parse(match.Groups["units"].Value),
                Combined = suffix.StartsWith("-> combined", StringComparison.Ordinal),
                InCrate = suffix.StartsWith("-> moved to Salvage Crate", StringComparison.Ordinal),
            };
            return true;
        }

        public static int Bezels(string line) {
            var match = BezelLine.Match(line?.Trim() ?? "");
            return match.Success ? int.Parse(match.Groups["count"].Value) : 0;
        }

        public static bool IsRefusal(string line) {
            return line != null && line.Trim().StartsWith("You do not have enough pack space for the salvage",
                StringComparison.Ordinal);
        }
    }
}
