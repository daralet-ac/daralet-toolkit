using System.IO;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class LinkTests {
        private static byte[] Event(uint eventType) {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream)) {
                writer.Write(CooldownMessages.GameEvent);
                writer.Write(0x50000001u);
                writer.Write(7u);
                writer.Write(eventType);
                writer.Flush();
                return stream.ToArray();
            }
        }

        [TestMethod]
        public void OnlyThePingResponseIsOne() {
            Assert.IsTrue(PingMessages.IsPingResponse(Event(0x01EA)));
            Assert.IsFalse(PingMessages.IsPingResponse(Event(0x002B)));
            Assert.IsFalse(PingMessages.IsPingResponse(new byte[8]));
            Assert.IsFalse(PingMessages.IsPingResponse(null));
        }

        [TestMethod]
        public void OnePingIsOutAtATime() {
            var pinger = new LinkPinger();

            Assert.IsTrue(pinger.ShouldSend(100, 1));
            pinger.Sent(100);
            Assert.IsFalse(pinger.ShouldSend(103, 1), "still waiting for the answer");
            Assert.AreEqual(80, pinger.Answered(100.08).Value, 0.001);
            Assert.IsFalse(pinger.ShouldSend(100.5, 1), "not before the interval");
            Assert.IsTrue(pinger.ShouldSend(101, 1));
        }

        [TestMethod]
        public void AnAnswerNobodyAskedForIsLetGoBy() {
            var pinger = new LinkPinger();

            Assert.IsNull(pinger.Answered(5));
            pinger.Sent(10);
            Assert.AreEqual(50, pinger.Answered(10.05).Value, 0.001);
            Assert.IsNull(pinger.Answered(10.06));
        }

        [TestMethod]
        public void ALostPingsLateAnswerIsntTakenForTheNext() {
            var pinger = new LinkPinger();
            pinger.Sent(0);

            Assert.IsNull(pinger.TimedOut(5));
            Assert.AreEqual(0, pinger.TimedOut(LinkPinger.LostAfter).Value);
            Assert.IsFalse(pinger.ShouldSend(LinkPinger.LostAfter + 1, 1), "waits for the late answer");
            Assert.IsNull(pinger.Answered(LinkPinger.LostAfter + 2), "the late answer is let go by");
            Assert.IsTrue(pinger.ShouldSend(LinkPinger.LostAfter + 2, 1));
        }

        [TestMethod]
        public void PingingGoesOnIfTheLateAnswerNeverComes() {
            var pinger = new LinkPinger();
            pinger.Sent(0);
            pinger.TimedOut(LinkPinger.LostAfter);

            Assert.IsTrue(pinger.ShouldSend(LinkPinger.LostAfter + LinkPinger.DrainAfter, 1));
        }

        [TestMethod]
        public void ASpikeIsWellOverTheUsual() {
            var history = new LinkHistory();

            for (var i = 0; i < 10; i++) {
                history.Add(i, 80, IndicatorOptions.LinkGood);
            }

            Assert.IsFalse(history.Add(10, 200, null).IsSpike(150), "120 over isn't enough");
            Assert.IsTrue(history.Add(11, 240, null).IsSpike(150));
            Assert.IsTrue(history.Add(12, null, null).IsSpike(150), "lost always counts");

            var slow = new LinkHistory();

            for (var i = 0; i < 10; i++) {
                slow.Add(i, 400, null);
            }

            Assert.IsFalse(slow.Add(10, 560, null).IsSpike(150), "not half again as long");
            Assert.IsTrue(slow.Add(11, 600, null).IsSpike(150));
        }

        [TestMethod]
        public void NoSpikesBeforeThereIsAUsual() {
            var history = new LinkHistory();

            Assert.IsNull(history.Add(0, 900, null).Baseline);
            Assert.IsFalse(history.Samples[0].IsSpike(150));
        }

        [TestMethod]
        public void StatsCoverOnlyTheTimeShown() {
            var history = new LinkHistory();
            history.Add(0, 1000, null);

            for (var i = 1; i <= 10; i++) {
                history.Add(i, i % 2 == 0 ? 100 : 80, null);
            }

            history.Add(11, null, null);
            var stats = history.Stats(1, 150);

            Assert.AreEqual(11, stats.Count);
            Assert.AreEqual(1, stats.Lost);
            Assert.AreEqual(1, stats.Spikes);
            Assert.AreEqual(80, stats.Lowest);
            Assert.AreEqual(100, stats.Highest);
            Assert.AreEqual(90, stats.Median);
            Assert.AreEqual(20, stats.Jitter);
        }

        [TestMethod]
        public void TheHistoryKeepsAnHour() {
            var history = new LinkHistory();

            for (var i = 0; i < LinkHistory.Capacity * 2; i++) {
                history.Add(i, 50, null);
            }

            Assert.IsTrue(history.Samples.Count <= LinkHistory.Capacity + 60);
            Assert.AreEqual(LinkHistory.Capacity * 2 - 1, history.Samples.Last().At);
        }

        [TestMethod]
        public void TheScaleTopsOutAtARoundNumber() {
            Assert.AreEqual(100, LinkHistory.ScaleTop(40));
            Assert.AreEqual(200, LinkHistory.ScaleTop(101));
            Assert.AreEqual(250, LinkHistory.ScaleTop(240));
            Assert.AreEqual(500, LinkHistory.ScaleTop(251));
            Assert.AreEqual(1000, LinkHistory.ScaleTop(999));
            Assert.AreEqual(2500, LinkHistory.ScaleTop(2100));
            Assert.AreEqual(10000, LinkHistory.ScaleTop(9000));
        }

        [TestMethod]
        public void TimesReadAsWords() {
            Assert.AreEqual("45s", LinkHistory.Ago(45));
            Assert.AreEqual("3m 05s", LinkHistory.Ago(185));
            Assert.AreEqual("1h 02m", LinkHistory.Ago(3720));
        }

        [TestMethod]
        public void OddSettingsAreHeldInRange() {
            var options = new LinkOptions { SpanMinutes = 7, IntervalSeconds = 0, SpikeMargin = 99999 };

            Assert.AreEqual(5, options.ClampedSpan);
            Assert.AreEqual(LinkOptions.MinInterval, options.ClampedInterval);
            Assert.AreEqual(LinkOptions.MaxSpikeMargin, options.ClampedSpikeMargin);
        }
    }
}
