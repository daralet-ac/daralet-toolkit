using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's trade window and its salvage panel (an Ust's) as places to let go of an item from the plugin's lists,
    /// as one from the game's own pack: it goes in the trade (Actions.TradeAdd) or on the panel (Actions.SalvagePanelAdd),
    /// not on the ground. A pack let go over the salvage panel puts in its salvageable items of one material, the next
    /// material each time it's let go there again.
    ///
    /// A plugin can't ask which panel the game is showing. A trade is followed from Decal (WorldFilter.EnterTrade and
    /// EndTrade), and while one's open, an item let go anywhere in the game (but the shortcut bar) goes in it: dropping one
    /// on the ground mid-trade is never what was meant, and one put in the trade by mistake comes out again with the trade
    /// window's Clear. The salvage panel shows in the panel along the bottom (ClientElements.Environment), where the vendor,
    /// chest and corpse windows do: while that's shown with no vendor, trade or container open, and you carry an Ust, it's
    /// taken to be the salvage panel. [drop] lines in log.txt say which was decided.
    /// </summary>
    internal static class PanelDrop {
        private static readonly TimeSpan UstCheckInterval = TimeSpan.FromSeconds(1);

        private static bool started;
        private static bool trading;
        private static PlayerFeed feed;

        private static bool carriesUst;
        private static DateTime ustCheckedAt = DateTime.MinValue;

        /// <summary>What was put on the salvage panel while it's been open, so a pack let go there again adds the next material.</summary>
        private static readonly HashSet<int> onUst = new HashSet<int>();

        public static bool Trading => trading;

        public static void Start(PlayerFeed playerFeed) {
            feed = playerFeed;

            if (started) {
                return;
            }

            started = true;
            var world = CoreManager.Current.WorldFilter;
            world.EnterTrade += World_EnterTrade;
            world.EndTrade += World_EndTrade;
        }

        public static void Stop() {
            if (!started) {
                return;
            }

            started = false;
            var world = CoreManager.Current.WorldFilter;
            world.EnterTrade -= World_EnterTrade;
            world.EndTrade -= World_EndTrade;
        }

        /// <summary>Whether an item let go at a spot goes in the trade or on the salvage panel (not the shortcut bar's).</summary>
        public static bool Takes(Vector2 at) {
            return trading || OverUst(at);
        }

        /// <summary>
        /// Puts an item (or a pack's items) in the trade or on the salvage panel, whichever Takes was for. False when
        /// neither is open there, so the drop goes on into the world.
        /// </summary>
        public static bool Drop(int id, Vector2 at) {
            if (trading) {
                PluginCore.LogFile($"[drop] 0x{id:X8} put in the trade");
                CoreManager.Current.Actions.TradeAdd(id);
                return true;
            }

            if (!OverUst(at)) {
                return false;
            }

            if (Carried.IsPack(id)) {
                AddPackToUst(id);
            }
            else {
                AddToUst(id);
            }

            return true;
        }

        /// <summary>Every frame: forgets what was put on the salvage panel once the panel along the bottom is gone.</summary>
        public static void Step() {
            if (onUst.Count > 0 && !ClientElements.TryGetScreenBox(ClientElements.Environment, ClientElements.Environment,
                    out _, out _)) {
                onUst.Clear();
            }
        }

        /// <summary>A spot on the panel along the bottom while it's taken to be showing the salvage panel.</summary>
        public static bool OverUst(Vector2 at) {
            var actions = CoreManager.Current.Actions;

            if (trading || actions.VendorId != 0 || actions.OpenedContainer != 0 || !CarriesUst()) {
                return false;
            }

            return ClientElements.IsOver(ClientElements.Environment, at);
        }

        private static void AddToUst(int id) {
            var item = CoreManager.Current.WorldFilter[id];

            if (item == null || !ItemChecks.Salvageable(Carried.Facts(item, feed))) {
                PluginCore.Chat($"{item?.Name ?? "That"} can't be salvaged.");
                return;
            }

            PluginCore.LogFile($"[drop] 0x{id:X8} put on the salvage panel");
            CoreManager.Current.Actions.SalvagePanelAdd(id);
            onUst.Add(id);
        }

        /// <summary>The pack's salvageable items of one material: the first one's not yet on the panel.</summary>
        private static void AddPackToUst(int pack) {
            var items = Carried.In(pack);
            var facts = items.Select(o => Carried.Facts(o, feed)).ToList();
            var next = ItemChecks.NextMaterial(facts, onUst);

            if (next.Count == 0) {
                PluginCore.Chat(onUst.Count > 0 && facts.Any(ItemChecks.Salvageable)
                    ? "Everything salvageable in that pack is on the salvage panel already."
                    : "Nothing in that pack can be salvaged.");
                return;
            }

            foreach (var item in next) {
                CoreManager.Current.Actions.SalvagePanelAdd(item.Id);
                onUst.Add(item.Id);
            }

            var otherKinds = ItemChecks.NextMaterial(facts, onUst).Count > 0;

            PluginCore.LogFile($"[drop] pack 0x{pack:X8}: {next.Count} of material {next[0].Material} put on the salvage panel");
            PluginCore.Chat($"Put {Words.Count(next.Count, "item")} on the salvage panel ({MaterialName(next[0].Id)})"
                + (otherKinds ? ". Drop the pack there again for the next material." : "."));
        }

        /// <summary>An item's material in words, once the server's described it, else "the same material as" its name.</summary>
        private static string MaterialName(int id) {
            if (feed != null && feed.Described.TryGetValue(unchecked((uint)id), out var described)
                && !string.IsNullOrEmpty(described.Material)) {
                return described.Material;
            }

            return "the same material as " + (CoreManager.Current.WorldFilter[id]?.Name ?? "the first");
        }

        /// <summary>Whether you carry an Ust (or wear one, as the server's FindUst counts): checked once a second at most.</summary>
        private static bool CarriesUst() {
            if (DateTime.UtcNow - ustCheckedAt < UstCheckInterval) {
                return carriesUst;
            }

            ustCheckedAt = DateTime.UtcNow;
            carriesUst = Carried.Items().Any(Carried.IsUst);
            return carriesUst;
        }

        private static void World_EnterTrade(object sender, EnterTradeEventArgs e) {
            try {
                trading = true;
                PluginCore.LogFile($"[drop] trade opened (0x{e.TraderId:X8} with 0x{e.TradeeId:X8})");
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private static void World_EndTrade(object sender, EndTradeEventArgs e) {
            try {
                trading = false;
                PluginCore.LogFile("[drop] trade closed");
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }
    }
}
