using System;
using System.Collections.Generic;
using System.Linq;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Microsoft.DirectX;
using Microsoft.DirectX.Direct3D;
using UtilityBelt.Service;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Billboards for the scenery past the real scenery's radius (billboards stage 3; the user's call, November 2026:
    /// real scenery to 4-5 landblocks, billboards beyond, out to the landscape's edge).
    /// <para>
    /// Which: every landblock in the landscape (within its radius of yours) outside the full-detail ring, its scenery
    /// worked out by Feed/SceneryLayout.cs at the density in use, a few blocks a frame (the dat readers are only safe on
    /// the game's thread, and a few hundred blocks at once would stall it). What: each object a pair of crossed upright
    /// pictures (BillboardModels), turned by its own rotation and sized by its scale, so they're the same from every
    /// side and can sit in one vertex buffer (managed, PositionTextured), made again only when you cross a landblock or
    /// the settings change, one draw a model.
    /// </para>
    /// <para>
    /// Drawn on Decal's RenderFrame, which comes after the game's 3D world and before its interface, so the world's depth
    /// buffer is there: billboards behind a hill are hidden by it. The game's camera is the device's View and Projection
    /// as it left them, the world in your landblock's meters with up the middle number (x, height, y), as the combat
    /// numbers read it (Feed/CombatProjection.cs); a frame where the device holds the camera with two axes swapped (some
    /// spell effects) draws none. The device's state is captured before and put back after (a StateBlock): depth test
    /// and write on, alpha test at half (the pictures' cut-out), no blending or culling, lit by the game's own sun and
    /// ambient (DrawDistance.Lighting.cs), the texture
    /// times that light. A failure is logged once and billboards stop for the session. `[billboards]` lines in log.txt.
    /// </para>
    /// Untested in the game when written.
    /// </summary>
    internal sealed partial class DrawDistance {
        private const int BlocksAFrame = 6;

        /// <summary>Two crossed pictures, two triangles each.</summary>
        private const int VerticesABillboard = 12;

        /// <summary>
        /// Each billboard of the build under way, before it's written: kept and reused, not a list of every vertex by model
        /// copied again into one array (three copies of 76,000 billboards' vertices, every landblock crossed, in the large
        /// object heap, which a 32-bit game doesn't easily get back).
        /// </summary>
        private readonly List<(BillboardModel Model, float X, float Y, float Z, float Yaw, float Scale, bool Far)>
            billboardPlaces = new List<(BillboardModel, float, float, float, float, float, bool)>();

        /// <summary>
        /// The billboards standing on the far land (Ui/FarLand.cs), past the game's landscape: drawn by FarLand with its
        /// camera, in the pixels the game left empty, since the game's camera ends near 3.3 km.
        /// </summary>
        private readonly List<(Texture Texture, int Start, int Triangles)> farLandBatches =
            new List<(Texture, int, int)>();

        /// <summary>The landscape radius the billboards were last asked for: past it, they stand on the far land.</summary>
        private int billboardLandscape;
        private int farLandBillboardCount;

        // CustomVertex.PositionNormalTextured.Format, which the compiler can't read without Microsoft.VisualC (a C++ const)
        private const VertexFormats BillboardFormat =
            VertexFormats.Position | VertexFormats.Normal | VertexFormats.Texture1;

        private readonly BillboardModels billboardModels = new BillboardModels();
        private readonly Dictionary<uint, List<PlacedScenery>> blockScenery = new Dictionary<uint, List<PlacedScenery>>();
        private readonly Dictionary<uint, List<byte>> blockHeights = new Dictionary<uint, List<byte>>();
        private readonly Queue<uint> blocksToWorkOut = new Queue<uint>();

        /// <summary>The models the build under way uses: the rest are let go after it (BillboardModels.KeepOnly).</summary>
        private readonly HashSet<uint> usedModels = new HashSet<uint>();
        private List<uint> billboardBlocks = new List<uint>();
        private int billboardDensity = -1;
        private string billboardKey;
        private uint billboardHome;

        /// <summary>
        /// The landblock the blocks being worked out are around: it only becomes billboardHome once their buffers are
        /// made. Set at once, the old buffers were drawn a landblock off until the new ones were ready (seen in the game,
        /// November 2026: crossing a landblock's edge, the far forest vanished and came back a few steps on).
        /// </summary>
        private uint pendingHome;
        private bool billboardsToBuild;
        private VertexBuffer billboardBuffer;
        private readonly List<(Texture Texture, int Start, int Triangles)> billboardBatches =
            new List<(Texture, int, int)>();
        private StateBlock savedState;
        private bool billboardsFailed;
        private int billboardCount;

        /// <summary>Once a second from Apply: which blocks want billboards now, and whether that's changed.</summary>
        private void UpdateBillboards(bool wanted, int landscape) {
            if (!wanted || billboardsFailed || inPortalSpace) {
                if (billboardBuffer != null) {
                    ClearBillboards();
                    PluginCore.LogFile("[billboards] off");
                }

                billboardKey = null;
                return;
            }

            // your character's landblock: the game's own full-detail ring is round it (the load point), so the hole
            // left for the real scenery is too; drawn shifted into the camera's frame (RenderOrigin), which near an
            // edge is a landblock away
            var home = unchecked((uint)CoreManager.Current.Actions.Landcell) >> 16;

            // indoors (a dungeon's cells) there's no landscape to stand billboards on
            if ((unchecked((uint)CoreManager.Current.Actions.Landcell) & 0xFFFF) >= 0x100) {
                if (billboardBuffer != null) {
                    ClearBillboards();
                }

                billboardKey = null;
                return;
            }

            var key = $"{home:X4} {patchedRadius} {TreeRing} {skippedVersion} "
                + $"{landscape} {appliedDensity} {Options.ClampedFarTrees} "
                + Options.Billboards + " " + Options.FarBuildings + " " + FarLandBillboardRings + " " + FarLandTakesOver;

            if (key == billboardKey) {
                return;
            }

            billboardKey = key;

            if (billboardDensity != appliedDensity) {
                blockScenery.Clear();
                billboardDensity = appliedDensity;
            }

            var hx = (int)(home >> 8);
            var hy = (int)(home & 0xFF);
            var blocks = new List<uint>();

            var outer = landscape + FarLandBillboardRings;
            billboardLandscape = landscape;

            for (var dx = -outer; dx <= outer; dx++) {
                for (var dy = -outer; dy <= outer; dy++) {
                    var ring = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    var x = hx + dx;
                    var y = hy + dy;

                    if (x < 0 || y < 0 || x > 0xFE || y > 0xFE) {
                        continue;
                    }

                    // past the full-detail rings, and inside them past Scenery where the game made no trees (Ground
                    // detail, DrawDistance.Ground.cs); a block walked away from keeps its real ones, and isn't doubled
                    var id = (uint)(x << 8 | y);

                    if (ring <= TreeRing || ring <= patchedRadius && !TreesSkipped(id)) {
                        continue;
                    }

                    blocks.Add(id);
                }
            }

            billboardBlocks = blocks;
            pendingHome = home;
            blocksToWorkOut.Clear();

            var missing = blocks.Where(b => !blockScenery.ContainsKey(b) || !blockBuildings.ContainsKey(b));

            foreach (var block in missing) {
                blocksToWorkOut.Enqueue(block);
            }

            billboardsToBuild = true;
            PluginCore.LogFile($"[billboards] {blocks.Count} landblocks from ring {TreeRing + 1} to {outer}, "
                + $"{blocksToWorkOut.Count} to work out");
        }

        /// <summary>Every frame: a few more blocks worked out, the buffer made once they all are, and the billboards
        /// drawn.</summary>
        private void BillboardFrame() {
            if (billboardKey == null || billboardsFailed) {
                return;
            }

            try {
                var portal = UBService.PortalDat;
                var cells = UBService.CellDat;

                if (portal == null || cells == null) {
                    return;
                }

                region = region ?? portal.ReadFromDat<ACE.DatLoader.FileTypes.RegionDesc>(RegionId);

                for (var i = 0; i < BlocksAFrame && blocksToWorkOut.Count > 0; i++) {
                    var block = blocksToWorkOut.Dequeue();
                    var id = block << 16;
                    var land = cells.ReadFromDat<ACE.DatLoader.FileTypes.CellLandblock>(id | 0xFFFF);
                    blockHeights[block] = land?.Height.ToList();
                    blockBuildings[block] = BuildingsOf(cells, id);
                    blockScenery[block] = land == null ? new List<PlacedScenery>()
                        : SceneryLayout.Place(id, land.Terrain, land.Height, region.LandDefs.LandHeightTable, ScenesFor,
                            sceneId => ObjectsOf(portal, sceneId), appliedDensity, BuildingCells(cells, id));
                }

                var device = UBService.Huds.D3Ddevice;

                if (device == null) {
                    return;
                }

                if (blocksToWorkOut.Count == 0 && billboardsToBuild) {
                    BuildBillboards(device, portal);
                }

                // drawn inside the game's frame, before its see-through things, when that's hooked (Ui/WorldPass.cs)
                if (!WorldPass.Hooked) {
                    DrawBillboards(device);
                }
            }
            catch (Exception ex) {
                billboardsFailed = true;
                PluginCore.LogFile("[billboards] stopped for the session: " + ex);
            }
        }

        /// <summary>The billboards drawn from inside the game's frame (Ui/WorldPass.cs).</summary>
        private void DrawBillboardsInWorldPass(Device device) {
            if (disposed || billboardKey == null || billboardsFailed) {
                return;
            }

            try {
                DrawBillboards(device);
            }
            catch (Exception ex) {
                billboardsFailed = true;
                PluginCore.LogFile("[billboards] stopped for the session: " + ex);
            }
        }

        /// <summary>The crossed pictures of every far object, grouped by model, into one vertex buffer.</summary>
        private void BuildBillboards(Device device, ACE.DatLoader.DatDatabase portal) {
            billboardsToBuild = false;
            var byModel = new Dictionary<(BillboardModel Model, bool Far), int>();
            usedModels.Clear();
            billboardPlaces.Clear();
            var farByModel = new Dictionary<FarTreeModel, List<FarTreeInstance>>();
            var showBillboards = Options.Billboards;
            var hx = (int)(pendingHome >> 8);
            var hy = (int)(pendingHome & 0xFF);
            var objects = 0;
            farBuildingCount = 0;

            foreach (var block in billboardBlocks) {
                if (!blockScenery.TryGetValue(block, out var placed)) {
                    continue;
                }

                var offsetX = ((int)(block >> 8) - hx) * SceneryLayout.BlockSize;
                var offsetY = ((int)(block & 0xFF) - hy) * SceneryLayout.BlockSize;

                // stood on the ground as the game draws this block, in less detail the further out it is: on the true
                // ground, trees on a hump floated over the flattened ridge the game draws
                var ring = Math.Max(Math.Abs((int)(block >> 8) - hx), Math.Abs((int)(block & 0xFF) - hy));
                // past the game's landscape, on the far land, which is drawn in full detail (Ui/FarLand.cs)
                var beyond = ring > billboardLandscape;
                // and with the far land taking over the game's coarse rings, on its full-detail ground there too
                var step = beyond || FarLandTakesOver ? 1 : SceneryLayout.DetailStep(ring, patchedRadius);
                blockHeights.TryGetValue(block, out var heights);

                var farRing = !beyond && IsFarTreeRing(ring);

                if (Options.FarBuildings && ring > patchedRadius && !beyond) {
                    AddFarBuildings(block, step, heights, offsetX, offsetY, portal, device, farByModel);
                }

                foreach (var p in placed) {
                    if (farRing) {
                        // the game's own model, drawn by the plugin (DrawDistance.FarTrees.cs)
                        var tree = farTreeModels.Get(p.Model, portal, device);
                        usedModels.Add(p.Model);

                        if (tree == null) {
                            continue;
                        }

                        if (!farByModel.TryGetValue(tree, out var copies)) {
                            copies = new List<FarTreeInstance>();
                            farByModel[tree] = copies;
                        }

                        var treeZ = heights == null || step == 1 ? p.Z : SceneryLayout.Ground(block << 16, heights,
                            region.LandDefs.LandHeightTable, p.X, p.Y, step).Z;
                        copies.Add(FarTree(offsetX + p.X, offsetY + p.Y, treeZ, p.Yaw, p.Scale));
                        continue;
                    }

                    if (!showBillboards) {
                        continue;
                    }

                    var model = billboardModels.Get(p.Model, portal, device);
                    usedModels.Add(p.Model);

                    if (model == null) {
                        continue;
                    }

                    var z = heights == null || step == 1 ? p.Z : SceneryLayout.Ground(block << 16, heights,
                        region.LandDefs.LandHeightTable, p.X, p.Y, step).Z;
                    byModel.TryGetValue((model, beyond), out var count);
                    byModel[(model, beyond)] = count + 1;
                    billboardPlaces.Add((model, offsetX + p.X, offsetY + p.Y, z, p.Yaw, p.Scale, beyond));
                    objects++;
                }
            }

            ClearBillboards();
            billboardHome = pendingHome;
            BuildFarTrees(device, farByModel);
            ForgetScenerySeen();
            var modelsGone = billboardModels.KeepOnly(usedModels) + farTreeModels.KeepOnly(usedModels);

            if (modelsGone > 0) {
                PluginCore.LogFile($"[billboards] {modelsGone} models no longer in sight let go");
            }

            if (objects == 0) {
                billboardPlaces.Clear();
                PluginCore.LogFile("[billboards] nothing to draw");
                return;
            }

            // each model's run in the buffer (the game's camera's first, then the far land's), then every billboard
            // written once, straight into it
            var first = new Dictionary<(BillboardModel Model, bool Far), int>();
            var start = 0;
            farLandBillboardCount = 0;

            foreach (var far in new[] { false, true }) {
                foreach (var kv in byModel) {
                    if (kv.Key.Far != far) {
                        continue;
                    }

                    first[kv.Key] = start;
                    (far ? farLandBatches : billboardBatches).Add((kv.Key.Model.Texture, start,
                        kv.Value * VerticesABillboard / 3));
                    start += kv.Value * VerticesABillboard;
                    farLandBillboardCount += far ? kv.Value : 0;
                }
            }

            billboardBuffer = new VertexBuffer(typeof(CustomVertex.PositionNormalTextured), start, device,
                Usage.WriteOnly, BillboardFormat, ScenePool.Buffers);
            WriteBillboards(billboardBuffer, first);
            billboardPlaces.Clear();

            // a list grown past its use keeps its room: let a big one go, so a far view seen once isn't held for good
            if (billboardPlaces.Capacity > 50000 && objects < billboardPlaces.Capacity / 2) {
                billboardPlaces.Capacity = 0;
            }

            billboardCount = objects;
            if (farBuildingCount > 0) {
                PluginCore.LogFile($"[far-buildings] {farBuildingCount} buildings and placed objects past ring "
                    + $"{patchedRadius}");
            }

            PluginCore.LogFile($"[billboards] {objects} billboards ({farLandBillboardCount} on the far land) in "
                + $"{billboardBatches.Count + farLandBatches.Count} runs, "
                + $"{start / 3} triangles, {FreeMegabytes()} MB free, plugin {GC.GetTotalMemory(false) >> 20} MB, "
                + $"{blockScenery.Count} blocks' scenery kept");
        }

        /// <summary>Every billboard placed this build, written into the buffer at its model's run.</summary>
        private unsafe void WriteBillboards(VertexBuffer buffer, Dictionary<(BillboardModel Model, bool Far), int> first) {
            var stream = buffer.Lock(0, 0, LockFlags.None);

            try {
                var vertices = (CustomVertex.PositionNormalTextured*)stream.InternalDataPointer;
                var next = new Dictionary<(BillboardModel Model, bool Far), int>(first);

                foreach (var (model, x, y, z, yaw, scale, far) in billboardPlaces) {
                    var at = next[(model, far)];
                    next[(model, far)] = at + VerticesABillboard;
                    WriteCrossedQuads(vertices + at, model, x, y, z, yaw, scale);
                }
            }
            finally {
                buffer.Unlock();
            }
        }

        /// <summary>
        /// Everything of the far scenery let go, for a teleport: the buffers, every block's worked-out scenery, and a full
        /// collection, so the game has that room while it loads the new place (November 2026: with 1.1 GB free at
        /// Holtburg a teleport to Drudge Cave ran it out of memory). Made again once you're out.
        /// </summary>
        private void ReleaseFarScenery() {
            var before = FreeMegabytes();
            ClearBillboards();
            blockScenery.Clear();
            blockHeights.Clear();
            blockBuildings.Clear();
            blocksToWorkOut.Clear();
            billboardPlaces.Clear();
            billboardPlaces.Capacity = 0;
            billboardKey = null;
            billboardsToBuild = false;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            PluginCore.LogFile($"[billboards] let go for the teleport: {before} MB free before, {FreeMegabytes()} after");
        }

        /// <summary>
        /// The worked-out scenery of blocks no longer wanted let go (all but the billboards' blocks now): it was kept
        /// for every block ever worked out, so each teleport added a whole landscape's worth (November 2026: the game
        /// had 830 MB free on a teleport, and froze).
        /// </summary>
        private void ForgetScenerySeen() {
            if (blockScenery.Count <= billboardBlocks.Count) {
                return;
            }

            var keep = new HashSet<uint>(billboardBlocks);

            foreach (var block in blockScenery.Keys.Where(b => !keep.Contains(b)).ToList()) {
                blockScenery.Remove(block);
                blockHeights.Remove(block);
                blockBuildings.Remove(block);
            }
        }

        /// <summary>
        /// Two upright pictures through the object's place at right angles, turned by its yaw: world x and y in your
        /// landblock's meters, z the ground; the device's world is (x, height, y).
        /// </summary>
        private static unsafe void WriteCrossedQuads(CustomVertex.PositionNormalTextured* vertices,
            BillboardModel model, float x, float y, float z, float yaw, float scale) {
            var half = model.Width * scale / 2;
            var bottom = z + model.Bottom * scale;
            var top = bottom + model.Height * scale;
            var middle = model.Middle * scale;
            var cos = (float)Math.Cos(yaw);
            var sin = (float)Math.Sin(yaw);

            for (var side = 0; side < 2; side++) {
                var dx = side == 0 ? cos : -sin;
                var dy = side == 0 ? sin : cos;
                var cx = x + dx * middle;
                var cy = y + dy * middle;
                var lx = cx - dx * half;
                var ly = cy - dy * half;
                var rx = cx + dx * half;
                var ry = cy + dy * half;
                var bl = new CustomVertex.PositionNormalTextured(lx, bottom, ly, 0, 1, 0, 0, 1);
                var br = new CustomVertex.PositionNormalTextured(rx, bottom, ry, 0, 1, 0, 1, 1);
                var tl = new CustomVertex.PositionNormalTextured(lx, top, ly, 0, 1, 0, 0, 0);
                var tr = new CustomVertex.PositionNormalTextured(rx, top, ry, 0, 1, 0, 1, 0);
                var at = vertices + side * 6;
                at[0] = bl;
                at[1] = tl;
                at[2] = tr;
                at[3] = bl;
                at[4] = tr;
                at[5] = br;
            }
        }

        private void DrawBillboards(Device device) {
            var anyBillboards = billboardBuffer != null && billboardBatches.Count > 0;

            if (!anyBillboards && farTreeInstances == null) {
                return;
            }

            // the device's world is measured from the camera's landblock (RenderOrigin); one crossed into since the buffers
            // were made is a whole landblock's shift, put on them until they're made again (once a second), so the far
            // scenery neither jumps nor blinks out
            var origin = RenderOrigin.Landblock;
            var shift = Matrix.Translation(
                ((int)(billboardHome >> 8) - (int)(origin >> 8)) * SceneryLayout.BlockSize, 0,
                ((int)(billboardHome & 0xFF) - (int)(origin & 0xFF)) * SceneryLayout.BlockSize);

            if (CameraSwapped(device)) {
                return;
            }

            DrawFarTrees(device, shift);

            if (!anyBillboards) {
                return;
            }

            DrawBillboardRuns(device, shift, billboardBatches, null);
        }

        /// <summary>How many rings of the far land carry billboards: all of it, with billboards and far land both on.</summary>
        private int FarLandBillboardRings => Options.Billboards ? Options.ClampedFarLand : 0;

        /// <summary>Whether the far land draws the game's coarse rings in full detail (Ui/FarLand.Takeover.cs).</summary>
        private bool FarLandTakesOver => Options.ClampedFarLand > 0 && Options.FarLandNear;

        /// <summary>
        /// The billboards standing on the far land, drawn by FarLand inside its pass (its stencil test and depths kept)
        /// with its camera, whose far plane is past the game's.
        /// </summary>
        public void DrawFarLandBillboards(Device device, Matrix projection) {
            if (disposed || billboardsFailed || billboardBuffer == null || farLandBatches.Count == 0) {
                return;
            }

            var origin = RenderOrigin.Landblock;
            var shift = Matrix.Translation(
                ((int)(billboardHome >> 8) - (int)(origin >> 8)) * SceneryLayout.BlockSize, 0,
                ((int)(billboardHome & 0xFF) - (int)(origin & 0xFF)) * SceneryLayout.BlockSize);
            DrawBillboardRuns(device, shift, farLandBatches, projection);
        }

        /// <summary>Runs of the billboard buffer, lit as the game lights outdoors, with the camera's projection or one given.</summary>
        private void DrawBillboardRuns(Device device, Matrix shift, List<(Texture Texture, int Start, int Triangles)> runs,
            Matrix? projection) {
            // made and let go each frame: Direct3D won't reset the device while a state block lives, and one kept
            // from frame to frame hung the game when it reset on exiting (November 2026)
            savedState = new StateBlock(device, StateBlockType.All);
            LogDeviceLights(device);
            savedState.Capture();

            try {
                device.Transform.World = shift;

                if (projection.HasValue) {
                    device.Transform.Projection = projection.Value;
                }

                device.VertexFormat = BillboardFormat;
                device.SetStreamSource(0, billboardBuffer, 0);
                device.RenderState.ZBufferEnable = true;
                device.RenderState.ZBufferWriteEnable = true;
                device.RenderState.AlphaTestEnable = true;
                device.RenderState.ReferenceAlpha = 128;
                device.RenderState.AlphaFunction = Compare.GreaterEqual;
                device.RenderState.AlphaBlendEnable = false;
                device.RenderState.CullMode = Cull.None;
                // the game's sun and ambient left as they are (DrawDistance.Lighting.cs), on a white material, normals up
                device.RenderState.Lighting = true;
                device.RenderState.ColorVertex = false;
                device.RenderState.SpecularEnable = false;
                LightLikeTheGame(device);
                device.Material = new Material {
                    DiffuseColor = new ColorValue(1f, 1f, 1f), AmbientColor = new ColorValue(1f, 1f, 1f),
                };
                device.TextureState[0].ColorOperation = TextureOperation.Modulate;
                device.TextureState[0].ColorArgument2 = TextureArgument.Diffuse;
                device.TextureState[0].ColorArgument1 = TextureArgument.TextureColor;
                device.TextureState[0].AlphaOperation = TextureOperation.SelectArg1;
                device.TextureState[0].AlphaArgument1 = TextureArgument.TextureColor;
                device.TextureState[1].ColorOperation = TextureOperation.Disable;
                device.SamplerState[0].MinFilter = TextureFilter.Linear;
                device.SamplerState[0].MagFilter = TextureFilter.Linear;
                device.SamplerState[0].MipFilter = TextureFilter.Linear;

                foreach (var batch in runs) {
                    device.SetTexture(0, batch.Texture);
                    device.DrawPrimitives(PrimitiveType.TriangleList, batch.Start, batch.Triangles);
                }
            }
            finally {
                savedState.Apply();
                savedState.Dispose();
                savedState = null;
            }
        }

        /// <summary>Whether the device holds the camera with its second and third axes swapped (CombatProjection).</summary>
        private static bool CameraSwapped(Device device) {
            return RenderOrigin.Swapped(device.Transform.View);
        }

        private void ClearBillboards() {
            ClearFarTrees();
            billboardBuffer?.Dispose();
            billboardBuffer = null;
            billboardBatches.Clear();
            farLandBatches.Clear();
            farLandBillboardCount = 0;
            billboardCount = 0;
        }

        private void DisposeBillboards() {
            ClearBillboards();
            DisposeFarTrees();
            savedState?.Dispose();
            savedState = null;
            billboardModels.Dispose();
        }

        /// <summary>For a test run's results: how many billboards are up.</summary>
        public string BillboardState => billboardsFailed ? "billboards stopped (see log.txt)"
            : billboardKey == null ? "no billboards" : $"{billboardCount} billboards"
                + (blocksToWorkOut.Count > 0 ? $", {blocksToWorkOut.Count} blocks still to work out" : "");
    }
}
