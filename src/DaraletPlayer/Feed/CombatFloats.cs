using System;
using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>The floating combat text on screen now: what's added, how far each has risen and how faded it is.</summary>
    public class CombatFloats {
        /// <summary>The most on screen at once: past this the oldest go, so a big fight can't fill the screen.</summary>
        public const int Most = 40;

        /// <summary>How far it rises in its time on screen, in text heights.</summary>
        private const float RiseLines = 6;

        /// <summary>It's fully there until this far through its time, then fades.</summary>
        private const float FadeFrom = 0.6f;

        private static readonly int[] LaneOrder = { 0, 1, -1, 2, -2 };

        private readonly List<CombatFloat> floats = new List<CombatFloat>();
        private int nextYours;
        private int nextTheirs;

        public IReadOnlyList<CombatFloat> All => floats;

        /// <summary>The text for a line, as it floats: a number, with a sign or "!" for a critical, or what happened.</summary>
        public static string TextFor(CombatLine line) {
            if (!line.Amount.HasValue) {
                return line.Outcome;
            }

            return CombatKindInfo.For(line.Kind).Sign + line.Amount.Value + (line.Critical ? "!" : "");
        }

        public void Add(CombatLine line, uint color, double now, int over = 0) {
            var info = CombatKindInfo.For(line.Kind);
            Add(TextFor(line), info.Yours, line.Critical || line.Kind == CombatKind.Kill || line.Kind == CombatKind.Death,
                color, now, over);
        }

        public void Add(string text, bool yours, bool big, uint color, double now, int over = 0) {
            if (string.IsNullOrEmpty(text)) {
                return;
            }

            var lane = yours ? nextYours++ : nextTheirs++;
            floats.Add(new CombatFloat {
                Text = text,
                Yours = yours,
                Big = big,
                Color = color,
                Start = now,
                Lane = LaneOrder[lane % LaneOrder.Length],
                Over = over,
            });

            if (floats.Count > Most) {
                floats.RemoveAt(0);
            }
        }

        /// <summary>Forgets those whose time is up.</summary>
        public void Expire(double now, double duration) {
            floats.RemoveAll(one => now - one.Start >= duration);
        }

        public void Clear() {
            floats.Clear();
        }

        /// <summary>How far it's risen, in text heights, and how solid it is, 0 to 1.</summary>
        public static (float Rise, float Alpha) At(double age, double duration) {
            var through = duration <= 0 ? 1 : (float)Math.Max(0, Math.Min(1, age / duration));
            var alpha = through <= FadeFrom ? 1 : 1 - (through - FadeFrom) / (1 - FadeFrom);
            return (through * RiseLines, alpha);
        }
    }
}
