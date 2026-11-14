using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>The Spellbook window's own choices (Ui/SpellbookPanel.cs), in settings.json as "spellbook".</summary>
    [DataContract]
    public class SpellbookOptions {
        public const string SpellsTab = "spells";
        public const string ComponentsTab = "components";

        /// <summary>Which page shows: your spells, or the components you carry.</summary>
        [DataMember(Name = "tab")] public string Tab;
        /// <summary>Spells whose name has this in it, case aside; empty for all.</summary>
        [DataMember(Name = "search")] public string Search;
        /// <summary>Schools of magic left out, by name (SpellInfo.School).</summary>
        [DataMember(Name = "hiddenSchools")] public List<string> HiddenSchools;
        /// <summary>Every spell component on the Components page, not only those you carry, buy or the picked spell takes.</summary>
        [DataMember(Name = "allComponents")] public bool AllComponents;
        /// <summary>Spell levels left out, 1 to 7.</summary>
        [DataMember(Name = "hiddenLevels")] public List<int> HiddenLevels;
        /// <summary>The list by level, then name; off, by name alone, as the game's spellbook has it.</summary>
        [DataMember(Name = "byLevel")] public bool ByLevel;

        public SpellbookOptions() {
            SetDefaults();
        }

        /// <summary>Whether a spell shows under these filters: its school and level not left out, its name matching.</summary>
        public bool Shows(string name, string school, int level) {
            if (school != null && HiddenSchools != null && HiddenSchools.Contains(school)) {
                return false;
            }

            if (HiddenLevels != null && HiddenLevels.Contains(level)) {
                return false;
            }

            return string.IsNullOrWhiteSpace(Search)
                || (name ?? "").IndexOf(Search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Spells in the list's order: by name, as the game's spellbook, or with By level, by level then name.</summary>
        public List<T> Order<T>(IEnumerable<T> spells, Func<T, string> name, Func<T, int> level) {
            var byName = StringComparer.OrdinalIgnoreCase;
            return (ByLevel ? spells.OrderBy(level).ThenBy(name, byName) : spells.OrderBy(name, byName)).ToList();
        }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            Tab = SpellsTab;
            Search = "";
            HiddenSchools = new List<string>();
            HiddenLevels = new List<int>();
        }
    }
}
