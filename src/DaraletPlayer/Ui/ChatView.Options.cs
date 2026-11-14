using System;
using System.Linq;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A chat window's options, as the game's chat options box has them: what it shows (the game's categories), who typing
    /// in it goes to, and the window's own bits: its name, words that keep a line out, its text size, its box, its tab's
    /// flashing, and popping it out. Opened from the button by its box, its tab's right-click menu, or its own.
    /// </summary>
    internal sealed partial class ChatView {
        private bool openOptions;
        private bool searching;

        /// <summary>The text size while its slider is held: applied once let go.</summary>
        private int? draggingSize;
        private string search = "";

        /// <summary>Opens the options the next time it's drawn, at its own level, where they're looked for.</summary>
        public void OpenOptions() {
            openOptions = true;
        }

        private void RenderSearch() {
            ImGui.SetNextItemWidth(Math.Max(60, ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("Done").X
                - ImGui.GetStyle().FramePadding.X * 2 - ImGui.GetStyle().ItemSpacing.X));
            var before = search;

            if (ImGui.InputTextWithHint("##chat-search", "Find in this window...", ref search, 60)) {
                search = search ?? "";

                if (search.Trim() != before.Trim()) {
                    Refilter();
                }
            }
            Widgets.Tip("Shows only lines with these words in them, a name or an item, say.");

            ImGui.SameLine();

            if (ImGui.SmallButton("Done##chat-search-done")) {
                searching = false;
                search = "";
                Refilter();
            }
        }

        private void RenderOptions() {
            if (!ImGui.BeginPopup("chat-options")) {
                return;
            }

            try {
                RenderOptionsContent();
            }
            finally {
                ImGui.EndPopup();
            }
        }

        private void RenderOptionsContent() {
            var window = Window;
            var settings = owner.Settings;

            Form.Section($"{window.Name}: what it shows");
            RenderCategories(window);

            Form.Section("Typing");
            RenderTalk(window);

            if (!Form.BeginForm($"chat-options-form-{index}")) {
                return;
            }

            Form.Label("Name", "The window's name, on its tab or title bar.");
            ImGui.SetNextItemWidth(Form.ControlWidth);
            var name = window.Name ?? "";

            if (ImGui.InputText("##chat-name", ref name, 24)) {
                window.Name = name.Trim().Length > 0 ? name : window.Name;
                settings.Changed();
            }

            Form.Label("Hide lines with", "Lines with any of these words in them aren't shown here, comma-separated: "
                + "\"spam, WTS\". Whole words, case ignored.");
            ImGui.SetNextItemWidth(Form.ControlWidth);
            var hide = window.HideWords ?? "";

            if (ImGui.InputTextWithHint("##chat-hide", "word, another", ref hide, 200)) {
                window.HideWords = hide;
                settings.Changed();
                Refilter();
            }

            Form.Label("Text size", "How tall the window's text is, in pixels, at its normal Size. Each size is a font of its "
                + "own, drawn sharp. Its right-click menu's Bigger and Smaller change it too.");

            if (index == 0 || window.PoppedOut) {
                var size = draggingSize ?? window.ClampedFontSize;
                ImGui.SetNextItemWidth(Form.ControlWidth);
                Sliders.SliderInt("##chat-font-size", ref size, ChatWindowSettings.MinFontSize, ChatWindowSettings.MaxFontSize,
                    "%d px");

                // applied once let go: each size is a font of its own, and every step of a drag would make one
                if (ImGui.IsItemActive()) {
                    draggingSize = size;
                }
                else if (draggingSize.HasValue || size != window.ClampedFontSize) {
                    window.FontSize = size;
                    draggingSize = null;
                    settings.Changed();
                }
            }
            else {
                Widgets.TextDisabled("The main window's, as a tab of it.");
            }

            Form.Label("Shows", "What else the window has.");
            var first = true;

            if (Form.FlowCheck(ref first, "Typing box##chat-input-on", ref window.Input, "A box at the bottom to type "
                + "in. Without it, the window only shows chat.")) {
                settings.Changed();
            }

            if (index > 0 && Form.FlowCheck(ref first, "Flash its tab##chat-flash", ref window.Flash, "Its tab in the main "
                + "window flashes while it has lines you haven't seen (with Flash unread tabs on, in the Chat tab).")) {
                settings.Changed();
            }

            ImGui.EndTable();
            ImGui.Spacing();
            first = true;

            if (index > 0) {
                var label = window.PoppedOut ? "Put back as a tab" : "Pop out";

                if (Form.FlowButton(ref first, $"{label}##chat-pop", window.PoppedOut
                    ? "Puts it back in the main window, as a tab."
                    : "Into a window of its own, which you can put anywhere and size as you like.")) {
                    owner.SetPoppedOut(index, !window.PoppedOut);
                    ImGui.CloseCurrentPopup();
                }
            }

            if (Form.FlowButton(ref first, "Find...##chat-find", "Shows only lines with words you type, in this window.")) {
                searching = true;
                ImGui.CloseCurrentPopup();
            }

            if (Form.FlowButton(ref first, "Clear##chat-clear", "Empties this window. The others keep their lines.")) {
                Clear();
                ImGui.CloseCurrentPopup();
            }
        }

        /// <summary>A checkbox for each of the game's chat categories, and All and None.</summary>
        private void RenderCategories(ChatWindowSettings window) {
            var first = true;
            var changed = false;

            foreach (var info in ChatCategoryInfo.All) {
                // the game always shows these in its main window
                var mandatory = index == 0 && info.Category == ChatCategory.Alerts;
                var on = mandatory || window.Shows(info.Category);
                ImGui.BeginDisabled(mandatory);

                if (Form.FlowCheck(ref first, $"{info.Label}##chat-show-{info.Key}", ref on, info.Tip)) {
                    window.SetShown(info.Category, on);
                    changed = true;
                }

                ImGui.EndDisabled();
            }

            first = true;

            if (Form.FlowSmallButton(ref first, "All##chat-show-all", "Ticks every category.")) {
                window.Show = ChatCategoryInfo.All.Select(c => c.Key).ToList();
                changed = true;
            }

            if (Form.FlowSmallButton(ref first, "None##chat-show-none", "Unticks every category.")) {
                window.Show.Clear();
                changed = true;
            }

            if (Form.FlowSmallButton(ref first, "As it started##chat-show-default", "What this window showed to start.")) {
                window.Show = ChatSettings.DefaultWindow(index).Show;
                changed = true;
            }

            if (changed) {
                owner.Settings.Changed();
                Refilter();
            }
        }

        private void RenderTalk(ChatWindowSettings window) {
            if (!Form.BeginForm($"chat-talk-form-{index}")) {
                return;
            }

            Form.Label("Talk to", "Who what you type in this window goes to, as the game's chat options box picks it. A "
                + "line starting with / or @ is a command, sent as typed.");
            ImGui.SetNextItemWidth(Form.ControlWidth);
            var options = ChatTalk.All.Select(t => (t.Key, t.Label)).ToArray();

            if (Widgets.Choice("##chat-talk-choice", options, ref window.Talk)) {
                owner.Settings.Changed();
            }
            Widgets.Tip(ChatTalk.For(window.Talk).Tip);

            if (window.Talk == ChatTalk.Custom) {
                Form.Label("In front", "What goes in front of each line you type.");
                ImGui.SetNextItemWidth(Form.ControlWidth);

                if (ImGui.InputTextWithHint("##chat-prefix-option", "/e ", ref window.CustomPrefix, 60)) {
                    owner.Settings.Changed();
                }
            }

            ImGui.EndTable();
        }

        /// <summary>The window's own items, at the end of a line's right-click menu.</summary>
        private void WindowMenuItems() {
            if (ImGui.MenuItem("Options...")) {
                openOptions = true;
            }

            if (ImGui.MenuItem("Find...")) {
                searching = true;
            }

            if (ImGui.MenuItem("Clear")) {
                Clear();
            }

            if (index > 0) {
                var poppedOut = Window.PoppedOut;

                if (ImGui.MenuItem(poppedOut ? "Put back as a tab" : "Pop out")) {
                    owner.SetPoppedOut(index, !poppedOut);
                }
            }

            if (host != null) {
                ImGui.Separator();
                host.RenderMenuItems();
            }
        }
    }
}
