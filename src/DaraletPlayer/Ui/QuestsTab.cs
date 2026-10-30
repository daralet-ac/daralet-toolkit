using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using DaraletPlayer.Map;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Quests tab: your contracts, what the game's Contracts panel lists (@playerui quests), as quests. A list on top,
    /// what you're doing first, with each one's stage, progress and timers; under it the picked quest's description, who
    /// starts it and who takes it back, and where they are, each a destination for the World tab's arrow.
    /// </summary>
    /// <remarks>The Quest Tracker window is in <c>QuestsTab.Tracker.cs</c>.</remarks>
    internal partial class QuestsTab : IDisposable {
        /// <summary>Quick enough to follow a kill count; it's only asked for while the tab or the window is open.</summary>
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

        private static readonly (string Value, string Label)[] Shows = {
            ("all", "All"), ("started", "Started"), ("available", "Not started"), ("done", "Done"),
        };

        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly Action<string, float, float> goTo;

        /// <summary>The quests in a window of their own: the same list, search and picked quest as the tab's.</summary>
        private readonly FloatingWindow window;

        /// <summary>The quests you're doing, each opening out to its details, in a small window that sizes itself.</summary>
        private readonly FloatingWindow tracker;

        private string search = "";
        private string show = "all";
        private uint selected;

        /// <summary>The quest whose Abandon was pressed once, waiting for Yes.</summary>
        private uint abandoning;

        /// <param name="goTo">Makes a spot the World tab's destination: a name, then map x (east) and y (north).</param>
        public QuestsTab(PlayerFeed feed, Settings settings, Action<string, float, float> goTo) {
            this.feed = feed;
            this.settings = settings;
            this.goTo = goTo;

            window = new FloatingWindow("quests", "Quests", settings, new Vector2(380, 120), RenderQuests,
                visibleByDefault: false, defaultSize: new Vector2(560, 520));
            tracker = new FloatingWindow("quest-tracker", "Quest Tracker", settings, new Vector2(20, 420), RenderTracker,
                visibleByDefault: false, inUse: TrackerInUse, menuItems: RenderTrackerMenu);
        }

        public FloatingWindow Window => window;

        public void Dispose() {
            window.Dispose();
            tracker.Dispose();
        }

        public void Render() {
            RenderSettings();
            ImGui.Spacing();
            RenderQuests();
        }

        private void RenderSettings() {
            if (!Form.SettingsHeader("quests-settings", "The Quests window and the Quest Tracker: whether they're on screen, "
                + "their size and background. The Quests window's toolbar button is under the Config tab's Client UI "
                + "Override.")) {
                return;
            }

            Form.Columns("quests-settings-grid", RenderWindowSettings, RenderTrackerSettings);
            ImGui.Spacing();
        }

        private void RenderWindowSettings() {
            Form.Section("Quests window");
            window.RenderControls("Your quests in a window of their own, to keep on screen while you play. Drag its "
                + "corner to size it.");
        }

        /// <summary>The quests, in the tab and in the window.</summary>
        private void RenderQuests() {
            feed.Refresh(PlayerFeed.KindQuests, RefreshInterval);

            if (!feed.Supports(PlayerFeed.KindQuests)) {
                Widgets.TextColored(Colors.Warning, "This server is too old for the Quests tab.");
                return;
            }

            var all = feed.Quests?.Quests;

            if (all == null) {
                Widgets.TextDisabled("Loading...");
                return;
            }

            RenderFilters(all);

            var quests = all
                .Where(q => show == "all" || q.Stage == show)
                .Where(q => Matches(q, search))
                .OrderBy(q => q.SortOrder)
                .ThenBy(q => q.Name)
                .ToList();

            if (all.Count == 0) {
                Widgets.TextDisabled("No quests. NPCs give them to you; they're what the game's Contracts panel lists.");
                return;
            }

            if (quests.Count == 0) {
                Widgets.TextDisabled("No quests match.");
                return;
            }

            if (quests.All(q => q.Id != selected)) {
                selected = quests[0].Id;
            }

            // the list takes the top, the picked quest the rest
            var avail = ImGui.GetContentRegionAvail().Y;
            var least = ImGui.GetFrameHeightWithSpacing() * 4;
            var listHeight = Panes.Size("quests-list", Math.Max(least, avail * 0.5f), avail, least, least);

            if (Widgets.BeginChild("quests-list", new Vector2(0, listHeight), false)) {
                RenderList(quests);
            }
            ImGui.EndChild();

            Panes.Divider("quests-list", false, listHeight, avail, least, least,
                "Drag to make the list of quests taller or shorter.");

            if (Widgets.BeginChild("quest-details", Vector2.Zero, false)) {
                RenderDetails(quests.First(q => q.Id == selected));
            }
            ImGui.EndChild();
        }

        private void RenderFilters(List<QuestInfo> all) {
            foreach (var (value, label) in Shows) {
                var count = value == "all" ? all.Count : all.Count(q => q.Stage == value);

                if (ImGui.RadioButton($"{label} ({count})##quests-show-{value}", show == value)) {
                    show = value;
                }

                ImGui.SameLine();
            }

            ImGui.SetNextItemWidth(Math.Max(100, ImGui.GetContentRegionAvail().X));
            ImGui.InputTextWithHint("##quests-search", "Find a quest", ref search, 100);
            Widgets.Tip("Lists the quests whose name, description or NPCs have these words.");
        }

        private void RenderList(List<QuestInfo> quests) {
            // Status, Progress and Time as wide as what's in them, and Progress and Time left out while no quest listed has
            // one: at set widths, a narrow window (the shared window attached to the toolbar, 310 pixels wide at 800x600)
            // squeezed the Quest column to a letter and pushed Track off the edge
            var progress = quests.Select(ProgressText).ToList();
            var times = quests.Select(TimeText).ToList();
            var showProgress = progress.Any(p => p != null);
            var showTime = times.Any(t => t != null);
            var tips = new List<string> {
                "The quest. Click it for its description, its NPCs and where they are.",
                "Started, not started yet, or done (and when it can be done again).",
            };

            if (showProgress) {
                tips.Add("How far along it is, for a quest that counts (kills, items handed in).");
            }

            if (showTime) {
                tips.Add("Time left on a timed quest, or until a done one can be done again.");
            }

            tips.Add("Whether the Quest Tracker lists it. Done quests are never listed.");

            if (!Widgets.BeginTable("quests-table", tips.Count, Widgets.TableFlags | ImGuiTableFlags.ScrollY)) {
                return;
            }

            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Quest", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, FitColumn("Status", quests.Select(StageText)));

            if (showProgress) {
                ImGui.TableSetupColumn("Progress", ImGuiTableColumnFlags.WidthFixed, FitColumn("Progress", progress));
            }

            if (showTime) {
                ImGui.TableSetupColumn("Time", ImGuiTableColumnFlags.WidthFixed, FitColumn("Time", times));
            }

            ImGui.TableSetupColumn("Track", ImGuiTableColumnFlags.WidthFixed,
                Math.Max(ImGui.CalcTextSize("Track").X, ImGui.GetFrameHeight()));
            Widgets.HeadersRow(tips.ToArray());

            for (var i = 0; i < quests.Count; i++) {
                var quest = quests[i];
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                var label = $"{quest.Name}##quest-{quest.Id}";

                // AllowOverlap: the Track checkbox drawn after it, at the row's end, takes its own clicks
                var flags = ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowOverlap;

                if (ImGui.Selectable(label, quest.Id == selected, flags)) {
                    selected = quest.Id;
                    abandoning = 0;
                }

                ImGui.TableNextColumn();
                Widgets.TextColored(StageColor(quest), StageText(quest));

                if (showProgress) {
                    ImGui.TableNextColumn();
                    RenderProgress(quest, ImGui.GetContentRegionAvail().X);
                }

                if (showTime) {
                    ImGui.TableNextColumn();
                    Widgets.TextDisabled(times[i] ?? "");
                }

                ImGui.TableNextColumn();
                RenderTrackCheckbox(quest);
            }

            ImGui.EndTable();
        }

        /// <summary>
        /// The quest's details. In the tracker (inTracker) its name is already on the line above, Abandon is left to the
        /// tab, and the text wraps at a set width, since the window sizes itself to what's in it.
        /// </summary>
        private void RenderDetails(QuestInfo quest, bool inTracker = false) {
            if (!inTracker) {
                Widgets.TextColored(ItemTooltip.Title, quest.Name ?? "");
            }

            var time = TimeText(quest);
            var timer = time != null ? $"  ({TimeLabel(quest)} {time})" : "";
            Widgets.TextColored(StageColor(quest), StageText(quest) + timer);

            if (quest.MaxSolves.HasValue || quest.Solves > 0) {
                RenderProgress(quest, ImGui.GetFontSize() * 14);

                if (quest.Progress != null) {
                    ImGui.SameLine();
                    Widgets.TextDisabled(quest.Progress);
                }
            }

            ImGui.Spacing();
            ImGui.PushTextWrapPos(inTracker ? ImGui.GetCursorPosX() + ImGui.GetFontSize() * TrackerWrapLines : 0);
            Widgets.Text(quest.Description ?? "No description.");
            ImGui.PopTextWrapPos();
            ImGui.Spacing();

            RenderPlace("Given by", quest.StartNpc, quest.StartAt, quest.Id, "start");
            RenderPlace("Hand in to", quest.EndNpc, quest.EndAt, quest.Id, "end");
            RenderPlace("Quest area", null, quest.Area, quest.Id, "area");

            if (!inTracker) {
                ImGui.Spacing();
                RenderAbandon(quest);
            }
        }

        /// <summary>An NPC or place: its name, where it is on the map, and Go, which points the World tab's arrow at it.</summary>
        private void RenderPlace(string label, string npc, PlaceInfo place, uint id, string which) {
            if (npc == null && place == null) {
                return;
            }

            Widgets.TextDisabled(label + ":");
            ImGui.SameLine();
            Widgets.Text(npc ?? "");

            if (place == null) {
                if (npc != null) {
                    ImGui.SameLine();
                    Widgets.TextDisabled("(in a dungeon)");
                }

                return;
            }

            ImGui.SameLine();
            Widgets.TextDisabled(MapProjection.Format(place.MapX, place.MapY));
            ImGui.SameLine();

            if (ImGui.SmallButton($"Go##quest-go-{id}-{which}")) {
                goTo(npc ?? label, place.MapX, place.MapY);
            }
            Widgets.Tip("Makes this the World tab's destination: the arrow window points the way, with how far it is.");
        }

        /// <summary>Abandon, pressed twice: the first asks, Yes sends it (@playerui abandon, the Contracts panel's own).</summary>
        private void RenderAbandon(QuestInfo quest) {
            if (!quest.CanAbandon) {
                Widgets.TextDisabled("This quest can't be abandoned until it's done.");
                return;
            }

            if (abandoning != quest.Id) {
                if (ImGui.Button("Abandon")) {
                    abandoning = quest.Id;
                }
                Widgets.Tip("Drops this quest from your list, as the Contracts panel's Abandon does. Asks first.");
                return;
            }

            Widgets.TextColored(Colors.Warning, $"Abandon {quest.Name}?");
            ImGui.SameLine();

            if (ImGui.Button("Yes, abandon it")) {
                feed.SendCommand($"@playerui {PlayerFeed.KindAbandon} {quest.Id}");
                abandoning = 0;
            }
            Widgets.Tip("Drops the quest, as the Contracts panel's Abandon does.");
            ImGui.SameLine();

            if (ImGui.Button("No")) {
                abandoning = 0;
            }
        }

        private static void RenderProgress(QuestInfo quest, float width) {
            if (quest.MaxSolves is int max && max > 0) {
                var solves = Math.Min(quest.Solves ?? 0, max);
                Widgets.Bar(solves / (float)max, Colors.Info, ProgressText(quest), width);
            }
            else if (quest.Solves > 0) {
                Widgets.TextDisabled(ProgressText(quest));
            }
        }

        /// <summary>What RenderProgress writes: the bar's count, or how many so far, or null for neither.</summary>
        private static string ProgressText(QuestInfo quest) {
            if (quest.MaxSolves is int max && max > 0) {
                return $"{Math.Min(quest.Solves ?? 0, max)} / {max}";
            }

            return quest.Solves > 0 ? $"{quest.Solves} so far" : null;
        }

        /// <summary>A column as wide as its header or the widest of its texts, with a frame's padding either side.</summary>
        private static float FitColumn(string header, IEnumerable<string> texts) {
            var widest = texts.Where(t => t != null).Select(t => ImGui.CalcTextSize(t).X).DefaultIfEmpty(0).Max();
            return Math.Max(ImGui.CalcTextSize(header).X, widest) + ImGui.GetStyle().FramePadding.X * 2;
        }

        private static string StageText(QuestInfo quest) {
            if (quest.IsDone) {
                return quest.RepeatsIn.HasValue ? "Done (repeatable)" : "Done";
            }

            return quest.IsStarted ? "Started" : "Not started";
        }

        private static Vector4 StageColor(QuestInfo quest) {
            return quest.IsDone ? Colors.Good : quest.IsStarted ? Colors.Info : Colors.Muted;
        }

        private static string TimeLabel(QuestInfo quest) => quest.IsDone ? "again in" : "time left";

        /// <summary>
        /// The quest's timer counted down from the reply: time left on a timed one, or until a done one can be done again.
        /// </summary>
        private string TimeText(QuestInfo quest) {
            var seconds = quest.IsDone ? quest.RepeatsIn : quest.DoneIn;

            if (!seconds.HasValue) {
                return null;
            }

            var left = seconds.Value - (DateTime.UtcNow - feed.QuestsUpdatedAt).TotalSeconds;
            return left <= 0 ? (quest.IsDone ? "now" : "done") : Widgets.Duration(left);
        }

        private static bool Matches(QuestInfo quest, string text) {
            var words = (text ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var haystack = string.Join(" ", quest.Name, quest.Description, quest.StartNpc, quest.EndNpc);
            return words.All(w => haystack.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}
