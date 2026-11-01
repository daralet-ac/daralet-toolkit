using System;
using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class ChatTests {
        private static readonly DateTime At = new DateTime(2026, 10, 31, 14, 5, 9);

        /// <summary>Every letter 7 pixels wide, a space 3: easy sums.</summary>
        private static float Advance(char letter) => letter == ' ' ? 3 : 7;

        [TestMethod]
        public void ChatTypesSortIntoTheGamesCategories() {
            // the game's ChatDisplayMask bits (daralet-ACE libs/entity/Enum/ChatDisplayMask.cs), bit n being chat type n
            var masks = new Dictionary<ChatCategory, long> {
                { ChatCategory.AreaChat, 0x1004 },
                { ChatCategory.Tells, 0x18 },
                { ChatCategory.Combat, 0x600040 },
                { ChatCategory.Magic, 0x20080 },
                { ChatCategory.Allegiance, 0x40C00 },
                { ChatCategory.Fellowship, 0x80000 },
                { ChatCategory.Errors, 0x4000000 },
                { ChatCategory.General, 0x8000000 },
                { ChatCategory.Trade, 0x10000000 },
                { ChatCategory.Lfg, 0x20000000 },
                { ChatCategory.Roleplay, 0x40000000 },
                { ChatCategory.Gameplay, 0x3912021 },
                { ChatCategory.Alerts, 0xC302 },
            };

            foreach (var pair in masks) {
                for (var type = 0; type < 31; type++) {
                    if ((pair.Value & (1L << type)) != 0) {
                        Assert.AreEqual(pair.Key, ChatTypeInfo.For(type).Category, $"chat type {type}");
                    }
                }
            }

            Assert.AreEqual(ChatCategory.Tells, ChatTypeInfo.For(0x1F).Category, "admin tells");
            Assert.AreEqual(ChatCategory.Gameplay, ChatTypeInfo.For(99).Category, "a type the game doesn't have");
            Assert.AreEqual(32, ChatTypeInfo.All.Length);

            for (var i = 0; i < ChatTypeInfo.All.Length; i++) {
                Assert.AreEqual(i, ChatTypeInfo.All[i].Type);
            }
        }

        [TestMethod]
        public void AChannelSaysWhichItIsInFront() {
            Assert.AreEqual(ChatCategory.Trade, ChatCategoryInfo.Of(0, "[Trade] Bob says, \"WTS sword\""));
            Assert.AreEqual(ChatCategory.Society, ChatCategoryInfo.Of(0, "[Celestial Hand] Bob says, \"hi\""));
            Assert.AreEqual(ChatCategory.Olthoi, ChatCategoryInfo.Of(0, "[Olthoi] Bob says, \"hi\""));
            Assert.AreEqual(ChatCategory.AreaChat, ChatCategoryInfo.Of(2, "Bob says, \"[Trade] isn't here\""));
            Assert.AreEqual(ChatCategory.Gameplay, ChatCategoryInfo.Of(5, "[Daralet Toolkit] hello"));
        }

        [TestMethod]
        public void CategoryKeysAreUniqueAndInOrder() {
            var keys = ChatCategoryInfo.All.Select(c => c.Key).ToList();
            CollectionAssert.AllItemsAreUnique(keys);

            for (var i = 0; i < ChatCategoryInfo.All.Length; i++) {
                Assert.AreEqual(i, (int)ChatCategoryInfo.All[i].Category);
                Assert.AreSame(ChatCategoryInfo.All[i], ChatCategoryInfo.ForKey(keys[i]));
            }
        }

        [TestMethod]
        public void LinksAreReadWithEitherClosingTag() {
            var segments = ChatMarkup.Parse("<Tell:IIDString:1342177281:Holmez>Holmez<\\Tell> tells you, \"hi\"");

            Assert.AreEqual(2, segments.Count);
            Assert.AreEqual(ChatSegmentKind.Link, segments[0].Kind);
            Assert.AreEqual(1342177281, segments[0].LinkId);
            Assert.AreEqual("Holmez", segments[0].LinkText);
            Assert.AreEqual("Holmez", segments[0].Text);
            Assert.AreEqual(" tells you, \"hi\"", segments[1].Text);

            var plugin = ChatMarkup.Parse("Out now: <Tell:IIDString:1147237985:openurl|https://example.com>Download it</Tell>, then.");
            Assert.AreEqual(3, plugin.Count);
            Assert.AreEqual("openurl|https://example.com", plugin[1].LinkText);
            Assert.AreEqual("Download it", plugin[1].Text);
            Assert.AreEqual("Out now: Download it, then.", ChatMarkup.PlainText(plugin));
        }

        [TestMethod]
        public void ABrokenLinkIsText() {
            var text = "a <Tell:IIDString:5:x>no end";
            Assert.AreEqual(text, ChatMarkup.PlainText(ChatMarkup.Parse(text)));
            Assert.IsTrue(ChatMarkup.Parse(text).All(s => s.Kind == ChatSegmentKind.Plain));

            var notNumber = "<Tell:IIDString:abc:x>y</Tell>";
            Assert.AreEqual(notNumber, ChatMarkup.PlainText(ChatMarkup.Parse(notNumber)));
        }

        [TestMethod]
        public void WhatASpeakerSaysInBracketsMayBeAnItem() {
            var line = ChatLine.Read(1, "<Tell:IIDString:5:Bob>Bob<\\Tell> says, \"WTS [Gold Sword of Fire], offers\"", 2, At);
            var item = line.Segments.Single(s => s.Kind == ChatSegmentKind.Item);

            Assert.AreEqual("[Gold Sword of Fire]", item.Text);
            Assert.AreEqual(ChatItemLink.Key("Bob", "Gold Sword of Fire"), item.ItemKey);
            Assert.AreEqual("Bob says, \"WTS [Gold Sword of Fire], offers\"", line.Text, "the words are unchanged");
        }

        [TestMethod]
        public void TheSpeakerIsWhoeverTheLineSays() {
            string SpeakerOf(string raw) => ChatMarkup.Speaker(ChatMarkup.Parse(raw), "Me");

            Assert.AreEqual("+Archer town", SpeakerOf("[General] <Tell:IIDString:5:+Archer town>+Archer town<\\Tell> says, \"hi\""));
            Assert.AreEqual("Bob", SpeakerOf("[Trade] Bob says, \"hi\""));
            Assert.AreEqual("Arwen", SpeakerOf("Arwen tells you, \"hi\""));
            Assert.AreEqual("Me", SpeakerOf("You say, \"hi\""));
            Assert.AreEqual("Me", SpeakerOf("You tell <Tell:IIDString:5:Bob>Bob<\\Tell>, \"hi\""));
            Assert.IsNull(SpeakerOf("You have entered the General channel."));
            Assert.IsNull(ChatMarkup.Speaker(ChatMarkup.Parse("You say, \"hi\""), null));
        }

        [TestMethod]
        public void BracketsOutsideSpeechArentItems() {
            Assert.IsTrue(ChatLine.Read(1, "[Trade] Bob says, \"hi\"", 2, At).Segments.All(s => s.Kind != ChatSegmentKind.Item),
                "a channel's name");
            Assert.IsTrue(ChatLine.Read(2, "You got a [Gold Sword].", 5, At, "Me").Segments.All(s => s.Kind != ChatSegmentKind.Item),
                "nobody said it");
            Assert.AreEqual(1, ChatLine.Read(3, "You say, \"[Gold Sword]\"", 2, At, "Me").Segments.Count(s => s.Kind == ChatSegmentKind.Item));
        }

        [TestMethod]
        public void AnItemKeyIsOneWordAndIgnoresCaseAndAPlus() {
            Assert.AreEqual(ChatItemLink.Key("Archer Town", "steel sword"), ChatItemLink.Key("+Archer town", "Steel Sword"));
            Assert.AreNotEqual(ChatItemLink.Key("Ann", "Sword"), ChatItemLink.Key("Bob", "Sword"));
            Assert.IsTrue(ChatItemLink.Key("Bob", "ÿþý>>>???").All(c => char.IsLetterOrDigit(c) || c == '-' || c == '_'));
        }

        [TestMethod]
        public void TellsSayWhoToReplyTo() {
            var to = ChatLine.Read(1, "<Tell:IIDString:1342177281:Holmez>Holmez<\\Tell> tells you, \"hi\"\n", 3, At);
            Assert.IsTrue(to.TellToYou);
            Assert.AreEqual("Holmez", to.Name);
            Assert.AreEqual(ChatCategory.Tells, to.Category);
            Assert.AreEqual("Holmez tells you, \"hi\"", to.Text);

            var plain = ChatLine.Read(2, "Arwen tells you, \"yo\"", 3, At);
            Assert.AreEqual("Arwen", plain.Name);

            var from = ChatLine.Read(3, "You tell Brin Holt, \"ok\"", 4, At);
            Assert.IsTrue(from.TellFromYou);
            Assert.AreEqual("Brin Holt", from.Name);

            Assert.IsFalse(ChatLine.Read(4, "Bob says, \"Arwen tells you, nothing\"", 2, At).TellToYou);
        }

        [TestMethod]
        public void NamesAreToldFromPluginsLinks() {
            Assert.IsTrue(ChatMarkup.LooksLikeName("+Spellsword"));
            Assert.IsTrue(ChatMarkup.LooksLikeName("Brin O'Holt-Smith"));
            Assert.IsFalse(ChatMarkup.LooksLikeName("lfg|3"));
            Assert.IsFalse(ChatMarkup.LooksLikeName("openurl|https://x"));
            Assert.IsFalse(ChatMarkup.LooksLikeName("A+"));
        }

        [TestMethod]
        public void HighlightsAreWholeWords() {
            var words = ChatHighlights.Words(" Holmez, wts ,, holmez");
            CollectionAssert.AreEqual(new[] { "Holmez", "wts" }, words);

            Assert.IsTrue(ChatHighlights.AnyIn("hey holmez!", words));
            Assert.IsTrue(ChatHighlights.AnyIn("WTS a sword", words));
            Assert.IsFalse(ChatHighlights.AnyIn("Holmezzz is here", words));
            Assert.IsFalse(ChatHighlights.AnyIn("swtsx", words));
            Assert.IsFalse(ChatHighlights.AnyIn("", words));
        }

        [TestMethod]
        public void LinesWrapAtSpacesAndIndentWhatWraps() {
            // "hello " is 38, "world" 35: they don't fit in 60
            var layout = ChatLayout.Wrap(new[] { "hello world" }, 60, Advance, indent: 10);

            Assert.AreEqual(2, layout.Rows);
            Assert.AreEqual("hello ", layout.Runs[0].Text);
            Assert.AreEqual(0, layout.Runs[0].Row);
            Assert.AreEqual("world", layout.Runs[1].Text);
            Assert.AreEqual(1, layout.Runs[1].Row);
            Assert.AreEqual(10, layout.Runs[1].X, "rows after the first start at the indent");
        }

        [TestMethod]
        public void PiecesOnOneRowStayApart() {
            var layout = ChatLayout.Wrap(new[] { "14:05 ", "Bob", " says hi" }, 500, Advance);

            Assert.AreEqual(1, layout.Rows);
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, layout.Runs.Select(r => r.Piece).ToArray());
            Assert.AreEqual(Advance('1') * 5 + 3, layout.Runs[1].X);
            Assert.AreEqual(" says hi", layout.Runs[2].Text, "a piece's words on a row are one run");
        }

        [TestMethod]
        public void AWordLongerThanARowIsBroken() {
            var layout = ChatLayout.Wrap(new[] { new string('x', 10) }, 30, Advance);

            // 4 letters (28 pixels) fit a row
            Assert.AreEqual(3, layout.Rows);
            CollectionAssert.AreEqual(new[] { "xxxx", "xxxx", "xx" }, layout.Runs.Select(r => r.Text).ToArray());
        }

        [TestMethod]
        public void NewLinesStartRows() {
            var layout = ChatLayout.Wrap(new[] { "one\ntwo" }, 500, Advance);

            Assert.AreEqual(2, layout.Rows);
            Assert.AreEqual("two", layout.Runs.Single(r => r.Row == 1).Text);
        }

        [TestMethod]
        public void TalkFocusPutsTheRightCommandInFront() {
            Assert.AreEqual("hello", ChatTalk.Command(ChatTalk.Say, " hello ", null, null, null, out _));
            Assert.AreEqual("/f hi all", ChatTalk.Command("fellowship", "hi all", null, null, null, out _));
            Assert.AreEqual("/general hi", ChatTalk.Command("general", "hi", null, null, null, out _));
            Assert.AreEqual("/tell Bob, hi", ChatTalk.Command(ChatTalk.TellSelected, "hi", "Bob", null, null, out _));
            Assert.AreEqual("/tell Ann, hi", ChatTalk.Command(ChatTalk.Reply, "hi", "Bob", "Ann", null, out _));
            Assert.AreEqual("/e waves", ChatTalk.Command(ChatTalk.Custom, "waves", null, null, "/e", out _));
            Assert.AreEqual("/tell Bob, hi", ChatTalk.Command(ChatTalk.Custom, "hi", null, null, "/tell Bob, ", out _));

            // a command goes as typed, whatever the focus
            Assert.AreEqual("/vt start", ChatTalk.Command("fellowship", "/vt start", null, null, null, out _));
            Assert.AreEqual("@playerui hello", ChatTalk.Command(ChatTalk.Reply, "@playerui hello", null, null, null, out _));

            Assert.IsNull(ChatTalk.Command(ChatTalk.TellSelected, "hi", null, null, null, out var noSelection));
            Assert.IsNotNull(noSelection);
            Assert.IsNull(ChatTalk.Command(ChatTalk.Reply, "hi", null, null, null, out var noReply));
            Assert.IsNotNull(noReply);
            Assert.IsNull(ChatTalk.Command(ChatTalk.Say, "   ", null, null, null, out var empty));
            Assert.IsNull(empty);
        }

        [TestMethod]
        public void TalkKeysAreUnique() {
            CollectionAssert.AllItemsAreUnique(ChatTalk.All.Select(t => t.Key).ToList());
            Assert.AreEqual(ChatTalk.Say, ChatTalk.For("not one").Key);
        }

        [TestMethod]
        public void ChatSettingsStartWithFourTabsAndReadBack() {
            var data = new SettingsData();

            Assert.IsFalse(data.Chat.On, "off until turned on");
            Assert.AreEqual(ChatSettings.WindowCount, data.Chat.Windows.Count);
            Assert.IsTrue(data.Chat.Windows.All(w => !w.PoppedOut), "four tabs of the main window");
            Assert.IsFalse(data.Chat.Window(0).Shows(ChatCategory.Combat));
            Assert.IsTrue(data.Chat.Window(1).Shows(ChatCategory.Tells));

            data.Chat.Window(2).PoppedOut = true;
            data.Chat.Window(3).SetShown(ChatCategory.Trade, false);
            data.Chat.Colors["type3"] = 0xFF00FF00;
            var read = Json.Read<SettingsData>(Json.Write(data));

            Assert.IsTrue(read.Chat.Window(2).PoppedOut);
            Assert.IsFalse(read.Chat.Window(3).Shows(ChatCategory.Trade));
            Assert.IsTrue(read.Chat.Window(3).Shows(ChatCategory.General));
            Assert.AreEqual(0xFF00FF00, read.Chat.Colors["type3"]);
        }

        [TestMethod]
        public void AnOlderFileGetsTheChatAsItStarts() {
            var read = Json.Read<SettingsData>("{\"textScale\":1}");

            Assert.AreEqual(ChatSettings.WindowCount, read.Chat.Windows.Count);
            Assert.AreEqual("Tells", read.Chat.Window(1).Name);

            // a file with fewer windows than there are now gets the rest as they start
            var fewer = Json.Read<SettingsData>("{\"chat\":{\"windows\":[{\"name\":\"Mine\"}]}}");
            Assert.AreEqual("Mine", fewer.Chat.Window(0).Name);
            Assert.AreEqual("Combat", fewer.Chat.Window(4).Name);
            Assert.AreEqual(15, fewer.Chat.Window(0).FontSize);
        }

        [TestMethod]
        public void SharedRepliesHaveTheServersNames() {
            // as daralet-ACE's SharedItemStoreTests pins them
            var reply = Json.Read<ShareReplyInfo>("{\"guid\":7,\"shared\":{\"key\":\"abcdefgh\",\"name\":\"Steel Sword\",\"owner\":\"Bob\",\"slots\":512,"
                + "\"item\":{\"guid\":7,\"name\":\"Steel Sword\",\"stackSize\":1,\"value\":100,\"burden\":50,"
                + "\"salvageable\":true,\"sellable\":true,\"material\":\"Steel\",\"workmanship\":5.5}}}");

            Assert.AreEqual(7u, reply.Guid);
            Assert.AreEqual("abcdefgh", reply.Shared.Key);
            Assert.AreEqual("Steel Sword", reply.Shared.Name);
            Assert.AreEqual("Bob", reply.Shared.Owner);
            Assert.AreEqual(512, reply.Shared.Slots);
            Assert.AreEqual("Steel Sword", reply.Shared.Item.Name);

            var lookup = Json.Read<SharedLookupInfo>("{\"items\":[],\"missing\":[\"jkmnpqrs\"]}");
            CollectionAssert.AreEqual(new[] { "jkmnpqrs" }, lookup.Missing);
        }

        [TestMethod]
        public void SharedItemsAreAskedForTenAtATime() {
            var requests = new SharedItemRequests(TimeSpan.FromSeconds(3));
            var now = At;

            for (var i = 0; i < 12; i++) {
                requests.Ask($"code{i:D4}", now);
            }

            var first = requests.Next(now);
            Assert.AreEqual(SharedItemRequests.Batch, first.Length);
            Assert.IsNull(requests.Next(now.AddSeconds(1)), "one request at a time");

            requests.Answered(new SharedLookupInfo {
                Items = new List<SharedItemInfo> { new SharedItemInfo { Key = "code0000", Owner = "Bob" } },
                Missing = new List<string> { "code0001" },
            }, now.AddSeconds(1));

            Assert.AreEqual("Bob", requests.Find("code0000").Owner);
            Assert.IsTrue(requests.IsMissing("code0001"));
            Assert.IsFalse(requests.IsWaiting("code0002"), "left out of the answer: let be for a while");

            var second = requests.Next(now.AddSeconds(2));
            CollectionAssert.AreEqual(new[] { "code0010", "code0011" }, second);

            // asked again soon after being left out: not yet
            requests.Ask("code0002", now.AddSeconds(2));
            Assert.IsFalse(requests.IsWaiting("code0002"));
            requests.Ask("code0002", now.AddSeconds(2) + SharedItemRequests.RetryAfter);
            Assert.IsTrue(requests.IsWaiting("code0002"));
        }

        [TestMethod]
        public void AnUnansweredRequestIsSentAgainThenGivenUp() {
            var requests = new SharedItemRequests(TimeSpan.FromSeconds(3));
            requests.Ask("abcdefgh", At);

            for (var tries = 0; tries < SharedItemRequests.MaxTries; tries++) {
                CollectionAssert.AreEqual(new[] { "abcdefgh" }, requests.Next(At.AddSeconds(tries * 4)), $"try {tries + 1}");
            }

            Assert.IsNull(requests.Next(At.AddSeconds(SharedItemRequests.MaxTries * 4)));
            Assert.IsFalse(requests.IsWaiting("abcdefgh"));

            // known items and missing ones aren't asked about
            requests.Add(new SharedItemInfo { Key = "jkmnpqrs" });
            requests.Ask("jkmnpqrs", At);
            Assert.IsFalse(requests.IsWaiting("jkmnpqrs"));

            // until a new line names it
            requests.Forget("jkmnpqrs");
            requests.Ask("jkmnpqrs", At);
            Assert.IsTrue(requests.IsWaiting("jkmnpqrs"));
            Assert.IsNull(requests.Find("jkmnpqrs"));
        }

        [TestMethod]
        public void LongKeysMakeShorterRequests() {
            var requests = new SharedItemRequests(TimeSpan.FromSeconds(3));
            var key = new string('a', 150);

            requests.Ask(key + "1", At);
            requests.Ask(key + "2", At);
            requests.Ask(key + "3", At);

            Assert.AreEqual(2, requests.Next(At).Length);
        }
    }
}
