using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Config tab: settings profiles, whole sets of the plugin's settings that each character picks from, made,
    /// renamed and deleted here, and saved to or added from files to keep or share (Settings.Profiles.cs).
    /// </summary>
    internal class ConfigTab {
        /// <summary>How long a button's message stays under the buttons.</summary>
        private static readonly TimeSpan MessageFor = TimeSpan.FromSeconds(5);

        private readonly Settings settings;
        private readonly ClientUiOverride clientUiOverride;
        private readonly HideGameUi hideGameUi;
        private readonly Launcher launcher;
        private readonly BorderlessWindow borderless;

        private string profileName = "";
        /// <summary>The profile the name box was filled in for: another, swapped in anywhere, fills it again.</summary>
        private string profileNameFor;
        private string problem;
        private string message;
        private DateTime messageAt;
        private int deleteArmedFrame = -1;
        private int textPercent = 100;
        private bool textSliderActive;

        /// <summary>Other key... picked: the next key pressed is the one tooltips need held.</summary>
        private bool capturingTooltipKey;

        /// <summary>The modifier keys, which Other key... takes on their own too.</summary>
        private static readonly int[] Modifiers = { Keys.Alt, Keys.Control, Keys.Shift };

        /// <summary>Modifiers already held as Other key... began, not taken until let go and pressed again.</summary>
        private readonly HashSet<int> modifiersHeld = new HashSet<int>();

        /// <summary>The files From file found when it was pressed.</summary>
        private List<string> exportFiles = new List<string>();

        public ConfigTab(Settings settings, ClientUiOverride clientUiOverride, HideGameUi hideGameUi, Launcher launcher,
            BorderlessWindow borderless) {
            this.settings = settings;
            this.clientUiOverride = clientUiOverride;
            this.hideGameUi = hideGameUi;
            this.launcher = launcher;
            this.borderless = borderless;
        }

        private static string Me => CoreManager.Current.CharacterFilter.Name;

        public void Render() {
            Form.Section("Settings profile");
            Form.Hint("Everything the plugin keeps is in the profile in use: its windows and where they are, every tab's "
                + "options, your loot profiles and your hotbars. Each character picks its own profile, and one that "
                + "hasn't uses Default, so a few characters can share a set up and the rest keep theirs.");

            if (Form.BeginForm("config-profile")) {
                Form.Label("Profile", ProfileTip);
                RenderPicker();
                RenderButtons();

                Form.Label("Used by", "The characters that picked this profile. Any that haven't picked one use Default.");
                RenderUsedBy();

                Form.Label("Name", "This profile's name, which is its file's name too. Default can't be renamed.");
                RenderRename();

                Form.Label("Share", "Profiles as files, to keep a copy or give to another player.");
                RenderShareButtons();

                // in the table, where From file opened it: a table adds to the ID stack popups are found by
                RenderExportFiles();
                ImGui.EndTable();
            }

            if (problem != null) {
                Widgets.TextColored(Colors.Warning, problem);
            }
            else if (message != null && DateTime.UtcNow - messageAt < MessageFor) {
                Widgets.TextColored(Colors.Good, message);
            }

            ImGui.Spacing();
            RenderText();
            RenderTheme();
            RenderEscape();
            RenderSnapping();

            ImGui.Spacing();
            clientUiOverride.Render();
            borderless.Render();
            hideGameUi.Render();
            launcher.Render();
        }

        /// <summary>
        /// A tooltip shown whatever Tooltips only while holding says: the setting's own, or there'd be no telling what it
        /// does with it on.
        /// </summary>
        private static void AlwaysTip(string text) {
            if (!ImGui.IsItemHovered()) {
                return;
            }

            Widgets.BeginTooltip(always: true);
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28);
            ImGui.TextUnformatted(text);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
        }

        /// <summary>The key tooltips need held: Alt, Ctrl or Shift from the menu, or any key after Other key...</summary>
        private void RenderTooltipKey() {
            var data = settings.Data;
            var key = data.TooltipKey != 0 ? data.TooltipKey : Keys.Alt;
            ImGui.SetNextItemWidth(Widgets.Scaled(110));

            if (Combos.Begin("##config-tooltip-key", capturingTooltipKey ? "Press a key..." : TooltipKeyName(key))) {
                foreach (var choice in new[] { Keys.Alt, Keys.Control, Keys.Shift }) {
                    if (Combos.Item(TooltipKeyName(choice), key == choice)) {
                        data.TooltipKey = choice;
                        capturingTooltipKey = false;
                        settings.Changed();
                    }
                }

                if (Combos.Item("Other key...", capturingTooltipKey)) {
                    capturingTooltipKey = true;
                    modifiersHeld.Clear();

                    foreach (var modifier in Modifiers) {
                        if (Hotkeys.IsDown(modifier)) {
                            modifiersHeld.Add(modifier);
                        }
                    }
                }

                Combos.End();
            }

            AlwaysTip("The key to hold for tooltips. Ctrl also moves windows when held over them and Shift places them "
                + "freely, so Alt suits most. Other key... takes the next key you press; Escape cancels.");

            if (!capturingTooltipKey || !Hotkeys.GameInFront()) {
                return;
            }

            // Alt, Ctrl or Shift on its own: the key list Hotkeys.Pressed reads (the hotbars' keys) leaves them out, so
            // Other key... ignored them. One that goes down since the capture began is the key.
            foreach (var modifier in Modifiers) {
                var down = Hotkeys.IsDown(modifier);

                if (down && !modifiersHeld.Contains(modifier)) {
                    data.TooltipKey = modifier;
                    capturingTooltipKey = false;
                    settings.Changed();
                    return;
                }

                if (!down) {
                    modifiersHeld.Remove(modifier);
                }
            }

            foreach (var pressed in Hotkeys.Pressed()) {
                if (pressed != Keys.Escape) {
                    data.TooltipKey = pressed;
                    settings.Changed();
                }
                else {
                    EscapeCloses.Taken();
                }

                capturingTooltipKey = false;
                break;
            }
        }

        private static string TooltipKeyName(int key) {
            return key == Keys.Alt ? "Alt" : key == Keys.Control ? "Ctrl" : key == Keys.Shift ? "Shift" : Keys.Name(key);
        }

        private void RenderText() {
            Form.Section("Text");

            if (!Form.BeginForm("config-text")) {
                return;
            }

            Form.Label("Text size", "How big the plugin's text is drawn, in this window and every other one of its "
                + "windows. A window's own Size (its Settings, or right-click it) goes on top of this.");

            // the slider grows with the text, so a size is only used once it's let go: changing it while dragging would
            // move the slider out from under the mouse
            if (!textSliderActive) {
                textPercent = (int)Math.Round(settings.Data.ClampedTextScale * 100);
            }

            var style = ImGui.GetStyle();
            var reset = ImGui.CalcTextSize("Reset").X + style.FramePadding.X * 2 + style.ItemSpacing.X;
            ImGui.SetNextItemWidth(Math.Max(ImGui.GetFontSize() * 4, Math.Min(Form.ControlWidth,
                ImGui.GetContentRegionAvail().X - reset)));
            Sliders.SliderInt("##config-text-size", ref textPercent, (int)(SettingsData.MinTextScale * 100),
                (int)(SettingsData.MaxTextScale * 100), "%d%%");
            textSliderActive = ImGui.IsItemActive();

            if (ImGui.IsItemDeactivatedAfterEdit()) {
                SetTextScale(textPercent / 100f);
            }
            Widgets.Tip("How big the plugin's text is drawn. The new size is used once you let go. Ctrl+click to type one.");

            ImGui.SameLine();

            if (ImGui.SmallButton("Reset##config-text-size-reset")) {
                SetTextScale(1);
            }
            Widgets.Tip("Puts the text size back to 100%.");

            Form.Label("Tooltips", "When the boxes that show as you hover over something appear.");

            if (ImGui.Checkbox("Only while holding##config-tooltips-alt", ref settings.Data.TooltipsNeedAlt)) {
                settings.Changed();
            }

            AlwaysTip("Every tooltip of the plugin's (help on its controls, items' details) shows only while the key "
                + "beside this is held.");
            ImGui.SameLine();
            RenderTooltipKey();

            ImGui.EndTable();
        }

        private void RenderTheme() {
            Form.Section("Theme");

            if (!Form.BeginForm("config-theme")) {
                return;
            }

            Form.Label("Theme", "A ready-made look for this window and every other one of the plugin's windows: their "
                + "background, buttons, tabs and corners. Other plugins' windows keep their own.");

            var current = UiThemes.Find(settings.Data.Theme);
            ImGui.SetNextItemWidth(Form.ControlWidth);

            if (Combos.Begin("##config-theme", current.Name)) {
                foreach (var theme in UiThemes.All) {
                    if (Combos.Item($"{theme.Name}##config-theme-{theme.Name}", theme == current)) {
                        SetTheme(theme);
                    }
                    Widgets.Tip(theme.Tip);
                    DrawSwatches(theme);

                    if (theme == current) {
                        ImGui.SetItemDefaultFocus();
                    }
                }

                Combos.End();
            }
            Widgets.Tip($"{current.Tip} Pick another to see it at once.");

            ImGui.EndTable();
        }

        /// <summary>
        /// The theme's background, frame (or its button and selection colours, if it has them), accent and text, as little
        /// squares at the right of its line.
        /// </summary>
        private static void DrawSwatches(UiTheme theme) {
            if (theme.Unstyled) {
                return;
            }

            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            var size = (float)Math.Round(ImGui.GetFontSize() * 0.8f);
            var gap = Math.Max(2, (float)Math.Round(size / 4));
            var top = (float)Math.Round(min.Y + (max.Y - min.Y - size) / 2);
            var colors = theme.Button is Vector4 button && theme.Selection is Vector4 selection
                ? new[] { theme.Window, button, selection, theme.Accent, theme.Text }
                : new[] { theme.Window, theme.Frame, theme.Accent, theme.Text };
            var left = max.X - colors.Length * (size + gap);
            var drawList = ImGui.GetWindowDrawList();
            var edge = ImGui.GetColorU32(new Vector4(0.5f, 0.5f, 0.5f, 1));

            foreach (var color in colors) {
                var a = new Vector2(left, top);
                var b = new Vector2(left + size, top + size);
                drawList.AddRectFilled(a, b, ImGui.GetColorU32(color));
                drawList.AddRect(a, b, edge);
                left += size + gap;
            }
        }

        private void SetTheme(UiTheme theme) {
            if (settings.Data.Theme != theme.Name) {
                settings.Data.Theme = theme.Name;
                settings.Changed();
            }
        }

        private void RenderEscape() {
            Form.Section("Escape key");

            if (!Form.BeginForm("config-escape")) {
                return;
            }

            Form.Label("Windows", "What Escape does to this plugin's windows.");

            if (Form.Check("Escape closes windows", ref settings.Data.EscapeClosesWindows, "Escape closes this window and "
                + "the Inventory, Bank, Character and Quests windows (closing the Bank window closes the bank). The Target, "
                + "Fellowship, Loot, hotbar, map and other windows that follow what you're doing stay. Escape does only its "
                + "other job while you're typing in chat or a text box, have a menu open, are using a tool on something or "
                + "are setting a key. With none of them open and Options replacing the game's panel, Escape opens the "
                + "Options window at Gameplay Options, and pressed again there asks to exit the game. The game hears "
                + "Escape too.")) {
                settings.Changed();
            }

            ImGui.EndTable();
        }

        /// <summary>Window snapping (WindowSnap, FloatingWindow.SnapWhileDragged): edges, the grid and its drawing.</summary>
        private void RenderSnapping() {
            Form.Section("Window snapping");

            if (!Form.BeginForm("config-snapping")) {
                return;
            }

            var data = settings.Data;
            Form.Label("Snap", "How the plugin's windows line up as you drag them.");

            if (Form.Check("Snap windows", ref data.SnapWindows, "A window you drag snaps to the screen's edges and to the "
                + "edges of the plugin's other windows when one comes near, so they line up. Hold Shift while dragging "
                + "to place one freely.")) {
                settings.Changed();
            }

            if (Form.Check("Snap when resizing", ref data.SnapResize, "The edge of a window you resize (one you size "
                + "yourself) snaps the same way: to the screen's edges, the edges of the plugin's other windows beside it, "
                + "and the grid. Hold Shift to size one freely.")) {
                settings.Changed();
            }

            Form.Label("Snap distance", "How near, in pixels, an edge has to come before the window snaps to it.");

            if (Form.Slider("##config-snap-distance", ref data.SnapDistance, 2, 40, "%d px")) {
                settings.Changed();
            }

            Form.Label("Grid", "A grid the windows snap to as well, where no edge is near.");

            if (Form.Check("Snap to a grid", ref data.SnapGrid, "With no edge near, a window you drag snaps its top left "
                + "corner to the nearest point of a grid. Its middle snaps to the screen's middle, thirds and quarters "
                + "too.")) {
                settings.Changed();
            }

            if (Form.Check("Snap resizing to the grid", ref data.SnapResizeGrid, "With no edge near, the edge of a "
                + "window you resize, by its edge or by a bar's grip (the XP bar, Vitals, Power bar, Spell bar), snaps to "
                + "the grid's lines and to the screen's middle, thirds and quarters. Needs Snap when resizing.")) {
                settings.Changed();
            }

            if (Form.Check("Show the grid", ref data.SnapGridShown, "Draws the grid faintly over the screen while a window "
                + "is dragged, with thicker lines at the middle, thirds and quarters; the one a window is centred on "
                + "lights up.")) {
                settings.Changed();
            }

            Form.Label("Grid size", "How far apart the grid's lines are, in pixels.");

            if (Form.Slider("##config-snap-grid", ref data.SnapGridSize, 4, 100, "%d px")) {
                settings.Changed();
            }

            ImGui.EndTable();
        }

        private void SetTextScale(float scale) {
            scale = Math.Max(SettingsData.MinTextScale, Math.Min(SettingsData.MaxTextScale, scale));

            if (settings.Data.TextScale != scale) {
                settings.Data.TextScale = scale;
                settings.Changed();
            }
        }

        private string ProfileTip => $"The settings profile {Me} uses. Picking another swaps every setting at once, and "
            + $"{Me} uses it from now on, at every login.";

        private void RenderPicker() {
            ImGui.SetNextItemWidth(Form.ControlWidth);

            if (Combos.Begin("##config-profile", settings.Profile)) {
                foreach (var name in settings.Profiles()) {
                    if (Combos.Item($"{name}##profile-{name}", name == settings.Profile)) {
                        Pick(name);
                    }
                }

                Combos.End();
            }
            Widgets.Tip(ProfileTip);
        }

        /// <summary>New, Copy and Delete, after the picker, wrapping when the window is narrow.</summary>
        private void RenderButtons() {
            var first = false;

            if (Form.FlowSmallButton(ref first, "New", $"Makes a profile with every setting as it starts, and {Me} uses it. "
                + "The profile in use is kept as it is.")) {
                Added(settings.Add("My settings", copyCurrent: false, Me, out problem), "Made");
            }

            if (Form.FlowSmallButton(ref first, "Copy", $"Makes a copy of this profile, and {Me} uses it: to change "
                + "without changing it for the characters that use this one.")) {
                Added(settings.Add(settings.Profile, copyCurrent: true, Me, out problem), "Copied this profile to");
            }

            var isDefault = settings.Profile == Settings.DefaultProfile;
            ImGui.BeginDisabled(isDefault);
            var armed = Form.Armed(deleteArmedFrame);
            var clicked = Form.FlowSmallButton(ref first, armed ? "Sure?##delete-profile" : "Delete##delete-profile",
                "Deletes this profile and its file: click twice. The characters that used it go back to Default. "
                + "Default can't be deleted.");
            ImGui.EndDisabled();

            if (Form.SecondClick(ref deleteArmedFrame, clicked)) {
                var deleted = settings.Profile;

                if (settings.Delete(out problem)) {
                    ShowMessage($"Deleted \"{deleted}\". {Me} uses Default now.");
                }
            }
        }

        private void RenderUsedBy() {
            var characters = settings.CharactersUsing(settings.Profile);
            ImGui.AlignTextToFramePadding();

            if (settings.Profile == Settings.DefaultProfile) {
                Widgets.TextWrapped(characters.Count == 0
                    ? "Every character that hasn't picked another profile."
                    : $"{string.Join(", ", characters)}, and every character that hasn't picked another profile.");
            }
            else {
                Widgets.TextWrapped(characters.Count == 0 ? "No one." : string.Join(", ", characters));
            }
        }

        private void RenderRename() {
            var isDefault = settings.Profile == Settings.DefaultProfile;

            if (profileNameFor != settings.Profile) {
                profileNameFor = settings.Profile;
                profileName = settings.Profile;
            }

            ImGui.BeginDisabled(isDefault);
            ImGui.SetNextItemWidth(Form.ControlWidth);
            var entered = ImGui.InputText("##config-profile-name", ref profileName, 40,
                ImGuiInputTextFlags.EnterReturnsTrue);
            Widgets.Tip("Type a new name, then press Enter or Rename.");

            ImGui.SameLine();
            var clicked = ImGui.SmallButton("Rename");
            Widgets.Tip("Renames this profile and its file. The characters that use it keep using it.");
            ImGui.EndDisabled();

            if ((entered || clicked) && profileName.Trim().Length > 0) {
                var old = settings.Profile;

                if (settings.Rename(profileName, out problem) && old != settings.Profile) {
                    ShowMessage($"Renamed \"{old}\" to \"{settings.Profile}\".");
                }

                profileNameFor = null;
            }
        }

        /// <summary>To file and From file.</summary>
        private void RenderShareButtons() {
            var folder = settings.ExportFolder;
            var first = true;

            if (Form.FlowSmallButton(ref first, "To file", $"Saves this profile as a file, named after it, in {folder}, "
                + "to keep or give to someone. A file of the same name is replaced. The dungeon rooms you've been in are "
                + "left out: they aren't a setting.")) {
                try {
                    problem = null;
                    ShowMessage($"Saved \"{settings.Profile}\" to {settings.Export()}.");
                }
                catch (Exception ex) {
                    problem = $"Couldn't save the file: {ex.Message}";
                }
            }

            if (Form.FlowSmallButton(ref first, "From file", $"Adds a profile from a file in {folder} (saved with To "
                + $"file, or put there by you), named after the file, and {Me} uses it.")) {
                exportFiles = settings.ExportFiles();
                ImGui.OpenPopup("config-profile-files");
            }
        }

        /// <summary>From file's list: one click adds a profile from a file, and a button opens the folder.</summary>
        private void RenderExportFiles() {
            if (!ImGui.BeginPopup("config-profile-files")) {
                return;
            }

            var folder = settings.ExportFolder;

            if (exportFiles.Count == 0) {
                Widgets.TextDisabled("No profile files yet. Save one with To file, or put .json files in the folder.");
            }

            foreach (var path in exportFiles) {
                if (ImGui.Selectable($"{Path.GetFileNameWithoutExtension(path)}##{path}")) {
                    Added(settings.Import(path, Me, out problem), $"Added {Path.GetFileName(path)} as");
                }
                Widgets.Tip(path);
            }

            ImGui.Separator();

            if (ImGui.SmallButton("Open folder")) {
                try {
                    Directory.CreateDirectory(folder);
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
                }
                catch (Exception ex) {
                    problem = $"Couldn't open the folder: {ex.Message}";
                }
            }
            Widgets.Tip($"Opens {folder} in Explorer, to add or remove profile files.");

            ImGui.EndPopup();
        }

        private void Pick(string name) {
            problem = null;
            settings.Choose(Me, name);
            ShowMessage($"{Me} uses \"{settings.Profile}\" now.");
        }

        /// <summary>After a profile's been made: says so, or leaves the problem showing.</summary>
        private void Added(string name, string done) {
            if (name == null) {
                return;
            }

            ShowMessage($"{done} \"{name}\". {Me} uses it now.");
        }

        private void ShowMessage(string text) {
            message = text;
            messageAt = DateTime.UtcNow;
        }
    }
}
