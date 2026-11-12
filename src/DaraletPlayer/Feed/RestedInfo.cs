using System;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Your rested experience, inside XpInfo (@playerui xp; PlayerUiRested in daralet-ACE, Player_RestedXp.cs): what's
    /// left and the most there can be, the bonus your kills earn from it (1.0 doubles their XP), how much an hour of rest
    /// adds (PerHour, while less than a level's cost is rested: past each full LevelCost it's SlowdownPerLevel less, 0.5
    /// halving it, as the server's RestedXpRules has it), and what your kills have earned from it since login. State is
    /// resting (in a town out of combat mode), combat (in a town in combat mode), away (not in a town), full, maxlevel (no
    /// XP to rest for), norest (too high a level to build any up) or off. The server's /rested says the same from the same
    /// reply.
    /// </summary>
    [DataContract]
    public class RestedInfo {
        public const string Resting = "resting";
        public const string InCombat = "combat";
        public const string Away = "away";
        public const string Full = "full";
        public const string MaxLevel = "maxlevel";
        /// <summary>Nothing builds up at this level (the server set it so); what is left still spends.</summary>
        public const string NoRest = "norest";
        /// <summary>Below MinLevel: nothing builds up yet.</summary>
        public const string LowLevel = "lowlevel";
        public const string Off = "off";

        [DataMember(Name = "enabled")] public bool Enabled;
        [DataMember(Name = "state")] public string State;
        [DataMember(Name = "rested")] public long Rested;
        /// <summary>The most there can be, or 0 for no most (the server's default).</summary>
        [DataMember(Name = "max")] public long Max;
        [DataMember(Name = "bonus")] public double Bonus;
        [DataMember(Name = "perHour")] public long PerHour;
        [DataMember(Name = "earnedThisSession")] public long EarnedThisSession;
        /// <summary>What your level costs: each full one rested slows it. 0 from a server without the slowdown.</summary>
        [DataMember(Name = "levelCost")] public long LevelCost;
        [DataMember(Name = "slowdownPerLevel")] public double SlowdownPerLevel;
        /// <summary>The level rest starts building at; 0 from a server without one.</summary>
        [DataMember(Name = "minLevel")] public int MinLevel;
        /// <summary>Logged out, the share of PerHour an hour adds (0.5 is half); null from an older server.</summary>
        [DataMember(Name = "offlineRate")] public double? OfflineRate;

        /// <summary>Whether this state builds anything up at all, anywhere.</summary>
        public bool CanRest => State != MaxLevel && State != NoRest && State != LowLevel && State != Off;

        public bool IsResting => State == Resting;

        public bool HasMost => Max > 0;

        /// <summary>The most as the sums use it: no most is as high as a pool could go.</summary>
        private long Cap => Max > 0 ? Max : long.MaxValue;

        /// <summary>
        /// What's left this many seconds after the reply: still growing while you rest, as the server's heartbeat adds it,
        /// so the bar moves between replies.
        /// </summary>
        public long RestedAfter(double seconds) {
            if (!IsResting || seconds <= 0 || PerHour <= 0 || Rested >= Cap) {
                return Rested;
            }

            return (long)Walk(Rested, seconds).Pool;
        }

        /// <summary>An hour's rest with this much rested: slower for each full level's cost in it.</summary>
        public double RateAt(long rested) {
            return PerHour * Math.Pow(Keep, FullLevels(rested));
        }

        /// <summary>Whether this much rested builds slower than the first level's worth did.</summary>
        public bool IsSlowed(long rested) {
            return SlowdownPerLevel > 0 && FullLevels(rested) > 0;
        }

        /// <summary>
        /// The same, less what earning this much XP since the reply most likely spent: of kill XP worth (1 + Bonus) times
        /// the kill, Bonus came from rested experience. A guess (quest XP spends none), until the next reply, which comes
        /// soon after XP does.
        /// </summary>
        public long RestedAfter(double seconds, long gainedXp) {
            var left = RestedAfter(seconds);

            if (gainedXp <= 0 || Bonus <= 0 || left <= 0) {
                return left;
            }

            return Math.Max(0, left - (long)(gainedXp * Bonus / (1 + Bonus)));
        }

        /// <summary>How full it is, 0 to 1, with this much left.</summary>
        public float Fraction(long rested) {
            return Max <= 0 ? 0f : Math.Min(1f, (float)rested / Max);
        }

        /// <summary>
        /// Seconds of rest until it's full with this much left, slowing past each level's cost: 0 when it is, null when it
        /// never will be (nothing builds up, there's no most, or a slowdown of 1 stops it at a level).
        /// </summary>
        public double? SecondsToFull(long rested) {
            // with no most it's never full
            if (!HasMost) {
                return null;
            }

            if (rested >= Max) {
                return 0;
            }

            if (PerHour <= 0) {
                return null;
            }

            var (pool, seconds) = Walk(rested, double.PositiveInfinity);
            return pool >= Max ? seconds : (double?)null;
        }

        /// <summary>
        /// Rests up to this many seconds (infinity for as long as it takes), a stretch at each speed, as the server's
        /// RestedXpRules.Walk does: the pool it comes to, and the seconds that took.
        /// </summary>
        private (double Pool, double Seconds) Walk(double pool, double seconds) {
            var used = 0.0;
            var levels = FullLevels(pool);

            while (pool < Cap && used < seconds) {
                var rate = PerHour * Math.Pow(Keep, levels);

                if (rate <= 0) {
                    break;
                }

                // counted on from where it started: floating point could leave the pool a hair under the level it reached
                var next = LevelCost > 0 ? Math.Min(Cap, (levels + 1.0) * LevelCost) : Cap;
                var need = Math.Max(0, next - pool) / rate * 3600;
                var left = seconds - used;

                if (need >= left) {
                    pool += rate * left / 3600;
                    used = seconds;
                    break;
                }

                pool = Math.Max(pool, next);
                used += need;
                levels++;
            }

            return (Math.Min(pool, Cap), used);
        }

        /// <summary>What's kept of the speed for each full level rested: a 0.5 slowdown keeps half.</summary>
        private double Keep => 1 - Math.Max(0, Math.Min(1, SlowdownPerLevel));

        private int FullLevels(double pool) {
            return LevelCost > 0 ? (int)Math.Max(0, Math.Floor(pool / LevelCost)) : 0;
        }

        /// <summary>"+100% kill XP".</summary>
        public string BonusText => $"+{Math.Round(Bonus * 100):0}% kill XP";

        /// <summary>Whether you're resting now, and if not, why: one line.</summary>
        public string StateText {
            get {
                switch (State) {
                    case Resting:
                        return "Resting";
                    case InCombat:
                        return "Not resting: in combat mode";
                    case Away:
                        return "Not resting: not in a town";
                    case Full:
                        return "Fully rested";
                    case MaxLevel:
                        return "Highest level: nothing to rest for";
                    case NoRest:
                        return "No rest at your level";
                    case LowLevel:
                        return $"Rest starts at level {MinLevel}";
                    case Off:
                        return "Turned off on this server";
                    default:
                        return State ?? "";
                }
            }
        }
    }
}
