using System.Collections.Generic;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Social tab's Squelch: the characters and accounts you've squelched (@playerui social), squelching a character
    /// or a whole account by name, and lifting the one picked in the list (@playerui squelch).
    /// </summary>
    internal class SquelchTab {
        private const int NameLength = 32;

        private readonly PlayerFeed feed;

        private string name = "";

        /// <summary>The squelch picked in the list, by guid, or 0.</summary>
        private uint picked;

        public SquelchTab(PlayerFeed feed) {
            this.feed = feed;
        }

        public void Render() {
            if (!feed.Supports(PlayerFeed.KindSocial)) {
                Widgets.TextColored(Colors.Warning, SocialActions.TooOld("the squelch list"));
                return;
            }

            feed.Refresh(PlayerFeed.KindSocial, SocialActions.RefreshInterval);
            var social = feed.Social;

            if (social == null) {
                Widgets.TextDisabled("Loading...");
                return;
            }

            RenderAdd();
            ImGui.Spacing();
            RenderList(social.Squelches ?? new List<SquelchInfo>());
        }

        private void RenderAdd() {
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 12);
            ImGui.InputTextWithHint("##squelch-name", "Character name", ref name, NameLength);
            Widgets.Tip("The character to squelch. Their chat (says, tells, emotes...) stops reaching you.");

            var trimmed = name.Trim();

            if (SocialActions.SelectedPlayer(out var selectedName) != 0) {
                ImGui.SameLine();

                if (ImGui.SmallButton("Selected")) {
                    name = selectedName;
                }
                Widgets.Tip($"Puts {selectedName}, your selection, in the box.");
            }

            if (Widgets.Button("Squelch Character", trimmed.Length > 0, "Squelches this one character on every channel "
                + "(@playerui squelch character).")) {
                SocialActions.Send(feed, PlayerFeed.KindSquelch, "character " + trimmed);
                name = "";
            }

            ImGui.SameLine();

            if (Widgets.Button("Squelch Account", trimmed.Length > 0, "Squelches every character on this one's account, "
                + "including ones they make later (@playerui squelch account).")) {
                SocialActions.Send(feed, PlayerFeed.KindSquelch, "account " + trimmed);
                name = "";
            }

            var command = feed.LastCommand ?? "";

            if (command.StartsWith("@playerui " + PlayerFeed.KindSquelch + " ") && feed.LastCommandReply.Count > 0) {
                Widgets.TextDisabled(feed.LastCommandReply[0]);
            }
        }

        private void RenderList(List<SquelchInfo> squelches) {
            Widgets.TextDisabled(Words.Count(squelches.Count, "squelch"));

            if (!squelches.Exists(s => s.Guid == picked)) {
                picked = 0;
            }

            var pickedSquelch = squelches.Find(s => s.Guid == picked);
            ImGui.SameLine();

            if (Widgets.Button("Remove", pickedSquelch != null, pickedSquelch == null
                ? "Pick a squelch in the list to lift it."
                : $"Unsquelches {pickedSquelch.Name}{(pickedSquelch.Account ? " and their account" : "")}.")) {
                SocialActions.Send(feed, PlayerFeed.KindSquelch, $"remove {pickedSquelch.Guid}");
                picked = 0;
            }

            if (squelches.Count == 0 || !ImGui.BeginTable("squelches", 2, Widgets.TableFlags)) {
                return;
            }

            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Squelched", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFontSize() * 6);
            Widgets.HeadersRow("Click one to pick it for Remove.",
                "Character: just them. Account: every character on their account.");

            foreach (var squelch in squelches) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                if (ImGui.Selectable($"{squelch.Name}##squelch-{squelch.Guid}", squelch.Guid == picked,
                    ImGuiSelectableFlags.SpanAllColumns)) {
                    picked = squelch.Guid;
                }

                ImGui.TableNextColumn();
                Widgets.TextDisabled(squelch.Account ? "Account" : "Character");
            }

            ImGui.EndTable();
        }
    }
}
