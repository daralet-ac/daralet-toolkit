using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The plugin's own Ust panel: a window of slots, as the game's salvage panel has, that items are dragged onto to be
    /// salvaged, and what they'll make. Items come from the plugin's lists, the game's own pack (GameDrag) or a whole
    /// pack, the main pack too (every item in it that can be salvaged). Salvage sends them with @playerui salvage, the
    /// server's own salvaging with the Ust you carry (Player.HandleSalvaging, which checks everything again), 20 a request.
    /// Unlike the game's panel it takes any mix of materials at once: the server's salvaging does.
    ///
    /// Every item is described by the server as it goes on (PlayerFeed.Describe), and one it says can't be salvaged
    /// (retained, unstable, no material) is taken off again with a line in chat; Salvage waits until they're all answered.
    /// It opens when the game's salvage panel does (PanelDrop.GamePanelShowing, a guess) and, in place of it, when an Ust
    /// is used from the plugin (TakesUse). `[salvage]` lines in log.txt.
    /// </summary>
    internal sealed partial class SalvagePanel : IDisposable {
        /// <summary>The most @playerui salvage takes at once.</summary>
        private const int MaxPerRequest = 20;

        /// <summary>Between salvage requests: the server ignores one of the same kind sooner than 400ms after the last.</summary>
        private static readonly TimeSpan SendGap = TimeSpan.FromMilliseconds(700);

        /// <summary>The one made, for ItemUser's Ust and the main pack's drag, which are static.</summary>
        private static SalvagePanel current;

        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly ItemIcons icons;

        /// <summary>What's on the panel, in the order it went on: ids only, never Decal's WorldObjects (see CLAUDE.md).</summary>
        private readonly List<int> queued = new List<int>();

        private readonly Queue<int[]> batches = new Queue<int[]>();
        private DateTime nextSendAt;

        /// <summary>Whether the game's salvage panel looked up last frame, so it's opening is seen once.</summary>
        private bool gamePanelWasUp;

        /// <summary>
        /// Whether the window's open: only from an Ust's use (TakesUse) or the game's salvage panel opening, until Close.
        /// A new number each time it opens, for FloatingWindow's inUse, which keeps a closed window away until it changes.
        /// </summary>
        private int openedAs;
        private bool isOpen;

        /// <summary>Opened with the game's panel, so it closes when that does.</summary>
        private bool openedWithGame;

        /// <summary>The window's box last frame, for the main pack's drag, which is drawn before the window.</summary>
        private Vector2 boxMin;
        private Vector2 boxMax;
        private int boxFrame = -10;

        public SalvagePanel(PlayerFeed feed, Settings settings, ItemIcons icons) {
            this.feed = feed;
            this.settings = settings;
            this.icons = icons;
            Window = new FloatingWindow("salvage", "Salvage", settings, new Vector2(40, 420), RenderWindow,
                defaultSize: new Vector2(620, 240), minSize: new Vector2(300, 140),
                inUse: () => isOpen ? (object)openedAs : null, alwaysOnlyInUse: true, onClose: () => CloseWindow());

            // only ever shown while open (inUse): an Ust's use is what opens it
            Window.Visible = true;
            CoreManager.Current.ChatBoxMessage += Core_ChatBoxMessage;
            current = this;
        }

        public FloatingWindow Window { get; }

        public void Dispose() {
            // the game's panel back first, whatever fails after
            try {
                PutBack();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }

            CoreManager.Current.ChatBoxMessage -= Core_ChatBoxMessage;

            if (current == this) {
                current = null;
            }

            Window.Dispose();
        }

        /// <summary>
        /// Whether a pack's items (the main pack's, dragged as MainPackDragType) let go here would go on the panel: the
        /// window as it was last drawn is under the mouse.
        /// </summary>
        public static bool TakesAt(Vector2 at) {
            var panel = current;
            return panel != null && ImGui.GetFrameCount() - panel.boxFrame <= 1
                && at.X >= panel.boxMin.X && at.Y >= panel.boxMin.Y && at.X < panel.boxMax.X && at.Y < panel.boxMax.Y;
        }

        /// <summary>
        /// An Ust used from the plugin (its lists, a hotbar): the Salvage window opens in place of the game's panel, when
        /// that's set. True when it took the use.
        /// </summary>
        public static bool TakesUse(WorldObject item) {
            var panel = current;

            if (panel == null || item == null || !Carried.IsUst(item) || !panel.settings.Data.UstOpensSalvageWindow) {
                return false;
            }

            PluginCore.LogFile($"[salvage] the {item.Name} used from the plugin: the Salvage window opened");
            panel.Open(fromGame: false);
            return true;
        }

        /// <summary>Opens the window, from an Ust's use (fromGame: the game's own panel opening).</summary>
        public void Open(bool fromGame) {
            if (!isOpen) {
                openedAs++;
            }

            isOpen = true;
            openedWithGame = fromGame;
            // Hide in its menu switches it off; an Ust used means it's wanted
            Window.Visible = true;
            Window.BringToFront();
        }

        /// <summary>Every frame once logged in: follows the game's panel, tidies the list, and sends what's to be salvaged.</summary>
        public void Step() {
            var gamePanelUp = PanelDrop.GamePanelShowing();

            if (gamePanelUp && !gamePanelWasUp && (settings.Data.SalvageOpensWithGame || settings.Data.SalvageReplace)) {
                PluginCore.LogFile("[salvage] the game's salvage panel looks open: the Salvage window opened");
                Open(fromGame: true);
            }

            // the game's panel closed (or a vendor, trade or container took its place): this goes with it
            if (!gamePanelUp && gamePanelWasUp && isOpen && openedWithGame) {
                PluginCore.LogFile("[salvage] the game's salvage panel closed: the Salvage window closed");
                CloseWindow();
            }

            gamePanelWasUp = gamePanelUp;
            HideFrame();
            Tidy();
            StepRules();
            StepReport();

            if (batches.Count > 0 && DateTime.UtcNow >= nextSendAt) {
                var batch = batches.Dequeue();
                feed.SendCommand("@playerui salvage " + string.Join(" ", batch.Select(id => $"0x{id:X8}")));
                nextSendAt = DateTime.UtcNow + SendGap;
            }
        }

        /// <summary>
        /// Takes off the panel what's gone from your packs (salvaged, moved out, worn) and what the server says can't be
        /// salvaged, and asks about the rest.
        /// </summary>
        private void Tidy() {
            for (var i = queued.Count - 1; i >= 0; i--) {
                var id = queued[i];
                var guid = unchecked((uint)id);

                if (!Carried.Has(id)) {
                    queued.RemoveAt(i);
                    continue;
                }

                if (feed.Described.TryGetValue(guid, out var described)) {
                    if (!described.Salvageable) {
                        queued.RemoveAt(i);
                        var name = CoreManager.Current.WorldFilter[id]?.Name ?? described.Name ?? "That item";
                        PluginCore.Chat($"{name} can't be salvaged (retained, unstable or of no material): taken off "
                            + "the Salvage window.");
                        PluginCore.LogFile($"[salvage] 0x{id:X8} taken off: the server says it can't be salvaged");
                    }

                    continue;
                }

                feed.Describe(guid);
            }
        }

        /// <summary>The queued items the server has said can be salvaged.</summary>
        private List<int> Confirmed() {
            return queued.Where(id => feed.Described.TryGetValue(unchecked((uint)id), out var d) && d.Salvageable).ToList();
        }

        /// <summary>Queued items the server hasn't answered for yet.</summary>
        private int Checking() {
            return queued.Count(id => !feed.Described.ContainsKey(unchecked((uint)id)));
        }

        private void SalvageAll() {
            var ids = Confirmed();

            if (ids.Count == 0) {
                return;
            }

            PluginCore.LogFile($"[salvage] the Salvage window salvaging {ids.Count} items");
            StartReport();

            for (var start = 0; start < ids.Count; start += MaxPerRequest) {
                batches.Enqueue(ids.Skip(start).Take(MaxPerRequest).ToArray());
            }

            nextSendAt = DateTime.UtcNow;
        }

        private bool Sending => batches.Count > 0;

        // ---- adding ----

        /// <summary>
        /// Whether something can go on the panel: true, or false with why not. A pack can when anything in it can.
        /// </summary>
        private (bool Ok, string Why) Verdict(int id) {
            var world = CoreManager.Current.WorldFilter;
            var me = CoreManager.Current.CharacterFilter.Id;

            if (id == me) {
                return Carried.In(me).Any(o => ItemVerdict(o).Ok) ? (true, null) : (false, "Nothing in it can be salvaged.");
            }

            var item = world[id];

            if (item == null) {
                return (false, null);
            }

            if (item.ObjectClass == ObjectClass.Container) {
                return HotbarItems.IsMine(item) && Carried.In(id).Any(o => ItemVerdict(o).Ok)
                    ? (true, null)
                    : (false, "Nothing in it can be salvaged.");
            }

            return ItemVerdict(item);
        }

        private (bool Ok, string Why) ItemVerdict(WorldObject item) {
            if (!HotbarItems.IsMine(item)) {
                return (false, "Only what you carry can be salvaged.");
            }

            if (HotbarItems.IsWorn(item)) {
                return (false, "Take it off first.");
            }

            if (queued.Contains(item.Id)) {
                return (false, "It's on the panel already.");
            }

            if (feed.Described.TryGetValue(unchecked((uint)item.Id), out var described)) {
                return described.Salvageable ? (true, null) : (false, "It can't be salvaged.");
            }

            return ItemChecks.Salvageable(Carried.Facts(item, feed)) ? (true, null) : (false, "It can't be salvaged.");
        }

        /// <summary>An item, or a pack's items (any pack you carry, the main pack by your own id), let go on the panel.</summary>
        private void Add(int id) {
            var me = CoreManager.Current.CharacterFilter.Id;

            if (id == 0) {
                return;
            }

            if (id == me || Carried.IsPack(id)) {
                AddPack(id);
                return;
            }

            var item = CoreManager.Current.WorldFilter[id];
            var verdict = item == null ? (false, null) : ItemVerdict(item);

            if (!verdict.Ok) {
                if (verdict.Why != null) {
                    PluginCore.Chat($"{item?.Name ?? "That"}: {verdict.Why}");
                }

                return;
            }

            queued.Add(id);
            feed.Describe(unchecked((uint)id));
            PluginCore.LogFile($"[salvage] 0x{id:X8} ({item.Name}) put on the Salvage window");
        }

        /// <summary>
        /// Every item in the pack that can be salvaged, whatever its material. Items marked for sale are left out, as
        /// Salvage on the Inventory windows leaves them.
        /// </summary>
        private void AddPack(int pack) {
            var me = CoreManager.Current.CharacterFilter.Id;
            var items = Carried.In(pack);
            var added = 0;
            var forSale = 0;

            foreach (var item in items) {
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

            var packName = pack == me ? "your main pack" : CoreManager.Current.WorldFilter[pack]?.Name ?? "that pack";
            PluginCore.LogFile($"[salvage] pack 0x{pack:X8}: {added} put on the Salvage window, {forSale} marked for sale left");

            if (added == 0) {
                PluginCore.Chat(forSale > 0 ? $"Everything salvageable in {packName} is marked for sale."
                    : $"Nothing in {packName} can be salvaged.");
                return;
            }

            PluginCore.Chat($"Put {Words.Count(added, "item")} from {packName} on the Salvage window"
                + (forSale > 0 ? $" ({Words.Count(forSale, "item")} marked for sale left out)." : "."));
        }

        private void Remove(int id) {
            queued.Remove(id);
        }

        // ---- settings ----

        /// <summary>The window's section in the Inventory tab's Settings.</summary>
        public void RenderSettings() {
            Form.Section("Salvage window");
            Window.RenderControls("The plugin's Ust panel: drag items or whole packs onto it, see what they'll make, and "
                + "salvage them with the Ust in your packs. It opens only when you use an Ust, and Close closes it.",
                showToggle: false);

            var first = true;

            if (Form.FlowCheck(ref first, "Open with the game's##salvage-settings", ref settings.Data.SalvageOpensWithGame,
                "Opens the Salvage window when the game's salvage panel opens (you used your Ust in the game).")) {
                settings.Changed();
            }

            if (Form.FlowCheck(ref first, "Hide salvage lines##salvage-settings", ref settings.Data.SalvageHideChatLines,
                "Keeps the server's line about each item salvaged out of the chat, leaving the Salvage window's one line "
                + "summing them up. Only lines from the Salvage window's own salvaging are hidden.")) {
                settings.Changed();
            }

            if (Form.FlowCheck(ref first, "Ust opens it##salvage-settings", ref settings.Data.UstOpensSalvageWindow,
                "Using an Ust from the plugin's lists or a hotbar opens the Salvage window instead of the game's panel.")) {
                settings.Changed();
            }
        }
    }
}
