using System;
using System.Collections.Generic;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// One chat window's chat (ChatWindows): the lines it shows of the history, by its categories, drawn in a scrolling
    /// region (ChatView.Lines.cs), and its box to type in (ChatView.Input.cs) with its options (ChatView.Options.cs). Drawn
    /// in its own window when popped out, else in the main one when its tab is open.
    /// </summary>
    internal sealed partial class ChatView {
        /// <summary>The chat window typed in last, for where a linked item goes (ChatWindows.TypingWindow), or -1.</summary>
        public static int LastTyped = -1;

        /// <summary>
        /// A line shown counts as seen while the window's on screen; this long after, new ones count as unread.
        /// </summary>
        private static readonly TimeSpan SeenFor = TimeSpan.FromSeconds(0.5);

        private readonly ChatWindows owner;
        private readonly int index;
        private readonly List<ChatLine> shown = new List<ChatLine>();

        /// <summary>What it shows has changed: shown is made again from the history before it's next drawn.</summary>
        private bool dirty = true;

        /// <summary>Lines up to this one were cleared from this window.</summary>
        private long clearedThrough;

        /// <summary>The oldest line the history kept when shown was last trimmed.</summary>
        private long trimmedTo;

        private long noteId;
        private DateTime onScreenAt = DateTime.MinValue;
        private string hideSource;
        private List<string> hideWords = new List<string>();

        /// <summary>The window it was last drawn in, for its menus.</summary>
        private FloatingWindow host;

        public ChatView(ChatWindows owner, int index) {
            this.owner = owner;
            this.index = index;
            callback = MakeCallback();
        }

        /// <summary>Lines come since it was last on screen.</summary>
        public int Unread { get; private set; }

        /// <summary>One of the unread lines is lit up (it mentions you).</summary>
        public bool UnreadHighlighted { get; private set; }

        private ChatSettings Data => owner.Data;
        private ChatWindowSettings Window => owner.Data.Window(index);

        /// <summary>Draws it in the window it's in (its own, or the main one with its tab open).</summary>
        public void Render(FloatingWindow window) {
            host = window;
            onScreenAt = DateTime.UtcNow;
            MarkRead();

            if (dirty) {
                Rebuild();
            }

            Trim();
            ImGui.PushID($"chat-view-{index}");

            try {
                if (openOptions) {
                    openOptions = false;
                    ImGui.OpenPopup("chat-options");
                }

                if (searching) {
                    RenderSearch();
                }

                var input = Window.Input ? ImGui.GetFrameHeightWithSpacing() : 0;
                RenderLines(new Vector2(0, -input));

                if (Window.Input) {
                    RenderInput();
                }

                RenderOptions();
            }
            finally {
                ImGui.PopID();
            }
        }

        public void MarkRead() {
            Unread = 0;
            UnreadHighlighted = false;
        }

        /// <summary>Shows its lines again from the history, for a change to what it shows.</summary>
        public void Refilter() {
            dirty = true;
        }

        /// <summary>Empties the window; other windows keep their lines.</summary>
        public void Clear() {
            clearedThrough = owner.Lines.Count > 0 ? owner.Lines[owner.Lines.Count - 1].Id : clearedThrough;
            dirty = true;
        }

        /// <summary>A line come into the history: shown here if it's one this window shows.</summary>
        public void Added(ChatLine line) {
            if (dirty || !Accepts(line)) {
                return;
            }

            shown.Add(line);

            if (DateTime.UtcNow - onScreenAt > SeenFor && !line.TellFromYou) {
                Unread++;
                UnreadHighlighted |= line.Highlighted;
            }

            if (!atBottom) {
                newBelow++;
            }
        }

        /// <summary>
        /// A line of the plugin's own in this window alone (why a tell couldn't go), gone when it's shown afresh.
        /// </summary>
        public void Note(string text) {
            var line = ChatLine.Read(--noteId, "[Chat] " + text, 0x1A, DateTime.Now);
            line.Category = ChatCategory.Errors;
            shown.Add(line);
            scrollToBottom = true;
        }

        private bool Accepts(ChatLine line) {
            if (line.Id <= clearedThrough) {
                return false;
            }

            var window = Window;
            var mandatory = index == 0 && line.Category == ChatCategory.Alerts;

            if (!mandatory && !window.Shows(line.Category)) {
                return false;
            }

            if (window.HideWords != hideSource) {
                hideSource = window.HideWords;
                hideWords = ChatHighlights.Words(window.HideWords);
            }

            if (hideWords.Count > 0 && ChatHighlights.AnyIn(line.Text, hideWords)) {
                return false;
            }

            var find = search.Trim();
            return find.Length == 0 || line.Text.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void Rebuild() {
            dirty = false;
            shown.Clear();
            trimmedTo = owner.OldestId;

            foreach (var line in owner.Lines) {
                if (Accepts(line)) {
                    shown.Add(line);
                }
            }

            scrollToBottom = true;
        }

        /// <summary>Lets go of lines the history no longer keeps.</summary>
        private void Trim() {
            var oldest = owner.OldestId;

            if (oldest <= trimmedTo) {
                return;
            }

            trimmedTo = oldest;
            shown.RemoveAll(l => l.Id > 0 && l.Id < oldest);

            if (laid.Count > shown.Count * 2 + 64) {
                laid.Clear();
            }
        }

        /// <summary>
        /// Draws the window's text in the chat font at its pixel size (ChatFonts): made at that size, it's sharp. Until it's
        /// ready, UtilityBelt's font is stretched to the size. Returns whether a font was pushed, for EndFont.
        /// </summary>
        public static bool BeginFont(ChatWindows owner, int index) {
            var size = owner.PixelSize(index);
            var font = ChatFonts.Get(owner.Data.Font, size);

            if (font.HasValue) {
                ImGui.PushFont(font.Value);
            }

            // the window's own scale, so its text is that many pixels: 1 for a font made at the size
            ImGui.SetWindowFontScale(1);
            var unscaled = ImGui.GetFontSize();

            if (unscaled > 0) {
                ImGui.SetWindowFontScale(size / unscaled);
            }

            return font.HasValue;
        }

        public static void EndFont(bool pushed) {
            if (pushed) {
                ImGui.PopFont();
            }
        }
    }
}
