using System;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Every slider in the plugin: ImGui's own (so dragging, clicking anywhere on it and Ctrl+click or double-click to type
    /// a number work as ever), drawn see-through, with the game's option slider art over it (template 0x1000036E: the
    /// 120x12 track 06001285 stretched to the width, the 7x12 thumb 06001286) and the number outlined on top. The thumb
    /// sits where ImGui's grab would (ImGui's SliderBehavior: 2 pixels of padding, an int slider's grab a step wide), so
    /// it stays under the mouse. While a number is typed in it's ImGui's plain box. Never call ImGui.SliderInt or
    /// SliderFloat directly.
    /// </summary>
    internal static class Sliders {
        /// <summary>ImGui's SliderBehavior keeps the grab this far inside the frame.</summary>
        private const float GrabPadding = 2;

        // packed by hand: ImGui.GetColorU32 crashes before ImGui has a context
        private static readonly uint Outline = PackedColor.Pack(0, 0, 0, 220);

        /// <summary>The slider being typed in, drawn as ImGui's own box until it's let go of.</summary>
        private static uint typing;

        public static bool SliderInt(string label, ref int value, int min, int max, string format = "%d") {
            var id = ImGui.GetID(label);
            var plain = typing == id;
            var width = ImGui.CalcItemWidth();
            var origin = ImGui.GetCursorScreenPos();

            Hide(plain);
            var changed = ImGui.SliderInt(label, ref value, min, max, format);
            Show(plain);

            var range = (float)max - min;
            Finish(id, label, origin, width, plain, range > 0 ? (value - min) / range : 0, range, false,
                SliderText.Format(format, value));
            return changed;
        }

        public static bool SliderFloat(string label, ref float value, float min, float max, string format = "%.3f") {
            var id = ImGui.GetID(label);
            var plain = typing == id;
            var width = ImGui.CalcItemWidth();
            var origin = ImGui.GetCursorScreenPos();

            Hide(plain);
            var changed = ImGui.SliderFloat(label, ref value, min, max, format);
            Show(plain);

            var range = max - min;
            Finish(id, label, origin, width, plain, range > 0 ? (value - min) / range : 0, range, true,
                SliderText.Format(format, value));
            return changed;
        }

        /// <summary>ImGui's frame, grab and text drawn see-through, the art going over them.</summary>
        private static void Hide(bool plain) {
            if (plain) {
                return;
            }

            ImGui.PushStyleColor(ImGuiCol.FrameBg, 0u);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, 0u);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, 0u);
            ImGui.PushStyleColor(ImGuiCol.SliderGrab, 0u);
            ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, 0u);
            ImGui.PushStyleColor(ImGuiCol.Text, 0u);
        }

        private static void Show(bool plain) {
            if (!plain) {
                ImGui.PopStyleColor(6);
            }
        }

        private static void Finish(uint id, string label, Vector2 origin, float width, bool plain, float t, float range,
            bool floating, string text) {
            var active = ImGui.IsItemActive();
            var hovered = ImGui.IsItemHovered();

            // Ctrl+click and double-click make ImGui's slider a box to type in, from the frame it's clicked
            if (ImGui.IsItemActivated() && (ImGui.GetIO().KeyCtrl || ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))) {
                typing = id;
            }
            else if (typing == id && !active) {
                typing = 0;
            }

            if (plain) {
                return;
            }

            var style = ImGui.GetStyle();
            var height = ImGui.GetFrameHeight();
            var drawList = ImGui.GetWindowDrawList();
            t = Math.Max(0, Math.Min(1, t));

            // the track, a little shorter than the frame so the number has room to read over it
            var track = (float)Math.Round(Math.Max(8, Math.Min(height - 2, height * 0.65f)));
            var trackMin = new Vector2(origin.X, (float)Math.Round(origin.Y + (height - track) / 2));
            var trackMax = new Vector2(origin.X + width, trackMin.Y + track);

            if (!ItemIcons.DrawArtBox(drawList, "option-slider", trackMin, trackMax)) {
                drawList.AddRectFilled(trackMin, trackMax, ImGui.GetColorU32(ImGuiCol.FrameBg));
            }

            if (hovered || active) {
                drawList.AddRect(trackMin - Vector2.One, trackMax + Vector2.One,
                    ImGui.GetColorU32(active ? ImGuiCol.SliderGrabActive : ImGuiCol.SliderGrab));
            }

            // the thumb's middle where ImGui's grab's is
            var sliderSize = width - GrabPadding * 2;
            var grab = style.GrabMinSize;

            if (!floating && range >= 0) {
                grab = Math.Max(sliderSize / (range + 1), style.GrabMinSize);
            }

            grab = Math.Min(grab, sliderSize);
            var centre = origin.X + GrabPadding + grab / 2 + t * (sliderSize - grab);
            var thumbWidth = (float)Math.Max(4, Math.Round(track * 7 / 12));
            var thumbX = (float)Math.Round(centre - thumbWidth / 2);
            var thumbMin = new Vector2(thumbX, trackMin.Y);
            var thumbMax = new Vector2(thumbX + thumbWidth, trackMax.Y);

            if (!ItemIcons.DrawArtBox(drawList, "option-slider-thumb", thumbMin, thumbMax)) {
                drawList.AddRectFilled(thumbMin, thumbMax, ImGui.GetColorU32(ImGuiCol.SliderGrabActive));
            }

            if (!string.IsNullOrEmpty(text)) {
                var size = ImGui.CalcTextSize(text);
                var at = new Vector2((float)Math.Round(origin.X + (width - size.X) / 2),
                    (float)Math.Round(origin.Y + (height - size.Y) / 2));
                Outlined(drawList, text, at);
            }

            // the label ImGui wrote see-through, at its place past the frame
            var shown = Visible(label);

            if (shown.Length > 0) {
                drawList.AddText(new Vector2(origin.X + width + style.ItemInnerSpacing.X, origin.Y + style.FramePadding.Y),
                    ImGui.GetColorU32(ImGuiCol.Text), shown);
            }
        }

        /// <summary>The part of an ImGui label that's shown: up to "##".</summary>
        private static string Visible(string label) {
            var hidden = label.IndexOf("##", StringComparison.Ordinal);
            return hidden < 0 ? label : label.Substring(0, hidden);
        }

        private static void Outlined(ImDrawListPtr drawList, string text, Vector2 at) {
            foreach (var offset in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1) }) {
                drawList.AddText(at + offset, Outline, text);
            }

            drawList.AddText(at, ImGui.GetColorU32(ImGuiCol.Text), text);
        }
    }
}
