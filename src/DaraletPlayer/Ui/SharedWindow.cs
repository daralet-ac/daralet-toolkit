using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The Inventory, Map, Social, Quests and Character windows sharing one place on screen, as the game's panels share the
    /// space above the toolbar: the one opened last shows, and opening it closes the others. They stay windows of their
    /// own, but take one place, size, lock, Size and background (the window "shared", FloatingWindow.SharedLook), and
    /// carry a row of tabs for each other (FloatingWindow.Header). Every one shares it but those kept separate
    /// (SharedWindowSeparate), which each window says for itself: Share with the other panels in its right-click menu
    /// (FloatingWindow.SharedMenu), and Separate at the end of the shared window's tabs. On for everyone: settings from
    /// before have SharedWindowSeparate worked out from what was ticked (SharedWindowMembers), and the shared window
    /// switched on (Upgrade).
    /// Attach to toolbar holds it right above the toolbar, as wide, wherever the toolbar is moved (FloatingWindow.Anchor),
    /// with its free edge dragged to any height (SharedWindowAttachHeight), and it takes turns with the game's panels: a game panel opening (one of the toolbar's buttons that isn't
    /// overridden, or the game's own key) closes it, and one of its windows opening closes the game's panel.
    /// </summary>
    internal class SharedWindow : IDisposable {
        /// <summary>The placement the shared windows use, in SettingsData.Windows.</summary>
        public const string PlacementId = "shared";

        private readonly Settings settings;
        private readonly List<(string Id, string Label, FloatingWindow Window)> windows;

        /// <summary>Whether each window was on last frame, to see which one was opened since.</summary>
        private readonly Dictionary<string, bool> wasOn = new Dictionary<string, bool>();

        /// <summary>The window showing now, or null.</summary>
        private string current;

        /// <summary>Whether the game's panel frame was shown last frame, or null before it's known.</summary>
        private bool? panelWasShown;

        /// <summary>The attached box last logged, so each new place goes in log.txt once.</summary>
        private static (Vector2 Position, Vector2 Size)? loggedBox;

        /// <summary>The game's panels' height at 800x600, for when the panel frame can't be measured.</summary>
        private const float DefaultPanelHeight = 372;

        /// <summary>The least an attached window can be dragged to.</summary>
        private const float MinAttachedHeight = 150;

        /// <summary>The least height of the strip along the edge that sizes an attached window.</summary>
        private const float MinGripHeight = 6;

        /// <summary>Whether it's attached above the toolbar, so it grows upward, rather than below it.</summary>
        private bool attachedAbove = true;

        /// <summary>The attached window's edge being dragged: where the mouse and the height were when it began.</summary>
        private (float MouseY, float Height)? sizing;

        private bool disposed;

        /// <summary>
        /// The plugin's Toolbar window while it stands in for the game's toolbar (Replace), else null: Attach to toolbar
        /// sits on it then (its DrawnBox), the game's own toolbar being hidden, and a Ctrl drag of the shared window moves
        /// it (FloatingWindow.Follows). Set by PlayerHud, which makes it after this.
        /// </summary>
        public Func<FloatingWindow> PluginToolbar { get; set; }

        /// <param name="more">
        /// Other windows that may share it (the game panels' that stand in for a panel the game shows in its frame: the
        /// Attributes window), by id, label and window; each must size by the player (a defaultSize), as these do.
        /// </param>
        public SharedWindow(Settings settings, FloatingWindow inventory, FloatingWindow map, FloatingWindow social,
            FloatingWindow quests, FloatingWindow character, params (string Id, string Label, FloatingWindow Window)[] more) {
            this.settings = settings;
            windows = new List<(string, string, FloatingWindow)> {
                ("carried", "Inventory", inventory),
                ("map", "Map", map),
                ("social", "Social", social),
                ("quests", "Quests", quests),
                ("character", "Character", character),
            };
            windows.AddRange(more);

            foreach (var (id, label, window) in windows) {
                window.SharedLook = () => Shares(id) ? Placement() : null;
                window.Anchor = () => Shares(id) && settings.Data.SharedWindowAttach ? PanelBox() : null;
                window.Follows = () => Shares(id) && settings.Data.SharedWindowAttach ? AttachedToolbar() : null;
                window.Header = () => {
                    RenderAttachedGrip(id);
                    RenderTabs(id);
                };
                window.SharedMenu = () => RenderShareItem(id, label);
                window.SharesNow = () => Shares(id);
                window.PopOut = () => PopOut(id, window);
            }

            Upgrade();
            settings.Swapped += Settings_Swapped;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        /// <summary>
        /// The shared place, starting where the Inventory window starts and at a size every one of them fits: unsaved, each
        /// window would open at its own default place until the shared one was dragged.
        /// </summary>
        private WindowPlacement Placement() {
            var placement = settings.Window(PlacementId, false);

            if (placement.X == null || placement.Y == null) {
                placement.X = placement.X ?? 360;
                placement.Y = placement.Y ?? 140;
                placement.Width = placement.Width ?? 520;
                placement.Height = placement.Height ?? 560;
            }

            return placement;
        }

        /// <summary>Whether a window is one of the shared window's now: every one but those kept separate.</summary>
        private bool Shares(string id) {
            var data = settings.Data;
            return data.SharedWindow && (data.SharedWindowSeparate == null || !data.SharedWindowSeparate.Contains(id));
        }

        /// <summary>
        /// Settings from before the windows chose for themselves (no SharedWindowSeparate yet): the windows that weren't
        /// ticked to share are kept separate, and the shared window is switched on, as it now is for everyone.
        /// </summary>
        private void Upgrade() {
            var data = settings.Data;

            if (data.SharedWindowSeparate != null) {
                return;
            }

            var members = data.SharedWindowMembers ?? new List<string>();
            data.SharedWindowSeparate = windows.Select(w => w.Id).Where(id => !members.Contains(id)).ToList();
            data.SharedWindow = true;
            settings.Changed();
            PluginCore.LogFile("[shared-window] on, every window sharing it but "
                + (data.SharedWindowSeparate.Count == 0 ? "none" : string.Join(", ", data.SharedWindowSeparate)));
        }

        /// <summary>
        /// A window's own Pop out (the Map window's): separated as Separate does, then made square and put in the middle
        /// of the screen (the user's call, November 2026), as big as its longer side was, but no more than 60% of the
        /// screen's shorter side, and brought to the front. Left exactly where it was, the first build's looked as if
        /// nothing had happened (the same box, the tabs gone).
        /// </summary>
        private void PopOut(string id, FloatingWindow window) {
            SetShared(id, false);

            var own = window.OwnPlacement;
            var screen = ImGui.GetIO().DisplaySize;
            var longer = Math.Max(own.Width ?? 420, own.Height ?? 420);
            var side = (float)Math.Round(Math.Max(200, Math.Min(longer, Math.Min(screen.X, screen.Y) * 0.6f)));
            own.Width = side;
            own.Height = side;
            own.X = (float)Math.Round(Math.Max(0, (screen.X - side) / 2));
            own.Y = (float)Math.Round(Math.Max(0, (screen.Y - side) / 2));
            window.PlaceAgain();
            window.Visible = true;
            window.BringToFront();
            settings.Changed();
        }

        /// <summary>Keeps a window out of the shared window, or puts it back in.</summary>
        private void SetShared(string id, bool shared) {
            var data = settings.Data;
            data.SharedWindowSeparate = data.SharedWindowSeparate ?? new List<string>();
            data.SharedWindowSeparate.Remove(id);

            if (!shared) {
                data.SharedWindowSeparate.Add(id);
            }

            // separated, it stays just where it was, taking the shared place and size as its own; put back, it shows in the
            // shared window in place of the one showing
            var window = windows.First(w => w.Id == id).Window;

            if (!shared) {
                // where it was drawn, which for one attached to the toolbar isn't the shared placement's place
                var place = Placement();
                var box = window.DrawnBox;
                var own = window.OwnPlacement;
                own.X = box?.Position.X ?? place.X;
                own.Y = box?.Position.Y ?? place.Y;
                own.Width = box?.Size.X ?? place.Width;
                own.Height = box?.Size.Y ?? place.Height;
                own.Locked = place.Locked;
                window.PlaceAgain();
            }
            else if (window.Visible) {
                Show(id);
            }

            settings.Changed();
            PluginCore.LogFile($"[shared-window] {id} {(shared ? "put back in it" : "kept out of it")}");
        }

        /// <summary>
        /// Share with the other panels, ticked while it does, in the window's own right-click menu: unticking separates it
        /// into a window of its own, ticking puts it back.
        /// </summary>
        private void RenderShareItem(string id, string label) {
            if (!settings.Data.SharedWindow) {
                return;
            }

            var shares = Shares(id);

            if (ImGui.MenuItem("Share with the other panels", null, shares)) {
                SetShared(id, !shares);
            }

            Widgets.Tip(shares
                ? $"Takes the {label} window out of the shared window, into a window of its own, placed and sized by itself."
                : $"Puts the {label} window back in the shared window, which the other panels share.");
            ImGui.Separator();
        }

        /// <summary>
        /// Every frame: the window opened last is the one that shows; a game panel opening closes it when attached.
        /// </summary>
        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (disposed || CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                    return;
                }

                FollowOpening();
                CloseForGamePanel();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>
        /// The window opened since last frame becomes the one showing; any other sharing window is closed, and attached,
        /// so is the game's panel, as the game's toolbar buttons close the one open before.
        /// </summary>
        private void FollowOpening() {
            var sharing = windows.Where(w => Shares(w.Id)).ToList();
            var opened = false;

            foreach (var (id, _, window) in sharing) {
                var on = window.Visible;

                // a window opened since last frame: the last one opened wins. On the first frame nothing was opened
                if (on && wasOn.TryGetValue(id, out var was) && !was) {
                    current = id;
                    opened = true;
                }
            }

            if (opened && settings.Data.SharedWindowAttach && ClientElements.CloseGamePanel()) {
                PluginCore.LogFile($"[shared-window] {current} opened: the game's panel closed");
            }

            if (current == null || !sharing.Any(w => w.Id == current && w.Window.Visible)) {
                current = sharing.FirstOrDefault(w => w.Window.Visible).Id;
            }

            foreach (var (id, _, window) in sharing) {
                if (id != current && window.Visible) {
                    window.Visible = false;
                }
            }

            foreach (var (id, _, window) in windows) {
                wasOn[id] = window.Visible;
            }
        }

        /// <summary>
        /// Attached, it takes the game's panels' place, so a game panel opening (one of the toolbar's buttons the plugin
        /// doesn't draw over, the skills or options, or a game key) closes it. Only the frame turning on counts: the other
        /// way round, FollowOpening closes the game's panel.
        /// </summary>
        private void CloseForGamePanel() {
            var shown = ClientElements.IsShown(ClientElements.PanelFrame);

            if (!settings.Data.SharedWindow || !settings.Data.SharedWindowAttach) {
                panelWasShown = shown;
                return;
            }

            if (shown == true && panelWasShown == false && current != null) {
                var window = windows.First(w => w.Id == current).Window;

                if (window.Visible) {
                    window.Visible = false;
                    PluginCore.LogFile($"[shared-window] a game panel opened: {current} closed");
                }
            }

            if (shown != panelWasShown && shown.HasValue) {
                PluginCore.LogFile($"[shared-window] the game's panel frame {(shown.Value ? "opened" : "closed")}");
            }

            panelWasShown = shown;
        }

        /// <summary>
        /// Right above the toolbar's frame and as wide, or below it when there's no room above (the toolbar moved to the
        /// top), or null when the toolbar can't be found. As tall as the player dragged it, or until then as the game's
        /// panels, which a new character's can be too short to use. Not the panel frame's own place: it's a floaty window
        /// the game keeps where it was last put (gmFloatyPanelUI saves its position), so it never follows the toolbar.
        /// While the plugin's Toolbar window stands in for the game's (PluginToolbar), it's that window it sits on.
        /// </summary>
        private (Vector2 Position, Vector2 Size)? PanelBox() {
            Vector2 barMin;
            Vector2 barMax;
            var plugin = PluginToolbar?.Invoke()?.DrawnBox;

            if (plugin.HasValue) {
                barMin = plugin.Value.Position;
                barMax = plugin.Value.Position + plugin.Value.Size;
            }
            else if (!ClientElements.TryGetFrameBox(ClientElements.Toolbar, out barMin, out barMax)) {
                return null;
            }

            var height = settings.Data.SharedWindowAttachHeight
                ?? (ClientElements.TryGetBox(ClientElements.PanelFrame, out var min, out var max)
                    ? max.Y - min.Y : DefaultPanelHeight);
            var roomAbove = barMin.Y;
            var roomBelow = ImGui.GetIO().DisplaySize.Y - barMax.Y;
            attachedAbove = height <= roomAbove || roomAbove >= roomBelow;
            height = Math.Max(1, Math.Min(height, attachedAbove ? roomAbove : roomBelow));

            var y = attachedAbove ? barMin.Y - height : barMax.Y;
            (Vector2 Position, Vector2 Size) box = (new Vector2(barMin.X, y), new Vector2(barMax.X - barMin.X, height));

            // logged once it settles, not every frame of a drag
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left) && loggedBox != box) {
                loggedBox = box;
                PluginCore.LogFile($"[shared-window] attached at {box.Position} size {box.Size}: the toolbar's frame "
                    + $"{barMin} to {barMax}");
            }

            return box;
        }

        /// <summary>
        /// Drags the attached window's free edge (the top, or the bottom when it's below the toolbar). By hand, not ImGui's
        /// own resizing, which would also offer the other edges and corners: the window is put back on the toolbar every
        /// frame, so dragging the edge against the toolbar would grow it without end. A double-click goes back to the game's
        /// panels' height.
        /// </summary>
        private void RenderAttachedGrip(string showing) {
            if (!Shares(showing) || !settings.Data.SharedWindowAttach || PanelBox() == null) {
                sizing = null;
                return;
            }

            var position = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            var mouse = ImGui.GetMousePos();
            var grip = Math.Max(ImGui.GetStyle().WindowPadding.Y, MinGripHeight);
            var edge = attachedAbove ? position.Y : position.Y + size.Y - 1;
            var inGrip = attachedAbove ? mouse.Y >= edge && mouse.Y < edge + grip : mouse.Y <= edge && mouse.Y > edge - grip;
            var over = inGrip && mouse.X >= position.X && mouse.X < position.X + size.X
                && ImGui.IsWindowHovered() && !ImGui.IsAnyItemHovered();

            if (over && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) {
                settings.Data.SharedWindowAttachHeight = null;
                settings.Changed();
                sizing = null;
            }
            else if (over && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                sizing = (mouse.Y, size.Y);
            }

            if (sizing.HasValue && !ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                sizing = null;
            }

            if (sizing.HasValue) {
                var moved = attachedAbove ? sizing.Value.MouseY - mouse.Y : mouse.Y - sizing.Value.MouseY;
                var height = Math.Max(MinAttachedHeight, sizing.Value.Height + moved);

                if (settings.Data.SharedWindowAttachHeight != height) {
                    settings.Data.SharedWindowAttachHeight = height;
                    settings.Changed();
                }
            }

            if (over || sizing.HasValue) {
                ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeNS);
                var color = ImGui.GetColorU32(sizing.HasValue ? ImGuiCol.ResizeGripActive : ImGuiCol.ResizeGripHovered);
                ImGui.GetWindowDrawList().AddLine(new Vector2(position.X, edge), new Vector2(position.X + size.X, edge),
                    color, 2);
            }
        }

        /// <summary>
        /// A tab for each window sharing it, at the top of the one showing: clicking one shows that one instead.
        /// </summary>
        private void RenderTabs(string showing) {
            if (!Shares(showing) || !settings.Data.SharedWindowTabs) {
                return;
            }

            var sharing = windows.Where(w => Shares(w.Id)).ToList();

            if (sharing.Count < 2) {
                return;
            }

            var first = true;

            foreach (var (id, label, window) in sharing) {
                var active = id == showing;

                // a row that wraps when the window's too narrow for them all, rather than running off its edge
                Form.Flow(ref first, ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2);

                if (active) {
                    ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive));
                }

                if (ImGui.SmallButton($"{label}##shared-tab-{id}") && !active) {
                    Show(id);
                }

                if (active) {
                    ImGui.PopStyleColor();
                }

                Widgets.Tip(active ? $"The {label} window, showing now." : $"Shows the {label} window here instead.");
            }

            // and at the end, taking the one showing out into a window of its own (its right-click menu puts it back)
            var showingLabel = sharing.First(w => w.Id == showing).Label;
            Form.Flow(ref first, ImGui.CalcTextSize("Separate").X + ImGui.GetStyle().FramePadding.X * 2);

            if (ImGui.SmallButton($"Separate##shared-separate-{showing}")) {
                SetShared(showing, false);
            }

            Widgets.Tip($"Takes the {showingLabel} window out of the shared window, into a window of its own just where "
                + "it is, placed and sized by itself. Share with the other panels in its right-click menu puts it back.");

            ImGui.Separator();
        }

        /// <summary>Shows one of the windows in it, closing the one showing now.</summary>
        private void Show(string id) {
            foreach (var (otherId, _, window) in windows) {
                if (otherId != id && Shares(otherId)) {
                    window.Visible = false;
                }
            }

            var shown = windows.First(w => w.Id == id).Window;
            shown.Visible = true;
            shown.BringToFront();
            current = id;
        }

        /// <summary>The Client UI Override section's switches: on or off, which windows, the tabs, and attached.</summary>
        public void RenderSettings() {
            var data = settings.Data;
            var first = true;

            if (Form.FlowCheck(ref first, "Shared window##shared-window", ref data.SharedWindow,
                "The Inventory, Map, Social, Quests, Character and game panel windows share one place on screen, as the "
                + "game's panels do: opening one, from the toolbar or anywhere, shows it there in place of the one before. "
                + "They share one position and size. To keep one out, use Separate at the end of the shared window's tabs, "
                + "or untick Share with the other panels in its right-click menu; tick it again to put it back.")) {
                settings.Changed();
            }

            ImGui.BeginDisabled(!data.SharedWindow);

            if (Form.FlowCheck(ref first, "Tabs##shared-window", ref data.SharedWindowTabs,
                "A row of tabs at the top of the shared window, one for each window in it, to switch between them.")) {
                settings.Changed();
            }

            if (Form.FlowCheck(ref first, "Attach to toolbar##shared-window", ref data.SharedWindowAttach,
                "Puts the shared window right above the toolbar and as wide, as tall as the game's panels, and keeps it "
                + "there, following the toolbar when it's moved: it can't be dragged. Drag its top edge to make it taller "
                + "or shorter (the game's own panels stay as they are); double-click that edge to match them again. It "
                + "takes turns with the game's panels: one opening (the skills, the options, or any toolbar button not "
                + "drawn over) closes it, and opening one of its windows closes the game's panel.")) {
                settings.Changed();
            }

            ImGui.EndDisabled();
        }

        private void Settings_Swapped() {
            wasOn.Clear();
            current = null;
            Upgrade();
        }

        /// <summary>The plugin's Toolbar window while the shared window sits on it (PanelBox), else null.</summary>
        private FloatingWindow AttachedToolbar() {
            var toolbar = PluginToolbar?.Invoke();
            return toolbar?.DrawnBox != null ? toolbar : null;
        }

        public void Dispose() {
            disposed = true;
            settings.Swapped -= Settings_Swapped;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;

            foreach (var (_, _, window) in windows) {
                window.SharedLook = null;
                window.Anchor = null;
                window.Follows = null;
                window.Header = null;
                window.SharesNow = null;
                window.PopOut = null;
            }
        }
    }
}
