using System;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// What the game's toolbar buttons do (gmToolbarUI::ListenToElementMessage, 0x4BFB20, read with a disassembler), called
    /// the same way, with plain pointers as ClientElements does: the combat button's ClientCombatSystem::ToggleCombatMode,
    /// the magnifier's CM_Examine::SendNotice_ExamineObject (the game's own appraisal, which opens its appraisal panel), and
    /// the hand's ItemHolder::UseObject (ClientUISystem::UseObject's, 0x565750, the game's own use of anything). The panel
    /// buttons are ClientElements.SetGamePanel. Game thread only, once logged in.
    /// </summary>
    internal static class ClientToolbar {
        /// <summary>ClientCombatSystem::s_pCombatSystem (ClientCombat).</summary>
        private const int CombatSystemAddress = 0x87166C;

        private static bool broken;

        /// <summary>Peace to the combat mode your wielded weapon suits, or back: the combat button and the game's key.</summary>
        public static unsafe bool ToggleCombatMode() {
            return Try("toggle combat mode", () => {
                var system = *(void**)CombatSystemAddress;

                if (system == null) {
                    return false;
                }

                ((delegate* unmanaged[Thiscall]<void*, void>)0x56D610)(system);
                return true;
            });
        }

        /// <summary>Appraises an object as the game's magnifier does: CM_Examine::SendNotice_ExamineObject (0x6B0510).</summary>
        public static unsafe bool Examine(int id) {
            return id != 0 && Try("examine", () => {
                ((delegate* unmanaged[Cdecl]<int, void>)0x6B0510)(id);
                return true;
            });
        }

        /// <summary>Uses an object as the game's hand does: ItemHolder::UseObject(id, 0, 0) (0x5898B0, static).</summary>
        public static unsafe bool Use(int id) {
            return id != 0 && Try("use", () => {
                ((delegate* unmanaged[Cdecl]<int, int, int, void>)0x5898B0)(id, 0, 0);
                return true;
            });
        }

        /// <summary>
        /// Logs out, as the X on the game's indicators does: CM_UI::SendNotice_EndCharacterSession(1) (0x479F40, static),
        /// called from gmIndicatorsUI::ListenToElementMessage (0x4C0710) for a click on element 0x100000FA.
        /// </summary>
        public static unsafe bool LogOut() {
            return Try("log out", () => {
                ((delegate* unmanaged[Cdecl]<int, void>)0x479F40)(1);
                return true;
            });
        }

        private static bool Try(string what, Func<bool> call) {
            if (broken) {
                return false;
            }

            try {
                var done = call();
                PluginCore.LogFile($"[toolbar] {what}: {(done ? "done" : "nothing to do it on")}");
                return done;
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.Log($"The game's toolbar can't be used ({what}), so the plugin's is off until it next starts: "
                    + ex);
                return false;
            }
        }
    }
}
