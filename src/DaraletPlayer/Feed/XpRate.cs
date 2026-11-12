using System;
using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// XP an hour, from your total XP noted as it changes: over the last few minutes, or since it started (login, or Reset).
    /// Changes older than the longest window are dropped, but for the last before it, which says what you had then.
    /// </summary>
    public class XpRate {
        /// <summary>The longest window it can be asked about, in minutes.</summary>
        public const int KeepMinutes = 60;

        /// <summary>Too soon to say before this: a kill in the first seconds would read as millions an hour.</summary>
        public static readonly TimeSpan MinElapsed = TimeSpan.FromSeconds(30);

        private readonly List<(DateTime At, long Xp)> changes = new List<(DateTime, long)>();
        private DateTime startedAt;
        private long startXp;

        public bool Started => changes.Count > 0;

        public DateTime StartedAt => startedAt;

        /// <summary>XP earned since it started.</summary>
        public long Gained => Started ? Latest - startXp : 0;

        private long Latest => changes[changes.Count - 1].Xp;

        /// <summary>Starts again from now.</summary>
        public void Reset(DateTime now, long xp) {
            changes.Clear();
            changes.Add((now, xp));
            startedAt = now;
            startXp = xp;
        }

        /// <summary>Your total XP now. Less than before is someone else, or a new start: it starts again.</summary>
        public void Note(DateTime now, long xp) {
            if (!Started || xp < Latest) {
                Reset(now, xp);
                return;
            }

            if (xp == Latest) {
                return;
            }

            changes.Add((now, xp));

            var cutoff = now - TimeSpan.FromMinutes(KeepMinutes);

            while (changes.Count > 1 && changes[1].At <= cutoff) {
                changes.RemoveAt(0);
            }
        }

        /// <summary>
        /// XP an hour over the last window (TimeSpan.Zero for since it started; a window reaching back before then counts
        /// from then), or null until MinElapsed has gone by.
        /// </summary>
        public double? PerHour(DateTime now, TimeSpan window) {
            if (!Started) {
                return null;
            }

            var from = startedAt;
            var fromXp = startXp;

            if (window > TimeSpan.Zero && now - window > startedAt) {
                from = now - window;
                fromXp = XpAt(from);
            }

            var elapsed = now - from;

            if (elapsed < MinElapsed) {
                return null;
            }

            return (Latest - fromXp) / elapsed.TotalHours;
        }

        /// <summary>Your total XP at a time since it started.</summary>
        private long XpAt(DateTime at) {
            var xp = changes[0].Xp;

            foreach (var change in changes) {
                if (change.At > at) {
                    break;
                }

                xp = change.Xp;
            }

            return xp;
        }
    }
}
