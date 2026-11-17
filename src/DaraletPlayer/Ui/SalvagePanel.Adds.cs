using System;
using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Decal.Adapter.Wrappers;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The window's two quick adds. **By rules**: everything you carry that your active loot profile says to Salvage, the
    /// first rule that matches an item deciding as in looting (Loot.Decide), so a Keep rule above a Salvage one keeps the
    /// item. The rules need the server's description of each item, so every carried item is described first, then those
    /// added (as Salvage on the Inventory windows does). **All packs**: every item in your main pack and side packs that
    /// can be salvaged, but those in packs tagged Keep (BankCommands.KeepTag, the pack's inscription). Both leave out items
    /// marked for sale, and the server's word on each still takes off what can't be salvaged (Tidy).
    /// </summary>
    internal sealed partial class SalvagePanel {
        /// <summary>How long the server has to describe everything before what's described is gone on with.</summary>
        private static readonly TimeSpan RulesTimeout = TimeSpan.FromSeconds(20);

        /// <summary>What By rules is waiting on the server to describe; empty when it isn't.</summary>
        private List<uint> rulesChecking = new List<uint>();
        private DateTime rulesStarted;

        private bool CheckingRules => rulesChecking.Count > 0;

        /// <summary>"Salvage 12/40...": By rules' progress, for its button.</summary>
        private string RulesProgress => $"{rulesChecking.Count(RulesAnswered)}/{rulesChecking.Count}";

        private void AddByRules() {
            rulesChecking = Carried.Items()
                .Where(o => o.ObjectClass != ObjectClass.Salvage && !SalvageMath.IsStackWcid(o.Type)
                    && o.ObjectClass != ObjectClass.Container
                    && o.Values(LongValueKey.StackMax, 0) <= 1 && !queued.Contains(o.Id))
                .Select(o => unchecked((uint)o.Id))
                .ToList();
            rulesStarted = DateTime.UtcNow;

            if (rulesChecking.Count == 0) {
                PluginCore.Chat("You carry nothing more that could be salvaged.");
            }

            PluginCore.LogFile($"[salvage] By rules: checking {rulesChecking.Count} carried items");
        }

        private bool RulesAnswered(uint guid) {
            var state = feed.DescribeStateOf(guid);
            return feed.Described.ContainsKey(guid) || state == DescribeState.NotYours || state == DescribeState.NoAnswer;
        }

        /// <summary>Every frame: asks about each item until all are answered, then adds the ones the rules salvage.</summary>
        private void StepRules() {
            if (!CheckingRules) {
                return;
            }

            foreach (var guid in rulesChecking) {
                feed.Describe(guid);
            }

            if (!rulesChecking.All(RulesAnswered) && DateTime.UtcNow - rulesStarted < RulesTimeout) {
                return;
            }

            var list = settings.Data.Loot.ActiveFor(CoreManager.Current.CharacterFilter.Name);
            var added = 0;
            var forSale = 0;

            foreach (var guid in rulesChecking) {
                var id = unchecked((int)guid);

                if (!feed.Described.TryGetValue(guid, out var described) || !described.Salvageable
                    || Loot.Decide(list, described).Outcome != LootOutcome.Salvage) {
                    continue;
                }

                var item = CoreManager.Current.WorldFilter[id];

                if (item == null || !ItemVerdict(item).Ok) {
                    continue;
                }

                if (VendorSale.IsMarked(settings, id)) {
                    forSale++;
                    continue;
                }

                queued.Add(id);
                added++;
            }

            var unanswered = rulesChecking.Count(g => !feed.Described.ContainsKey(g));
            rulesChecking = new List<uint>();
            PluginCore.LogFile($"[salvage] By rules: {added} added, {forSale} marked for sale left out, {unanswered} not "
                + "described");

            var notes = new List<string>();

            if (forSale > 0) {
                notes.Add($"{Words.Count(forSale, "item")} marked for sale left out");
            }

            if (unanswered > 0) {
                notes.Add($"{Words.Count(unanswered, "item")} the server didn't describe");
            }

            var tail = notes.Count > 0 ? $" ({string.Join(", ", notes)})." : ".";
            PluginCore.Chat(added > 0
                ? $"Put {Words.Count(added, "item")} your loot profile's Salvage rules take on the Salvage window{tail}"
                : $"Nothing more you carry is for your loot profile's Salvage rules{tail}");
        }

        /// <summary>Every item in your packs that can be salvaged, but those in Keep packs and those marked for sale.</summary>
        private void AddAllPacks() {
            var me = CoreManager.Current.CharacterFilter.Id;
            var packs = new List<int> { me };
            var kept = 0;

            foreach (var pack in SidePacks()) {
                if (BankCommands.HasTag(Inscription(pack), BankCommands.KeepTag)) {
                    kept++;
                    continue;
                }

                packs.Add(pack.Id);
            }

            var added = 0;
            var forSale = 0;

            foreach (var item in packs.SelectMany(Carried.In)) {
                if (!ItemVerdict(item).Ok) {
                    continue;
                }

                if (VendorSale.IsMarked(settings, item.Id)) {
                    forSale++;
                    continue;
                }

                queued.Add(item.Id);
                feed.Describe(unchecked((uint)item.Id));
                added++;
            }

            PluginCore.LogFile($"[salvage] All packs: {added} added, {kept} Keep packs skipped, {forSale} marked for sale");
            var skipped = kept > 0 ? $", {Words.Count(kept, "Keep pack")} left alone" : "";
            var sale = forSale > 0 ? $", {Words.Count(forSale, "item")} marked for sale left out" : "";
            PluginCore.Chat(added > 0
                ? $"Put {Words.Count(added, "item")} from your packs on the Salvage window{skipped}{sale}."
                : $"Nothing more in your packs can be salvaged{skipped}{sale}.");
        }

        private static IEnumerable<WorldObject> SidePacks() {
            var me = CoreManager.Current.CharacterFilter.Id;
            return CoreManager.Current.WorldFilter.GetAll().Cast<WorldObject>()
                .Where(o => o.ObjectClass == ObjectClass.Container && o.Values(LongValueKey.Container, 0) == me);
        }

        /// <summary>A side pack's tags: the server's (the bank reply's carried packs) when it's sent them, else Decal's.</summary>
        private string Inscription(WorldObject pack) {
            var fromServer = feed.Bank?.Carried?.FirstOrDefault(p => p.Guid == unchecked((uint)pack.Id));
            return fromServer != null ? fromServer.Inscription ?? "" : pack.Values(StringValueKey.Inscription, "");
        }
    }
}
