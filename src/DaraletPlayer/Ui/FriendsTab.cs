using System.Collections.Generic;
using System.Linq;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Social tab's Friends: your friends and who's online (@playerui social), adding one by name or the selected player
    /// (@playerui friend add), Appear Offline, and a right-click menu on each: Invite, Send tell, Remove.
    /// </summary>
    internal class FriendsTab {
        private const int NameLength = 32;
        private const int TellLength = 200;

        private readonly PlayerFeed feed;

        private string addName = "";

        /// <summary>The friend Send tell was picked for: its popup opens next frame, outside the menu (ID stack).</summary>
        private FriendInfo tellTo;
        private bool openTell;
        private string tellText = "";

        public FriendsTab(PlayerFeed feed) {
            this.feed = feed;
        }

        public void Render() {
            if (!feed.Supports(PlayerFeed.KindSocial)) {
                Widgets.TextColored(Colors.Warning, SocialActions.TooOld("the friends list"));
                return;
            }

            feed.Refresh(PlayerFeed.KindSocial, SocialActions.RefreshInterval);
            var social = feed.Social;

            if (social == null) {
                Widgets.TextDisabled("Loading...");
                return;
            }

            RenderAppearOffline(social);
            ImGui.Spacing();
            RenderAdd(social);
            ImGui.Spacing();
            RenderList(social.Friends ?? new List<FriendInfo>());
            RenderTellPopup();
        }

        private void RenderAppearOffline(SocialInfo social) {
            const AcClient.PlayerOption option = AcClient.PlayerOption.AppearOffline_PlayerOption;
            var offline = SocialActions.Option(feed, option, social.AppearOffline);

            if (Form.Check("Appear Offline", ref offline, "Your friends see you as offline, and aren't told when you "
                + "come on or go off line: the game's own Appear Offline option.")) {
                SocialActions.SetOption(feed, option, offline);
            }
        }

        private void RenderAdd(SocialInfo social) {
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 12);
            var enter = ImGui.InputTextWithHint("##friend-name", "Character name", ref addName, NameLength,
                ImGuiInputTextFlags.EnterReturnsTrue);
            Widgets.Tip("A character's name to add to your friends. Press Enter or Add.");

            var name = addName.Trim();
            ImGui.SameLine();

            if ((Widgets.Button("Add", name.Length > 0, "Adds the character named in the box to your friends "
                + "(@playerui friend add).") || enter) && name.Length > 0) {
                SocialActions.Send(feed, PlayerFeed.KindFriend, "add " + name);
                addName = "";
            }

            var selected = SocialActions.SelectedPlayer(out var selectedName);
            var already = selected != 0 && (social.Friends ?? new List<FriendInfo>()).Any(f => f.Guid == unchecked((uint)selected));
            ImGui.SameLine();

            var tip = selected == 0 ? "Select a player in the game to add them."
                : already ? $"{selectedName} is already your friend."
                : $"Adds {selectedName}, your selection, to your friends.";

            if (Widgets.Button(selected != 0 ? $"Add {selectedName}" : "Add selected", selected != 0 && !already, tip)) {
                SocialActions.Send(feed, PlayerFeed.KindFriend, "add " + selectedName);
            }

            RenderReply();
        }

        /// <summary>What the game said to the last button: added, not found...</summary>
        private void RenderReply() {
            var command = feed.LastCommand ?? "";

            if (!command.StartsWith("@playerui " + PlayerFeed.KindFriend + " ") || feed.LastCommandReply.Count == 0) {
                return;
            }

            Widgets.TextDisabled(feed.LastCommandReply[0]);
        }

        private void RenderList(List<FriendInfo> friends) {
            var online = friends.Count(f => f.Online);
            Widgets.TextDisabled($"{Words.Count(friends.Count, "friend")}, {online} online");

            if (friends.Count == 0) {
                return;
            }

            if (!ImGui.BeginTable("friends", 2, Widgets.TableFlags)) {
                return;
            }

            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 5);
            Widgets.HeadersRow("Right-click a friend to invite them to your fellowship, send them a tell or remove them.",
                "Online or offline. One who appears offline shows as offline.");

            foreach (var friend in friends) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                if (!friend.Online) {
                    ImGui.PushStyleColor(ImGuiCol.Text, Colors.Muted);
                }

                ImGui.Selectable($"{friend.Name}##friend-{friend.Guid}", false, ImGuiSelectableFlags.SpanAllColumns);

                if (!friend.Online) {
                    ImGui.PopStyleColor();
                }

                Widgets.Tip($"Right-click for Invite, Send tell and Remove.");
                RenderMenu(friend);

                ImGui.TableNextColumn();
                Widgets.TextColored(friend.Online ? Colors.Good : Colors.Muted, friend.Online ? "Online" : "Offline");
            }

            ImGui.EndTable();
        }

        private void RenderMenu(FriendInfo friend) {
            if (!ImGui.BeginPopupContextItem($"friend-menu-{friend.Guid}")) {
                return;
            }

            Widgets.TextDisabled(friend.Name);
            ImGui.Separator();

            var fellowship = feed.Fellowship;
            var canRecruit = SocialActions.CanRecruit(fellowship, out var why);
            var inIt = fellowship?.Members?.Any(m => m.Guid == friend.Guid) == true;

            if (canRecruit && inIt) {
                canRecruit = false;
                why = $"{friend.Name} is already in your fellowship.";
            }
            else if (canRecruit && !friend.Online) {
                canRecruit = false;
                why = $"{friend.Name} is offline.";
            }

            if (ImGui.MenuItem("Invite", null, false, canRecruit)) {
                CoreManager.Current.Actions.FellowshipRecruit(unchecked((int)friend.Guid));
            }
            Widgets.Tip(why ?? $"Asks {friend.Name} to join your fellowship, as the fellowship panel's Recruit does. "
                + "They need to be near enough to see.");

            if (ImGui.MenuItem("Send tell", null, false, friend.Online)) {
                tellTo = friend;
                openTell = true;
                tellText = "";
            }
            Widgets.Tip(friend.Online ? $"Type a tell to {friend.Name}." : $"{friend.Name} is offline.");

            if (ImGui.MenuItem("Remove")) {
                SocialActions.Send(feed, PlayerFeed.KindFriend, $"remove {friend.Guid}");
            }
            Widgets.Tip($"Takes {friend.Name} off your friends list (@playerui friend remove).");

            ImGui.EndPopup();
        }

        /// <summary>Send tell's box, opened here at the tab's level the frame after the menu picked it.</summary>
        private void RenderTellPopup() {
            const string id = "Send tell##friend-tell";

            if (openTell) {
                ImGui.OpenPopup(id);
                openTell = false;
            }

            if (!ImGui.BeginPopup(id)) {
                return;
            }

            Widgets.Text($"Tell {tellTo?.Name}");

            if (ImGui.IsWindowAppearing()) {
                ImGui.SetKeyboardFocusHere();
            }

            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 20);
            var enter = ImGui.InputText("##tell-text", ref tellText, TellLength, ImGuiInputTextFlags.EnterReturnsTrue);
            var text = tellText.Trim();

            if ((Widgets.Button("Send", text.Length > 0, "Sends it as /tell would.") || enter) && text.Length > 0) {
                SocialActions.Tell(tellTo?.Name, text);
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel")) {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }
}
