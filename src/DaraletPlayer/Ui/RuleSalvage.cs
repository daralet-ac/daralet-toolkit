using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Salvage on the Inventory windows: salvages what you carry that your active loot profile says to Salvage, the rule
    /// deciding each item being the first that matches it (Loot.Decide), so a Keep rule above a Salvage one keeps the item.
    /// Every item is described by the server first (the rules need its stats), then a popup lists what will go, and only
    /// its Salvage button salvages, with @playerui salvage (the server's own salvaging with your Ust: new salvage fills
    /// bags of the same kind you carry first, and new bags go in your salvage crate if you carry one). Afterwards, if you
    /// carry two bags of the same material and workmanship, /salvage combine pours them together (the server asks first).
    /// </summary>
    internal class RuleSalvage {
        private const string PopupId = "Salvage by your rules##rule-salvage";

        /// <summary>The most @playerui salvage takes at once.</summary>
        private const int MaxPerRequest = 20;

        /// <summary>How long the server has to describe everything before what's described is gone on with.</summary>
        private static readonly TimeSpan CheckTimeout = TimeSpan.FromSeconds(20);

        /// <summary>
        /// Between salvage requests: the server ignores one of the same kind sooner than 400ms after the last
        /// (PlayerUiLimits), and the plugin never asks sooner than 500ms (PlayerFeed.MinInterval).
        /// </summary>
        private static readonly TimeSpan SendGap = TimeSpan.FromMilliseconds(700);

        /// <summary>How long after the last salvage request the bags are looked over for any to combine.</summary>
        private static readonly TimeSpan CombineDelay = TimeSpan.FromSeconds(3);

        private const string CombineCommand = "/salvage combine";

        private readonly PlayerFeed feed;
        private readonly Settings settings;

        private enum Stage { Idle, Checking, Confirm }

        private Stage stage;
        private List<uint> checking = new List<uint>();
        private DateTime checkStarted;

        /// <summary>The window whose Salvage was clicked, which draws the popup.</summary>
        private string host;
        private bool openPending;

        /// <summary>When the popup was last drawn: a window closed with it open would otherwise leave Salvage waiting.</summary>
        private DateTime popupSeenAt;

        private readonly List<(uint Guid, string Name, string Rule)> picks = new List<(uint, string, string)>();
        private int leftMarkedForSale;
        private bool combineAfter = true;

        private readonly Queue<uint[]> batches = new Queue<uint[]>();
        private DateTime nextSendAt;
        private DateTime? combineAt;

        public RuleSalvage(PlayerFeed feed, Settings settings) {
            this.feed = feed;
            this.settings = settings;
        }

        private bool Busy => stage != Stage.Idle || batches.Count > 0;

        /// <summary>The Salvage button. Host names the window it's in, which draws the popup (RenderPopup).</summary>
        public void RenderButton(string host, bool small) {
            var supported = feed.Supports(PlayerFeed.KindSalvage) && feed.Supports(PlayerFeed.KindDescribe);
            var label = stage == Stage.Checking
                ? $"Salvage ({checking.Count(Answered)}/{checking.Count})...##rule-salvage-{host}"
                : $"Salvage##rule-salvage-{host}";

            ImGui.BeginDisabled(!supported || Busy);
            var clicked = small ? ImGui.SmallButton(label) : ImGui.Button(label);
            ImGui.EndDisabled();

            Widgets.Tip(!supported
                ? "This server is too old for salvaging from here."
                : "Salvages what you carry that your active loot profile says to Salvage: the first of its rules that "
                    + "matches an item decides, as in looting, so a rule above that keeps the item keeps it. Asks the server "
                    + "about every item first, then shows what will go before anything's salvaged. Needs an Ust in your "
                    + "packs. Items marked for sale are left alone.");

            if (clicked) {
                Begin(host);
            }
        }

        private void Begin(string fromHost) {
            host = fromHost;
            checking = Carried.Items()
                .Where(o => o.ObjectClass != ObjectClass.Salvage && o.Values(LongValueKey.StackMax, 0) <= 1)
                .Select(o => unchecked((uint)o.Id))
                .ToList();

            if (checking.Count == 0) {
                PluginCore.Chat("You carry nothing that could be salvaged.");
                return;
            }

            stage = Stage.Checking;
            checkStarted = DateTime.UtcNow;
            PluginCore.LogFile($"[salvage] checking {checking.Count} carried items against the loot rules");
        }

        /// <summary>The server has said what it can about the item: described, or not described after every try.</summary>
        private bool Answered(uint guid) {
            var state = feed.DescribeStateOf(guid);
            return feed.Described.ContainsKey(guid) || state == DescribeState.NotYours || state == DescribeState.NoAnswer;
        }

        /// <summary>Every frame: asks for descriptions until all are in, then sends the salvage a request at a time.</summary>
        public void Step() {
            if (stage == Stage.Checking) {
                StepChecking();
            }

            if (stage == Stage.Confirm && !openPending && DateTime.UtcNow - popupSeenAt > TimeSpan.FromSeconds(2)) {
                stage = Stage.Idle;
            }

            if (batches.Count > 0 && DateTime.UtcNow >= nextSendAt) {
                var batch = batches.Dequeue();
                feed.SendCommand("@playerui salvage " + Display.TargetArguments(batch));
                nextSendAt = DateTime.UtcNow + SendGap;

                if (batches.Count == 0 && combineAfter) {
                    combineAt = DateTime.UtcNow + CombineDelay;
                }
            }

            if (combineAt.HasValue && DateTime.UtcNow >= combineAt.Value) {
                combineAt = null;
                CombineIfAny();
            }
        }

        private void StepChecking() {
            foreach (var guid in checking) {
                feed.Describe(guid);
            }

            if (!checking.All(Answered) && DateTime.UtcNow - checkStarted < CheckTimeout) {
                return;
            }

            var list = settings.Data.Loot.ActiveFor(CoreManager.Current.CharacterFilter.Name);
            picks.Clear();
            leftMarkedForSale = 0;

            foreach (var guid in checking) {
                if (!feed.Described.TryGetValue(guid, out var described) || !described.Salvageable) {
                    continue;
                }

                var decision = Loot.Decide(list, described);

                if (decision.Outcome != LootOutcome.Salvage) {
                    continue;
                }

                if (VendorSale.IsMarked(settings, unchecked((int)guid))) {
                    leftMarkedForSale++;
                    continue;
                }

                var name = CoreManager.Current.WorldFilter[unchecked((int)guid)]?.Name ?? described.Name ?? "?";
                picks.Add((guid, name, decision.Rule?.Name ?? ""));
            }

            var unanswered = checking.Count(g => !feed.Described.ContainsKey(g));
            PluginCore.LogFile($"[salvage] {picks.Count} to salvage by the rules, {unanswered} not described, "
                + $"{leftMarkedForSale} marked for sale left out");

            if (picks.Count == 0) {
                stage = Stage.Idle;
                PluginCore.Chat("Nothing you carry is for your loot profile's Salvage rules"
                    + (unanswered > 0 ? $" ({Words.Count(unanswered, "item")} the server didn't describe)." : "."));
                return;
            }

            stage = Stage.Confirm;
            openPending = true;
            popupSeenAt = DateTime.UtcNow;
        }

        /// <summary>
        /// The popup listing what will be salvaged, in the window whose Salvage was clicked. Called every frame at that
        /// window's own level, where popups are found by ID.
        /// </summary>
        public void RenderPopup(string fromHost) {
            if (fromHost != host) {
                return;
            }

            if (openPending) {
                openPending = false;
                ImGui.OpenPopup(PopupId);
            }

            // a plain popup, not a modal, which greys out the whole screen; a click outside it cancels
            if (!ImGui.BeginPopup(PopupId, ImGuiWindowFlags.AlwaysAutoResize)) {
                if (stage == Stage.Confirm && !openPending) {
                    stage = Stage.Idle;
                }

                return;
            }

            popupSeenAt = DateTime.UtcNow;
            var hasUst = Carried.Items().Any(Carried.IsUst);

            Widgets.Text($"Salvage {Words.Count(picks.Count, "item")}?");
            Widgets.TextDisabled("Your loot profile's Salvage rules are the first to match these. They can't be got back.");

            var em = ImGui.GetFontSize();
            var height = Math.Min(picks.Count, 12) * ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().WindowPadding.Y * 2;

            if (Widgets.BeginChild("rule-salvage-list", new Vector2(em * 24, height), true)) {
                foreach (var (_, name, rule) in picks) {
                    Widgets.Text(name);

                    if (rule.Length > 0) {
                        ImGui.SameLine();
                        Widgets.TextDisabled($"({rule})");
                    }
                }
            }
            ImGui.EndChild();

            if (leftMarkedForSale > 0) {
                Widgets.TextDisabled($"{Words.Count(leftMarkedForSale, "item")} marked for sale left alone.");
            }

            ImGui.Checkbox("Combine like bags afterwards", ref combineAfter);
            Widgets.Tip($"If you then carry two salvage bags of the same material and workmanship, sends {CombineCommand}, "
                + "which pours them into one (the server asks you first). Salvaging itself already tops up the bags you "
                + "carry and puts new ones in your salvage crate, if you carry one.");

            if (!hasUst) {
                Widgets.TextColored(Colors.Warning, "You don't carry an Ust, which salvaging needs.");
            }

            if (Widgets.Button("Salvage", hasUst, "Salvages every item listed, with the Ust in your packs.")) {
                Confirm();
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel")) {
                stage = Stage.Idle;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        private void Confirm() {
            stage = Stage.Idle;

            // anything that's gone or been moved out of your packs since the list was made stays out
            var guids = picks.Select(p => p.Guid).Where(g => Carried.Has(unchecked((int)g))).ToList();
            PluginCore.LogFile($"[salvage] salvaging {guids.Count} items");

            for (var start = 0; start < guids.Count; start += MaxPerRequest) {
                batches.Enqueue(guids.Skip(start).Take(MaxPerRequest).ToArray());
            }

            nextSendAt = DateTime.UtcNow;
        }

        /// <summary>Sends /salvage combine when you carry two bags of the same material and workmanship.</summary>
        private void CombineIfAny() {
            var bags = Carried.Items().Where(o => o.ObjectClass == ObjectClass.Salvage);
            var duplicates = bags
                .GroupBy(o => (o.Values(LongValueKey.Material, 0),
                    (int)Math.Round(o.Values(DoubleValueKey.SalvageWorkmanship, 1.0))))
                .Any(g => g.Count() > 1);

            PluginCore.LogFile($"[salvage] bags to combine: {duplicates}");

            if (duplicates) {
                feed.SendCommand(CombineCommand);
            }
        }
    }
}
