using System;
using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The time of day pinned on this client, for testing only (a test run's `set timeofday F`, F the fraction of the day
    /// from 0 to 1, -1 to let it go; the user's call, November 2026: night and day on demand for lighting tests, never a
    /// player setting). The client keeps its clock in a GameTime (the pointer at 0x8EE9C8): GameTime::UseTime
    /// (0x5A7800) takes the timer (the double at 0x8379A8) plus two offsets (+0x78, +0x80, doubles), the day's begin
    /// (+0x50, double) and length (+0x0C, float), and works out the fraction of the day (+0x48) that the sky's light and
    /// colours follow. Each frame, the second offset is set so the clock reads the pinned fraction of the current day;
    /// let go, it's put back. Only this client's sky changes: the server never hears of it. Untested in the game when
    /// written.
    /// </summary>
    internal static unsafe class TimeOfDay {
        private const uint GameTimeAt = 0x8EE9C8;
        private const uint TimerAt = 0x8379A8;

        private static double? pinned;
        private static double originalOffset;
        private static bool subscribed;

        public static double? Pinned => pinned;

        public static void Pin(double fraction) {
            var time = *(byte**)GameTimeAt;

            if (time == null) {
                PluginCore.LogFile("[time-of-day] no game time yet");
                return;
            }

            if (fraction < 0) {
                Release();
                return;
            }

            if (pinned == null) {
                originalOffset = *(double*)(time + 0x80);
            }

            pinned = Math.Min(0.9999, fraction);

            if (!subscribed) {
                subscribed = true;
                CoreManager.Current.RenderFrame += Core_RenderFrame;
            }

            Apply();
            PluginCore.LogFile($"[time-of-day] pinned at {pinned:0.000} of the day ({*(float*)(time + 0x0C):0} s long)");
        }

        public static void Release() {
            if (subscribed) {
                CoreManager.Current.RenderFrame -= Core_RenderFrame;
                subscribed = false;
            }

            if (pinned == null) {
                return;
            }

            var time = *(byte**)GameTimeAt;

            if (time != null) {
                *(double*)(time + 0x80) = originalOffset;
            }

            pinned = null;
            PluginCore.LogFile("[time-of-day] let go: the game's own time again");
        }

        private static void Core_RenderFrame(object sender, EventArgs e) {
            try {
                Apply();
            }
            catch (Exception ex) {
                PluginCore.LogFile("[time-of-day] " + ex.Message);
                Release();
            }
        }

        /// <summary>The offset that makes the clock read the pinned fraction of the day it's in now.</summary>
        private static void Apply() {
            var time = *(byte**)GameTimeAt;

            if (time == null || pinned == null) {
                return;
            }

            var length = *(float*)(time + 0x0C);
            var dayBegin = *(double*)(time + 0x50);

            if (length <= 0) {
                return;
            }

            var wanted = dayBegin + pinned.Value * length;
            *(double*)(time + 0x80) = wanted - *(double*)TimerAt - *(double*)(time + 0x78);
        }
    }
}
