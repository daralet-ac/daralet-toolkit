using System;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's book window (gmBookUI, element type 0x10000019, layout 0x21000019, 300x362; panel 2 in the panel
    /// frame), for reading books, notes and scrolls of writing, in a window of its own, by GamePanelInterceptor: the
    /// game's panel is closed the moment it opens and this one shown with the book the server just sent (BookReader).
    /// One page at a time, the page picked from the drop-down under it or turned with the curled corners, as the game's.
    /// Writing stays the game's: Write in the right-click menu (only for a book you could write in) uses the book again
    /// and leaves the game's window alone when it opens. Retail draws it with the game's own art (BookPanel.Retail.cs).
    /// </summary>
    internal sealed partial class BookPanel : GamePanel {
        /// <summary>The game's book window, as the panel frame numbers it.</summary>
        public const uint GamePanelId = 0x2;

        // the ink the game writes a page in, on the parchment
        private static readonly Vector4 Ink = new Vector4(0.08f, 0.06f, 0.02f, 1);

        private readonly BookReader reader;
        private readonly GamePanelInterceptor interceptor;

        /// <summary>The page showing, and the book it's of: the same book opened again opens at the same page.</summary>
        private int page;
        private uint pageOf;
        private BookData shown;
        private bool scrollToTop;

        /// <summary>The Text size slider's value while it's held, applied once let go.</summary>
        private int? draggingSize;

        public BookPanel(Settings settings) : this(settings, new BookReader()) {
        }

        private BookPanel(Settings settings, BookReader reader)
            : base("book", "Book", settings, new uint[0], new Vector2(480, 120), inUse: () => reader.Showing,
                hideWhen: () => false, defaultSize: new Vector2(300, 362), alwaysOnlyInUse: true, onClose: reader.Close,
                minSize: new Vector2(200, 180), padding: Vector2.Zero) {
            this.reader = reader;
            reader.Wanted = () => Options.Replace;
            interceptor = new GamePanelInterceptor("Book", GamePanelId, () => Options.Replace, Window);
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        protected override string ReplaceTip => "Opens the plugin's Book window instead of the game's book window when you "
            + "read a book, note or anything else with writing in it. Writing in one is still the game's: Write, in the "
            + "window's right-click menu, opens the game's window for that book.";

        protected override string WindowTip => "Shows the book, note or scroll you're reading, a page at a time, as the "
            + "game's book window does. It only shows while one is open.";

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                // outside ImGui's frame, where the page's font can be made (the chat windows do it too, when they're on)
                ChatFonts.MakeWanted();

                // the game's window had the book (Write) and has closed: it's done with, not the plugin's again
                if (reader.HandedToGame && !interceptor.LettingOpen) {
                    reader.HandedToGame = false;
                    reader.Close();
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>What it's called: the item's own name, as the game titles its window.</summary>
        private static string Title(BookData book) {
            return CoreManager.Current.WorldFilter[unchecked((int)book.BookId)]?.Name ?? "Book";
        }

        protected override void RenderContents() {
            var book = reader.Open;

            if (book == null || CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                Widgets.TextDisabled("No book open.");
                return;
            }

            if (!ReferenceEquals(book, shown)) {
                // the game's way (gmBookUI::OpenBook): a new book at its first page, the same one where it was left
                if (book.BookId != pageOf) {
                    page = 0;
                }

                shown = book;
                pageOf = book.BookId;
                scrollToTop = true;
            }

            page = Math.Max(0, Math.Min(page, book.Pages.Count - 1));

            if (Retail) {
                RenderRetail(book);
            }
            else {
                RenderModern(book);
            }
        }

        /// <summary>Turns to another page, the text back at its top.</summary>
        private void TurnTo(int to) {
            if (to != page) {
                page = to;
                scrollToTop = true;
            }
        }

        private static string PageLabel(int index) => $"Page {index + 1}";

        /// <summary>The page's words, wrapping, in a child that scrolls when there are more than fit.</summary>
        private void RenderText(BookData book, Vector2 size, float rightMargin = 0) {
            ImGui.PushStyleColor(ImGuiCol.ChildBg, Vector4.Zero);

            try {
                if (Widgets.BeginChild("book-text", size, false)) {
                    if (scrollToTop) {
                        ImGui.SetScrollY(0);
                        scrollToTop = false;
                    }

                    var text = book.Pages.Count == 0 ? "" : book.Pages[page].Text;
                    var pushed = BeginPageFont();
                    // short of the right edge by the margin (the game keeps its words off the scroll bar's place)
                    ImGui.PushTextWrapPos(rightMargin > 0 ? Math.Max(20, size.X - rightMargin) : 0);

                    try {
                        if (text == null) {
                            Widgets.TextDisabled("This page's words weren't sent.");
                        }
                        else if (Retail) {
                            Widgets.TextColored(Ink, text);
                        }
                        else {
                            Widgets.Text(text);
                        }
                    }
                    finally {
                        ImGui.PopTextWrapPos();

                        if (pushed) {
                            ImGui.PopFont();
                        }
                    }
                }

                Widgets.EndChild();
            }
            finally {
                ImGui.PopStyleColor();
            }
        }

        /// <summary>How tall the page's words are, in pixels: the Text size times the window's Size.</summary>
        private int PagePixels => (int)Math.Round(Math.Max(BookOptions.MinFontSize,
            settings.Data.Book.ClampedFontSize * Window.Scale));

        /// <summary>
        /// The page's words in a font made at exactly their size (ChatFonts, as the chat windows have theirs), so they're
        /// sharp at any size; until it's made, UtilityBelt's font stretched to it. Set on the text's own child window, so
        /// nothing else in the window changes. Returns whether a font was pushed.
        /// </summary>
        private bool BeginPageFont() {
            var size = PagePixels;
            var font = ChatFonts.Get("", size);

            if (font.HasValue) {
                ImGui.PushFont(font.Value);
            }

            ImGui.SetWindowFontScale(1);
            var unscaled = ImGui.GetFontSize();

            if (unscaled > 0) {
                ImGui.SetWindowFontScale(size / unscaled);
            }

            return font.HasValue;
        }

        protected override void RenderOwnSettings() {
            if (!Form.BeginForm("book-settings")) {
                return;
            }

            Form.Label("Text size", "How tall a page's words are, in pixels, at the window's normal Size. Each size is a "
                + "font of its own, drawn sharp. The window's right-click menu has this slider too.");
            FontSizeSlider("settings", Form.ControlWidth);
            ImGui.EndTable();
        }

        /// <summary>
        /// The Text size slider, in the settings and the right-click menu: applied once let go, since each size is a font
        /// of its own and every step of a drag would make one.
        /// </summary>
        private void FontSizeSlider(string where, float width) {
            ImGui.SetNextItemWidth(width);
            var size = draggingSize ?? settings.Data.Book.ClampedFontSize;
            Sliders.SliderInt($"##book-font-size-{where}", ref size, BookOptions.MinFontSize, BookOptions.MaxFontSize,
                "%d px");

            if (ImGui.IsItemActive()) {
                draggingSize = size;
            }
            else if (ImGui.IsItemDeactivated() || size != settings.Data.Book.ClampedFontSize && !draggingSize.HasValue) {
                SetFontSize(size);
                draggingSize = null;
            }
        }

        private void SetFontSize(int size) {
            settings.Data.Book.FontSize = Math.Max(BookOptions.MinFontSize, Math.Min(BookOptions.MaxFontSize, size));
            settings.Changed();
        }

        /// <summary>The page drop-down, as wide as it's given, "Page 1" on it and a row a page.</summary>
        private void RenderPagePicker(BookData book, float width, bool retail) {
            ImGui.SetNextItemWidth(width);
            var look = retail ? ComboLook.Book : null;

            if (!Combos.Begin("##book-page", PageLabel(page), ImGuiComboFlags.HeightLarge, look: look)) {
                return;
            }

            for (var i = 0; i < book.Pages.Count; i++) {
                if (Combos.Item($"{PageLabel(i)}##book-page-{i}", i == page, look: look)) {
                    TurnTo(i);
                }

                var author = book.Pages[i].AuthorName;

                if (!book.Pages[i].IgnoreAuthor && !string.IsNullOrEmpty(author)) {
                    Widgets.Tip("Written by " + author + ".");
                }
            }

            Combos.End();
        }

        private void RenderModern(BookData book) {
            var padding = ImGui.GetStyle().WindowPadding;
            ImGui.SetCursorPos(ImGui.GetCursorPos() + padding);
            var avail = ImGui.GetContentRegionAvail() - padding;
            var spacing = ImGui.GetStyle().ItemSpacing;
            var rowHeight = ImGui.GetFrameHeight();

            ImGui.BeginGroup();
            Widgets.Text(Title(book));
            ImGui.Separator();
            var textHeight = Math.Max(rowHeight, avail.Y - ImGui.GetTextLineHeightWithSpacing() - spacing.Y * 2 - rowHeight);
            RenderText(book, new Vector2(avail.X, textHeight));

            var buttonWidth = Widgets.Scaled(28);
            ImGui.BeginDisabled(page <= 0);

            if (ImGui.Button("<##book-back", new Vector2(buttonWidth, rowHeight))) {
                TurnTo(page - 1);
            }

            ImGui.EndDisabled();
            Widgets.Tip("The page before.");
            ImGui.SameLine();
            RenderPagePicker(book, Math.Max(Widgets.Scaled(60), avail.X - (buttonWidth + spacing.X) * 2), retail: false);
            ImGui.SameLine();
            ImGui.BeginDisabled(page >= book.Pages.Count - 1);

            if (ImGui.Button(">##book-next", new Vector2(buttonWidth, rowHeight))) {
                TurnTo(page + 1);
            }

            ImGui.EndDisabled();
            Widgets.Tip("The page after.");
            ImGui.EndGroup();
        }

        protected override void RenderOwnMenu() {
            Widgets.Text("Text size");
            FontSizeSlider("menu", Widgets.Scaled(160));
            Widgets.Tip("How tall a page's words are, in pixels. Applied when you let go.");

            var book = reader.Open;

            if (book == null || !book.WritableBy(unchecked((uint)CoreManager.Current.CharacterFilter.Id))) {
                return;
            }

            if (ImGui.MenuItem("Write in the game's window")) {
                Write(book);
            }

            Widgets.Tip("Opens this book in the game's own book window, where you can write in it: uses the book again, "
                + "and leaves the game's window alone until it's closed.");
        }

        /// <summary>The book handed to the game's own window: used again, and the window left alone when it opens.</summary>
        private void Write(BookData book) {
            interceptor.LetNextOpen();
            reader.HandedToGame = true;
            CoreManager.Current.Actions.UseItem(unchecked((int)book.BookId), 0);
            PluginCore.LogFile($"[book] 0x{book.BookId:X8} handed to the game's window to write in");
        }

        public override void Dispose() {
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            reader.Dispose();
            interceptor.Dispose();
            base.Dispose();
        }
    }
}
