using System;
using DaraletPlayer.Feed;
using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The book or note last opened, read from the server's own messages as the game's book window reads them
    /// (BookMessages: 0x00B4 when one is used, every page's words in it; 0x00B8 for a page sent on its own). Decal
    /// doesn't pass books on. `[book]` lines in log.txt.
    /// </summary>
    internal sealed class BookReader : IDisposable {
        /// <summary>The book last opened, until it's closed; null for none.</summary>
        public BookData Open { get; private set; }

        /// <summary>Whether the plugin's window is to show books now (the Book panel's Replace).</summary>
        public Func<bool> Wanted { get; set; } = () => false;

        /// <summary>The book was handed to the game's own window (to write in it), so the plugin's stays away.</summary>
        public bool HandedToGame { get; set; }

        /// <summary>What the window shows: the open book, while it's wanted and the game's window hasn't got it.</summary>
        public object Showing => Open != null && !HandedToGame && Wanted() ? Open : null;

        public BookReader() {
            CoreManager.Current.EchoFilter.ServerDispatch += EchoFilter_ServerDispatch;
        }

        public void Close() {
            Open = null;
        }

        private void EchoFilter_ServerDispatch(object sender, NetworkMessageEventArgs e) {
            try {
                if (e.Message.Type != CooldownMessages.GameEvent) {
                    return;
                }

                var data = e.Message.RawData;
                var book = BookMessages.ReadBook(data);

                if (book != null) {
                    Open = book;
                    PluginCore.LogFile($"[book] 0x{book.BookId:X8} opened: {book.Pages.Count} of {book.MaxPages} pages, "
                        + $"{book.MaxCharsPerPage} letters a page");
                    return;
                }

                var page = BookMessages.ReadPage(data);

                if (page != null && Open != null && !Open.Apply(page)) {
                    PluginCore.LogFile($"[book] page {page.Index} of 0x{page.BookId:X8} not taken");
                }
            }
            catch (Exception ex) {
                PluginCore.LogFile("[book] " + ex);
            }
        }

        public void Dispose() {
            CoreManager.Current.EchoFilter.ServerDispatch -= EchoFilter_ServerDispatch;
        }
    }
}
