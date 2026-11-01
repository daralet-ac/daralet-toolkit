using System;
using System.Collections.Generic;
using System.Linq;
using ImGuiNET;
using UtilityBelt.Service;
using UtilityBelt.Service.Views;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The chat windows' fonts, each made at exactly the size it's drawn at, so text is sharp at any size: ImGui stretches
    /// one font's picture for SetWindowFontScale, which blurs it. UtilityBelt.Service lists the fonts Windows and it have
    /// (its FontManager, the one its own font setting uses) and adds one to ImGui's font picture on request. Adding one can
    /// only be done between ImGui's frames, and it can't be drawn until UtilityBelt has rebuilt the picture at the start
    /// of the next, so a font asked for while drawing (Get) is made on Decal's next RenderFrame (MakeWanted), and
    /// meanwhile the window draws with UtilityBelt's font, stretched. Each new one rebuilds the font picture (and every
    /// texture with it), a moment's stall, so there are only so many.
    /// </summary>
    internal static class ChatFonts {
        /// <summary>The most fonts made: every size of every family is one of its own.</summary>
        private const int MaxMade = 24;

        private static readonly Dictionary<(string Family, int Size), FontInstance> made =
            new Dictionary<(string, int), FontInstance>();

        private static readonly HashSet<(string Family, int Size)> wanted = new HashSet<(string, int)>();
        private static readonly HashSet<(string Family, int Size)> failed = new HashSet<(string, int)>();

        /// <summary>
        /// Each font's advances by character, unscaled, read once from ImGui (ChatLayout measures with them).
        /// </summary>
        private static readonly Dictionary<IntPtr, (float[] Advances, float Fallback, float Size)> advances =
            new Dictionary<IntPtr, (float[], float, float)>();

        private static List<string> families;
        private static bool tooMany;

        /// <summary>
        /// The font of a family ("" for UtilityBelt's own) at a size, if it's ready to draw with; else null, and it's made
        /// before the next frame if it can be.
        /// </summary>
        public static unsafe ImFontPtr? Get(string family, int size) {
            try {
                var current = UBService.Huds.CurrentFont;

                // UtilityBelt's own font at its own size is there already
                if (string.IsNullOrEmpty(family) && current != null && current.Size == size) {
                    return current.Font;
                }

                var key = (family ?? "", size);

                if (made.TryGetValue(key, out var instance)) {
                    var font = instance.Font;
                    return font.NativePtr != null && font.ContainerAtlas.NativePtr != null ? font : (ImFontPtr?)null;
                }

                if (!failed.Contains(key)) {
                    wanted.Add(key);
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[chat] font: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Makes the fonts asked for, one a frame, from Decal's RenderFrame: outside ImGui's frame, where its font picture
        /// can take a new one.
        /// </summary>
        public static void MakeWanted() {
            if (wanted.Count == 0) {
                return;
            }

            var key = wanted.First();
            wanted.Remove(key);

            if (made.ContainsKey(key) || failed.Contains(key)) {
                return;
            }

            if (made.Count >= MaxMade) {
                failed.Add(key);

                if (!tooMany) {
                    tooMany = true;
                    PluginCore.LogFile($"[chat] no more fonts made: {MaxMade} already (each family and size is one)");
                }

                return;
            }

            try {
                var info = Find(key.Family);

                if (info == null) {
                    failed.Add(key);
                    PluginCore.LogFile($"[chat] no font called {key.Family}");
                    return;
                }

                made[key] = UBService.Huds.FontManager.Create(info, key.Size);
                PluginCore.LogFile($"[chat] made font {info.Family} {key.Size}px");
            }
            catch (Exception ex) {
                failed.Add(key);
                PluginCore.LogFile($"[chat] couldn't make font {key.Family} {key.Size}px: {ex.Message}");
            }
        }

        /// <summary>The families there are to pick from, by name, regular weights only.</summary>
        public static IReadOnlyList<string> Families() {
            if (families != null) {
                return families;
            }

            try {
                families = UBService.Huds.FontManager.AvailableFonts
                    .Where(f => f.SubFamily == FontSubFamily.Regular && !string.IsNullOrWhiteSpace(f.Family))
                    .Select(f => f.Family)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[chat] fonts couldn't be listed: {ex.Message}");
                families = new List<string>();
            }

            return families;
        }

        /// <summary>The name of UtilityBelt's own font, for the picker.</summary>
        public static string UtilityBeltFont() {
            try {
                return UBService.Huds.CurrentFont?.Info?.Family;
            }
            catch {
                return null;
            }
        }

        /// <summary>
        /// How wide a character is in a font drawn at a size: ImGui's own advances (IndexAdvanceX), which is all it uses to
        /// lay text out, read into a table once a font, so wrapping a line needs no call into ImGui per letter.
        /// </summary>
        public static unsafe Func<char, float> Advance(ImFontPtr font, float size) {
            var key = (IntPtr)font.NativePtr;

            if (!advances.TryGetValue(key, out var table)) {
                var source = font.IndexAdvanceX;
                var count = Math.Min(source.Size, 0x10000);
                var copy = new float[count];

                for (var i = 0; i < count; i++) {
                    copy[i] = source[i];
                }

                table = (copy, font.FallbackAdvanceX, font.FontSize);
                advances[key] = table;
            }

            var scale = table.Size > 0 ? size / table.Size : 1;
            var array = table.Advances;
            var fallback = table.Fallback;
            return c => (c < array.Length ? array[c] : fallback) * scale;
        }

        private static FontInfo Find(string family) {
            if (string.IsNullOrEmpty(family)) {
                return UBService.Huds.CurrentFont?.Info;
            }

            var fonts = UBService.Huds.FontManager.AvailableFonts.Where(f =>
                string.Equals(f.Family, family, StringComparison.OrdinalIgnoreCase)).ToList();
            return fonts.FirstOrDefault(f => f.SubFamily == FontSubFamily.Regular) ?? fonts.FirstOrDefault();
        }
    }
}
