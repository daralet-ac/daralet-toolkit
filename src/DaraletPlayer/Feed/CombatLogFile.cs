using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The saved combat log: a text file for each character and day, in a folder of its own, added to about once a second.
    /// Lines wait in memory between writes: a fight mustn't mean a disk write for every hit, on the game's thread.
    /// </summary>
    public class CombatLogFile {
        private readonly string folder;
        private readonly Action<string> log;
        private readonly List<(string Path, string Line)> waiting = new List<(string, string)>();
        private bool failed;

        public CombatLogFile(string folder, Action<string> log) {
            this.folder = folder;
            this.log = log;
        }

        public string Folder => folder;

        /// <summary>Its name: the character's, without what a file's name can't hold, and the day.</summary>
        public static string FileName(string character, DateTime at) {
            var name = new StringBuilder();

            foreach (var letter in character ?? "") {
                name.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), letter) >= 0 ? '_' : letter);
            }

            return $"{(name.Length == 0 ? "Unknown" : name.ToString())} {at:yyyy-MM-dd}.txt";
        }

        /// <summary>One line of the file: the time, the kind, and the line as the game wrote it.</summary>
        public static string Format(CombatLine line) {
            return $"[{line.At:HH:mm:ss}] [{CombatKindInfo.For(line.Kind).Label}] {line.Text}";
        }

        public void Add(string character, CombatLine line) {
            waiting.Add((Path.Combine(folder, FileName(character, line.At)), Format(line)));
        }

        /// <summary>Writes what's waiting. A file that can't be written says so once and the lines are dropped.</summary>
        public void Flush() {
            if (waiting.Count == 0) {
                return;
            }

            try {
                Directory.CreateDirectory(folder);
                var start = 0;

                // a run of lines for one file at a time: midnight, or a change of character, starts another
                while (start < waiting.Count) {
                    var path = waiting[start].Path;
                    var text = new StringBuilder();
                    var next = start;

                    for (; next < waiting.Count && waiting[next].Path == path; next++) {
                        text.Append(waiting[next].Line).Append("\r\n");
                    }

                    File.AppendAllText(path, text.ToString());
                    start = next;
                }

                failed = false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) {
                if (!failed) {
                    failed = true;
                    log?.Invoke($"Couldn't save the combat log: {ex.Message}");
                }
            }

            waiting.Clear();
        }
    }
}
