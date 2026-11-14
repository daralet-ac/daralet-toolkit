using System.Numerics;
using DaraletPlayer.Feed;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DaraletPlayer.Tests {
    [TestClass]
    public class WindowSnapTests {
        private static readonly Vector2 Screen = new Vector2(1000, 800);
        private static readonly Vector2 Size = new Vector2(200, 100);
        private static readonly (Vector2, Vector2)[] None = new (Vector2, Vector2)[0];

        [TestMethod]
        public void AWindowNearTheScreensEdgeSnapsToIt() {
            Assert.AreEqual(new Vector2(0, 0), WindowSnap.Snap(new Vector2(6, 5), Size, Screen, None, 8, 0));
            Assert.AreEqual(new Vector2(800, 700), WindowSnap.Snap(new Vector2(795, 704), Size, Screen, None, 8, 0));
        }

        [TestMethod]
        public void AWindowOutOfReachStaysWhereItIs() {
            Assert.AreEqual(new Vector2(300, 300), WindowSnap.Snap(new Vector2(300, 300), Size, Screen, None, 8, 0));
        }

        [TestMethod]
        public void AWindowBesideAnotherSnapsToItsEdge() {
            var other = new[] { (new Vector2(400, 300), new Vector2(600, 500)) };
            // left edge to the other's right edge, side by side
            Assert.AreEqual(new Vector2(600, 320), WindowSnap.Snap(new Vector2(605, 320), Size, Screen, other, 8, 0));
            // right edge to the other's left edge
            Assert.AreEqual(new Vector2(200, 320), WindowSnap.Snap(new Vector2(197, 320), Size, Screen, other, 8, 0));
            // tops lined up, no edge in reach sideways
            Assert.AreEqual(new Vector2(500, 300), WindowSnap.Snap(new Vector2(500, 304), Size, Screen, other, 8, 0));
        }

        [TestMethod]
        public void AWindowFarAboveAnotherDoesntLineUpWithIt() {
            var other = new[] { (new Vector2(400, 600), new Vector2(600, 700)) };
            Assert.AreEqual(new Vector2(404, 100), WindowSnap.Snap(new Vector2(404, 100), Size, Screen, other, 8, 0));
        }

        [TestMethod]
        public void AResizedEdgeSnapsToTheScreenOrABesideWindowsEdge() {
            var other = new[] { (new Vector2(400, 300), new Vector2(600, 500)) };
            // a right edge dragged near the screen's
            Assert.AreEqual(1000f, WindowSnap.SnapEdge(994, true, 300, 100, Screen, None, 8, 0));
            // a right edge dragged near the other's left, the two side by side
            Assert.AreEqual(400f, WindowSnap.SnapEdge(405, true, 320, 100, Screen, other, 8, 0));
            // not side by side: no snap to it
            Assert.AreEqual(405f, WindowSnap.SnapEdge(405, true, 600, 100, Screen, other, 8, 0));
            // a bottom edge to the grid
            Assert.AreEqual(240f, WindowSnap.SnapEdge(236, false, 100, 200, Screen, None, 8, 20));
        }

        [TestMethod]
        public void TheGridTakesWhatNoEdgeDoes() {
            Assert.AreEqual(new Vector2(320, 300), WindowSnap.Snap(new Vector2(327, 293), Size, Screen, None, 8, 20));
            // an edge in reach wins over the grid
            Assert.AreEqual(new Vector2(0, 300), WindowSnap.Snap(new Vector2(3, 293), Size, Screen, None, 8, 20));
        }

        [TestMethod]
        public void TheGuidesAreTheMiddleThirdsAndQuarters() {
            CollectionAssert.AreEqual(new[] { 500f, 333f, 667f, 250f, 750f }, WindowSnap.Guides(1000));
        }

        [TestMethod]
        public void WithTheGridOnAWindowsMiddleSnapsToTheGuides() {
            // 200 wide: its middle at 497 goes to the screen's middle, 500; at 336, to the third, 333
            Assert.AreEqual(400f, WindowSnap.Snap(new Vector2(397, 157), Size, Screen, None, 8, 20).X);
            Assert.AreEqual(233f, WindowSnap.Snap(new Vector2(236, 157), Size, Screen, None, 8, 20).X);
            // 100 high on an 800 screen: its middle at 403 goes to 400
            Assert.AreEqual(350f, WindowSnap.Snap(new Vector2(157, 353), Size, Screen, None, 8, 20).Y);
            // the grid off, no guides
            Assert.AreEqual(397f, WindowSnap.Snap(new Vector2(397, 157), Size, Screen, None, 8, 0).X);
        }

        [TestMethod]
        public void WithTheGridOnAResizedEdgeSnapsToTheGuides() {
            Assert.AreEqual(667f, WindowSnap.SnapEdge(663, true, 300, 100, Screen, None, 8, 20));
            Assert.AreEqual(663f, WindowSnap.SnapEdge(663, true, 300, 100, Screen, None, 8, 0));
        }
    }
}
