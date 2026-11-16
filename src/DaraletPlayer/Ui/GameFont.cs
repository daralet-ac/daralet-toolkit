using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using ACE.DatLoader.Entity;
using ImGuiNET;
using UtilityBelt.Common.Enums;
using UtilityBelt.Service.Views;
using DatTexture = ACE.DatLoader.FileTypes.Texture;
using DatFont = ACE.DatLoader.FileTypes.Font;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// One of the game's own fonts (portal.dat 0x40000000 on, GameFonts lists them), drawn as the client draws it: each
    /// letter cut from the font's letter sheet, first from its edge sheet in black (the outline the client puts round its
    /// text), then from its letter sheet in the text's colour, at the font's own pixel size so nothing is stretched.
    /// Both sheets are 8-bit alpha (PFID_A8), made into white textures with that alpha the first time it's drawn, once the
    /// game's graphics device is there. A letter's spacing before and after is signed (stored as bytes, -1 as 255).
    /// </summary>
    internal sealed class GameFont : IDisposable {
        private readonly Dictionary<char, FontCharDesc> letters = new Dictionary<char, FontCharDesc>();
        private readonly uint letterSheetId;
        private readonly uint edgeSheetId;
        private ManagedTexture letterSheet;
        private ManagedTexture edgeSheet;
        private Vector2 sheetSize;
        private bool made;
        private bool failed;

        public GameFont(uint id, DatFont font) {
            Id = id;
            LineHeight = (int)font.MaxCharHeight;
            letterSheetId = font.ForegroundSurfaceDataID;
            edgeSheetId = font.BackgroundSurfaceDataID;

            foreach (var letter in font.CharDescs) {
                var key = (char)letter.Unicode;

                if (!letters.ContainsKey(key)) {
                    letters[key] = letter;
                }
            }
        }

        public uint Id { get; }

        /// <summary>The tallest letter, edge included: a line of this font.</summary>
        public int LineHeight { get; }

        /// <summary>How wide the text is drawn, in pixels.</summary>
        public int Measure(string text) {
            var width = 0;

            foreach (var ch in text ?? "") {
                width += Advance(ch);
            }

            return width;
        }

        /// <summary>
        /// Draws the text with the top of its line at a whole pixel. False if the font's sheets couldn't be made, for the
        /// caller to draw ImGui's text instead.
        /// </summary>
        public bool Draw(ImDrawListPtr drawList, string text, Vector2 at, uint color, bool edge = true) {
            if (!Make()) {
                return false;
            }

            var start = new Vector2((float)Math.Round(at.X), (float)Math.Round(at.Y));

            if (edge && edgeSheet != null) {
                DrawLayer(drawList, edgeSheet, text, start, 0xFF000000);
            }

            DrawLayer(drawList, letterSheet, text, start, color);
            return true;
        }

        private void DrawLayer(ImDrawListPtr drawList, ManagedTexture sheet, string text, Vector2 start, uint color) {
            var x = start.X;

            foreach (var ch in text) {
                if (!TryGet(ch, out var letter)) {
                    x += Advance(ch);
                    continue;
                }

                var left = x + (sbyte)letter.HorizontalOffsetBefore;
                var top = start.Y + letter.VerticalOffsetBefore;
                var uv0 = new Vector2(letter.OffsetX, letter.OffsetY) / sheetSize;
                var uv1 = new Vector2(letter.OffsetX + letter.Width, letter.OffsetY + letter.Height) / sheetSize;
                drawList.AddImage(sheet.TexturePtr, new Vector2(left, top),
                    new Vector2(left + letter.Width, top + letter.Height), uv0, uv1, color);
                x += Advance(ch);
            }
        }

        private int Advance(char ch) {
            return TryGet(ch, out var letter)
                ? (sbyte)letter.HorizontalOffsetBefore + letter.Width + (sbyte)letter.HorizontalOffsetAfter
                : Math.Max(3, LineHeight / 3);
        }

        /// <summary>The letter, or a question mark for one the font hasn't got (but a space, which is left empty).</summary>
        private bool TryGet(char ch, out FontCharDesc letter) {
            if (letters.TryGetValue(ch, out letter)) {
                return true;
            }

            return ch != ' ' && letters.TryGetValue('?', out letter);
        }

        private bool Make() {
            if (made || failed) {
                return made;
            }

            try {
                var portalDat = UtilityBelt.Service.UBService.PortalDat;
                letterSheet = Sheet(portalDat?.ReadFromDat<DatTexture>(letterSheetId), out sheetSize);
                edgeSheet = edgeSheetId == 0 ? null : Sheet(portalDat.ReadFromDat<DatTexture>(edgeSheetId), out _);
                made = letterSheet != null;
                failed = !made;

                if (failed) {
                    PluginCore.LogFile($"[fonts] 0x{Id:X8}'s letter sheet 0x{letterSheetId:X8} couldn't be read.");
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[fonts] Couldn't make 0x{Id:X8}'s sheets: {ex}");
                failed = true;
            }

            return made;
        }

        /// <summary>An A8 sheet as a white texture with the sheet's alpha; null for any other format.</summary>
        private static ManagedTexture Sheet(DatTexture texture, out Vector2 size) {
            size = Vector2.One;

            if (texture == null || texture.Format != SurfacePixelFormat.PFID_A8
                || texture.SourceData == null || texture.SourceData.Length < texture.Width * texture.Height) {
                return null;
            }

            var pixels = new int[texture.Width * texture.Height];

            for (var i = 0; i < pixels.Length; i++) {
                pixels[i] = (texture.SourceData[i] << 24) | 0xFFFFFF;
            }

            using (var bitmap = new Bitmap(texture.Width, texture.Height, PixelFormat.Format32bppArgb)) {
                var data = bitmap.LockBits(new Rectangle(0, 0, texture.Width, texture.Height), ImageLockMode.WriteOnly,
                    PixelFormat.Format32bppArgb);

                try {
                    // a row is exactly its width in pixels at 32 bits each, so the stride is the width's
                    Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
                }
                finally {
                    bitmap.UnlockBits(data);
                }

                size = new Vector2(texture.Width, texture.Height);

                // the texture takes a copy of the bitmap
                return new ManagedTexture(bitmap);
            }
        }

        public void Dispose() {
            letterSheet?.Dispose();
            edgeSheet?.Dispose();
            letterSheet = null;
            edgeSheet = null;
            made = false;
        }
    }
}
