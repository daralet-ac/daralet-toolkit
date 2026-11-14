namespace DaraletPlayer.Feed {
    /// <summary>One ping of the Link Status window's (LinkHistory).</summary>
    public sealed class LinkSample {
        /// <summary>When it was sent, in seconds on the history's clock.</summary>
        public double At;

        /// <summary>How long the answer took, in milliseconds; null for one never answered (LinkPinger.LostAfter).</summary>
        public double? Ms;

        /// <summary>
        /// The usual round trip when it was sent: the median of the answered pings before it (LinkHistory.BaselineOf), or
        /// null with too few to say. A spike is measured against it.
        /// </summary>
        public double? Baseline;

        /// <summary>The game's link indicator then (IndicatorOptions.LinkGood...); null if unreadable.</summary>
        public uint? GameState;

        public bool Lost => Ms == null;

        /// <summary>
        /// A lag spike: lost, or at least margin milliseconds over the usual and half again as long. Both, so a quiet
        /// 20ms connection going to 40 isn't one, nor a 300ms one going to 400.
        /// </summary>
        public bool IsSpike(double margin) {
            if (Ms == null) {
                return true;
            }

            return Baseline != null && Ms.Value >= Baseline.Value + margin && Ms.Value >= Baseline.Value * 1.5;
        }
    }
}
