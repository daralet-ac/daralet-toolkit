using System;
using UtilityBelt.Service;
using UtilityBelt.Service.Views;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The far scenery lives in video memory only (ScenePool): the billboards' and 3D trees' buffers and pictures in
    /// Direct3D's default pool, so no copy of them stays in the game's 4 GB. That pool is lost at a device reset (alt-tab
    /// in full screen, a change of resolution, exiting), and anything still alive in it then makes the reset fail, so a
    /// hidden hud of its own hears UtilityBelt's warning (OnDestroyTextures, from Decal's PreReset) and lets all of it go;
    /// it's made again the next second (billboardKey null). `[video-memory]` lines in log.txt.
    /// </summary>
    internal sealed partial class DrawDistance {
        private Hud resetHud;

        /// <summary>From Apply: the hud that hears of a device reset, made once.</summary>
        private void FollowVideoMemory() {
            if (resetHud != null) {
                return;
            }

            resetHud = UBService.Huds.CreateHud("Daralet far scenery");
            resetHud.ShowInBar = false;
            resetHud.Visible = false;
            resetHud.OnDestroyTextures += ResetHud_OnDestroyTextures;
        }

        private void ResetHud_OnDestroyTextures(object sender, EventArgs e) {
            try {
                LetGoOfFarSceneryResources();
                PluginCore.LogFile("[video-memory] let go of the far scenery for a device reset");
            }
            catch (Exception ex) {
                PluginCore.LogFile("[video-memory] " + ex);
            }
        }

        /// <summary>Every buffer and picture of the far scenery's let go, to be made again.</summary>
        private void LetGoOfFarSceneryResources() {
            ClearBillboards();
            billboardModels.Dispose();
            farTreeModels.Dispose();
            billboardKey = null;
        }

        private void DisposeVideoMemory() {
            if (resetHud == null) {
                return;
            }

            resetHud.OnDestroyTextures -= ResetHud_OnDestroyTextures;
            resetHud.Dispose();
            resetHud = null;
        }
    }
}
