using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class FeedTests {
        // The JSON in these is what daralet-ACE's PlayerUiProtocolTests pin the server to writing.

        [TestMethod]
        public void ReadsTargetsAsTheServerWritesThem() {
            var json = "[{\"guid\":2147488308,\"status\":\"ok\",\"name\":\"Drudge\",\"level\":50,\"health\":100,\"maxHealth\":200,"
                + "\"stamina\":10,\"maxStamina\":20,\"mana\":5,\"maxMana\":50,\"distance\":12.5,\"attackTarget\":1342177281,"
                + "\"attackTargetName\":\"Bob\",\"enchantments\":[{\"spellId\":1,\"name\":\"Fester\",\"level\":7,"
                + "\"school\":\"LifeMagic\",\"beneficial\":false,\"secondsLeft\":30,\"casterGuid\":1342177281,"
                + "\"casterName\":\"Bob\",\"yours\":true,\"iconId\":100667956}],\"threat\":[{\"guid\":1342177281,"
                + "\"name\":\"Bob\",\"threat\":400,\"isYou\":true,\"isPlayer\":true}]}]";

            var target = Json.Read<List<TargetInfo>>(json)[0];

            Assert.AreEqual(0x80001234u, target.Guid);
            Assert.IsTrue(target.IsOk);
            Assert.AreEqual("Drudge", target.Name);
            Assert.AreEqual(200u, target.MaxHealth);
            Assert.AreEqual(12.5f, target.Distance);
            Assert.AreEqual(0x50000001u, target.AttackTarget);
            Assert.AreEqual("Fester", target.Enchantments[0].Name);
            Assert.IsFalse(target.Enchantments[0].Beneficial);
            Assert.IsTrue(target.Enchantments[0].Yours);
            Assert.AreEqual(30, target.Enchantments[0].SecondsLeft);
            Assert.AreEqual(400, target.Threat[0].Threat);
            Assert.IsTrue(target.Threat[0].IsYou);
        }

        [TestMethod]
        public void ReadsGoneAndPlayerTargets() {
            var json = "[{\"guid\":1,\"status\":\"gone\",\"name\":null,\"level\":0,\"health\":0,\"maxHealth\":0,\"stamina\":0,"
                + "\"maxStamina\":0,\"mana\":0,\"maxMana\":0,\"distance\":null,\"attackTarget\":null,\"attackTargetName\":null,"
                + "\"enchantments\":null,\"threat\":null},"
                + "{\"guid\":2,\"status\":\"player\",\"name\":\"Alice\",\"level\":0,\"health\":0,\"maxHealth\":0,\"stamina\":0,"
                + "\"maxStamina\":0,\"mana\":0,\"maxMana\":0,\"distance\":3,\"attackTarget\":null,\"attackTargetName\":null,"
                + "\"enchantments\":null,\"threat\":null}]";

            var targets = Json.Read<List<TargetInfo>>(json);

            Assert.AreEqual(TargetInfo.StatusGone, targets[0].Status);
            Assert.IsNull(targets[0].Distance);
            Assert.IsNull(targets[0].AttackTarget);
            Assert.IsNull(targets[0].Threat);
            Assert.IsTrue(targets[1].IsPlayer);
            Assert.AreEqual("Alice", targets[1].Name);
        }

        [TestMethod]
        public void ReadsTheFellowshipAndNoFellowship() {
            var json = "{\"name\":\"Raiders\",\"leader\":1342177281,\"open\":true,\"shareXp\":true,\"evenShare\":false,\"locked\":false,"
                + "\"members\":[{\"guid\":1342177281,\"name\":\"Bob\",\"level\":126,\"health\":1,\"maxHealth\":2,\"stamina\":3,"
                + "\"maxStamina\":4,\"mana\":5,\"maxMana\":6,\"distance\":null,\"location\":\"Holtburg (42.1N, 33.6E)\"}]}";

            var fellowship = Json.Read<FellowshipInfo>(json);

            Assert.AreEqual("Raiders", fellowship.Name);
            Assert.AreEqual(0x50000001u, fellowship.Leader);
            Assert.IsTrue(fellowship.ShareXp);
            Assert.AreEqual(6u, fellowship.Members[0].MaxMana);
            Assert.IsNull(fellowship.Members[0].Distance);
            Assert.AreEqual("Holtburg (42.1N, 33.6E)", fellowship.Members[0].Location);

            Assert.IsNull(Json.Read<FellowshipInfo>("null"));
        }

        [TestMethod]
        public void ReadsAFellowsFocusAndSpells() {
            // the shape @playerui fellowship spells sends, nulls left out (the second fellow wears no focus)
            var json = "{\"name\":\"Raiders\",\"leader\":1,\"members\":["
                + "{\"guid\":1,\"name\":\"Bob\",\"focus\":{\"name\":\"Combat Focus\",\"icon\":100667956,\"underlay\":0,"
                + "\"overlay\":1},\"buffs\":[2,3],\"debuffs\":[4]},"
                + "{\"guid\":2,\"name\":\"Al\",\"distance\":3}]}";

            var fellowship = Json.Read<FellowshipInfo>(json);

            Assert.AreEqual(0x06001234u, fellowship.Members[0].Focus.Icon);
            Assert.AreEqual("Combat Focus", fellowship.Members[0].Focus.Name);
            CollectionAssert.AreEqual(new[] { 2, 3 }, fellowship.Members[0].Buffs);
            CollectionAssert.AreEqual(new[] { 4 }, fellowship.Members[0].Debuffs);
            Assert.IsNull(fellowship.Members[1].Focus);
            Assert.IsNull(fellowship.Members[1].Buffs);
            Assert.AreEqual(3f, fellowship.Members[1].Distance);
        }

        private static FellowInfo Fellow(uint guid, string name, uint health, float? distance) {
            return new FellowInfo { Guid = guid, Name = name, Health = health, MaxHealth = 100, Distance = distance };
        }

        [TestMethod]
        public void BigFellowshipsShowPinnedFirstThenSorted() {
            var members = new List<FellowInfo> {
                Fellow(1, "Leader", 90, 0), Fellow(2, "Bo", 20, 30), Fellow(3, "Al", 60, null), Fellow(4, "Cy", 40, 5),
            };

            var byHealth = FellowshipOrder.Arrange(members, new List<string> { "cy" }, FellowshipOrder.ByHealth, false, 0, 1);
            CollectionAssert.AreEqual(new[] { "Cy", "Bo", "Al", "Leader" }, byHealth.Select(m => m.Name).ToArray());

            var near = FellowshipOrder.Arrange(members, null, FellowshipOrder.ByDistance, true, 2, 1);
            CollectionAssert.AreEqual(new[] { "Leader", "Cy" }, near.Select(m => m.Name).ToArray(), "Al is too far to say");

            var asSent = FellowshipOrder.Arrange(members, null, FellowshipOrder.ByFellowship, false, 0, 1);
            CollectionAssert.AreEqual(new[] { "Leader", "Bo", "Al", "Cy" }, asSent.Select(m => m.Name).ToArray());
        }

        [TestMethod]
        public void PagesSplitMembersAndTheSizeLimitComesFromTheServer() {
            var members = Enumerable.Range(1, 23).Select(i => Fellow((uint)i, "F" + i, 100, 1)).ToList();

            var third = FellowshipOrder.Page(members, 10, 2, out var pages);

            Assert.AreEqual(3, pages);
            CollectionAssert.AreEqual(new[] { "F21", "F22", "F23" }, third.Select(m => m.Name).ToArray());
            Assert.AreEqual(9, FellowshipOrder.MaxMembers(new FellowshipInfo()), "an older server doesn't say");
            Assert.AreEqual(int.MaxValue, FellowshipOrder.MaxMembers(
                Json.Read<FellowshipInfo>("{\"name\":\"Defense\",\"members\":[],\"maxMembers\":2147483647}")));
        }

        [TestMethod]
        public void ReadsTheSocialReply() {
            // the server's own test pins this same JSON (PlayerUiProtocolTests.SocialJsonHasTheNamesThePluginReads)
            var json = "{\"appearOffline\":true,\"ignoreFellowshipRequests\":false,\"autoAcceptFellowshipRequests\":true,"
                + "\"shareFellowshipXp\":true,\"shareFellowshipLoot\":false,"
                + "\"friends\":[{\"guid\":1342177282,\"name\":\"Al\",\"online\":true}],"
                + "\"squelches\":[{\"guid\":1342177283,\"name\":\"Cy\",\"account\":true}],"
                + "\"allegiance\":{\"name\":\"Order\",\"rank\":3,\"followers\":7,\"xpGenerated\":1000,\"xpReceived\":2000,"
                + "\"monarch\":{\"guid\":1342177284,\"name\":\"Mo\",\"level\":200,\"rank\":6,\"online\":false,\"xpGenerated\":0},"
                + "\"patron\":null,"
                + "\"vassals\":[{\"guid\":1342177285,\"name\":\"Vi\",\"level\":40,\"rank\":1,\"online\":true,\"xpGenerated\":50}]}}";

            var social = Json.Read<SocialInfo>(json);

            Assert.IsTrue(social.AppearOffline);
            Assert.IsTrue(social.AutoAcceptFellowshipRequests);
            Assert.IsTrue(social.ShareFellowshipXp);
            Assert.IsFalse(social.ShareFellowshipLoot);
            Assert.AreEqual("Al", social.Friends[0].Name);
            Assert.IsTrue(social.Friends[0].Online);
            Assert.IsTrue(social.Squelches[0].Account);
            Assert.AreEqual(7, social.Allegiance.Followers);
            Assert.AreEqual(3u, social.Allegiance.Rank);
            Assert.AreEqual(2000ul, social.Allegiance.XpReceived);
            Assert.AreEqual("Mo", social.Allegiance.Monarch.Name);
            Assert.IsNull(social.Allegiance.Patron);
            Assert.AreEqual(50ul, social.Allegiance.Vassals[0].XpGenerated);
            Assert.IsNull(Json.Read<SocialInfo>("{\"friends\":[],\"squelches\":[],\"allegiance\":null}").Allegiance);
        }

        [TestMethod]
        public void TimeLeftCountsDownFromTheReply() {
            Assert.AreEqual("30s", Display.TimeLeft(30, 0));
            // rounded up, like a countdown: it reads 30s until a whole second has gone
            Assert.AreEqual("30s", Display.TimeLeft(30, 0.5));
            Assert.AreEqual("29s", Display.TimeLeft(30, 1.2));
            Assert.AreEqual("0s", Display.TimeLeft(30, 45));
            Assert.AreEqual("4:05", Display.TimeLeft(245, 0));
            Assert.AreEqual("1h 02m", Display.TimeLeft(3725, 0));
            Assert.AreEqual("", Display.TimeLeft(-1, 10));
        }

        [TestMethod]
        public void SmallerPieces() {
            Assert.AreEqual("13m", Display.Distance(12.6f));
            Assert.AreEqual("", Display.Distance(null));

            Assert.AreEqual(0.5f, Display.ThreatShare(200, 400));
            Assert.AreEqual(1f, Display.ThreatShare(500, 400));
            Assert.AreEqual(0f, Display.ThreatShare(10, 0));

            Assert.AreEqual("Life", Display.School("LifeMagic"));
            Assert.AreEqual("Creature", Display.School("CreatureEnchantment"));
            Assert.AreEqual("", Display.School("None"));
        }

        [TestMethod]
        public void TargetArgumentsAreEachGuidOnceInHex() {
            Assert.AreEqual("0x80001234 0x80005678", Display.TargetArguments(0x80001234, 0x80005678));
            Assert.AreEqual("0x80001234", Display.TargetArguments(0x80001234, 0x80001234));
            Assert.AreEqual("0x80005678", Display.TargetArguments(0, 0x80005678));
            Assert.AreEqual("", Display.TargetArguments(0, 0));
        }
    }
}
