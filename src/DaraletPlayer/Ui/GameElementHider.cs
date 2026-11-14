using System;
using System.Collections.Generic;
using System.Linq;
using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Hides some of the game's own UI elements while a plugin window takes their place (GamePanel), as Hide game chat does
    /// for the chat windows: UIElement::SetVisible (ClientElements.SetVisible) by element id, looked at again once a second
    /// in case the game shows one itself, and those that were showing shown again when it's no longer wanted, at logout
    /// (the game makes its UI afresh for the next login, so nothing is called then), and when the plugin shuts down or
    /// reloads. Nothing of it is saved. While Hide game UI is on it leaves everything to that, which hides and shows the
    /// whole UI itself and puts back only what was showing when it started.
    /// Two hiders may share an element (the Combat floaty holds both the power bar and the spell bar, each its own
    /// panel), so whether an element was showing is kept per element for all of them (WasShown), and one isn't shown again
    /// while another hider still wants it hidden: the power bar handing it to the spell bar at a change to magic doesn't
    /// flash it, and the spell bar knows to put it back after.
    /// </summary>
    internal sealed class GameElementHider : IDisposable {
        private static readonly TimeSpan DefaultCheckEvery = TimeSpan.FromSeconds(1);

        /// <summary>Every hider, to see whether another still wants an element hidden.</summary>
        private static readonly List<GameElementHider> All = new List<GameElementHider>();

        /// <summary>
        /// The elements some hider has hidden, and whether each was showing before (or the game has shown it since), to
        /// show those again: for every hider together.
        /// </summary>
        private static readonly Dictionary<uint, bool> WasShown = new Dictionary<uint, bool>();

        private readonly string name;
        private readonly uint[] elements;
        private readonly Func<bool> wanted;
        private readonly TimeSpan checkEvery;
        private readonly Func<uint, bool, bool> showAgain;

        private bool hidden;
        private DateTime checkAt;
        private bool disposed;

        /// <param name="name">What's hidden, for log.txt.</param>
        /// <param name="wanted">Whether the elements should be hidden now, asked every frame.</param>
        /// <param name="checkEvery">
        /// How often a hidden element is looked at again in case the game showed it; once a second if not given. Zero is every
        /// frame, for one the game shows often (the combat panel, at every change of combat mode).
        /// </param>
        /// <param name="showAgain">
        /// Whether an element is shown again when it's no longer hidden, from its id and whether it was showing when it was
        /// hidden: for one the game itself would have hidden since (the combat panel, in peace mode). Null for whether it was.
        /// </param>
        public GameElementHider(string name, uint[] elements, Func<bool> wanted, TimeSpan? checkEvery = null,
            Func<uint, bool, bool> showAgain = null) {
            this.name = name;
            this.elements = elements;
            this.wanted = wanted;
            this.checkEvery = checkEvery ?? DefaultCheckEvery;
            this.showAgain = showAgain;
            All.Add(this);
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        /// <summary>The game's elements are hidden now.</summary>
        public bool Hidden => hidden;

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (disposed) {
                    return;
                }

                if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                    // leaving the world: the game makes these again at the next login
                    if (hidden) {
                        hidden = false;
                        Forget();
                        PluginCore.LogFile($"[game-panels] {name}: the game's back at logout");
                    }

                    return;
                }

                // Hide game UI hides and shows the whole UI itself
                if (HideGameUi.On) {
                    return;
                }

                var hide = wanted();

                if (hide && (!hidden || DateTime.UtcNow >= checkAt)) {
                    Hide();
                }
                else if (!hide && hidden) {
                    Restore();
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void Hide() {
            checkAt = DateTime.UtcNow + checkEvery;
            var first = !hidden;

            foreach (var id in elements) {
                var shown = ClientElements.IsShown(id) == true;
                var known = WasShown.TryGetValue(id, out var was);

                // one the game has shown since (a window it opened) is shown again too; one another hider already hid
                // keeps what it noted
                if (!known || shown) {
                    WasShown[id] = shown || was;
                }

                if (shown) {
                    ClientElements.SetVisible(id, false);
                }
            }

            if (first) {
                hidden = true;
                PluginCore.LogFile($"[game-panels] {name}: the game's hidden (" + string.Join(", ", Describe()) + ")");
            }
        }

        private IEnumerable<string> Describe() {
            foreach (var id in elements) {
                yield return $"0x{id:X8} " + (WasShown.TryGetValue(id, out var shown) && shown ? "was shown" : "wasn't shown");
            }
        }

        /// <summary>Whether another hider has this element hidden and still wants it so.</summary>
        private bool HeldByAnother(uint id) {
            return All.Any(h => h != this && !h.disposed && h.hidden && h.elements.Contains(id) && h.wanted());
        }

        /// <summary>The elements that were showing, shown again, but those another hider still wants hidden.</summary>
        private void Restore() {
            if (!hidden) {
                return;
            }

            hidden = false;
            var inWorld = CoreManager.Current.CharacterFilter.LoginStatus == 3;

            foreach (var id in elements) {
                if (!WasShown.TryGetValue(id, out var was)) {
                    continue;
                }

                if (inWorld && HeldByAnother(id)) {
                    PluginCore.LogFile($"[game-panels] {name}: 0x{id:X8} left hidden for another panel");
                    continue;
                }

                WasShown.Remove(id);

                if (inWorld && (showAgain?.Invoke(id, was) ?? was)) {
                    ClientElements.SetVisible(id, true);
                }
            }

            if (inWorld) {
                PluginCore.LogFile($"[game-panels] {name}: the game's shown again");
            }
        }

        /// <summary>At logout: nothing to put back, the game makes its UI again.</summary>
        private void Forget() {
            foreach (var id in elements) {
                WasShown.Remove(id);
            }
        }

        public void Dispose() {
            disposed = true;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;

            try {
                Restore();
            }
            finally {
                All.Remove(this);
            }
        }
    }
}
