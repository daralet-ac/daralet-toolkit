using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using Footprints = System.Collections.Generic.List<(float X, float Y, float Cos, float Sin, float MinX, float MaxX, float MinY,
    float MaxY)>;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Where buildings stand, so no grass grows through their floors: each building of a block (LandblockInfo's
    /// Buildings) as its model's box across the ground (the model's vertices, a Setup's parts placed by its first frame),
    /// turned and placed as the building is, and a little more. Before, a whole 24-meter cell with a building's origin
    /// in it had no grass (CSortCell::has_building, the game's rule for its trees), which left bare squares round every
    /// house in a town (the user's screenshot in Hebian-to, November 2026).
    /// </summary>
    internal sealed partial class Grass {
        /// <summary>How far past a building's box no grass grows, in meters.</summary>
        private const float BuildingMargin = 0.5f;

        /// <summary>Each model's box across the ground, in its own meters (x, y), worked out once.</summary>
        private readonly Dictionary<uint, (float MinX, float MaxX, float MinY, float MaxY)> modelBoxes =
            new Dictionary<uint, (float, float, float, float)>();

        /// <summary>A block's buildings: where each stands, its turn, and its box.</summary>
        private Footprints FootprintsOf(DatDatabase cells, DatDatabase portal, uint landblock) {
            var found = new Footprints();
            var info = cells.ReadFromDat<LandblockInfo>((landblock & 0xFFFF0000) | 0xFFFE);

            if (info == null || portal == null) {
                return found;
            }

            foreach (var building in info.Buildings) {
                var box = BoxOf(portal, building.ModelId);

                if (box == null) {
                    continue;
                }

                var q = building.Frame.Orientation;
                var yaw = 2 * Math.Atan2(q.Z, q.W);
                var (minX, maxX, minY, maxY) = box.Value;
                found.Add((building.Frame.Origin.X, building.Frame.Origin.Y, (float)Math.Cos(yaw), (float)Math.Sin(yaw),
                    minX - BuildingMargin, maxX + BuildingMargin, minY - BuildingMargin, maxY + BuildingMargin));
            }

            return found;
        }

        /// <summary>Whether a place in the block is under one of its buildings.</summary>
        private static bool UnderBuilding(Footprints footprints, float x, float y) {
            foreach (var f in footprints) {
                var dx = x - f.X;
                var dy = y - f.Y;
                // into the building's own meters: turned back by its yaw
                var lx = dx * f.Cos + dy * f.Sin;
                var ly = -dx * f.Sin + dy * f.Cos;

                if (lx >= f.MinX && lx <= f.MaxX && ly >= f.MinY && ly <= f.MaxY) {
                    return true;
                }
            }

            return false;
        }

        /// <summary>A model's box across the ground, from its vertices; null if it can't be read.</summary>
        private (float, float, float, float)? BoxOf(DatDatabase portal, uint id) {
            if (modelBoxes.TryGetValue(id, out var known)) {
                return known;
            }

            try {
                var parts = new List<(uint Gfx, Vector3 Origin, Quaternion Turn, Vector3 Scale)>();

                if (id >> 24 == 2) {
                    var setup = portal.ReadFromDat<SetupModel>(id);
                    var frames = setup.PlacementFrames.ContainsKey(0) ? setup.PlacementFrames[0]
                        : setup.PlacementFrames.Values.FirstOrDefault();

                    for (var i = 0; i < setup.Parts.Count; i++) {
                        var frame = frames != null && i < frames.AnimFrame.Frames.Count ? frames.AnimFrame.Frames[i] : null;
                        var scale = i < setup.DefaultScale.Count ? BillboardModels.Vec(setup.DefaultScale[i]) : Vector3.One;
                        parts.Add((setup.Parts[i], frame != null ? BillboardModels.Vec(frame.Origin) : Vector3.Zero,
                            frame != null ? BillboardModels.Quat(frame.Orientation) : Quaternion.Identity, scale));
                    }
                }
                else {
                    parts.Add((id, Vector3.Zero, Quaternion.Identity, Vector3.One));
                }

                float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;

                foreach (var part in parts) {
                    var gfx = portal.ReadFromDat<GfxObj>(part.Gfx);

                    foreach (var vertex in gfx.VertexArray.Vertices.Values) {
                        var place = Vector3.Transform(BillboardModels.Vec(vertex.Origin) * part.Scale, part.Turn) + part.Origin;
                        minX = Math.Min(minX, place.X);
                        maxX = Math.Max(maxX, place.X);
                        minY = Math.Min(minY, place.Y);
                        maxY = Math.Max(maxY, place.Y);
                    }
                }

                var box = minX <= maxX ? (minX, maxX, minY, maxY) : ((float, float, float, float)?)null;

                if (box != null) {
                    modelBoxes[id] = box.Value;
                }

                return box;
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[grass] building model 0x{id:X8}'s size couldn't be read: {ex.Message}");
                modelBoxes[id] = (0, 0, 0, 0);
                return null;
            }
        }
    }
}
