using System;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class LoadPiecesTests {
        private static readonly DateTime Start = new DateTime(2026, 11, 21, 20, 0, 0, DateTimeKind.Utc);

        [TestMethod]
        public void WaitsUntilEachPieceIsReady() {
            LoadPieces.Expect(new[] { LoadPieces.Trees, LoadPieces.FarLand }, Start);

            Assert.AreEqual(2, LoadPieces.Expected);
            Assert.AreEqual(2, LoadPieces.Waiting(Start.AddSeconds(5)));
            Assert.AreEqual("far land, trees and buildings", LoadPieces.Describe());

            LoadPieces.Ready(LoadPieces.FarLand);
            Assert.AreEqual(1, LoadPieces.Waiting(Start.AddSeconds(6)));
            Assert.AreEqual("trees and buildings", LoadPieces.Describe());

            LoadPieces.Ready(LoadPieces.Trees);
            Assert.AreEqual(0, LoadPieces.Waiting(Start.AddSeconds(7)));
            Assert.AreEqual("", LoadPieces.Describe());
        }

        [TestMethod]
        public void GivesUpOnAPieceThatNeverComes() {
            LoadPieces.Expect(new[] { LoadPieces.FarLand }, Start);

            Assert.AreEqual(1, LoadPieces.Waiting(Start.AddSeconds(LoadPieces.GiveUpSeconds - 1)));
            Assert.AreEqual(0, LoadPieces.Waiting(Start.AddSeconds(LoadPieces.GiveUpSeconds + 1)));
        }

        [TestMethod]
        public void ANewLoadReplacesTheLast() {
            LoadPieces.Expect(new[] { LoadPieces.FarLand, LoadPieces.Trees }, Start);
            LoadPieces.Expect(new[] { LoadPieces.Trees }, Start.AddSeconds(2));

            Assert.AreEqual(1, LoadPieces.Expected);
            Assert.AreEqual("trees and buildings", LoadPieces.Describe());
            LoadPieces.Ready(LoadPieces.Trees);
        }
    }
}
