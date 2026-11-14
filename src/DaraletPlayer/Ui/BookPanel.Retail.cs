using System;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The book window in the game's own art, laid out as layout 0x21000019 is (300x362, every size here in its pixels,
    /// scaled with the text): the pink title bar 06001279 (276x30, `book-title`) with the item's name on it and the close
    /// button 0x1000010E beside it (24x30, 0600126C, pressed 0600126D, `book-close`); the parchment 0x10000110 under it
    /// (from template 0x10000116: its top 06001273, 300x33, bottom 06001270, 300x32, left 06001271, 22 wide, right
    /// 06001272, 21 wide, each stretched along its side, and the middle 0600126F, 265x100, repeated), the page's words
    /// on it at 22,26 (262x240, the game's scroll bar beside them at 284); the curled corners that turn the page, back
    /// 0x100002D7 at its top left (54x33: 06001269, pressed 6A, none to turn to 6B, `book-back`) and next 0x100002D8 at
    /// its bottom right (65x32: 06001266, 67, 68, `book-next`); then under it the edge 0600126E (8x20) and the page
    /// drop-down 0x10000470 (template 0x10000471 in 0x21000043, 292x18: ComboLook.Book), and the foot 06001265 (300x6).
    /// </summary>
    internal sealed partial class BookPanel {
        private void RenderRetail(BookData book) {
            var drawList = ImGui.GetWindowDrawList();
            var origin = ImGui.GetCursorScreenPos();
            var avail = ImGui.GetContentRegionAvail();
            var s = Widgets.Scaled(1);
            var width = avail.X;
            var height = avail.Y;
            Vector2 At(float x, float y) => origin + new Vector2((float)Math.Round(x), (float)Math.Round(y));

            // the page drop-down's row: its field 18 high, or as tall as the text needs
            var rowHeight = Math.Max((float)Math.Round(18 * s), ImGui.GetFrameHeight());
            var footHeight = (float)Math.Round(6 * s);
            var rowTop = height - footHeight - rowHeight - (float)Math.Round(2 * s);
            var titleHeight = (float)Math.Round(30 * s);
            var closeWidth = (float)Math.Round(24 * s);

            // the title bar and its close button
            ItemIcons.DrawArtBox(drawList, "book-title", At(0, 0), At(width - closeWidth, titleHeight));
            var title = Title(book);
            var titleSize = ImGui.CalcTextSize(title);
            drawList.PushClipRect(At(0, 0), At(width - closeWidth, titleHeight), true);
            drawList.AddText(At(Math.Max(4 * s, (width - closeWidth - titleSize.X) / 2), (titleHeight - titleSize.Y) / 2),
                ImGui.GetColorU32(Ink), title);
            drawList.PopClipRect();

            ImGui.SetCursorScreenPos(At(width - closeWidth, 0));

            if (ImGui.InvisibleButton("##book-close", new Vector2(closeWidth, titleHeight))) {
                reader.Close();
            }

            Widgets.Tip("Closes the book.");
            ItemIcons.DrawArtBox(drawList, ImGui.IsItemActive() ? "book-close-pressed" : "book-close",
                At(width - closeWidth, 0), At(width, titleHeight));

            // the parchment, from under the title to the page row
            var top = titleHeight;
            var bottom = rowTop;
            Parchment(drawList, At(0, top), At(width, bottom), s);

            // the words, then the corners over the parchment, as the game layers them
            ImGui.SetCursorScreenPos(At(22 * s, top + 26 * s));
            var textSize = new Vector2(Math.Max(20, width - 22 * s), Math.Max(20, bottom - top - 26 * s - 40 * s));
            RenderText(book, textSize, 16 * s);

            if (Corner("book-back", At(0, top), new Vector2(54, 33) * s, page > 0, "The page before.")) {
                TurnTo(page - 1);
            }

            var nextSize = new Vector2(65, 32) * s;

            if (Corner("book-next", At(width - nextSize.X, bottom - nextSize.Y), nextSize, page < book.Pages.Count - 1,
                "The page after.")) {
                TurnTo(page + 1);
            }

            // the page row: the edge, then the drop-down
            var edgeWidth = (float)Math.Round(8 * s);
            ItemIcons.DrawArtBox(drawList, "book-page-edge", At(0, rowTop), At(edgeWidth, height - footHeight));
            ImGui.SetCursorScreenPos(At(edgeWidth, rowTop + (float)Math.Round(s)));
            var framePadding = ImGui.GetStyle().FramePadding;
            ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,
                new Vector2(framePadding.X, Math.Max(0, (rowHeight - ImGui.GetFontSize()) / 2)));

            try {
                RenderPagePicker(book, width - edgeWidth, retail: true);
            }
            finally {
                ImGui.PopStyleVar();
            }

            ItemIcons.DrawArtBox(drawList, "book-foot", At(0, height - footHeight), At(width, height));

            // the window's whole size taken, so nothing scrolls
            ImGui.SetCursorScreenPos(origin);
            ImGui.Dummy(avail);
        }

        /// <summary>
        /// The parchment across min to max: its middle repeated, then its four sides stretched along their length, the top
        /// and bottom over the sides' ends, as the game's template draws them.
        /// </summary>
        private static void Parchment(ImDrawListPtr drawList, Vector2 min, Vector2 max, float s) {
            var left = (float)Math.Round(22 * s);
            var right = (float)Math.Round(21 * s);
            var topHeight = (float)Math.Round(33 * s);
            var bottomHeight = (float)Math.Round(32 * s);
            var tile = new Vector2((float)Math.Round(265 * s), (float)Math.Round(100 * s));
            var middleMin = new Vector2(min.X + left, min.Y + topHeight);
            var middleMax = new Vector2(max.X - right, max.Y - bottomHeight);

            if (middleMax.X > middleMin.X && middleMax.Y > middleMin.Y) {
                drawList.PushClipRect(middleMin, middleMax, true);

                for (var y = middleMin.Y; y < middleMax.Y; y += tile.Y) {
                    ItemIcons.DrawArtTiled(drawList, "book-parchment", new Vector2(middleMin.X, y),
                        new Vector2(middleMax.X, y + tile.Y), tile.X);
                }

                drawList.PopClipRect();
            }

            ItemIcons.DrawArtBox(drawList, "book-left", new Vector2(min.X, min.Y + topHeight),
                new Vector2(min.X + left, max.Y - bottomHeight));
            ItemIcons.DrawArtBox(drawList, "book-right", new Vector2(max.X - right, min.Y + topHeight),
                new Vector2(max.X, max.Y - bottomHeight));
            ItemIcons.DrawArtBox(drawList, "book-top", min, new Vector2(max.X, min.Y + topHeight));
            ItemIcons.DrawArtBox(drawList, "book-bottom", new Vector2(min.X, max.Y - bottomHeight), max);
        }

        /// <summary>
        /// A curled corner that turns the page: its arrow while there's a page that way (pressed while held), the plain
        /// curl when there isn't. True when clicked.
        /// </summary>
        private static bool Corner(string art, Vector2 at, Vector2 size, bool enabled, string tip) {
            var size2 = new Vector2((float)Math.Round(size.X), (float)Math.Round(size.Y));
            ImGui.SetCursorScreenPos(at);
            ImGui.BeginDisabled(!enabled);
            var clicked = ImGui.InvisibleButton("##" + art, size2);
            ImGui.EndDisabled();

            if (enabled) {
                Widgets.Tip(tip);
            }

            var state = !enabled ? "-disabled" : ImGui.IsItemActive() ? "-pressed" : "";
            ItemIcons.DrawArtBox(ImGui.GetWindowDrawList(), art + state, at, at + size2);
            return clicked && enabled;
        }
    }
}
