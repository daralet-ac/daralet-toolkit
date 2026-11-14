using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Titles tab, laid out as the game's Titles page (layout 0x2100005E, its own page with no character at the top):
    /// "Current Display Title:" and the title, the gold bar 06001420, "All Available Titles:" and the list (270x24 rows on
    /// the stone, the picked one under 06001AAF), the bar again, and Set as Display Title (200x32, the Cast button's art,
    /// greyed until another title is picked). The words are the game's (string table 0x23000001). A double-click on a
    /// title sets it too. The titles are ClientTitles'.
    /// </summary>
    internal sealed partial class AttributesPanel {
        private readonly ClientTitles titles = new ClientTitles();
        private uint pickedTitle;
        private string titleSearch = "";

        /// <summary>How tall the bar and the button under the list were last frame, which the list leaves room for.</summary>
        private float titlesFooter;

        private void RenderTitles(bool retail) {
            var mine = titles.Mine;
            Widgets.Text("Current Display Title:");
            Widgets.Tip("This is the title that all other players will see when they examine your character.");

            if (mine == null) {
                Widgets.TextDisabled("Your titles come from the server as you log in: log out and back in to see them "
                    + "here.");
                return;
            }

            var display = mine.Display ?? 0;
            Widgets.TextColored(Colors.Info, display != 0 ? titles.Name(display) : "None");
            TitleBar(retail);

            Widgets.Text("All Available Titles:");
            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
            ImGui.InputTextWithHint("##titles-search", "Search titles", ref titleSearch, 64);

            var list = mine.Owned
                .Select(id => (Id: id, Name: titles.Name(id)))
                .Where(t => titleSearch.Length == 0 || t.Name.IndexOf(titleSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (!mine.Has(pickedTitle)) {
                pickedTitle = 0;
            }

            var footer = titlesFooter > 0 ? titlesFooter : Widgets.Scaled(9) + Widgets.Scaled(32)
                + ImGui.GetStyle().ItemSpacing.Y * 3;
            var top = ImGui.GetCursorScreenPos();
            var listSize = new Vector2(ImGui.GetContentRegionAvail().X, Math.Max(ImGui.GetFrameHeight(),
                ImGui.GetContentRegionAvail().Y - footer));

            if (retail) {
                GamePanelArt.Stone(ImGui.GetWindowDrawList(), top, top + listSize);
            }

            if (Widgets.BeginChild("titles-list", listSize, false)) {
                RenderTitleRows(list, display, retail);
            }

            Widgets.EndChild();

            var start = ImGui.GetCursorPosY();
            TitleBar(retail);
            RenderSetTitle(display, retail);
            titlesFooter = ImGui.GetCursorPosY() - start;
        }

        private void RenderTitleRows(List<(uint Id, string Name)> list, uint display, bool retail) {
            if (list.Count == 0) {
                Widgets.TextDisabled(titleSearch.Length > 0 ? "No titles match." : "You have no titles yet.");
                return;
            }

            var height = Math.Max(Widgets.Scaled(24), ImGui.GetTextLineHeight() + 2);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(ImGui.GetStyle().ItemSpacing.X, 0));

            foreach (var (id, name) in list) {
                var min = ImGui.GetCursorScreenPos();
                var width = ImGui.GetContentRegionAvail().X;
                var max = min + new Vector2(width, height);

                if (ImGui.InvisibleButton($"##title-{id}", new Vector2(width, height))) {
                    pickedTitle = id;
                }

                var hovered = ImGui.IsItemHovered();

                if (hovered && Widgets.DoubleClicked() && id != display) {
                    titles.SetDisplay(id);
                }

                Widgets.Tip(id == display ? $"{name}: the title other players see."
                    : $"{name}: double-click, or pick it and Set as Display Title, to have other players see it.");

                var drawList = ImGui.GetWindowDrawList();

                if (pickedTitle == id) {
                    if (!retail || !ItemIcons.DrawArtBox(drawList, "title-row-selected", min, max)) {
                        drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.Header));
                    }
                }
                else if (hovered) {
                    drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.HeaderHovered, 0.5f));
                }

                var textY = (float)Math.Round(min.Y + (height - ImGui.GetTextLineHeight()) / 2);
                var color = id == display ? Colors.Info : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
                drawList.AddText(new Vector2(min.X + Widgets.Scaled(5), textY), ImGui.GetColorU32(color), name);
            }

            ImGui.PopStyleVar();
        }

        private void RenderSetTitle(uint display, bool retail) {
            var size = new Vector2(Math.Min(Widgets.Scaled(200), ImGui.GetContentRegionAvail().X), Widgets.Scaled(32));
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, (ImGui.GetContentRegionAvail().X - size.X) / 2));
            var enabled = pickedTitle != 0 && pickedTitle != display;

            if (GamePanelArt.Button("set-title", "Set as Display Title", size, "cast", retail, enabled)) {
                titles.SetDisplay(pickedTitle);
            }

            Widgets.Tip("Makes the picked title the one other players see when they examine you.");
        }

        /// <summary>The Titles page's gold bar (06001420, 300x9), or ImGui's line.</summary>
        private static void TitleBar(bool retail) {
            var min = ImGui.GetCursorScreenPos();
            var width = ImGui.GetContentRegionAvail().X;
            var height = Widgets.Scaled(9);

            if (retail && ItemIcons.DrawArtBox(ImGui.GetWindowDrawList(), "title-separator", min,
                min + new Vector2(width, height))) {
                ImGui.Dummy(new Vector2(width, height));
            }
            else {
                ImGui.Separator();
            }
        }
    }
}
