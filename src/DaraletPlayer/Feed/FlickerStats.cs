using System;
using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// How much the picture flickers, from each frame's brightness in a grid of tiles (Ui/FlickerMeter.cs, a test run's
    /// `flicker` step; the user, November 2026: "a lot of flickering with object lighting, while moving around"). Moving
    /// changes a tile's brightness smoothly from frame to frame; a flicker is a tile that jumps and comes straight back
    /// (a blip: up then down, or down then up, each more than Threshold, over two frames), or jumps once with the frames
    /// either side of it steady (a step). Counted per tile, so one light popping on a character counts once however
    /// small the character is on screen. Compared between builds on the same walk, the numbers say whether a change cut
    /// the flicker; alone they mean little, since a camera passing an edge makes steps too.
    /// </summary>
    public sealed class FlickerStats {
        /// <summary>A tile's change in brightness that counts as a jump, of 255.</summary>
        public const float Threshold = 6;

        private readonly List<float[]> frames = new List<float[]>();

        public int Frames => frames.Count;

        /// <summary>One frame's tiles' brightness, 0 to 255, always as many.</summary>
        public void Add(float[] tiles) {
            frames.Add((float[])tiles.Clone());
        }

        /// <summary>Blips and steps over all tiles, from the frames given so far.</summary>
        public (int Blips, int Steps) Count() {
            var blips = 0;
            var steps = 0;

            for (var t = 2; t < frames.Count - 1; t++) {
                var before = frames[t - 2];
                var last = frames[t - 1];
                var now = frames[t];
                var next = frames[t + 1];

                for (var i = 0; i < now.Length; i++) {
                    var into = now[i] - last[i];
                    var outOf = next[i] - now[i];
                    var earlier = last[i] - before[i];

                    if (Math.Abs(into) <= Threshold) {
                        continue;
                    }

                    if (Math.Abs(outOf) > Threshold && Math.Sign(outOf) != Math.Sign(into)) {
                        blips++;
                    }
                    else if (Math.Abs(outOf) < Threshold / 3 && Math.Abs(earlier) < Threshold / 3) {
                        steps++;
                    }
                }
            }

            return (blips, steps);
        }

        /// <summary>The result in a line: blips and steps a second, over the seconds given.</summary>
        public string Describe(double seconds) {
            var (blips, steps) = Count();
            var per = Math.Max(0.001, seconds);
            return $"{Frames} frames in {seconds:0.0} s: {blips} blips ({blips / per:0.0}/s), {steps} steps ({steps / per:0.0}/s)";
        }
    }
}
