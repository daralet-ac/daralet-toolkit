using System;
using System.IO;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Decal.Adapter.Wrappers;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Every object the server creates in the WatchSeconds after a teleport, a line each in Test Runs\objects.txt as it
    /// comes (Feed/CreateObjectSummary.cs: name, weenie class, setup, motion table, movement), so the last ones the game
    /// was given before a teleport crash outlive it (November 2026: the crashes were in the game reading animation and
    /// motion data on reaching places whose dat files unpack cleanly, the same places again and again). Only while a test
    /// run's `set objectlog 1` has it on; each line written straight to the file, a crash leaving nothing unwritten.
    /// </summary>
    internal static class ObjectWatch {
        private const double WatchSeconds = 45;

        private static bool on;
        private static DateTime watchUntil = DateTime.MinValue;
        private static int teleport;
        private static int count;
        private static int unread;

        private static string FilePath => Path.Combine(DataFolder.For("Test Runs"), "objects.txt");

        public static void Enable(bool wanted) {
            if (wanted == on) {
                return;
            }

            on = wanted;

            if (wanted) {
                CoreManager.Current.EchoFilter.ServerDispatch += EchoFilter_ServerDispatch;
                CoreManager.Current.CharacterFilter.ChangePortalMode += CharacterFilter_ChangePortalMode;
                Write("watch on");
            }
            else {
                CoreManager.Current.EchoFilter.ServerDispatch -= EchoFilter_ServerDispatch;
                CoreManager.Current.CharacterFilter.ChangePortalMode -= CharacterFilter_ChangePortalMode;
                Write("watch off");
            }
        }

        private static void CharacterFilter_ChangePortalMode(object sender, ChangePortalModeEventArgs e) {
            try {
                if (e.Type == PortalEventType.EnterPortal) {
                    teleport++;
                    count = 0;
                    var a = CoreManager.Current.Actions;
                    Write($"teleport {teleport}: into portal space from {unchecked((uint)a.Landcell):X8}");
                    // what's sent during portal space is the new place's too
                    watchUntil = DateTime.MaxValue;
                }
                else {
                    Write($"teleport {teleport}: out of portal space, {count} objects so far");
                    watchUntil = DateTime.UtcNow.AddSeconds(WatchSeconds);
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile("[objects] " + ex.Message);
            }
        }

        private static void EchoFilter_ServerDispatch(object sender, NetworkMessageEventArgs e) {
            try {
                if (e.Message.Type != CreateObjectSummary.Type || DateTime.UtcNow > watchUntil) {
                    return;
                }

                var o = CreateObjectSummary.Read(e.Message.RawData, out var problem);
                count++;

                if (o == null) {
                    if (unread++ < 50) {
                        Write($"unread CreateObject ({problem}), {e.Message.RawData?.Length ?? 0} bytes");
                    }

                    return;
                }

                Write(o.Describe());
            }
            catch (Exception ex) {
                PluginCore.LogFile("[objects] " + ex.Message);
            }
        }

        private static void Write(string line) {
            try {
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {line}\n");
            }
            catch {
                // never what stops the game's messages
            }
        }
    }
}
