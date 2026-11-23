using System;
using System.Collections.Generic;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;
using UtilityBelt.Service;
using UtilityBelt.Service.Views;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A loading bar while the draw distance steps up (the user's call, November 2026: "it's a bad UX how long it takes to
    /// load"): at the top of the screen, how far the landscape and scenery have come from where the step-up began, with
    /// what's loading in words; it says Done for a moment once there, then goes. Drawn from a hud of its own that takes no
    /// mouse (NoInputs), as CombatText's. Hidden with the plugin's windows (Hide game UI).
    /// <para>
    /// It also waits for the plugin's own pieces made after arriving (Feed/LoadPieces.cs: the far land with its pictures,
    /// the trees and buildings past the game's), on logging in and after every teleport, with or without steps: it said
    /// Done 8 to 11 seconds before they were in (the user's eye, November 2026). With steps, they're the first 60% of the
    /// bar and the pieces the rest. Untested in the game when written.
    /// </para>
    /// </summary>
    internal sealed partial class DrawDistance {
        private const ImGuiWindowFlags ProgressFlags = ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNav;

        /// <summary>How long Done stays up.</summary>
        private const double DoneSeconds = 1.5;

        private Hud progressHud;
        private bool stepping;
        private int startScenery;
        private int startLandscape;
        private int progressScenery;
        private int progressLandscape;
        private int progressTargetScenery;
        private int progressTargetLandscape;
        private DateTime doneAt = DateTime.MinValue;

        /// <summary>The share of the bar the steps take when pieces are waited for after them.</summary>
        private const float StepShare = 0.6f;

        /// <summary>Whether this load had steps, so the pieces fill only the bar's last part.</summary>
        private bool stepsInThisLoad;

        /// <summary>Whether the bar has been waiting for pieces, so their end is its Done.</summary>
        private bool piecesShown;

        private bool barLoggedIn;

        /// <summary>
        /// On arriving (logging in, out of portal space): the pieces to wait for, as they're on. Indoors or switched off,
        /// each says at once that nothing's coming.
        /// </summary>
        private void ExpectPieces() {
            var options = Options;
            var pieces = new List<string>();

            if (options.ClampedFarLand > 0) {
                pieces.Add(LoadPieces.FarLand);
            }

            if (options.On && options.FarScenery && !billboardsFailed) {
                pieces.Add(LoadPieces.Trees);
            }

            LoadPieces.Expect(pieces, DateTime.UtcNow);

            // a load with no steps (a teleport): the bar is the pieces alone
            if (!stepping) {
                stepsInThisLoad = false;
            }
        }

        /// <summary>From Apply: what's loaded now against what it's stepping up to.</summary>
        private void NoteProgress(int scenery, int landscape, int targetScenery, int targetLandscape, bool there) {
            if (there) {
                if (stepping) {
                    stepping = false;
                    progressScenery = targetScenery;
                    progressLandscape = targetLandscape;

                    // Done once the pieces after the steps are in too (UpdateProgressHud)
                    if (LoadPieces.Waiting(DateTime.UtcNow) == 0) {
                        doneAt = DateTime.UtcNow;
                    }
                }

                return;
            }

            var goingUp = targetScenery > scenery || targetLandscape > landscape;

            if (!goingUp) {
                return;
            }

            // a new target that's further (or the first) starts the bar from where things are now
            if (!stepping || targetScenery != progressTargetScenery || targetLandscape != progressTargetLandscape) {
                if (!stepping) {
                    startScenery = scenery;
                    startLandscape = landscape;
                }

                stepping = true;
                stepsInThisLoad = true;
                doneAt = DateTime.MinValue;
            }

            progressScenery = scenery;
            progressLandscape = landscape;
            progressTargetScenery = targetScenery;
            progressTargetLandscape = targetLandscape;
        }

        /// <summary>From RenderFrame: the bar's hud made once, shown while there's something to show.</summary>
        private void UpdateProgressHud() {
            var now = DateTime.UtcNow;
            var loggedIn = CoreManager.Current.CharacterFilter.LoginStatus == 3;

            // logging in (a hot reload too): everything's made afresh
            if (loggedIn && !barLoggedIn) {
                ExpectPieces();
            }

            barLoggedIn = loggedIn;
            var waiting = LoadPieces.Waiting(now);

            if (waiting > 0) {
                if (!piecesShown) {
                    piecesShown = true;
                    doneAt = DateTime.MinValue;
                }
            }
            else if (piecesShown) {
                piecesShown = false;

                if (!stepping) {
                    doneAt = now;
                }
            }

            var showing = (stepping || waiting > 0 || (now - doneAt).TotalSeconds < DoneSeconds)
                && !HideGameUi.PluginWindowsHidden && loggedIn;

            if (progressHud == null) {
                if (!showing) {
                    return;
                }

                progressHud = UBService.Huds.CreateHud("Draw distance loading");
                progressHud.ShowInBar = false;
                progressHud.OnPreRender += ProgressHud_OnPreRender;
                progressHud.OnRender += ProgressHud_OnRender;
            }

            if (progressHud.Visible != showing) {
                progressHud.Visible = showing;
            }
        }

        private void ProgressHud_OnPreRender(object sender, EventArgs e) {
            try {
                progressHud.WindowSettings = ProgressFlags;
                ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
                ImGui.SetNextWindowSize(Vector2.One, ImGuiCond.Always);
            }
            catch (Exception ex) {
                PluginCore.LogFile("[draw-distance] " + ex);
            }
        }

        private void ProgressHud_OnRender(object sender, EventArgs e) {
            try {
                if (disposed) {
                    return;
                }

                var total = Math.Max(0, progressTargetScenery - startScenery)
                    + Math.Max(0, progressTargetLandscape - startLandscape);
                var done = Math.Max(0, progressScenery - startScenery) + Math.Max(0, progressLandscape - startLandscape);
                var stepFraction = !stepping ? 1f : total <= 0 ? 0f : Math.Min(1f, (float)done / total);
                var expected = LoadPieces.Expected;
                var waiting = LoadPieces.Waiting(DateTime.UtcNow);
                var pieceFraction = expected == 0 ? 1f : (float)(expected - waiting) / expected;
                // with pieces still to come the steps are the bar's first part, without them all of it
                var stepShare = !stepsInThisLoad ? 0f : waiting > 0 ? StepShare : 1f;
                var fraction = waiting == 0 && !stepping ? 1f
                    : stepShare * stepFraction + (1 - stepShare) * (stepping ? 0f : pieceFraction);
                var text = stepping
                    ? done == 0 ? "Draw distance: waiting for the area to load..."
                        : $"Loading draw distance: landscape {progressLandscape} of {progressTargetLandscape}, "
                            + $"scenery {progressScenery} of {progressTargetScenery}"
                    : waiting > 0
                        ? Arriving ? "Waiting for the area to load..." : $"Loading the {LoadPieces.Describe()}..."
                    : "Draw distance loaded";

                var screen = ImGui.GetIO().DisplaySize;
                var line = ImGui.GetTextLineHeight();
                var width = Math.Max(ImGui.CalcTextSize(text).X + line * 2, line * 22);
                var top = new Vector2((screen.X - width) / 2, line * 3);
                var barTop = top + new Vector2(line * 0.5f, line * 1.6f);
                var barEnd = new Vector2(top.X + width - line * 0.5f, barTop.Y + line * 0.5f);
                var draw = ImGui.GetForegroundDrawList();
                var rounding = line * 0.25f;

                draw.AddRectFilled(top, new Vector2(top.X + width, barEnd.Y + line * 0.5f),
                    ImGui.GetColorU32(new Vector4(0.05f, 0.04f, 0.03f, 0.75f)), rounding);
                draw.AddText(top + new Vector2(line, line * 0.3f), ImGui.GetColorU32(Colors.Text), text);
                draw.AddRectFilled(barTop, barEnd, ImGui.GetColorU32(new Vector4(1, 1, 1, 0.12f)), rounding);
                draw.AddRectFilled(barTop, new Vector2(barTop.X + (barEnd.X - barTop.X) * fraction, barEnd.Y),
                    ImGui.GetColorU32(Colors.Warning), rounding);
            }
            catch (Exception ex) {
                PluginCore.LogFile("[draw-distance] " + ex);
            }
        }

        private void DisposeProgress() {
            if (progressHud == null) {
                return;
            }

            progressHud.OnPreRender -= ProgressHud_OnPreRender;
            progressHud.OnRender -= ProgressHud_OnRender;
            progressHud.Visible = false;
            progressHud.Dispose();
            progressHud = null;
        }
    }
}
