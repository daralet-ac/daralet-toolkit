namespace DaraletPlayer.Feed {
    /// <summary>
    /// When the Link Status window sends a ping, and which answer is whose. The server's PingResponse carries nothing to
    /// tell one from another, so there's only ever one ping out: the next goes once it's answered and the interval has
    /// passed. One unanswered for LostAfter seconds is counted lost; a late answer to it could then be taken for the next
    /// one's, far too quick, so after a loss the next answer is let go by (or DrainAfter seconds pass) before pinging again.
    /// An answer nobody asked for (the game's own link panel pings too) is let go by as well. Times are in seconds, on
    /// any clock that only goes forward.
    /// </summary>
    public sealed class LinkPinger {
        public const double LostAfter = 10;
        public const double DrainAfter = 10;

        private double? sentAt;
        private double? drainingSince;
        private double lastSent = double.NegativeInfinity;

        /// <summary>When the ping out now was sent; null with none out.</summary>
        public double? Waiting => sentAt;

        /// <summary>Whether to send a ping now, every interval seconds at most.</summary>
        public bool ShouldSend(double now, double interval) {
            if (sentAt != null) {
                return false;
            }

            if (drainingSince != null) {
                if (now - drainingSince.Value < DrainAfter) {
                    return false;
                }

                drainingSince = null;
            }

            return now - lastSent >= interval;
        }

        public void Sent(double now) {
            sentAt = now;
            lastSent = now;
        }

        /// <summary>A PingResponse came: how long the ping out took, in milliseconds, or null when it wasn't ours.</summary>
        public double? Answered(double now) {
            if (sentAt == null) {
                drainingSince = null;
                return null;
            }

            var ms = (now - sentAt.Value) * 1000;
            sentAt = null;
            return ms;
        }

        /// <summary>Whether the ping out has gone unanswered too long, and is now lost: when, if so, it was sent.</summary>
        public double? TimedOut(double now) {
            if (sentAt == null || now - sentAt.Value < LostAfter) {
                return null;
            }

            var sent = sentAt.Value;
            sentAt = null;
            drainingSince = now;
            return sent;
        }

        /// <summary>Forgets the ping out (logged out, another character): its answer, if any, is let go by.</summary>
        public void Reset(double now) {
            if (sentAt != null) {
                drainingSince = now;
            }

            sentAt = null;
        }
    }
}
