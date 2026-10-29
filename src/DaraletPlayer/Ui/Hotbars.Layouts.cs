using System;
using System.Collections.Generic;
using System.IO;
using DaraletPlayer.Feed;
using ImGuiNET;
using static DaraletPlayer.Ui.Form;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Hotbars page's Layouts: every bar saved to a file (Export layout) and put back from one (Import layout), with
    /// where each bar's window is (HotbarLayout).
    /// </summary>
    internal partial class Hotbars {
        private const string ExportPopup = "hotbar-layout-export";
        private const string ImportPopup = "hotbar-layout-import";
        private static readonly TimeSpan LayoutMessageFor = TimeSpan.FromSeconds(6);

        private string layoutName = "";
        private bool layoutWithItems = true;
        private List<string> layoutFiles = new List<string>();

        /// <summary>The layout picked in Import's list, waiting for Replace, and the file it came from.</summary>
        private HotbarLayout pendingLayout;
        private string pendingLayoutFile;

        private string layoutMessage;
        private bool layoutMessageIsProblem;
        private DateTime layoutMessageAt;

        private void RenderLayouts() {
            Section("Layouts");
            var folder = HotbarLayout.DefaultFolder();

            if (ImGui.Button("Export layout", new System.Numerics.Vector2(-1, 0))) {
                layoutName = layoutName.Length > 0 ? layoutName : $"{Character} hotbars";
                ImGui.OpenPopup(ExportPopup);
            }
            Widgets.Tip($"Saves every hotbar to a file in {folder}: each bar's layout, look and keys, where its window is "
                + "and how it's shown, and your items on them if you like. To keep, to give another character or profile "
                + "the same bars, or to share.");

            if (ImGui.Button("Import layout", new System.Numerics.Vector2(-1, 0))) {
                layoutFiles = HotbarLayout.List(folder);
                pendingLayout = null;
                ImGui.OpenPopup(ImportPopup);
            }
            Widgets.Tip($"Puts the hotbars in a file from {folder} in place of yours (it asks first).");

            if (layoutMessage != null && DateTime.UtcNow - layoutMessageAt < LayoutMessageFor) {
                ImGui.PushTextWrapPos(0);
                Widgets.TextColored(layoutMessageIsProblem ? Colors.Warning : Colors.Good, layoutMessage);
                ImGui.PopTextWrapPos();
            }

            RenderExportPopup(folder);
            RenderImportPopup(folder);
        }

        private void RenderExportPopup(string folder) {
            if (!ImGui.BeginPopup(ExportPopup)) {
                return;
            }

            Widgets.Text("Export layout");
            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 16);
            var enter = ImGui.InputText("##hotbar-layout-name", ref layoutName, 60, ImGuiInputTextFlags.EnterReturnsTrue);
            Widgets.Tip("The layout's name, which is its file's name too. A file of the same name is replaced.");

            Check("With my items", ref layoutWithItems, $"Saves what's on the bars for {Character} too. Importing it puts "
                + "them on the bars of whoever imports it: an item that character doesn't have shows shaded until they "
                + "carry one like it.");

            var name = layoutName.Trim();

            if ((Widgets.Button("Save", name.Length > 0, $"Saves {Words.Count(Data.Bars.Count, "hotbar")} to "
                + $"{HotbarLayout.FileName(name)} in {folder}.") || enter) && name.Length > 0) {
                SaveLayout(name, folder);
                ImGui.CloseCurrentPopup();
            }

            ImGui.SameLine();

            if (ImGui.Button("Cancel##hotbar-layout-export")) {
                ImGui.CloseCurrentPopup();
            }

            ImGui.EndPopup();
        }

        private void SaveLayout(string name, string folder) {
            try {
                var layout = HotbarLayout.Make(name, Data, settings.Data.Windows, Character, layoutWithItems);
                var path = HotbarLayout.Save(layout, folder);
                LayoutMessage($"Saved {Words.Count(layout.Bars.Count, "hotbar")} to {path}.", false);
            }
            catch (Exception ex) {
                LayoutMessage($"Couldn't save the layout: {ex.Message}", true);
            }
        }

        /// <summary>Import's list of files; one picked asks before it replaces the bars.</summary>
        private void RenderImportPopup(string folder) {
            if (!ImGui.BeginPopup(ImportPopup)) {
                return;
            }

            if (pendingLayout != null) {
                RenderReplaceQuestion();
                ImGui.EndPopup();
                return;
            }

            if (layoutFiles.Count == 0) {
                Widgets.TextDisabled("No hotbar layouts yet. Save one with Export layout, or put .json files in the folder.");
            }

            foreach (var path in layoutFiles) {
                if (ImGui.Selectable($"{Path.GetFileNameWithoutExtension(path)}##{path}", false,
                    ImGuiSelectableFlags.DontClosePopups)) {
                    pendingLayout = HotbarLayout.Load(path, out var problem);
                    pendingLayoutFile = Path.GetFileName(path);

                    if (pendingLayout == null) {
                        LayoutMessage(problem, true);
                        ImGui.CloseCurrentPopup();
                    }
                }
                Widgets.Tip(path);
            }

            ImGui.Separator();

            if (ImGui.SmallButton("Open folder")) {
                OpenLayoutFolder(folder);
            }
            Widgets.Tip($"Opens {folder} in Explorer, to add or remove layout files.");

            ImGui.EndPopup();
        }

        private void RenderReplaceQuestion() {
            var layout = pendingLayout;
            var items = layout.Character != null
                ? $" It has {layout.Character}'s items, which go on your bars in place of yours."
                : " It has no items: yours stay on the bars with the same numbers.";

            Widgets.PopupText($"Replace your {Words.Count(Data.Bars.Count, "hotbar")} with the "
                + $"{Words.Count(layout.Bars.Count, "hotbar")} in {pendingLayoutFile}?{items} Other characters' items stay "
                + "on the bars with the same numbers.");

            if (ImGui.Button("Replace")) {
                ApplyLayout(layout);
                pendingLayout = null;
                ImGui.CloseCurrentPopup();
            }
            Widgets.Tip("Puts the layout's bars in place of yours, windows and all.");

            ImGui.SameLine();

            if (ImGui.Button("Back")) {
                pendingLayout = null;
            }
            Widgets.Tip("Back to the list.");
        }

        private void ApplyLayout(HotbarLayout layout) {
            try {
                HotbarLayout.Apply(layout, Data, settings.Data.Windows, Character);
                RemakeWindows();
                selected = 0;
                settings.Changed();
                LayoutMessage($"Loaded {Words.Count(layout.Bars.Count, "hotbar")} from {pendingLayoutFile}.", false);
            }
            catch (Exception ex) {
                LayoutMessage($"Couldn't load the layout: {ex.Message}", true);
            }
        }

        private void OpenLayoutFolder(string folder) {
            try {
                Directory.CreateDirectory(folder);
                System.Diagnostics.Process.Start("explorer.exe", $"\"{folder}\"");
            }
            catch (Exception ex) {
                LayoutMessage($"Couldn't open the folder: {ex.Message}", true);
            }
        }

        private void LayoutMessage(string message, bool problem) {
            layoutMessage = message;
            layoutMessageIsProblem = problem;
            layoutMessageAt = DateTime.UtcNow;
        }
    }
}
