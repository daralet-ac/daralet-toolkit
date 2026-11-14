using System;
using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// One of the game's experience tables (attributes, vitals, trained or specialized skills): the total XP spent to have
    /// raised it each number of times, from 0, as the client's ExperienceSystem gives it (Ui/ClientTraining.cs). What a
    /// raise costs is the next step's total less what's been spent, as the game's raise buttons work it out.
    /// </summary>
    public class XpTable {
        private readonly long[] totals;

        /// <param name="totals">The XP for each number of raises, from 0, rising; the last is as far as it goes.</param>
        public XpTable(IList<long> totals) {
            this.totals = new long[totals.Count];
            totals.CopyTo(this.totals, 0);
        }

        /// <summary>The most raises there are.</summary>
        public int MaxRank => totals.Length - 1;

        /// <summary>How many raises this much spent XP has bought: the last step it reaches.</summary>
        public int Rank(long spent) {
            var low = 0;
            var high = totals.Length - 1;

            // the last total no greater than what's spent
            while (low < high) {
                var middle = (low + high + 1) / 2;

                if (totals[middle] <= spent) {
                    low = middle;
                }
                else {
                    high = middle - 1;
                }
            }

            return Math.Max(0, low);
        }

        /// <summary>
        /// The XP that raises it this many more times (or as many as are left), and how many that is; null when it's as high
        /// as it goes, or the table is empty.
        /// </summary>
        public (long Cost, int Raises)? Cost(long spent, int raises) {
            if (totals.Length < 2 || raises < 1) {
                return null;
            }

            var rank = Rank(spent);
            var target = Math.Min(MaxRank, rank + raises);

            if (target <= rank) {
                return null;
            }

            return (Math.Max(0, totals[target] - spent), target - rank);
        }
    }
}
