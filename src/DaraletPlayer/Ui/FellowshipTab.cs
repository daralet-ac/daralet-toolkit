using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Social tab's Fellowship: your fellowship's members and its buttons (Leader, Dismiss, Open, Recruit, Quit,
    /// Disband), or Create Fellowship when you aren't in one, and the fellowship vitals window it controls. Both show what
    /// @playerui fellowship sends, asked for once a second while either is on screen.
    /// </summary>
    /// <remarks>Split across files: <c>.Window</c> (the fellowship window) and <c>.Create</c> (Create Fellowship).</remarks>
    internal partial class FellowshipTab : IDisposable {
        private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
        private static readonly TimeSpan BigRefreshInterval = TimeSpan.FromSeconds(2);

        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly ItemIcons icons;
        private readonly FloatingWindow window;
        private readonly Action openFindGroup;

        public FellowshipTab(PlayerFeed feed, Settings settings, ItemIcons icons, Action openFindGroup) {
            this.feed = feed;
            this.settings = settings;
            this.icons = icons;
            this.openFindGroup = openFindGroup;

            window = new FloatingWindow("fellowship", "Fellowship", settings, new Vector2(20, 200), RenderWindow,
                inUse: FellowshipInUse, menuItems: RenderMenuMember);

            // only the fellowship window draws spells, so only it makes the requests ask for them
            feed.WantFellowSpells = () => window.Shown
                && (settings.Data.FellowshipShowBuffs || settings.Data.FellowshipShowDebuffs)
                && (feed.Fellowship?.Members?.Count ?? 0) <= settings.Data.FellowshipSpellsUpTo;
        }

        /// <summary>
        /// Menu items for which vitals one fellow's bars show (with Default, back to the Stamina and Mana checkboxes).
        /// </summary>
        private void RenderVitalChoices(string name) {
            var data = settings.Data;
            var (health, stamina, mana) = data.FellowShows(name);

            if (ImGui.MenuItem("Health", null, health)) {
                data.FellowVitalsFor(name).Health = !health;
                settings.Changed();
            }

            if (ImGui.MenuItem("Stamina", null, stamina)) {
                data.FellowVitalsFor(name).Stamina = !stamina;
                settings.Changed();
            }

            if (ImGui.MenuItem("Mana", null, mana)) {
                data.FellowVitalsFor(name).Mana = !mana;
                settings.Changed();
            }

            var own = data.FellowVitals.TryGetValue(name, out var vitals) && vitals != null && !vitals.IsDefault;

            if (ImGui.MenuItem("Default vitals", null, false, own)) {
                data.FellowVitals.Remove(name);
                settings.Changed();
            }

            data.RemoveDefaultFellowVitals(name);
        }

        public void Render() {
            Refresh();

            if (!feed.Supports(PlayerFeed.KindFellowship)) {
                Widgets.TextColored(Colors.Warning, "This server is too old to send fellowship vitals.");
                return;
            }

            RenderSettings();
            ImGui.Spacing();

            var fellowship = feed.Fellowship;

            if (fellowship == null) {
                if (feed.FellowshipUpdatedAt == DateTime.MinValue) {
                    Widgets.TextDisabled("Loading...");
                    return;
                }

                RenderCreate();
                return;
            }

            Widgets.Text(fellowship.Name ?? "");
            ImGui.SameLine();
            Widgets.TextDisabled(Describe(fellowship));
            Widgets.Tip("Sharing XP needs every member within 5 levels of the leader, or all of them level 50 or higher. "
                + "Even share splits XP equally; otherwise higher levels get more. "
                + "Open fellowships let any member recruit. Locked ones can't take new members.");

            RenderButtons(fellowship);
            ImGui.Spacing();
            RenderTable(fellowship);
        }

        /// <summary>
        /// The fellowship panel's buttons, each greyed out (saying why) while it can't be used. Leader, Dismiss and Recruit
        /// act on your selection, as the game's do: click a member's name to select them.
        /// </summary>
        private void RenderButtons(FellowshipInfo fellowship) {
            var leader = SocialActions.IsLeader(fellowship);
            var me = unchecked((uint)CoreManager.Current.CharacterFilter.Id);
            var selected = unchecked((uint)SocialActions.Selection());
            var member = fellowship.Members?.FirstOrDefault(m => m.Guid == selected && m.Guid != me);
            var first = true;

            var memberTip = !leader ? "Only the leader can." : member == null ? "Select a member first (click their name)."
                : null;

            Form.Flow(ref first, ButtonWidth("Leader"));

            if (Widgets.Button("Leader", leader && member != null, memberTip
                ?? $"Makes {member.Name} the fellowship's leader.")) {
                CoreManager.Current.Actions.FellowshipGrantLeader(unchecked((int)member.Guid));
            }

            Form.Flow(ref first, ButtonWidth("Dismiss"));

            if (Widgets.Button("Dismiss", leader && member != null, memberTip
                ?? $"Dismisses {member.Name} from the fellowship.")) {
                CoreManager.Current.Actions.FellowshipDismiss(unchecked((int)member.Guid));
            }

            var openLabel = fellowship.Open ? "Close" : "Open";
            Form.Flow(ref first, ButtonWidth(openLabel));
            var openTip = !leader ? "Only the leader can open or close the fellowship."
                : fellowship.Locked ? "A locked fellowship can't be opened or closed."
                : fellowship.Open ? "Closes the fellowship: only the leader can recruit."
                : "Opens the fellowship: any member can recruit.";

            if (Widgets.Button($"{openLabel}##fellowship-open", leader && !fellowship.Locked, openTip)) {
                CoreManager.Current.Actions.FellowshipSetOpen(!fellowship.Open);
            }

            var recruit = SocialActions.SelectedPlayer(out var recruitName);
            var canRecruit = SocialActions.CanRecruit(fellowship, out var why);

            if (canRecruit && recruit == 0) {
                canRecruit = false;
                why = "Select the player to recruit.";
            }
            else if (canRecruit && fellowship.Members?.Any(m => m.Guid == unchecked((uint)recruit)) == true) {
                canRecruit = false;
                why = $"{recruitName} is already in your fellowship.";
            }

            Form.Flow(ref first, ButtonWidth("Recruit"));

            if (Widgets.Button("Recruit", canRecruit, why ?? $"Asks {recruitName} to join the fellowship.")) {
                CoreManager.Current.Actions.FellowshipRecruit(recruit);
            }

            Form.Flow(ref first, ButtonWidth("Quit"));

            if (Widgets.Button("Quit", true, leader
                ? "Leaves the fellowship; it carries on with a new leader."
                : "Leaves the fellowship.")) {
                CoreManager.Current.Actions.FellowshipQuit();
            }

            Form.Flow(ref first, ButtonWidth("Disband"));

            if (Widgets.Button("Disband", leader, leader ? "Ends the fellowship for everyone in it."
                : "Only the leader can disband the fellowship.")) {
                CoreManager.Current.Actions.FellowshipDisband();
            }
        }

        private static float ButtonWidth(string label) {
            return ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2;
        }

        private void RenderSettings() {
            if (!Form.SettingsHeader("fellowship-settings", "The fellowship window: whether it's on screen, its size and "
                + "background, and what it shows.")) {
                return;
            }

            Form.Section("Fellowship window");
            window.RenderControls("A small window with each member's health, stamina and mana that stays on screen while "
                + "you play. Click a name in it to select that member.");

            Form.Section("Vitals shown");
            var data = settings.Data;
            var first = true;

            if (Form.FlowCheck(ref first, "Stamina##fellowship", ref data.FellowshipShowStamina,
                "Shows each member's stamina in the fellowship window.")) {
                settings.Changed();
            }

            if (Form.FlowCheck(ref first, "Mana##fellowship", ref data.FellowshipShowMana,
                "Shows each member's mana in the fellowship window.")) {
                settings.Changed();
            }

            if (Form.FlowCheck(ref first, "Side by side##fellowship", ref data.FellowshipSideBySide,
                "Puts each member's bars in a row, beside each other, instead of one under another.")) {
                settings.Changed();
            }

            if (Form.FlowCheck(ref first, "Retail bars##fellowship", ref data.RetailVitalBars, Widgets.RetailBarsTip)) {
                settings.Changed();
            }

            if (Form.FlowCheck(ref first, "Bold numbers##fellowship", ref data.BoldVitalText, Widgets.BoldVitalsTip)) {
                settings.Changed();
            }

            if (Form.BeginForm("fellowship-bars")) {
                Form.Label("Bar width", "How wide each vital bar is in the fellowship window, which grows or shrinks to fit. "
                    + "Its Size makes everything bigger, bars included.");
                var width = data.FellowshipBarWidth;

                if (Form.Slider("##fellowship-bar-width", ref width, MinBarWidth, MaxBarWidth, "%d px")) {
                    data.FellowshipBarWidth = width;
                    settings.Changed();
                }

                ImGui.EndTable();
            }

            Widgets.TextDisabled("Right-click a member, here or in the window, to pick their bars for them alone.");

            Form.Section("By each name");

            // the focus and spells came with the server's social requests: an older one sends neither
            if (!feed.Supports(PlayerFeed.KindSocial)) {
                Form.Hint("This server is too old to send each member's Combat Focus, buffs or debuffs: those stay empty "
                    + "until it's updated.");
            }

            first = true;

            if (Form.FlowCheck(ref first, "Level##fellowship", ref data.FellowshipShowLevel,
                "Shows each member's character level by their name.")) {
                settings.Changed();
            }

            if (Form.FlowCheck(ref first, "Combat Focus##fellowship", ref data.FellowshipShowFocus,
                "Shows the icon of the Combat Focus each member wears, before their name.")) {
                settings.Changed();
            }

            if (Form.FlowCheck(ref first, "Distance##fellowship", ref data.FellowshipShowDistance,
                "Shows how far each member is from you, when they're near enough for the server to say.")) {
                settings.Changed();
            }

            if (Form.FlowCheck(ref first, "Buffs##fellowship", ref data.FellowshipShowBuffs,
                "Shows an icon for each timed helpful spell on each member (hover one for its name); ones that never "
                + "run out, such as an item's cantrips, are left out. The server then sends "
                + "every member's spells each second.")) {
                settings.Changed();
            }

            if (Form.FlowCheck(ref first, "Debuffs##fellowship", ref data.FellowshipShowDebuffs,
                "Shows an icon for each harmful spell on each member (hover one for its name), framed in red.")) {
                settings.Changed();
            }

            RenderLayoutSettings();
        }

        private static string Describe(FellowshipInfo fellowship) {
            var parts = new List<string>();

            if (!fellowship.ShareXp) {
                parts.Add("not sharing XP");
            }
            else {
                parts.Add(fellowship.EvenShare ? "sharing XP evenly" : "sharing XP");
            }

            if (fellowship.Open) {
                parts.Add("open");
            }

            if (fellowship.Locked) {
                parts.Add("locked");
            }

            return "(" + string.Join(", ", parts) + ")";
        }

        private void RenderTable(FellowshipInfo fellowship) {
            if (!ImGui.BeginTable("fellowship-members", 7, Widgets.TableFlags)) {
                return;
            }

            ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("Lvl", ImGuiTableColumnFlags.WidthFixed, 35);
            ImGui.TableSetupColumn("Health", ImGuiTableColumnFlags.WidthFixed, Widgets.BarWidth);
            ImGui.TableSetupColumn("Stamina", ImGuiTableColumnFlags.WidthFixed, Widgets.BarWidth);
            ImGui.TableSetupColumn("Mana", ImGuiTableColumnFlags.WidthFixed, Widgets.BarWidth);
            ImGui.TableSetupColumn("Dist", ImGuiTableColumnFlags.WidthFixed, 45);
            ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthStretch);
            const string vitalTip = "Current / maximum, updated every second.";
            Widgets.HeadersRow(
                "Click a name to select that member. A * marks the leader.",
                "Character level.",
                vitalTip,
                vitalTip,
                vitalTip,
                "How far away they are, when they're near enough for the server to say.",
                "Where they are: a named place or map coordinates, or Indoors.");

            foreach (var member in fellowship.Members ?? new List<FellowInfo>()) {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                MemberName(member, fellowship);

                ImGui.TableNextColumn();
                Widgets.Text(member.Level.ToString());
                ImGui.TableNextColumn();
                Widgets.VitalBar(member.Health, member.MaxHealth, Colors.Health);
                ImGui.TableNextColumn();
                Widgets.VitalBar(member.Stamina, member.MaxStamina, Colors.Stamina);
                ImGui.TableNextColumn();
                Widgets.VitalBar(member.Mana, member.MaxMana, Colors.Mana);
                ImGui.TableNextColumn();
                Widgets.TextDisabled(Display.Distance(member.Distance));
                ImGui.TableNextColumn();
                Widgets.TextDisabled(member.Location ?? "");
            }

            ImGui.EndTable();
        }

        /// <summary>A member's name in the table, greyed out when they're too far away to say how far; a click selects them.</summary>
        private void MemberName(FellowInfo member, FellowshipInfo fellowship) {
            var far = !member.Distance.HasValue;

            if (far) {
                ImGui.PushStyleColor(ImGuiCol.Text, Colors.Muted);
            }

            if (ImGui.Selectable($"{MemberLabel(member, fellowship)}##member-{member.Guid}",
                member.Guid == unchecked((uint)SocialActions.Selection()))) {
                Select(member);
            }

            if (far) {
                ImGui.PopStyleColor();
            }

            MemberTip(member);

            if (ImGui.BeginPopupContextItem($"member-menu-{member.Guid}")) {
                Widgets.TextDisabled($"{member.Name}'s bars in the fellowship window");
                RenderVitalChoices(member.Name);
                RenderPinChoice(member.Name);
                ImGui.EndPopup();
            }
        }

        private static string MemberLabel(FellowInfo member, FellowshipInfo fellowship) {
            return (member.Guid == fellowship.Leader ? "*" : "") + member.Name;
        }

        private static void MemberTip(FellowInfo member) {
            var where = member.Distance.HasValue
                ? $"Click to select {member.Name}, {Display.Distance(member.Distance)} away."
                : $"{member.Name} is too far away to select. {member.Location}";

            Widgets.Tip(where + " Right-click to choose which of their vitals the fellowship window shows.");
        }

        private static void Select(FellowInfo member) {
            CoreManager.Current.Actions.SelectItem(unchecked((int)member.Guid));
        }

        private void Refresh() {
            // a big fellowship is a long reply: every two seconds is plenty
            var big = (feed.Fellowship?.Members?.Count ?? 0) > FellowshipOrder.DefaultMaxMembers;
            feed.RefreshFellowship(big ? BigRefreshInterval : RefreshInterval);
        }

        public void Dispose() {
            window.Dispose();
        }
    }
}
