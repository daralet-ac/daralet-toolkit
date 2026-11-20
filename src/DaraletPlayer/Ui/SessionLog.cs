using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DaraletPlayer.Feed;
using Decal.Adapter;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// What log.txt says about each session, so a player's log alone is enough to look into a crash (the user's call,
    /// November 2026). At start: the log trimmed past 4 MB (the rest kept in log.old.txt), and a marker file
    /// (session.txt, the game's process id and when it started) checked: one left by another process means the last
    /// session didn't end cleanly, since a clean shutdown deletes it (a hot reload is the same process, so it isn't
    /// taken for a crash). Once logged in: a header with the version, screen size, free memory and every graphics setting
    /// (GraphicsSummary). If the last session didn't end cleanly and the player has opted in (SettingsData.
    /// ReadCrashRecords, off to start), Windows' Application event log is asked, off the game's thread, for the game's
    /// own "Application Error" and "Application Hang" entries since that session started, and each is written to log.txt
    /// with the crash address and the client's function there (Feed/CrashReport.cs). Untested in the game when written.
    /// </summary>
    internal static class SessionLog {
        private const long MostLogBytes = 4 * 1024 * 1024;

        private static DateTime? uncleanSince;
        private static bool headerWritten;

        private static string MarkerPath => DataFolder.For("session.txt");

        /// <summary>From the plugin's start: the log trimmed, the last session's end judged, this one's marker written.</summary>
        public static void Start() {
            try {
                Trim();
                var pid = Process.GetCurrentProcess().Id;

                if (File.Exists(MarkerPath)) {
                    var parts = File.ReadAllText(MarkerPath).Split('|');

                    if (parts.Length == 2 && int.TryParse(parts[0], out var oldPid) && oldPid != pid
                        && DateTime.TryParse(parts[1], null, System.Globalization.DateTimeStyles.RoundtripKind,
                            out var started)) {
                        uncleanSince = started;
                        PluginCore.LogFile($"[session] the last session (started {started.ToLocalTime():yyyy-MM-dd HH:mm:ss}) "
                            + "didn't end cleanly: a crash, a freeze, or the game closed from outside");
                    }
                }

                File.WriteAllText(MarkerPath, $"{pid}|{DateTime.UtcNow:o}");
                headerWritten = false;
            }
            catch (Exception ex) {
                PluginCore.LogFile("[session] " + ex.Message);
            }
        }

        /// <summary>From the plugin's shutdown: a clean end, the marker gone.</summary>
        public static void End() {
            try {
                if (File.Exists(MarkerPath)) {
                    File.Delete(MarkerPath);
                }

                PluginCore.LogFile("[session] ended cleanly");
            }
            catch {
            }
        }

        /// <summary>Every frame from PluginCore: once logged in, the header, and the crash records if opted in.</summary>
        public static void Frame(Settings settings) {
            if (headerWritten || CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                return;
            }

            headerWritten = true;

            try {
                var device = UBService.Huds.D3Ddevice;
                var screen = "unknown";

                if (device != null) {
                    using (var back = device.GetBackBuffer(0, 0, Microsoft.DirectX.Direct3D.BackBufferType.Mono)) {
                        screen = $"{back.Description.Width}x{back.Description.Height}";
                    }
                }

                PluginCore.LogFile($"[session] Daralet Toolkit {typeof(SessionLog).Assembly.GetName().Version}, screen "
                    + $"{screen}, {DrawDistance.FreeMegabytes()} MB of the game's address space free; graphics: "
                    + GraphicsSummary.Of(settings.Data));
            }
            catch (Exception ex) {
                PluginCore.LogFile("[session] " + ex.Message);
            }

            if (uncleanSince is DateTime since) {
                uncleanSince = null;

                if (settings.Data.ReadCrashRecords) {
                    Task.Run(() => ReadCrashRecords(since));
                }
                else {
                    PluginCore.LogFile("[session] to have the cause written here next time, turn on Config, Troubleshooting, "
                        + "Read the game's crash records");
                }
            }
        }

        private static void Trim() {
            var log = DataFolder.For("log.txt");

            if (!File.Exists(log) || new FileInfo(log).Length < MostLogBytes) {
                return;
            }

            var old = DataFolder.For("log.old.txt");

            if (File.Exists(old)) {
                File.Delete(old);
            }

            File.Move(log, old);
            PluginCore.LogFile("[session] log.txt passed 4 MB: the rest is in log.old.txt");
        }

        /// <summary>The game's crash and hang entries since a time, off the game's thread.</summary>
        private static void ReadCrashRecords(DateTime since) {
            try {
                var from = since.ToUniversalTime().AddMinutes(-1).ToString("o");
                var query = new EventLogQuery("Application", PathType.LogName,
                    "*[System[(Provider[@Name='Application Error'] or Provider[@Name='Application Hang'])"
                    + $" and TimeCreated[@SystemTime>='{from}']]]");
                var found = 0;
                var functions = ClientFunctions();

                using (var reader = new EventLogReader(query)) {
                    for (var record = reader.ReadEvent(); record != null; record = reader.ReadEvent()) {
                        using (record) {
                            var hang = record.ProviderName == "Application Hang";
                            var report = CrashReport.Read(record.FormatDescription(), hang);

                            if (report == null) {
                                continue;
                            }

                            found++;
                            PluginCore.LogFile($"[crash] {record.TimeCreated:yyyy-MM-dd HH:mm:ss}: "
                                + report.Describe(functions));
                        }
                    }
                }

                PluginCore.LogFile($"[session] read Windows' crash records for acclient.exe: {found} found");
            }
            catch (Exception ex) {
                PluginCore.LogFile("[session] Windows' crash records couldn't be read: " + ex.Message);
            }
        }

        /// <summary>The client's known functions by address (UtilityBelt.Service's AcClient.Entrypoint), sorted.</summary>
        private static List<(uint Address, string Name)> ClientFunctions() {
            var names = Enum.GetNames(typeof(AcClient.Entrypoint));
            return names.Select(n => (Address: Convert.ToUInt32(Enum.Parse(typeof(AcClient.Entrypoint), n)), Name: n))
                .OrderBy(f => f.Address).ToList();
        }
    }
}
