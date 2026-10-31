using System;
using System.IO;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Combat tab's Combat log: the settings for floating combat text, the Combat log window and the saved logs, with a
    /// row for each kind of combat line saying where it goes, and the Combat log window itself (`combat-log`, hidden to
    /// start). Every choice is in the settings profile.
    /// </summary>
    internal sealed class CombatLogTab : IDisposable {
        /// <summary>The most lines drawn at once, the newest: each is wrapped text, so they can't be skipped cheaply.</summary>
        private const int Drawn = 300;

        private readonly Settings settings;
        private readonly CombatWatch watch;
        private readonly CombatText text;
        private readonly FloatingWindow window;

        private string search = "";
        private bool follow = true;
        private int lastCount;
        private string problem;

        public CombatLogTab(Settings settings, ItemIcons icons) {
            this.settings = settings;
            watch = new CombatWatch(settings);
            text = new CombatText(settings, icons, watch);
            window = new FloatingWindow("combat-log", "Combat log", settings, new Vector2(20, 300), RenderLog,
                visibleByDefault: false, defaultSize: new Vector2(460, 260));
        }

        public void Render() {
            RenderFloating();
            RenderKinds();
            RenderWindow();
            RenderSaving();
        }

        private void RenderFloating() {
            Form.Section("Floating combat text");
            Form.Hint("Numbers that rise and fade as you fight: over whoever they landed on, or from one place on screen for "
                + "what you do and another for what's done to you.");

            if (!Form.BeginForm("combat-text")) {
                return;
            }

            var data = settings.Data;
            Form.Label("Show", "Whether floating combat text is drawn.");

            if (Form.Check("Enabled##combat-text-on", ref data.CombatTextOn, "Shows floating combat text. The table below "
                + "says which kinds. It's read from chat, so kinds you squelch in the game don't show.")) {
                settings.Changed();
            }

            ImGui.SameLine();

            if (Form.LitButton("Preview##combat-text-preview", text.Preview)) {
                text.Preview = !text.Preview;
            }
            Widgets.Tip("Keeps sample numbers rising, to see their size and place while you change these. Click again to stop.");

            Form.Label("Size", "How tall the text is, in pixels. Criticals, kills and deaths are bigger.");
            Changed(Form.Slider("##combat-text-size", ref data.CombatTextSize, SettingsData.MinCombatTextSize,
                SettingsData.MaxCombatTextSize, "%d px"));

            Form.Label("Time", "How long each number stays on screen, in tenths of a second.");
            Changed(Form.Slider("##combat-text-time", ref data.CombatTextTenths, SettingsData.MinCombatTextTenths,
                SettingsData.MaxCombatTextTenths, "%d tenths"));

            Form.Label("Over target", "Whether numbers float over whoever they landed on.");

            if (Form.Check("Over their head##combat-text-over", ref data.CombatTextOverHead, "Your hits float over the creature "
                + "you hit, and what's done to you over your own head. Chat only gives a name, so among several of that name "
                + "it's the one you hit last, then your selection, then the nearest. Anything it can't place uses the places "
                + "on screen below. They're drawn on top of everything, so a wall doesn't hide them.")) {
                settings.Changed();
            }

            if (data.CombatTextOverHead) {
                Form.Label("Height", "How far above a person's height the numbers begin, in tenths of a meter. The game "
                    + "doesn't say how tall a creature is, so this is one height for all of them.");
                Changed(Form.Slider("##combat-text-head-height", ref data.CombatTextHeadHeight, -20, 40, "%d tenths"));
            }

            Form.Label("You: across", "Where what you do starts, as a percent of the way across the screen.");
            Changed(Form.Slider("##combat-text-yours-x", ref data.CombatTextYoursX, 0, 100, "%d%%"));
            Form.Label("You: down", "Where what you do starts, as a percent of the way down the screen.");
            Changed(Form.Slider("##combat-text-yours-y", ref data.CombatTextYoursY, 0, 100, "%d%%"));
            Form.Label("On you: across", "Where what's done to you starts, as a percent of the way across the screen.");
            Changed(Form.Slider("##combat-text-theirs-x", ref data.CombatTextTheirsX, 0, 100, "%d%%"));
            Form.Label("On you: down", "Where what's done to you starts, as a percent of the way down the screen.");
            Changed(Form.Slider("##combat-text-theirs-y", ref data.CombatTextTheirsY, 0, 100, "%d%%"));

            ImGui.EndTable();
        }

        private void Changed(bool changed) {
            if (changed) {
                settings.Changed();
            }
        }

        /// <summary>A row for each kind of line: where it goes, and its colour.</summary>
        private void RenderKinds() {
            Form.Section("What goes where");

            if (!ImGui.BeginTable("combat-kinds", 6, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.RowBg)) {
                return;
            }

            ImGui.TableSetupColumn("Kind", ImGuiTableColumnFlags.WidthStretch);

            foreach (var column in new[] { "Floating", "Log", "Save", "Hide in chat", "Colour" }) {
                ImGui.TableSetupColumn(column);
            }

            ImGui.TableHeadersRow();

            foreach (var info in CombatKindInfo.All) {
                var options = settings.Data.CombatOptions(info.Kind);
                ImGui.PushID(info.Key);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                Widgets.Text(info.Label);
                Widgets.Tip(info.Tip);

                Cell("##floating", ref options.Floating, $"{info.Label} show as floating combat text.");
                Cell("##log", ref options.Log, $"{info.Label} are listed in the Combat log window.");
                Cell("##save", ref options.Save, $"{info.Label} are written to the day's file, while Save to files is on.");
                Cell("##hide", ref options.HideChat, $"{info.Label} are kept out of the game's chat. Other plugins reading "
                    + "chat may still see them.");

                ImGui.TableNextColumn();
                var color = PackedColor.Unpack(options.Color);
                var rgb = new Vector3(color.R, color.G, color.B);

                if (ImGui.ColorEdit3("##color", ref rgb, ImGuiColorEditFlags.NoInputs)) {
                    options.Color = PackedColor.Pack(rgb.X, rgb.Y, rgb.Z, 1);
                    settings.Changed();
                }
                Widgets.Tip($"The colour of {info.Label.ToLowerInvariant()} in floating text and the Combat log window.");

                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        private void Cell(string id, ref bool value, string tip) {
            ImGui.TableNextColumn();

            if (Form.Check(id, ref value, tip)) {
                settings.Changed();
            }
        }

        private void RenderWindow() {
            Form.Section("Combat log window");
            window.RenderControls("A window listing recent combat, each kind in its colour, that you can search. The Log "
                + "column above says which kinds it lists.");
        }

        private void RenderSaving() {
            Form.Section("Saved logs");
            var first = true;

            if (Form.FlowCheck(ref first, "Save to files##combat-save", ref settings.Data.CombatLogSave, "Writes each line of "
                + "combat as it happens to a text file for the character and day, in "
                + $"{watch.SaveFolder}. The Save column above says which kinds. Nothing is ever deleted: the files are yours "
                + "to clear out.")) {
                settings.Changed();
            }

            if (Form.FlowSmallButton(ref first, "Open folder##combat-folder", $"Opens {watch.SaveFolder} in Explorer.")) {
                try {
                    problem = null;
                    Directory.CreateDirectory(watch.SaveFolder);
                    System.Diagnostics.Process.Start("explorer.exe", $"\"{watch.SaveFolder}\"");
                }
                catch (Exception ex) {
                    problem = $"Couldn't open the folder: {ex.Message}";
                }
            }

            if (problem != null) {
                Widgets.TextColored(Colors.Warning, problem);
            }
        }

        /// <summary>The window: a search, then the lines the Log column lets through that hold it, newest at the bottom.</summary>
        private void RenderLog() {
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 12);
            ImGui.InputTextWithHint("##combat-log-search", "Search...", ref search, 60);
            Widgets.Tip("Shows only lines with these words in them, a name or a spell, say.");

            ImGui.SameLine();

            if (Form.Check("Follow##combat-log-follow", ref follow, "Keeps the newest line in sight as new ones come in.")) {
                lastCount = -1;
            }

            ImGui.SameLine();

            if (ImGui.SmallButton("Clear##combat-log-clear")) {
                watch.Clear();
            }
            Widgets.Tip("Empties this window. Files already saved aren't touched.");

            if (!Widgets.BeginChild("combat-log-lines", Vector2.Zero, true)) {
                ImGui.EndChild();
                return;
            }

            var lines = watch.Lines;
            var drawn = 0;
            var first = lines.Count;

            // back from the newest for where to start, then drawn in order
            while (first > 0 && drawn < Drawn) {
                first--;

                if (Shows(lines[first])) {
                    drawn++;
                }
            }

            for (var i = first; i < lines.Count; i++) {
                if (Shows(lines[i])) {
                    DrawLine(lines[i]);
                }
            }

            if (follow && lines.Count != lastCount) {
                lastCount = lines.Count;
                ImGui.SetScrollHereY(1);
            }

            ImGui.EndChild();
        }

        private bool Shows(CombatLine line) {
            return settings.Data.CombatOptions(line.Kind).Log
                && (search.Length == 0 || line.Text.IndexOf(search.Trim(), StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void DrawLine(CombatLine line) {
            var color = PackedColor.Unpack(settings.Data.CombatOptions(line.Kind).Color);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(color.R, color.G, color.B, 1));
            Widgets.TextWrapped($"{line.At:HH:mm:ss} {line.Text}");
            ImGui.PopStyleColor();
        }

        /// <summary>Each on its own, so one that fails can't leave the others listening.</summary>
        public void Dispose() {
            foreach (var piece in new IDisposable[] { window, text, watch }) {
                try {
                    piece.Dispose();
                }
                catch (Exception ex) {
                    PluginCore.Log(ex);
                }
            }
        }
    }
}
