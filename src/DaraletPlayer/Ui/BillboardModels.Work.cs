using System;
using System.Collections.Generic;
using UtilityBelt.Common.Enums;

namespace DaraletPlayer.Ui {
    internal sealed partial class BillboardModels {
        /// <summary>
        /// A surface's texture as read from the dats on the game's thread, for the worker to decode: the image's bytes,
        /// format, size and palette, or a plain colour (ImageId 0).
        /// </summary>
        internal sealed class SurfaceSource {
            public bool CutOut;
            public uint Colour;
            public uint ImageId;
            public SurfacePixelFormat Format;
            public byte[] Data;
            public int Width;
            public int Height;
            public IReadOnlyList<uint> Palette;
        }

        /// <summary>A picture the worker finished (its levels, the full one first), or why it couldn't (Levels null).</summary>
        private sealed class Finished {
            public Finished(uint id, BillboardModel model, List<(uint[] Pixels, int Width, int Height)> levels,
                string error, int triangles, TimeSpan took) {
                Id = id;
                Model = model;
                Levels = levels;
                Error = error;
                Triangles = triangles;
                Took = took;
            }

            public uint Id { get; }

            public BillboardModel Model { get; }

            public List<(uint[] Pixels, int Width, int Height)> Levels { get; }

            public string Error { get; }

            public int Triangles { get; }

            public TimeSpan Took { get; }
        }
    }
}
