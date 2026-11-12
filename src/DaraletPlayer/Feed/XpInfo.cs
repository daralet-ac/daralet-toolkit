using System;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Your level's XP, as @playerui xp sends it (PlayerUiXp in daralet-ACE, PlayerUiXpData), for the XP bar: your level and
    /// total XP, the total your level starts at and the next one does (the client has no XP table: it's the server's), and
    /// your rested experience. At the highest level both are the same and MaxLevel is set.
    /// </summary>
    [DataContract]
    public class XpInfo {
        [DataMember(Name = "level")] public int Level;
        [DataMember(Name = "totalXp")] public long TotalXp;
        [DataMember(Name = "levelXp")] public long LevelXp;
        [DataMember(Name = "nextLevelXp")] public long NextLevelXp;
        [DataMember(Name = "maxLevel")] public bool MaxLevel;
        [DataMember(Name = "rested")] public RestedInfo Rested;

        /// <summary>What this level costs, start to end.</summary>
        public long LevelCost => Math.Max(0, NextLevelXp - LevelXp);

        /// <summary>
        /// Past this level's end: you levelled since the reply, and the next one has the new level's bounds. The bar shows
        /// full until then.
        /// </summary>
        public bool PastLevel(long total) => !MaxLevel && total >= NextLevelXp;

        /// <summary>How far through the level you are, 0 to 1 (1 at the highest level).</summary>
        public float Progress(long total) {
            if (MaxLevel || LevelCost <= 0) {
                return 1f;
            }

            return (float)Math.Max(0, Math.Min(1, (double)(total - LevelXp) / LevelCost));
        }

        /// <summary>XP earned into this level.</summary>
        public long IntoLevel(long total) => Math.Max(0, Math.Min(LevelCost, total - LevelXp));

        /// <summary>XP left to the next level, or 0.</summary>
        public long ToNextLevel(long total) => MaxLevel ? 0 : Math.Max(0, NextLevelXp - total);

        /// <summary>Where rested experience this big ends on the bar, 0 to 1: past your XP, not past the end.</summary>
        public float RestedEnd(long total, long rested) {
            var progress = Progress(total);

            if (MaxLevel || LevelCost <= 0 || rested <= 0) {
                return progress;
            }

            return (float)Math.Min(1, progress + (double)rested / LevelCost);
        }
    }
}
