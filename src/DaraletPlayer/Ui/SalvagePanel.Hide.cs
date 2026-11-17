using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Replace (SettingsData.SalvageReplace, Client UI Override's Salvage panel): while the game's salvage panel is up
    /// (PanelDrop.GamePanelShowing, a guess), ENVP, the panel along the bottom it's shown in, is moved off the left of the
    /// screen as the Vendor window's Replace moves it (ClientElements.Place, which saves nothing), and the Salvage window
    /// shows instead. It's put back where it was when the salvage panel goes, a vendor, trade or container takes ENVP,
    /// Replace goes off or the plugin stops; looked at again once a second in case the game moved it back.
    ///
    /// The game's panel is left open off the screen, so closing the Salvage window hides ENVP (UIElement::SetVisible), which
    /// for the vendor closed the vendor (gmVendorUI::OnVisibilityChanged) and is taken to close the salvage panel the same
    /// way; ENVP is then put back in its place, hidden, for the game to show next time. `[salvage]` lines in log.txt.
    /// </summary>
    internal sealed partial class SalvagePanel {
        /// <summary>Where ENVP was before it was moved, or null while it's where the game put it.</summary>
        private (int X, int Y, int Width, int Height)? moved;

        private int framesToCheck;

        /// <summary>Replace's checkbox, in Client UI Override's row of them.</summary>
        public static void RenderToggle(ref bool first, Settings settings) {
            if (Form.FlowCheck(ref first, "Salvage panel##client-ui-salvage", ref settings.Data.SalvageReplace,
                "Shows the plugin's Salvage window in place of the game's salvage panel (an Ust's) when it opens. The "
                + "game's can't be hidden without closing it, so the panel along the bottom it's in is moved off the "
                + "screen until it closes; closing the Salvage window closes it. The window's own settings are in the "
                + "Inventory tab's Settings.")) {
                settings.Changed();
            }
        }

        private void HideFrame() {
            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                moved = null;
                return;
            }

            var want = settings.Data.SalvageReplace && !HideGameUi.On && PanelDrop.GamePanelShowing();

            if (!want) {
                PutBack();
                return;
            }

            // the game's panel is going off the screen, so this window is the only way to use it
            if (!isOpen || !Window.Visible) {
                Open(fromGame: true);
            }

            if (moved.HasValue && --framesToCheck > 0) {
                return;
            }

            framesToCheck = 60;

            if (!ClientElements.TryGetPlace(ClientElements.Environment, out var x, out var y, out var width, out var height,
                out _)) {
                return;
            }

            var off = OffScreen(width);

            if (x == off) {
                // ours, or the Vendor window's not yet put back: either way not a place to go back to
                return;
            }

            moved = (x, y, width, height);

            if (ClientElements.Place(ClientElements.Environment, off, y, width, height)) {
                PluginCore.LogFile($"[salvage] the game's salvage panel moved off the screen from {x},{y} ({width}x{height})");
            }
        }

        private void PutBack() {
            if (!moved.HasValue) {
                return;
            }

            var place = moved.Value;
            moved = null;

            if (ClientElements.Place(ClientElements.Environment, place.X, place.Y, place.Width, place.Height)) {
                PluginCore.LogFile($"[salvage] the game's salvage panel put back at {place.X},{place.Y}");
            }
        }

        /// <summary>
        /// The window's close button and Close: hidden, and with the game's panel off the screen, that closed too, so it
        /// doesn't show again when ENVP is put back.
        /// </summary>
        private void CloseWindow() {
            if (!isOpen) {
                return;
            }

            // as the game's panel empties when it closes; anything already sent to be salvaged still goes
            isOpen = false;
            queued.Clear();
            rulesChecking = new System.Collections.Generic.List<uint>();

            if (!moved.HasValue) {
                return;
            }

            var hidden = ClientElements.SetVisible(ClientElements.Environment, false);
            PluginCore.LogFile("[salvage] the Salvage window closed: the game's salvage panel "
                + (hidden ? "hidden" : "couldn't be hidden"));
            PutBack();
        }

        /// <summary>The same place the Vendor window's Replace uses.</summary>
        private static int OffScreen(int width) {
            return -(width + 200);
        }
    }
}
