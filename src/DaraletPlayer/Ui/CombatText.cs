using System;
using System.Diagnostics;
using System.Numerics;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;
using UtilityBelt.Service;
using UtilityBelt.Service.Views;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Floating combat text: numbers rising over whoever they landed on (CombatHeadText), or from two fixed places on screen,
    /// one for what you do and one for what's done to you. The fixed ones are drawn on ImGui's background list, under the
    /// plugin's windows, from a hud of its own that's only there to be drawn from: it takes no mouse (NoInputs), or it would
    /// take clicks meant for the game.
    /// </summary>
    internal sealed class CombatText : IDisposable {
        private const ImGuiWindowFlags Flags = ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNav;

        /// <summary>How much bigger a critical, a kill or a death is.</summary>
        private const float BigScale = 1.4f;

        private static readonly (string Text, bool Yours, bool Big, CombatKind Kind)[] Samples = {
            ("128", true, false, CombatKind.YouHit),
            ("-54", false, false, CombatKind.HitYou),
            ("312!", true, true, CombatKind.YouSpell),
            ("Evaded", false, false, CombatKind.YouAvoid),
            ("+40", false, false, CombatKind.Healing),
            ("Blocked", true, false, CombatKind.YourMiss),
        };

        private readonly Settings settings;
        private readonly ItemIcons icons;
        private readonly CombatWatch watch;
        private readonly Hud hud;
        private readonly CombatFloats floats = new CombatFloats();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly CombatHeadText heads;

        private double nextSample;
        private int sample;
        private bool disposed;

        public CombatText(Settings settings, ItemIcons icons, CombatWatch watch) {
            this.settings = settings;
            this.icons = icons;
            this.watch = watch;
            heads = new CombatHeadText(settings);

            hud =UBService.Huds.CreateHud("Combat text");
            hud.ShowInBar = false;
            hud.Visible = false;
            hud.OnPreRender += Hud_OnPreRender;
            hud.OnRender += Hud_OnRender;
            watch.Line += Watch_Line;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
        }

        /// <summary>Sample numbers keep rising, to see where they go while setting it up.</summary>
        public bool Preview { get; set; }

        private double Now => clock.Elapsed.TotalSeconds;

        private bool Wanted => settings.Data.CombatTextOn || Preview;

        private void Watch_Line(CombatLine line) {
            if (settings.Data.CombatTextOn && settings.Data.CombatOptions(line.Kind).Floating) {
                var over = settings.Data.CombatTextOverHead ? heads.TargetFor(line) : 0;
                floats.Add(line, settings.Data.CombatOptions(line.Kind).Color, Now, over);
            }
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (disposed) {
                    return;
                }

                var shown = Wanted && !HideGameUi.PluginWindowsHidden && CoreManager.Current.CharacterFilter.LoginStatus == 3;

                if (hud.Visible != shown) {
                    hud.Visible = shown;
                }

                if (!shown) {
                    floats.Clear();
                    heads.Clear();
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void Hud_OnPreRender(object sender, EventArgs e) {
            try {
                hud.WindowSettings = Flags;
                ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
                ImGui.SetNextWindowSize(Vector2.One, ImGuiCond.Always);
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void Hud_OnRender(object sender, EventArgs e) {
            try {
                if (disposed) {
                    return;
                }

                var now = Now;
                AddSample(now);

                var duration = settings.Data.ClampedCombatTextSeconds;
                floats.Expire(now, duration);
                heads.Step(floats.All, now, duration);

                var data = settings.Data;
                var screen = ImGui.GetIO().DisplaySize;
                var yours = new Vector2(screen.X * data.CombatTextYoursX / 100f, screen.Y * data.CombatTextYoursY / 100f);
                var theirs = new Vector2(screen.X * data.CombatTextTheirsX / 100f, screen.Y * data.CombatTextTheirsY / 100f);
                var drawList = ImGui.GetBackgroundDrawList();

                foreach (var one in floats.All) {
                    if (one.Over != 0 && !heads.Failed) {
                        continue;
                    }

                    var pixels = (int)Math.Round(data.ClampedCombatTextSize * (one.Big ? BigScale : 1));
                    var size = icons.LabelSize(one.Text, pixels);

                    if (!size.HasValue) {
                        continue;
                    }

                    var (rise, alpha) = CombatFloats.At(now - one.Start, duration);
                    var from = one.Yours ? yours : theirs;
                    var at = from + new Vector2(one.Lane * data.ClampedCombatTextSize * 2.5f - size.Value.X / 2,
                        -rise * data.ClampedCombatTextSize);
                    icons.DrawLabel(drawList, one.Text, at, pixels, PackedColor.Fade(one.Color, alpha));
                }
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }

        private void AddSample(double now) {
            if (!Preview || now < nextSample) {
                return;
            }

            nextSample = now + 0.4;
            var one = Samples[sample++ % Samples.Length];
            var over = settings.Data.CombatTextOverHead ? heads.TargetForSample(one.Yours) : 0;
            floats.Add(one.Text, one.Yours, one.Big, settings.Data.CombatOptions(one.Kind).Color, now, over);
        }

        public void Dispose() {
            disposed = true;
            watch.Line -= Watch_Line;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            hud.OnPreRender -= Hud_OnPreRender;
            hud.OnRender -= Hud_OnRender;
            hud.Visible = false;
            hud.Dispose();
            heads.Dispose();
        }
    }
}
