using System;
using System.IO;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Chat tab: the chat windows on or off, Hide game chat, their windows, their font, times, unread tabs and
    /// highlights, colours and saved chat. Each window's own options (what it shows, who typing in it goes to, its text
    /// size) are in the window itself (ChatView.Options.cs), as the game keeps them in each chat window's options box.
    /// </summary>
    internal sealed class ChatTab {
        private readonly ChatWindows chat;
        private readonly Settings settings;
        private string fontFilter = "";
        private string problem;

        /// <summary>The kept lines while their slider is held: applied once let go.</summary>
        private int? draggingHistory;

        public ChatTab(ChatWindows chat, Settings settings) {
            this.chat = chat;
            this.settings = settings;
        }

        private ChatSettings Data => settings.Data.Chat;

        public const string HideGameChatTip = "Hides the game's own chat windows, the main one with its chat bar and the "
            + "floating ones, and turns on the plugin's chat windows to use instead: Enter starts typing in them, as in "
            + "the game. Untick it to have the game's back.";

        /// <summary>Hide game chat, as Client UI Override and this tab both have it: one setting.</summary>
        public static void RenderHideGameChat(ref bool first, Settings settings, string label = "Chat") {
            Form.Flow(ref first, Form.CheckWidth(label));
            var data = settings.Data.Chat;

            if (Widgets.GuardedCheckbox(label, ref data.HideGameChat, HideGameChatTip,
                "The game's own chat windows are hidden, and the plugin's Chat window used instead. Press Enter to type in "
                    + "it, as in the game. Its tabs and windows show what the game's options would: right-click a tab, or "
                    + "click the ... by the box you type in, for each one's options.\n\nThe game's chat comes back when "
                    + "this is unticked or the plugin is turned off.",
                "The game's chat windows are back. The plugin's chat windows stay too, until you untick Chat windows in "
                    + "the Chat tab.")) {
                // the windows have to be on to chat at all with the game's hidden
                if (data.HideGameChat) {
                    data.On = true;
                }

                settings.Changed();
            }
        }

        public void Render() {
            RenderWindows();
            Form.Columns("chat-columns", RenderText, RenderUnread);
            Form.Columns("chat-columns-2", RenderLinks, RenderSaving);
            RenderColors();
        }

        private void RenderWindows() {
            Form.Section("Chat windows");
            Form.Hint("Chat in windows of the plugin's own: a main window with a tab for each of four others (Tells, "
                + "Group, Channels and Combat to start), each showing what you pick of the game's chat, as the game's chat "
                + "windows do, and each able to pop out into a window of its own. Right-click a tab, or click the ... by "
                + "the box you type in, for its options. Hover a line to copy it; hover an item someone linked to see it.");

            var first = true;

            if (Form.FlowCheck(ref first, "Chat windows##chat-on", ref Data.On, "Shows the plugin's chat windows. They keep "
                + "the last of chat from before they were turned on, too.")) {
                settings.Changed();
            }

            RenderHideGameChat(ref first, settings, "Hide game chat");

            if (Form.FlowCheck(ref first, "Enter types here##chat-enter", ref Data.EnterTypes, "Enter (anything that opens "
                + "the game's chat bar) starts typing in the plugin's chat instead, even with the game's chat showing. "
                + "Always so with Hide game chat.")) {
                settings.Changed();
            }

            if (Form.FlowCheck(ref first, "Keep typing##chat-keep", ref Data.KeepTyping, "After you send a line, the box "
                + "stays ready for the next instead of handing the keys back to the game.")) {
                settings.Changed();
            }

            ImGui.Spacing();

            if (!ImGui.BeginTable("chat-windows", 4, Widgets.TableFlags)) {
                return;
            }

            ImGui.TableSetupColumn("Window", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Where");
            ImGui.TableSetupColumn("Shows");
            ImGui.TableSetupColumn("");
            Widgets.HeadersRow("Each chat window by its name.", "A tab of the main window, or a window of its own.",
                "The categories of the game's chat it shows.", null);

            for (var i = 0; i < ChatSettings.WindowCount; i++) {
                var window = Data.Window(i);
                ImGui.PushID($"chat-window-row-{i}");
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                Widgets.Text(window.Name);

                ImGui.TableNextColumn();

                if (i == 0) {
                    Widgets.TextDisabled("Main window");
                }
                else if (ImGui.SmallButton(window.PoppedOut ? "Own window" : "Tab")) {
                    chat.SetPoppedOut(i, !window.PoppedOut);
                }

                if (i > 0) {
                    Widgets.Tip(window.PoppedOut ? "In a window of its own: click to put it back as a tab of the main window."
                        : "A tab of the main window: click to pop it out into a window of its own.");
                }

                ImGui.TableNextColumn();
                var shows = window.Show == null || window.Show.Count == 0
                    ? "nothing"
                    : string.Join(", ", ChatCategoryInfo.All.Where(c => window.Shows(c.Category)).Select(c => c.Label));
                Widgets.TextDisabled(shows.Length > 60 ? shows.Substring(0, 57) + "..." : shows);
                Widgets.Tip(shows);

                ImGui.TableNextColumn();

                if (ImGui.SmallButton("Options...")) {
                    chat.ShowOptions(i);
                }
                Widgets.Tip("Opens its options in the window itself.");

                ImGui.PopID();
            }

            ImGui.EndTable();

            for (var i = 0; i < ChatSettings.WindowCount; i++) {
                if (i > 0 && !Data.Window(i).PoppedOut) {
                    continue;
                }

                Form.Section(i == 0 ? "Main chat window" : $"{Data.Window(i).Name} window");
                chat.Window(i).RenderControls(i == 0
                    ? "Shows the main chat window. Closed with Hide game chat on, Enter brings it back."
                    : "Shows this chat window, popped out of the main one.");
            }
        }

        private void RenderText() {
            Form.Section("Text");

            if (!Form.BeginForm("chat-text")) {
                return;
            }

            Form.Label("Font", "The chat windows' font. Each size of it is made sharp for the size it's drawn at, so pick "
                + "any: Verdana and Tahoma were made for small text on screens. A new font or size takes a moment to make.");
            RenderFontPicker();

            Form.Label("Shadow", "A dark copy of the text a pixel down and right, so it reads over the game.");

            if (Form.Check("Text shadow##chat-shadow", ref Data.TextShadow, "Good with a see-through background (a "
                + "window's Background slider, or its right-click menu).")) {
                settings.Changed();
            }

            Form.Label("Line spacing", "Pixels between rows of text.");

            if (Form.Slider("##chat-spacing", ref Data.LineSpacing, 0, 12, "%d px")) {
                settings.Changed();
            }

            Form.Label("Times", "Each line's time, in front of it.");

            if (Form.Check("##chat-times", ref Data.Timestamps, "Shows each line's time in front of it.")) {
                settings.Changed();
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 7);
            var formats = new[] {
                ("HH:mm", "14:05"), ("HH:mm:ss", "14:05:09"), ("h:mm tt", "2:05 PM"), ("h:mm:ss tt", "2:05:09 PM"),
            };

            if (Widgets.Choice("##chat-time-format", formats, ref Data.TimeFormat)) {
                settings.Changed();
            }
            Widgets.Tip("How the times are written.");

            Form.Label("Wrapped lines", "How a line too long for the window goes on.");

            if (Form.Check("Indent##chat-indent", ref Data.IndentWrapped, "Rows a long line wraps onto start in a little "
                + "(past its time, with times on), so where each line starts is easy to see.")) {
                settings.Changed();
            }

            Form.Label("Lines kept", "How many lines of chat are kept, every window together. The oldest go first.");
            var kept = draggingHistory ?? Data.ClampedHistory;
            ImGui.SetNextItemWidth(Form.ControlWidth);
            Sliders.SliderInt("##chat-history", ref kept, ChatSettings.MinHistory, ChatSettings.MaxHistory, "%d lines");

            if (ImGui.IsItemActive()) {
                draggingHistory = kept;
            }
            else if (draggingHistory.HasValue) {
                Data.History = kept;
                draggingHistory = null;
                settings.Changed();
            }

            ImGui.EndTable();
        }

        /// <summary>A combo of the fonts Windows and UtilityBelt have, with a box to find one by name.</summary>
        private void RenderFontPicker() {
            var ub = ChatFonts.UtilityBeltFont();
            var current = string.IsNullOrEmpty(Data.Font) ? $"UtilityBelt's ({ub ?? "its own"})" : Data.Font;
            ImGui.SetNextItemWidth(Form.ControlWidth);

            if (!Combos.Begin("##chat-font", current)) {
                return;
            }

            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 10);
            ImGui.InputTextWithHint("##chat-font-find", "Find...", ref fontFilter, 40);

            if (Combos.Item($"UtilityBelt's ({ub ?? "its own"})", string.IsNullOrEmpty(Data.Font))) {
                Data.Font = "";
                settings.Changed();
            }

            foreach (var family in ChatFonts.Families()) {
                if (fontFilter.Length > 0 && family.IndexOf(fontFilter, StringComparison.OrdinalIgnoreCase) < 0) {
                    continue;
                }

                if (Combos.Item(family, string.Equals(family, Data.Font, StringComparison.OrdinalIgnoreCase))) {
                    Data.Font = family;
                    settings.Changed();
                }
            }

            Combos.End();
        }

        private void RenderUnread() {
            Form.Section("Unread and highlights");

            if (!Form.BeginForm("chat-unread")) {
                return;
            }

            Form.Label("Unread tabs", "What a tab does while it has lines you haven't seen: it counts them either way.");

            if (Form.Check("Flash##chat-flash-all", ref Data.FlashUnread, "Tabs flash gold while they have lines you haven't "
                + "seen, red when one of them mentions you. Each window's options can turn its own off.")) {
                settings.Changed();
            }

            Form.Label("Your name", "Lines that mention your character light up.");

            if (Form.Check("Highlight##chat-highlight-name", ref Data.HighlightName, "Lights up lines with your character's "
                + "name in them (not your own tells).")) {
                settings.Changed();
            }

            Form.Label("Words", "More words that light a line up, comma-separated: whole words, case ignored.");
            ImGui.SetNextItemWidth(Form.ControlWidth);

            if (ImGui.InputTextWithHint("##chat-highlights", "word, another", ref Data.Highlights, 300)) {
                settings.Changed();
            }
            Widgets.Tip("A guild's name, an item you're after, a nickname. Lines from now on light up.");

            Form.Label("Colour", "What a lit-up line's background is drawn in.");
            var color = PackedColor.Unpack(Data.HighlightColor);
            var rgba = new Vector4(color.R, color.G, color.B, color.A);

            var flags = ImGuiColorEditFlags.NoInputs | ImGuiColorEditFlags.AlphaBar;

            if (ImGui.ColorEdit4("##chat-highlight-color", ref rgba, flags)) {
                Data.HighlightColor = PackedColor.Pack(rgba.X, rgba.Y, rgba.Z, rgba.W);
                settings.Changed();
            }

            ImGui.EndTable();
        }

        private void RenderLinks() {
            Form.Section("Names and links");

            if (!Form.BeginForm("chat-links")) {
                return;
            }

            Form.Label("Names", "What clicking a name in chat does.");

            if (Form.Check("Click for a tell##chat-click-names", ref Data.ClickNames, "Clicking a name starts a tell to them "
                + "in the box, as the game's chat does. Right-click a name for more: select them, invite them, add them as "
                + "a friend.")) {
                settings.Changed();
            }

            Form.Label("Items", "Linking items in chat.");
            Widgets.TextWrapped(chat.Feed.Supports(PlayerFeed.KindShareItem)
                ? "Right-click an item in your packs, the bank or a hotbar for Link in chat."
                : "This server is too old for item links.");

            ImGui.EndTable();
        }

        private void RenderSaving() {
            Form.Section("Saved chat");
            var first = true;

            if (Form.FlowCheck(ref first, "Save to files##chat-save", ref Data.Save, "Writes chat as it comes to a text file "
                + $"for the character and day, in {chat.SaveFolder}, whether the chat windows are on or not. Nothing is "
                + "ever deleted: the files are yours to clear out.")) {
                settings.Changed();
            }

            if (Form.FlowSmallButton(ref first, "Open folder##chat-folder", $"Opens {chat.SaveFolder} in Explorer.")) {
                try {
                    problem = null;
                    Directory.CreateDirectory(chat.SaveFolder);
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{chat.SaveFolder}\"");
                }
                catch (Exception ex) {
                    problem = $"Couldn't open the folder: {ex.Message}";
                }
            }

            if (Form.FlowSmallButton(ref first, "Clear every window##chat-clear-all", "Forgets every line kept, in every "
                + "chat window. Saved files aren't touched.")) {
                chat.ClearAll();
            }

            if (problem != null) {
                Widgets.TextColored(Colors.Warning, problem);
            }
        }

        /// <summary>A colour for each of the game's chat types, by category, and one for each chat channel.</summary>
        private void RenderColors() {
            Form.Section("Colours");
            Form.Hint("Each kind of line's colour, near the game's own to start. Channels have one each.");

            if (!ImGui.BeginTable("chat-colors", 2, Widgets.TableFlags)) {
                return;
            }

            ImGui.TableSetupColumn("Kind", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Colour");

            foreach (var category in ChatCategoryInfo.All) {
                if (category.Channel) {
                    ColorRow(category.Label, category.Key, category.Color, category.Tip);
                    continue;
                }

                foreach (var type in ChatTypeInfo.All.Where(t => t.Category == category.Category)) {
                    ColorRow($"{category.Label}: {type.Label}", type.Key, type.Color, category.Tip);
                }
            }

            ImGui.EndTable();
        }

        private void ColorRow(string label, string key, uint start, string tip) {
            ImGui.PushID(key);
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            var color = chat.ColorFor(key, start);
            var unpacked = PackedColor.Unpack(color);
            Widgets.TextColored(new Vector4(unpacked.R, unpacked.G, unpacked.B, 1), label);
            Widgets.Tip(tip);

            ImGui.TableNextColumn();
            var rgb = new Vector3(unpacked.R, unpacked.G, unpacked.B);

            if (ImGui.ColorEdit3("##color", ref rgb, ImGuiColorEditFlags.NoInputs)) {
                Data.Colors = Data.Colors ?? new System.Collections.Generic.Dictionary<string, uint>();
                Data.Colors[key] = PackedColor.Pack(rgb.X, rgb.Y, rgb.Z, 1);
                settings.Changed();
            }

            if (Data.Colors?.ContainsKey(key) == true) {
                ImGui.SameLine();

                if (ImGui.SmallButton("Reset")) {
                    Data.Colors.Remove(key);
                    settings.Changed();
                }
                Widgets.Tip("Back to the colour it started with.");
            }

            ImGui.PopID();
        }
    }
}
