using System;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Pieces the game's panel-frame pages share (Character Detail, Spell Management), for the windows that stand in for
    /// them (AttributesPanel, SpellbookPanel): the frame's tabs (template 0x1000043A, ends 17x25 and a 66x25 middle, closed
    /// 06005D93/95/97, open 06005D92/94/96), the thin separator bar (06004CC7), and centred and label-value lines.
    /// </summary>
    internal static class GamePanelArt {
        private static readonly Vector2 RetailTab = new Vector2(100, 25);
        private const float TabCap = 17;

        /// <summary>
        /// How tall Tabs draws its tabs: the height asked for (the panels' 25 if none) at the Text size, never shorter than
        /// a label needs (the frame's height for the panels' tabs, as before; the text and a little room for flatter ones).
        /// </summary>
        public static float TabHeight(float? tabHeight = null) {
            return tabHeight.HasValue
                ? Math.Max(Widgets.Scaled(tabHeight.Value), ImGui.GetTextLineHeight() + Widgets.Scaled(4))
                : Math.Max(Widgets.Scaled(RetailTab.Y), ImGui.GetFrameHeight());
        }

        /// <summary>A row of tabs, lit where open; a tab whose value is null is a button (Titles). The value clicked, or null.</summary>
        /// <param name="maxWidth">How wide the row may be, for something beside it (the Vendor window's name); null for the
        /// window's width.</param>
        /// <param name="tabHeight">The tabs' height at 100% text, for a window whose tabs are flatter than the panels' 25
        /// (the vendor's, 20); null for 25.</param>
        public static string Tabs(string id, (string Label, string Value, string Tip)[] tabs, string open, bool retail,
            float tabWidth = 100, float? maxWidth = null, float? tabHeight = null) {
            string clicked = null;
            var height = TabHeight(tabHeight);
            var gap = retail ? 0 : ImGui.GetStyle().ItemSpacing.X;

            var width = Math.Min(maxWidth ?? float.MaxValue, ImGui.GetContentRegionAvail().X);
            var room = width - gap * (tabs.Length - 1);
            var widths = TabWidths(tabs.Select(t => ImGui.CalcTextSize(t.Label).X).ToArray(), Widgets.Scaled(tabWidth),
                Widgets.Scaled(16), Widgets.Scaled(6), room);

            for (var i = 0; i < tabs.Length; i++) {
                if (i > 0) {
                    ImGui.SameLine(0, gap);
                }

                var (label, value, tip) = tabs[i];
                var isOpen = value != null && open == value;
                var size = new Vector2(widths[i], height);
                bool pressed;

                if (!retail) {
                    pressed = Form.LitButton($"{label}##{id}-tab", isOpen, size);
                }
                else {
                    var min = ImGui.GetCursorScreenPos();
                    pressed = ImGui.InvisibleButton($"##{id}-tab-{label}", size);
                    DrawTab(ImGui.GetWindowDrawList(), min, size, isOpen, label);
                }

                Widgets.Tip(tip);

                if (pressed && !isOpen) {
                    clicked = value ?? label;
                }
            }

            return clicked;
        }

        /// <summary>
        /// The tabs' widths: each as wide as its label and padding (the game's Options tabs are), or the least width; when
        /// that's wider than the room, first the room past each label's need is given up, the tabs with the most to spare
        /// the most, and only then are all narrowed alike, so a long label ("Gameplay Options") isn't squeezed while short
        /// ones keep spare room.
        /// </summary>
        /// <param name="labels">Each label's width.</param>
        /// <param name="least">The least a tab is (tabWidth).</param>
        /// <param name="padding">Room either side of a label altogether, when there's room for it.</param>
        /// <param name="tight">Room either side of a label altogether, at a pinch.</param>
        public static float[] TabWidths(float[] labels, float least, float padding, float tight, float room) {
            var widths = labels.Select(l => Math.Max(least, l + padding)).ToArray();
            var total = widths.Sum();

            if (total <= room || room <= 0) {
                return widths;
            }

            var needs = labels.Select(l => l + tight).ToArray();
            var spare = widths.Select((w, i) => Math.Max(0, w - needs[i])).ToArray();
            var spareTotal = spare.Sum();
            var excess = total - room;

            if (spareTotal >= excess) {
                // the spare room alone is enough, taken from each tab by how much it has
                return widths.Select((w, i) => (float)Math.Floor(w - spare[i] * excess / spareTotal)).ToArray();
            }

            // every tab at its label's need is still too wide: all narrowed alike, the labels drawn smaller (DrawTab)
            var needTotal = needs.Sum();
            return needs.Select(n => Math.Max(1, (float)Math.Floor(n * room / needTotal))).ToArray();
        }

        /// <summary>The panel's tab: its ends at their own shape, the middle stretched, open or not.</summary>
        private static void DrawTab(ImDrawListPtr drawList, Vector2 min, Vector2 size, bool open, string label) {
            var state = open ? "panel-tab-open" : "panel-tab";
            var cap = (float)Math.Round(Math.Min(size.X / 3, TabCap * size.Y / RetailTab.Y));
            var max = min + size;
            var drawn = ItemIcons.DrawArtBox(drawList, state + "-left", min, new Vector2(min.X + cap, max.Y))
                && ItemIcons.DrawArtBox(drawList, state + "-middle", new Vector2(min.X + cap, min.Y),
                    new Vector2(max.X - cap, max.Y))
                && ItemIcons.DrawArtBox(drawList, state + "-right", new Vector2(max.X - cap, min.Y), max);

            if (!drawn) {
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(open ? ImGuiCol.TabActive : ImGuiCol.Tab));
            }

            // the label kept inside its tab when the tabs have been narrowed to fit: drawn smaller (to 70%) first, and only
            // then clipped
            var room = size.X - Widgets.Scaled(6);
            var fontSize = ImGui.GetFontSize();
            var textSize = ImGui.CalcTextSize(label);

            if (textSize.X > room && textSize.X > 0) {
                var scale = Math.Max(0.7f, room / textSize.X);
                fontSize *= scale;
                textSize *= scale;
            }

            drawList.PushClipRect(min, max, true);
            drawList.AddText(ImGui.GetFont(), fontSize, new Vector2((float)Math.Round(min.X + Math.Max(2, (size.X
                - textSize.X) / 2)), (float)Math.Round(min.Y + (size.Y - textSize.Y) / 2)),
                ImGui.GetColorU32(open ? ImGuiCol.Text : ImGuiCol.TextDisabled), label);
            drawList.PopClipRect();
        }

        /// <summary>The game's thin bar between a page's parts (06004CC7), or ImGui's line.</summary>
        public static void Separator(bool retail) {
            var min = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var height = Widgets.Scaled(7);

            if (retail && ItemIcons.DrawArtBox(ImGui.GetWindowDrawList(), "panel-separator", min,
                min + new Vector2(width, height))) {
                ImGui.Dummy(new Vector2(width, height));
            }
            else {
                ImGui.Separator();
            }
        }

        /// <summary>
        /// One of the game's three-part buttons (template 0x100002BC: 32-pixel ends and a stretched middle, states 1, 3
        /// pressed and D disabled): "menu-button" the Options page's blue ones (0x10000618: 060074A7-AF), "cast" the red
        /// Apply, Reset and Defaults (0x100002CD). A themed button in Modern. True when clicked.
        /// </summary>
        public static bool Button(string id, string label, Vector2 size, string art, bool retail, bool enabled = true) {
            if (!retail) {
                ImGui.BeginDisabled(!enabled);
                var pressedModern = ImGui.Button($"{label}##{id}", size);
                ImGui.EndDisabled();
                return pressedModern && enabled;
            }

            var min = ImGui.GetCursorScreenPos();
            ImGui.BeginDisabled(!enabled);
            var clicked = ImGui.InvisibleButton($"##{id}", size);
            ImGui.EndDisabled();

            var state = !enabled ? "-disabled" : ImGui.IsItemActive() ? "-pressed" : "";
            var drawList = ImGui.GetWindowDrawList();
            var max = min + size;
            var cap = (float)Math.Round(Math.Min(size.X / 2, 32 * size.Y / 32));
            var drawn = ItemIcons.DrawArtBox(drawList, art + "-left" + state, min, new Vector2(min.X + cap, max.Y))
                && ItemIcons.DrawArtBox(drawList, art + "-middle" + state, new Vector2(min.X + cap, min.Y),
                    new Vector2(max.X - cap, max.Y))
                && ItemIcons.DrawArtBox(drawList, art + "-right" + state, new Vector2(max.X - cap, min.Y), max);

            if (!drawn) {
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(enabled ? ImGuiCol.Button : ImGuiCol.FrameBg));
            }

            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(new Vector2((float)Math.Round(min.X + (size.X - textSize.X) / 2),
                (float)Math.Round(min.Y + (size.Y - textSize.Y) / 2)),
                ImGui.GetColorU32(enabled ? ImGuiCol.Text : ImGuiCol.TextDisabled), label);
            return clicked && enabled;
        }

        /// <summary>
        /// One of the game's option lights (template 0x1000032B: a 13x13 light, 06004D15 off, 16 off pressed, 17 on, 18 on
        /// pressed, 19 disabled) with its label after it, the whole line clickable; ImGui's checkbox in Modern. True when
        /// clicked, with on flipped.
        /// </summary>
        public static bool Light(string id, string label, ref bool on, bool retail, bool enabled = true) {
            if (!retail) {
                ImGui.BeginDisabled(!enabled);
                var changed = ImGui.Checkbox($"{label}##{id}", ref on);
                ImGui.EndDisabled();
                return changed && enabled;
            }

            var height = Math.Max(ImGui.GetTextLineHeight(), Widgets.Scaled(14));
            var light = Widgets.Scaled(13);
            var gap = Widgets.Scaled(8);
            var width = light + gap + ImGui.CalcTextSize(label).X;
            var min = ImGui.GetCursorScreenPos();

            ImGui.BeginDisabled(!enabled);
            var clicked = ImGui.InvisibleButton($"##{id}", new Vector2(width, height));
            ImGui.EndDisabled();

            var pressed = ImGui.IsItemActive();
            var art = !enabled ? "option-disabled" : (on ? "option-on" : "option-off") + (pressed ? "-pressed" : "");
            var drawList = ImGui.GetWindowDrawList();
            var lightMin = new Vector2(min.X, (float)Math.Round(min.Y + (height - light) / 2));

            if (!ItemIcons.DrawArtBox(drawList, art, lightMin, lightMin + new Vector2(light, light))) {
                drawList.AddCircleFilled(lightMin + new Vector2(light, light) / 2, light / 2,
                    ImGui.GetColorU32(on ? ImGuiCol.CheckMark : ImGuiCol.FrameBg));
            }

            drawList.AddText(new Vector2(min.X + light + gap, (float)Math.Round(min.Y + (height
                - ImGui.GetTextLineHeight()) / 2)), ImGui.GetColorU32(enabled ? ImGuiCol.Text : ImGuiCol.TextDisabled), label);

            if (clicked && enabled) {
                on = !on;
                return true;
            }

            return false;
        }

        /// <summary>
        /// One of the game's option sliders (template 0x1000036E: its 120x12 track 06001285 stretched to the width, the
        /// 7x12 thumb 06001286), with the words for its ends under it when given; ImGui's slider in Modern. True while
        /// dragged, with value moved.
        /// </summary>
        public static bool Slider(string id, ref float value, float min, float max, float width, bool retail,
            string low = null, string high = null) {
            var changed = false;

            if (!retail) {
                ImGui.SetNextItemWidth(width);
                changed = Sliders.SliderFloat($"##{id}", ref value, min, max, "");
            }
            else {
                var size = new Vector2(width, Math.Max(Widgets.Scaled(12), 8));
                var origin = ImGui.GetCursorScreenPos();
                ImGui.InvisibleButton($"##{id}", size);

                if (ImGui.IsItemActive() && max > min) {
                    var t = Math.Max(0, Math.Min(1, (ImGui.GetIO().MousePos.X - origin.X) / size.X));
                    var next = min + t * (max - min);
                    changed = Math.Abs(next - value) > float.Epsilon;
                    value = next;
                }

                var drawList = ImGui.GetWindowDrawList();

                if (!ItemIcons.DrawArtBox(drawList, "option-slider", origin, origin + size)) {
                    drawList.AddRectFilled(origin, origin + size, ImGui.GetColorU32(ImGuiCol.FrameBg));
                }

                var thumb = new Vector2(size.Y * 7 / 12, size.Y);
                var at = max > min ? (value - min) / (max - min) : 0;
                var x = (float)Math.Round(origin.X + Math.Max(0, Math.Min(1, at)) * (size.X - thumb.X));

                if (!ItemIcons.DrawArtBox(drawList, "option-slider-thumb", new Vector2(x, origin.Y),
                    new Vector2(x + thumb.X, origin.Y + size.Y))) {
                    drawList.AddRectFilled(new Vector2(x, origin.Y), new Vector2(x + thumb.X, origin.Y + size.Y),
                        ImGui.GetColorU32(ImGuiCol.SliderGrab));
                }
            }

            // the words for the ends, a little smaller, under the slider as the game sets them
            if (low != null || high != null) {
                // under the slider itself: the next line starts at the row's left, where the label is
                var at = new Vector2(ImGui.GetItemRectMin().X, ImGui.GetCursorScreenPos().Y);
                var size = ImGui.GetFontSize() * 0.85f;
                var scale = size / ImGui.GetFontSize();
                var color = ImGui.GetColorU32(ImGuiCol.TextDisabled);
                var drawList = ImGui.GetWindowDrawList();

                if (low != null) {
                    drawList.AddText(ImGui.GetFont(), size, at, color, low);
                }

                if (high != null) {
                    var highWidth = ImGui.CalcTextSize(high).X * scale;
                    drawList.AddText(ImGui.GetFont(), size, new Vector2(at.X + Math.Max(0, width - highWidth), at.Y), color,
                        high);
                }

                ImGui.Dummy(new Vector2(width, size));
            }

            return changed;
        }

        /// <summary>
        /// The game's dark stone (06004CC2, 48x48, which the Vitae panel and the vendor's pages are made of), tiled over a
        /// box as the game tiles it.
        /// </summary>
        public static void Stone(ImDrawListPtr drawList, Vector2 min, Vector2 max) {
            var tile = Widgets.Scaled(48);
            drawList.PushClipRect(min, max, true);

            for (var y = min.Y; y < max.Y; y += tile) {
                ItemIcons.DrawArtTiled(drawList, "vitae-background", new Vector2(min.X, y), new Vector2(max.X, y + tile),
                    tile);
            }

            drawList.PopClipRect();
        }

        /// <summary>The game's gold bar between an options page's groups (060012C5, 292x8), or ImGui's line.</summary>
        public static void GoldBar(bool retail) {
            var min = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var height = Widgets.Scaled(8);

            if (retail && ItemIcons.DrawArtBox(ImGui.GetWindowDrawList(), "option-separator", min,
                min + new Vector2(width, height))) {
                ImGui.Dummy(new Vector2(width, height));
            }
            else {
                ImGui.Separator();
            }
        }

        /// <summary>
        /// Text centred in a line this wide, in bold: a label drawn by GDI+ at exactly the text's pixel size (ItemIcons'
        /// labels, Segoe UI bold, scale times the font size), on whole pixels. ImGui has no bold font, and its text drawn
        /// twice a pixel apart, at the fractional place centring gives, smeared. One line of text tall (the Vendor
        /// window's picked name, the creature's name). Plain text in that colour if a label can't be made.
        /// </summary>
        public static void BoldCentred(ItemIcons icons, string text, float width, float scale, uint color) {
            var pixels = (int)Math.Round(ImGui.GetFontSize() * scale);
            var size = icons.LabelSize(text, pixels, outlined: false);

            if (!size.HasValue) {
                Centred(text, width, ImGui.ColorConvertU32ToFloat4(color));
                return;
            }

            var start = ImGui.GetCursorScreenPos();
            var height = Math.Max(ImGui.GetTextLineHeight(), size.Value.Y);
            var at = new Vector2((float)Math.Round(start.X + Math.Max(0, (width - size.Value.X) / 2)),
                (float)Math.Round(start.Y + (height - size.Value.Y) / 2));

            if (!icons.DrawLabel(ImGui.GetWindowDrawList(), text, at, pixels, color, outlined: false)) {
                Centred(text, width, ImGui.ColorConvertU32ToFloat4(color));
                return;
            }

            ImGui.Dummy(new Vector2(width, height));
        }

        /// <summary>Text centred in a line this wide.</summary>
        public static void Centred(string text, float width, Vector4 color) {
            var size = ImGui.CalcTextSize(text).X;
            var x = ImGui.GetCursorPosX();
            ImGui.SetCursorPosX(x + Math.Max(0, (width - size) / 2));
            Widgets.TextColored(color, text);

            // the text leaves the cursor at the line's start, which is where it began unless that was mid-line. Moving it
            // when it's already there would be a SetCursorPos with nothing after it, should this be the last thing in a
            // group or window, and ImGui stops the game with an assertion for that (the Vendor window's picked line).
            if (Math.Abs(ImGui.GetCursorPosX() - x) > 0.5f) {
                ImGui.SetCursorPosX(x);
            }
        }

        /// <summary>A label at the left and its value at the right of a line this wide, as the game's panels set them.</summary>
        public static void LeftRight(string label, string value, float width, Vector4 valueColor) {
            var labelWidth = ImGui.CalcTextSize(label).X;
            Widgets.Text(label);

            // placed by its distance from the label, not at an x: SameLine(x) inside a group adds the group's offset to
            // a cursor x that has it already, which pushed the value a group's width too far (the Vendor window's
            // Buying and Selling totals, beside Clear Item, then pushed the Buy and Sell buttons out of the window)
            var at = Math.Max(labelWidth + Widgets.Scaled(8), width - ImGui.CalcTextSize(value).X);
            ImGui.SameLine(0, at - labelWidth);
            Widgets.TextColored(valueColor, value);
        }
    }
}
