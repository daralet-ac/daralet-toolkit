using System;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// One of the game's panels a plugin window can take the place of (the Vitals panel, say): its window
    /// (a FloatingWindow, hidden to start), Replace, which hides the game's own elements while it's on
    /// (GameElementHider), and a Look, Retail (the client's own art, from its LayoutDescs, in Resources/ui) or Modern
    /// (plain ImGui in the plugin's theme). Its choices are SettingsData.GamePanel(key). Replace and Look sit in the
    /// Config tab's Client UI Override (GamePanels), with the window's own settings under them.
    /// </summary>
    internal abstract class GamePanel : IDisposable {
        private static readonly (string Value, string Label)[] Looks = {
            (GamePanelOptions.Retail, "Retail"),
            (GamePanelOptions.Modern, "Modern"),
        };

        protected readonly Settings settings;
        private readonly GameElementHider hider;

        /// <summary>The window's side grips, made the first time WidthGrip is called.</summary>
        private SideGrips sideGrips;

        /// <param name="key">The window's id and the panel's key in settings.json.</param>
        /// <param name="name">What players see it called: the window's title and its checkbox.</param>
        /// <param name="elements">The game's elements it takes the place of, hidden while Replace is on.</param>
        /// <param name="inUse">
        /// What the window has to show now, or null for nothing (the power bar, out of melee and missile combat), for Only
        /// when needed; null for a window always in use.
        /// </param>
        /// <param name="hideWhen">
        /// While Replace is on, whether the game's elements are hidden now (only in melee and missile combat, for the power
        /// bar); null for always.
        /// </param>
        /// <param name="checkEvery">How often the game's are looked at again (GameElementHider).</param>
        /// <param name="showAgain">Whether one of the game's is shown again (GameElementHider).</param>
        /// <param name="defaultSize">
        /// For a window the player sizes (one that can be in the shared window, whose members all are), its size to start;
        /// null for one that sizes itself.
        /// </param>
        /// <param name="alwaysOnlyInUse">Only ever shown while in use (FloatingWindow's), with no Only when needed.</param>
        /// <param name="onClose">What closing the window does instead of hiding it (FloatingWindow's).</param>
        /// <param name="minSize">For a window the player sizes, the smallest it may be (FloatingWindow's: 300 by 200 if
        /// not given).</param>
        protected GamePanel(string key, string name, Settings settings, uint[] elements, Vector2 defaultPosition,
            Func<object> inUse = null, Func<bool> hideWhen = null, TimeSpan? checkEvery = null,
            Func<uint, bool, bool> showAgain = null, Vector2? defaultSize = null, bool alwaysOnlyInUse = false,
            Action onClose = null, Vector2? minSize = null) {
            Key = key;
            Name = name;
            this.settings = settings;
            Window = new FloatingWindow(key, name, settings, defaultPosition, RenderWindow, visibleByDefault: false,
                menuItems: RenderMenu, inUse: inUse, defaultSize: defaultSize, alwaysOnlyInUse: alwaysOnlyInUse,
                onClose: onClose, minSize: minSize);
            hider = new GameElementHider(name, elements, () => Options.Replace && (hideWhen?.Invoke() ?? true), checkEvery,
                showAgain);
        }

        public string Key { get; }
        public string Name { get; }
        public FloatingWindow Window { get; }

        public GamePanelOptions Options => settings.Data.GamePanel(Key);

        /// <summary>The client's own art, rather than plain ImGui.</summary>
        public bool Retail => Options.IsRetail;

        /// <summary>What Replace does, for its tooltip: which of the game's panels goes.</summary>
        protected abstract string ReplaceTip { get; }

        /// <summary>What the window shows, for its Enabled tooltip.</summary>
        protected abstract string WindowTip { get; }

        /// <summary>What's in the window, in the look chosen (Retail).</summary>
        protected abstract void RenderContents();

        /// <summary>The panel's own settings, under its window's.</summary>
        protected virtual void RenderOwnSettings() {
        }

        /// <summary>The panel's own items in its window's right-click menu, after the look.</summary>
        protected virtual void RenderOwnMenu() {
        }

        private void RenderWindow() {
            RenderContents();
        }

        /// <summary>
        /// Makes the window wider or narrower by dragging its left or right side (SideGrips): the window sizes itself to
        /// what's in it, so the panel changes what's in it by how far the side moved. Called as part of the contents each
        /// frame; works on an unlocked window, or a locked one with Ctrl held. Returns how far it was dragged this frame,
        /// in pixels at 100% text (0 for none).
        /// </summary>
        protected float WidthGrip(string tip) {
            sideGrips = sideGrips ?? new SideGrips(Window);
            return sideGrips.Render(tip);
        }

        private void RenderMenu() {
            var options = Options;

            foreach (var look in Looks) {
                if (ImGui.MenuItem(look.Label + " look", null, options.IsRetail == (look.Value == GamePanelOptions.Retail))) {
                    options.Look = look.Value;
                    settings.Changed();
                }
            }

            RenderOwnMenu();
            ImGui.Separator();
        }

        /// <summary>
        /// Where in the main window the panel's settings are (GamePanels.VitalsTab, CombatBarsTab), or null for Client UI
        /// Override's Game panels.
        /// </summary>
        public virtual string SettingsTab => null;

        /// <summary>Replace, as a checkbox in Client UI Override's row of them. Ticking it shows the window too.</summary>
        public void RenderToggle(ref bool first) {
            RenderReplace(ref first, Name, "client-ui");
        }

        /// <summary>Replace, under a label of the caller's: one setting, shown in Client UI Override and the settings.</summary>
        private void RenderReplace(ref bool first, string label, string where) {
            var options = Options;
            var replace = options.Replace;
            var settingsAre = SettingsTab == null ? "under Game panels, in Client UI Override"
                : $"on the {SettingsTab}";

            if (Form.FlowCheck(ref first, $"{label}##game-panel-{Key}-{where}", ref replace, ReplaceTip + " The game's own "
                + "is shown again when this is unticked, when you log out, and when the plugin stops. Its look and the "
                + $"window's settings are {settingsAre}; this checkbox is in the Config tab's Client UI Override too.")) {
                options.Replace = replace;

                if (replace) {
                    Window.Visible = true;
                }

                settings.Changed();
            }
        }

        /// <summary>A section of its own: Replace, the look, the window's controls, then the panel's own settings.</summary>
        public void RenderSettings() {
            Form.Section(Name);
            var first = true;
            RenderReplace(ref first, "Replace the game's", "settings");

            if (Form.BeginForm($"game-panel-{Key}-look")) {
                Form.Label("Look", "Retail draws it with the game's own art; Modern with plain bars and buttons in the "
                    + "plugin's theme (the Theme section).");
                var options = Options;
                var look = options.IsRetail ? GamePanelOptions.Retail : GamePanelOptions.Modern;

                if (Form.Segmented($"game-panel-{Key}-look", Looks, ref look)) {
                    options.Look = look;
                    settings.Changed();
                }

                ImGui.EndTable();
            }

            Window.RenderControls(WindowTip);
            RenderOwnSettings();
        }

        public virtual void Dispose() {
            // the game's panel back first, whatever fails after
            try {
                hider.Dispose();
            }
            finally {
                Window.Dispose();
            }
        }
    }
}
