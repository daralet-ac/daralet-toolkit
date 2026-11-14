using System;
using System.Collections.Generic;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The Link Status window's pings, oldest first, up to Capacity (an hour's at one a second), with what they come to
    /// over any recent stretch (Stats). Each ping notes the usual round trip when it was sent (its Baseline), so whether
    /// it was a spike doesn't change as later pings come in.
    /// </summary>
    public sealed class LinkHistory {
        public const int Capacity = 3600;

        /// <summary>How many answered pings before one make its baseline, and how few will do.</summary>
        public const int BaselineOf = 60;
        public const int BaselineAtLeast = 5;

        private readonly List<LinkSample> samples = new List<LinkSample>();

        public IReadOnlyList<LinkSample> Samples => samples;

        public void Clear() {
            samples.Clear();
        }

        /// <summary>
        /// A ping sent at at (seconds), answered in ms milliseconds (null for never), with the game's link state then.
        /// </summary>
        public LinkSample Add(double at, double? ms, uint? gameState) {
            var sample = new LinkSample { At = at, Ms = ms, Baseline = Baseline(), GameState = gameState };
            samples.Add(sample);

            // a few at a time, rather than shifting the whole list for every ping
            if (samples.Count > Capacity + 60) {
                samples.RemoveRange(0, samples.Count - Capacity);
            }

            return sample;
        }

        /// <summary>The median of the last BaselineOf answered pings, or null with fewer than BaselineAtLeast.</summary>
        public double? Baseline() {
            var recent = new List<double>(BaselineOf);

            for (var i = samples.Count - 1; i >= 0 && recent.Count < BaselineOf; i--) {
                if (samples[i].Ms != null) {
                    recent.Add(samples[i].Ms.Value);
                }
            }

            return recent.Count < BaselineAtLeast ? (double?)null : Median(recent);
        }

        /// <summary>The pings sent at from or later.</summary>
        public IEnumerable<LinkSample> Since(double from) {
            var start = samples.Count;

            while (start > 0 && samples[start - 1].At >= from) {
                start--;
            }

            for (var i = start; i < samples.Count; i++) {
                yield return samples[i];
            }
        }

        /// <summary>What the pings sent at from or later come to, spikes being margin milliseconds over the usual.</summary>
        public LinkStats Stats(double from, double margin) {
            var stats = new LinkStats();
            var answered = new List<double>();
            double? previous = null;
            double jitter = 0;
            var steps = 0;

            foreach (var sample in Since(from)) {
                stats.Count++;

                if (sample.IsSpike(margin)) {
                    stats.Spikes++;
                }

                if (sample.Ms == null) {
                    stats.Lost++;
                    continue;
                }

                var ms = sample.Ms.Value;
                answered.Add(ms);

                if (previous != null) {
                    jitter += Math.Abs(ms - previous.Value);
                    steps++;
                }

                previous = ms;
            }

            if (answered.Count > 0) {
                stats.Average = answered.Average();
                stats.Lowest = answered.Min();
                stats.Highest = answered.Max();
                stats.Median = Median(answered);
            }

            if (steps > 0) {
                stats.Jitter = jitter / steps;
            }

            return stats;
        }

        public static double Median(List<double> values) {
            var sorted = values.OrderBy(v => v).ToList();
            var middle = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
        }

        /// <summary>
        /// The top of the graph's scale, in milliseconds: the next of 100, 200, 250, 500, 1000, 2000, 2500, 5000... at or
        /// over highest, so the tallest bar fits and the lines across land on round numbers.
        /// </summary>
        public static double ScaleTop(double highest) {
            for (var unit = 100.0; ; unit *= 10) {
                foreach (var step in new[] { 1.0, 2, 2.5, 5 }) {
                    if (unit * step >= highest) {
                        return unit * step;
                    }
                }
            }
        }

        /// <summary>A duration in words for the window: "45s", "3m 05s", "1h 02m".</summary>
        public static string Ago(double seconds) {
            var whole = (int)Math.Max(0, Math.Round(seconds));

            if (whole < 60) {
                return $"{whole}s";
            }

            if (whole < 3600) {
                return $"{whole / 60}m {whole % 60:00}s";
            }

            return $"{whole / 3600}h {whole % 3600 / 60:00}m";
        }
    }
}
