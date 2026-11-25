using System;
using System.Collections.Generic;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;
using UtilityBelt.Service;
using UtilityBelt.Service.Views;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Daralet Toolkit window: a status line, then a tab for each area.
    /// </summary>
    internal class PlayerHud : IDisposable {
        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly UpdateCheck updates;
        private readonly ItemIcons icons;
        private readonly Hud hud;
        private readonly ConfigTab configTab;
        private readonly SocialTab socialTab;
        private readonly CombatTab combatTab;
        private readonly ChatWindows chatWindows;
        private readonly ChatTab chatTab;
        private readonly WorldTab worldTab;
        private readonly InventoryTab inventoryTab;
        private readonly MarketTab marketTab;
        private readonly ArenaTab arenaTab;
        private readonly CharacterTab characterTab;
        private readonly QuestsTab questsTab;
        private readonly LootTab lootTab;
        private readonly LeaderboardsTab leaderboardsTab;
        private readonly SharedWindow sharedWindow;

        /// <summary>The item, creature and spell appraisal windows, sharing one window.</summary>
        private AppraisalGroup appraisalGroup;
        private readonly GameHotbar gameHotbar;
        private readonly GamePanels gamePanels;
        private readonly HideGameUi hideGameUi;
        private readonly EscapeCloses escapeCloses;
        private readonly WorldAppraise worldAppraise;
        private readonly Launcher launcher;
        private readonly BorderlessWindow borderless;
        private readonly MarketApi api = new MarketApi();

        /// <summary>
        /// The tabs in the order they're shown, by name, picture, tooltip and what draws them.
        /// </summary>
        private readonly List<(string Name, TabIcon Icon, string Tip, Action Render)> tabs;

        /// <summary>The tab open last frame, by its place in tabs, ticked in the list of tabs.</summary>
        private int openTab;

        /// <summary>A tab picked from the list of tabs, opened on the next frame (-1 for none).</summary>
        private int selectTab = -1;

        private bool disposed;

        public PlayerHud(PlayerFeed feed, Settings settings, UpdateCheck updates) {
            this.feed = feed;
            this.settings = settings;
            this.updates = updates;
            icons = new ItemIcons(settings);
            ItemUser.Icons = icons;

            socialTab = new SocialTab(feed, settings, icons);
            // before the Combat tab, whose Bars has their settings
            gamePanels = new GamePanels(settings, icons, feed);
            combatTab = new CombatTab(feed, settings, icons, gamePanels);
            // after the combat tab, whose combat log keeps lines out of chat: the chat windows read chat after it has
            chatWindows = new ChatWindows(feed, settings, icons);
            chatTab = new ChatTab(chatWindows, settings);
            worldTab = new WorldTab(feed, settings);
            inventoryTab = new InventoryTab(feed, icons, settings);
            marketTab = new MarketTab(feed, api, icons);
            arenaTab = new ArenaTab(feed, api);
            characterTab = new CharacterTab(feed, settings);
            questsTab = new QuestsTab(feed, settings, worldTab.GoTo);
            lootTab = new LootTab(feed, settings, icons);
            inventoryTab.Looter = lootTab.Render;
            // the hud is made below: these read the field when called
            hideGameUi = new HideGameUi(settings, () => hud.Visible, shown => hud.Visible = shown);
            launcher = new Launcher(settings, () => hud.Visible, Toggle);
            gameHotbar = new GameHotbar(settings);
            borderless = new BorderlessWindow(settings);
            sharedWindow = new SharedWindow(settings, inventoryTab.CarriedWindow, worldTab.MapWindow, socialTab.Window,
                questsTab.Window, characterTab.Window,
                ("attributes", "Attributes", gamePanels.Get<AttributesPanel>().Window),
                ("spellbook", "Spellbook", gamePanels.Get<SpellbookPanel>().Window),
                ("options", "Options", gamePanels.Get<OptionsPanel>().Window),
                ("buffs", "Buffs", gamePanels.Get("buffs").Window),
                ("debuffs", "Debuffs", gamePanels.Get("debuffs").Window),
                ("vitae", "Vitae", gamePanels.Get("vitae").Window),
                ("link", "Link", gamePanels.Get<LinkPanel>().Window),
                ("book", "Book", gamePanels.Get<BookPanel>().Window));
            configTab = new ConfigTab(settings, new ClientUiOverride(settings, feed, lootTab.CorpseWindow, sharedWindow,
                gamePanels), chatTab, hideGameUi, launcher, borderless);
            leaderboardsTab = new LeaderboardsTab(feed, api);
            // its buttons open the plugin's windows where Client UI Override's Windows row has them ticked
            var attributes = gamePanels.Get<AttributesPanel>();
            var spellbook = gamePanels.Get<SpellbookPanel>();
            var options = gamePanels.Get<OptionsPanel>();
            gamePanels.Add(new ToolbarPanel(settings, feed, new Dictionary<string, (Func<bool>, FloatingWindow)> {
                ["inventory"] = (() => settings.Data.ToolbarInventory, inventoryTab.CarriedWindow),
                ["quests"] = (() => settings.Data.ToolbarQuests, questsTab.Window),
                ["social"] = (() => settings.Data.ToolbarSocial, socialTab.Window),
                ["map"] = (() => settings.Data.ToolbarMap, worldTab.MapWindow),
                ["attributes"] = (() => attributes.Options.Replace, attributes.Window),
                ["spellbook"] = (() => spellbook.Options.Replace, spellbook.Window),
                ["options"] = (() => options.Options.Replace, options.Window),
            }));

            // Attach to toolbar goes on the plugin's Toolbar window while it stands in for the game's
            var toolbarPanel = gamePanels.Get<ToolbarPanel>();
            sharedWindow.PluginToolbar = () => toolbarPanel.Options.Replace ? toolbarPanel.Window : null;
            // each indicator opens its window where it takes the game's panel's place
            gamePanels.Add(new IndicatorsPanel(settings, new Dictionary<string, (Func<bool>, FloatingWindow)> {
                [IndicatorOptions.Burden] = (() => settings.Data.ToolbarCharacter, characterTab.Window),
                [IndicatorOptions.Buffs] = (() => gamePanels.Get("buffs").Options.Replace, gamePanels.Get("buffs").Window),
                [IndicatorOptions.Debuffs] = (() => gamePanels.Get("debuffs").Options.Replace,
                    gamePanels.Get("debuffs").Window),
                [IndicatorOptions.Vitae] = (() => gamePanels.Get("vitae").Options.Replace, gamePanels.Get("vitae").Window),
                [IndicatorOptions.Link] = (() => gamePanels.Get<LinkPanel>().Options.Replace,
                    gamePanels.Get<LinkPanel>().Window),
            }));
            // its dungeon map is the Dungeon window's, which the World tab makes
            var radar = new RadarPanel(settings, feed, worldTab.DungeonWindow);
            gamePanels.Add(radar);
            worldTab.RadarSettings = radar.RenderSettings;
            // the hud is made below: these read the field when called
            escapeCloses = new EscapeCloses(settings, () => hud.Visible, () => hud.Visible = false, options,
                inventoryTab.CarriedWindow, inventoryTab.BankWindow, characterTab.Window, questsTab.Window);
            worldAppraise = new WorldAppraise(settings);

            Appraisal.Start(feed, settings, icons);
            appraisalGroup = new AppraisalGroup(settings, Appraisal.FirstWindow,
                gamePanels.Get<CreatureAppraisalPanel>()?.Window, gamePanels.Get<SpellbookPanel>()?.Appraisal.Window);
            ItemTooltip.Scale = () => settings.Data.ClampedItemTextScale;
            Panes.Settings = settings;
            Widgets.RetailBars = () => settings.Data.RetailVitalBars;
            Widgets.BoldVitals = () => settings.Data.BoldVitalText;
            Widgets.TooltipsHidden = () => settings.Data.TooltipsNeedAlt
                && !Hotkeys.IsDown(settings.Data.TooltipKey != 0 ? settings.Data.TooltipKey : Keys.Alt);
            RightClick.AppraisesNow = () => settings.Data.RightClickAppraises
                && !Hotkeys.IsDown(settings.Data.MenuKey != 0 ? settings.Data.MenuKey : Keys.Alt);

            tabs = new List<(string, TabIcon, string, Action)> {
                ("Config", TabIcon.Gear, "Settings profiles: every setting of the plugin's as a set each character picks "
                    + "from, saved to and added from files to keep or share. Client UI Override: the plugin's windows "
                    + "instead of the game's panels. And Chat: the plugin's chat windows.", configTab.Render),
                ("World", TabIcon.Globe, "A map of Dereth with Daralet's towns, dungeons, lifestones and portals, you and "
                    + "your fellowship, and an arrow to point you anywhere on it.", worldTab.Render),
                ("Character", TabIcon.Person, "What the game's Character Information panel says (age, deaths, innate "
                    + "attributes, augmentations, burden), your XP and rested experience (with an XP bar of its own), then "
                    + "your character stats, as the Combat Focus appraisal shows them: gear mods, ratings, ward, armor "
                    + "level, resistances and jewel effects.", characterTab.Render),
                ("Social", TabIcon.People, "Your allegiance, your fellowship (its members' vitals, its buttons, a vitals "
                    + "window you can place anywhere), Find a group (/lfgroup and /lfmore), your friends and your "
                    + "squelches.", socialTab.Render),
                ("Combat", TabIcon.Axe, "Your selected target's vitals, buffs, debuffs and threat table, a "
                    + "tracked target to watch alongside it, hotbars of items to use with a click or a hotkey, and "
                    + "floating combat text and a combat log.", combatTab.Render),
                ("Inventory", TabIcon.Backpack, "The packs you carry, your bank's packs while it's open, every /bank "
                    + "command as a button, and the looter with its loot profiles.", inventoryTab.Render),
                ("Quests", TabIcon.Quill, "Your quests, what the game's Contracts panel lists: their progress and timers, "
                    + "who gives them and who takes them back, and where, with an arrow to point you there.",
                    questsTab.Render),
                ("Market", TabIcon.CoinPurse, "Your listings, sales and expired items, the Market Broker's actions near "
                    + "the market stand, and a search of everything on the market.", marketTab.Render),
                ("Arena", TabIcon.CrossedSwords, "Your arena ratings and where you stand, the /arena commands as buttons, "
                    + "duels to watch, and the top ratings.", arenaTab.Render),
                ("Leaderboards", TabIcon.Medal, "The leaderboards from the Daralet website: level, arena, capstones, "
                    + "deaths and more.", leaderboardsTab.Render),
            };

            hud = UBService.Huds.CreateHud("Daralet Toolkit");
            hud.ShowInBar = true;
            hud.OnPreRender += Hud_OnPreRender;
            hud.OnRender += Hud_OnRender;
            settings.Swapped += Settings_Swapped;
            GameDrag.Start();
            CursorKeeper.Start();
            KeyReleases.Start();
        }

        /// <summary>Another settings profile: what was worked out from the old one's starts again.</summary>
        private void Settings_Swapped() {
            InventoryBrowser.ForgetWornSizes();
            lootTab.SettingsSwapped();
        }

        public void Toggle() {
            hud.Visible = !hud.Visible;
        }

        /// <summary>/dap chat: the main chat window shown or hidden (the chat windows turned on to show it).</summary>
        public void ToggleChat() {
            var data = settings.Data.Chat;
            var window = chatWindows.Window(0);

            if (!data.On || !window.Visible) {
                data.On = true;
                window.Visible = true;
                settings.Changed();
                chatWindows.Focus(chatWindows.TypingWindow());
                return;
            }

            window.Visible = false;
        }

        private void Hud_OnPreRender(object sender, EventArgs e) {
            try {
                ImGui.SetNextWindowSize(new Vector2(760, 620), ImGuiCond.FirstUseEver);
                ImGui.SetNextWindowSizeConstraints(new Vector2(360, 260), new Vector2(float.MaxValue, float.MaxValue));

                if (!disposed) {
                    UiThemeStyle.Apply(UiThemes.Find(settings.Data.Theme));
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void Hud_OnRender(object sender, EventArgs e) {
            try {
                if (disposed) {
                    return;
                }

                ImGui.SetWindowFontScale(settings.Data.ClampedTextScale);
                Widgets.TextScale = settings.Data.ClampedTextScale;

                // a drag from the game's pack let go over this window mustn't drop the item on the ground
                GameDrag.Cover();

                if (!RenderStatus()) {
                    return;
                }

                // the tabs are pictures, so a dozen fit even a narrow window; with big text they shrink to fit rather
                // than scroll, so each tab's box is where it's drawn. The list button at the left names them all: ImGui's
                // own would show their labels, which are only spaces.
                if (ImGui.BeginTabBar("DaraletPlayerTabs", ImGuiTabBarFlags.FittingPolicyResizeDown)) {
                    RenderTabList();
                    var select = selectTab;
                    selectTab = -1;

                    for (var i = 0; i < tabs.Count; i++) {
                        var (name, icon, tip, render) = tabs[i];

                        if (TabIcons.TabItem(icon, name, tip, select == i)) {
                            openTab = i;
                            render();
                            ImGui.EndTabItem();
                        }
                    }

                    ImGui.EndTabBar();
                }

                // the game's scroll bar over the window's own, if it has one
                Scrollbars.Draw();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
            finally {
                // only this window has the theme: other plugins' windows keep their own look
                UiThemeStyle.Restore();
            }
        }

        /// <summary>
        /// The button at the left of the tab bar, like ImGui's tab list button, listing the tabs by name: a pick opens it
        /// on the next frame.
        /// </summary>
        private void RenderTabList() {
            var arrow = ImGui.GetFontSize() * 0.6f;
            var label = new string(' ', Math.Max(1, (int)Math.Ceiling(arrow / Math.Max(1, ImGui.CalcTextSize(" ").X))));

            if (ImGui.TabItemButton(label + "###tab-list", ImGuiTabItemFlags.Leading | ImGuiTabItemFlags.NoTooltip)) {
                ImGui.OpenPopup("tab-list");
            }

            var centre = (ImGui.GetItemRectMin() + ImGui.GetItemRectMax()) / 2;
            var half = arrow / 2;
            ImGui.GetWindowDrawList().AddTriangleFilled(centre + new Vector2(-half, -half * 0.5f),
                centre + new Vector2(half, -half * 0.5f), centre + new Vector2(0, half * 0.6f),
                ImGui.GetColorU32(ImGuiCol.Text));
            Widgets.Tip("Every tab, by name.");

            if (!ImGui.BeginPopup("tab-list")) {
                return;
            }

            for (var i = 0; i < tabs.Count; i++) {
                if (ImGui.Selectable(tabs[i].Name, i == openTab)) {
                    selectTab = i;
                }
            }

            ImGui.EndPopup();
        }

        /// <summary>
        /// Draws the line at the top of the window. Returns false when the tabs can't be used yet.
        /// </summary>
        private bool RenderStatus() {
            switch (feed.Status) {
                case FeedStatus.Connecting:
                    Widgets.TextDisabled("Connecting to the server...");
                    return false;
                case FeedStatus.Unsupported:
                    Widgets.TextColored(Colors.Warning,
                        "This server doesn't support this plugin. It only works on the Daralet server.");
                    return false;
            }

            Widgets.TextDisabled(feed.Hello.Name);
            Widgets.Tip("The character you're on. Type /dap to show or hide this window.");

            RenderVersion();
            RenderUpdate();
            RenderCompactToggle();
            return true;
        }

        /// <summary>The plugin's version after the character's name, so a player can say which they have.</summary>
        private void RenderVersion() {
            if (updates == null) {
                return;
            }

            ImGui.SameLine();
            Widgets.TextDisabled($"v{updates.Current.ToString(3)}");
            Widgets.Tip($"The version of Daralet Toolkit you have. {updates.Status}");
        }

        /// <summary>An Update button after the character's name and version, while a newer release is out.</summary>
        private void RenderUpdate() {
            var newer = updates?.Newer;

            if (newer == null) {
                return;
            }

            var version = newer.Version.ToString(3);
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, Colors.Warning);
            var clicked = ImGui.SmallButton($"Update: {version}");
            ImGui.PopStyleColor();

            if (clicked) {
                try {
                    // the default browser, as any link
                    System.Diagnostics.Process.Start(newer.Url);
                }
                catch (Exception ex) {
                    PluginCore.Log($"Couldn't open {newer.Url}: {ex.Message}");
                }
            }
            Widgets.Tip($"Daralet Toolkit {version} is out (you have {updates.Current.ToString(3)}). Opens its download page in "
                + $"your browser: close the game and the Decal agent, then run DaraletToolkit-Setup-{version}.exe from there. "
                + "Your settings are kept.");
        }

        /// <summary>
        /// The Compact checkbox, at the right of the top line. Plugin appraisal was beside it until it became Client UI
        /// Override's Item Appraisal (November 2026).
        /// </summary>
        private void RenderCompactToggle() {
            const string label = "Compact";
            var width = ImGui.GetFrameHeight() + ImGui.GetStyle().ItemInnerSpacing.X + ImGui.CalcTextSize(label).X;

            ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - width));

            if (ImGui.Checkbox(label, ref settings.Data.CompactLists)) {
                settings.Changed();
            }
            Widgets.Tip("Hides the item icons in the bank, loot and market lists, for shorter rows.");
        }

        /// <summary>
        /// Closes every piece on its own, so one that fails can't leave the rest drawing and asking the server for data after
        /// the plugin has been shut down.
        /// </summary>
        public void Dispose() {
            disposed = true;
            settings.Swapped -= Settings_Swapped;
            hud.OnPreRender -= Hud_OnPreRender;
            hud.OnRender -= Hud_OnRender;

            try {
                UiThemeStyle.Restore();
                GameDrag.Stop();
                CursorKeeper.Stop();
                KeyReleases.Stop();
                appraisalGroup?.Dispose();
                Appraisal.Stop();
                ItemUser.Icons = null;
                ItemTooltip.Scale = () => 1;
                Panes.Settings = null;
                Widgets.RetailBars = () => false;
                Widgets.BoldVitals = () => false;
                Widgets.TooltipsHidden = () => false;
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }

            // the game's UI back first, whatever fails after
            var pieces = new IDisposable[] {
                hideGameUi, borderless, escapeCloses, worldAppraise, launcher, hud, sharedWindow, gameHotbar, gamePanels,
                chatWindows, socialTab, combatTab, worldTab, inventoryTab, questsTab, characterTab, marketTab,
                lootTab, api, icons,
            };

            foreach (var piece in pieces) {
                try {
                    piece.Dispose();
                }
                catch (Exception ex) {
                    PluginCore.Log(ex);
                }
            }

            try {
                GameFonts.Free();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }
    }
}
