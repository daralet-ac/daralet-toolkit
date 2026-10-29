using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Quest Tracker: the quests you're doing in a small window that stays on screen, each a line with its progress
    /// and time left that opens out to the same details the tab shows (but Abandon). Which are open is saved
    /// (SettingsData.QuestTrackerOpen).
    /// </summary>
    internal partial class QuestsTab {
        /// <summary>
        /// How wide an open quest's description wraps, in lines of text: the window sizes itself to its contents.
        /// </summary>
        private const float TrackerWrapLines = 22;

        /// <summary>The quest right-clicked in the tracker, for its menu (the window's own, opened from the line).</summary>
        private uint menuQuest;

        /// <summary>
        /// The quests the tracker lists: started ones, then (if asked for) the ones not started yet, but those hidden.
        /// </summary>
        private List<QuestInfo> TrackedQuests() {
            var hidden = settings.Data.QuestTrackerHidden;

            return (feed.Quests?.Quests ?? new List<QuestInfo>())
                .Where(q => Trackable(q) && !hidden.Contains(q.Id))
                .OrderBy(q => q.SortOrder)
                .ThenBy(q => q.Name)
                .ToList();
        }

        /// <summary>A quest of the kind the tracker lists, hidden or not.</summary>
        private bool Trackable(QuestInfo quest) {
            return quest.IsStarted || (settings.Data.QuestTrackerAvailable && !quest.IsDone);
        }

        private void SetTracked(uint id, bool tracked) {
            var hidden = settings.Data.QuestTrackerHidden;

            if (tracked) {
                hidden.Remove(id);
            }
            else if (!hidden.Contains(id)) {
                hidden.Add(id);
            }

            settings.Changed();
        }

        /// <summary>The Quests tab's Track box for one quest: ticked while the tracker lists it.</summary>
        private void RenderTrackCheckbox(QuestInfo quest) {
            if (quest.IsDone) {
                return;
            }

            var tracked = !settings.Data.QuestTrackerHidden.Contains(quest.Id);

            if (ImGui.Checkbox($"##track-{quest.Id}", ref tracked)) {
                SetTracked(quest.Id, tracked);
            }

            Widgets.Tip(Trackable(quest) || !tracked
                ? "Ticked, the Quest Tracker lists this quest. Unticked, it's left out."
                : "Ticked, but the Quest Tracker only lists quests you haven't started with its Not started too.");
        }

        /// <summary>The tracker's right-click menu, over a quest's line: hide it.</summary>
        private void RenderTrackerMenu() {
            var quest = feed.Quests?.Quests?.FirstOrDefault(q => q.Id == menuQuest);

            if (quest == null) {
                return;
            }

            Widgets.TextDisabled(quest.Name ?? "");

            if (ImGui.MenuItem("Hide from tracker")) {
                SetTracked(quest.Id, false);
                menuQuest = 0;
            }
            Widgets.Tip("Leaves this quest out of the tracker. Tick it under Track in the Quests tab to bring it back.");

            ImGui.Separator();
        }

        /// <summary>What the tracker shows, or null with nothing to track, for Only when needed.</summary>
        private object TrackerInUse() {
            var count = TrackedQuests().Count;
            return count > 0 ? (object)count : null;
        }

        private void RenderTracker() {
            feed.Refresh(PlayerFeed.KindQuests, RefreshInterval);

            if (!feed.Supports(PlayerFeed.KindQuests)) {
                Widgets.TextColored(Colors.Warning, "This server is too old for quests.");
                return;
            }

            if (feed.Quests?.Quests == null) {
                Widgets.TextDisabled("Loading...");
                return;
            }

            var quests = TrackedQuests();

            // a right-click anywhere else is the window's own menu, without a quest
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && ImGui.IsWindowHovered()) {
                menuQuest = 0;
            }

            if (quests.Count == 0) {
                var allHidden = feed.Quests.Quests.Any(Trackable);
                Widgets.TextDisabled(allHidden ? "Every quest is hidden: tick them under Track in the Quests tab."
                    : "No quests in progress.");
                return;
            }

            foreach (var quest in quests) {
                RenderTracked(quest);
            }
        }

        /// <summary>One quest: its line (name, then progress and time left), and its details while it's open.</summary>
        private void RenderTracked(QuestInfo quest) {
            var open = settings.Data.QuestTrackerOpen;
            var wasOpen = open.Contains(quest.Id);

            ImGui.SetNextItemOpen(wasOpen, ImGuiCond.Always);
            ImGui.PushStyleColor(ImGuiCol.Text, StageColor(quest));
            var isOpen = ImGui.TreeNodeEx($"{quest.Name}##tracked-{quest.Id}", ImGuiTreeNodeFlags.SpanAvailWidth);
            ImGui.PopStyleColor();

            // the window's menu doesn't open over an item, so the line opens it, with this quest
            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
                menuQuest = quest.Id;
                tracker.OpenMenu();
            }

            Widgets.Tip("Click to show or hide its details; right-click to hide it from the tracker.");

            if (isOpen != wasOpen) {
                if (isOpen) {
                    open.Add(quest.Id);
                }
                else {
                    open.Remove(quest.Id);
                }

                settings.Changed();
            }

            var summary = TrackerSummary(quest);

            if (summary.Length > 0) {
                ImGui.SameLine();
                Widgets.TextColored(Colors.Detail, summary);
            }

            if (!isOpen) {
                return;
            }

            ImGui.PushID((int)quest.Id);
            RenderDetails(quest, inTracker: true);
            ImGui.PopID();
            ImGui.TreePop();
            ImGui.Spacing();
        }

        /// <summary>The line's short progress ("3 / 10") and time left, whichever the quest has.</summary>
        private string TrackerSummary(QuestInfo quest) {
            var parts = new List<string>();

            if (quest.MaxSolves is int max && max > 0) {
                parts.Add($"{System.Math.Min(quest.Solves ?? 0, max)} / {max}");
            }
            else if (quest.Solves > 0) {
                parts.Add($"{quest.Solves}");
            }

            var time = TimeText(quest);

            if (time != null) {
                parts.Add(time);
            }

            return string.Join("  ", parts);
        }

        private void RenderTrackerSettings() {
            Form.Section("Quest tracker");
            tracker.RenderControls("A small window of the quests you're doing, each one's progress and time left on its "
                + "line; click one to open its details.");

            var data = settings.Data;
            var first = true;

            if (Form.FlowCheck(ref first, "Not started too##quest-tracker", ref data.QuestTrackerAvailable,
                "Lists the quests you've been given but haven't started yet as well, under the started ones.")) {
                settings.Changed();
            }

            if (Form.FlowSmallButton(ref first, "Open all##quest-tracker",
                "Opens every quest in the tracker to its details.")) {
                data.QuestTrackerOpen = TrackedQuests().Select(q => q.Id).ToList();
                settings.Changed();
            }

            if (Form.FlowSmallButton(ref first, "Close all##quest-tracker",
                "Folds every quest in the tracker back to its line.")) {
                data.QuestTrackerOpen.Clear();
                settings.Changed();
            }

            var hiddenCount = data.QuestTrackerHidden.Count;

            if (hiddenCount > 0 && Form.FlowSmallButton(ref first, $"Show hidden ({hiddenCount})##quest-tracker",
                "Puts every quest you've hidden back in the tracker.")) {
                data.QuestTrackerHidden.Clear();
                settings.Changed();
            }
        }
    }
}
