using System;
using System.Collections.Generic;
using System.Numerics;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A chat window's lines: each wrapped to the width once (ChatLayout, by the font's own advances) and kept until the
    /// width, the font or the time's format changes, so only the lines in sight are drawn, straight onto the draw list, and
    /// a long history scrolls as fast as a short one. Hovering a line lights it and shows its Copy button; names and plugins'
    /// links are underlined under the mouse and clicked as in the game's chat (ChatWindows.ClickLink); a linked item shows
    /// the item when hovered and opens it in the Appraisal window when clicked. New lines keep the newest in sight unless
    /// you've scrolled up, when a button at the bottom says how many came.
    /// </summary>
    internal sealed partial class ChatView {
        private const double CopiedSeconds = 1.5;

        /// <summary>The text's shadow: black, most of the way solid.</summary>
        private const uint Shadow = 0xD0000000;

        private readonly Dictionary<long, Laid> laid = new Dictionary<long, Laid>();
        private readonly List<string> pieces = new List<string>();
        private (float Width, float Size, IntPtr Font, string Time, bool Indent, int Spacing) laidFor;

        private bool atBottom = true;
        private bool scrollToBottom = true;
        private int newBelow;
        private long newestDrawn;

        private ChatLine copied;
        private double copiedAt;
        private bool copyWorked;

        private ChatLine menuLine;
        private ChatSegment menuSegment;
        private bool openMenu;

        /// <summary>A line as laid out: its runs and its height.</summary>
        private sealed class Laid {
            public ChatLayout Layout;
            public float Height;
            /// <summary>How many pieces come before the line's own (the time).</summary>
            public int Offset;
        }

        private void RenderLines(Vector2 size) {
            // NoMove: a drag over the lines, with nothing there to take it, would move the whole window
            if (!Widgets.BeginChild($"chat-lines-{index}", size, false, ImGuiWindowFlags.NoMove)) {
                ImGui.EndChild();
                return;
            }

            try {
                DrawLines();
                RenderLineMenu();
            }
            finally {
                ImGui.EndChild();
            }
        }

        private unsafe void DrawLines() {
            var font = ImGui.GetFont();
            var size = ImGui.GetFontSize();
            var width = Math.Max(20, ImGui.GetContentRegionAvail().X);
            var spacing = Math.Max(0, Math.Min(12, Data.LineSpacing));
            var time = Data.Timestamps ? (string.IsNullOrEmpty(Data.TimeFormat) ? "HH:mm" : Data.TimeFormat) : null;
            var key = (width, size, (IntPtr)font.NativePtr, time, Data.IndentWrapped, spacing);

            if (!key.Equals(laidFor)) {
                laid.Clear();
                laidFor = key;
            }

            var advance = ChatFonts.Advance(font, size);
            var row = size + spacing;
            var scroll = ImGui.GetScrollY();
            atBottom = scroll >= ImGui.GetScrollMaxY() - 1;

            if (atBottom) {
                newBelow = 0;
            }

            var origin = ImGui.GetCursorScreenPos();
            var view = ImGui.GetWindowHeight();
            var y = 0f;

            for (var i = 0; i < shown.Count; i++) {
                var line = shown[i];
                var entry = Lay(line, width, advance, row, time);
                var top = y;
                y += entry.Height;

                if (top + entry.Height < scroll || top > scroll + view) {
                    continue;
                }

                DrawLine(line, entry, origin + new Vector2(0, top), width, row, font, size);
            }

            ImGui.Dummy(new Vector2(width, Math.Max(1, y)));

            var newest = shown.Count > 0 ? shown[shown.Count - 1].Id : 0;

            if (scrollToBottom || (atBottom && newest != newestDrawn)) {
                ImGui.SetScrollHereY(1);
                scrollToBottom = false;
                newBelow = 0;
            }

            newestDrawn = newest;
            DrawNewBelow(width);
        }

        private Laid Lay(ChatLine line, float width, Func<char, float> advance, float row, string time) {
            if (laid.TryGetValue(line.Id, out var entry)) {
                return entry;
            }

            pieces.Clear();
            var offset = 0;
            var indent = 0f;

            if (time != null) {
                var stamp = line.At.ToString(time) + " ";
                pieces.Add(stamp);
                offset = 1;

                foreach (var letter in stamp) {
                    indent += advance(letter);
                }
            }

            foreach (var segment in line.Segments) {
                pieces.Add(segment.Text);
            }

            if (Data.IndentWrapped && indent == 0) {
                indent = advance('m') * 1.5f;
            }

            var layout = ChatLayout.Wrap(pieces, width, advance, Data.IndentWrapped ? indent : 0);
            entry = new Laid { Layout = layout, Height = layout.Rows * row, Offset = offset };
            laid[line.Id] = entry;
            return entry;
        }

        /// <summary>
        /// The item a "[Name]" in a line names, kept with the line once it's known; null while it isn't, and for text in
        /// brackets that nobody linked, which stays plain text.
        /// </summary>
        private SharedItemInfo ItemOf(ChatSegment segment) {
            return segment.Shared ?? (segment.Shared = owner.Feed.SharedItem(segment.ItemKey));
        }

        private void DrawLine(ChatLine line, Laid entry, Vector2 at, float width, float row, ImFontPtr font, float size) {
            var drawList = ImGui.GetWindowDrawList();
            var mouse = ImGui.GetMousePos();
            var max = at + new Vector2(width, entry.Height);
            var over = ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)
                && mouse.X >= at.X && mouse.X < max.X && mouse.Y >= at.Y && mouse.Y < max.Y;

            if (line.Highlighted) {
                drawList.AddRectFilled(at, max, Data.HighlightColor);
            }

            if (over) {
                drawList.AddRectFilled(at, max, PackedColor.Pack(1f, 1f, 1f, 0.07f));
            }

            // the link or item under the mouse, every row of it underlined
            var hovered = -1;

            if (over) {
                foreach (var run in entry.Layout.Runs) {
                    var piece = run.Piece - entry.Offset;

                    if (piece < 0 || line.Segments[piece].Kind == ChatSegmentKind.Plain
                        || (line.Segments[piece].Kind == ChatSegmentKind.Item && ItemOf(line.Segments[piece])?.Item == null)) {
                        continue;
                    }

                    var min =at + new Vector2(run.X, run.Row * row);

                    if (mouse.X >= min.X && mouse.X < min.X + run.Width && mouse.Y >= min.Y && mouse.Y < min.Y + row) {
                        hovered = piece;
                    }
                }
            }

            var color = owner.ColorOf(line);
            var shadow = Data.TextShadow;

            foreach (var run in entry.Layout.Runs) {
                var piece = run.Piece - entry.Offset;
                var place = at + new Vector2(run.X, run.Row * row);
                var runColor = piece < 0 ? TimeColor(color) : SegmentColor(line.Segments[piece], color);

                if (shadow) {
                    drawList.AddText(font, size, place + Vector2.One, Shadow, run.Text);
                }

                drawList.AddText(font, size, place, runColor, run.Text);

                if (piece >= 0 && piece == hovered) {
                    var underline = (float)Math.Round(place.Y + size);
                    drawList.AddLine(new Vector2(place.X, underline), new Vector2(place.X + run.Width, underline), runColor);
                }
            }

            if (!over) {
                if (ReferenceEquals(copied, line) && ImGui.GetTime() - copiedAt < CopiedSeconds) {
                    DrawCopyButton(line, at, width, row, false);
                }

                return;
            }

            var onCopy = DrawCopyButton(line, at, width, row, true);
            var segment = hovered >= 0 ? line.Segments[hovered] : null;

            if (segment != null) {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            }

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                if (onCopy) {
                    Copy(line);
                }
                else if (segment?.Kind == ChatSegmentKind.Item) {
                    OpenItem(segment, ImGui.GetIO().KeyCtrl);
                }
                else if (segment?.Kind == ChatSegmentKind.Link) {
                    owner.ClickLink(segment, this);
                }
            }

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right)) {
                menuLine = line;
                menuSegment = segment;
                openMenu = true;
            }

            if (segment?.Kind == ChatSegmentKind.Item && !onCopy) {
                ItemTip(segment);
            }
        }

        private static uint TimeColor(uint line) {
            return PackedColor.Fade(PackedColor.Lerp(line, PackedColor.Pack(0.65f, 0.65f, 0.65f, 1), 0.7f), 0.85f);
        }

        private uint SegmentColor(ChatSegment segment, uint line) {
            if (segment.Kind != ChatSegmentKind.Item || ItemOf(segment)?.Item == null) {
                return line;
            }

            return ImGui.GetColorU32(ItemTooltip.Title);
        }

        /// <summary>
        /// The Copy button at the end of the line's first row: drawn by hand, not an ImGui button, so it can sit over text
        /// drawn straight onto the window. Returns whether the mouse is on it.
        /// </summary>
        private bool DrawCopyButton(ChatLine line, Vector2 at, float width, float row, bool live) {
            var drawList = ImGui.GetWindowDrawList();
            var result = ReferenceEquals(copied, line) && ImGui.GetTime() - copiedAt < CopiedSeconds;
            var label = !result ? "Copy" : copyWorked ? "Copied!" : "Couldn't copy";
            var padding = ImGui.GetStyle().FramePadding.X;
            var text = ImGui.CalcTextSize(label);
            var min = new Vector2(at.X + width - text.X - padding * 2, at.Y);
            var max = new Vector2(at.X + width, at.Y + Math.Max(row, text.Y));
            var mouse = ImGui.GetMousePos();
            var on = live && mouse.X >= min.X && mouse.X < max.X && mouse.Y >= min.Y && mouse.Y < max.Y;

            var fill = result
                ? (copyWorked ? PackedColor.Pack(0.15f, 0.55f, 0.25f, 1) : PackedColor.Pack(0.7f, 0.2f, 0.15f, 1))
                : ImGui.GetColorU32(on ? ImGuiCol.ButtonHovered : ImGuiCol.Button);
            drawList.AddRectFilled(min, max, fill, ImGui.GetStyle().FrameRounding);
            var textAt = new Vector2(min.X + padding, min.Y + (max.Y - min.Y - text.Y) / 2);
            drawList.AddText(textAt, ImGui.GetColorU32(ImGuiCol.Text), label);

            if (on) {
                Widgets.BeginTooltip();
                ImGui.TextUnformatted(Data.Timestamps ? "Copies this line, with its time." : "Copies this line.");
                ImGui.EndTooltip();
            }

            return on;
        }

        /// <summary>The line as text, with its time when times are shown.</summary>
        private string LineText(ChatLine line) {
            return Data.Timestamps ? $"[{line.At.ToString(laidFor.Time ?? "HH:mm")}] {line.Text}" : line.Text;
        }

        private void Copy(ChatLine line) {
            copyWorked = SystemClipboard.Set(LineText(line));
            copied = line;
            copiedAt = ImGui.GetTime();
        }

        /// <summary>How many lines came while you were scrolled up, as a button at the bottom that goes back down.</summary>
        private void DrawNewBelow(float width) {
            if (atBottom || newBelow <= 0) {
                return;
            }

            var label = newBelow == 1 ? "1 new line" : $"{newBelow} new lines";
            var drawList = ImGui.GetWindowDrawList();
            var padding = ImGui.GetStyle().FramePadding;
            var text = ImGui.CalcTextSize(label);
            var windowMin = ImGui.GetWindowPos();
            var bottom = windowMin.Y + ImGui.GetWindowHeight();
            var min = new Vector2(windowMin.X + width - text.X - padding.X * 2 - 4, bottom - text.Y - padding.Y * 2 - 4);
            var max = new Vector2(windowMin.X + width - 4, bottom - 4);
            var mouse = ImGui.GetMousePos();
            var on = ImGui.IsWindowHovered() && mouse.X >= min.X && mouse.X < max.X && mouse.Y >= min.Y && mouse.Y < max.Y;

            drawList.AddRectFilled(min, max, ImGui.GetColorU32(on ? ImGuiCol.ButtonActive : ImGuiCol.Button), 4);
            drawList.AddText(min + padding, ImGui.GetColorU32(ImGuiCol.Text), label);

            if (on && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                scrollToBottom = true;
            }
        }

        /// <summary>A linked item, as its tooltip: every stat, who linked it, and what your loot rules make of it.</summary>
        private void ItemTip(ChatSegment segment) {
            var shared = ItemOf(segment);

            if (shared?.Item == null) {
                return;
            }

            var lines = ItemLines.Build(shared.Item);
            lines.Add(new ItemLine($"Linked by {shared.Owner}. Click to appraise it; Ctrl+click in a new window.",
                ItemLineStyle.Detail));
            var rules = owner.Settings.Data.Loot.ActiveFor(CoreManager.Current.CharacterFilter.Name);
            ItemTooltip.Show(lines, iconHovered: true, rule: Loot.Decide(rules, shared.Item).Rule);
        }

        private void OpenItem(ChatSegment segment, bool newWindow) {
            var shared = ItemOf(segment);

            if (shared?.Item == null) {
                return;
            }

            Appraisal.Show(shared.Item.Guid, shared.Item, newWindow, shared.Slots, shared.Owner ?? "someone");
        }

        /// <summary>
        /// The right-click menu: for the name, link or item under the mouse, then for the line, then the window.
        /// </summary>
        private void RenderLineMenu() {
            if (openMenu) {
                openMenu = false;
                ImGui.OpenPopup("chat-line-menu");
            }

            if (!ImGui.BeginPopup("chat-line-menu")) {
                return;
            }

            try {
                RenderLineMenuItems();
            }
            finally {
                ImGui.EndPopup();
            }
        }

        private void RenderLineMenuItems() {
            var line = menuLine;
            var segment = menuSegment;

            if (segment?.Kind == ChatSegmentKind.Link && ChatMarkup.LooksLikeName(segment.LinkText)) {
                NameItems(segment.LinkText, segment.LinkId);
                ImGui.Separator();
            }
            else if (segment?.Kind == ChatSegmentKind.Item) {
                ItemMenuItems(segment);
                ImGui.Separator();
            }

            if (line != null) {
                if (ImGui.MenuItem("Copy line")) {
                    Copy(line);
                }

                if (ImGui.MenuItem("Copy all")) {
                    var all = new System.Text.StringBuilder();

                    foreach (var each in shown) {
                        all.Append(LineText(each)).Append("\r\n");
                    }

                    SystemClipboard.Set(all.ToString());
                }
                Widgets.Tip("Copies every line in this window.");

                if (segment == null && !string.IsNullOrEmpty(line.Name) && ImGui.MenuItem($"Tell {line.Name}")) {
                    StartTell(line.Name);
                }

                ImGui.Separator();
            }

            WindowMenuItems();
        }

        /// <summary>A player's name: a tell, select them, invite them, befriend them, copy the name.</summary>
        private void NameItems(string name, int guid) {
            if (ImGui.MenuItem($"Tell {name}")) {
                StartTell(name);
            }

            if (ImGui.MenuItem("Select")) {
                SocialActions.Select(unchecked((uint)guid));
            }
            Widgets.Tip("Selects them, if they're near enough for the game to know them.");

            if (ImGui.MenuItem("Invite to fellowship")) {
                FellowshipActions.Recruit(unchecked((uint)guid));
            }

            if (owner.Feed.Supports(PlayerFeed.KindFriend) && ImGui.MenuItem("Add friend")) {
                SocialActions.Send(owner.Feed, PlayerFeed.KindFriend, "add " + name);
            }

            if (ImGui.MenuItem("Copy name")) {
                SystemClipboard.Set(name);
            }
        }

        private void ItemMenuItems(ChatSegment segment) {
            if (ImGui.MenuItem("Appraise")) {
                OpenItem(segment, false);
            }

            if (ImGui.MenuItem("Appraise in new window")) {
                OpenItem(segment, true);
            }

            if (owner.Feed.Supports(PlayerFeed.KindShareAgain)) {
                if (ImGui.MenuItem("Link it again") && !owner.Feed.ShareAgain(segment.ItemKey)) {
                    PluginCore.Chat("The item couldn't be linked just now: try again in a moment.");
                }
                Widgets.Tip("Puts the same link in your chat box, to pass it on.");
            }
        }
    }
}
