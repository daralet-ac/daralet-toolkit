using System;
using System.Collections.Generic;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Where the game would put a landblock's scenery, worked out from the dats alone (billboards stage 1; the user's
    /// call, November 2026): past the scenery radius the game never makes those objects, so billboards need their places
    /// from the same rules the game uses.
    /// <para>
    /// The rules are the client's CLandBlock::get_land_scenes (0x5311A0), as ACE's Entity/Scenery.cs has them (daralet-ACE
    /// apps/server): for each of the block's 9x9 terrain vertices, its terrain type and scene type pick the region's list
    /// of scenes, a hash of the vertex's global cell picks one, and each of that scene's objects is placed when a second
    /// hash is under its frequency, displaced, turned and scaled by more hashes, and kept if it lands inside the block and
    /// not on a road. All the hashes are 32-bit unsigned arithmetic, wrapping, as the client's are. Ground height is the
    /// block's height grid through the region's LandHeightTable, each 24 m cell split into two triangles along the
    /// diagonal LandblockMesh.GetSplitDir picks (from AC2D).
    /// </para>
    /// <para>
    /// Then, as the client: nothing in a 24 m cell with a building (CSortCell::has_building, 0x534D40; the cells are
    /// passed in, here the cells holding a building's origin), and nothing on ground steeper or flatter than the object
    /// takes (ObjectDesc::CheckSlope, 0x52FDD0: the ground triangle's normal's upward part, from MinSlope to MaxSlope).
    /// Without those two, 90% of the real objects were predicted in the game, and 4% more were predicted than made.
    /// </para>
    /// <para>
    /// Not the client's: the road test is ACE's simple one (the cell's road bits; the client's on_road also looks at how
    /// near the road's edge it is), and objects that would overlap each other are kept. Scenery density's rule
    /// (SceneryDensity.Keeps) is applied, so the two layers agree.
    /// </para>
    /// Kept apart from the dat reader so it can be tested.
    /// </summary>
    public static class SceneryLayout {
        public const int VertexDim = 9;
        public const float CellSize = 24;
        public const float BlockSize = 192;
        private const double ToFraction = 2.3283064e-10;

        /// <summary>
        /// A landblock's scenery: its id (0xXXYY....), its 81 terrain words and 81 height bytes (x major, as the cell dat
        /// has them), the region's height table, the scenes for a terrain type and scene type, the objects of a scene,
        /// the Scenery density percent, and the cells holding a building (cell x times 8 plus cell y), or null.
        /// </summary>
        public static List<PlacedScenery> Place(uint landblock, IReadOnlyList<ushort> terrain, IReadOnlyList<byte> heights,
            IReadOnlyList<float> heightTable, Func<int, int, IReadOnlyList<uint>> scenesFor,
            Func<uint, IReadOnlyList<SceneryObject>> sceneObjects, int densityPercent,
            ICollection<int> buildingCells = null) {
            var placed = new List<PlacedScenery>();

            if (terrain == null || terrain.Count < VertexDim * VertexDim) {
                return placed;
            }

            var blockX = (landblock >> 24) * 8;
            var blockY = ((landblock >> 16) & 0xFF) * 8;

            for (var i = 0; i < VertexDim * VertexDim; i++) {
                var word = terrain[i];
                var terrainType = (word >> 2) & 0x1F;
                var sceneType = word >> 11;
                var scenes = scenesFor(terrainType, sceneType);

                if (scenes == null || scenes.Count == 0) {
                    continue;
                }

                var cellX = i / VertexDim;
                var cellY = i % VertexDim;
                var gx = (uint)(cellX + blockX);
                var gy = (uint)(cellY + blockY);

                unchecked {
                    var pick = gy * (712977289u * gx + 1813693831u) - 1109124029u * gx + 2139937281u;
                    var sceneIndex = (int)(scenes.Count * (pick * ToFraction));

                    if (sceneIndex >= scenes.Count) {
                        sceneIndex = 0;
                    }

                    var objects = sceneObjects(scenes[sceneIndex]);

                    if (objects == null) {
                        continue;
                    }

                    var cellHash = 1360117743u * gx * gy + 1888038839u;
                    var noise = ((uint)-1109124029 * gx + 1813693831u * gy - cellHash * 23399u) * ToFraction;

                    for (var j = 0u; j < objects.Count; j++) {
                        var obj = objects[(int)j];

                        if (noise >= obj.Frequency || obj.Weenie != 0) {
                            continue;
                        }

                        var (dx, dy) = Displace(obj, gx, gy, j);
                        var x = cellX * CellSize + dx;
                        var y = cellY * CellSize + dy;

                        if (x < 0 || y < 0 || x > BlockSize || y > BlockSize || OnRoad(terrain, x, y)
                            || !SceneryDensity.Keeps(landblock & 0xFFFF0000, x, y, densityPercent)
                            || buildingCells != null && buildingCells.Contains(CellOf(x) * 8 + CellOf(y))) {
                            continue;
                        }

                        var (z, upward) = Ground(landblock, heights, heightTable, x, y);

                        if (upward < obj.MinSlope || upward > obj.MaxSlope) {
                            continue;
                        }

                        placed.Add(new PlacedScenery(obj.Model, x, y, z, Rotation(obj, gx, gy, j), Scale(obj, gx, gy, j)));
                    }
                }
            }

            return placed;
        }

        /// <summary>The object's place in its cell (Scenery.Displace).</summary>
        public static (float X, float Y) Displace(SceneryObject obj, uint ix, uint iy, uint iq) {
            unchecked {
                var x = obj.DisplaceX <= 0 ? obj.BaseX
                    : (float)((1813693831u * iy - (iq + 45773u) * (1360117743u * iy * ix + 1888038839u) - 1109124029u * ix)
                        * ToFraction * obj.DisplaceX + obj.BaseX);
                var y = obj.DisplaceY <= 0 ? obj.BaseY
                    : (float)((1813693831u * iy - (iq + 72719u) * (1360117743u * iy * ix + 1888038839u) - 1109124029u * ix)
                        * ToFraction * obj.DisplaceY + obj.BaseY);
                var quadrant = (1813693831u * iy - ix * (1870387557u * iy + 1109124029u) - 402451965u) * ToFraction;

                if (quadrant >= 0.75) {
                    return (y, -x);
                }

                if (quadrant >= 0.5) {
                    return (-x, -y);
                }

                if (quadrant >= 0.25) {
                    return (-y, x);
                }

                return (x, y);
            }
        }

        /// <summary>Its turn about the vertical, in radians (Scenery.RotateObj).</summary>
        public static float Rotation(SceneryObject obj, uint x, uint y, uint k) {
            if (obj.MaxRotation <= 0) {
                return 0;
            }

            unchecked {
                return (float)((1813693831u * y - (k + 63127u) * (1360117743u * y * x + 1888038839u) - 1109124029u * x)
                    * ToFraction * obj.MaxRotation * 0.0174533f);
            }
        }

        /// <summary>Its size (Scenery.ScaleObj).</summary>
        public static float Scale(SceneryObject obj, uint x, uint y, uint k) {
            if (obj.MinScale == obj.MaxScale || obj.MinScale <= 0) {
                return obj.MaxScale > 0 ? obj.MaxScale : 1;
            }

            unchecked {
                var power = (1813693831u * y - (k + 32593u) * (1360117743u * y * x + 1888038839u)
                    - 1109124029u * x) * ToFraction;
                return (float)(Math.Pow(obj.MaxScale / obj.MinScale, power) * obj.MinScale);
            }
        }

        /// <summary>Whether a place in the block is on a road: its cell's road bits (ACE's simple test).</summary>
        public static bool OnRoad(IReadOnlyList<ushort> terrain, float x, float y) {
            var cellX = Math.Min(VertexDim - 1, Math.Max(0, (int)Math.Floor(x / CellSize)));
            var cellY = Math.Min(VertexDim - 1, Math.Max(0, (int)Math.Floor(y / CellSize)));
            return (terrain[cellX * VertexDim + cellY] & 0x3) != 0;
        }

        /// <summary>How far a road reaches from its vertex's edge of a cell, in meters (the client's, and ACE's RoadWidth).</summary>
        public const float RoadWidth = 5;

        /// <summary>
        /// Whether a place is on a road as the client tells it (CLandBlock::on_road, 0x530D30; daralet-ACE's
        /// Physics Landblock.OnRoad): by the road bits at all four of the cell's corners, a road reaching RoadWidth from
        /// the cell's edges or across its diagonal. OnRoad, the simple test, takes the whole cell from its south-west
        /// corner alone, so grass stood on roads through a cell's other corners and was gone from cells beside one
        /// (the user's screenshot in Hebian-to, November 2026).
        /// </summary>
        public static bool OnRoadExact(IReadOnlyList<ushort> terrain, float x, float y) {
            var cellX = Math.Min(VertexDim - 2, Math.Max(0, (int)Math.Floor(x / CellSize)));
            var cellY = Math.Min(VertexDim - 2, Math.Max(0, (int)Math.Floor(y / CellSize)));
            bool Road(int vx, int vy) => (terrain[vx * VertexDim + vy] & 0x3) != 0;
            var r0 = Road(cellX, cellY);
            var r1 = Road(cellX, cellY + 1);
            var r2 = Road(cellX + 1, cellY);
            var r3 = Road(cellX + 1, cellY + 1);

            if (!r0 && !r1 && !r2 && !r3) {
                return false;
            }

            var dx = x - cellX * CellSize;
            var dy = y - cellY * CellSize;
            const float near = RoadWidth;
            const float far = CellSize - RoadWidth;

            if (r0) {
                if (r1) {
                    return r2 ? r3 || dx < near || dy < near : r3 ? dx < near || dy > far : dx < near;
                }

                return r2 ? r3 ? dx > far || dy < near : dy < near : r3 ? Math.Abs(dx - dy) < near : dx + dy < near;
            }

            if (r1) {
                return r2 ? r3 ? dx > far || dy > far : Math.Abs(dx + dy - CellSize) < near
                    : r3 ? dy > far : CellSize + dx - dy < near;
            }

            return r2 ? r3 ? dx > far : CellSize - dx + dy < near : r3 && CellSize * 2 - dx - dy < near;
        }

        /// <summary>
        /// Whether a cell's split runs north-west to south-east (true) or north-east to south-west
        /// (LandblockMesh.GetSplitDir,
        /// from AC2D), by its global cell.
        /// </summary>
        public static bool SplitNwSe(uint landblock, int cellX, int cellY) {
            unchecked {
                var x = (int)((landblock >> 24) * 8) + cellX;
                var y = (int)(((landblock >> 16) & 0xFF) * 8) + cellY;
                var dw = x * y * 0x0CCAC033 - x * 0x421BE3BD + y * 0x6C1AC587 - 0x519B8F25;
                return (dw & 0x80000000) == 0;
            }
        }

        /// <summary>The ground's height at a place in the block, on the cell's triangles.</summary>
        public static float GroundZ(uint landblock, IReadOnlyList<byte> heights, IReadOnlyList<float> heightTable,
            float x, float y) {
            return Ground(landblock, heights, heightTable, x, y).Z;
        }

        /// <summary>
        /// How many vertices apart the game draws a block's ground, by its ring from yours and the full-detail radius:
        /// LScape::get_block_orient's 1 within it, then 2, 4 out to 3 past it, and 8 (two triangles a block) beyond.
        /// </summary>
        public static int DetailStep(int ring, int fullRadius) {
            return ring <= fullRadius ? 1 : ring <= fullRadius + 1 ? 2 : ring <= fullRadius + 3 ? 4 : 8;
        }

        /// <summary>A place's cell along one side, 0 to 7.</summary>
        public static int CellOf(float meters) {
            return Math.Min(7, Math.Max(0, (int)Math.Floor(meters / CellSize)));
        }

        /// <summary>
        /// The ground at a place in the block: its height, and its normal's upward part (1 flat, less as it steepens; the
        /// value ObjectDesc::CheckSlope is given), both of the cell's triangle the place is on. With a step, the
        /// ground as the game draws a block in less detail: every step-th vertex, each coarse cell split by
        /// SplitNwSe of its coarse index (CLandBlockStruct::ConstructPolygons, 0x532A50, adds the cell's own index to
        /// the block's times 8 whatever its detail).
        /// </summary>
        public static (float Z, float Upward) Ground(uint landblock, IReadOnlyList<byte> heights,
            IReadOnlyList<float> heightTable, float x, float y, int step = 1) {
            if (heights == null || heights.Count < VertexDim * VertexDim || heightTable == null || heightTable.Count < 256) {
                return (0, 1);
            }

            var size = CellSize * step;
            var cells = (VertexDim - 1) / step;
            var cellX = Math.Min(cells - 1, Math.Max(0, (int)Math.Floor(x / size)));
            var cellY = Math.Min(cells - 1, Math.Max(0, (int)Math.Floor(y / size)));
            var u = Math.Min(1, Math.Max(0, x / size - cellX));
            var v = Math.Min(1, Math.Max(0, y / size - cellY));
            float H(int vx, int vy) => heightTable[heights[vx * step * VertexDim + vy * step]];
            var h00 = H(cellX, cellY);
            var h10 = H(cellX + 1, cellY);
            var h01 = H(cellX, cellY + 1);
            var h11 = H(cellX + 1, cellY + 1);

            // each triangle a plane: its height, and its rises east and north over a cell, for its normal
            float z, riseX, riseY;

            if (SplitNwSe(landblock, cellX, cellY)) {
                // the diagonal from the north-west corner (0,1) to the south-east (1,0)
                if (u + v <= 1) {
                    riseX = h10 - h00;
                    riseY = h01 - h00;
                    z = h00 + riseX * u + riseY * v;
                }
                else {
                    riseX = h11 - h01;
                    riseY = h11 - h10;
                    z = h11 - riseX * (1 - u) - riseY * (1 - v);
                }
            }
            else if (u >= v) {
                // the diagonal from the south-west corner (0,0) to the north-east (1,1)
                riseX = h10 - h00;
                riseY = h11 - h10;
                z = h00 + riseX * u + riseY * v;
            }
            else {
                riseX = h11 - h01;
                riseY = h01 - h00;
                z = h00 + riseX * u + riseY * v;
            }

            // the normal of (1, 0, riseX / cell) and (0, 1, riseY / cell), made unit: its upward part
            var gx = riseX / size;
            var gy = riseY / size;
            return (z, (float)(1 / Math.Sqrt(1 + gx * gx + gy * gy)));
        }
    }
}
