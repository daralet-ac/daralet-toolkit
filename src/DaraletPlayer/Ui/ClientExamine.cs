using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Where the game starts an appraisal. ClientUISystem::ExamineObject (0x5657B0), which every way of appraising goes
    /// through (the appraise key, the toolbar's magnifier, its menus), calls CM_Examine::SendNotice_ExamineObject
    /// (0x6B0510, static, cdecl) with the object, once, at 0x5657BC; that notice is what asks the server and opens the game's
    /// examination panel. And while that panel is open, the game appraises whatever's selected next itself, calling
    /// CM_Item::Event_Appraise (static, cdecl, the object) from three places (0x4AB8FE, 0x4ABB53, 0x4B755D) without
    /// ExamineObject: missed, the panel (cut down to a creature's model under the Creature Appraisal window) followed a
    /// selected portal device and showed its item page inside the plugin's window. Each call is pointed at the plugin
    /// (UtilityBelt.Service's AcClient.Hook, as SpellDrag does), which asks TakeOver: yes, and the game's call is never
    /// made, so its panel stays shut (or is closed), and a plugin window shows the appraisal instead; no, and the game's
    /// function is called as ever. The Event_Appraise calls are only asked about while the game's panel is showing.
    /// [appraisal] lines in log.txt. Untested in the game when written.
    /// </summary>
    internal static class ClientExamine {
        /// <summary>The game's examination panel (the Examination floaty).</summary>
        public const uint Floaty = 0x100005F7;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ExamineFunction(uint id);

        private static readonly List<AcClient.Hook> hooked = new List<AcClient.Hook>();

        // kept for as long as the hooks are in, so they're never collected while the client can call them
        private static ExamineFunction hook;
        private static ExamineFunction original;
        private static ExamineFunction appraiseHook;
        private static ExamineFunction appraiseOriginal;

        private static int loggedFollows;

        /// <summary>Asked of each appraisal the game starts: true takes it from the game (and does it).</summary>
        public static Func<int, bool> TakeOver;

        /// <summary>Whether anything decides where appraisals go (the Creature Appraisal panel's been made).</summary>
        public static bool Routing => TakeOver != null;

        /// <summary>
        /// Whether a plugin window takes an appraisal the plugin starts itself (the Toolbar window's magnifier), which
        /// calls the game's notice straight and so never passes the hook: true, and it's done.
        /// </summary>
        public static bool TryTakeOver(int id) {
            try {
                return id != 0 && TakeOver?.Invoke(id) == true;
            }
            catch (Exception ex) {
                PluginCore.LogFile("[appraisal] " + ex);
                return false;
            }
        }

        public static void Start() {
            if (hook != null) {
                return;
            }

            try {
                original = Marshal.GetDelegateForFunctionPointer<ExamineFunction>(
                    new IntPtr((int)AcClient.Entrypoint.CM_Examine__SendNotice_ExamineObject));
                hook = OnExamine;
                Hook((int)AcClient.Entrypoint.CM_Examine__SendNotice_ExamineObject,
                    (int)AcClient.Call.CM_Examine__SendNotice_ExamineObject_0x005657BC, hook, "the game's appraisals");

                appraiseOriginal = Marshal.GetDelegateForFunctionPointer<ExamineFunction>(
                    new IntPtr((int)AcClient.Entrypoint.CM_Item__Event_Appraise));
                appraiseHook = OnAppraise;

                foreach (var call in new[] {
                    (int)AcClient.Call.CM_Item__Event_Appraise_0x004AB8FE,
                    (int)AcClient.Call.CM_Item__Event_Appraise_0x004ABB53,
                    (int)AcClient.Call.CM_Item__Event_Appraise_0x004B755D,
                }) {
                    Hook((int)AcClient.Entrypoint.CM_Item__Event_Appraise, call, appraiseHook,
                        $"the game panel's appraisal at 0x{call:X8}");
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[appraisal] couldn't listen for the game's appraisals: {ex}");
            }
        }

        private static void Hook(int entrypoint, int call, ExamineFunction function, string what) {
            var hookAt = new AcClient.Hook(entrypoint, call);

            if (hookAt.Setup(function)) {
                hooked.Add(hookAt);
                PluginCore.LogFile($"[appraisal] listening for {what}");
            }
            else {
                PluginCore.LogFile($"[appraisal] couldn't hook {what}");
            }
        }

        /// <summary>Puts the game's calls back as they were. A hook left in after the plugin's gone would crash the game.</summary>
        public static void Stop() {
            foreach (var call in hooked) {
                try {
                    call.Remove();
                }
                catch (Exception ex) {
                    PluginCore.Log(ex);
                }
            }

            hooked.Clear();
            hook = null;
            appraiseHook = null;
            TakeOver = null;
        }

        /// <summary>Whether the game's examination panel is on screen.</summary>
        public static bool GamePanelShown() {
            try {
                return ClientElements.IsShownUnder(Floaty, Floaty) == true;
            }
            catch (Exception ex) {
                PluginCore.LogFile("[appraisal] " + ex);
                return false;
            }
        }

        /// <summary>Closes the game's examination panel, for an appraisal a plugin window shows instead.</summary>
        public static void CloseGamePanel() {
            try {
                if (ClientElements.IsShownUnder(Floaty, Floaty) == true) {
                    ClientElements.SetVisibleUnder(Floaty, Floaty, false);
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile("[appraisal] " + ex);
            }
        }

        private static int OnExamine(uint id) {
            if (id != 0 && TryTakeOver(unchecked((int)id))) {
                return 0;
            }

            return original(id);
        }

        /// <summary>The game's panel appraising what's selected now: routed like any other, while the panel shows.</summary>
        private static int OnAppraise(uint id) {
            try {
                if (id != 0 && GamePanelShown()) {
                    if (loggedFollows < 10) {
                        loggedFollows++;
                        PluginCore.LogFile($"[appraisal] the game's panel follows the selection to 0x{id:X8}");
                    }

                    if (TryTakeOver(unchecked((int)id))) {
                        return 0;
                    }
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile("[appraisal] " + ex);
            }

            return appraiseOriginal(id);
        }
    }
}
