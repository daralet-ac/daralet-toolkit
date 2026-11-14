using System;
using System.Runtime.InteropServices;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Where the game starts an appraisal: ClientUISystem::ExamineObject (0x5657B0), which every way of appraising goes
    /// through (the appraise key, the toolbar's magnifier, its menus), calls CM_Examine::SendNotice_ExamineObject
    /// (0x6B0510, static, cdecl) with the object, once, at 0x5657BC; that notice is what asks the server and opens the game's
    /// examination panel. The call is pointed at OnExamine (UtilityBelt.Service's AcClient.Hook, as SpellDrag does), which
    /// asks TakeOver: yes, and the game's notice is never sent, so its panel stays shut, and the plugin's window shows the
    /// appraisal instead; no, and the game's function is called as ever.
    /// </summary>
    internal static class ClientExamine {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int ExamineFunction(uint id);

        private static AcClient.Hook hooked;

        // kept for as long as the hook is in, so they're never collected while the client can call them
        private static ExamineFunction hook;
        private static ExamineFunction original;

        /// <summary>Asked of each appraisal the game starts: true takes it from the game (and does it).</summary>
        public static Func<int, bool> TakeOver;

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
                var call = new AcClient.Hook((int)AcClient.Entrypoint.CM_Examine__SendNotice_ExamineObject,
                    (int)AcClient.Call.CM_Examine__SendNotice_ExamineObject_0x005657BC);

                if (call.Setup(hook)) {
                    hooked = call;
                    PluginCore.LogFile("[appraisal] listening for the game's appraisals");
                }
                else {
                    PluginCore.LogFile("[appraisal] couldn't hook the game's appraisals");
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[appraisal] couldn't listen for the game's appraisals: {ex}");
            }
        }

        /// <summary>Puts the game's call back as it was. A hook left in after the plugin's gone would crash the game.</summary>
        public static void Stop() {
            try {
                hooked?.Remove();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }

            hooked = null;
            hook = null;
            TakeOver = null;
        }

        private static int OnExamine(uint id) {
            try {
                if (id != 0 && TakeOver?.Invoke(unchecked((int)id)) == true) {
                    return 0;
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile("[appraisal] " + ex);
            }

            return original(id);
        }
    }
}
