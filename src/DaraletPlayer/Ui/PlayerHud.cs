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
        private readonly ToolbarButtons toolbarButtons;
        private readonly HideGameUi hideGameUi;
        private readonly EscapeCloses escapeCloses;
        private readonly Launcher launcher;
        private readonly BorderlessWindow borderless;
        private readonly MarketApi api = new MarketApi();

        /// <summary>
        /// The tabs in the order they're shown, by name, tooltip and what draws them.
        /// </summary>
        private readonly List<(string Name, string Tip, Action Render)> tabs;

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
                ("vitae", "Vitae", gamePanels.Get("vitae").Window));
            configTab = new ConfigTab(settings, new ClientUiOverride(settings, feed, lootTab.CorpseWindow, sharedWindow,
                gamePanels), hideGameUi, launcher, borderless);
            leaderboardsTab = new LeaderboardsTab(feed, api);
            toolbarButtons = new ToolbarButtons(settings, icons, inventoryTab.CarriedWindow, questsTab.Window,
                worldTab.MapWindow, characterTab.Window, socialTab.Window);
            // its buttons open these where Client UI Override's toolbar buttons say so, so it comes after them
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
            }));
            // its dungeon map is the Dungeon window's, which the World tab makes
            var radar = new RadarPanel(settings, feed, worldTab.DungeonWindow);
            gamePanels.Add(radar);
            worldTab.RadarSettings = radar.RenderSettings;
            // the hud is made below: these read the field when called
            escapeCloses = new EscapeCloses(settings, () => hud.Visible, () => hud.Visible = false,
                inventoryTab.CarriedWindow, inventoryTab.BankWindow, characterTab.Window, questsTab.Window);

            Appraisal.Start(feed, settings, icons);
            appraisalGroup = new AppraisalGroup(settings, Appraisal.FirstWindow,
                gamePanels.Get<CreatureAppraisalPanel>()?.Window, gamePanels.Get<SpellbookPanel>()?.Appraisal.Window);
            ItemTooltip.Scale = () => settings.Data.ClampedItemTextScale;
            Panes.Settings = settings;
            Widgets.RetailBars = () => settings.Data.RetailVitalBars;
            Widgets.BoldVitals = () => settings.Data.BoldVitalText;
            Widgets.TooltipsHidden = () => settings.Data.TooltipsNeedAlt
                && !Hotkeys.IsDown(settings.Data.TooltipKey != 0 ? settings.Data.TooltipKey : Keys.Alt);

            tabs = new List<(string, string, Action)> {
                ("Config", "Settings profiles: every setting of the plugin's as a set each character picks from, saved to "
                    + "and added from files to keep or share. And Client UI Override: the game toolbar's buttons opening "
                    + "the plugin's windows instead of the game's panels.", configTab.Render),
                ("World", "A map of Dereth with Daralet's towns, dungeons, lifestones and portals, you and your fellowship, "
                    + "and an arrow to point you anywhere on it.", worldTab.Render),
                ("Social", "Your allegiance, your fellowship (its members' vitals, its buttons, a vitals window you can "
                    + "place anywhere), Find a group (/lfgroup and /lfmore), your friends and your squelches.",
                    socialTab.Render),
                ("Chat", "Chat windows of the plugin's own: a main window with tabs, others popped out, each showing what you "
                    + "pick of the game's chat, with sharp text at any size, items linked in chat, copying, and Hide game "
                    + "chat to use them instead of the game's.", chatTab.Render),
                ("Combat", "Your selected target's vitals, buffs, debuffs and threat table, a tracked target to watch "
                    + "alongside it, hotbars of items to use with a click or a hotkey, and floating combat text and a "
                    + "combat log.", combatTab.Render),
                ("Character", "What the game's Character Information panel says (age, deaths, innate attributes, "
                    + "augmentations, burden), your XP and rested experience (with an XP bar of its own), then your character "
                    + "stats, as the Combat Focus appraisal shows them: gear mods, ratings, ward, armor level, resistances "
                    + "and jewel effects.", characterTab.Render),
                ("Quests", "Your quests, what the game's Contracts panel lists: their progress and timers, who gives them "
                    + "and who takes them back, and where, with an arrow to point you there.", questsTab.Render),
                ("Loot", "Loot rules in named loot profiles you can swap, and what happens when you open a corpse or a "
                    + "chest: loot, salvage, or just show what matches.", lootTab.Render),
                ("Inventory", "The packs you carry, your bank's packs while it's open, and every /bank command as a "
                    + "button.", inventoryTab.Render),
                ("Market", "Your listings, sales and expired items, the Market Broker's actions near the market stand, and "
                    + "a search of everything on the market.", marketTab.Render),
                ("Arena", "Your arena ratings and where you stand, the /arena commands as buttons, duels to watch, and the "
                    + "top ratings.", arenaTab.Render),
                ("Leaderboards", "The leaderboards from the Daralet website: level, arena, capstones, deaths and more.",
                    leaderboardsTab.Render),
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

                // the tabs don't fit a narrow window, so they scroll, and the list button at the left jumps to any of them
                var tabBarFlags = ImGuiTabBarFlags.FittingPolicyScroll | ImGuiTabBarFlags.TabListPopupButton;

                if (ImGui.BeginTabBar("DaraletPlayerTabs", tabBarFlags)) {
                    foreach (var (name, tip, render) in tabs) {
                        if (Widgets.TabItem(name, tip)) {
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

        /// <summary>The Plugin appraisal and Compact checkboxes, at the right of the top line.</summary>
        private void RenderCompactToggle() {
            const string appraisal = "Plugin appraisal";
            const string label = "Compact";
            var style = ImGui.GetStyle();
            var box = ImGui.GetFrameHeight() + style.ItemInnerSpacing.X;
            var width = box * 2 + ImGui.CalcTextSize(appraisal).X + style.ItemSpacing.X + ImGui.CalcTextSize(label).X;

            ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - width));

            if (ImGui.Checkbox(appraisal, ref settings.Data.AppraiseInPlugin)) {
                settings.Changed();
            }
            Widgets.Tip("Appraise, in an item's right-click menu in the Corpse window or the bank, shows it in the plugin's "
                + "Appraisal window: every stat, how well each rolled, what your loot rules make of it and how it compares "
                + "with what you're wearing. Appraise in new window opens another, to keep several. Off, Appraise selects "
                + "the item instead, for the game's own appraisal (press your appraise key): a plugin can't open the game's "
                + "appraisal panel.");

            ImGui.SameLine();

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
                hideGameUi, borderless, escapeCloses, launcher, hud, toolbarButtons, sharedWindow, gameHotbar, gamePanels,
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
        }
    }
}
