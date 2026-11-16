using System;
using System.Collections.Generic;
using System.Linq;
using DatFont = ACE.DatLoader.FileTypes.Font;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's fonts by family, each in the sizes portal.dat has it (0x40000000 on, 49 of them), so text drawn bigger
    /// takes the nearest size rather than being stretched. Families as they look (the layouts name a font in a property
    /// not decoded yet): Serif (book-like: the Toolbar strip's name is 0x40000002, matched pixel for pixel), Sans
    /// (Tahoma-like), Display (Celtic capitals, for titles) and Mono. 0x26-0x32 are more of these in other sizes and
    /// weights, and 0x17, 0x18, 0x21-0x23 a large character set with no edge sheet; not grouped yet. Each font is read
    /// the first time a size is asked for, its textures made the first time it's drawn; Free lets them all go.
    /// </summary>
    internal static class GameFonts {
        public static readonly Family Serif = new Family("serif",
            0x4000001C, 0x40000025, 0x40000002, 0x40000000, 0x40000001, 0x40000004, 0x40000005, 0x40000006);

        public static readonly Family Sans = new Family("sans",
            0x40000007, 0x40000008, 0x40000009, 0x4000000A, 0x4000000B, 0x40000019, 0x4000000C, 0x4000000D, 0x4000001B);

        public static readonly Family Display = new Family("display",
            0x40000016, 0x4000000E, 0x4000000F, 0x40000010, 0x40000011, 0x40000012, 0x40000024, 0x40000013, 0x40000014);

        public static readonly Family Mono = new Family("mono",
            0x4000001D, 0x4000001E, 0x4000001A, 0x4000001F, 0x40000030, 0x40000031, 0x40000020);

        private static readonly Family[] All = { Serif, Sans, Display, Mono };

        /// <summary>Lets every font's textures go (the plugin stopping); they're made again if drawn after.</summary>
        public static void Free() {
            foreach (var family in All) {
                family.Free();
            }
        }

        /// <summary>One family's sizes, read from the dat once.</summary>
        internal sealed class Family {
            private readonly string name;
            private readonly uint[] ids;
            private List<GameFont> fonts;
            private bool failed;

            public Family(string name, params uint[] ids) {
                this.name = name;
                this.ids = ids;
            }

            /// <summary>
            /// The size whose line is nearest this many pixels, the smaller of two as near; null if the fonts can't be
            /// read (no portal dat), for the caller to draw ImGui's text instead.
            /// </summary>
            public GameFont Nearest(float lineHeight) {
                if (!Load()) {
                    return null;
                }

                return fonts.OrderBy(f => Math.Abs(f.LineHeight - lineHeight)).ThenBy(f => f.LineHeight).First();
            }

            private bool Load() {
                if (fonts != null || failed) {
                    return fonts != null;
                }

                try {
                    ItemIcons.EnsurePortalDat();
                    var portalDat = UtilityBelt.Service.UBService.PortalDat;

                    if (portalDat == null) {
                        failed = true;
                        PluginCore.LogFile($"[fonts] No portal dat, so the game's {name} fonts can't be drawn.");
                        return false;
                    }

                    var read = new List<GameFont>();

                    foreach (var id in ids) {
                        try {
                            read.Add(new GameFont(id, portalDat.ReadFromDat<DatFont>(id)));
                        }
                        catch (Exception ex) {
                            PluginCore.LogFile($"[fonts] Couldn't read 0x{id:X8}: {ex.Message}");
                        }
                    }

                    failed = read.Count == 0;
                    fonts = failed ? null : read;
                    PluginCore.LogFile($"[fonts] {name}: {string.Join(", ", read.Select(f => $"0x{f.Id:X8} {f.LineHeight}px"))}");
                }
                catch (Exception ex) {
                    PluginCore.LogFile($"[fonts] Couldn't read the game's {name} fonts: {ex}");
                    failed = true;
                }

                return fonts != null;
            }

            public void Free() {
                if (fonts == null) {
                    return;
                }

                foreach (var font in fonts) {
                    font.Dispose();
                }
            }
        }
    }
}
