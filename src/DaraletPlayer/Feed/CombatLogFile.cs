using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The saved combat log: a text file for each character and day, in a folder of its own, added to about once a second.
    /// Lines wait in memory between writes: a fight mustn't mean a disk write for every hit, on the game's thread. The chat
    /// windows' saved chat is written the same way, to a folder of its own.
    /// </summary>
    public class CombatLogFile {
        private readonly string folder;
        private readonly Action<string> log;
        private readonly string what;
        private readonly List<(string Path, string Line)> waiting = new List<(string, string)>();
        private bool failed;

        /// <param name="what">What the files are, for the line in chat when they can't be written.</param>
        public CombatLogFile(string folder, Action<string> log, string what = "combat log") {
            this.folder = folder;
            this.log = log;
            this.what = what;
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
            Add(character, line.At, Format(line));
        }

        /// <summary>A line as it's to be written, in the file for the character and the day it came.</summary>
        public void Add(string character, DateTime at, string line) {
            waiting.Add((Path.Combine(folder, FileName(character, at)), line));
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
                    log?.Invoke($"Couldn't save the {what}: {ex.Message}");
                }
            }

            waiting.Clear();
        }
    }
}
