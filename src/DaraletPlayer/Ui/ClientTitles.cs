using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using DaraletPlayer.Feed;
using Decal.Adapter;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Your character titles, for the Attributes window's Titles tab: which you have and which is shown, read from the
    /// server's own messages as they come (TitleMessages: the whole table at login, then each title granted or picked),
    /// since Decal doesn't pass them on; so a plugin started after login (a hot reload) has none until the next login.
    /// Their names are the game's, read once from the dats (TitleNames): the portal dat UtilityBelt has open, and
    /// client_local_English.dat from the game's own folder, opened here only for its title strings. Picking one is the
    /// client's own message, CM_Social.Event_SetDisplayCharacterTitle (0x002C, the game's Set as Display Title), which the
    /// server answers with UpdateTitle. [titles] lines in log.txt.
    /// </summary>
    internal sealed class ClientTitles : IDisposable {
        private readonly CharacterTitles titles = new CharacterTitles();
        private Dictionary<uint, string> names;
        private bool namesTried;
        private bool loggedTable;

        public ClientTitles() {
            CoreManager.Current.EchoFilter.ServerDispatch += EchoFilter_ServerDispatch;
        }

        /// <summary>The titles told for the character playing now; null when none have come for them.</summary>
        public CharacterTitles Mine {
            get {
                var me = (uint)CoreManager.Current.CharacterFilter.Id;
                return titles.Character != 0 && titles.Character == me ? titles : null;
            }
        }

        /// <summary>A title's name, as the game shows it, or "Title n" when the dats couldn't be read.</summary>
        public string Name(uint id) {
            LoadNames();
            return names != null && names.TryGetValue(id, out var name) ? name : $"Title {id}";
        }

        /// <summary>Makes this the title other players see, as the game's Set as Display Title does.</summary>
        public void SetDisplay(uint id) {
            try {
                PluginCore.LogFile($"[titles] set display title {id} ({Name(id)})");
                AcClient.CM_Social.Event_SetDisplayCharacterTitle(id);
            }
            catch (Exception ex) {
                PluginCore.LogFile("[titles] couldn't set the display title: " + ex);
                PluginCore.Chat("Couldn't set your title (see log.txt).");
            }
        }

        private void LoadNames() {
            if (namesTried) {
                return;
            }

            namesTried = true;
            var watch = Stopwatch.StartNew();

            try {
                ItemIcons.EnsurePortalDat();
                var map = UBService.PortalDat?.ReadFromDat<EnumMapper>(TitleNames.EnumMapperId);
                var file = Path.Combine(System.Environment.CurrentDirectory, "client_local_English.dat");

                if (map == null || !File.Exists(file)) {
                    PluginCore.LogFile($"[titles] no names: portal dat's title list {(map == null ? "missing" : "read")}, "
                        + $"{file} {(File.Exists(file) ? "there" : "missing")}");
                    return;
                }

                var language = new LanguageDatDatabase(file, false);
                var byHash = new Dictionary<uint, string>();

                foreach (var entry in language.CharacterTitles.StringTableData) {
                    var text = entry.Strings?.FirstOrDefault();

                    if (text != null) {
                        byHash[entry.Id] = text;
                    }
                }

                names = TitleNames.Build(map.IdToStringMap, byHash);
                PluginCore.LogFile($"[titles] {names.Count} title names read in {watch.ElapsedMilliseconds}ms");
            }
            catch (Exception ex) {
                PluginCore.LogFile("[titles] couldn't read the title names: " + ex);
            }
        }

        private void EchoFilter_ServerDispatch(object sender, NetworkMessageEventArgs e) {
            try {
                if (e.Message.Type != CooldownMessages.GameEvent) {
                    return;
                }

                var change = TitleMessages.Read(e.Message.RawData);

                if (change == null) {
                    return;
                }

                titles.Apply(change);

                if (change.Whole && !loggedTable) {
                    loggedTable = true;
                    PluginCore.LogFile($"[titles] {change.Titles.Count} titles, display title {change.Display}");
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile("[titles] " + ex);
            }
        }

        public void Dispose() {
            CoreManager.Current.EchoFilter.ServerDispatch -= EchoFilter_ServerDispatch;
        }
    }
}
