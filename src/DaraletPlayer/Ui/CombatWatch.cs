using System;
using System.Collections.Generic;
using Decal.Adapter;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Reads combat from chat (CombatLines) and hands it on: to whatever listens (the floating text), the Combat log window's
    /// list and the saved file, each by what SettingsData.CombatOptions says of that kind, and keeps it out of chat where it
    /// says to. Kills and deaths are told by the server's own message, which comes just before the client writes its line.
    /// The first few combat-looking lines it couldn't read go in log.txt (`[combat]`), to add their wording.
    /// </summary>
    internal sealed class CombatWatch : IDisposable {
        /// <summary>The most lines the Combat log window keeps.</summary>
        private const int Kept = 1000;

        private const int UnreadLogged = 20;

        // Combat, Magic, CombatEnemy and CombatSelf, the chat types a line of combat comes in
        private static readonly HashSet<int> CombatChatTypes = new HashSet<int> { 6, 7, 21, 22 };

        private static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(1);

        private readonly Settings settings;
        private readonly List<CombatLine> lines = new List<CombatLine>();

        /// <summary>The kills and deaths told, whose lines the client hasn't written yet.</summary>
        private readonly Dictionary<string, CombatKind> deathMessages = new Dictionary<string, CombatKind>();

        private readonly CombatLogFile file;
        private DateTime flushAt;
        private int unreadLogged;
        private bool disposed;

        public CombatWatch(Settings settings) {
            this.settings = settings;
            file = new CombatLogFile(DataFolder.For("Combat Logs"), PluginCore.Log);
            CoreManager.Current.EchoFilter.ServerDispatch += EchoFilter_ServerDispatch;
            CoreManager.Current.ChatBoxMessage += Core_ChatBoxMessage;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        /// <summary>A line of combat that's come in, for the floating text.</summary>
        public event Action<CombatLine> Line;

        public IReadOnlyList<CombatLine> Lines => lines;

        public string SaveFolder => file.Folder;

        public void Clear() {
            lines.Clear();
        }

        private void EchoFilter_ServerDispatch(object sender, NetworkMessageEventArgs e) {
            try {
                if (e.Message.Type == 0xF7B0 && CombatLines.TryReadDeathMessage(e.Message.RawData, out var text, out var kind)) {
                    // only the last few: one the client never writes shouldn't stay here for good
                    if (deathMessages.Count > 8) {
                        deathMessages.Clear();
                    }

                    deathMessages[text.TrimEnd('\r', '\n', ' ')] = kind;
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void Core_ChatBoxMessage(object sender, ChatTextInterceptEventArgs e) {
            try {
                if (disposed || e.Eat || CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                    return;
                }

                var line = Read(e.Text, e.Color);

                if (line == null) {
                    return;
                }

                var options = settings.Data.CombatOptions(line.Kind);
                e.Eat = options.HideChat;
                lines.Add(line);

                if (lines.Count > Kept) {
                    lines.RemoveRange(0, lines.Count - Kept);
                }

                if (settings.Data.CombatLogSave && options.Save) {
                    file.Add(CoreManager.Current.CharacterFilter.Name, line);
                }

                Line?.Invoke(line);
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private CombatLine Read(string text, int chatType) {
            var trimmed = (text ?? "").TrimEnd('\r', '\n', ' ');

            if (deathMessages.TryGetValue(trimmed, out var kind)) {
                deathMessages.Remove(trimmed);
                return CombatLines.ForDeathMessage(trimmed, kind, DateTime.Now);
            }

            var line = CombatLines.Read(text, chatType, DateTime.Now);

            // only what mentions points or damage: buff messages would use up the few that are logged
            if (line == null && CombatChatTypes.Contains(chatType) && unreadLogged < UnreadLogged
                && (trimmed.IndexOf("point", StringComparison.OrdinalIgnoreCase) >= 0
                    || trimmed.IndexOf("damage", StringComparison.OrdinalIgnoreCase) >= 0)) {
                unreadLogged++;
                PluginCore.LogFile($"[combat] not read, type {chatType}: {trimmed}");
            }

            return line;
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (DateTime.UtcNow >= flushAt) {
                    flushAt = DateTime.UtcNow + FlushEvery;
                    file.Flush();
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        public void Dispose() {
            disposed = true;
            CoreManager.Current.EchoFilter.ServerDispatch -= EchoFilter_ServerDispatch;
            CoreManager.Current.ChatBoxMessage -= Core_ChatBoxMessage;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            file.Flush();
        }
    }
}
