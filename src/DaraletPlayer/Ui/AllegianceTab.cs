using System.Collections.Generic;
using System.Globalization;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Social tab's Allegiance, as the game's allegiance panel has it: your rank and followers, your monarch and patron
    /// (with the XP you've passed up), your vassals (with the XP each has passed to you), and Swear (to your selection),
    /// Break (from your patron) and Kick (a vassal), the last two asked first. All from @playerui social and allegiance.
    /// </summary>
    internal class AllegianceTab {
        private const string BreakPopup = "Break allegiance?##allegiance-break";
        private const string KickPopup = "Kick vassal?##allegiance-kick";

        private readonly PlayerFeed feed;

        /// <summary>The vassal picked in the list, for Kick, or 0.</summary>
        private uint pickedVassal;

        /// <summary>Who Kick will kick, kept while its question is up.</summary>
        private AllegianceMemberInfo kicking;

        public AllegianceTab(PlayerFeed feed) {
            this.feed = feed;
        }

        public void Render() {
            if (!feed.Supports(PlayerFeed.KindSocial)) {
                Widgets.TextColored(Colors.Warning, SocialActions.TooOld("allegiances"));
                return;
            }

            feed.Refresh(PlayerFeed.KindSocial, SocialActions.RefreshInterval);
            var social = feed.Social;

            if (social == null) {
                Widgets.TextDisabled("Loading...");
                return;
            }

            var allegiance = social.Allegiance;
            var vassals = allegiance?.Vassals ?? new List<AllegianceMemberInfo>();

            if (!vassals.Exists(v => v.Guid == pickedVassal)) {
                pickedVassal = 0;
            }

            RenderButtons(allegiance, vassals);
            ImGui.Spacing();

            if (allegiance == null) {
                Widgets.TextDisabled("You aren't in an allegiance. Select a player and Swear to make them your patron.");
                return;
            }

            RenderSummary(allegiance);
            ImGui.Spacing();
            RenderVassals(vassals);
        }

        private void RenderButtons(AllegianceInfo allegiance, List<AllegianceMemberInfo> vassals) {
            var selected = SocialActions.SelectedPlayer(out var selectedName);
            var patron = allegiance?.Patron;

            var swearTip = patron != null ? $"You're already sworn to {patron.Name}: Break first."
                : selected == 0 ? "Select the player you'd like as your patron."
                : $"Walks to {selectedName} and asks to swear allegiance to them, as the game's Swear does.";

            if (Widgets.Button("Swear", patron == null && selected != 0, swearTip)) {
                SocialActions.Send(feed, PlayerFeed.KindAllegiance, $"swear {unchecked((uint)selected)}");
            }

            ImGui.SameLine();

            if (Widgets.Button("Break", patron != null, patron == null ? "You have no patron to break from."
                : $"Breaks your allegiance to {patron.Name}, your patron. Asks first.")) {
                ImGui.OpenPopup(BreakPopup);
            }

            ImGui.SameLine();

            // the vassal picked in the list, else the selection when it's one of yours
            var vassal = vassals.Find(v => v.Guid == pickedVassal)
                ?? vassals.Find(v => v.Guid == unchecked((uint)SocialActions.Selection()));

            if (Widgets.Button("Kick", vassal != null, vassal == null ? "Pick one of your vassals in the list (or select "
                + "them) to kick them." : $"Releases {vassal.Name} from your allegiance, with all their own vassals. "
                + "Asks first.")) {
                kicking = vassal;
                ImGui.OpenPopup(KickPopup);
            }

            RenderReply();
            RenderBreakPopup(patron);
            RenderKickPopup();
        }

        private void RenderReply() {
            var command = feed.LastCommand ?? "";

            if (command.StartsWith("@playerui " + PlayerFeed.KindAllegiance + " ") && feed.LastCommandReply.Count > 0) {
                Widgets.TextDisabled(feed.LastCommandReply[0]);
            }
        }

        private void RenderBreakPopup(AllegianceMemberInfo patron) {
            var open = true;

            if (!ImGui.BeginPopupModal(BreakPopup, ref open, ImGuiWindowFlags.AlwaysAutoResize)) {
                return;
            }

            Widgets.PopupText(patron == null ? "You have no patron any more."
                : $"Break your allegiance to {patron.Name}? You stop passing them XP, and your vassals go with you.");

            if (Widgets.Button("Break", patron != null, "Breaks it.")) {
                SocialActions.Send(feed, PlayerFeed.KindAllegiance, "break");
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel")) {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        private void RenderKickPopup() {
            var open = true;

            if (!ImGui.BeginPopupModal(KickPopup, ref open, ImGuiWindowFlags.AlwaysAutoResize)) {
                return;
            }

            Widgets.PopupText($"Kick {kicking?.Name} from your allegiance? They and their own vassals stop being yours.");

            if (Widgets.Button("Kick", kicking != null, "Kicks them.")) {
                SocialActions.Send(feed, PlayerFeed.KindAllegiance, $"kick {kicking.Guid}");
                pickedVassal = 0;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel")) {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        private static void RenderSummary(AllegianceInfo allegiance) {
            if (!string.IsNullOrEmpty(allegiance.Name)) {
                Widgets.TextColored(Colors.Info, allegiance.Name);
            }

            Widgets.Text($"Rank {allegiance.Rank}");
            Widgets.Tip("Your allegiance rank: it grows with your vassals' ranks.");
            ImGui.SameLine();
            Widgets.TextDisabled("|");
            ImGui.SameLine();
            Widgets.Text(Words.Count(allegiance.Followers, "follower"));
            Widgets.Tip("Everyone under you: your vassals, theirs, and so on down.");

            if (!ImGui.BeginTable("allegiance-chain", 2, ImGuiTableFlags.SizingFixedFit)) {
                return;
            }

            ImGui.TableSetupColumn("label", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 5);
            ImGui.TableSetupColumn("who", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            Widgets.TextDisabled("Monarch");
            ImGui.TableNextColumn();

            if (allegiance.Monarch == null) {
                Widgets.Text("You");
            }
            else {
                Member(allegiance.Monarch);
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            Widgets.TextDisabled("Patron");
            ImGui.TableNextColumn();

            if (allegiance.Patron == null) {
                Widgets.TextDisabled("None");
            }
            else {
                Member(allegiance.Patron);
                Widgets.TextDisabled($"You've passed {Xp(allegiance.Patron.XpGenerated)} XP to {allegiance.Patron.Name}.");
            }

            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            Widgets.TextDisabled("Received");
            ImGui.TableNextColumn();
            Widgets.Text($"{Xp(allegiance.XpReceived)} XP");
            Widgets.Tip("All the XP your vassals have passed up to you.");

            ImGui.EndTable();
        }

        /// <summary>A monarch or patron: name (a click selects them), level and rank, greyed while offline.</summary>
        private static void Member(AllegianceMemberInfo member) {
            if (ImGui.Selectable($"{member.Name}##allegiance-{member.Guid}", false, ImGuiSelectableFlags.None,
                ImGui.CalcTextSize(member.Name))) {
                SocialActions.Select(member.Guid);
            }
            Widgets.Tip($"Click to select {member.Name}.");

            ImGui.SameLine();
            Widgets.TextColored(member.Online ? Colors.Good : Colors.Muted,
                $"level {member.Level}, rank {member.Rank}, {(member.Online ? "online" : "offline")}");
        }

        private void RenderVassals(List<AllegianceMemberInfo> vassals) {
            Widgets.TextDisabled(Words.Count(vassals.Count, "vassal"));

            if (vassals.Count == 0 || !ImGui.BeginTable("allegiance-vassals", 5, Widgets.TableFlags)) {
                return;
            }

            ImGui.TableSetupColumn("Vassal", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Lvl", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 2.5f);
            ImGui.TableSetupColumn("Rank", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 2.5f);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 4);
            ImGui.TableSetupColumn("XP to you", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 7);
            Widgets.HeadersRow("Click a vassal to pick them for Kick, and select them in the game.",
                "Character level.", "Allegiance rank.", "Online or offline.", "All the XP they've passed up to you.");

            foreach (var vassal in vassals) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                if (ImGui.Selectable($"{vassal.Name}##vassal-{vassal.Guid}", vassal.Guid == pickedVassal,
                    ImGuiSelectableFlags.SpanAllColumns)) {
                    pickedVassal = vassal.Guid;
                    SocialActions.Select(vassal.Guid);
                }

                ImGui.TableNextColumn();
                Widgets.Text(vassal.Level.ToString());
                ImGui.TableNextColumn();
                Widgets.Text(vassal.Rank.ToString());
                ImGui.TableNextColumn();
                Widgets.TextColored(vassal.Online ? Colors.Good : Colors.Muted, vassal.Online ? "Online" : "Offline");
                ImGui.TableNextColumn();
                Widgets.Text(Xp(vassal.XpGenerated));
            }

            ImGui.EndTable();
        }

        private static string Xp(ulong xp) {
            return xp.ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
