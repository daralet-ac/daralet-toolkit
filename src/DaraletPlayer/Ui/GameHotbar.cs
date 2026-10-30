using System;
using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Hide hotbar (SettingsData.HideGameHotbar): the game's shortcut bar, the rows of slots on its toolbar, hidden while
    /// nothing is on it, for players who use the plugin's hotbars instead. The toolbar (TBAR, a gmFloatyToolbarUI) is a
    /// frame that clips what's in it to its own height: at its shortest, 100, it shows the buttons and one row of
    /// slots, and at 132 both rows. That's how the game shows one row or two, and it never lets the frame get shorter
    /// (its attributes 0x3E and 0x3C, the least and most height, in layout 0x21000070). So the plugin lowers that least
    /// height to the buttons' alone, and makes the frame that height, keeping its bottom edge where it was (its top
    /// when it's in the top half of the screen), which clips every slot away. Hiding the slots themselves, as the first
    /// build did, left the frame as big as before. An item put on the bar (a game key, or the bar from another character)
    /// shows it again within a second. Shown again when the setting goes off, and when the plugin shuts down or reloads.
    /// None of it is saved: the base ResizeTo and MoveTo are called (ClientElements.Place), not the toolbar's own, which
    /// write its place to the player's UI preferences, and the attribute is the layout's again at the next login.
    /// </summary>
    internal class GameHotbar : IDisposable {
        /// <summary>How often it's looked at: whether it's empty, and shrunk again in case the game put it back.</summary>
        private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(1);

        /// <summary>The toolbar's least height, attribute 0x3E (UIElement::ResizeTo keeps to it).</summary>
        private const uint MinHeightAttribute = 0x3E;

        /// <summary>The toolbar frame with only its buttons: a 5-pixel border, the 58-pixel row of buttons, a border.</summary>
        private const int ButtonsHeight = 68;

        private readonly Settings settings;
        private DateTime checkedAt = DateTime.MinValue;

        /// <summary>The toolbar's height before it was shrunk, or 0 while it isn't.</summary>
        private int shrunkFrom;

        /// <summary>Its least height before it was lowered, put back with the height.</summary>
        private int? minHeightWas;

        /// <summary>Whether its bottom edge was kept in place (moving it down), rather than its top.</summary>
        private bool keptBottom;

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

                // nothing of the game's to hide before login is over, and logging out makes the toolbar again
                if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                    shrunkFrom = 0;
                    return;
                }

                if (settings.Data.HideGameHotbar && GameShortcuts.AllEmpty() == true) {
                    Shrink();
                }
                else if (shrunkFrom > 0) {
                    Restore();
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>Makes the toolbar its buttons' height, or again if the game or the player made it taller.</summary>
        private void Shrink() {
            var toolbar = ClientElements.Toolbar;

            if (!ClientElements.TryGetPlace(toolbar, out var x, out var y, out var width, out var height, out var screen)
                || height <= ButtonsHeight) {
                return;
            }

            if (shrunkFrom == 0) {
                shrunkFrom = height;
                minHeightWas = ClientElements.GetIntAttribute(toolbar, MinHeightAttribute);
                keptBottom = y + height / 2 > screen / 2;
            }

            ClientElements.SetIntAttribute(toolbar, MinHeightAttribute, ButtonsHeight);
            var top = keptBottom ? y + height - ButtonsHeight : y;

            if (ClientElements.Place(toolbar, x, top, width, ButtonsHeight)) {
                PluginCore.LogFile($"[hotbar] the game's shortcut bar hidden: it's empty (toolbar {width}x{height} at {x},"
                    + $"{y} made {width}x{ButtonsHeight} at {x},{top})");
            }
        }

        /// <summary>The toolbar back to its height before, wherever it's been moved since.</summary>
        private void Restore() {
            var toolbar = ClientElements.Toolbar;
            var from = shrunkFrom;
            shrunkFrom = 0;

            if (minHeightWas.HasValue) {
                ClientElements.SetIntAttribute(toolbar, MinHeightAttribute, minHeightWas.Value);
            }

            if (!ClientElements.TryGetPlace(toolbar, out var x, out var y, out var width, out var height, out _)) {
                return;
            }

            var top = keptBottom ? y + height - from : y;

            if (ClientElements.Place(toolbar, x, top, width, from)) {
                PluginCore.LogFile($"[hotbar] the game's shortcut bar shown (toolbar {width}x{from} at {x},{top})");
            }
        }

        /// <summary>
        /// The checkbox, for the Config tab's Client UI Override and the Hotbars page: one setting, two places.
        /// </summary>
        public static void RenderCheckbox(ref bool first, Settings settings) {
            if (Form.FlowCheck(ref first, "Hide hotbar##hide-game-hotbar", ref settings.Data.HideGameHotbar,
                "Hides the game's own shortcut bar, the rows of slots on its toolbar, while nothing is on it: for using "
                + "the plugin's hotbars instead. The toolbar gets shorter, keeping its buttons. It shows again as soon "
                + "as something is put on it. To put something on it, untick this.")) {
                settings.Changed();
            }
        }

        public void Dispose() {
            CoreManager.Current.RenderFrame -= Core_RenderFrame;

            if (shrunkFrom > 0) {
                Restore();
            }
        }
    }
}
