using System.Collections.Generic;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Character tab, as the game's (layout 0x21000028: a list over Apply, Reset and Defaults, 80x32 from template
    /// 0x100002CD): each group's heading in yellow, its options as lights (GamePanelArt.Light), a gold bar between groups
    /// (060012C5). As in the game, a change waits for Apply: Reset throws the waiting changes away, and Defaults sets every
    /// option to the game's default (ClientOptions.Default), to Apply too. Apply sets each changed option on the client
    /// (ClientOptions.Set), which the game hears and saves.
    /// </summary>
    internal sealed partial class OptionsPanel {
        private static readonly Vector4 HeadingColor = new Vector4(1f, 0.87f, 0.25f, 1f);

        /// <summary>Changes waiting for Apply, by option.</summary>
        private readonly Dictionary<uint, bool> pending = new Dictionary<uint, bool>();

        private void RenderCharacter(bool retail) {
            if (ClientOptions.Broken) {
                Widgets.TextColored(Colors.Danger, "The game's options couldn't be used (see log.txt).");
                return;
            }

            var buttonHeight = Widgets.Scaled(32);
            var footer = buttonHeight + ImGui.GetStyle().ItemSpacing.Y * 2;

            if (Widgets.BeginChild("options-character", new Vector2(0, -footer), false)) {
                var first = true;

                foreach (var (title, options) in GameOptions.Groups) {
                    if (!first) {
                        GamePanelArt.GoldBar(retail);
                    }

                    first = false;
                    Widgets.TextColored(HeadingColor, title);

                    foreach (var option in options) {
                        RenderOption(option, retail);
                    }
                }
            }

            Widgets.EndChild();
            RenderCharacterButtons(retail, buttonHeight);
        }

        private void RenderOption(GameOption option, bool retail) {
            var current = ClientOptions.Get(option.Option);
            var on = pending.TryGetValue(option.Option, out var waiting) ? waiting : current ?? false;

            ImGui.Indent(Widgets.Scaled(8));

            if (GamePanelArt.Light($"option-{option.Option}", option.Label, ref on, retail, current.HasValue)) {
                // back to what it is: nothing waiting
                if (on == current) {
                    pending.Remove(option.Option);
                }
                else {
                    pending[option.Option] = on;
                }
            }

            ImGui.Unindent(Widgets.Scaled(8));
            Widgets.Tip(option.Tooltip + (pending.ContainsKey(option.Option) ? "\n\nChanged: press Apply to keep it." : ""));
        }

        /// <summary>Apply, Reset and Defaults, spread across the bottom as the game's are.</summary>
        private void RenderCharacterButtons(bool retail, float height) {
            var width = ImGui.GetContentRegionAvail().X;
            var size = new Vector2(Widgets.Scaled(80), height);
            var spacing = (width - size.X * 3) / 4;
            var changed = pending.Count > 0;

            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + spacing);

            if (GamePanelArt.Button("options-apply", "Apply", size, "cast", retail, changed)) {
                foreach (var change in pending) {
                    ClientOptions.Set(change.Key, change.Value);
                }

                pending.Clear();
            }

            Widgets.Tip("Keeps your changes, as the game's Apply does: the game uses them at once and saves them.");
            ImGui.SameLine(0, spacing);

            if (GamePanelArt.Button("options-reset", "Reset", size, "cast", retail, changed)) {
                pending.Clear();
            }

            Widgets.Tip("Throws away the changes you haven't applied.");
            ImGui.SameLine(0, spacing);

            if (GamePanelArt.Button("options-defaults", "Defaults", size, "cast", retail)) {
                foreach (var (_, options) in GameOptions.Groups) {
                    foreach (var option in options) {
                        var normal = ClientOptions.Default(option.Option);

                        if (normal.HasValue && normal != ClientOptions.Get(option.Option)) {
                            pending[option.Option] = normal.Value;
                        }
                        else {
                            pending.Remove(option.Option);
                        }
                    }
                }
            }

            Widgets.Tip("Sets every option to the game's default. Press Apply to keep them.");
        }
    }
}
