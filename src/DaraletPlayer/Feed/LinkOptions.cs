using System;
using System.Runtime.Serialization;

namespace DaraletPlayer.Feed {
    /// <summary>The Link Status window's own choices (Ui/LinkPanel.cs), in settings.json as "link".</summary>
    [DataContract]
    public class LinkOptions {
        /// <summary>How far back the graph goes, in minutes, as the window offers them.</summary>
        public static readonly int[] Spans = { 1, 5, 15, 60 };

        public const int MinInterval = 1;
        public const int MaxInterval = 10;
        public const int MinSpikeMargin = 50;
        public const int MaxSpikeMargin = 2000;

        /// <summary>How far back the graph and the numbers go, in minutes (one of Spans).</summary>
        [DataMember(Name = "spanMinutes")] public int SpanMinutes;
        /// <summary>Seconds between pings.</summary>
        [DataMember(Name = "intervalSeconds")] public int IntervalSeconds;
        /// <summary>How many milliseconds over the usual round trip make a lag spike (LinkSample.IsSpike).</summary>
        [DataMember(Name = "spikeMargin")] public int SpikeMargin;
        /// <summary>
        /// Goes on pinging with the window closed, once it's been opened, so the history's there when it next opens.
        /// </summary>
        [DataMember(Name = "keepMeasuring")] public bool KeepMeasuring;
        /// <summary>The list of recent spikes under the graph.</summary>
        [DataMember(Name = "showSpikeList")] public bool ShowSpikeList;

        public LinkOptions() {
            SetDefaults();
        }

        public int ClampedSpan => Array.IndexOf(Spans, SpanMinutes) >= 0 ? SpanMinutes : 5;
        public int ClampedInterval => Math.Max(MinInterval, Math.Min(MaxInterval, IntervalSeconds));
        public int ClampedSpikeMargin => Math.Max(MinSpikeMargin, Math.Min(MaxSpikeMargin, SpikeMargin));

        [OnDeserializing]
        private void OnDeserializing(StreamingContext context) {
            SetDefaults();
        }

        private void SetDefaults() {
            SpanMinutes = 5;
            IntervalSeconds = 1;
            SpikeMargin = 150;
            KeepMeasuring = true;
            ShowSpikeList = true;
        }
    }
}
