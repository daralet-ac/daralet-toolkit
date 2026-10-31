using System;
using System.Collections.Generic;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Floating combat text over a creature's head. A plugin can't ask where something is on screen, but Decal's own 3D
    /// objects are anchored to an object and follow it, so these are those, not ImGui's text: they stand in the world, so
    /// a wall can hide one. Each is kept and used again, as making one draws its text. If Decal won't make them, Failed
    /// sends the text back to its fixed places on screen.
    /// </summary>
    internal sealed class CombatHeadText : IDisposable {
        private const string Font = "Arial";

        /// <summary>How far a number rises, in meters for each text height CombatFloats.At gives.</summary>
        private const float RiseMeters = 0.2f;

        /// <summary>How much bigger a critical, a kill or a death is.</summary>
        private const float BigScale = 1.4f;

        /// <summary>Colour only changes in steps as it fades: each change is a call into Decal.</summary>
        private const int FadeSteps = 5;

        private readonly Settings settings;
        private readonly Dictionary<CombatFloat, (D3DObj Obj, int Fade)> shown = new Dictionary<CombatFloat, (D3DObj, int)>();
        private readonly List<D3DObj> spare = new List<D3DObj>();
        private readonly List<CombatFloat> gone = new List<CombatFloat>();
        private int last;

        public CombatHeadText(Settings settings) {
            this.settings = settings;
        }

        public bool Failed { get; private set; }

        /// <summary>Who a line of combat floats over, or 0 to use the fixed places on screen.</summary>
        public int TargetFor(CombatLine line) {
            if (Failed) {
                return 0;
            }

            var core = CoreManager.Current;
            var around = new List<(int, double)>();

            if (line.Target != null) {
                var self = core.CharacterFilter.Id;

                foreach (WorldObject one in core.WorldFilter.GetByName(line.Target)) {
                    around.Add((one.Id, core.WorldFilter.Distance(self, one.Id)));
                }
            }

            var yours = CombatKindInfo.For(line.Kind).Yours;
            var over = CombatTargets.Pick(!yours && line.Target == null, line.Target, core.CharacterFilter.Id, last,
                core.Actions.CurrentSelection, around);

            if (over == 0 || core.WorldFilter[over] == null) {
                return 0;
            }

            if (yours && over != core.CharacterFilter.Id) {
                last = over;
            }

            return over;
        }

        /// <summary>Who a sample number floats over: your selection, or you.</summary>
        public int TargetForSample(bool yours) {
            if (Failed) {
                return 0;
            }

            var core = CoreManager.Current;
            var selected = core.Actions.CurrentSelection;
            return yours && selected != 0 && core.WorldFilter[selected] != null ? selected : core.CharacterFilter.Id;
        }

        /// <summary>Makes them match what's floating now, this frame.</summary>
        public void Step(IReadOnlyList<CombatFloat> floats, double now, double duration) {
            if (Failed) {
                return;
            }

            try {
                foreach (var one in floats) {
                    if (one.Over != 0) {
                        Place(one, now, duration);
                    }
                }

                gone.Clear();

                foreach (var one in shown.Keys) {
                    if (!Contains(floats, one)) {
                        gone.Add(one);
                    }
                }

                foreach (var one in gone) {
                    Retire(one);
                }
            }
            catch (Exception ex) {
                Failed = true;
                PluginCore.LogFile($"[combat] Decal's 3D text failed, so the numbers go to their places on screen: {ex}");
                Clear();
            }
        }

        private void Place(CombatFloat one, double now, double duration) {
            var (rise, alpha) = CombatFloats.At(now - one.Start, duration);
            var fade = (int)Math.Round(alpha * FadeSteps);

            if (!shown.TryGetValue(one, out var held)) {
                held = (Make(one), -1);
            }

            if (held.Fade != fade) {
                held.Obj.Color = CombatTargets.ToArgb(one.Color, fade / (float)FadeSteps);
                held.Fade = fade;
            }

            shown[one] = held;

            // the lane's sideways in the world, not on screen: a plugin isn't told which way the camera's looking
            held.Obj.Anchor(one.Over, 1f, one.Lane * 0.25f, 0, settings.Data.CombatTextHeadHeight / 10f + rise * RiseMeters);
        }

        private D3DObj Make(CombatFloat one) {
            D3DObj obj;

            if (spare.Count > 0) {
                obj = spare[spare.Count - 1];
                spare.RemoveAt(spare.Count - 1);
            }
            else {
                obj = CoreManager.Current.D3DService.NewD3DObj();
                obj.OrientToCamera(true);
            }

            obj.SetText(D3DTextType.Text2D, one.Text, Font, 0);
            obj.Autoscale = settings.Data.CombatTextHeadFixedSize;
            obj.Scale(settings.Data.ClampedCombatTextHeadSize / 100f * (one.Big ? BigScale : 1));
            obj.Visible = true;
            return obj;
        }

        private void Retire(CombatFloat one) {
            var obj = shown[one].Obj;
            shown.Remove(one);
            obj.Visible = false;
            spare.Add(obj);
        }

        private static bool Contains(IReadOnlyList<CombatFloat> floats, CombatFloat one) {
            for (var i = 0; i < floats.Count; i++) {
                if (ReferenceEquals(floats[i], one)) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Takes every one off the screen.</summary>
        public void Clear() {
            gone.Clear();
            gone.AddRange(shown.Keys);

            foreach (var one in gone) {
                try {
                    Retire(one);
                }
                catch (Exception ex) {
                    PluginCore.LogFile($"[combat] Couldn't hide a number: {ex.Message}");
                    shown.Remove(one);
                }
            }
        }

        public void Dispose() {
            Clear();

            foreach (var obj in spare) {
                try {
                    obj.Dispose();
                }
                catch (Exception ex) {
                    PluginCore.LogFile($"[combat] Couldn't free a number: {ex.Message}");
                }
            }

            spare.Clear();
        }
    }
}
