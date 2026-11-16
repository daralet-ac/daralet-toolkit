using System;
using System.Globalization;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The strip for a stack you carry, as the game's toolbar shows it (layout 0x21000016, the strip 0x1000019E, 140x31):
    /// the amount and the name along the top, and under it the stack box 0x100001A3 (50x14 at 0,13, the black box
    /// 06004CF5, a number you can type) and the slider 0x100001A4 (90x14 at 50,13: the track 06004CF6, 140 wide and cut
    /// off at the slider's end, and the thumb 06005DC3, 16x14). Behind them the strip's stack picture, 06004CF4
    /// (0x100001A0's state 0x1000000C), green under the name. Both set how many the next drag carries
    /// (StackSplit.SetAmount, the game's stack bar).
    /// </summary>
    internal sealed partial class ToolbarPanel {
        // the game's layout, in the strip's own pixels
        private const float StackStripWidth = 140;
        private const float StackRowTop = 13;
        private const float StackRowHeight = 14;
        private const float StackBoxWidth = 50;
        private const float StackSliderWidth = 90;
        private const float StackTrackArtWidth = 140;
        private const float StackThumbWidth = 16;

        private static readonly uint StackBoxColor = PackedColor.Pack(0, 0, 0);

        // the green glow behind the name, a little darker than the game's so the text stands out from it
        private static readonly uint StackGlowTint = PackedColor.Pack(190, 190, 190);

        // the second, lighter pass of DrawStrong's text and its outline
        private static readonly uint StrongText = PackedColor.Pack(255, 255, 255, 128);
        private static readonly uint StrongOutline = PackedColor.Pack(0, 0, 0, 115);
        private static readonly uint StackTrackColor = PackedColor.Pack(30, 70, 30);
        private static readonly uint StackThumbColor = PackedColor.Pack(60, 200, 60);

        /// <summary>What's typed in the stack box, kept while it has the keyboard.</summary>
        private string stackTyped = "";
        private bool stackBoxActive;

        /// <summary>The game's font's line in its 31-pixel strip (0x40000002's).</summary>
        private const float StripTextHeight = 14;

        /// <summary>The strip's height as drawn this frame, which StripFont picks the text's size from.</summary>
        private static float stripHeight = 31;

        private void RenderStackStrip(int selection, string name, Vector2 min, float width, float height, bool retail) {
            var drawList = ImGui.GetWindowDrawList();
            var stack = StackSplit.StackOf(selection);
            var amount = CoreManager.Current.Actions.SelectedStackCount;
            amount = amount <= 0 || amount > stack ? stack : amount;

            if (retail) {
                DrawStripArt(drawList, "toolbar-strip-stack", min, min + new Vector2(width, height), StackGlowTint);
            }

            // the strip's pixels to the window's: the game's layout stretched to the width and height given
            var scaleX = width / StackStripWidth;
            var rowTop = (float)Math.Round(min.Y + height * StackRowTop / 31f);
            var rowHeight = (float)Math.Round(height * StackRowHeight / 31f);

            // the name with the amount in front, as the game writes it ("3 Brass Salvage"), cut to fit above the row
            var label = $"{amount} {name}";
            drawList.PushClipRect(min, new Vector2(min.X + width, rowTop), true);
            var labelSize = StripTextSize(label);
            DrawStripText(drawList, label, new Vector2(StripTextLeft(min.X, width, labelSize.X),
                retail ? StripTextTop(min.Y, height) : (float)Math.Round(min.Y + (rowTop - min.Y - labelSize.Y) / 2)));
            drawList.PopClipRect();

            var boxMin = new Vector2(min.X, rowTop);
            var boxMax = new Vector2((float)Math.Round(min.X + StackBoxWidth * scaleX), rowTop + rowHeight);
            var changed = RenderStackBox(selection, boxMin, boxMax, ref amount, stack, retail);

            var sliderMin = new Vector2(boxMax.X, rowTop);
            var sliderMax = new Vector2((float)Math.Round(boxMax.X + StackSliderWidth * scaleX), rowTop + rowHeight);
            changed |= RenderStackSlider(sliderMin, sliderMax, scaleX, ref amount, stack, retail);

            if (changed) {
                StackSplit.SetAmount(selection, amount);
            }
        }

        /// <summary>The black box with the amount, typed in as the game's: set on Enter, or once you leave it.</summary>
        private bool RenderStackBox(int selection, Vector2 min, Vector2 max, ref int amount, int stack, bool retail) {
            var drawList = ImGui.GetWindowDrawList();

            if (!retail || !ItemIcons.DrawArtBox(drawList, "stack-box", min, max)) {
                drawList.AddRectFilled(min, max, StackBoxColor);
            }

            var size = max - min;
            ImGui.SetCursorScreenPos(min);
            ImGui.SetNextItemWidth(size.X);
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(3, Math.Max(0, (size.Y - ImGui.GetFontSize()) / 2)));
            ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0f);
            ImGui.PushStyleColor(ImGuiCol.FrameBg, 0u);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, 0u);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, 0u);
            // the box's own text is only seen while typing (ImGui only writes it from the left); otherwise the amount
            // is drawn over it, at the right, as the game's box has it
            ImGui.PushStyleColor(ImGuiCol.Text, stackBoxActive ? TextColor : 0u);

            // while it has the keyboard ImGui keeps its own copy of the text; otherwise it shows the amount as it is
            var id = $"##toolbar-stack-box-{selection}";
            var shown = amount.ToString(CultureInfo.InvariantCulture);

            if (!stackBoxActive) {
                stackTyped = shown;
            }

            ImGui.InputText(id, ref stackTyped, 10, ImGuiInputTextFlags.CharsDecimal | ImGuiInputTextFlags.AutoSelectAll);
            stackBoxActive = ImGui.IsItemActive();
            var done = ImGui.IsItemDeactivatedAfterEdit();

            if (!stackBoxActive) {
                var shownSize = StripTextSize(shown);
                drawList.PushClipRect(min, max, true);
                DrawStripText(drawList, shown, new Vector2(max.X - 4 - shownSize.X, min.Y + (size.Y - shownSize.Y) / 2));
                drawList.PopClipRect();
            }

            ImGui.PopStyleColor(4);
            ImGui.PopStyleVar(2);
            Widgets.Tip("How many of the stack your next drag of it carries, as the game's stack box sets it. Click and "
                + "type a number, then press Enter.");

            if (!done || !int.TryParse(stackTyped, NumberStyles.Integer, CultureInfo.InvariantCulture, out var typed)) {
                return false;
            }

            typed = Math.Max(1, Math.Min(stack, typed));
            stackTyped = typed.ToString(CultureInfo.InvariantCulture);

            if (typed == amount) {
                return false;
            }

            amount = typed;
            return true;
        }

        /// <summary>How big the strip's text is drawn: in the game's font, else ImGui's.</summary>
        private static Vector2 StripTextSize(string text) {
            var font = StripFont();
            return font != null
                ? new Vector2(font.Measure(text), font.LineHeight)
                : ImGui.CalcTextSize(text) + new Vector2(1, 0);
        }

        /// <summary>The strip's text (the name, the stack's amount) in the game's font, else DrawStrong's.</summary>
        private static void DrawStripText(ImDrawListPtr drawList, string text, Vector2 at) {
            if (StripFont()?.Draw(drawList, text, at, TextColor) != true) {
                DrawStrong(drawList, text, at);
            }
        }

        /// <summary>
        /// The game's own font for the strip, 0x40000002 (a 14-pixel line in its 31-pixel strip, matched to the client's
        /// "Cragstone Guard" pixel for pixel), or the serif size nearest that once the strip's scaled: not ImGui's font,
        /// which the Text size makes bigger again on top of the window's scale. Null if the fonts can't be read.
        /// </summary>
        private static GameFont StripFont() {
            return GameFonts.Serif.Nearest(stripHeight * StripTextHeight / 31f);
        }

        /// <summary>
        /// Outlined text made a little heavier (ImGui has no bold font): drawn again a pixel to the right at half
        /// strength, halfway between plain text and text drawn twice in full, both on whole pixels.
        /// </summary>
        private static void DrawStrong(ImDrawListPtr drawList, string text, Vector2 at) {
            at = new Vector2((float)Math.Round(at.X), (float)Math.Round(at.Y));
            var right = at + new Vector2(1, 0);

            foreach (var offset in Outline) {
                drawList.AddText(at + offset, TextOutline, text);
                drawList.AddText(right + offset, StrongOutline, text);
            }

            drawList.AddText(at, TextColor, text);
            drawList.AddText(right, StrongText, text);
        }

        /// <summary>The game's slider: the green track, the thumb at the amount, dragged or clicked along it.</summary>
        private static bool RenderStackSlider(Vector2 min, Vector2 max, float scaleX, ref int amount, int stack,
            bool retail) {
            var drawList = ImGui.GetWindowDrawList();
            var thumbWidth = (float)Math.Round(StackThumbWidth * scaleX);
            var travel = Math.Max(1, max.X - min.X - thumbWidth);

            ImGui.SetCursorScreenPos(min);
            ImGui.InvisibleButton("##toolbar-stack-slider", max - min);
            var changed = false;

            if (ImGui.IsItemActive() && stack > 1) {
                var fraction = Math.Max(0, Math.Min(1, (ImGui.GetIO().MousePos.X - min.X - thumbWidth / 2) / travel));
                var wanted = 1 + (int)Math.Round(fraction * (stack - 1));

                if (wanted != amount) {
                    amount = wanted;
                    changed = true;
                }
            }

            Widgets.Tip($"{amount} of {stack}: how many of the stack your next drag of it carries. Drag the slider, or "
                + "type the number in the box beside it.");

            // the track's picture is wider than the slider, which shows only its start, as the game's does
            drawList.PushClipRect(min, max, true);

            if (!retail || !ItemIcons.DrawArtBox(drawList, "stack-slider", min,
                    new Vector2(min.X + (float)Math.Round(StackTrackArtWidth * scaleX), max.Y))) {
                drawList.AddRectFilled(min, max, StackTrackColor);
            }

            var thumbX = (float)Math.Round(min.X + (stack > 1 ? (amount - 1f) / (stack - 1) : 1f) * travel);
            var thumbMin = new Vector2(thumbX, min.Y);
            var thumbMax = new Vector2(thumbX + thumbWidth, max.Y);

            if (!retail || !ItemIcons.DrawArtBox(drawList, "stack-slider-thumb", thumbMin, thumbMax)) {
                drawList.AddRectFilled(thumbMin, thumbMax, StackThumbColor);
            }

            drawList.PopClipRect();
            return changed;
        }
    }
}
