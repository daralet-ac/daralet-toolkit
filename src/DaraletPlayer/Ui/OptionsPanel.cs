using System;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Options window (options): the game's Options panel (panel 0xA, layout 0x2100002B) in a window of its own, its
    /// tabs as the game's, Gameplay Options, Character, Chat and Config (tab panel 0x10000208: tabs 0x1000020D, 0x1000020E,
    /// 0x1000050B, 0x1000020F; pages 0x10000212 gmGameplayOptionsUI, 0x10000211 gmCharacterSettingsUI (element type
    /// 0x10000027), 0x1000050C (0x10000042), 0x10000213 gmConfigUI). Gameplay Options is the game's menu of buttons (log
    /// out, exit, help...: OptionsPanel.Menu.cs); Character its options, with Apply, Reset and Defaults
    /// (OptionsPanel.Character.cs). Chat and Config, client preferences with sliders and lists, open the game's own panel
    /// at that tab for now. GamePanelInterceptor closes the game's panel when it opens and shows this instead, while
    /// Replace is on; it sizes by the player and can share the shared window (options). Retail draws the game's art
    /// (Resources/ui menu-button-*, option-*, cast-*, panel-tab-*); Modern ImGui's.
    /// </summary>
    internal sealed partial class OptionsPanel : GamePanel {
        /// <summary>The game's Options panel, as the toolbar's checklist button and the panel frame number it.</summary>
        public const uint GamePanelId = ToolbarOptions.OptionsPanel;

        private const string MenuTab = "menu";
        private const string CharacterTab = "character";
        private const string ChatTab = "chat";
        private const string ConfigTab = "config";

        /// <summary>The game's Options panel's tab panel, and the tabs it opens to for Chat, Config and the menu.</summary>
        private const uint GameTabPanel = 0x10000208;
        private const uint GameMenuTab = 0x1000020D;
        private const uint GameChatTab = 0x1000050B;
        private const uint GameConfigTab = 0x1000020F;

        private readonly GamePanelInterceptor interceptor;
        private string tab = CharacterTab;

        /// <summary>A tab of the game's panel to open once it's showing (OpenGamePage), and until when to keep trying.</summary>
        private uint openTab;
        private DateTime openTabUntil;

        public OptionsPanel(Settings settings)
            : base("options", "Options", settings, new uint[0], new Vector2(500, 120), hideWhen: () => false,
                defaultSize: new Vector2(330, 620)) {
            interceptor = new GamePanelInterceptor("Options", GamePanelId, () => Options.Replace, Window);
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        protected override string ReplaceTip => "Opens the plugin's Options window instead of the game's Options panel, "
            + "from the toolbar's button, the game's key or anywhere: its Gameplay Options menu and its Character options. "
            + "Chat and Config open the game's own panel at that tab.";

        protected override string WindowTip => "Shows the game's Options panel: the Gameplay Options menu (log out, exit, "
            + "help...) and the Character options, set as the game's own buttons and lights do.";

        protected override void RenderContents() {
            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                Widgets.TextDisabled("Not in the world yet.");
                return;
            }

            StepOpenTab();
            var retail = Retail;
            var clicked = GamePanelArt.Tabs("options", new[] {
                ("Gameplay Options", MenuTab, "Log out, exit the game, and the game's help and settings pages."),
                ("Character", CharacterTab, "Your character's options."),
                ("Chat", ChatTab, "The chat windows' options: in the game's own panel for now."),
                ("Config", ConfigTab, "Sound, camera, graphics and input options: in the game's own panel for now."),
            }, tab, retail, 76);

            if (clicked != null) {
                tab = clicked;
            }

            switch (tab) {
                case MenuTab:
                    RenderMenu(retail);
                    break;
                case ChatTab:
                    RenderGamePage("The chat windows' options", GameChatTab, retail);
                    break;
                case ConfigTab:
                    RenderConfig(retail);
                    break;
                default:
                    RenderCharacter(retail);
                    break;
            }

            RenderConfirm();
        }

        /// <summary>A page the plugin hasn't got yet: a word, and a button to the game's own panel at that tab.</summary>
        private void RenderGamePage(string what, uint gameTab, bool retail) {
            ImGui.Spacing();
            var width = ImGui.GetContentRegionAvail().X;
            GamePanelArt.Centred($"{what} are in the game's own Options panel for now.", width, Colors.Detail);
            ImGui.Spacing();
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (width - Widgets.Scaled(240)) / 2));

            if (GamePanelArt.Button($"options-open-{gameTab:X}", "Open the Game's Panel",
                new Vector2(Widgets.Scaled(240), Widgets.Scaled(32)), "menu-button", retail)) {
                OpenGamePage(gameTab);
            }

            Widgets.Tip("Opens the game's Options panel at this tab, and leaves it open until you close it.");
        }

        /// <summary>The game's own Options panel, at one of its tabs: opened now, the tab once the panel's showing.</summary>
        private void OpenGamePage(uint gameTab) {
            interceptor.LetOpen();
            openTab = gameTab;
            openTabUntil = DateTime.UtcNow.AddSeconds(1);
        }

        private void StepOpenTab() {
            if (openTab == 0) {
                return;
            }

            if (ClientElements.OpenTab(GameTabPanel, openTab) || DateTime.UtcNow > openTabUntil) {
                openTab = 0;
            }
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                ConfigFrame();
            }
            catch (Exception ex) {
                PluginCore.LogFile("[options] " + ex);
            }
        }

        public override void Dispose() {
            CoreManager.Current.RenderFrame -= Core_RenderFrame;

            try {
                // a slider tried and not applied isn't kept by a reload either
                if (CoreManager.Current.CharacterFilter.LoginStatus == 3) {
                    PutSlidersBack();
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile("[options] " + ex);
            }

            interceptor.Dispose();
            base.Dispose();
        }
    }
}
