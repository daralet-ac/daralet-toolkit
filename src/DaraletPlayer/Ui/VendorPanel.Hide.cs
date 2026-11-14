using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Replace: the game's vendor window can't be hidden (gmVendorUI::OnVisibilityChanged, 0x4C4410, closes each tab and
    /// resets the shop when it goes), so ENVP, the panel along the bottom it's shown in, is moved off the left of the screen
    /// while a vendor is open (ClientElements.Place: the base UIElement::MoveTo, which saves nothing), and put back where it
    /// was when the vendor closes, Replace goes off or the plugin stops. Looked at again once a second in case the game moved
    /// it back. At logout it's forgotten: the game makes its UI again. `[vendor]` lines in log.txt.
    /// </summary>
    internal sealed partial class VendorPanel {
        /// <summary>Where ENVP was before it was moved, or null while it's where the game put it.</summary>
        private (int X, int Y, int Width, int Height)? moved;

        private int framesToCheck;

        private void HideFrame() {
            var loggedIn = CoreManager.Current.CharacterFilter.LoginStatus == 3;

            if (!loggedIn) {
                moved = null;
                return;
            }

            var want = Options.Replace && CoreManager.Current.Actions.VendorId != 0 && !HideGameUi.On;

            if (!want) {
                PutBack();
                return;
            }

            // the game's window is going off the screen, so this one is the only way to trade: never left switched off
            // (an older build's close button switched it off for good, and vendors then showed nothing at all)
            if (!Window.Visible) {
                Window.Visible = true;
                PluginCore.LogFile("[vendor] the Vendor window was switched off: switched on, as Replace needs it");
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

            if (moved.HasValue && x == off) {
                return;
            }

            // first time, or the game put it back: what it is now is where it goes back to
            moved = (x, y, width, height);

            if (ClientElements.Place(ClientElements.Environment, off, y, width, height)) {
                PluginCore.LogFile($"[vendor] the game's vendor panel moved off the screen from {x},{y} ({width}x{height})");
            }
        }

        private void PutBack() {
            if (!moved.HasValue) {
                return;
            }

            var place = moved.Value;
            moved = null;

            if (ClientElements.Place(ClientElements.Environment, place.X, place.Y, place.Width, place.Height)) {
                PluginCore.LogFile($"[vendor] the game's vendor panel put back at {place.X},{place.Y}");
            }
        }

        private static int OffScreen(int width) {
            return -(width + 200);
        }
    }
}
