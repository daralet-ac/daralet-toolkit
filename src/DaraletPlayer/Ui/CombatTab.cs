using System;
using System.Linq;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Combat tab: Vitals (your own vitals window's settings, then your target, the Target tab it used to be, with
    /// its target windows), your hotbars, combat text, and Combat Bars, the power bar's and spell bar's settings.
    /// </summary>
    internal class CombatTab : IDisposable {
        private readonly TargetTab targetTab;
        private readonly Hotbars hotbars;
        private readonly CombatLogTab combatLog;
        private readonly GamePanels gamePanels;

        public CombatTab(PlayerFeed feed, Settings settings, ItemIcons icons, GamePanels gamePanels) {
            this.gamePanels = gamePanels;
            targetTab = new TargetTab(feed, settings);
            hotbars = new Hotbars(feed, settings, icons);
            combatLog = new CombatLogTab(settings, icons);
        }

        public void Render() {
            if (!ImGui.BeginTabBar("combat-tabs")) {
                return;
            }

            if (Widgets.TabItem("Vitals", "Your own vitals window (in place of the game's vitals panel or beside it), then "
                + "your selected target's vitals, buffs, debuffs and threat table, and a tracked target you can watch "
                + "alongside it.")) {
                RenderVitals();
                ImGui.EndTabItem();
            }

            if (Widgets.TabItem("Hotbars", "Bars of items you use with a click or a hotkey: potions, kits, weapons to "
                + "swap. Drag items onto them from your packs. Set their size, pages, colours, cooldowns, structure bars and "
                + "keys here.")) {
                hotbars.RenderTab();
                ImGui.EndTabItem();
            }

            if (Widgets.TabItem("Combat Text", "Floating combat text, a window listing recent combat, what's kept out of chat, "
                + "and combat saved to files by day.")) {
                combatLog.Render();
                ImGui.EndTabItem();
            }

            if (Widgets.TabItem("Combat Bars", "Windows of the power or accuracy bar and the spell bar, in place of the "
                + "game's combat panel or beside it: what shows, how they look (the game's art or the plugin's theme), their "
                + "size and text.")) {
                RenderPanels(GamePanels.CombatBarsTab, "combat-bars");
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }

        /// <summary>Your own vitals window's settings first, then the target's, as the Target tab had them.</summary>
        private void RenderVitals() {
            RenderPanels(GamePanels.VitalsTab, "combat-vitals");
            Form.Section("Target");
            targetTab.Render();
        }

        /// <summary>Each game panel whose settings are here, a section each, two abreast when there's room.</summary>
        private void RenderPanels(string tab, string id) {
            var panels = gamePanels.On(tab).ToList();
            Form.Columns(id, panels.Select(p => (Action)p.RenderSettings).ToArray());
        }

        /// <summary>Each on its own, so one that fails can't leave the other's windows drawing.</summary>
        public void Dispose() {
            foreach (var piece in new IDisposable[] { targetTab, hotbars, combatLog }) {
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
