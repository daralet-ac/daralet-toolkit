using System;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Social tab, the game's social panel and more: Allegiance, Fellowship (with the fellowship window), Find a group,
    /// Friends and Squelch. The Social window (social, hidden to start) is the same tabs in a FloatingWindow, which the
    /// game toolbar's Social button opens with Client UI Override's Social on.
    /// </summary>
    internal class SocialTab : IDisposable {
        private readonly PlayerFeed feed;
        private readonly FellowshipTab fellowship;
        private readonly FindGroupTab findGroup;
        private readonly FriendsTab friends;
        private readonly SquelchTab squelch;
        private readonly AllegianceTab allegiance;
        private readonly FloatingWindow window;

        /// <summary>Set by Fellowship's Find a group button: Find a group is the open tab next frame.</summary>
        private bool openFindGroup;

        public SocialTab(PlayerFeed feed, Settings settings, ItemIcons icons) {
            this.feed = feed;
            fellowship = new FellowshipTab(feed, settings, icons, () => openFindGroup = true);
            findGroup = new FindGroupTab(feed, settings);
            friends = new FriendsTab(feed);
            squelch = new SquelchTab(feed);
            allegiance = new AllegianceTab(feed);

            window = new FloatingWindow("social", "Social", settings, new Vector2(340, 140), RenderWindow,
                visibleByDefault: false, defaultSize: new Vector2(520, 480));
        }

        public FloatingWindow Window => window;

        public void Render() {
            RenderSettings();
            RenderTabs("social-tabs");
        }

        private void RenderSettings() {
            if (!Form.SettingsHeader("social-settings", "The Social window: the same tabs as here, in a window of their own, "
                + "which the game toolbar's Social button opens with Client UI Override's Social ticked (the Config tab).")) {
                return;
            }

            Form.Section("Social window");
            window.RenderControls("A window with these same tabs that stays on screen while you play. With Client UI "
                + "Override's Social ticked (the Config tab), the game toolbar's Social button opens it instead of the "
                + "game's panel.");
        }

        private void RenderWindow() {
            RenderTabs("social-window-tabs");
        }

        private void RenderTabs(string id) {
            if (!ImGui.BeginTabBar(id)) {
                return;
            }

            if (Widgets.TabItem("Allegiance", "Your patron, monarch and vassals, the XP passed between you, and Swear, "
                + "Break and Kick.")) {
                allegiance.Render();
                ImGui.EndTabItem();
            }

            if (Widgets.TabItem("Fellowship", "Your fellowship's members and their vitals, its buttons (Leader, Dismiss, "
                + "Open, Recruit, Quit, Disband) and the fellowship window; or, out of one, Create Fellowship.")) {
                fellowship.Render();
                ImGui.EndTabItem();
            }

            var select = openFindGroup;
            openFindGroup = false;

            if (Widgets.TabItem("Find a group", "Look for a group, or for more players for your fellowship, and see who else "
                + "is looking: /lfgroup and /lfmore as a form.", select)) {
                findGroup.Render();
                ImGui.EndTabItem();
            }

            if (Widgets.TabItem("Friends", "Your friends, who's online, Appear Offline, and invites and tells from a "
                + "right-click.")) {
                friends.Render();
                ImGui.EndTabItem();
            }

            if (Widgets.TabItem("Squelch", "The characters and accounts you've squelched, and squelching more.")) {
                squelch.Render();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        public void Dispose() {
            try {
                window.Dispose();
            }
            finally {
                fellowship.Dispose();
            }
        }
    }
}
