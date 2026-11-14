using System;
using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// For a game panel that opens in the panel frame (Character Detail, Spell Management...), which shares the frame with
    /// every other, so has no element of its own to hide: while it's wanted, the panel is closed the moment it opens
    /// (ClientElements.OpenGamePanel, SetGamePanel), from the toolbar, the game's key or anywhere, and the plugin's window
    /// shown instead. LetOpen opens the game's own panel and leaves it alone until it's closed (for a page the plugin
    /// hasn't got). Nothing is done while Hide game UI is on.
    /// </summary>
    internal sealed class GamePanelInterceptor : IDisposable {
        /// <summary>How long after the game's panel was last seen open a LetOpen stops counting.</summary>
        private const int LetOpenGraceMs = 1000;

        private readonly string name;
        private readonly uint panel;
        private readonly Func<bool> wanted;
        private readonly FloatingWindow window;

        private bool letOpen;
        private int seenOpenAt;

        /// <param name="name">What it is, for log.txt.</param>
        /// <param name="panel">The game's panel id (ClientElements.OpenGamePanel's).</param>
        /// <param name="wanted">Whether to take the panel's place now (its Replace).</param>
        public GamePanelInterceptor(string name, uint panel, Func<bool> wanted, FloatingWindow window) {
            this.name = name;
            this.panel = panel;
            this.wanted = wanted;
            this.window = window;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        /// <summary>The game's own panel, opened and left alone until it closes.</summary>
        public void LetOpen() {
            letOpen = true;
            seenOpenAt = Environment.TickCount;
            ClientElements.SetGamePanel(panel, true);
            PluginCore.LogFile($"[game-panels] {name}: the game's panel opened from the plugin's window");
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (CoreManager.Current.CharacterFilter.LoginStatus != 3 || !wanted() || HideGameUi.On) {
                    return;
                }

                var open = ClientElements.OpenGamePanel() == panel;

                if (letOpen) {
                    if (open) {
                        seenOpenAt = Environment.TickCount;
                    }
                    else if (Environment.TickCount - seenOpenAt > LetOpenGraceMs) {
                        letOpen = false;
                    }

                    return;
                }

                if (!open) {
                    return;
                }

                ClientElements.SetGamePanel(panel, false);
                window.Visible = true;
                window.BringToFront();
                PluginCore.LogFile($"[game-panels] {name}: the game's panel 0x{panel:X} opened: the plugin's window instead");
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        public void Dispose() {
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
        }
    }
}
