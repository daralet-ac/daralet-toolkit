using System;
using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Every GamePanel, the game's panels a plugin window can take the place of: their Replace checkboxes in Client UI
    /// Override's row, and their looks and windows' settings where they belong (SettingsTab: the Combat tab's
    /// Vitals and Combat Bars, CombatTab) or, for one with none, in Client UI Override's Game panels. A new panel is a
    /// GamePanel made here.
    /// </summary>
    internal sealed class GamePanels : IDisposable {
        // where on the Combat tab panels' settings go: its Vitals sub tab (your own vitals, before the target's) and its
        // Combat Bars sub tab (the power bar and the spell bar)
        public const string VitalsTab = "Combat tab's Vitals";
        public const string CombatBarsTab = "Combat tab's Combat Bars";

        private readonly List<GamePanel> panels;

        public GamePanels(Settings settings, ItemIcons icons, PlayerFeed feed) {
            panels = new List<GamePanel> {
                new VitalsPanel(settings),
                new PowerBarPanel(settings),
                new SpellBarPanel(settings, icons),
                new AttributesPanel(settings, icons, feed),
                new SpellbookPanel(settings, icons),
                new OptionsPanel(settings),
                new EffectsPanel(settings, icons, beneficial: true),
                new EffectsPanel(settings, icons, beneficial: false),
                new VitaePanel(settings),
                new VendorPanel(settings, icons, feed),
                new CreatureAppraisalPanel(settings, icons),
            };
        }

        public IReadOnlyList<GamePanel> All => panels;

        /// <summary>The panel of this kind, for another that opens its window (the Toolbar's Character button).</summary>
        public T Get<T>() where T : GamePanel => panels.OfType<T>().FirstOrDefault();

        /// <summary>The panel by its key, for a kind made more than once (Buffs and Debuffs are both EffectsPanels).</summary>
        public GamePanel Get(string key) => panels.FirstOrDefault(p => p.Key == key);

        /// <summary>A panel that needs what PlayerHud makes after GamePanels (the Toolbar, the plugin's windows).</summary>
        public void Add(GamePanel panel) {
            panels.Add(panel);
        }

        /// <summary>The panels whose settings are on this tab.</summary>
        public IEnumerable<GamePanel> On(string tab) => panels.Where(p => p.SettingsTab == tab);

        /// <summary>Each panel's Replace, in Client UI Override's row of checkboxes.</summary>
        public void RenderToggles(ref bool first) {
            foreach (var panel in panels) {
                panel.RenderToggle(ref first);
            }
        }

        /// <summary>
        /// The settings of each panel with no tab of its own, a section each, folded under Game panels; nothing when every
        /// panel has a tab.
        /// </summary>
        public void RenderSettings() {
            var homeless = On(null).ToList();

            if (homeless.Count == 0) {
                return;
            }

            // folded, like a tab's Settings: Client UI Override is one section of the Config tab
            var open = ImGui.CollapsingHeader("Game panels##client-ui-game-panels");
            Widgets.Tip("The plugin's windows that can take the place of the game's own panels: each one's look (the "
                + "game's art or the plugin's theme) and its window.");

            if (!open) {
                return;
            }

            using var fold = Form.Folded();

            foreach (var panel in homeless) {
                panel.RenderSettings();
            }
        }

        public void Dispose() {
            foreach (var panel in panels) {
                try {
                    panel.Dispose();
                }
                catch (Exception ex) {
                    PluginCore.Log(ex);
                }
            }
        }
    }
}
