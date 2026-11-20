using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The landscape set before login, so the game's own login load is already at it. At login the game loaded its
    /// own Landscape Draw Distance (15), and twelve seconds later the plugin cut it to the setting's (6), a whole reload
    /// of the landscape; a teleport nine seconds after crashed the game emptying its cache (0x58712F, November 2026),
    /// the crash seen before whenever a reload came close to the game's own loading: a player who logs in and recalls
    /// at once would meet it. The LScape takes its radius at login from the int at 0x81FFB4, the game's setting's
    /// storage, so while not logged in that's written with the setting's radius, the game's own kept
    /// (savedLandscape) for everything that asks for the game's (GameOwnLandscape), and put back when the draw distance
    /// is turned off or the plugin stops. A hot reload starts logged in, so changes nothing. `[draw-distance] before
    /// login` lines in log.txt. Untested in the game when written.
    /// </summary>
    internal sealed partial class DrawDistance {
        /// <summary>The game's own landscape radius, while the int at 0x81FFB4 holds the setting's instead.</summary>
        private int? savedLandscape;

        /// <summary>The game's own Landscape Draw Distance, whatever's written in its place.</summary>
        private unsafe int GameOwnLandscape => savedLandscape ?? *(int*)GameLandscapeRadius;

        /// <summary>Whether the landscape is set before login (a test run's `set prelogin 0|1`).</summary>
        internal static bool SetBeforeLogin = false;

        /// <summary>While not logged in, the setting's landscape radius put where the game's login load reads it.</summary>
        private unsafe void PrepareBeforeLogin() {
            if (CoreManager.Current.CharacterFilter.LoginStatus == 3 || !SetBeforeLogin) {
                return;
            }

            var options = Options;
            var wanted = options.On && !broken ? options.ClampedLandscapeRadius : 0;

            if (wanted < 1) {
                RestoreGameLandscape();
                return;
            }

            var now = *(int*)GameLandscapeRadius;

            if (now == wanted) {
                return;
            }

            if (!savedLandscape.HasValue) {
                savedLandscape = now;
            }

            *(int*)GameLandscapeRadius = wanted;
            PluginCore.LogFile($"[draw-distance] before login: the game's landscape {savedLandscape} set to {wanted}, "
                + "so its own login load is at it");
        }

        /// <summary>The game's own landscape radius put back in its place.</summary>
        private unsafe void RestoreGameLandscape() {
            if (!savedLandscape.HasValue) {
                return;
            }

            *(int*)GameLandscapeRadius = savedLandscape.Value;
            PluginCore.LogFile($"[draw-distance] the game's landscape {savedLandscape.Value} put back");
            savedLandscape = null;
        }
    }
}
