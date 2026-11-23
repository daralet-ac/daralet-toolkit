using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using UtilityBelt.Service;
using DatEnvironment = ACE.DatLoader.FileTypes.Environment;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Surfaces painted with fire become lights (the user's call, November 2026: Hebian-to's fireplace, "make this
    /// fireplace texture show up as a light source"). Its embers are no object but the hearth's floor, a polygon of the
    /// indoor cell 0xE74E0137 (3.6 by 1.5 m) whose surface (0x080004BA) shows the picture 0x05000D32, so nothing named
    /// like a fire is there for FireLights. So every polygon whose surface shows one of FirePictures (looked through by
    /// eye: the embers, and the glowing embers 0x050026C6; lava and flames are left out for now, or a lava dungeon would
    /// crowd out the lamps, eight at most) is gathered into clusters 3 m across, each a warm flickering light half a
    /// meter above its middle, reaching 1.5 times its size (4 to 8 m). Looked for in the 3x3 landblocks' indoor cells
    /// (each LandblockInfo's NumCells, 0xXXYY0100 on, a few a frame within CellBudgetMs, read from the file's bytes as
    /// the far land reads its blocks, since UtilityBelt's FileCache keeps every file ReadFromDat reads), those cells'
    /// static objects, and the outdoor Objects and Buildings, a model's fires worked out once by id. Untested in the
    /// game when written.
    /// </summary>
    internal static partial class PlacedLights {
        private const double CellBudgetMs = 2;
        private const float ClusterMeters = 3;
        private const float FireLift = 0.5f;

        private static readonly HashSet<uint> FirePictures = new HashSet<uint> { 0x05000D32, 0x050026C6 };

        private static readonly Queue<uint> cellsToRead = new Queue<uint>();
        private static readonly Dictionary<uint, bool> fireSurfaces = new Dictionary<uint, bool>();
        private static readonly Dictionary<uint, DatEnvironment> environments = new Dictionary<uint, DatEnvironment>();
        private static readonly Dictionary<uint, List<(Vector3 Centre, float Size)>> modelFires =
            new Dictionary<uint, List<(Vector3, float)>>();

        private static int loggedFires;

        private static void QueueCells(uint block, uint count) {
            for (uint i = 0; i < count; i++) {
                cellsToRead.Enqueue((block << 16) | (0x100 + i));
            }
        }

        private static void ReadSomeCells() {
            if (cellsToRead.Count == 0) {
                return;
            }

            var cells = UBService.CellDat;
            var portal = UBService.PortalDat;

            if (cells == null || portal == null) {
                return;
            }

            var clock = Stopwatch.StartNew();

            while (cellsToRead.Count > 0 && clock.Elapsed.TotalMilliseconds < CellBudgetMs) {
                var id = cellsToRead.Dequeue();

                // a block let go since it was queued (Clear) has nowhere to put its fires
                if (!byBlock.TryGetValue(id >> 16, out var found)) {
                    continue;
                }

                var cell = ReadCell(cells, id);

                if (cell != null) {
                    AddCellFires(found, id, portal, cell);
                }
            }
        }

        /// <summary>A cell read from its file without the dat keeping it.</summary>
        private static EnvCell ReadCell(DatDatabase cells, uint id) {
            var reader = cells.GetReaderForFile(id);

            if (reader == null) {
                return null;
            }

            var cell = new EnvCell();

            using (var input = new MemoryStream(reader.Buffer))
            using (var binary = new BinaryReader(input)) {
                cell.Unpack(binary);
            }

            return cell;
        }

        private static void AddCellFires(List<PlacedLight> found, uint id, DatDatabase portal, EnvCell cell) {
            var block = id >> 16;
            var burning = new List<(Vector3 Min, Vector3 Max)>();

            if (cell.Surfaces.Any(s => IsFire(portal, 0x08000000u | s))) {
                var envId = 0x0D000000u | cell.EnvironmentId;

                if (!environments.TryGetValue(envId, out var env)) {
                    env = portal.ReadFromDat<DatEnvironment>(envId);
                    environments[envId] = env;
                }

                if (env != null && env.Cells.TryGetValue(cell.CellStructure, out var structure)) {
                    foreach (var polygon in structure.Polygons.Values) {
                        if (polygon.PosSurface < 0 || polygon.PosSurface >= cell.Surfaces.Count
                            || !IsFire(portal, 0x08000000u | cell.Surfaces[polygon.PosSurface])) {
                            continue;
                        }

                        var points = polygon.VertexIds
                            .Where(v => structure.VertexArray.Vertices.ContainsKey((ushort)v))
                            .Select(v => Place(cell.Position, BillboardModels.Vec(structure.VertexArray.Vertices[(ushort)v].Origin)))
                            .ToList();

                        if (points.Count > 0) {
                            burning.Add((Min(points), Max(points)));
                        }
                    }
                }
            }

            foreach (var (centre, size) in Clusters(burning)) {
                AddFire(found, block, centre, size);
            }

            foreach (var stab in cell.StaticObjects) {
                AddFires(found, block, portal, stab.Id, stab.Frame);
            }
        }

        /// <summary>The fires of a placed model (a Setup or a GfxObj), placed by the object's frame.</summary>
        private static void AddFires(List<PlacedLight> found, uint block, DatDatabase portal, uint model,
            ACE.DatLoader.Entity.Frame frame) {
            foreach (var (centre, size) in FiresOf(portal, model)) {
                AddFire(found, block, Place(frame, centre), size);
            }
        }

        private static void AddFire(List<PlacedLight> found, uint block, Vector3 at, float size) {
            var light = new PlacedLight {
                Block = block, X = at.X, Y = at.Y, Z = at.Z + FireLift, Intensity = 100,
                Falloff = Math.Max(4, Math.Min(8, size * 1.5f)), Fire = true,
                Seed = (int)(at.X * 31 + at.Y * 17) ^ (int)block,
            };
            found.Add(light);

            if (loggedFires++ < 10) {
                PluginCore.LogFile($"[lamps] a fire surface given light in {block:X4} at {light.X:0.0} {light.Y:0.0} "
                    + $"{light.Z:0.0}, {size:0.0} m across, falloff {light.Falloff:0.#}");
            }
        }

        /// <summary>A model's fires in its own space, worked out once.</summary>
        private static List<(Vector3 Centre, float Size)> FiresOf(DatDatabase portal, uint model) {
            if (modelFires.TryGetValue(model, out var known)) {
                return known;
            }

            var burning = new List<(Vector3 Min, Vector3 Max)>();
            var parts = new List<(uint Gfx, Vector3 Origin, Quaternion Turn, Vector3 Scale)>();

            if (model >> 24 == 0x02) {
                var setup = portal.ReadFromDat<SetupModel>(model);

                if (setup != null) {
                    var frames = setup.PlacementFrames.ContainsKey(0) ? setup.PlacementFrames[0]
                        : setup.PlacementFrames.Values.FirstOrDefault();

                    for (var i = 0; i < setup.Parts.Count; i++) {
                        var frame = frames != null && i < frames.AnimFrame.Frames.Count ? frames.AnimFrame.Frames[i] : null;
                        var scale = i < setup.DefaultScale.Count ? BillboardModels.Vec(setup.DefaultScale[i]) : Vector3.One;
                        parts.Add((setup.Parts[i], frame != null ? BillboardModels.Vec(frame.Origin) : Vector3.Zero,
                            frame != null ? BillboardModels.Quat(frame.Orientation) : Quaternion.Identity, scale));
                    }
                }
            }
            else if (model >> 24 == 0x01) {
                parts.Add((model, Vector3.Zero, Quaternion.Identity, Vector3.One));
            }

            foreach (var part in parts) {
                var gfx = portal.ReadFromDat<GfxObj>(part.Gfx);

                if (gfx == null || !gfx.Surfaces.Any(s => IsFire(portal, s))) {
                    continue;
                }

                foreach (var polygon in gfx.Polygons.Values) {
                    if (polygon.PosSurface < 0 || polygon.PosSurface >= gfx.Surfaces.Count
                        || !IsFire(portal, gfx.Surfaces[polygon.PosSurface])) {
                        continue;
                    }

                    var points = polygon.VertexIds
                        .Where(v => gfx.VertexArray.Vertices.ContainsKey((ushort)v))
                        .Select(v => Vector3.Transform(BillboardModels.Vec(gfx.VertexArray.Vertices[(ushort)v].Origin)
                            * part.Scale, part.Turn) + part.Origin)
                        .ToList();

                    if (points.Count > 0) {
                        burning.Add((Min(points), Max(points)));
                    }
                }
            }

            var fires = Clusters(burning);
            modelFires[model] = fires;
            return fires;
        }

        private static bool IsFire(DatDatabase portal, uint surfaceId) {
            if (!fireSurfaces.TryGetValue(surfaceId, out var fire)) {
                var surface = portal.ReadFromDat<Surface>(surfaceId);
                fire = surface != null && FirePictures.Contains(surface.OrigTextureId);
                fireSurfaces[surfaceId] = fire;
            }

            return fire;
        }

        /// <summary>Polygons' boxes gathered into clusters about ClusterMeters across: each its middle and size.</summary>
        private static List<(Vector3 Centre, float Size)> Clusters(List<(Vector3 Min, Vector3 Max)> boxes) {
            var clusters = new List<(Vector3 Min, Vector3 Max)>();

            foreach (var box in boxes) {
                var middle = (box.Min + box.Max) / 2;
                var joined = false;

                for (var i = 0; i < clusters.Count; i++) {
                    var c = clusters[i];

                    if (Vector3.Distance((c.Min + c.Max) / 2, middle) <= ClusterMeters) {
                        clusters[i] = (Vector3.Min(c.Min, box.Min), Vector3.Max(c.Max, box.Max));
                        joined = true;
                        break;
                    }
                }

                if (!joined) {
                    clusters.Add(box);
                }
            }

            return clusters.Select(c => ((c.Min + c.Max) / 2,
                new Vector2(c.Max.X - c.Min.X, c.Max.Y - c.Min.Y).Length())).ToList();
        }

        /// <summary>A point in a frame's space placed in its landblock: turned by its quaternion, then moved.</summary>
        private static Vector3 Place(ACE.DatLoader.Entity.Frame frame, Vector3 v) {
            return Vector3.Transform(v, BillboardModels.Quat(frame.Orientation)) + BillboardModels.Vec(frame.Origin);
        }

        private static Vector3 Min(List<Vector3> points) {
            return points.Aggregate(Vector3.Min);
        }

        private static Vector3 Max(List<Vector3> points) {
            return points.Aggregate(Vector3.Max);
        }

        private static void ClearFires() {
            cellsToRead.Clear();
            fireSurfaces.Clear();
            environments.Clear();
            modelFires.Clear();
        }
    }
}
