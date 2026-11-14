using System;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Gameplay Options tab, the game's menu (layout 0x2100002A): seven blue buttons, 240x32 (template 0x10000618),
    /// grouped as the game's are: Exit to Character Selection and Exit Game; Configure Keyboard and Use Mouse Turning
    /// Settings, and In-Game Help Files; Urgent Assistance and Report Abuse. What each does is ClientMenu's; the exits ask
    /// first here.
    /// </summary>
    internal sealed partial class OptionsPanel {
        private const string ConfirmId = "options-confirm";

        // the game's Urgent Assistance and Abuse panels (gmUrgentAssistanceUI, gmAbuseUI), as the panel frame numbers them
        private const uint UrgentAssistancePanel = 0xE;
        private const uint AbusePanel = 0x1;

        /// <summary>The exit waiting for a yes: its question and what it does.</summary>
        private (string Question, Func<bool> Act)? confirming;
        private bool openConfirm;

        private void RenderMenu(bool retail) {
            var buttonSize = new Vector2(Widgets.Scaled(240), Widgets.Scaled(32));
            var gap = Widgets.Scaled(8);
            var group = Widgets.Scaled(18);
            ImGui.Dummy(new Vector2(0, Widgets.Scaled(12)));

            MenuButton("Exit to Character Selection", buttonSize, retail, "Logs out to character selection, as the "
                + "game's button does. It asks first.", () => Confirm("Exit to character selection?",
                ClientMenu.ExitToCharacterSelection));
            ImGui.Dummy(new Vector2(0, gap));
            MenuButton("Exit Game", buttonSize, retail, "Closes the game, as the game's button does. It asks first.",
                () => Confirm("Exit the game?", ClientMenu.ExitGame));
            ImGui.Dummy(new Vector2(0, group));

            MenuButton("Configure Keyboard", buttonSize, retail, "Opens the game's keyboard settings, as the game's button "
                + "does (or, if that can't be done, the game's Options panel on this tab).",
                () => {
                    if (!ClientMenu.ConfigureKeyboard()) {
                        OpenGamePage(GameMenuTab);
                    }
                });
            ImGui.Dummy(new Vector2(0, gap));
            MenuButton("Use Mouse Turning Settings", buttonSize, retail, "Sets the camera and turning to the game's mouse "
                + "turning settings, as the game's button does.", () => ClientMenu.MouseTurningSettings());
            ImGui.Dummy(new Vector2(0, gap));
            MenuButton("In-Game Help Files", buttonSize, retail, "Opens the game's help, as the game's button does (or, if "
                + "that can't be done, the game's Options panel on this tab).",
                () => {
                    if (!ClientMenu.HelpFiles()) {
                        OpenGamePage(GameMenuTab);
                    }
                });
            ImGui.Dummy(new Vector2(0, group));

            MenuButton("Urgent Assistance", buttonSize, retail, "Opens the game's Urgent Assistance panel. (The game's own "
                + "button opens Turbine's old support site, which is gone.)",
                () => ClientElements.SetGamePanel(UrgentAssistancePanel, true));
            ImGui.Dummy(new Vector2(0, gap));
            MenuButton("Report Abuse", buttonSize, retail, "Opens the game's Report Abuse panel. (The game's own button "
                + "opens Turbine's old support site, which is gone.)",
                () => ClientElements.SetGamePanel(AbusePanel, true));

            if (ClientMenu.Broken) {
                Widgets.TextColored(Colors.Danger, "The game's menu couldn't be used (see log.txt).");
            }
        }

        /// <summary>A button centred in the page, as the game's are.</summary>
        private static void MenuButton(string label, Vector2 size, bool retail, string tip, Action act) {
            var width = ImGui.GetContentRegionAvail().X;
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (width - size.X) / 2));

            if (GamePanelArt.Button("options-menu-" + label, label, size, "menu-button", retail)) {
                act();
            }

            Widgets.Tip(tip);
        }

        private void Confirm(string question, Func<bool> act) {
            confirming = (question, act);
            openConfirm = true;

        }

        /// <summary>The exits' question: they can't be taken back.</summary>
        private void RenderConfirm() {
            if (openConfirm) {
                openConfirm = false;
                ImGui.OpenPopup(ConfirmId);
            }

            if (!ImGui.BeginPopup(ConfirmId)) {
                return;
            }

            Widgets.Text(confirming?.Question ?? "");

            if (ImGui.Button("Yes##options-confirm")) {
                confirming?.Act();
                confirming = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel##options-confirm")) {
                confirming = null;
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }
    }
}
