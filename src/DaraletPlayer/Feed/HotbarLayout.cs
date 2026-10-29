using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Every hotbar as a file, to keep or share, or to give another character or profile the same bars: each bar's layout,
    /// look and keys, where its window is and how it's shown, and (With my items) what's on it for the character who saved
    /// it. In Documents\Decal Plugins\Daralet Toolkit\Hotbar Layouts.
    /// </summary>
    [DataContract]
    public class HotbarLayout {
        /// <summary>Raised only when an older plugin couldn't read the file.</summary>
        public const int CurrentFormat = 1;

        [DataMember(Name = "format")] public int Format;
        [DataMember(Name = "name")] public string Name;
        /// <summary>Whose items the bars carry (the character who saved it), or null for a layout without items.</summary>
        [DataMember(Name = "character")] public string Character;
        /// <summary>The bars, each with at most the one character's slots.</summary>
        [DataMember(Name = "bars")] public List<HotbarBar> Bars;
        /// <summary>Each bar's window, by its id ("hotbar-2"), as SettingsData.Windows keeps them.</summary>
        [DataMember(Name = "windows")] public Dictionary<string, WindowPlacement> Windows;

        public static string WindowId(int barId) => $"hotbar-{barId}";

        public static string DefaultFolder() {
            return DataFolder.For("Hotbar Layouts");
        }

        /// <summary>The layout of the bars as they are now, copied, with one character's items or none.</summary>
        public static HotbarLayout Make(string name, HotbarData data, Dictionary<string, WindowPlacement> windows,
            string character, bool withItems) {
            // a copy through JSON: what's saved mustn't change as the bars do
            var bars = Json.Read<List<HotbarBar>>(Json.Write(data.Bars));
            var placements = new Dictionary<string, WindowPlacement>();

            foreach (var bar in bars) {
                var mine = withItems ? bar.SlotsFor(character) : null;
                bar.Slots = new Dictionary<string, List<HotbarSlot>>();

                if (mine != null && mine.Count > 0) {
                    bar.Slots[character] = mine;
                }

                if (windows != null && windows.TryGetValue(WindowId(bar.Id), out var placement) && placement != null) {
                    placements[WindowId(bar.Id)] = Json.Read<WindowPlacement>(Json.Write(placement));
                }
            }

            return new HotbarLayout {
                Format = CurrentFormat,
                Name = name,
                Character = withItems ? character : null,
                Bars = bars,
                Windows = placements,
            };
        }

        /// <summary>
        /// Puts the layout's bars in place of the ones there are, for a character: their windows where the layout has them,
        /// its items (if it has any) as theirs, and every other character's items kept on the bars with the same numbers.
        /// Without items in the layout, the character keeps theirs the same way.
        /// </summary>
        public static void Apply(HotbarLayout layout, HotbarData data, Dictionary<string, WindowPlacement> windows,
            string character) {
            var old = data.Bars.ToDictionary(b => b.Id);
            var bars = Json.Read<List<HotbarBar>>(Json.Write(layout.Bars));

            foreach (var bar in bars) {
                var theirs = layout.Character != null && bar.Slots != null
                    && bar.Slots.TryGetValue(layout.Character, out var saved) ? saved : null;
                bar.Slots = new Dictionary<string, List<HotbarSlot>>();

                if (old.TryGetValue(bar.Id, out var before) && before.Slots != null) {
                    foreach (var entry in before.Slots) {
                        // with items in the layout, the character's own are the layout's
                        var own = string.Equals(entry.Key, character, StringComparison.OrdinalIgnoreCase);

                        if (layout.Character == null || !own) {
                            bar.Slots[entry.Key] = entry.Value;
                        }
                    }
                }

                if (theirs != null) {
                    bar.Slots[character] = theirs;
                }
            }

            foreach (var bar in data.Bars) {
                windows.Remove(WindowId(bar.Id));
            }

            foreach (var entry in layout.Windows ?? new Dictionary<string, WindowPlacement>()) {
                if (entry.Value != null) {
                    windows[entry.Key] = entry.Value;
                }
            }

            data.Bars = bars;
            data.NextId = bars.Select(b => b.Id + 1).DefaultIfEmpty(1).Max();
            data.Clamp();
        }

        /// <summary>A name made safe as a file name: characters Windows won't take become "_".</summary>
        public static string FileName(string name) {
            var invalid = Path.GetInvalidFileNameChars();
            var replaced = (name ?? "").Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
            var safe = new string(replaced).Trim('.', ' ');
            return (safe.Length == 0 ? "Hotbars" : safe) + ".json";
        }

        /// <summary>Writes the layout to its file in the folder, replacing one of the same name. Returns the file's path.</summary>
        public static string Save(HotbarLayout layout, string folder) {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, FileName(layout.Name));
            File.WriteAllText(path, Json.Write(layout));
            return path;
        }

        /// <summary>The layout files in the folder, by name; none when there's no folder yet.</summary>
        public static List<string> List(string folder) {
            if (!Directory.Exists(folder)) {
                return new List<string>();
            }

            return Directory.GetFiles(folder, "*.json").OrderBy(p => Path.GetFileName(p), StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>The layout in a file, or null (with why) if it can't be read or isn't one.</summary>
        public static HotbarLayout Load(string path, out string problem) {
            problem = null;

            try {
                var layout = Json.Read<HotbarLayout>(File.ReadAllText(path));

                if (layout?.Bars == null || layout.Bars.Count == 0) {
                    problem = $"{Path.GetFileName(path)} isn't a hotbar layout.";
                    return null;
                }

                if (layout.Format > CurrentFormat) {
                    problem = $"{Path.GetFileName(path)} was saved by a newer Daralet Toolkit: update to load it.";
                    return null;
                }

                layout.Bars.RemoveAll(b => b == null);
                return layout;
            }
            catch (Exception ex) {
                problem = $"Couldn't read {Path.GetFileName(path)}: {ex.Message}";
                return null;
            }
        }
    }
}
