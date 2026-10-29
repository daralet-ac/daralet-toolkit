using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The fellowship window's layouts for big fellowships (a Town Defense one has no cap): List, which scrolls past so many
    /// members; Pages, tabs of so many each; and Grid, a small tile each.
    /// </summary>
    internal partial class FellowshipTab {
        /// <summary>Below this fraction of their health, a fellow marks their page's tab.</summary>
        private const float HurtBelow = 0.5f;

        /// <summary>A Grid tile's stamina and mana strips, in pixels at 100%.</summary>
        private const float StripHeight = 3;

        /// <summary>The biggest member drawn last frame, for the List's scrolling box: members differ (their own bars).</summary>
        private Vector2 measuredMember;

        /// <summary>The biggest member drawn so far this frame, which is measuredMember next frame.</summary>
        private Vector2 measuringMember;

        /// <summary>One under another; past Scroll after, a box that scrolls, as tall as that many.</summary>
        private void RenderList(List<FellowInfo> members, FellowshipInfo fellowship, uint selected, Vector2 barSize) {
            var scrollAfter = Math.Max(1, settings.Data.FellowshipScrollAfter);

            if (members.Count <= scrollAfter) {
                RenderMembers(members, fellowship, selected, barSize);
                return;
            }

            var style = ImGui.GetStyle();
            var member = measuredMember.X > 0 ? measuredMember
                : new Vector2(barSize.X, ImGui.GetTextLineHeight() + 3 * (barSize.Y + style.ItemSpacing.Y));
            var size = new Vector2(member.X + style.ScrollbarSize + style.ItemSpacing.X,
                scrollAfter * (member.Y + style.ItemSpacing.Y));

            if (Widgets.BeginChild("fellowship-scroll", size, false)) {
                RenderMembers(members, fellowship, selected, barSize);
            }

            ImGui.EndChild();
        }

        /// <summary>Tabs of Page size members each; a tab with someone badly hurt on it is red.</summary>
        private void RenderPages(List<FellowInfo> members, FellowshipInfo fellowship, uint selected, Vector2 barSize) {
            var size = Math.Max(1, settings.Data.FellowshipPageSize);
            FellowshipOrder.Page(members, size, 0, out var pages);

            if (pages == 1) {
                RenderMembers(members, fellowship, selected, barSize);
                return;
            }

            if (!ImGui.BeginTabBar("fellowship-pages")) {
                return;
            }

            for (var i = 0; i < pages; i++) {
                var onPage = FellowshipOrder.Page(members, size, i, out _);
                var hurt = onPage.Any(m => FellowshipOrder.HealthFraction(m) < HurtBelow);
                var label = $"{i * size + 1}-{i * size + onPage.Count}###fellowship-page-{i}";

                if (hurt) {
                    ImGui.PushStyleColor(ImGuiCol.Text, Colors.Danger);
                }

                var open = ImGui.BeginTabItem(label);

                if (hurt) {
                    ImGui.PopStyleColor();
                }

                Widgets.Tip(hurt ? "Someone on this page is below half health." : "These members.");

                if (open) {
                    RenderMembers(onPage, fellowship, selected, barSize);
                    ImGui.EndTabItem();
                }
            }

            ImGui.EndTabBar();
        }

        /// <summary>A tile each, Columns to a row: name, a health bar, and stamina and mana as thin strips under it.</summary>
        private void RenderGrid(List<FellowInfo> members, FellowshipInfo fellowship, uint selected) {
            var data = settings.Data;
            var columns = Math.Max(1, data.FellowshipGridColumns);
            var width = (float)Math.Round(Math.Max(40, data.FellowshipTileWidth) * window.Scale);

            for (var i = 0; i < members.Count; i++) {
                if (i % columns != 0) {
                    ImGui.SameLine();
                }

                ImGui.PushID((int)members[i].Guid);
                RenderTile(members[i], fellowship, members[i].Guid == selected, width);
                ImGui.PopID();
            }
        }

        private void RenderTile(FellowInfo member, FellowshipInfo fellowship, bool selected, float width) {
            var (_, stamina, mana) = settings.Data.FellowShows(member.Name);
            var strip = (float)Math.Max(2, Math.Round(StripHeight * window.Scale));
            var drawList = ImGui.GetWindowDrawList();

            ImGui.BeginGroup();

            var nameColor = !member.Distance.HasValue ? Colors.Muted : selected ? Colors.Info : Colors.Text;
            Widgets.TextColored(nameColor, Fit(MemberLabel(member, fellowship), width));

            Widgets.Bar(FellowshipOrder.HealthFraction(member), Colors.Health, "", width, ImGui.GetTextLineHeight());

            if (stamina) {
                Strip(drawList, member.Stamina, member.MaxStamina, Colors.Stamina, width, strip);
            }

            if (mana) {
                Strip(drawList, member.Mana, member.MaxMana, Colors.Mana, width, strip);
            }

            ImGui.EndGroup();

            if (selected) {
                drawList.AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), ImGui.GetColorU32(Colors.Info));
            }

            Widgets.OutlineIfHovered();

            if (ImGui.IsItemClicked(ImGuiMouseButton.Left)) {
                Select(member);
            }

            if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
                menuMember = member.Name;
                window.OpenMenu();
            }

            Widgets.Tip($"{member.Name}, level {member.Level}: health {member.Health} / {member.MaxHealth}, stamina "
                + $"{member.Stamina} / {member.MaxStamina}, mana {member.Mana} / {member.MaxMana}"
                + (member.Distance.HasValue ? $", {Display.Distance(member.Distance)} away" : ", too far away to say")
                + ". Click to select them; right-click for their bars and Pin to top.");
        }

        /// <summary>The window's Big fellowships settings: the layout and its numbers, then who shows and in what order.</summary>
        private void RenderLayoutSettings() {
            var data = settings.Data;
            Form.Section("Big fellowships");

            if (!Form.BeginForm("fellowship-layout")) {
                return;
            }

            Form.Label("Layout", "How the window shows a fellowship: List (one under another, scrolling past so many), "
                + "Pages (tabs of so many each) or Grid (a small tile each).");
            var layout = data.FellowshipLayout ?? FellowshipOrder.List;

            if (Form.Segmented("fellowship-layout-choice", new[] {
                (FellowshipOrder.List, "List"), (FellowshipOrder.Pages, "Pages"), (FellowshipOrder.Grid, "Grid"),
            }, ref layout)) {
                data.FellowshipLayout = layout;
                settings.Changed();
            }

            switch (layout) {
                case FellowshipOrder.Pages:
                    Form.Label("Page size", "Members on each page. A page with someone below half health has a red tab.");
                    SliderSetting("##fellowship-page-size", ref data.FellowshipPageSize, 2, 20, "%d");
                    break;
                case FellowshipOrder.Grid:
                    Form.Label("Columns", "Tiles to a row.");
                    SliderSetting("##fellowship-columns", ref data.FellowshipGridColumns, 1, 10, "%d");
                    Form.Label("Tile width", "Each tile's width; the window's Size makes them bigger too.");
                    SliderSetting("##fellowship-tile-width", ref data.FellowshipTileWidth, 40, 200, "%d px");
                    break;
                default:
                    Form.Label("Scroll after", "Past this many members the window stops growing and scrolls.");
                    SliderSetting("##fellowship-scroll-after", ref data.FellowshipScrollAfter, 3, 30, "%d");
                    break;
            }

            Form.Label("Show up to", "The most members the window shows, pinned ones first (right-click a member to pin "
                + "them). All shows everyone.");
            SliderSetting("##fellowship-show-up-to", ref data.FellowshipShowUpTo, 0, 40,
                data.FellowshipShowUpTo == 0 ? "All" : "%d");

            Form.Label("Sort", "The order after pinned members: the fellowship's own (leader first), by name, lowest "
                + "health first, or nearest first.");
            var sort = data.FellowshipSort ?? FellowshipOrder.ByFellowship;

            if (Form.Segmented("fellowship-sort", new[] {
                (FellowshipOrder.ByFellowship, "Leader"), (FellowshipOrder.ByName, "Name"),
                (FellowshipOrder.ByHealth, "Health"), (FellowshipOrder.ByDistance, "Distance"),
            }, ref sort)) {
                data.FellowshipSort = sort;
                settings.Changed();
            }

            Form.Label("Far away", "Members too far away for the server to say how far.");

            if (Form.Check("Hide far away##fellowship", ref data.FellowshipHideFar, "Leaves out members too far away for "
                + "the server to say how far (not in your landblock or the ones round it). Pinned ones too.")) {
                settings.Changed();
            }

            Form.Label("Spells up to", "Buffs and debuffs by each name are only asked for in a fellowship of up to this "
                + "many: each member's spells are a lot for the server to send every second.");
            SliderSetting("##fellowship-spells-up-to", ref data.FellowshipSpellsUpTo, 1, 40, "%d");

            ImGui.EndTable();

            if (data.FellowshipPinned.Count > 0) {
                Widgets.TextDisabled("Pinned: " + string.Join(", ", data.FellowshipPinned));
                ImGui.SameLine();

                if (ImGui.SmallButton("Unpin all##fellowship")) {
                    data.FellowshipPinned.Clear();
                    settings.Changed();
                }
                Widgets.Tip("Puts every pinned member back in the sort.");
            }
        }

        private void SliderSetting(string id, ref int value, int min, int max, string format) {
            if (Form.Slider(id, ref value, min, max, format)) {
                settings.Changed();
            }
        }

        /// <summary>A thin bar with no text: a tile's stamina or mana.</summary>
        private static void Strip(ImDrawListPtr drawList, uint current, uint max, Vector4 color, float width, float height) {
            var min = ImGui.GetCursorScreenPos();
            var fraction = max == 0 ? 0f : Math.Min(1f, (float)current / max);
            drawList.AddRectFilled(min, min + new Vector2(width, height), ImGui.GetColorU32(ImGuiCol.FrameBg));
            drawList.AddRectFilled(min, min + new Vector2((float)Math.Round(width * fraction), height),
                ImGui.GetColorU32(color));
            ImGui.Dummy(new Vector2(width, height));
        }

        /// <summary>A name cut short with ".." to fit a tile, as wide as it can be.</summary>
        private static string Fit(string text, float width) {
            if (ImGui.CalcTextSize(text).X <= width) {
                return text;
            }

            for (var length = text.Length - 1; length > 1; length--) {
                var cut = text.Substring(0, length) + "..";

                if (ImGui.CalcTextSize(cut).X <= width) {
                    return cut;
                }
            }

            return text.Substring(0, 1);
        }
    }
}
