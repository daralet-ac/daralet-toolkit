namespace DaraletPlayer.Feed {
    /// <summary>What the pings over a stretch of time come to (LinkHistory.Stats).</summary>
    public sealed class LinkStats {
        /// <summary>Pings sent, answered or not.</summary>
        public int Count;
        /// <summary>Pings never answered.</summary>
        public int Lost;
        /// <summary>Lag spikes, lost pings included (LinkSample.IsSpike).</summary>
        public int Spikes;

        // over the answered pings, in milliseconds; null with none answered
        public double? Average;
        public double? Lowest;
        public double? Highest;
        public double? Median;

        /// <summary>
        /// How much one answered ping differs from the next, on average (milliseconds); null with fewer than two.
        /// </summary>
        public double? Jitter;

        /// <summary>The share of pings lost, 0 to 1.</summary>
        public double LostShare => Count == 0 ? 0 : (double)Lost / Count;
    }
}
