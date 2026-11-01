using System;
using System.Collections.Generic;
using System.Numerics;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The plugin's chat windows: the main one, with a tab for each of the four others, which pop out into windows of their
    /// own. Every line of chat is read from Decal's ChatBoxMessage and kept in one history the windows each show part of
    /// (ChatView), by the game's own chat categories. A line is only added on the next RenderFrame, once every handler has
    /// had it: other plugins' handlers can run after this one (Daralet Admin's, hiding its DAA2 replies), so a line eaten
    /// by anyone (the feed's replies) isn't shown here either, but for combat the Combat log's Hide in chat keeps out of
    /// the game's chat, which still shows here where combat is picked. With Hide game chat (Client UI
    /// Override) the game's chat windows are hidden and these used instead (ChatWindows.GameChat.cs); links in them, and
    /// items linked in chat, are in ChatWindows.Links.cs.
    /// </summary>
    internal sealed partial class ChatWindows : IDisposable {
        private static readonly TimeSpan FlushEvery = TimeSpan.FromSeconds(1);

        private const int LinksLogged = 40;

        /// <summary>The one running, for item menus elsewhere (Link in chat), or null.</summary>
        public static ChatWindows Current { get; private set; }

        private readonly PlayerFeed feed;
        private readonly Settings settings;
        private readonly ItemIcons icons;
        private readonly List<ChatLine> lines = new List<ChatLine>();

        /// <summary>Lines heard since the last frame, with Decal's event for each: whether it was eaten isn't settled yet.</summary>
        private readonly List<(ChatTextInterceptEventArgs Event, string Text, DateTime At, bool Combat)> heard =
            new List<(ChatTextInterceptEventArgs, string, DateTime, bool)>();

        private readonly ChatView[] views = new ChatView[ChatSettings.WindowCount];
        private readonly FloatingWindow[] windows = new FloatingWindow[ChatSettings.WindowCount];
        private readonly CombatLogFile file;

        private int linksLogged;
        private long nextId = 1;
        private DateTime flushAt;
        private string highlightSource;
        private List<string> highlightWords = new List<string>();

        /// <summary>The tab the main window opens on its next frame, or -1.</summary>
        private int selectTab = -1;

        /// <summary>The tab whose right-click menu is open.</summary>
        private int menuTab;
        private bool openTabMenu;

        private bool disposed;

        public ChatWindows(PlayerFeed feed, Settings settings, ItemIcons icons) {
            this.feed = feed;
            this.settings = settings;
            this.icons = icons;
            file = new CombatLogFile(DataFolder.For("Chat Logs"), PluginCore.Log, "chat log");

            for (var i = 0; i < views.Length; i++) {
                views[i] = new ChatView(this, i);
            }

            windows[0] = new FloatingWindow("chat", "Daralet Chat", settings, new Vector2(10, 420), RenderMain,
                defaultSize: new Vector2(540, 240), inUse: () => Data.On ? (object)true : null, alwaysOnlyInUse: true,
                menuItems: MainMenuItems, minSize: new Vector2(220, 110));
            windows[0].Title = "Chat";

            for (var i = 1; i < windows.Length; i++) {
                var number = i;
                windows[i] = new FloatingWindow($"chat-{i}", $"Daralet Chat {i}", settings,
                    new Vector2(560, 120 + i * 40), () => RenderPopped(number), defaultSize: new Vector2(420, 200),
                    inUse: () => Data.On && Data.Window(number).PoppedOut ? (object)true : null, alwaysOnlyInUse: true,
                    onClose: () => SetPoppedOut(number, false), minSize: new Vector2(180, 90));
                windows[i].Title = Data.Window(i).Name;
            }

            // ImGui opens a tab bar on its first tab: the one saved is opened instead
            selectTab = Data.MainTab;
            Current = this;
            feed.ItemShared += Feed_ItemShared;
            settings.Swapped += Settings_Swapped;
            // after the plugin's other chat handlers (made before this), so a line one adds again keeps its place
            CoreManager.Current.ChatBoxMessage += Core_ChatBoxMessage;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        public ChatSettings Data => settings.Data.Chat;
        public Settings Settings => settings;
        public PlayerFeed Feed => feed;
        public ItemIcons Icons => icons;
        public IReadOnlyList<ChatLine> Lines => lines;
        public string SaveFolder => file.Folder;

        /// <summary>The oldest line kept: anything older is gone from every window.</summary>
        public long OldestId => lines.Count > 0 ? lines[0].Id : nextId;

        /// <summary>Who last sent you a tell, for Reply.</summary>
        public string ReplyTo { get; private set; }

        /// <summary>
        /// The window (FloatingWindow) a chat window is drawn in: its own when popped out, else the main one.
        /// </summary>
        public FloatingWindow HostOf(int index) {
            return index > 0 && Data.Window(index).PoppedOut ? windows[index] : windows[0];
        }

        public FloatingWindow Window(int index) => windows[index];

        public ChatView View(int index) => views[index];

        /// <summary>Whether a chat window can be seen now: its own window shown, or its tab open in the main one.</summary>
        public bool OnScreen(int index) {
            if (index > 0 && Data.Window(index).PoppedOut) {
                return windows[index].Shown;
            }

            return windows[0].Shown && Data.MainTab == index;
        }

        /// <summary>Every window shows its lines again from the history, for a change to what they show.</summary>
        public void Refilter() {
            foreach (var view in views) {
                view.Refilter();
            }
        }

        /// <summary>Pops a chat window out of the main one into a window of its own, or puts it back as a tab.</summary>
        public void SetPoppedOut(int index, bool poppedOut) {
            if (index == 0 || Data.Window(index).PoppedOut == poppedOut) {
                return;
            }

            Data.Window(index).PoppedOut = poppedOut;

            if (poppedOut) {
                windows[index].Visible = true;
                windows[index].BringToFront();

                if (Data.MainTab == index) {
                    Data.MainTab = 0;
                }
            }
            else {
                selectTab = index;
            }

            settings.Changed();
        }

        /// <summary>
        /// Brings a chat window to where it can be typed in, and starts typing there: the main window shown, and the window's
        /// tab opened, or its own window shown.
        /// </summary>
        public void Focus(int index, string insert = null) {
            if (!Data.On) {
                return;
            }

            windows[0].Visible = true;

            if (index > 0 && Data.Window(index).PoppedOut) {
                windows[index].Visible = true;
                windows[index].BringToFront();
            }
            else {
                selectTab = index;
                windows[0].BringToFront();
            }

            views[index].Focus(insert);
        }

        /// <summary>Opens a chat window's options in it: its window shown, or its tab opened.</summary>
        public void ShowOptions(int index) {
            Focus(index);
            views[index].OpenOptions();
        }

        /// <summary>
        /// The chat window typing goes to now: the last typed in that's still on screen, else the main window's tab.
        /// </summary>
        public int TypingWindow() {
            var last = ChatView.LastTyped;
            return last >= 0 && last < views.Length && OnScreen(last) ? last : Data.MainTab;
        }

        /// <summary>Sends a line, as typed in the game's chat bar (a command, or something said).</summary>
        public void Send(string command) {
            if (disposed || string.IsNullOrWhiteSpace(command)) {
                return;
            }

            CoreManager.Current.Actions.InvokeChatParser(command);
        }

        /// <summary>
        /// A line's colour: its channel's for a chat channel's line, else its chat type's, either as picked in the Chat tab or
        /// as it starts, near the game's own.
        /// </summary>
        public uint ColorOf(ChatLine line) {
            var category = ChatCategoryInfo.For(line.Category);

            if (category.Channel) {
                return ColorFor(category.Key, category.Color);
            }

            var type = ChatTypeInfo.For(line.Type);
            return ColorFor(type.Key, type.Color);
        }

        public uint ColorFor(string key, uint start) {
            return Data.Colors != null && Data.Colors.TryGetValue(key, out var color) ? color : start;
        }

        /// <summary>The pixel size a chat window's text is drawn at: its own text size, times its window's Size.</summary>
        public int PixelSize(int index) {
            var host = index > 0 && Data.Window(index).PoppedOut ? index : 0;
            var size = Data.Window(host).ClampedFontSize * windows[host].Scale;
            return (int)Math.Round(Math.Max(ChatWindowSettings.MinFontSize, size));
        }

        private void Core_ChatBoxMessage(object sender, ChatTextInterceptEventArgs e) {
            try {
                // Hide in chat keeps combat out of the game's chat, not out of a chat window set to show it
                var hiddenCombat = ReferenceEquals(e, CombatWatch.LastHidden);

                if (disposed || (e.Eat && !hiddenCombat)) {
                    return;
                }

                var text = e.Text ?? "";

                if (text.Trim().Length == 0) {
                    return;
                }

                heard.Add((e, text, DateTime.Now, hiddenCombat));
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>The lines heard since the last frame, but those some handler has eaten since.</summary>
        private void AddHeard() {
            try {
                var you = CoreManager.Current.CharacterFilter.Name;

                foreach (var line in heard) {
                    if (line.Event.Eat && !line.Combat) {
                        continue;
                    }

                    var read = ChatLine.Read(nextId++, line.Text, line.Event.Color, line.At, you);

                    // a kill's line may be of another chat type: hidden as combat, it only shows where combat does
                    if (line.Combat && read.Category != ChatCategory.Magic) {
                        read.Category = ChatCategory.Combat;
                    }

                    Add(read);
                }
            }
            finally {
                heard.Clear();
            }
        }

        private void Add(ChatLine line) {
            line.Highlighted = !line.TellFromYou && ChatHighlights.AnyIn(line.Text, HighlightWords());

            if (line.TellToYou && !string.IsNullOrEmpty(line.Name)) {
                ReplyTo = line.Name;
            }

            lines.Add(line);
            var keep = Data.ClampedHistory;

            if (lines.Count > keep) {
                lines.RemoveRange(0, lines.Count - keep);
            }

            foreach (var view in views) {
                view.Added(line);
            }

            LogLink(line);

            // asked now, not when it's drawn, so the line gets the item linked when it was said
            if (Data.On) {
                foreach (var segment in line.Segments) {
                    if (segment.Kind == ChatSegmentKind.Item) {
                        feed.RefreshSharedItem(segment.ItemKey);
                    }
                }
            }

            if (Data.Save) {
                var category = ChatCategoryInfo.For(line.Category).Label;
                file.Add(CoreManager.Current.CharacterFilter.Name, line.At, $"[{line.At:HH:mm:ss}] [{category}] {line.Text}");
            }
        }

        /// <summary>
        /// A line with brackets in what was said goes in log.txt, as Decal had it and with the keys it was read as: `[item-link]`
        /// lines, the first few a session.
        /// </summary>
        private void LogLink(ChatLine line) {
            var quote = line.Text.IndexOf('"');

            if (linksLogged >= LinksLogged || quote < 0 || line.Text.IndexOf('[', quote) < 0) {
                return;
            }

            linksLogged++;
            var keys = new List<string>();

            foreach (var segment in line.Segments) {
                if (segment.Kind == ChatSegmentKind.Item) {
                    keys.Add(segment.ItemKey);
                }
            }

            PluginCore.LogFile($"[item-link] type {line.Type}, keys [{string.Join(" ", keys)}]: {line.Raw}");
        }

        /// <summary>The highlight words, and your name with HighlightName, worked out again only when they change.</summary>
        private List<string> HighlightWords() {
            var name = Data.HighlightName ? (CoreManager.Current.CharacterFilter.Name ?? "").TrimStart('+') : "";
            var source = Data.Highlights + "|" + name;

            if (source != highlightSource) {
                highlightSource = source;
                highlightWords = ChatHighlights.Words(Data.Highlights);

                if (name.Length > 0) {
                    highlightWords.Add(name);
                }
            }

            return highlightWords;
        }

        /// <summary>Forgets every line, in every window.</summary>
        public void ClearAll() {
            lines.Clear();
            Refilter();
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (disposed) {
                    return;
                }

                AddHeard();

                // outside ImGui's frame, where a new font can be added to its font picture
                ChatFonts.MakeWanted();

                if (DateTime.UtcNow >= flushAt) {
                    flushAt = DateTime.UtcNow + FlushEvery;
                    file.Flush();
                }

                for (var i = 1; i < windows.Length; i++) {
                    windows[i].Title = Data.Window(i).Name;
                }

                StepGameChat();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>The main window: a tab for itself and each window not popped out, then the open tab's chat.</summary>
        private void RenderMain() {
            var pushed = ChatView.BeginFont(this, 0);

            try {
                var shown = RenderTabs();
                views[shown].Render(windows[0]);
            }
            finally {
                ChatView.EndFont(pushed);
            }
        }

        private void RenderPopped(int index) {
            var pushed = ChatView.BeginFont(this, index);

            try {
                views[index].Render(windows[index]);
            }
            finally {
                ChatView.EndFont(pushed);
            }
        }

        /// <summary>The main window's tabs. Returns the chat window to show under them.</summary>
        private int RenderTabs() {
            var open = Data.MainTab;

            if (open < 0 || open >= views.Length || (open > 0 && Data.Window(open).PoppedOut)) {
                open = 0;
            }

            var tabs = 0;

            for (var i = 0; i < views.Length; i++) {
                if (i == 0 || !Data.Window(i).PoppedOut) {
                    tabs++;
                }
            }

            // a main window with nothing docked in it has no tabs to show
            if (tabs <= 1) {
                SetMainTab(0);
                return 0;
            }

            var flags = ImGuiTabBarFlags.FittingPolicyScroll | ImGuiTabBarFlags.NoTooltip;

            if (!ImGui.BeginTabBar("chat-tabs", flags)) {
                return open;
            }

            try {
                open = RenderTabItems(open);
            }
            finally {
                ImGui.EndTabBar();
            }

            selectTab = -1;
            SetMainTab(open);

            // opened at the window's level, where RenderTabMenu looks for it: the tab bar has an ID of its own
            if (openTabMenu) {
                openTabMenu = false;
                ImGui.OpenPopup("chat-tab-menu");
            }

            RenderTabMenu();
            return open;
        }

        /// <summary>A tab for the main window and each window not popped out. Returns the one open.</summary>
        private int RenderTabItems(int open) {
            for (var i = 0; i < views.Length; i++) {
                if (i > 0 && Data.Window(i).PoppedOut) {
                    continue;
                }

                var view = views[i];
                var window = Data.Window(i);
                var label = view.Unread > 0 && i != open
                    ? $"{window.Name} ({view.Unread})###chat-tab-{i}"
                    : $"{window.Name}###chat-tab-{i}";
                var flashing = Data.FlashUnread && window.Flash && view.Unread > 0 && i != open;

                if (flashing) {
                    ImGui.PushStyleColor(ImGuiCol.Tab, FlashColor(view.UnreadHighlighted));
                    ImGui.PushStyleColor(ImGuiCol.TabHovered, FlashColor(view.UnreadHighlighted, 0.25f));
                }

                // no tooltip yet: one drawn now would be the last item, and the right-click would be its
                var selected = Widgets.TabItem(label, null, selectTab == i);

                if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) {
                    menuTab = i;
                    openTabMenu = true;
                }

                Widgets.Tip($"{window.Name}: right-click for its options, or to pop it out into a window of its own.");

                if (selected) {
                    open = i;
                    ImGui.EndTabItem();
                }

                if (flashing) {
                    ImGui.PopStyleColor(2);
                }
            }

            return open;
        }

        private void SetMainTab(int tab) {
            if (Data.MainTab != tab) {
                Data.MainTab = tab;
                settings.Changed();
            }
        }

        /// <summary>A tab's colour while it flashes: towards gold, or red for a line that mentions you, and back.</summary>
        private static Vector4 FlashColor(bool highlighted, float lighter = 0) {
            var normal = ImGui.GetStyle().Colors[(int)ImGuiCol.Tab];
            var lit = highlighted ? new Vector4(0.85f, 0.25f, 0.2f, 1) : new Vector4(0.85f, 0.6f, 0.15f, 1);
            var pulse = (float)(0.5 + 0.5 * Math.Sin(ImGui.GetTime() * Math.PI * 1.6)) * 0.85f + lighter;
            return Vector4.Lerp(normal, lit, Math.Min(1, pulse));
        }

        private void RenderTabMenu() {
            if (!ImGui.BeginPopup("chat-tab-menu")) {
                return;
            }

            try {
                RenderTabMenuItems();
            }
            finally {
                ImGui.EndPopup();
            }
        }

        private void RenderTabMenuItems() {
            var view = views[menuTab];

            if (ImGui.MenuItem("Options...")) {
                view.OpenOptions();
                selectTab = menuTab;
            }

            if (menuTab > 0 && ImGui.MenuItem("Pop out")) {
                SetPoppedOut(menuTab, true);
            }
            Widgets.Tip("Into a window of its own, which you can put anywhere. Put it back from its options, or its own "
                + "window's right-click menu.");

            if (view.Unread > 0 && ImGui.MenuItem("Mark as read")) {
                view.MarkRead();
            }

            if (ImGui.MenuItem("Clear")) {
                view.Clear();
            }
            Widgets.Tip("Empties this tab. Other tabs keep their lines.");
        }

        /// <summary>The main window's right-click menu, over its tabs.</summary>
        private void MainMenuItems() {
            if (ImGui.MenuItem("Chat options...")) {
                views[Data.MainTab].OpenOptions();
            }

            ImGui.Separator();
        }

        private void Settings_Swapped() {
            if (disposed) {
                return;
            }

            Refilter();
            selectTab = Data.MainTab;
        }

        public void Dispose() {
            disposed = true;

            if (Current == this) {
                Current = null;
            }

            feed.ItemShared -= Feed_ItemShared;
            settings.Swapped -= Settings_Swapped;
            CoreManager.Current.ChatBoxMessage -= Core_ChatBoxMessage;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;

            try {
                AddHeard();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }

            // the game's chat back first, whatever fails after
            try {
                RestoreGameChat();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }

            foreach (var window in windows) {
                try {
                    window?.Dispose();
                }
                catch (Exception ex) {
                    PluginCore.Log(ex);
                }
            }

            file.Flush();
        }
    }
}
