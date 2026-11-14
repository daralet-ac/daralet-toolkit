using System;
using System.Collections.Generic;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The picked row's raise at the bottom, as the game's (layout 0x21000045's 0x1000024D, 300x55): "Strength: 10039
    /// (+40)" across the top, then "Experience To Raise:" and "Unassigned Experience:" with their numbers at the right, and
    /// the +10 arrow over the +1 at the far right (0x100005EB, 0x10000246: 0600712B and 06004CB6, dimmed 712C and 4CB7).
    /// An untrained skill has Train for its skill credits instead. A raise waits for the server's answer (what's spent
    /// changing, or two seconds) before it can be pressed again, so a second click can't spend the same XP twice.
    /// </summary>
    internal sealed partial class AttributesPanel {
        /// <summary>How long a raise waits for the server before its buttons can be pressed again.</summary>
        private static readonly TimeSpan RaiseWait = TimeSpan.FromSeconds(2);

        /// <summary>The game's raise arrows are 30x26.</summary>
        private static readonly Vector2 RetailRaise = new Vector2(30, 26);

        /// <summary>A row's raise sent, by row key, and what it had spent then: its buttons wait for the server.</summary>
        private readonly Dictionary<string, (DateTime At, long Spent)> raising = new Dictionary<string, (DateTime, long)>();

        /// <summary>The footer's height: three lines of text, or the two arrows, whichever's taller, and the separator.</summary>
        private float FooterHeight() {
            var style = ImGui.GetStyle();
            var text = ImGui.GetTextLineHeightWithSpacing() * 3;
            var arrows = ArrowSize().Y * 2 + style.ItemSpacing.Y;
            return Math.Max(text, arrows) + Widgets.Scaled(7) + style.ItemSpacing.Y * 2
                + (ClientTraining.Broken ? ImGui.GetTextLineHeightWithSpacing() : 0);
        }

        private static Vector2 ArrowSize() {
            var height = Math.Max(ImGui.GetTextLineHeight(), Widgets.Scaled(RetailRaise.Y));
            return new Vector2(height * RetailRaise.X / RetailRaise.Y, height);
        }

        private void RenderFooter(StatRow row, bool retail) {
            if (row == null) {
                Widgets.TextDisabled("Nothing to raise here.");
                return;
            }

            var character = CoreManager.Current.CharacterFilter;
            var unassigned = Math.Max(0, character.UnassignedXP);
            var credits = character.SkillPoints;
            var style = ImGui.GetStyle();
            var arrows = ArrowSize();
            var textWidth = Math.Max(Widgets.Scaled(120), ImGui.GetContentRegionAvail().X - arrows.X - style.ItemSpacing.X);

            ImGui.BeginGroup();
            RenderFooterTitle(row, textWidth);

            if (row.Untrained) {
                GamePanelArt.LeftRight("Skill Credits To Train:", row.TrainCost > 0 ? row.TrainCost.ToString() : "-",
                    textWidth,
                    credits >= row.TrainCost ? Colors.Text : Colors.Danger);
                GamePanelArt.LeftRight("Skill Credits:", credits.ToString(), textWidth, Colors.Text);
            }
            else {
                var one = row.Table?.Cost(row.Spent, 1);
                GamePanelArt.LeftRight("Experience To Raise:", row.Table == null ? "..." : one == null ? "Infinity!"
                    : AttributesOptions.Xp(one.Value.Cost), textWidth,
                    one != null && unassigned < one.Value.Cost ? Colors.Danger : Colors.Text);
                GamePanelArt.LeftRight("Unassigned Experience:", AttributesOptions.Xp(unassigned), textWidth, Colors.Text);
            }

            ImGui.EndGroup();
            ImGui.SameLine();
            ImGui.BeginGroup();

            if (row.Untrained) {
                RenderTrain(row, credits, retail, arrows);
            }
            else {
                var waiting = Waiting(row.Key, row.Spent);
                RaiseButton(row, 10, unassigned, waiting, retail, arrows);
                RaiseButton(row, 1, unassigned, waiting, retail, arrows);
            }

            ImGui.EndGroup();
        }

        /// <summary>"Strength: 10039 (+40)", the buff green and a debuff red, centred.</summary>
        private static void RenderFooterTitle(StatRow row, float width) {
            var value = row.ValueText ?? row.Value.ToString();
            var head = $"{row.Name}: {value}";
            var buff = row.ValueText == "-" || row.Buff == 0 ? "" : $" ({(row.Buff > 0 ? "+" : "")}{row.Buff})";
            var size = ImGui.CalcTextSize(head + buff).X;
            var start = ImGui.GetCursorPosX();

            ImGui.SetCursorPosX(start + Math.Max(0, (width - size) / 2));
            Widgets.Text(head);

            if (buff.Length > 0) {
                ImGui.SameLine(0, 0);
                Widgets.TextColored(row.Buff > 0 ? Colors.Good : Colors.Danger, buff);
            }
        }

        /// <summary>
        /// Train, for an untrained skill: in Retail the game's own button, the green arrow the +1 raise has (06004CB6),
        /// bright with credits enough for it and dulled (06004CB7) without, half an arrow down so it sits beside the two
        /// lines of credits as the game's does; in Modern a Train button.
        /// </summary>
        private void RenderTrain(StatRow row, int credits, bool retail, Vector2 size) {
            var key = row.Key;
            var waiting = Waiting(key, credits);
            var can = row.TrainCost > 0 && credits >= row.TrainCost && !waiting && !ClientTraining.Broken;
            bool clicked;

            if (retail) {
                ImGui.SetCursorPosY(ImGui.GetCursorPosY()
                    + (float)Math.Round((size.Y + ImGui.GetStyle().ItemSpacing.Y) / 2));
                var min = ImGui.GetCursorScreenPos();
                ImGui.BeginDisabled(!can);
                clicked = ImGui.InvisibleButton($"##train-{key}", size);
                ImGui.EndDisabled();

                if (!ItemIcons.DrawArtBox(ImGui.GetWindowDrawList(), can ? "raise-1" : "raise-1-disabled", min,
                    min + size)) {
                    var fill = can ? ImGuiCol.Button : ImGuiCol.FrameBg;
                    ImGui.GetWindowDrawList().AddRectFilled(min, min + size, ImGui.GetColorU32(fill));
                }
            }
            else {
                ImGui.BeginDisabled(!can);
                clicked = ImGui.Button($"Train##{key}", new Vector2(Math.Max(size.X, ImGui.CalcTextSize("Train").X
                    + ImGui.GetStyle().FramePadding.X * 2), size.Y * 2));
                ImGui.EndDisabled();
            }

            if (clicked && can && ClientTraining.TrainSkill((uint)row.Skill, row.TrainCost)) {
                raising[key] = (DateTime.UtcNow, credits);
            }

            Widgets.Tip(row.TrainCost <= 0 ? $"{row.Name} can't be told what training costs."
                : $"Trains {row.Name} for {row.TrainCost} skill credit{(row.TrainCost == 1 ? "" : "s")}, as the game's "
                    + "panel does." + (credits < row.TrainCost ? $" You have {credits}." : ""));
        }

        /// <summary>
        /// +10 or +1, for the XP the game's table says: greyed out when you haven't the XP, it's as high as it goes, or the
        /// last raise hasn't been answered yet.
        /// </summary>
        private void RaiseButton(StatRow row, int by, long unassigned, bool waiting, bool retail, Vector2 size) {
            var cost = row.Table?.Cost(row.Spent, by);
            var enabled = cost.HasValue && row.Raise != null && !waiting && unassigned >= cost.Value.Cost
                && !ClientTraining.Broken;
            var min = ImGui.GetCursorScreenPos();

            ImGui.BeginDisabled(!enabled);
            var clicked = ImGui.InvisibleButton($"##raise-{row.Key}-{by}", size);
            ImGui.EndDisabled();

            if (clicked && enabled && row.Raise(cost.Value.Cost)) {
                raising[row.Key] = (DateTime.UtcNow, row.Spent);
            }

            Widgets.Tip(!cost.HasValue ? $"{row.Name} is as high as it goes."
                : $"Raises {row.Name} {(cost.Value.Raises == 1 ? "once" : cost.Value.Raises + " times")} for "
                    + $"{AttributesOptions.Xp(cost.Value.Cost)} XP" + (unassigned < cost.Value.Cost ? ": you haven't that "
                    + "much unassigned." : ", as the game's raise button does."));

            var drawList = ImGui.GetWindowDrawList();
            var art = (by == 1 ? "raise-1" : "raise-10") + (enabled ? "" : "-disabled");

            if (retail && ItemIcons.DrawArtBox(drawList, art, min, min + size)) {
                return;
            }

            var fill = !enabled ? ImGuiCol.FrameBg : ImGui.IsItemActive() ? ImGuiCol.ButtonActive
                : ImGui.IsItemHovered() ? ImGuiCol.ButtonHovered : ImGuiCol.Button;
            drawList.AddRectFilled(min, min + size, ImGui.GetColorU32(fill), ImGui.GetStyle().FrameRounding);
            var label = "+" + by;
            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(min + (size - textSize) / 2, ImGui.GetColorU32(enabled ? ImGuiCol.Text : ImGuiCol.TextDisabled),
                label);
        }

        /// <summary>
        /// A raise sent and not yet answered: what was spent hasn't moved since, and it's been under two seconds. Stops a
        /// second click spending the same XP twice.
        /// </summary>
        private bool Waiting(string key, long spent) {
            if (!raising.TryGetValue(key, out var sent)) {
                return false;
            }

            if (sent.Spent != spent || DateTime.UtcNow - sent.At > RaiseWait) {
                raising.Remove(key);
                return false;
            }

            return true;
        }
    }
}
