using System;
using System.Runtime.InteropServices;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The test step walkmode 1|0: walking instead of running, as holding Shift does (the user's call, November 2026:
    /// a test walking in at a door ran ten meters in a second, past the moment it was looking for). The client's
    /// CommandInterpreter::SetHoldRun (0x6B42B0, thiscall, one int) on the SmartBox's command interpreter (the SmartBox
    /// at [0x83DA58], its cmdinterp at +0xB8, after the LScape at +0xB0 and the ambient sounds), which is what the Shift
    /// key calls. Untested in the game when written.
    /// </summary>
    internal sealed partial class TestRunner {
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void SetHoldRunCall(IntPtr interpreter, int hold);

        private static SetHoldRunCall setHoldRun;
        private int walkShotsTaken;

        private static unsafe void WalkMode(bool walk) {
            var smartBox = *(byte**)0x83DA58;
            var interpreter = smartBox == null ? IntPtr.Zero : *(IntPtr*)(smartBox + 0xB8);

            if (interpreter == IntPtr.Zero) {
                return;
            }

            setHoldRun = setHoldRun ?? Marshal.GetDelegateForFunctionPointer<SetHoldRunCall>((IntPtr)0x6B42B0);
            setHoldRun(interpreter, walk ? 1 : 0);
        }
    }
}
