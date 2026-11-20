using System;
using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A trace of the depth passes for the test step `set lighttrace 1` (the user's report, November 2026: walking in
    /// at the Holtburg tavern's door, every light and shadow went off for a moment and came back further in). Each frame
    /// notes where the passes drew (in the game's frame or on RenderFrame, or not at all), how many of the game's lights
    /// were near and in each list, and the camera's and your cells; a line goes in log.txt whenever that changes.
    /// </summary>
    internal sealed partial class DepthShading {
        public static bool Trace;

        private static unsafe IntPtr DeviceComPointer(Microsoft.DirectX.Direct3D.Device device) {
            return (IntPtr)device.UnmanagedComPointer;
        }

        private string lastTrace;
        private string tracePath = "none";
        private int traceLamps = -1;
        private int traceStatic;
        private int traceDynamic;

        private bool traceSunReady;
        private float traceSunUp;

        /// <summary>Once a frame, after the passes: a line when anything traced has changed.</summary>
        private unsafe void NoteTrace() {
            var sunReady = traceSunReady;
            var sunUp = traceSunUp;
            traceSunReady = false;
            if (!Trace) {
                lastTrace = null;
                return;
            }

            var cameraCell = *(uint*)(0x81FF10 + 4);
            var playerCell = unchecked((uint)CoreManager.Current.Actions.Landcell);
            var line = $"path {tracePath}, lamps {traceLamps} ({traceStatic} static, {traceDynamic} dynamic), sun shadows "
                + $"{(sunReady ? "drawn" : "none")} (sun up {sunUp:0.00}), camera {cameraCell:X8}, you {playerCell:X8}";

            if (line != lastTrace) {
                lastTrace = line;
                PluginCore.LogFile("[light-trace] " + line);
            }

            tracePath = "none";
            traceLamps = -1;
        }
    }
}
