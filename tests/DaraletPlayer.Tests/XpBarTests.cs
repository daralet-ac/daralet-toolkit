using System;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class XpBarTests {
        [TestMethod]
        public void ReadsXpAsTheServerWritesIt() {
            // PlayerUiProtocolTests.XpSendsTheNamesThePluginReads in daralet-ACE pins the same JSON
            var json = "{\"level\":12,\"totalXp\":6500,\"levelXp\":5000,\"nextLevelXp\":7000,\"maxLevel\":false,"
                + "\"rested\":{\"enabled\":true,\"state\":\"resting\",\"rested\":1200,\"max\":3000,\"bonus\":1.5,"
                + "\"perHour\":250,\"earnedThisSession\":40,\"levelCost\":2000,\"slowdownPerLevel\":0.5,"
                + "\"minLevel\":10,\"offlineRate\":0.5}}";

            var xp = Json.Read<XpInfo>(json);

            Assert.AreEqual(12, xp.Level);
            Assert.AreEqual(6500, xp.TotalXp);
            Assert.AreEqual(2000, xp.LevelCost);
            Assert.IsFalse(xp.MaxLevel);

            var rested = xp.Rested;
            Assert.IsTrue(rested.Enabled);
            Assert.IsTrue(rested.IsResting);
            Assert.AreEqual(1200, rested.Rested);
            Assert.AreEqual(3000, rested.Max);
            Assert.AreEqual(1.5, rested.Bonus, 0.0001);
            Assert.AreEqual(250, rested.PerHour);
            Assert.AreEqual(40, rested.EarnedThisSession);
            Assert.AreEqual(2000, rested.LevelCost);
            Assert.AreEqual(0.5, rested.SlowdownPerLevel, 0.0001);
            Assert.AreEqual(10, rested.MinLevel);
            Assert.AreEqual(0.5, rested.OfflineRate.Value, 0.0001);
            Assert.AreEqual("+150% kill XP", rested.BonusText);
        }

        [TestMethod]
        public void TheBarIsTheWayThroughTheLevel() {
            var xp = new XpInfo { Level = 12, LevelXp = 5000, NextLevelXp = 7000 };

            Assert.AreEqual(0.75f, xp.Progress(6500), 0.0001f);
            Assert.AreEqual(1500, xp.IntoLevel(6500));
            Assert.AreEqual(500, xp.ToNextLevel(6500));
            // levelled since the reply: full until the next one says the new level
            Assert.AreEqual(1f, xp.Progress(7300), 0.0001f);
            Assert.IsTrue(xp.PastLevel(7300));
        }

        [TestMethod]
        public void RestedIsShadedAheadNoFurtherThanTheEnd() {
            var xp = new XpInfo { LevelXp = 5000, NextLevelXp = 7000 };

            Assert.AreEqual(0.75f, xp.RestedEnd(5500, 1000), 0.0001f);
            Assert.AreEqual(1f, xp.RestedEnd(6500, 3000), 0.0001f);
            Assert.AreEqual(0.25f, xp.RestedEnd(5500, 0), 0.0001f);
        }

        [TestMethod]
        public void TheHighestLevelIsAFullBar() {
            var xp = new XpInfo { Level = 275, LevelXp = 9000, NextLevelXp = 9000, MaxLevel = true };

            Assert.AreEqual(1f, xp.Progress(9500), 0.0001f);
            Assert.AreEqual(0, xp.ToNextLevel(9500));
            Assert.AreEqual("Lv 275   Max level", new XpBarOptions().BarText(xp, 9500));
        }

        [TestMethod]
        public void BarTextFollowsTheOptions() {
            var xp = new XpInfo { Level = 12, LevelXp = 5000, NextLevelXp = 55000 };
            var options = new XpBarOptions();

            Assert.AreEqual("Lv 12   12.3k / 50k (24.6%)", options.BarText(xp, 17300));

            options.Text = XpBarOptions.TextPercent;
            options.ShowLevel = false;
            Assert.AreEqual("24.6%", options.BarText(xp, 17300));

            options.Text = XpBarOptions.TextNumbers;
            options.ShortNumbers = false;
            Assert.AreEqual("12,300 / 50,000", options.BarText(xp, 17300));

            options.Text = XpBarOptions.TextNone;
            Assert.AreEqual("", options.BarText(xp, 17300));
        }

        [TestMethod]
        public void GrowsBetweenRepliesOnlyWhileResting() {
            var resting = new RestedInfo { State = RestedInfo.Resting, Rested = 1000, Max = 3000, PerHour = 3600 };
            var away = new RestedInfo { State = RestedInfo.Away, Rested = 1000, Max = 3000, PerHour = 3600 };

            Assert.AreEqual(1010, resting.RestedAfter(10));
            Assert.AreEqual(3000, resting.RestedAfter(100_000));
            Assert.AreEqual(1000, away.RestedAfter(10));
        }

        [TestMethod]
        public void KillsSinceTheReplySpendTheBonusShare() {
            // doubled kills: of 600 earned, 300 was the bonus
            var rested = new RestedInfo { State = RestedInfo.Away, Rested = 1000, Max = 3000, Bonus = 1.0 };

            Assert.AreEqual(700, rested.RestedAfter(0, 600));
            Assert.AreEqual(0, rested.RestedAfter(0, 5000));
        }

        [TestMethod]
        public void SlowsPastEachFullLevel() {
            // 500 an hour to a level's cost (1,000), then half that to the most (1,500), as the server has it
            var rested = new RestedInfo {
                State = RestedInfo.Resting, Rested = 0, Max = 1500, PerHour = 500, LevelCost = 1000, SlowdownPerLevel = 0.5,
            };

            Assert.AreEqual(1250, rested.RestedAfter(3 * 3600));
            Assert.AreEqual(4 * 3600, rested.SecondsToFull(0).Value, 0.001);
            Assert.AreEqual(250, rested.RateAt(1200), 0.001);
            Assert.IsTrue(rested.IsSlowed(1200));
            Assert.IsFalse(rested.IsSlowed(999));

            rested.SlowdownPerLevel = 1;
            Assert.IsNull(rested.SecondsToFull(0));
        }

        [TestMethod]
        public void WithNoMostItKeepsGrowingEverSlower() {
            var rested = new RestedInfo {
                State = RestedInfo.Resting, Rested = 0, Max = 0, PerHour = 500, LevelCost = 1000, SlowdownPerLevel = 0.5,
            };

            // a level's worth in 2 hours, the next in 4, the next in 8
            Assert.IsFalse(rested.HasMost);
            Assert.AreEqual(2000, rested.RestedAfter(6 * 3600));
            Assert.AreEqual(3000, rested.RestedAfter(14 * 3600));
            Assert.IsNull(rested.SecondsToFull(0));
        }

        [TestMethod]
        public void SaysWhenRestStarts() {
            var rested = new RestedInfo { State = RestedInfo.LowLevel, MinLevel = 10 };

            Assert.AreEqual("Rest starts at level 10", rested.StateText);
            Assert.IsFalse(rested.CanRest);
        }

        [TestMethod]
        public void SaysWhenRestHasEnded() {
            Assert.AreEqual("No rest at your level", new RestedInfo { State = RestedInfo.NoRest }.StateText);
        }

        [TestMethod]
        public void SaysHowLongUntilFull() {
            var rested = new RestedInfo { State = RestedInfo.Resting, Rested = 1000, Max = 3000, PerHour = 1000 };

            Assert.AreEqual(7200, rested.SecondsToFull(1000).Value, 0.001);
            Assert.AreEqual(0, rested.SecondsToFull(3000).Value, 0.001);
            Assert.AreEqual(0.5f, rested.Fraction(1500), 0.0001f);
        }

        [TestMethod]
        public void XpPerHourOverTheWindow() {
            var start = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
            var rate = new XpRate();
            rate.Reset(start, 1000);

            // too soon to say
            rate.Note(start.AddSeconds(10), 5000);
            Assert.IsNull(rate.PerHour(start.AddSeconds(10), TimeSpan.Zero));

            // 30,000 in the first half hour, then nothing for the next
            rate.Note(start.AddMinutes(30), 31000);

            Assert.AreEqual(30000, rate.Gained);
            Assert.AreEqual(60000, rate.PerHour(start.AddMinutes(30), TimeSpan.Zero).Value, 0.01);
            Assert.AreEqual(30000, rate.PerHour(start.AddMinutes(60), TimeSpan.Zero).Value, 0.01);
            Assert.AreEqual(0, rate.PerHour(start.AddMinutes(60), TimeSpan.FromMinutes(15)).Value, 0.01);
        }

        [TestMethod]
        public void XpPerHourStartsAgainWhenXpGoesDown() {
            var start = new DateTime(2026, 11, 11, 12, 0, 0, DateTimeKind.Utc);
            var rate = new XpRate();
            rate.Reset(start, 50000);
            rate.Note(start.AddMinutes(5), 60000);

            // another character
            rate.Note(start.AddMinutes(6), 2000);

            Assert.AreEqual(0, rate.Gained);
            Assert.AreEqual(start.AddMinutes(6), rate.StartedAt);
        }

        [TestMethod]
        public void ShortNumbers() {
            Assert.AreEqual("950", XpNumbers.Short(950));
            Assert.AreEqual("12.3k", XpNumbers.Short(12_345));
            Assert.AreEqual("999k", XpNumbers.Short(999_999));
            Assert.AreEqual("1.5M", XpNumbers.Short(1_500_000));
            Assert.AreEqual("123M", XpNumbers.Short(123_456_789));
            Assert.AreEqual("2.34B", XpNumbers.Short(2_345_678_901));
        }
    }
}
