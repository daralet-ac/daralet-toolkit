using System;
using System.Collections.Generic;
using System.IO;
using ACE.DatLoader.FileTypes;
using Decal.Adapter;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Whether the outdoors can be seen from where you are: outdoors (a cell under 0x100), or in an indoor cell the dats
    /// mark as seeing outside (the EnvCell's SeenOutside flag, 0x1: a building's rooms and doorways, never a dungeon's
    /// cells). The far land, billboards, 3D trees, water reflections, grass and night darkening all went by the cell
    /// number alone, so a building counted as a dungeon (the user's eye, November 2026): with your character in its
    /// doorway cell, standing on the grass outside, the horizon was gone, and stepping out rebuilt every far tree and
    /// billboard at once, a second's hang each time. Your character's cell or the game's camera's will do, so a camera
    /// left indoors behind you still sees out. Each cell is read once from the cell dat (its file, not the dat's cache,
    /// which keeps every file read) and remembered.
    /// </summary>
    internal static unsafe class OutdoorView {
        private const int CameraCell = 0x81FF10 + 4;

        /// <summary>The EnvCell flag for a cell the outdoors can be seen from.</summary>
        private const uint SeenOutside = 0x1;

        private static readonly Dictionary<uint, bool> seesOutside = new Dictionary<uint, bool>();
        private static bool failed;

        /// <summary>Whether the outdoors can be seen now, from your character's cell or the camera's.</summary>
        public static bool Now {
            get {
                var player = unchecked((uint)CoreManager.Current.Actions.Landcell);
                return From(player) || From(*(uint*)CameraCell);
            }
        }

        /// <summary>Whether the outdoors can be seen from this cell (0 counts as not).</summary>
        public static bool From(uint cell) {
            if (cell == 0) {
                return false;
            }

            if ((cell & 0xFFFF) < 0x100) {
                return true;
            }

            if (seesOutside.TryGetValue(cell, out var known)) {
                return known;
            }

            var sees = ReadSeesOutside(cell);

            // a long journey's cells, let go now and then rather than kept for good
            if (seesOutside.Count > 4000) {
                seesOutside.Clear();
            }

            seesOutside[cell] = sees;
            return sees;
        }

        private static bool ReadSeesOutside(uint cell) {
            var cells = UBService.CellDat;

            if (failed || cells == null) {
                return false;
            }

            try {
                var reader = cells.GetReaderForFile(cell);

                if (reader == null) {
                    return false;
                }

                var envCell = new EnvCell();

                using (var input = new MemoryStream(reader.Buffer))
                using (var binary = new BinaryReader(input)) {
                    envCell.Unpack(binary);
                }

                return ((uint)envCell.Flags & SeenOutside) != 0;
            }
            catch (Exception ex) {
                failed = true;
                PluginCore.LogFile("[outdoor-view] couldn't read the cell dat, indoor cells taken to see nothing: "
                    + ex.Message);
                return false;
            }
        }

        /// <summary>Let go at shutdown (a hot reload keeps every build's statics).</summary>
        public static void Clear() {
            seesOutside.Clear();
        }
    }
}
