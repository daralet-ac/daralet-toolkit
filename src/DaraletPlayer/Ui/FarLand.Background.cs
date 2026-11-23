using System;
using System.Runtime.InteropServices;
using Decal.Adapter;
using Microsoft.DirectX.Direct3D;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The far land drawn as background, right after the game's sky and before its world (the user's screenshots,
    /// November 2026: the far skyline covered the lower half of lifestones' crystals, some characters and big see-through
    /// crystals, while their tops, against the sky, showed). Drawn from WorldPass, after the game's world, it went only
    /// where the depth was still the cleared 1.0, and those objects are drawn without writing depth, so to it they looked
    /// empty. LScape's draw (0x506D90) draws the sky's pieces (0x507A50 with 0), then the landblocks (0x5069E0, thiscall,
    /// no arguments, called at 0x506DAB), then the sky's after-world pieces; it's reached from SmartBox::RenderNormalMode
    /// (0x453BED) with the camera outdoors and from the render device's indoor drawing (0x5A597B) with the camera in a
    /// building, after the cells round it. That call is pointed here: the far land (its own drawing unchanged: only where
    /// nothing's been drawn, depth 1 put back after) and then the game's landblocks, so everything the game draws, see-
    /// through or not, lands on top. Sun shadows' noting of the game's draws is paused meanwhile. If the hook can't be
    /// set, or the landscape draw didn't come this frame, the world pass draws it as before. Untested in the game when
    /// written.
    /// </summary>
    internal sealed partial class FarLand {
        private const int LandblocksDraw = 0x5069E0;
        private const int LandblocksDrawCall = 0x506DAB;

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void LandblocksCall(IntPtr lscape);

        private AcClient.Hook backgroundHook;

        // both kept while the hook is in, so they're never collected while the client can call them
        private LandblocksCall backgroundCall;
        private LandblocksCall gameLandblocks;
        private bool backgroundFailed;

        /// <summary>Whether this frame's far land was drawn as background, so the world pass leaves it.</summary>
        private bool drawnAsBackground;

        /// <summary>The hook put in, once, while the far land is drawn (from the world pass).</summary>
        private void HookBackground() {
            if (backgroundHook != null || backgroundFailed) {
                return;
            }

            try {
                gameLandblocks = Marshal.GetDelegateForFunctionPointer<LandblocksCall>((IntPtr)LandblocksDraw);
                backgroundCall = DrawBackground;
                var hooked = new AcClient.Hook(LandblocksDraw, LandblocksDrawCall);

                if (!hooked.Setup(backgroundCall)) {
                    backgroundFailed = true;
                    backgroundCall = null;
                    PluginCore.LogFile("[far-land] couldn't hook the landscape draw at 0x506DAB: drawn after the world");
                    return;
                }

                backgroundHook = hooked;
                PluginCore.LogFile("[far-land] drawn as background, after the sky and before the world");
            }
            catch (Exception ex) {
                backgroundFailed = true;
                backgroundCall = null;
                PluginCore.LogFile("[far-land] " + ex);
            }
        }

        /// <summary>The game's call put back. A hook left in after the plugin's gone would crash the game.</summary>
        private void UnhookBackground() {
            if (backgroundHook == null) {
                return;
            }

            try {
                backgroundHook.Remove();
            }
            catch (Exception ex) {
                PluginCore.LogFile("[far-land] " + ex);
            }

            backgroundHook = null;
            backgroundCall = null;
        }

        private void DrawBackground(IntPtr lscape) {
            try {
                if (!disposed && !failed && vertices != null && Options.ClampedFarLand > 0
                    && CoreManager.Current.CharacterFilter.LoginStatus == 3 && OutdoorView.Now
                    && UBService.Huds.D3Ddevice is Device device) {
                    var noting = ShadowCasters.Hooked;

                    // the far land's own draws aren't the game's world: no sun shadows from them
                    if (noting) {
                        ShadowStubs.Recording = false;
                    }

                    DrawProbe.Ours = true;

                    try {
                        Draw(device);
                        drawnAsBackground = true;
                    }
                    finally {
                        DrawProbe.Ours = false;

                        if (noting) {
                            ShadowStubs.Recording = true;
                        }
                    }
                }
            }
            catch (Exception ex) {
                // nothing may escape into the game's frame
                failed = true;
                PluginCore.LogFile("[far-land] stopped for the session: " + ex);
            }

            gameLandblocks(lscape);
        }
    }
}
