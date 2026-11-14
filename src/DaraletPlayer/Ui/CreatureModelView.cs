using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The creature's model in the plugin's Creature Appraisal window (the user's call, November 2026), drawn by the game:
    /// its examination panel (the Examination floaty, 0x100005F7, layout 0x2100006B) shows a creature's model in a 3D
    /// view, 0x10000148 (a UIElement_Viewport, its CreatureMode posing the creature), behind the stats, on the stone
    /// 0x10000141, in the creature page 0x10000140. So the game is let do its own appraisal of a creature again, and its
    /// panel is cut down to the model: every other piece hidden (its frame, title, close button, other pages, and the
    /// page's name, level, stats and description), the window sized to the view, the page moved up so the view sits at
    /// its corner (a parent clips its children), and the window moved under a see-through part of the plugin's window
    /// (FloatingWindow.SetHole), the game drawing its UI under ImGui's. All with UIElement's own MoveTo, ResizeTo and
    /// SetVisible (ClientElements, under the floaty, since its pieces' ids are used in other layouts too), none of which
    /// the game saves. Put back as it was (places, and the pieces that were showing) before an appraisal the game shows
    /// itself (an item's), when the window stops showing the model (then hidden too), and when the plugin stops.
    /// [appraisal] lines in log.txt. Untested in the game when written.
    /// </summary>
    internal sealed class CreatureModelView : IDisposable {
        private const uint Floaty = 0x100005F7;
        private const uint Page = 0x10000140;
        private const uint View = 0x10000148;
        private const uint Stone = 0x10000141;

        /// <summary>The page's gold bar (300x5, over the description), which the game draws behind the model.</summary>
        private const uint Bar = 0x10000461;

        /// <summary>How long after the window last placed the model it's taken to have stopped showing it.</summary>
        private static readonly TimeSpan Released = TimeSpan.FromMilliseconds(250);

        /// <summary>The floaty's own pieces: its frame, title, close button and its item and other pages.</summary>
        private static readonly uint[] FloatyPieces = {
            0x100005F3, 0x10000528, 0x10000529, 0x10000153, 0x1000012D, 0x1000012E,
            0x10000673, 0x10000674, 0x10000675, 0x10000676, 0x10000677, 0x10000678, 0x10000679, 0x1000067A, 0x1000067B,
            0x1000067C, 0x1000067D, 0x1000067E, 0x1000067F, 0x10000680, 0x10000681, 0x10000682,
        };

        /// <summary>The creature page's pieces but the view and the stone: the stats, name, level and description.</summary>
        private static readonly uint[] PagePieces = {
            0x10000149, 0x1000014A, 0x1000014B, 0x1000014C, 0x1000014D, 0x1000014F, 0x10000335, 0x10000145,
            0x1000053A, 0x10000146,
        };

        /// <summary>The pieces hidden, and whether each was showing then, to show again.</summary>
        private readonly Dictionary<uint, bool> wasShown = new Dictionary<uint, bool>();

        /// <summary>Where the floaty, the page, the view and the stone were before they were moved, while they are.</summary>
        private readonly Dictionary<uint, (int X, int Y, int Width, int Height)> original =
            new Dictionary<uint, (int, int, int, int)>();

        /// <summary>The floaty's least height (as GameHotbar has the toolbar's).</summary>
        private const uint LeastHeight = 0x3E;

        /// <summary>The floaty's size limits before they were lowered, by attribute, while they are.</summary>
        private readonly Dictionary<uint, int> leastSizeWas = new Dictionary<uint, int>();

        private bool active;
        private bool barWasShown;
        private (int Y, int Width)? barAt;
        private bool loggedPlaced;
        private bool loggedMovedBack;
        private (int X, int Y, int Width, int Height)? placed;
        private DateTime lastShown = DateTime.MinValue;

        /// <summary>The creature the game was let appraise for its model; 0 for none.</summary>
        public int For { get; private set; }

        /// <summary>The last place the model was shown at, in screen pixels, for the next one before it's drawn.</summary>
        public (Vector2 Min, Vector2 Size)? LastPlace { get; private set; }

        public CreatureModelView() {
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        /// <summary>
        /// The game's about to appraise this creature for its model: its panel is cut down at once, where the model last
        /// was, so the whole panel never flashes up.
        /// </summary>
        public void Expect(int id) {
            For = id;

            if (LastPlace.HasValue) {
                Show(LastPlace.Value.Min, LastPlace.Value.Size);
            }
        }

        /// <summary>Whether the game's panel is up, with the creature's model in it to show.</summary>
        public bool Ready => For != 0 && ClientElements.IsShownUnder(Floaty, Floaty) == true;

        /// <summary>
        /// Puts the model here (in screen pixels), the panel cut down to it, from the window's draw each frame it shows
        /// the model.
        /// </summary>
        public void Show(Vector2 min, Vector2 size) {
            lastShown = DateTime.UtcNow;
            LastPlace = (min, size);

            if (!active) {
                Begin();
            }

            if (!active) {
                return;
            }

            HidePieces();

            var view = original[View];
            var place = ((int)Math.Round(min.X), (int)Math.Round(min.Y), Math.Max(16, (int)Math.Round(size.X)),
                Math.Max(16, (int)Math.Round(size.Y)));

            // laid out again only when it's changed, or the game has moved it back (opening it again, say)
            var known = ClientElements.TryGetPlaceUnder(Floaty, Floaty, out var x, out var y, out var w, out var h);

            if (placed == place && known && x == place.Item1 && y == place.Item2 && w == place.Item3 && h == place.Item4) {
                return;
            }

            if (placed == place && known && !loggedMovedBack) {
                loggedMovedBack = true;
                PluginCore.LogFile($"[appraisal] the game's panel is at {x},{y} {w}x{h}, not where it was put, "
                    + $"{place.Item1},{place.Item2} {place.Item3}x{place.Item4}: put there again");
            }

            var (left, top, width, height) = place;
            ClientElements.PlaceUnder(Floaty, Floaty, left, top, width, height);
            ClientElements.PlaceUnder(Floaty, Page, 0, -view.Y, width, view.Y + height);
            ClientElements.PlaceUnder(Floaty, Stone, 0, 0, width, view.Y + height);
            ClientElements.PlaceUnder(Floaty, View, 0, view.Y, width, height);
            placed = place;

            // the page laid out again may have moved the bar: placed afresh
            barAt = null;

            if (!loggedPlaced) {
                loggedPlaced = true;
                ClientElements.TryGetPlaceUnder(Floaty, Floaty, out x, out y, out w, out h);
                ClientElements.TryGetPlaceUnder(Floaty, View, out var vx, out var vy, out var vw, out var vh);
                PluginCore.LogFile($"[appraisal] model placed at {left},{top} {width}x{height}: the game's panel is at "
                    + $"{x},{y} {w}x{h}, its view at {vx},{vy} {vw}x{vh}");
            }
        }

        /// <summary>
        /// The page's gold bar at this height on screen (the window's divider over the model, which the game draws behind
        /// it, as its own panel's bar over the description is), the model's width; hidden for null, or out of the model's
        /// box. Called after the window has drawn what's over the model, each frame it shows it.
        /// </summary>
        public void PlaceBar(float? screenY) {
            if (!active || !original.TryGetValue(Bar, out var bar) || !LastPlace.HasValue) {
                return;
            }

            var (min, size) = LastPlace.Value;
            var height = bar.Height;

            if (!screenY.HasValue || screenY.Value < min.Y || screenY.Value + height > min.Y + size.Y) {
                if (ClientElements.IsShownUnder(Floaty, Bar) == true) {
                    ClientElements.SetVisibleUnder(Floaty, Bar, false);
                }

                barAt = null;
                return;
            }

            // in the page, which sits the view's height above the panel's top
            var y = (int)Math.Round(original[View].Y + screenY.Value - min.Y);
            var width = (int)Math.Round(size.X);

            if (barAt != (y, width)) {
                ClientElements.PlaceUnder(Floaty, Bar, 0, y, width, height);
                barAt = (y, width);
            }

            if (ClientElements.IsShownUnder(Floaty, Bar) != true) {
                ClientElements.SetVisibleUnder(Floaty, Bar, true);
            }
        }

        /// <summary>Notes where everything was, before anything's moved.</summary>
        private void Begin() {
            original.Clear();

            foreach (var id in new[] { Floaty, Page, View, Stone }) {
                if (!ClientElements.TryGetPlaceUnder(Floaty, id, out var x, out var y, out var w, out var h) || w <= 0) {
                    PluginCore.LogFile($"[appraisal] the game's examination panel has no 0x{id:X8}: no model");
                    original.Clear();
                    return;
                }

                original[id] = (x, y, w, h);
            }

            // the bar is placed and shown for the window (PlaceBar), so where it was and whether it showed are kept
            if (ClientElements.TryGetPlaceUnder(Floaty, Bar, out var bx, out var by, out var bw, out var bh) && bw > 0) {
                original[Bar] = (bx, by, bw, bh);
                barWasShown = ClientElements.IsShownUnder(Floaty, Bar) == true;
            }

            active = true;
            placed = null;
            LowerLeastSize();
            PluginCore.LogFile($"[appraisal] cutting the game's examination panel down to its model (it was at "
                + $"{original[Floaty].X},{original[Floaty].Y} {original[Floaty].Width}x{original[Floaty].Height}, "
                + $"its view {original[View].Width}x{original[View].Height})");
        }

        /// <summary>
        /// The floaty keeps to a least size (310x400: the first build's was never smaller, so it stood out past the window's
        /// hole), its attributes, which ResizeTo holds it to: 0x3E the least height, as the toolbar's (GameHotbar), and the
        /// least width the smaller of 0x3D and 0x3F. Lowered while it's cut down, and put back after (RestoreLeastSize).
        /// </summary>
        private void LowerLeastSize() {
            leastSizeWas.Clear();

            foreach (var attribute in new uint[] { 0x3C, 0x3D, 0x3E, 0x3F }) {
                var value = ClientElements.GetIntAttribute(Floaty, attribute);

                if (value.HasValue) {
                    leastSizeWas[attribute] = value.Value;
                }
            }

            PluginCore.LogFile("[appraisal] the panel's size limits: "
                + string.Join(", ", leastSizeWas.Select(a => $"0x{a.Key:X2} {a.Value}")));

            var lower = new List<uint> { LeastHeight };

            if (leastSizeWas.TryGetValue(0x3D, out var d) && leastSizeWas.TryGetValue(0x3F, out var f)) {
                lower.Add(d <= f ? 0x3Du : 0x3Fu);
            }
            else if (leastSizeWas.ContainsKey(0x3F)) {
                lower.Add(0x3F);
            }

            foreach (var attribute in lower) {
                if (leastSizeWas.ContainsKey(attribute)) {
                    ClientElements.SetIntAttribute(Floaty, attribute, 16);
                }
            }
        }

        private void RestoreLeastSize() {
            foreach (var was in leastSizeWas) {
                ClientElements.SetIntAttribute(Floaty, was.Key, was.Value);
            }

            leastSizeWas.Clear();
        }

        /// <summary>Hides what the game's shown of the panel but the model, noting what was showing.</summary>
        private void HidePieces() {
            Hide(FloatyPieces);
            Hide(PagePieces);
        }

        private void Hide(uint[] pieces) {
            foreach (var id in pieces) {
                if (ClientElements.IsShownUnder(Floaty, id) != true) {
                    continue;
                }

                wasShown[id] = true;
                ClientElements.SetVisibleUnder(Floaty, id, false);
            }
        }

        /// <summary>
        /// Puts the game's panel back as it was: its places, and the pieces that were showing. Hidden too, when it was only
        /// up for the model (the window stopped showing it).
        /// </summary>
        public void Restore(bool hide) {
            // the panel's no longer the creature's model: an item's next, or nothing
            For = 0;

            if (!active) {
                return;
            }

            if (hide) {
                ClientElements.SetVisibleUnder(Floaty, Floaty, false);
            }

            // its limits first, so its own size fits them again
            RestoreLeastSize();

            foreach (var id in new[] { Bar, View, Stone, Page, Floaty }) {
                if (original.TryGetValue(id, out var at)) {
                    ClientElements.PlaceUnder(Floaty, id, at.X, at.Y, at.Width, at.Height);
                }
            }

            foreach (var shown in wasShown) {
                if (shown.Value) {
                    ClientElements.SetVisibleUnder(Floaty, shown.Key, true);
                }
            }

            if (original.ContainsKey(Bar)) {
                ClientElements.SetVisibleUnder(Floaty, Bar, barWasShown);
            }

            wasShown.Clear();
            original.Clear();
            active = false;
            barAt = null;
            placed = null;
            PluginCore.LogFile("[appraisal] the game's examination panel put back" + (hide ? " and closed" : ""));
        }

        /// <summary>The window stopped placing the model (closed, hidden, showing another): the panel goes back.</summary>
        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (active && DateTime.UtcNow - lastShown > Released) {
                    Restore(hide: true);
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        public void Dispose() {
            CoreManager.Current.RenderFrame -= Core_RenderFrame;

            try {
                Restore(hide: true);
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }
    }
}
