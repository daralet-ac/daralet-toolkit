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
    /// carry a row of tabs for each other (FloatingWindow.Header). Which ones share is the player's (SharedWindowMembers),
    /// from the Config tab's Client UI Override section.
    /// Attach to toolbar holds it right above the toolbar, as wide, wherever the toolbar is moved (FloatingWindow.Anchor),
    /// and it takes turns with the game's panels: a game panel opening (one of the toolbar's buttons that isn't
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

        private bool disposed;

        public SharedWindow(Settings settings, FloatingWindow inventory, FloatingWindow map, FloatingWindow social,
            FloatingWindow quests, FloatingWindow character) {
            this.settings = settings;
            windows = new List<(string, string, FloatingWindow)> {
                ("carried", "Inventory", inventory),
                ("map", "Map", map),
                ("social", "Social", social),
                ("quests", "Quests", quests),
                ("character", "Character", character),
            };

            foreach (var (id, _, window) in windows) {
                window.SharedLook = () => Shares(id) ? Placement() : null;
                window.Anchor = () => Shares(id) && settings.Data.SharedWindowAttach ? PanelBox() : null;
                window.Header = () => RenderTabs(id);
            }

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

        /// <summary>Whether a window is one of the shared window's now.</summary>
        private bool Shares(string id) {
            var data = settings.Data;
            return data.SharedWindow && data.SharedWindowMembers != null && data.SharedWindowMembers.Contains(id);
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
        /// Right above the toolbar's frame and as wide, as tall as the game's panels, or below it when there's no room
        /// above (the toolbar moved to the top), or null when the toolbar can't be found. Not the panel frame's own place:
        /// it's a floaty window the game keeps where it was last put (gmFloatyPanelUI saves its position), so it never
        /// follows the toolbar.
        /// </summary>
        private static (Vector2 Position, Vector2 Size)? PanelBox() {
            if (!ClientElements.TryGetFrameBox(ClientElements.Toolbar, out var barMin, out var barMax)) {
                return null;
            }

            var height = ClientElements.TryGetBox(ClientElements.PanelFrame, out var min, out var max)
                ? max.Y - min.Y : DefaultPanelHeight;
            var y = barMin.Y - height >= 0 ? barMin.Y - height : barMax.Y;
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

            foreach (var (id, label, window) in sharing) {
                var active = id == showing;

                if (id != sharing[0].Id) {
                    ImGui.SameLine(0, ImGui.GetStyle().ItemSpacing.X / 2);
                }

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
                "The windows ticked beside it share one place on screen, as the game's panels do: opening one, from the "
                + "toolbar or anywhere, shows it there in place of the one before. They share one position and size.")) {
                settings.Changed();
            }

            ImGui.BeginDisabled(!data.SharedWindow);

            foreach (var (id, label, _) in windows) {
                var shares = data.SharedWindowMembers.Contains(id);

                if (Form.FlowCheck(ref first, $"{label}##shared-member-{id}", ref shares,
                    $"The {label} window is one of the shared window's. Unticked, it's a window of its own.")) {
                    data.SharedWindowMembers.Remove(id);

                    if (shares) {
                        data.SharedWindowMembers.Add(id);
                    }

                    settings.Changed();
                }
            }

            if (Form.FlowCheck(ref first, "Tabs##shared-window", ref data.SharedWindowTabs,
                "A row of tabs at the top of the shared window, one for each window in it, to switch between them.")) {
                settings.Changed();
            }

            if (Form.FlowCheck(ref first, "Attach to toolbar##shared-window", ref data.SharedWindowAttach,
                "Puts the shared window right above the toolbar and as wide, as tall as the game's panels, and keeps it "
                + "there, following the toolbar when it's moved: it can't be dragged or sized. It takes turns with the "
                + "game's panels: one opening (the skills, the options, or any toolbar button not drawn over) closes it, "
                + "and opening one of its windows closes the game's panel.")) {
                settings.Changed();
            }

            ImGui.EndDisabled();
        }

        private void Settings_Swapped() {
            wasOn.Clear();
            current = null;
        }

        public void Dispose() {
            disposed = true;
            settings.Swapped -= Settings_Swapped;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;

            foreach (var (_, _, window) in windows) {
                window.SharedLook = null;
                window.Anchor = null;
                window.Header = null;
            }
        }
    }
}
