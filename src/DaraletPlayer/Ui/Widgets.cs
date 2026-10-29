using System;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Pieces several tabs draw the same way: tooltips, tables, combo boxes, vital bars, durations.
    /// </summary>
    internal static class Widgets {
        public const float BarWidth = 110;

        /// <summary>
        /// The Config tab's Text size, as the window being drawn applied it (PlayerHud, FloatingWindow), for SquareFontSize.
        /// </summary>
        public static float TextScale = 1;

        /// <summary>Whether vital bars are drawn in the game's own art (SettingsData.RetailVitalBars), set by PlayerHud.</summary>
        public static Func<bool> RetailBars = () => false;

        /// <summary>Whether vital bars' numbers are drawn bold (SettingsData.BoldVitalText), set by PlayerHud.</summary>
        public static Func<bool> BoldVitals = () => false;

        /// <summary>How far the second copy of bold text is drawn from the first.</summary>
        private static readonly Vector2 BoldShift = new Vector2(1, 0);

        // the numbers on a Retail bar: white, with a black outline (packed by hand: ImGui has no context yet here)
        private const uint BarText = 0xFFFFFFFF;
        private const uint BarTextOutline = 0xFF000000;

        /// <summary>The Retail bars checkbox's tooltip, wherever it is (the fellowship and target windows' settings).</summary>
        public const string BoldVitalsTip = "Draws the numbers on health, stamina and mana bars bold, so they stand out "
            + "better, in the fellowship and target windows and the Fellowship tab. One setting for all of them.";

        public const string RetailBarsTip = "Draws health, stamina and mana bars with the game's own vitals art (its "
            + "bars and their glass caps) instead of plain filled bars, in the fellowship and target windows and the "
            + "Fellowship tab. One setting for all of them.";

        /// <summary>How every list in the plugin is drawn: striped rows, column lines, columns as wide as what's in them.</summary>
        public const ImGuiTableFlags TableFlags =
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingFixedFit;

        // ImGui.NET's Text, TextColored, TextDisabled and TextWrapped take a printf format, so a "%" in what they draw is
        // read as a format: "+12%" loses its "%", and a name with "%s" in it reads memory that isn't there. These draw
        // the text as it is; the plugin draws all its text with them.

        public static void Text(string text) {
            ImGui.TextUnformatted(text ?? "");
        }

        public static void TextColored(Vector4 color, string text) {
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextUnformatted(text ?? "");
            ImGui.PopStyleColor();
        }

        public static void TextDisabled(string text) {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
            ImGui.TextUnformatted(text ?? "");
            ImGui.PopStyleColor();
        }

        /// <summary>Text that wraps at the window's edge.</summary>
        public static void TextWrapped(string text) {
            ImGui.PushTextWrapPos(0);
            ImGui.TextUnformatted(text ?? "");
            ImGui.PopTextWrapPos();
        }

        /// <summary>A button with a tooltip, greyed out (its tooltip still showing) while it can't be used.</summary>
        public static bool Button(string label, bool enabled, string tip) {
            ImGui.BeginDisabled(!enabled);
            var clicked = ImGui.Button(label);
            ImGui.EndDisabled();
            Tip(tip);
            return clicked;
        }

        /// <summary>Shows text in a hover tooltip for the item drawn just before this, greyed-out ones included.</summary>
        public static void Tip(string text) {
            if (string.IsNullOrEmpty(text) || !ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
                return;
            }

            BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        /// <summary>
        /// ImGui.BeginTooltip, with the text as big as the window it's shown from: a tooltip is a window of its own,
        /// which ImGui draws at the font's own size, whatever the Text size or the window's size. extraScale goes on top (the
        /// Loot tab's Item details size, for item tooltips).
        /// </summary>
        public static void BeginTooltip(float extraScale = 1) {
            var font = ImGui.GetFont();
            var scale = ImGui.GetFontSize() / (font.FontSize * font.Scale * ImGui.GetIO().FontGlobalScale) * extraScale;
            ImGui.BeginTooltip();
            ImGui.SetWindowFontScale(scale);
        }

        /// <summary>
        /// The window's font size without the Text size, for text drawn in a square that doesn't grow with it (an item
        /// icon's stack count, a hotbar slot's labels): at a bigger Text size it would spill out of the square. A
        /// window's own Size still counts, since it grows the squares too.
        /// </summary>
        public static float SquareFontSize() => ImGui.GetFontSize() / TextScale;

        /// <summary>CalcTextSize at SquareFontSize.</summary>
        public static Vector2 SquareTextSize(string text) => ImGui.CalcTextSize(text) / TextScale;

        /// <summary>
        /// A double-click as Windows (and so the game) counts them: every second click of a quick run, the 2nd, 4th, 6th.
        /// ImGui's IsMouseDoubleClicked is only the 2nd, its count going on up while the clicks keep coming, so clicking
        /// away at the top of a pack to put on one item after another put on only the first.
        /// </summary>
        public static bool DoubleClicked() {
            var count = ImGui.GetMouseClickedCount(ImGuiMouseButton.Left);
            return ImGui.IsMouseClicked(ImGuiMouseButton.Left) && count >= 2 && count % 2 == 0;
        }

        /// <summary>A small (?) on the same line, explaining what's next to it when hovered.</summary>
        public static void HelpMarker(string text) {
            ImGui.SameLine();
            ImGui.TextDisabled("(?)");
            Tip(text);
        }

        /// <summary>
        /// A table's header row with a tooltip on each header. Call after TableSetupColumn, in place of TableHeadersRow;
        /// a null tip leaves that header without one.
        /// </summary>
        public static void HeadersRow(params string[] tips) {
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers);

            for (var column = 0; column < ImGui.TableGetColumnCount(); column++) {
                if (!ImGui.TableSetColumnIndex(column)) {
                    continue;
                }

                ImGui.TableHeader(ImGui.TableGetColumnName(column));

                if (column < tips.Length && tips[column] != null) {
                    Tip(tips[column]);
                }
            }
        }

        /// <summary>
        /// A checkbox for a setting that changes what the game itself shows: ticking it asks first (confirmOn, with Turn on
        /// and Cancel), and unticking it says what follows (noticeOff, with OK). The popups are opened and drawn here, in the
        /// same ID scope as the checkbox, which is where ImGui looks for them. Returns true when the value changed.
        /// </summary>
        public static bool GuardedCheckbox(string label, ref bool value, string tip, string confirmOn, string noticeOff) {
            var changed = false;
            var ticked = value;
            var confirmId = $"Turn on {label}?##confirm-{label}";
            var noticeId = $"{label} is off##notice-{label}";

            if (ImGui.Checkbox(label, ref ticked)) {
                if (ticked) {
                    // not on until it's confirmed
                    ImGui.OpenPopup(confirmId);
                }
                else {
                    value = false;
                    changed = true;
                    ImGui.OpenPopup(noticeId);
                }
            }
            Tip(tip);

            var open = true;

            if (ImGui.BeginPopupModal(confirmId, ref open, ImGuiWindowFlags.AlwaysAutoResize)) {
                PopupText(confirmOn);

                if (ImGui.Button("Turn on")) {
                    value = true;
                    changed = true;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();

                if (ImGui.Button("Cancel")) {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            open = true;

            if (ImGui.BeginPopupModal(noticeId, ref open, ImGuiWindowFlags.AlwaysAutoResize)) {
                PopupText(noticeOff);

                if (ImGui.Button("OK")) {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }

            return changed;
        }

        /// <summary>A popup's text, wrapped to a readable width, with a little room under it.</summary>
        public static void PopupText(string text) {
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 30);
            ImGui.TextUnformatted(text ?? "");
            ImGui.PopTextWrapPos();
            ImGui.Spacing();
        }

        /// <summary>A tab in a tab bar with a tooltip. Returns true while it's the open tab; call EndTabItem then.</summary>
        public static bool TabItem(string label, string tip) {
            var open = ImGui.BeginTabItem(label);
            Tip(tip);
            return open;
        }

        /// <summary>
        /// The same, made the open tab when select is true. ImGui.NET only takes flags with a close button's bool, so this
        /// calls ImGui itself, with no bool and so no close button.
        /// </summary>
        public static unsafe bool TabItem(string label, string tip, bool select) {
            var bytes = System.Text.Encoding.UTF8.GetBytes(label + "\0");
            var flags = select ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            bool open;

            fixed (byte* text = bytes) {
                open = ImGuiNative.igBeginTabItem(text, null, flags) != 0;
            }

            Tip(tip);
            return open;
        }

        /// <summary>
        /// A combo box over (value, label) choices, showing the chosen one's label (the first one's, when the value isn't one
        /// of them). Returns true when the choice changed.
        /// </summary>
        public static bool Choice<T>(string id, (T Value, string Label)[] options, ref T value) {
            var current = value;
            var label = options.FirstOrDefault(o => Equals(o.Value, current)).Label ?? options[0].Label;
            var changed = false;

            if (ImGui.BeginCombo(id, label)) {
                foreach (var option in options) {
                    var chosen = Equals(option.Value, current);

                    if (ImGui.Selectable(option.Label, chosen)) {
                        changed = !chosen;
                        value = option.Value;
                    }
                }

                ImGui.EndCombo();
            }

            return changed;
        }

        /// <summary>
        /// A combo box of tick boxes over (value, label) choices, the ticked values kept comma-separated in the options' order,
        /// showing "Any" with none ticked. Returns true when the ticks changed.
        /// </summary>
        public static bool MultiChoice(string id, (string Value, string Label)[] options, ref string value) {
            var ticked = (value ?? "").Split(',').Select(v => v.Trim()).Where(v => v.Length > 0).ToList();
            var labels = options.Where(o => ticked.Contains(o.Value)).Select(o => o.Label).ToList();
            var changed = false;

            if (ImGui.BeginCombo(id, labels.Count == 0 ? "Any" : string.Join(", ", labels))) {
                foreach (var option in options) {
                    var on = ticked.Contains(option.Value);

                    if (ImGui.Checkbox($"{option.Label}##{option.Value}", ref on)) {
                        ticked.RemoveAll(v => v == option.Value);

                        if (on) {
                            ticked.Add(option.Value);
                        }

                        changed = true;
                    }
                }

                if (ticked.Count > 0) {
                    ImGui.Separator();

                    if (ImGui.Selectable("Clear", false, ImGuiSelectableFlags.DontClosePopups)) {
                        ticked.Clear();
                        changed = true;
                    }
                }

                ImGui.EndCombo();
            }

            if (changed) {
                value = string.Join(",", options.Where(o => ticked.Contains(o.Value)).Select(o => o.Value));
            }

            return changed;
        }

        /// <summary>
        /// A width for vital bars that fits "current / max" for the biggest of these maximums in the font as drawn now, or
        /// the given width when that's wider: bars that line up, with their numbers inside them at any font size.
        /// </summary>
        public static float VitalBarWidth(float width, params uint[] maximums) {
            var widest = maximums.Length == 0 ? 0 : maximums.Max();
            return Math.Max(width, ImGui.CalcTextSize($"{widest} / {widest}").X + ImGui.GetStyle().FramePadding.X * 2 + 4);
        }

        /// <summary>Outlines the item drawn just before this while the mouse is over it: a sign it can be clicked.</summary>
        public static void OutlineIfHovered() {
            if (ImGui.IsItemHovered()) {
                var color = ImGui.GetColorU32(ImGuiCol.ButtonHovered);
                ImGui.GetWindowDrawList().AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), color);
            }
        }

        /// <summary>A vital as a filled bar with "current / max" on it. A height of 0 is the normal line height.</summary>
        public static void VitalBar(uint current, uint max, Vector4 color, float width = BarWidth, float height = 0) {
            var fraction = max == 0 ? 0f : Math.Min(1f, (float)current / max);

            Bar(fraction, color, $"{current} / {max}", width, height);
        }

        /// <summary>
        /// The bar in the game's own art (ItemIcons.DrawVitalBar), with its numbers outlined on top so they read over the
        /// bright fill. False when the art can't be drawn, for the plain bar instead.
        /// </summary>
        private static bool RetailBar(string vital, float fraction, string text, float width, float height) {
            height = Math.Max(height > 0 ? height : ImGui.GetFrameHeight(), ImGui.GetTextLineHeight());
            var min = ImGui.GetCursorScreenPos();
            var max = min + new Vector2(width, height);
            var drawList = ImGui.GetWindowDrawList();

            if (!ItemIcons.DrawVitalBar(drawList, vital, min, max, fraction)) {
                return false;
            }

            var at = BarTextAt(text, min, width, height);
            var bold = BoldVitals();

            foreach (var offset in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1) }) {
                drawList.AddText(at + offset, BarTextOutline, text);

                if (bold) {
                    drawList.AddText(at + offset + BoldShift, BarTextOutline, text);
                }
            }

            DrawBarText(drawList, at, BarText, text, bold);
            ImGui.Dummy(new Vector2(width, height));
            return true;
        }

        /// <summary>Where a bar's text goes: in its middle, on whole pixels.</summary>
        private static Vector2 BarTextAt(string text, Vector2 min, float width, float height) {
            var size = ImGui.CalcTextSize(text);
            return new Vector2((float)Math.Round(min.X + (width - size.X) / 2), (float)Math.Round(min.Y + (height - size.Y) / 2));
        }

        /// <summary>A bar's text, bold drawn twice a pixel apart: ImGui has one font, with no bold of its own.</summary>
        private static void DrawBarText(ImDrawListPtr drawList, Vector2 at, uint color, string text, bool bold) {
            drawList.AddText(at, color, text);

            if (bold) {
                drawList.AddText(at + BoldShift, color, text);
            }
        }

        /// <summary>
        /// A filled bar with text on it. In a vital's colour (Colors.Health, Stamina, Mana) with Retail bars on, it's drawn in
        /// the game's own art.
        /// </summary>
        public static void Bar(float fraction, Vector4 color, string text, float width = BarWidth, float height = 0) {
            // never shorter than its text: a bigger font (the game's resolution, a font size setting) than the bar's height
            // was chosen for wrote each bar's numbers over the next
            if (height > 0) {
                height = Math.Max(height, ImGui.GetTextLineHeight());
            }

            var vital = color == Colors.Health ? "health" : color == Colors.Stamina ? "stamina" : color == Colors.Mana ? "mana"
                : null;

            if (vital != null && RetailBars() && RetailBar(vital, fraction, text, width, height)) {
                return;
            }

            // bold numbers are drawn here, in the middle, over a bar with none of its own
            var bold = vital != null && BoldVitals();

            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, color);
            ImGui.ProgressBar(fraction, new Vector2(width, height), bold ? "" : text);
            ImGui.PopStyleColor();

            if (bold) {
                var min = ImGui.GetItemRectMin();
                var box = ImGui.GetItemRectSize();
                DrawBarText(ImGui.GetWindowDrawList(), BarTextAt(text, min, box.X, box.Y), ImGui.GetColorU32(ImGuiCol.Text),
                    text, true);
            }
        }

        /// <summary>45s, 12m 05s, 3h 07m, 2d 04h.</summary>
        public static string Duration(double seconds) {
            var time = TimeSpan.FromSeconds(Math.Max(0, seconds));

            if (time.TotalMinutes < 1) {
                return $"{time.Seconds}s";
            }
            if (time.TotalHours < 1) {
                return $"{time.Minutes}m {time.Seconds:00}s";
            }
            if (time.TotalDays < 1) {
                return $"{(int)time.TotalHours}h {time.Minutes:00}m";
            }
            return $"{(int)time.TotalDays}d {time.Hours:00}h";
        }
    }
}
