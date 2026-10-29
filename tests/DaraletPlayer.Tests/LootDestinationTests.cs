using System.Collections.Generic;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class LootDestinationTests {
        private static readonly PackRoom FullMain = new PackRoom { Id = 1, Count = 102, Capacity = 102 };
        private static readonly PackRoom RoomyMain = new PackRoom { Id = 1, Count = 40, Capacity = 102 };

        [TestMethod]
        public void TheMainPackComesFirstWhileItHasRoom() {
            var packs = LootDestination.Candidates(RoomyMain, new[] { Pack(2, 3, 24) }, itemIsPack: false);
            CollectionAssert.AreEqual(new List<int> { 1, 2 }, packs);
        }

        [TestMethod]
        public void AFullMainPackSendsItToASidePackWithRoom() {
            // the bug: every loot went to the main pack, which the server refuses once it's full
            var packs = LootDestination.Candidates(FullMain, new[] { Pack(2, 24, 24), Pack(3, 5, 24) }, itemIsPack: false);
            CollectionAssert.AreEqual(new List<int> { 3 }, packs);
        }

        [TestMethod]
        public void NothingWhenEveryPackIsFull() {
            var packs = LootDestination.Candidates(FullMain, new[] { Pack(2, 24, 24) }, itemIsPack: false);
            Assert.AreEqual(0, packs.Count);
        }

        [TestMethod]
        public void APackWithNoKnownSizeIsSkipped() {
            var packs = LootDestination.Candidates(FullMain, new[] { Pack(2, 0, 0) }, itemIsPack: false);
            Assert.AreEqual(0, packs.Count);
        }

        [TestMethod]
        public void APackAlwaysGoesToTheMainPack() {
            var packs = LootDestination.Candidates(FullMain, new[] { Pack(2, 3, 24) }, itemIsPack: true);
            CollectionAssert.AreEqual(new List<int> { 1 }, packs);
        }

        [TestMethod]
        public void SaysWhyInOrderOfLikelihood() {
            Assert.AreEqual("your packs are full", LootDestination.WhyNot(false, 50));
            Assert.AreEqual("you're carrying too much", LootDestination.WhyNot(true, 300));
            StringAssert.StartsWith(LootDestination.WhyNot(true, 120), "the server didn't hand it over");
        }

        private static PackRoom Pack(int id, int count, int capacity) {
            return new PackRoom { Id = id, Count = count, Capacity = capacity };
        }
    }
}
