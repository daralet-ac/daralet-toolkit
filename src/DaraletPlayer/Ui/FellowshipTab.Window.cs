using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>The fellowship window: each fellow's name line (focus, level, distance, spells) and their bars.</summary>
    internal partial class FellowshipTab {
        private const int MinBarWidth = 40;
        private const int MaxBarWidth = 400;
        private const float WindowBarHeight = 14;

        /// <summary>A debuff's icon is framed in this red, so it stands out from the buffs.</summary>
        private static readonly uint DebuffFrame = PackedColor.Pack(0.9f, 0.25f, 0.25f, 1f);

        /// <summary>The fellow right-clicked in the window, whose vitals its menu offers; null when it wasn't on a fellow.</summary>
        private string menuMember;

        /// <summary>Whether the mouse is over an icon on the name line drawn last, which shows its own tooltip.</summary>
        private bool spellHovered;

        /// <summary>The window's right-click menu, over a fellow: which of their vitals it shows, for them alone.</summary>
        private void RenderMenuMember() {
            if (menuMember == null) {
                return;
            }

            Widgets.TextDisabled(menuMember);
            RenderVitalChoices(menuMember);
            RenderPinChoice(menuMember);
            ImGui.Separator();
        }

        /// <summary>Pin to top or Unpin, for a fellow: pinned members are always shown, first.</summary>
        private void RenderPinChoice(string name) {
            var pinned = settings.Data.FellowshipPinned;
            var index = pinned.FindIndex(p => string.Equals(p, name, StringComparison.OrdinalIgnoreCase));

            if (ImGui.MenuItem(index >= 0 ? "Unpin" : "Pin to top")) {
                if (index >= 0) {
                    pinned.RemoveAt(index);
                }
                else {
                    pinned.Add(name);
                }

                settings.Changed();
            }
            Widgets.Tip(index >= 0 ? $"{name} goes back to their place in the sort."
                : $"Shows {name} first in the fellowship window, always, whatever the sort or Show up to.");
        }

        private void RenderWindow() {
            Refresh();

            var fellowship = feed.Fellowship;

            if (fellowship?.Members == null || fellowship.Members.Count == 0) {
                Widgets.TextDisabled("No fellowship");
                return;
            }

            var data = settings.Data;
            var barWidth = Math.Max(MinBarWidth, Math.Min(MaxBarWidth, data.FellowshipBarWidth));
            var maximums = fellowship.Members.SelectMany(m => new[] { m.MaxHealth, m.MaxStamina, m.MaxMana }).ToArray();
            var barSize = new Vector2(Widgets.VitalBarWidth(barWidth * window.Scale, maximums), WindowBarHeight * window.Scale);
            var selected = unchecked((uint)CoreManager.Current.Actions.CurrentSelection);
            var me = unchecked((uint)CoreManager.Current.CharacterFilter.Id);

            // a right-click is on a fellow (their vitals join the window's menu) or not
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows)) {
                menuMember = null;
            }

            if (measuringMember.X > 0) {
                measuredMember = measuringMember;
            }

            measuringMember = Vector2.Zero;

            var members = FellowshipOrder.Arrange(fellowship.Members, data.FellowshipPinned, data.FellowshipSort,
                data.FellowshipHideFar, data.FellowshipShowUpTo, me);

            switch (data.FellowshipLayout) {
                case FellowshipOrder.Grid:
                    RenderGrid(members, fellowship, selected);
                    break;
                case FellowshipOrder.Pages:
                    RenderPages(members, fellowship, selected, barSize);
                    break;
                default:
                    RenderList(members, fellowship, selected, barSize);
                    break;
            }

            var hidden = fellowship.Members.Count - members.Count;

            if (hidden > 0) {
                Widgets.TextDisabled($"+{hidden} not shown");
                Widgets.Tip("Left out by Show up to or Hide far away, in the window's settings (the Social tab's "
                    + "Fellowship). Right-click someone to pin them, so they always show.");
            }
        }

        private void RenderMembers(List<FellowInfo> members, FellowshipInfo fellowship, uint selected, Vector2 barSize) {
            foreach (var member in members) {
                ImGui.PushID((int)member.Guid);
                RenderWindowMember(member, fellowship, member.Guid == selected, barSize);
                ImGui.PopID();
            }
        }

        /// <summary>
        /// One fellow in the window: their name line and bars, as one group, so clicking anywhere on them selects them the
        /// way the game's own fellowship panel does.
        /// </summary>
        private void RenderWindowMember(FellowInfo member, FellowshipInfo fellowship, bool selected, Vector2 barSize) {
            var (health, stamina, mana) = settings.Data.FellowShows(member.Name);
            var sideBySide = settings.Data.FellowshipSideBySide;
            var bars = (health ? 1 : 0) + (stamina ? 1 : 0) + (mana ? 1 : 0);
            var spacing = ImGui.GetStyle().ItemSpacing.X;
            var lineWidth = sideBySide && bars > 1 ? bars * barSize.X + (bars - 1) * spacing : barSize.X;

            ImGui.BeginGroup();
            RenderNameLine(member, fellowship, selected, lineWidth);

            var firstBar = true;
            Bar(ref firstBar, sideBySide, health, member.Health, member.MaxHealth, Colors.Health, barSize);
            Bar(ref firstBar, sideBySide, stamina, member.Stamina, member.MaxStamina, Colors.Stamina, barSize);
            Bar(ref firstBar, sideBySide, mana, member.Mana, member.MaxMana, Colors.Mana, barSize);

            ImGui.EndGroup();
            measuringMember = Vector2.Max(measuringMember, ImGui.GetItemRectSize());
            Widgets.OutlineIfHovered();

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                Select(member);
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
                menuMember = member.Name;
                window.OpenMenu();
            }

            // a spell icon's own tooltip comes first
            if (!spellHovered) {
                MemberTip(member);
            }
        }

        private static void Bar(ref bool first, bool sideBySide, bool shown, uint current, uint max, Vector4 color,
            Vector2 size) {
            if (!shown) {
                return;
            }

            if (sideBySide && !first) {
                ImGui.SameLine();
            }

            first = false;
            Widgets.VitalBar(current, max, color, size.X, size.Y);
        }


        /// <summary>
        /// The line with a fellow's name: their Combat Focus's icon, the name, level and distance, then their debuffs and
        /// buffs as icons, wrapping inside the bars' width. Each piece by its own checkbox.
        /// </summary>
        private void RenderNameLine(FellowInfo member, FellowshipInfo fellowship, bool selected, float lineWidth) {
            var data = settings.Data;
            var left = ImGui.GetCursorScreenPos().X;
            var size = ImGui.GetTextLineHeight();
            var drawList = ImGui.GetWindowDrawList();
            spellHovered = false;

            if (data.FellowshipShowFocus && member.Focus != null && member.Focus.Icon != 0) {
                var at = ImGui.GetCursorScreenPos();
                icons.DrawAt(drawList, new IconLayers(member.Focus.Underlay, member.Focus.Icon, member.Focus.Overlay), at, size);
                ImGui.Dummy(new Vector2(size, size));
                SpellTip(member.Focus.Name);
                ImGui.SameLine();
            }

            var nameColor = !member.Distance.HasValue ? Colors.Muted : selected ? Colors.Info : Colors.Text;
            Widgets.TextColored(nameColor, MemberLabel(member, fellowship));

            if (data.FellowshipShowLevel) {
                ImGui.SameLine();
                Widgets.TextColored(Colors.Detail, member.Level.ToString());
            }

            var me = unchecked((uint)CoreManager.Current.CharacterFilter.Id);

            if (data.FellowshipShowDistance && member.Distance.HasValue && member.Guid != me) {
                ImGui.SameLine();
                Widgets.TextColored(Colors.Detail, Display.Distance(member.Distance));
            }

            if (data.FellowshipShowDebuffs) {
                SpellIcons(member.Debuffs, true, left, lineWidth, size);
            }

            if (data.FellowshipShowBuffs) {
                SpellIcons(member.Buffs, false, left, lineWidth, size);
            }
        }

        /// <summary>One icon a spell, on along the line, starting a new one where the next wouldn't fit the bars' width.</summary>
        private void SpellIcons(List<int> spells, bool debuffs, float left, float lineWidth, float size) {
            if (spells == null) {
                return;
            }

            var drawList = ImGui.GetWindowDrawList();
            var gap = Math.Max(1f, size / 8);

            foreach (var id in spells) {
                var layers = Spells.Icon(id);

                if (layers == null) {
                    continue;
                }

                ImGui.SameLine(0, gap);

                if (ImGui.GetCursorScreenPos().X + size > left + Math.Max(lineWidth, size)) {
                    ImGui.NewLine();
                }

                var at = ImGui.GetCursorScreenPos();
                icons.DrawAt(drawList, layers, at, size);

                if (debuffs) {
                    drawList.AddRect(at, at + new Vector2(size, size), DebuffFrame);
                }

                ImGui.Dummy(new Vector2(size, size));
                SpellTip(Spells.Name(id) + (debuffs ? " (harmful)" : ""));
            }
        }

        /// <summary>A tooltip for the icon drawn last, which the fellow's own then leaves alone.</summary>
        private void SpellTip(string text) {
            if (!ImGui.IsItemHovered()) {
                return;
            }

            spellHovered = true;
            Widgets.Tip(text);
        }

        /// <summary>
        /// Whether the window has a fellowship to show, for Only when needed. It asks the server as the window would, since a
        /// hidden window doesn't draw, and drawing is what asks.
        /// </summary>
        private object FellowshipInUse() {
            Refresh();
            return feed.Fellowship?.Members?.Count > 0 ? "fellowship" : null;
        }
    }
}
