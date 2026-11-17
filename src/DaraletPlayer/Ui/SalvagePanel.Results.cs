using System;
using System.Linq;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Salvage results: what the last Salvage made, read from the lines the server writes in chat as it salvages each
    /// item (SalvageResults: "Iron Sword (W5 | 3 units) -> combined with ...", bezel fragments, the pack space refusal),
    /// heard from Salvage being pressed until 4 seconds after the last request or line. Shown in the window under the
    /// slots (the units by material and tier, what you now carry of each, every item in a fold), and summed up in one line
    /// in chat when the run ends. With Hide salvage lines (SettingsData.SalvageHideChatLines) the server's lines are kept
    /// out of the chat (e.Eat), the one line standing for them. Only Broadcast lines are read (chat type 0, the server's
    /// own), so a tell can't add to it. A player squelching Salvaging gets no item lines, and so no results.
    /// </summary>
    internal sealed partial class SalvagePanel {
        private static readonly TimeSpan ListenAfter = TimeSpan.FromSeconds(4);

        /// <summary>The last run's results, or null before any.</summary>
        private SalvageReport report;
        private DateTime listenUntil = DateTime.MinValue;
        private bool reportAnnounced = true;

        private bool Listening => DateTime.UtcNow < listenUntil;

        private void StartReport() {
            report = new SalvageReport { Started = DateTime.Now };
            reportAnnounced = false;
            listenUntil = DateTime.UtcNow + ListenAfter;
        }

        /// <summary>Every frame: once the run's over, its one line in chat.</summary>
        private void StepReport() {
            if (Sending) {
                listenUntil = DateTime.UtcNow + ListenAfter;
            }

            if (report == null || reportAnnounced || Listening) {
                return;
            }

            reportAnnounced = true;
            PluginCore.LogFile($"[salvage] results: {report.Items.Count} items, {report.Units} units, "
                + $"{report.BezelFragments} bezel fragments" + (report.Refused != null ? ", refused" : ""));

            if (report.Items.Count == 0) {
                return;
            }

            // what "You carry" will say, in case it's wrong: the stacks found by their weenie
            var carried = CarriedStacks();
            PluginCore.LogFile("[salvage] carried after: " + string.Join(", ", report.Groups().Select(g =>
                $"{MaterialNames.Of(g.Material)} W{g.Tier} {carried(SalvageMath.StackWcid(g.Material, g.Tier)).Units}")));

            var parts = report.Groups().Select(g => $"{g.Units:N0} {MaterialNames.Of(g.Material)} W{g.Tier}");
            var bezels = report.BezelFragments > 0 ? $", and {Words.Count(report.BezelFragments, "Bezel Fragment")}" : "";
            PluginCore.Chat($"Salvaged {Words.Count(report.Items.Count, "item")} into {report.Units:N0} units: "
                + string.Join(", ", parts) + bezels + ".");
        }

        private void Core_ChatBoxMessage(object sender, ChatTextInterceptEventArgs e) {
            try {
                if (report == null || !Listening || e.Color != 0) {
                    return;
                }

                if (report.Read(e.Text)) {
                    listenUntil = DateTime.UtcNow + ListenAfter;

                    if (settings.Data.SalvageHideChatLines && report.Refused == null) {
                        e.Eat = true;
                    }
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>The last run's results, in a fold under the summary.</summary>
        private void RenderResults() {
            if (report == null) {
                return;
            }

            ImGui.Spacing();
            var title = Listening ? "Salvaging..." : $"Last salvage ({report.Started:t})";

            if (!ImGui.CollapsingHeader($"{title}###salvage-results", ImGuiTreeNodeFlags.DefaultOpen)) {
                return;
            }

            using var fold = Form.Folded();

            if (report.Refused != null) {
                Widgets.TextColored(Colors.Danger, report.Refused);
            }

            if (report.Items.Count == 0) {
                Widgets.TextDisabled(Listening ? "Waiting for the server..." : "The server said nothing of what it made. "
                    + "Is Salvaging squelched?");
                return;
            }

            Widgets.Text($"{Words.Count(report.Items.Count, "item")} made {report.Units:N0} units");

            if (report.BezelFragments > 0) {
                ImGui.SameLine();
                Widgets.TextColored(Colors.Good, $"and {Words.Count(report.BezelFragments, "Bezel Fragment")}");
            }

            var carried = CarriedStacks();
            var flags = ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV;

            if (ImGui.BeginTable("salvage-results-groups", 4, flags)) {
                ImGui.TableSetupColumn("Material");
                ImGui.TableSetupColumn("Items");
                ImGui.TableSetupColumn("Units");
                ImGui.TableSetupColumn("You carry");
                Widgets.HeadersRow(
                    "What they salvaged into, and the stack's tier.",
                    "How many items went into it.",
                    "The units of salvage they made.",
                    "The units of that salvage you carry now, your salvage crate included.");

                foreach (var group in report.Groups()) {
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    Widgets.Text($"{MaterialNames.Of(group.Material) ?? "?"} W{group.Tier}");
                    ImGui.TableNextColumn();
                    Widgets.Text($"{group.Items}");
                    ImGui.TableNextColumn();
                    Widgets.TextColored(Colors.Good, $"+{group.Units:N0}");
                    ImGui.TableNextColumn();
                    Widgets.Text($"{carried(SalvageMath.StackWcid(group.Material, group.Tier)).Units:N0}");
                }

                ImGui.EndTable();
            }

            if (ImGui.TreeNode($"Each item ({report.Items.Count})##salvage-results-items")) {
                foreach (var item in report.Items) {
                    var where = item.InCrate ? " (to your salvage crate)" : item.Combined ? "" : " (a new stack)";
                    Widgets.Text($"{item.Item}");
                    ImGui.SameLine();
                    Widgets.TextDisabled($"{item.Units} {MaterialNames.Of(item.Material)} W{item.Tier}{where}");
                }

                ImGui.TreePop();
            }

            if (ImGui.SmallButton("Dismiss##salvage-results")) {
                report = null;
            }

            Widgets.Tip("Clears these results. They're also replaced by the next Salvage's.");
        }
    }
}
