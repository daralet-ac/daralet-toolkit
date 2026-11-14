using System;
using System.Numerics;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;
using UtilityBelt.Service;
using UtilityBelt.Service.Views;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A small window outside the main one that stays on screen while you play (fellowship vitals, target, hotbars...).
    /// It has no title bar: unlocked, it's dragged by its top edge (EdgeDrag) and sized by its others; locked, only with
    /// Ctrl held (CtrlDrag). Its place and visibility are saved in the settings.
    /// </summary>
    internal partial class FloatingWindow : IDisposable {
        private const float DefaultMinWidth = 300;

        private readonly Hud hud;
        private readonly Settings settings;
        private readonly string id;
        private readonly Vector2 defaultPosition;
        private readonly Action render;
        private readonly bool visibleByDefault;

        /// <summary>The size a window you size yourself starts at, or null for one that sizes itself.</summary>
        private readonly Vector2? defaultSize;

        /// <summary>What the window is showing now, or null for nothing; the func itself is null for an always-busy window.</summary>
        private readonly Func<object> inUse;

        /// <summary>Only ever shown while in use (the Corpse and Bank windows), with no choice about it.</summary>
        private readonly bool alwaysOnlyInUse;

        /// <summary>What closing does for a window about something you have open (a corpse), instead of hiding it for good.</summary>
        private readonly Action onClose;

        /// <summary>Extra items at the top of the right-click menu (a fellow's vitals), or null.</summary>
        private readonly Action menuItems;

        /// <summary>A size of its own on top of the Text size (the Appraisal windows' Item details size), or null.</summary>
        private readonly Func<float> textScale;

        /// <summary>Less room round the contents than the style's (a hotbar), or null.</summary>
        private readonly Vector2? padding;

        /// <summary>The smallest a window you size yourself may be; null for 300 by 200.</summary>
        private readonly Vector2? minSize;

        /// <summary>The saved position is set once, when the window first draws; after that ImGui keeps it.</summary>
        private bool positioned;

        /// <summary>What the window was showing when it was closed: it stays away until it's showing something else.</summary>
        private object closedFor;

        /// <summary>Set while the plugin shows or hides the hud itself, so OnHide can tell that from the close button.</summary>
        private bool settingVisibility;

        private bool bringToFront;

        /// <summary>Where in a locked window the mouse took hold of it with Ctrl, while it's being dragged; else null.</summary>
        private Vector2? ctrlGrab;

        /// <summary>The frame a window held to this one last moved it (MoveTo), when its own drag mustn't place it.</summary>
        private int ledFrame = -10;

        /// <summary>A locked window's edge taken hold of with Ctrl (CtrlResize): it resizes till the button's let go.</summary>
        private bool ctrlSizing;

        /// <summary>
        /// Set by what's in the window, every frame, while it has hold of a handle of its own that works with Ctrl held (the
        /// XP bar's width grip, XpBar.Resize.cs): a Ctrl press there isn't a move. Contents are drawn before CtrlDrag, so
        /// one set on the press is seen that frame.
        /// </summary>
        public bool CtrlHandleHeld { get; set; }

        /// <summary>
        /// Set by what's in the window, every frame, while the mouse is on (or holding) a handle of its own drawn on the
        /// window's edge (the XP bar's width grips, down its sides): the top-edge move (EdgeDrag), which shares the top
        /// corners, leaves it alone.
        /// </summary>
        public bool HandleHovered { get; set; }

        /// <summary>Set by OpenMenu: the right-click menu opens this frame, at the window's level, where it's looked for.</summary>
        private bool openMenu;

        // the style's border and padding, while this window's Begin has its own
        private float? savedBorderSize;
        private Vector2? savedPadding;

        private float minWidth = DefaultMinWidth;
        private float? pendingWidth;
        private bool disposed;

        /// <param name="defaultSize">For a window whose contents fill it: you size it yourself, starting here.</param>
        /// <param name="inUse">What the window shows now (anything that tells one thing from the next), or null for nothing.</param>
        /// <param name="alwaysOnlyInUse">Only ever shown while in use.</param>
        /// <param name="onClose">What closing it does instead of hiding it; it comes back for the next thing.</param>
        /// <param name="padding">The room round what's in it, instead of the style's.</param>
        /// <param name="textScale">A text size of its own, on top of the window's Size and the Text size.</param>
        /// <param name="minSize">For a window you size yourself, the smallest it may be (300 by 200 if not given).</param>
        public FloatingWindow(string id, string title, Settings settings, Vector2 defaultPosition, Action render,
            bool visibleByDefault = true, Vector2? defaultSize = null, Func<object> inUse = null, bool alwaysOnlyInUse = false,
            Action onClose = null, Action menuItems = null, Vector2? padding = null, Func<float> textScale = null,
            Vector2? minSize = null) {
            this.id = id;
            this.settings = settings;
            this.defaultPosition = defaultPosition;
            this.render = render;
            this.defaultSize = defaultSize;
            this.inUse = inUse;
            this.alwaysOnlyInUse = alwaysOnlyInUse;
            this.onClose = onClose;
            this.menuItems = menuItems;
            this.padding = padding;
            this.textScale = textScale;
            this.minSize = minSize;
            this.visibleByDefault = visibleByDefault;

            if (minSize.HasValue) {
                minWidth = minSize.Value.X;
            }

            // made here so a window that starts hidden is saved that way
            settings.Window(id, visibleByDefault);
            Showing.Add(this);
            settings.Swapped += Settings_Swapped;

            hud = UBService.Huds.CreateHud(title);
            hud.ShowInBar = false;
            hud.Visible = false;
            hud.OnPreRender += Hud_OnPreRender;
            hud.OnRender += Hud_OnRender;
            hud.OnHide += Hud_OnHide;

            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        private WindowPlacement Placement => settings.Window(id);

        /// <summary>
        /// Where it is, how big, locked or not, its Size and its background: its own placement, or the shared window's while
        /// it's one of those (SharedWindow). Whether it's on, and Only when needed, stay its own.
        /// </summary>
        private WindowPlacement Look => SharedLook?.Invoke() ?? Placement;

        /// <summary>The shared window's placement while this is one of its windows (SharedWindow), else null.</summary>
        public Func<WindowPlacement> SharedLook { get; set; }

        /// <summary>
        /// A box on screen the window is held to (the shared window attached to the toolbar), else null: no
        /// dragging or ImGui sizing (SharedWindow sizes the box itself), and nothing saved here.
        /// </summary>
        public Func<(Vector2 Position, Vector2 Size)?> Anchor { get; set; }

        /// <summary>
        /// The plugin window this one is held to (Anchor) while it is (the shared window on the plugin's Toolbar window),
        /// else null: a Ctrl drag of this one moves that one, which this follows, and that one doesn't snap to this.
        /// </summary>
        public Func<FloatingWindow> Follows { get; set; }

        /// <summary>Drawn above what's in the window (the shared window's tabs), or null.</summary>
        public Action Header { get; set; }

        /// <summary>
        /// Items at the top of the window's own menu items (the shared window's Share with the other panels), or null.
        /// </summary>
        public Action SharedMenu { get; set; }

        /// <summary>Whether it's one of the shared window's now (SharedWindow sets it), or null for never.</summary>
        public Func<bool> SharesNow { get; set; }

        /// <summary>Takes it out of the shared window into a window of its own (SharedWindow's Separate), or null.</summary>
        public Action PopOut { get; set; }

        /// <summary>
        /// While true the window isn't drawn, whatever its own setting (the Dungeon window while the Map window shows the
        /// dungeon map in itself); null for never.
        /// </summary>
        public Func<bool> Suppressed { get; set; }

        /// <summary>
        /// The words in its title bar (a chat window's name). UtilityBelt knows a window by the name it was made with, which
        /// stays: this is only what's shown.
        /// </summary>
        public string Title {
            get => hud.Title;
            set {
                if (hud.Title != value) {
                    hud.Title = value;
                }
            }
        }

        /// <summary>
        /// For a window the player sizes: locked, it can still be resized by its edges while Ctrl is held, as Ctrl moves it.
        /// The Vendor window's first, every window's since (the user's call, November 2026: nearly every window sized by
        /// its edges); a window that sizes itself is sized by EdgeScale instead.
        /// </summary>
        public bool CtrlResize { get; set; } = true;

        /// <summary>
        /// For a window the player sizes, the least height its contents need, measured by them as they draw (the Vendor
        /// window's single row: it follows the Text size and the title bar); null for minSize's.
        /// </summary>
        public float? MinHeight { get; set; }

        /// <summary>Whether the window may show at all (its tab's checkbox). Saved.</summary>
        public bool Visible {
            get => Placement.Visible;
            set {
                if (Placement.Visible == value) {
                    return;
                }

                Placement.Visible = value;
                settings.Changed();
            }
        }

        public bool Locked {
            get => Look.Locked;
            set {
                if (Look.Locked == value) {
                    return;
                }

                // no title bar either way (EdgeDrag), so nothing in it moves
                Look.Locked = value;
                settings.Changed();
            }
        }

        /// <summary>How big the window is drawn: 1 is normal. Whatever draws in it multiplies its own sizes by this.</summary>
        public float Scale {
            get => Look.ClampedScale;
            set {
                var scale = Math.Max(WindowPlacement.MinScale, Math.Min(WindowPlacement.MaxScale, value));

                if (Look.Scale == scale) {
                    return;
                }

                Look.Scale = scale;
                settings.Changed();
            }
        }

        /// <summary>The window's text size: its own size times the Config tab's Text size.</summary>
        private float FontScale => Scale * settings.Data.ClampedTextScale * (textScale?.Invoke() ?? 1);

        /// <summary>Shown only while in use: always for some windows, by choice (Only when needed) for the rest.</summary>
        private bool OnlyInUse => inUse != null && (alwaysOnlyInUse || Placement.OnlyInUse);

        /// <summary>Whether it's on screen now.</summary>
        public bool Shown => hud.Visible;

        /// <summary>What its close button does: Close for a window with onClose, otherwise hidden like unticking it.</summary>
        public void Dismiss() {
            if (onClose != null) {
                Close();
                return;
            }

            Visible = false;
        }

        /// <summary>Closes the window for what it's showing now (onClose); it stays away until it shows something else.</summary>
        public void Close() {
            closedFor = inUse?.Invoke();
            SetHudVisible(false);
            onClose?.Invoke();
        }

        /// <summary>Puts the window back where it starts out, for one that's been dragged off screen.</summary>
        public void ResetPosition() {
            Look.X = null;
            Look.Y = null;
            Look.Width = null;
            Look.Height = null;
            positioned = false;
            settings.Changed();
        }

        /// <summary>
        /// Focuses the window the next time it's drawn. These windows don't take focus when they appear, so as not to steal
        /// it while you play.
        /// </summary>
        public void BringToFront() {
            bringToFront = true;
        }

        /// <summary>Makes a window you size yourself this wide, from its next frame; its height stays.</summary>
        public void SetWidth(float width) {
            pendingWidth = Math.Max(100, width);
        }

        /// <summary>
        /// The window's settings, for its tab's Settings, under a <see cref="Form.Section"/> named for it: Enabled, Only when
        /// needed, Locked and Reset position in a row, then its size and background in a form. A window its tab opens and
        /// closes itself has no Enabled (showToggle false).
        /// </summary>
        public void RenderControls(string tip, bool showToggle = true) {
            var first = true;

            if (showToggle) {
                var visible = Visible;

                if (Form.FlowCheck(ref first, $"Enabled##{id}-visible", ref visible, tip)) {
                    Visible = visible;
                }
            }

            if (inUse != null && !alwaysOnlyInUse) {
                var onlyInUse = Placement.OnlyInUse;

                if (Form.FlowCheck(ref first, $"Only when needed##{id}-in-use", ref onlyInUse,
                    "Shows the window only while it has something to show, and hides it the rest of the time, instead of "
                    + "leaving it on screen saying there's nothing.")) {
                    Placement.OnlyInUse = onlyInUse;
                    settings.Changed();
                }
            }

            var locked = Locked;

            if (Form.FlowCheck(ref first, $"Locked##{id}-locked", ref locked, "Unlocked, a window is moved by dragging its "
                + "top edge (the mouse shows the four arrows there) and sized by its other edges. Locked, it stays put. "
                + "Either way, hold Ctrl and drag it from anywhere on it to move it. You can also right-click the window "
                + "to lock or unlock it.")) {
                Locked = locked;
            }

            if (Form.FlowButton(ref first, $"Reset position##{id}-reset",
                "Moves the window back to where it starts out, in case it's off screen.")) {
                ResetPosition();
            }

            if (!Form.BeginForm($"{id}-window-form")) {
                return;
            }

            Form.Label("Size", "How big the window's text and bars are drawn.");

            // leaving room for its Reset button in a narrow column
            var style = ImGui.GetStyle();
            var reset = ImGui.CalcTextSize("Reset").X + style.FramePadding.X * 2 + style.ItemSpacing.X;
            var room = ImGui.GetContentRegionAvail().X - reset;
            ImGui.SetNextItemWidth(Math.Max(ImGui.GetFontSize() * 4, Math.Min(Form.ControlWidth, room)));
            ScaleSlider($"##{id}-scale");

            Form.Label("Background", "How solid the window's background is.");
            ImGui.SetNextItemWidth(Form.ControlWidth);
            BackgroundSlider($"##{id}-background");

            ImGui.EndTable();
        }

        public void ScaleSlider(string label) {
            var percent = (int)Math.Round(Scale * 100);
            var min = (int)(WindowPlacement.MinScale * 100);
            var max = (int)(WindowPlacement.MaxScale * 100);

            if (Sliders.SliderInt(label, ref percent, min, max, "%d%%")) {
                Scale = percent / 100f;
            }
            Widgets.Tip("How big the window's text and bars are drawn. Ctrl+click to type a size. You can also right-click the window for Bigger, Smaller and Normal size.");

            ImGui.SameLine();

            if (ImGui.SmallButton($"Reset##{label}-reset")) {
                Scale = 1;
            }
            Widgets.Tip("Puts the size back to 100%.");
        }

        public void BackgroundSlider(string label) {
            var percent = (int)Math.Round(Look.BackgroundAlpha * 100);

            if (Sliders.SliderInt(label, ref percent, 0, 100, "%d%%")) {
                Look.Background = percent / 100f;
                settings.Changed();
            }
            Widgets.Tip("How solid the window's background is. At 0% there's no box at all, only what's in it. Right-click what's left to get this back.");
        }

        /// <summary>
        /// The window's own menu items, for a window that's all items (a hotbar) to put in its items' menus: its own
        /// right-click menu doesn't open over items.
        /// </summary>
        public void RenderMenuItems() {
            RenderWindowItems(Locked ? "Unlock window" : "Lock window", $"Background##{id}-item-menu-background");

            if (ImGui.MenuItem("Hide")) {
                Visible = false;
            }
        }

        /// <summary>
        /// Opens the right-click menu from something drawn in the window (a fellow, with menuItems about them): the menu
        /// itself doesn't open over items, and a popup opened inside one's ID scope wouldn't be the window's.
        /// </summary>
        public void OpenMenu() {
            openMenu = true;
        }

        private void RenderContextMenu() {
            // not over an item, whose own right-click (the game's appraisal, say) comes first
            var flags = ImGuiPopupFlags.MouseButtonRight | ImGuiPopupFlags.NoOpenOverItems;

            if (openMenu) {
                openMenu = false;
                ImGui.OpenPopup($"{id}-menu");
            }

            if (!ImGui.BeginPopupContextWindow($"{id}-menu", flags)) {
                return;
            }

            menuItems?.Invoke();
            RenderWindowItems(Locked ? "Unlock" : "Lock", $"Background##{id}-menu-background");
            ImGui.Separator();

            if (onClose != null && ImGui.MenuItem("Close")) {
                Close();
            }

            if (ImGui.MenuItem("Hide")) {
                Visible = false;
            }

            ImGui.EndPopup();
        }

        /// <summary>Lock, Bigger, Smaller, Normal size and the background slider.</summary>
        private void RenderWindowItems(string lockLabel, string backgroundId) {
            SharedMenu?.Invoke();

            if (ImGui.MenuItem(lockLabel)) {
                Locked = !Locked;
            }

            if (ImGui.MenuItem("Bigger")) {
                Scale += 0.1f;
            }

            if (ImGui.MenuItem("Smaller")) {
                Scale -= 0.1f;
            }

            if (ImGui.MenuItem($"Normal size ({Scale * 100:0}% now)")) {
                Scale = 1;
            }

            ImGui.SetNextItemWidth(ImGui.GetFontSize() * 8);
            BackgroundSlider(backgroundId);
        }

        /// <summary>
        /// The height of the title bar unlocked windows had (UpgradeFromTitleBar): a line of the window's font and the
        /// frame's padding, as ImGui made it.
        /// </summary>
        private float TitleBarHeight() {
            // the font's own size, not GetFontSize: that's whichever window is being drawn, at its own scale
            var line = ImGui.GetFont().FontSize * ImGui.GetIO().FontGlobalScale * FontScale;
            return (float)Math.Round(line + ImGui.GetStyle().FramePadding.Y * 2);
        }

        private void SetHudVisible(bool shown) {
            if (hud.Visible == shown) {
                return;
            }

            settingVisibility = true;

            try {
                hud.Visible = shown;
            }
            finally {
                settingVisibility = false;
            }
        }

        /// <summary>Every frame: shown or hidden, from the setting, what it's showing, and whether that was closed.</summary>
        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                // in case the window wasn't drawn after all, the border it took away and the theme come back
                RestoreStyle();
                UiThemeStyle.Restore();

                if (disposed) {
                    return;
                }

                // a window that's off doesn't ask what it would show (some ask the server). None is drawn until login is
                // over: what they draw reads Decal's hooks (your heading, your selection), and those read the game's
                // memory, which isn't there yet: an access violation, which no catch stops and which kills the game.
                // HideGameUi hides them all for a screenshot, keeping each one's own setting.
                if (!Visible || HideGameUi.PluginWindowsHidden || CoreManager.Current.CharacterFilter.LoginStatus != 3
                    || Suppressed?.Invoke() == true) {
                    SetHudVisible(false);
                    return;
                }

                var showing = inUse?.Invoke();

                // the thing that was closed is over: the next one shows
                if (closedFor != null && !Equals(showing, closedFor)) {
                    closedFor = null;
                }

                var hasSomething = !OnlyInUse || showing != null;
                var wasClosed = closedFor != null && Equals(showing, closedFor);
                SetHudVisible(hasSomething && !wasClosed);
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void Hud_OnPreRender(object sender, EventArgs e) {
            try {
                var flags = ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoSavedSettings;

                if (defaultSize == null) {
                    flags |= ImGuiWindowFlags.AlwaysAutoResize;
                }

                // no title bar, locked or not: an unlocked window is moved by its top edge, as the game's panels are
                // (EdgeDrag), never by ImGui
                flags |= ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoMove;
                UpgradeFromTitleBar();

                if (Locked || Anchor?.Invoke() != null) {
                    flags |= ImGuiWindowFlags.NoResize;

                    if (CanCtrlResize() && (ImGui.GetIO().KeyCtrl || ctrlSizing)) {
                        flags &= ~ImGuiWindowFlags.NoResize;
                    }
                }
                else if (edgeHover || edgeGrab.HasValue) {
                    // ImGui resizes from the top edge too: there it moves the window instead
                    flags |= ImGuiWindowFlags.NoResize;
                }

                hud.WindowSettings = flags;

                SetNextPlacement();
                SetNextBackground(flags);

                if (padding.HasValue) {
                    // read in Begin, like the border, and put back the same way
                    var style = ImGui.GetStyle();
                    savedPadding = savedPadding ?? style.WindowPadding;
                    style.WindowPadding = padding.Value;
                }

                if (bringToFront) {
                    ImGui.SetNextWindowFocus();
                    bringToFront = false;
                }

                if (!disposed) {
                    UiThemeStyle.Apply(UiThemes.Find(settings.Data.Theme));
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>
        /// The saved position every frame but while the left button is down (a drag), the saved size the first time, then a
        /// width asked for (SetWidth), then the size limits.
        /// </summary>
        private void SetNextPlacement() {
            // ImGui pulls a window back inside the screen by itself whenever the screen is smaller than where it is, and the
            // game's screen is for a moment around login: the hotbars came back in the middle, and that place was saved. So
            // only a drag moves a window: the rest of the time it's put where it was saved, which ImGui doesn't pull back.
            var anchor = Anchor?.Invoke();

            if (anchor.HasValue) {
                ImGui.SetNextWindowPos(anchor.Value.Position, ImGuiCond.Always);
                ImGui.SetNextWindowSize(anchor.Value.Size, ImGuiCond.Always);
                positioned = true;
                return;
            }

            // a Ctrl drag or a drag by the top edge moves Look itself, so the window follows it
            if (!positioned || !ImGui.IsMouseDown(ImGuiMouseButton.Left) || ctrlGrab.HasValue || edgeGrab.HasValue
                || Led) {
                ImGui.SetNextWindowPos(new Vector2(Look.X ?? defaultPosition.X, Look.Y ?? defaultPosition.Y), ImGuiCond.Always);
            }

            // the shared window's windows each take its size whenever they show, not only the first time: the last one
            // shown may have been sized since
            var shared = SharedLook?.Invoke() != null;

            if (shared && positioned && defaultSize.HasValue && !ImGui.IsMouseDown(ImGuiMouseButton.Left)
                && !pendingWidth.HasValue) {
                var size = new Vector2(Look.Width ?? defaultSize.Value.X, Look.Height ?? defaultSize.Value.Y);
                ImGui.SetNextWindowSize(size, ImGuiCond.Always);
            }

            if (!positioned) {
                if (defaultSize.HasValue) {
                    var size = new Vector2(Look.Width ?? defaultSize.Value.X, Look.Height ?? defaultSize.Value.Y);
                    ImGui.SetNextWindowSize(size, ImGuiCond.Always);
                }

                positioned = true;
            }

            if (!defaultSize.HasValue) {
                return;
            }

            if (pendingWidth.HasValue) {
                // allowed under the usual smallest, so it's exactly what was asked
                minWidth = Math.Min(minSize?.X ?? DefaultMinWidth, pendingWidth.Value);
                ImGui.SetNextWindowSize(new Vector2(pendingWidth.Value, Look.Height ?? defaultSize.Value.Y), ImGuiCond.Always);
                pendingWidth = null;
            }

            // the callback snaps a resize's edges as ImGui works the size out from the mouse, so it shows as it's dragged
            SetSizeConstraints(new Vector2(minWidth, MinHeight ?? minSize?.Y ?? 200));
        }

        /// <summary>
        /// The background's alpha. At 0 there's no box at all, border included: the style's border is zeroed for Begin and
        /// put back in Hud_OnRender (or next frame at the latest).
        /// </summary>
        private void SetNextBackground(ImGuiWindowFlags flags) {
            var alpha = Look.BackgroundAlpha;

            // with a hole the window draws its background itself, round it (DrawBackgroundRoundHole)
            if (alpha > 0 && HasHole) {
                hud.WindowSettings = flags | ImGuiWindowFlags.NoBackground;
                return;
            }

            if (alpha > 0) {
                ImGui.SetNextWindowBgAlpha(alpha);
                return;
            }

            hud.WindowSettings = flags | ImGuiWindowFlags.NoBackground;
            RestoreStyle();

            var style = ImGui.GetStyle();
            savedBorderSize = style.WindowBorderSize;
            style.WindowBorderSize = 0;
        }

        /// <summary>Puts the style's border and padding back, once the window they were changed for has begun.</summary>
        private void RestoreStyle() {
            // GetStyle only when there's something to put back: it needs an ImGui context, which may not exist yet
            if (savedBorderSize.HasValue) {
                var style = ImGui.GetStyle();
                style.WindowBorderSize = savedBorderSize.Value;
                savedBorderSize = null;
            }

            if (savedPadding.HasValue) {
                var style = ImGui.GetStyle();
                style.WindowPadding = savedPadding.Value;
                savedPadding = null;
            }
        }

        private void Hud_OnRender(object sender, EventArgs e) {
            try {
                RestoreStyle();

                // the window may still be drawn once after the plugin has shut down (the loader swapping builds)
                if (disposed) {
                    return;
                }

                ImGui.SetWindowFontScale(FontScale);
                Widgets.TextScale = settings.Data.ClampedTextScale * (textScale?.Invoke() ?? 1);
                DrawBackgroundRoundHole();


                // a drag from the game's pack let go over this window mustn't drop the item on the ground
                GameDrag.Cover();
                Header?.Invoke();

                // with Ctrl held a locked window is a handle to move it by: nothing in it takes a click meanwhile (a
                // hotbar slot uses its item on the press), drawn at full strength rather than greyed out
                var ctrlMove = CanCtrlMove() && (ImGui.GetIO().KeyCtrl || ctrlGrab.HasValue);

                if (ctrlMove) {
                    var style = ImGui.GetStyle();
                    var disabledAlpha = style.DisabledAlpha;
                    style.DisabledAlpha = 1;
                    ImGui.BeginDisabled();
                    style.DisabledAlpha = disabledAlpha;
                }

                try {
                    render();
                }
                finally {
                    if (ctrlMove) {
                        ImGui.EndDisabled();
                    }
                }

                // the game's scroll bar over the window's own, if it has one
                Scrollbars.Draw();

                // over the contents, before the top-edge move, which leaves its corner alone
                RenderCloseButton();

                // a window that sizes itself: its edges change its Size
                EdgeScale();

                if (ctrlMove) {
                    DrawCtrlMoveHighlight();
                }

                // before the menu, whose items would count as hovered
                EdgeDrag();
                RenderContextMenu();

                // held to a box on screen: nothing to save, but a Ctrl drag may move the window it's held to
                if (Anchor?.Invoke() == null) {
                    SnapWhileDragged();
                    RememberPosition();
                    TrackCtrlSizing();
                }

                CtrlDrag();

                // the move or resize cursor through a drag, which Windows and ImGui would otherwise take away
                CursorKeeper.KeepWhileHeld();
                TrackResizePress();
                NoteBox();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
            finally {
                // only the plugin's windows have the theme: other plugins' windows keep their own look
                UiThemeStyle.Restore();
            }
        }

        /// <summary>
        /// A faint wash and a frame in the theme's accent over the whole window, so it's plain which windows Ctrl can move,
        /// brighter on the one being dragged.
        /// </summary>
        private void DrawCtrlMoveHighlight() {
            var position = ImGui.GetWindowPos();
            var corner = position + ImGui.GetWindowSize();
            var accent = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonHovered];
            var dragging = ctrlGrab.HasValue;
            var fill = ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, dragging ? 0.18f : 0.08f));
            var frame = ImGui.GetColorU32(new Vector4(accent.X, accent.Y, accent.Z, dragging ? 1f : 0.7f));
            var rounding = ImGui.GetStyle().WindowRounding;

            // the window's own clip stops at its padding: the frame goes round its very edge
            var drawList = ImGui.GetWindowDrawList();
            drawList.PushClipRect(position, corner);
            drawList.AddRectFilled(position, corner, fill, rounding);
            drawList.AddRect(position + new Vector2(1, 1), corner - new Vector2(1, 1), frame, rounding, ImDrawFlags.None, 2);
            drawList.PopClipRect();
        }

        /// <summary>
        /// Any window, locked or not (the user's call, November 2026), not held to a box on screen, or one held to another
        /// plugin window (Follows): Ctrl and a drag move it, or the one it's held to.
        /// </summary>
        private bool CanCtrlMove() {
            return Anchor?.Invoke() == null || Follows?.Invoke() != null;
        }

        /// <summary>The window a Ctrl drag of this one moves: itself, or the one it's held to; null for neither.</summary>
        private FloatingWindow CtrlMover() {
            return Anchor?.Invoke() == null ? this : Follows?.Invoke();
        }

        /// <summary>A locked window the player sizes, with CtrlResize: Ctrl and a drag of an edge resize it.</summary>
        private bool CanCtrlResize() {
            return CtrlResize && defaultSize.HasValue && Locked && Anchor?.Invoke() == null;
        }

        /// <summary>A window held to this one moved it this frame or the last (MoveTo): its place isn't a drag's.</summary>
        private bool Led => ledFrame >= ImGui.GetFrameCount() - 1;

        /// <summary>
        /// Puts the window here, from a Ctrl drag of a window held to it (CtrlDrag): it's drawn there from its next Begin,
        /// though the button is down, and doesn't save where it was back meanwhile (RememberPosition).
        /// </summary>
        public void MoveTo(Vector2 position) {
            ledFrame = ImGui.GetFrameCount();

            if (Look.X != position.X || Look.Y != position.Y) {
                Look.X = position.X;
                Look.Y = position.Y;
                settings.Changed();
            }
        }

        /// <summary>
        /// Whether an edge or corner has hold of the mouse. ImGui's resize grips are its only active items while Ctrl is held
        /// (everything in the window is disabled then), so an active item at the press is one. Kept till the button's let go.
        /// </summary>
        private void TrackCtrlSizing() {
            // an unlocked one the player sizes keeps its edges with Ctrl held too: one taken hold of mustn't also move it
            var sizable = CanCtrlResize() || !Locked && defaultSize.HasValue && Anchor?.Invoke() == null;

            if (!sizable || !ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                ctrlSizing = false;
                return;
            }

            if (!ctrlSizing && ctrlGrab == null && ImGui.GetIO().KeyCtrl && ImGui.IsAnyItemActive()) {
                ctrlSizing = true;
            }
        }

        /// <summary>
        /// Moves a locked window while Ctrl and the left button are held on it. Done by hand, not by lifting NoMove: ImGui
        /// only moves a window from its empty background, and a hotbar is all slots. A window held to another plugin window
        /// (Follows: the shared window on the Toolbar window) moves that one instead, and follows it.
        /// </summary>
        private void CtrlDrag() {
            var mover = CtrlMover();

            if (!CanCtrlMove() || mover == null || !ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                ctrlGrab = null;
                return;
            }

            var box = mover == this ? (ImGui.GetWindowPos(), ImGui.GetWindowSize()) : mover.DrawnBox;

            if (box == null) {
                ctrlGrab = null;
                return;
            }

            var mouse = ImGui.GetMousePos();

            if (!ctrlGrab.HasValue) {
                var hovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows
                    | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);

                // an edge taken hold of with Ctrl resizes it (CtrlResize), and mustn't move it too
                if (!ImGui.GetIO().KeyCtrl || !hovered || !ImGui.IsMouseClicked(ImGuiMouseButton.Left) || ctrlSizing
                    || CtrlHandleHeld) {
                    return;
                }

                ctrlGrab = mouse - box.Value.Position;
            }

            // the window's drawn there from the next frame (SetNextPlacement)
            var position = mover.Snapped(mouse - ctrlGrab.Value, box.Value.Size, true);

            if (mover != this) {
                mover.MoveTo(position);
                return;
            }

            if (Look.X != position.X || Look.Y != position.Y) {
                Look.X = position.X;
                Look.Y = position.Y;
                settings.Changed();
            }
        }

        private void RememberPosition() {
            // a new position is on its way (Reset position, a window held to this one, a drag by the top edge, which moved
            // Look just before this): this frame's is the old one, and saving it put the window back every frame
            if (!positioned || Led || edgeGrab.HasValue) {
                return;
            }

            var position = ImGui.GetWindowPos();

            // only a drag moves a window (SetNextPlacement): anything else is ImGui keeping it on a smaller screen
            if (ImGui.IsMouseDown(ImGuiMouseButton.Left) && (Look.X != position.X || Look.Y != position.Y)) {
                Look.X = position.X;
                Look.Y = position.Y;
                settings.Changed();
            }

            if (!defaultSize.HasValue) {
                return;
            }

            var size = ImGui.GetWindowSize();

            if (Look.Width != size.X || Look.Height != size.Y) {
                Look.Width = size.X;
                Look.Height = size.Y;
                settings.Changed();
            }
        }

        /// <summary>The window's own close button.</summary>
        private void Hud_OnHide(object sender, EventArgs e) {
            try {
                // hidden by Core_RenderFrame or Close, not the close button
                if (settingVisibility) {
                    return;
                }

                Dismiss();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>Another settings profile: the window goes where that one has it, and starts hidden there if it should.</summary>
        private void Settings_Swapped() {
            if (disposed) {
                return;
            }

            settings.Window(id, visibleByDefault);
            positioned = false;
        }

        /// <summary>
        /// Puts the window where its placement says, size and all, on the next frame: for its placement changed behind it
        /// (a window taken out of the shared window, given the shared place as its own).
        /// </summary>
        public void PlaceAgain() {
            positioned = false;
        }

        /// <summary>The window's own placement, not the shared one it may be using now.</summary>
        public WindowPlacement OwnPlacement => Placement;

        public void Dispose() {
            disposed = true;
            Showing.Remove(this);
            settings.Swapped -= Settings_Swapped;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            hud.OnPreRender -= Hud_OnPreRender;
            hud.OnRender -= Hud_OnRender;
            hud.OnHide -= Hud_OnHide;
            hud.Visible = false;
            hud.Dispose();
            UiThemeStyle.Restore();
        }
    }
}
