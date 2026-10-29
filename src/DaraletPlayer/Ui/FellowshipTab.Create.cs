using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Out of a fellowship: the game panel's Create Fellowship, a name and its four options (each the game's own option,
    /// SocialActions.SetOption), and Find a group.
    /// </summary>
    internal partial class FellowshipTab {
        /// <summary>The longest name the game's own box takes.</summary>
        private const int FellowshipNameLength = 40;

        private string createName = "";

        private void RenderCreate() {
            Widgets.TextDisabled("You aren't in a fellowship.");
            ImGui.SameLine();

            if (Widgets.Button("Find a group", true, "Opens Find a group, to look for a fellowship to join.")) {
                openFindGroup();
            }

            Form.Section("Create Fellowship");

            if (!feed.Supports(PlayerFeed.KindFellowCreate) || !feed.Supports(PlayerFeed.KindSocial)) {
                Widgets.TextColored(Colors.Warning, SocialActions.TooOld("creating a fellowship here"));
                return;
            }

            feed.Refresh(PlayerFeed.KindSocial, SocialActions.RefreshInterval);

            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 14);
            var enter = ImGui.InputTextWithHint("##fellowship-name", "Fellowship name", ref createName, FellowshipNameLength,
                ImGuiInputTextFlags.EnterReturnsTrue);
            Widgets.Tip("Your new fellowship's name. Press Enter or Create Fellowship.");

            var name = createName.Trim();
            var social = feed.Social;
            ImGui.SameLine();

            if ((Widgets.Button("Create Fellowship", name.Length > 0, name.Length > 0
                ? "Creates the fellowship, with you as its leader. Then recruit members with Recruit."
                : "Type a name first.") || enter) && name.Length > 0) {
                var shareXp = social == null || SocialActions.Option(feed,
                    AcClient.PlayerOption.FellowshipShareXP_PlayerOption, social.ShareFellowshipXp);
                SocialActions.Send(feed, PlayerFeed.KindFellowCreate, (shareXp ? "1 " : "0 ") + name);
                createName = "";
            }

            if (social == null) {
                Widgets.TextDisabled("Loading your options...");
                return;
            }

            Option(AcClient.PlayerOption.IgnoreFellowshipRequests_PlayerOption, social.IgnoreFellowshipRequests,
                "Ignore Fellowship Requests", "Turns down every invitation to a fellowship without asking you.");
            Option(AcClient.PlayerOption.FellowshipAutoAcceptRequests_PlayerOption, social.AutoAcceptFellowshipRequests,
                "Automatically Accept Fellowship Requests", "Joins any fellowship you're invited to without asking you.");
            Option(AcClient.PlayerOption.FellowshipShareXP_PlayerOption, social.ShareFellowshipXp,
                "Share Fellowship Experience and Luminance", "A fellowship you create shares the XP and luminance its "
                + "members earn.");
            Option(AcClient.PlayerOption.FellowshipShareLoot_PlayerOption, social.ShareFellowshipLoot,
                "Share Fellowship Loot", "Lets your fellows loot the corpses of what you kill.");

            var command = feed.LastCommand ?? "";

            if (command.StartsWith("@playerui " + PlayerFeed.KindFellowCreate + " ") && feed.LastCommandReply.Count > 0) {
                Widgets.TextDisabled(feed.LastCommandReply[0]);
            }
        }

        /// <summary>One of the game's options as a checkbox: ticking it sends the change, as the game's panel does.</summary>
        private void Option(AcClient.PlayerOption option, bool fromServer, string label, string tip) {
            var value = SocialActions.Option(feed, option, fromServer);

            if (Form.Check($"{label}##fellowship-option-{(int)option}", ref value, tip + " The game's own option.")) {
                SocialActions.SetOption(feed, option, value);
            }
        }
    }
}
