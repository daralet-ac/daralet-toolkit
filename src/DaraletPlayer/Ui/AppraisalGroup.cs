using System;
using System.Collections.Generic;
using System.Linq;
using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The appraisal windows sharing one window (the user's call, November 2026): the item Appraisal window (the first:
    /// Appraise in new window's still open beside it), Creature Appraisal and Spell Appraisal take one place, size, lock,
    /// Size and background (FloatingWindow.SharedLook, the placement "appraisals", which starts where the item Appraisal
    /// window was), and the one that came up last shows: the others are closed (Dismiss, which for the item window forgets
    /// its item), as the shared window does for the toolbar's panels. Each has a close button (FloatingWindow.CloseButton),
    /// title bars being gone. Which is up is each window's being on screen (FloatingWindow.Shown), not its Visible: the item
    /// window is always Visible and shows whenever it has an item.
    /// </summary>
    internal sealed class AppraisalGroup : IDisposable {
        private const string PlacementId = "appraisals";

        private readonly Settings settings;
        private readonly List<FloatingWindow> windows;
        private readonly Dictionary<FloatingWindow, bool> wasUp = new Dictionary<FloatingWindow, bool>();
        private FloatingWindow current;
        private bool disposed;

        public AppraisalGroup(Settings settings, params FloatingWindow[] windows) {
            this.settings = settings;
            this.windows = windows.Where(w => w != null).ToList();

            foreach (var window in this.windows) {
                window.SharedLook = Placement;
                window.CloseButton = true;
            }

            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        /// <summary>The shared place, starting where the item Appraisal window was (or starts).</summary>
        private WindowPlacement Placement() {
            var placement = settings.Window(PlacementId, false);

            if (placement.X == null || placement.Y == null) {
                var item = settings.Window("appraisal");
                placement.X = item.X ?? 600;
                placement.Y = item.Y ?? 150;
            }

            return placement;
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (!disposed) {
                    FollowOpening();
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        /// <summary>The window that came up since last frame becomes the one showing; any other up is closed.</summary>
        private void FollowOpening() {
            foreach (var window in windows) {
                // the first frame's are only noted: nothing came up then
                if (window.Shown && wasUp.TryGetValue(window, out var was) && !was) {
                    current = window;
                }
            }

            if (current == null || !current.Shown) {
                current = windows.FirstOrDefault(w => w.Shown);
            }

            foreach (var window in windows) {
                if (window != current && window.Shown) {
                    window.Dismiss();
                }

                wasUp[window] = window.Shown;
            }
        }

        public void Dispose() {
            disposed = true;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;

            foreach (var window in windows) {
                window.SharedLook = null;
            }
        }
    }
}
