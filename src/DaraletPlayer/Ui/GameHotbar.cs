using System;
using System.Linq;
using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Hide hotbar (SettingsData.HideGameHotbar): the game's shortcut bar, its two rows of slots on the toolbar, hidden
    /// while nothing is on it, for players who use the plugin's hotbars instead. The slots and the caps at each row's ends
    /// are children of the toolbar of their own (its layout 0x21000016), with nothing behind them but the toolbar's frame,
    /// so each is hidden (ClientElements.SetVisible) and the rest of the toolbar stays. An item put on the bar (a game
    /// key, or the bar from another character) shows it again within a second. Shown again when the setting goes off,
    /// and when the plugin shuts down or reloads.
    /// </summary>
    internal class GameHotbar : IDisposable {
        /// <summary>The ends of each row: 6 pixels of the bar's own art, either side of its slots.</summary>
        private static readonly uint[] Caps = { 0x100001A6, 0x100001B0, 0x100006B6, 0x100006C0 };

        /// <summary>How often it's looked at: whether it's empty, and hidden again in case the game showed it.</summary>
        private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(1);

        private readonly Settings settings;
        private DateTime checkedAt = DateTime.MinValue;

        /// <summary>Whether the plugin has the bar hidden now.</summary>
        private bool hidden;

        public GameHotbar(Settings settings) {
            this.settings = settings;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (DateTime.UtcNow - checkedAt < CheckEvery) {
                    return;
                }

                checkedAt = DateTime.UtcNow;

                // nothing of the game's to hide before login is over
                if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                    hidden = false;
                    return;
                }

                var hide = settings.Data.HideGameHotbar && GameShortcuts.AllEmpty() == true;

                // hidden again each time, in case the game showed it (as Hide game UI does)
                if (hide || hidden) {
                    SetShown(!hide);
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void SetShown(bool shown) {
            foreach (var id in GameShortcuts.SlotIds.Concat(Caps)) {
                ClientElements.SetVisible(id, shown);
            }

            if (hidden == shown) {
                PluginCore.LogFile($"[hotbar] the game's shortcut bar {(shown ? "shown" : "hidden: it's empty")}");
            }

            hidden = !shown;
        }

        /// <summary>
        /// The checkbox, for the Config tab's Client UI Override and the Hotbars page: one setting, two places.
        /// </summary>
        public static void RenderCheckbox(ref bool first, Settings settings) {
            if (Form.FlowCheck(ref first, "Hide hotbar##hide-game-hotbar", ref settings.Data.HideGameHotbar,
                "Hides the game's own shortcut bar, the two rows of slots on its toolbar, while nothing is on it: for "
                + "using the plugin's hotbars instead. It shows again as soon as something is put on it. To put "
                + "something on it, untick this.")) {
                settings.Changed();
            }
        }

        public void Dispose() {
            CoreManager.Current.RenderFrame -= Core_RenderFrame;

            if (hidden) {
                SetShown(true);
            }
        }
    }
}
