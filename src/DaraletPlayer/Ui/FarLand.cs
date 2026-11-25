using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using ACE.DatLoader;
using ACE.DatLoader.FileTypes;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Microsoft.DirectX;
using Microsoft.DirectX.Direct3D;
using UtilityBelt.Service;
using DatTexture = ACE.DatLoader.FileTypes.Texture;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Far land (DrawDistanceOptions.FarLand; stage 1 of the plan in SESSION-HANDOFF.md, the user's go-ahead, November
    /// 2026): the ground past the game's landscape, drawn by the plugin at full detail (all 9x9 height points a block,
    /// so ridges keep their shape far off) for a few MB, so the game's own landscape, which costs about 1 MB a block of
    /// the 32-bit game's memory, can be kept low.
    /// <para>
    /// Each block from the game's last ring out: its 81 heights through the region's LandHeightTable, each cell split
    /// as the game splits it (SceneryLayout.SplitNwSe), normals from the heights, each vertex the colour of its terrain's
    /// texture averaged (a road vertex the road's), and a skirt down each edge so no crack shows where blocks meet.
    /// Read straight from the cell dat's file, not through ReadFromDat, which keeps every file it reads for good.
    /// </para>
    /// <para>
    /// Drawn only where the game drew nothing: the game's projection ends at about 3.3 km, so the land has a camera of its
    /// own (the game's, with the far plane at 60 km), and its depths mean nothing to the game's. So the stencil's top bit
    /// marks the pixels whose depth is still the cleared 1.0 (a screen quad at depth 1, LessEqual), the land is drawn in
    /// those alone with its own depths (so it hides itself properly), and depth 1.0 is put back there after, for the
    /// billboards and the game's see-through things to test against as before. Drawn in the game's frame (WorldPass).
    /// Lit as Grass lights the ground. `[far-land]` lines in log.txt. Untested in the game when written.
    /// </para>
    /// </summary>
    internal sealed partial class FarLand : IDisposable {
        private const uint RegionId = 0x13000000;
        private const int Dim = SceneryLayout.VertexDim;
        private const float BlockSize = SceneryLayout.BlockSize;
        private const float CellSize = SceneryLayout.CellSize;

        /// <summary>
        /// The light on the far land: its ambient plus the sun on its normal, times this. Measured where the far land meets
        /// the game's own ground at the Holtburg overlook (November 2026), in two lights (stormy, then dusk): Grass's 1.15
        /// came out 1.31 times as bright as the game's ground (86, 88, 39 against 66, 67, 31), its water too.
        /// </summary>
        private const float GroundLight = 0.88f;
        private const float SkirtDepth = 30;
        private const float NearPlane = 10;
        private const float FarPlane = 60000;
        private const uint LScapeAt = 0x8456F0;
        private const int StencilBit = 0x80;
        private const uint RoadType = 0x20;


        /// <summary>Milliseconds a frame spent reading blocks from the dat.</summary>
        private const double ReadBudgetMs = 3;

        private const VertexFormats Format = VertexFormats.Position | VertexFormats.Normal | VertexFormats.Diffuse
            | VertexFormats.Texture1;
        private const VertexFormats QuadFormat = VertexFormats.Transformed | VertexFormats.Diffuse;

        private readonly Settings settings;
        private readonly DrawDistance drawDistance;

        /// <summary>The plugin's land tinted red (test step farlanddebug), to see where it starts.</summary>
        public static bool Debug;

        /// <summary>A test step's switch (`set farlandfine 0`): no fine tier, the mid atlas instead, to compare.</summary>
        public static bool NoFine;
        private readonly Action<Device> inWorldPass;
        private readonly Dictionary<uint, LandBlock> blocks = new Dictionary<uint, LandBlock>();
        private readonly Queue<uint> toRead = new Queue<uint>();
        private readonly Dictionary<int, int> colours = new Dictionary<int, int>();
        private bool disposed;
        private bool failed;

        private IntPtr vertexShader;
        private IntPtr pixelShader;
        private RegionDesc region;
        /// <summary>The terrains' pictures and alpha maps made (StartTerrain, on a worker).</summary>
        private volatile bool terrainReady;
        private VertexBuffer vertices;
        private IndexBuffer indices;
        private int vertexCount;
        private int triangleCount;

        /// <summary>The landblock the buffers are measured from (0xXXYY).</summary>
        private uint builtHome = uint.MaxValue;
        private string wantedKey;
        private uint wantedHome;
        private bool toBuild;

        /// <summary>Whether the land's buffers are in the default pool, so let go before a device reset.</summary>
        private bool buffersInDefaultPool;
        private int roadColour = unchecked((int)0xFF807060);

        /// <summary>Blocks read only to be baked into the atlases (the ring inside the seam), never drawn.</summary>
        private HashSet<uint> marginOnly = new HashSet<uint>();

        /// <summary>The triangles of each run, near (0) or far (1) by tier, in the buffers' order.</summary>
        private readonly int[,] runTriangles = new int[2, TierCount];

        private sealed class LandBlock {
            public byte[] Heights;
            public ushort[] Terrain;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LandVertex {
            public float X, Y, Z;
            public float NX, NY, NZ;
            public int Colour;
            public float U, V;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct QuadVertex {
            public float X, Y, Z, W;
            public int Colour;
        }

        public FarLand(Settings settings, DrawDistance drawDistance) {
            this.drawDistance = drawDistance;
            this.settings = settings;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
            inWorldPass = DrawInWorldPass;
            WorldPass.Add(inWorldPass, () => !failed && vertices != null && Options.ClampedFarLand > 0);
        }

        private DrawDistanceOptions Options => settings.Data.DrawDistance;

        /// <summary>For a test run's results.</summary>
        public string State => failed ? "far land stopped (see log.txt)" : vertices == null ? "no far land"
            : $"far land {blocks.Count} blocks, {triangleCount} triangles, {MegabytesHeld:0.0} MB";

        private double MegabytesHeld => (vertexCount * 28.0 + triangleCount * 12.0) / (1024 * 1024);

        private static unsafe int LoadedLandscape {
            get {
                var lscape = *(IntPtr*)LScapeAt;
                return lscape == IntPtr.Zero ? 0 : *(int*)lscape;
            }
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            using var hitch = HitchLog.Measure("far land");

            if (disposed || failed) {
                if (failed) {
                    LoadPieces.Ready(LoadPieces.FarLand);
                }

                return;
            }

            try {
                if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                    return;
                }

                FollowTakeover();
                var rings = Options.ClampedFarLand;
                var landcell = unchecked((uint)CoreManager.Current.Actions.Landcell);
                var device = UBService.Huds.D3Ddevice;
                // outdoors, or in a building's room that sees out (OutdoorView), not by the cell number alone
                var outdoors = OutdoorView.Now;

                nearActive = hooked && rings > 0 && device != null && outdoors && vertices != null;
                comDevice = device == null ? IntPtr.Zero : DeviceCom(device);

                if (rings == 0 || device == null || !outdoors) {
                    if (vertices != null && rings == 0) {
                        Clear();
                        blocks.Clear();
                        toRead.Clear();
                        // the atlases too: off, they held about 130 MB of the card's memory for nothing
                        LetGoOfAtlas();
                        PluginCore.LogFile("[far-land] off");
                    }

                    if (rings == 0) {
                        wantedKey = null;
                    }

                    // off or indoors: nothing coming for the loading bar to wait for
                    if (device != null) {
                        LoadPieces.Ready(LoadPieces.FarLand);
                    }

                    return;
                }

                Frame(device, landcell >> 16, rings);
            }
            catch (Exception ex) {
                failed = true;
                PluginCore.LogFile("[far-land] stopped for the session: " + ex);
                Clear();
            }
        }

        private void Frame(Device device, uint home, int rings) {
            var portal = UBService.PortalDat;
            var cells = UBService.CellDat;
            var landscape = LoadedLandscape;

            if (portal == null || cells == null || landscape < 1) {
                return;
            }

            // nothing read, baked or built while the game loads a place (a teleport or a login, and a while after):
            // building at the moment of a teleport, beside the game's own load, came before crashes in its cache with
            // little memory free (November 2026); the old place's land isn't drawn meanwhile
            if (drawDistance.Arriving) {
                AbortBuild();

                if (vertices != null) {
                    Clear();
                    PluginCore.LogFile("[far-land] let go while the game loads the place");
                }

                wantedKey = null;
                toRead.Clear();
                ForgetBakes();
                bakesQueued = false;
                return;
            }

            // built once, for the landscape stepped up to, not first for the game's own (DrawDistance.HoldBuilds)
            if (drawDistance.HoldBuilds && vertices == null) {
                return;
            }

            if (vertexShader == IntPtr.Zero) {
                vertexShader = DeviceCalls.VertexShader(device, "farland.vs.cso");
                pixelShader = DeviceCalls.PixelShader(device, "farland.ps.cso");
            }

            if (region == null) {
                region = portal.ReadFromDat<RegionDesc>(RegionId);
                StartTerrain(portal);
            }

            // the terrains' pictures and the alpha maps are made on a worker at first (StartTerrain)
            if (!terrainReady) {
                return;
            }

            NoteCoarseRings(home);
            var nearStart = hooked && nearestCoarseRing > 0 ? Math.Min(nearestCoarseRing, landscape) : 0;
            var key = $"{home:X4} {landscape} {rings} {nearStart} {NoFine}";

            if (key != wantedKey) {
                Want(home, landscape, rings, nearStart);
                wantedKey = key;
            }

            ReadSome(cells);

            // made again after a device reset: every block in the buffers baked again, the land in its vertex colours
            // meanwhile
            if (atlases[MidTier] == null) {
                MakeAtlas(device);

                if (!toBuild) {
                    QueueBakes();
                }
            }

            // the new blocks' pictures baked before their buffers are made: the old buffers' blocks keep their slots
            // meanwhile (two blocks share a slot only 64 apart), so nothing shows another block's picture
            if (toBuild && toRead.Count == 0 && !bakesQueued) {
                QueueBakes();
                bakesQueued = true;
            }

            BakeSome(device);

            // written a slice a frame, the old buffers drawn till the new are whole (FarLand.Building.cs)
            if (toBuild && toRead.Count == 0 && !BakesPending && !Building) {
                StartBuild(device);
                toBuild = false;
                bakesQueued = false;
            }

            ContinueBuild();
        }

        /// <summary>
        /// The blocks from the game's last ring out to the far land's edge, nearest first; the rest let go.
        /// </summary>
        private void Want(uint home, int landscape, int rings, int nearStart) {
            var hx = (int)(home >> 8);
            var hy = (int)(home & 0xFF);
            var outer = landscape + rings;
            var wanted = new List<(uint Block, int Ring)>();
            var near = new HashSet<uint>();
            var start = nearStart > 0 ? nearStart : landscape;
            // each block's atlas tier by its ring (FarLand.Textures.cs), with a ring either side baked into each too, the
            // inner one (the game's own ground) read for that alone
            var drawn = new Dictionary<uint, int>();
            var baked = new Dictionary<uint, List<int>>();
            var margin = new HashSet<uint>();

            for (var dx = -outer; dx <= outer; dx++) {
                for (var dy = -outer; dy <= outer; dy++) {
                    var ring = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    var x = hx + dx;
                    var y = hy + dy;

                    if (x < 0 || y < 0 || x > 0xFE || y > 0xFE) {
                        continue;
                    }

                    var id = (uint)(x << 8 | y);
                    var tiers = new List<int>();
                    TiersFor(ring, start, out var tier, tiers);

                    if (tiers.Count > 0) {
                        baked[id] = tiers;
                    }

                    drawn[id] = tier;

                    // from the game's last ring, drawn there too: the game draws it coarsely, and land only shows where
                    // the game drew nothing
                    if (ring < start) {
                        if (ring == start - 1 && baked.ContainsKey(id)) {
                            margin.Add(id);
                            wanted.Add((id, ring));
                        }

                        continue;
                    }

                    wanted.Add(((uint)(x << 8 | y), ring));

                    // inside the game's landscape: its coarse rings, which the hook leaves to these (FarLand.Takeover.cs)
                    if (nearStart > 0 && ring <= landscape) {
                        near.Add((uint)(x << 8 | y));
                    }
                }
            }

            wanted.Sort((a, b) => a.Ring.CompareTo(b.Ring));
            var keep = new HashSet<uint>();
            toRead.Clear();

            foreach (var (block, _) in wanted) {
                keep.Add(block);

                if (!blocks.ContainsKey(block)) {
                    toRead.Enqueue(block);
                }
            }

            foreach (var block in new List<uint>(blocks.Keys)) {
                if (!keep.Contains(block)) {
                    blocks.Remove(block);
                }
            }

            wantedHome = home;
            wantedNear = near;
            drawnTier = drawn;
            bakedTiers = baked;
            marginOnly = margin;
            // the bakes are queued again for the new set once it's read: a set wanted mid-bake left its new blocks black
            ForgetBakes();
            bakesQueued = false;
            toBuild = true;
            PluginCore.LogFile($"[far-land] rings {landscape} to {landscape + rings} round {home:X4}: {keep.Count} blocks, "
                + $"{toRead.Count} to read");
        }

        private void ReadSome(DatDatabase cells) {
            if (toRead.Count == 0) {
                return;
            }

            var clock = Stopwatch.StartNew();

            var thisFrame = 0;

            while (toRead.Count > 0 && clock.Elapsed.TotalMilliseconds < ReadBudgetMs) {
                var block = toRead.Dequeue();
                blocks[block] = Read(cells, block) ?? new LandBlock();
                thisFrame++;
            }

            readTimer.Add(thisFrame, clock.Elapsed);

            if (toRead.Count == 0 && readTimer.Finish("[far-land] blocks read:") is string done) {
                PluginCore.LogFile(done);
            }
        }

        /// <summary>The reading's totals, logged once its queue is empty, to see what loading waits on.</summary>
        private readonly WorkTimer readTimer = new WorkTimer();

        /// <summary>A block's heights and terrain, read from its file without the dat keeping it.</summary>
        private static LandBlock Read(DatDatabase cells, uint block) {
            var reader = cells.GetReaderForFile(block << 16 | 0xFFFF);

            if (reader == null) {
                return null;
            }

            var land = new CellLandblock();

            using (var input = new MemoryStream(reader.Buffer))
            using (var binary = new BinaryReader(input)) {
                land.Unpack(binary);
            }

            if (land.Height.Count < Dim * Dim || land.Terrain.Count < Dim * Dim) {
                return null;
            }

            return new LandBlock { Heights = land.Height.ToArray(), Terrain = land.Terrain.ToArray() };
        }

        /// <summary>
        /// The terrains' textures and the alpha maps read on the game's thread (the dat readers aren't safe on another),
        /// then decoded and shrunk for every tier on a worker, terrainReady once they're done: all on the game's thread,
        /// the first frame at login took 594 ms (November 2026).
        /// </summary>
        private void StartTerrain(DatDatabase portal) {
            var colourSources = ReadColourSources(portal);
            var shapeSources = ReadShapeSources(portal);

            System.Threading.Tasks.Task.Run(() => {
                var clock = System.Diagnostics.Stopwatch.StartNew();

                try {
                    FindColours(colourSources);
                    LoadShapes(shapeSources);
                    PluginCore.LogFile($"[far-land] terrain pictures and alpha maps made in "
                        + $"{clock.Elapsed.TotalMilliseconds:0} ms on a worker");
                }
                catch (Exception ex) {
                    PluginCore.LogFile("[far-land] terrain pictures couldn't be made: " + ex);
                }
                finally {
                    terrainReady = true;
                }
            });
        }

        /// <summary>A terrain's texture as read from the dats, for the worker; Error if it couldn't be.</summary>
        private sealed class ColourSource {
            public ACE.DatLoader.Entity.TMTerrainDesc Desc;
            public int Type;
            public uint TextureId;
            public DatTexture Image;
            public string Error;
        }

        private List<ColourSource> ReadColourSources(DatDatabase portal) {
            var sources = new List<ColourSource>();
            var seen = new HashSet<int>();

            foreach (var desc in region.TerrainInfo.LandSurfaces.TexMerge.TerrainDesc) {
                var type = (int)desc.TerrainType;

                if (!seen.Add(type)) {
                    continue;
                }

                var source = new ColourSource { Desc = desc, Type = type };

                try {
                    var id = desc.TerrainTex.TexGID;
                    source.TextureId = id >> 24 == 6 ? id : portal.ReadFromDat<SurfaceTexture>(id).Textures[0];
                    source.Image = portal.ReadFromDat<DatTexture>(source.TextureId);
                }
                catch (Exception ex) {
                    source.Error = ex.Message;
                }

                sources.Add(source);
            }

            return sources;
        }

        /// <summary>Each terrain's colour: its texture in the region's TexMerge, averaged (as Grass does).</summary>
        private void FindColours(List<ColourSource> sources) {
            foreach (var source in sources) {
                var type = source.Type;
                var desc = source.Desc;

                if (colours.ContainsKey(type)) {
                    continue;
                }

                try {
                    if (source.Error != null) {
                        throw new InvalidOperationException(source.Error);
                    }

                    var id = desc.TerrainTex.TexGID;
                    var textureId = source.TextureId;
                    var image = source.Image;
                    var pixels = TextureDecode.Decode((uint)image.Format, image.SourceData, image.Width, image.Height,
                        null);
                    var average = pixels == null ? 0xFF6A8040u : GrassPicture.Average(pixels);
                    colours[type] = unchecked((int)(average | 0xFF000000u));
                    PluginCore.LogFile($"[far-land] terrain {type:X2}: texture 0x{textureId:X8} "
                        + $"{image.Width}x{image.Height} "
                        + $"averages {average:X8}, tiling {desc.TerrainTex.TexTiling}, from 0x{id:X8}");
                    AddTiles(type, pixels, image.Width, image.Height, colours[type]);
                }
                catch (Exception ex) {
                    colours[type] = unchecked((int)0xFF6A8040);
                    AddTiles(type, null, 0, 0, colours[type]);
                    PluginCore.LogFile($"[far-land] terrain {type:X}'s texture couldn't be read: {ex.Message}");
                }
            }

            if (colours.TryGetValue((int)RoadType, out var road)) {
                roadColour = road;
            }

            SetRoadTiles(roadColour);

            PluginCore.LogFile($"[far-land] {colours.Count} terrain colours, road {roadColour:X8}");
        }

        private const int VerticesABlock = Dim * Dim + 4 * Dim;
        private const int IndicesABlock = ((Dim - 1) * (Dim - 1) * 2 + 4 * (Dim - 1) * 2) * 3;

        // one block's vertices and indices, reused: the buffers are written straight from them (WriteBlock)
        private readonly List<LandVertex> blockVertices = new List<LandVertex>(VerticesABlock);
        private readonly List<int> blockIndices = new List<int>(IndicesABlock);

        /// <summary>
        /// The buffers made and written block by block, straight into them: the first build grew lists of every vertex
        /// and copied them twice, some 30 MB of large-object heap at each landblock crossed or teleport, which a 32-bit
        /// game never gets back in one piece (the largest free piece of its memory fell from 1,418 MB to 983 over a
        /// test run, and a teleport then crashed in the game's own cache; November 2026).
        /// </summary>
        /// <summary>Each edge of a block: its first vertex (x, y) and the step along it.</summary>
        private static readonly (int, int, int, int)[] SkirtEdges = {
            (0, 0, 0, 1), (Dim - 1, 0, 0, 1), (0, 0, 1, 0), (0, Dim - 1, 1, 0),
        };

        /// <summary>
        /// A triangle's three vertices added: each one an array before, about 1.9 million of them a rebuild.
        /// </summary>
        private static void AddTriangle(List<int> tris, int a, int b, int c) {
            tris.Add(a);
            tris.Add(b);
            tris.Add(c);
        }

        /// <summary>A block's 81 vertices, its 128 triangles, and a skirt down each edge.</summary>
        private void AddBlock(List<LandVertex> verts, List<int> tris, uint block, LandBlock land,
            IReadOnlyList<float> table, float offsetX, float offsetY, int tier) {
            var first = verts.Count;
            var slots = TierSlots[tier];
            var blockPixels = TierBlockPixels[tier];
            var cellPixels = CellPixelsOf(tier);
            var slotX = (int)((block >> 8) & (slots - 1));
            var slotY = (int)(block & (slots - 1));
            var id = block << 16;
            float H(int x, int y) => table[land.Heights[x * Dim + y]];

            // heights past the block's edge, for the normals, taken from the edge itself (no neighbour needed)
            float HC(int x, int y) => H(Math.Min(Dim - 1, Math.Max(0, x)), Math.Min(Dim - 1, Math.Max(0, y)));

            for (var x = 0; x < Dim; x++) {
                for (var y = 0; y < Dim; y++) {
                    var word = land.Terrain[x * Dim + y];
                    var colour = (word & 3) != 0 ? roadColour
                        : colours.TryGetValue((word >> 2) & 0x1F, out var c) ? c : unchecked((int)0xFF6A8040);
                    // the normal from the slopes either side: (-dh/dx, 1, -dh/dy) in the device's (east, up, north)
                    var nx = (HC(x - 1, y) - HC(x + 1, y)) / (2 * CellSize);
                    var ny = (HC(x, y - 1) - HC(x, y + 1)) / (2 * CellSize);
                    verts.Add(new LandVertex {
                        X = offsetX + x * CellSize, Y = H(x, y), Z = offsetY + y * CellSize,
                        NX = nx, NY = 1, NZ = ny, Colour = colour,
                        U = (slotX * blockPixels + x * cellPixels) / (float)(slots * blockPixels),
                        V = (slotY * blockPixels + y * cellPixels) / (float)(slots * blockPixels),
                    });
                }
            }

            int V(int x, int y) => first + x * Dim + y;

            for (var x = 0; x < Dim - 1; x++) {
                for (var y = 0; y < Dim - 1; y++) {
                    if (SceneryLayout.SplitNwSe(id, x, y)) {
                        // the diagonal from the north-west corner (x, y+1) to the south-east (x+1, y)
                        AddTriangle(tris, V(x, y), V(x, y + 1), V(x + 1, y));
                        AddTriangle(tris, V(x + 1, y), V(x, y + 1), V(x + 1, y + 1));
                    }
                    else {
                        AddTriangle(tris, V(x, y), V(x + 1, y + 1), V(x + 1, y));
                        AddTriangle(tris, V(x, y), V(x, y + 1), V(x + 1, y + 1));
                    }
                }
            }

            // skirts: each edge's vertices again, lowered, and a strip between
            foreach (var edge in SkirtEdges) {
                var skirt = verts.Count;

                for (var i = 0; i < Dim; i++) {
                    var top = verts[V(edge.Item1 + edge.Item3 * i, edge.Item2 + edge.Item4 * i)];
                    top.Y -= SkirtDepth;
                    verts.Add(top);
                }

                for (var i = 0; i < Dim - 1; i++) {
                    var a = V(edge.Item1 + edge.Item3 * i, edge.Item2 + edge.Item4 * i);
                    var b = V(edge.Item1 + edge.Item3 * (i + 1), edge.Item2 + edge.Item4 * (i + 1));
                    AddTriangle(tris, a, b, skirt + i);
                    AddTriangle(tris, b, skirt + i + 1, skirt + i);
                }
            }
        }

        /// <summary>The far land drawn from inside the game's frame (Ui/WorldPass.cs).</summary>
        private void DrawInWorldPass(Device device) {
            if (disposed || failed || vertices == null || Options.ClampedFarLand == 0) {
                return;
            }

            try {
                HookBackground();

                // drawn already, after the sky and before the world (FarLand.Background.cs)
                if (drawnAsBackground) {
                    drawnAsBackground = false;
                    return;
                }

                if (!OutdoorView.Now) {
                    return;
                }

                Draw(device);
            }
            catch (Exception ex) {
                failed = true;
                PluginCore.LogFile("[far-land] stopped for the session: " + ex);
            }
        }

        private unsafe void Draw(Device device) {
            var view = device.Transform.View;
            var projection = device.Transform.Projection;
            var you = RenderOrigin.Player();

            if (projection.M11 == 0 || RenderOrigin.Swapped(view)) {
                return;
            }

            // the game's camera with the far plane pushed out (left-handed: M33 f/(f-n), M43 -nf/(f-n))
            var far = projection;
            far.M33 = FarPlane / (FarPlane - NearPlane);
            far.M43 = -NearPlane * FarPlane / (FarPlane - NearPlane);

            // built round your landblock, drawn in the camera's
            var origin = RenderOrigin.Landblock;
            var shift = Matrix.Translation(((int)(builtHome >> 8) - (int)(origin >> 8)) * BlockSize, 0,
                ((int)(builtHome & 0xFF) - (int)(origin & 0xFF)) * BlockSize);
            var state = device.RenderState;
            var fog = (On: state.FogEnable, Start: state.FogStart, End: state.FogEnd, Colour: state.FogColor);

            // made and let go each frame: a state block alive at a device reset hangs the game
            using (var saved = new StateBlock(device, StateBlockType.All)) {
                saved.Capture();

                try {
                    state.Lighting = false;
                    state.FogEnable = false;
                    state.AlphaBlendEnable = false;
                    state.AlphaTestEnable = false;
                    state.CullMode = Cull.None;
                    state.ZBufferEnable = true;
                    state.ZBufferFunction = Compare.LessEqual;
                    state.ZBufferWriteEnable = true;

                    // 0: the game's coarse rings, skipped by the hook, with its own camera and real depths
                    // (FarLand.Takeover.cs), so what stands there sorts against them as against the game's ground
                    if (nearActive && nearTriangleCount > 0) {
                        state.StencilEnable = false;
                        DrawRuns(device, shift * view * projection, fog, 0, 0);
                    }

                    var farTriangles = triangleCount - nearTriangleCount;

                    if (farTriangles <= 0) {
                        return;
                    }

                    state.StencilEnable = true;
                    state.StencilMask = StencilBit;
                    state.StencilWriteMask = StencilBit;
                    state.ReferenceStencil = StencilBit;
                    DeviceCalls.UseShaders(device, IntPtr.Zero, IntPtr.Zero);
                    device.SetTexture(0, null);

                    // 1: the stencil bit set where nothing's been drawn (depth still 1), cleared elsewhere
                    state.ZBufferWriteEnable = false;
                    state.ColorWriteEnable = (ColorWriteEnable)0;
                    state.StencilFunction = Compare.Always;
                    state.StencilPass = StencilOperation.Replace;
                    state.StencilZBufferFail = StencilOperation.Zero;
                    state.StencilFail = StencilOperation.Keep;
                    ScreenQuad(device);

                    // 2: the land past the game's there alone, with depths of its own
                    state.ColorWriteEnable = ColorWriteEnable.RedGreenBlueAlpha;
                    state.ZBufferWriteEnable = true;
                    state.StencilFunction = Compare.Equal;
                    state.StencilPass = StencilOperation.Keep;
                    state.StencilZBufferFail = StencilOperation.Keep;
                    DrawRuns(device, shift * view * far, fog, 1, nearTriangleCount);
                    DeviceCalls.UseShaders(device, IntPtr.Zero, IntPtr.Zero);

                    // the trees standing on it, in the same pixels, with the same camera (DrawDistance.Billboards.cs), and
                    // the game's fog on again for them: it was off for the land's shader, which fogs itself, and far
                    // billboards stood out bright against hazy hills (the user, November 2026)
                    state.FogEnable = fog.On;
                    drawDistance.DrawFarLandBillboards(device, far);
                    DeviceCalls.UseShaders(device, IntPtr.Zero, IntPtr.Zero);

                    // 3: depth 1 put back there, for what's drawn after to test against as before
                    state.ColorWriteEnable = (ColorWriteEnable)0;
                    state.ZBufferFunction = Compare.Always;
                    state.StencilPass = StencilOperation.Zero;
                    ScreenQuad(device);
                }
                finally {
                    DeviceCalls.UseShaders(device, IntPtr.Zero, IntPtr.Zero);
                    saved.Apply();
                }
            }
        }

        /// <summary>The near (0) or far (1) runs, a tier each, from the first triangle given.</summary>
        private void DrawRuns(Device device, Matrix m, (bool On, float Start, float End, System.Drawing.Color Colour) fog,
            int run, int first) {
            for (var tier = 0; tier < TierCount; tier++) {
                DrawLand(device, m, fog, first, runTriangles[run, tier], tier);
                first += runTriangles[run, tier];
            }
        }

        /// <summary>A run of the land's triangles, lit, textured and fogged, through the matrix given.</summary>
        private unsafe void DrawLand(Device device, Matrix m,
            (bool On, float Start, float End, System.Drawing.Color Colour) fog,
            int firstTriangle, int triangles, int tier) {
            if (triangles <= 0) {
                return;
            }

            float F(uint at) => *(float*)at;
            device.VertexFormat = Format;
            device.SetStreamSource(0, vertices, 0);
            device.Indices = indices;
            DeviceCalls.UseShaders(device, vertexShader, pixelShader);
            DeviceCalls.VertexConstants(device, 0, new[] {
                m.M11, m.M12, m.M13, m.M14,
                m.M21, m.M22, m.M23, m.M24,
                m.M31, m.M32, m.M33, m.M34,
                m.M41, m.M42, m.M43, m.M44,
                fog.Start, fog.End, fog.On ? 1 : 0, 0,
                F(0x8682B0) * GroundLight, F(0x8682B4) * GroundLight, F(0x8682B8) * GroundLight, 0,
                F(0x8682BC) * GroundLight, F(0x8682C0) * GroundLight, F(0x8682C4) * GroundLight, 0,
                // toward the sun: x east, y north, z up, into the device's (east, up, north)
                F(0x8682C8), F(0x8682D0), F(0x8682CC), 0,
            });
            var picture = atlases[tier];
            var textured = picture != null && atlasReady;
            device.SetTexture(0, textured ? picture : null);
            device.SamplerState[0].MinFilter = TextureFilter.Linear;
            device.SamplerState[0].MagFilter = TextureFilter.Linear;
            device.SamplerState[0].MipFilter = TextureFilter.Linear;
            device.SamplerState[0].AddressU = TextureAddress.Wrap;
            device.SamplerState[0].AddressV = TextureAddress.Wrap;
            DeviceCalls.PixelConstants(device, 0, new[] {
                fog.Colour.R / 255f, fog.Colour.G / 255f, fog.Colour.B / 255f, textured ? 1 : 0,
                Debug ? 1 : 0, 0, 0, 0,
            });
            device.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, vertexCount, firstTriangle * 3, triangles);
        }

        /// <summary>The whole viewport at depth 1.</summary>
        private static void ScreenQuad(Device device) {
            var viewport = device.Viewport;
            float l = viewport.X - 0.5f, t = viewport.Y - 0.5f;
            float r = l + viewport.Width, b = t + viewport.Height;
            device.VertexFormat = QuadFormat;
            device.DrawUserPrimitives(PrimitiveType.TriangleStrip, 2, new[] {
                new QuadVertex { X = l, Y = t, Z = 1, W = 1 },
                new QuadVertex { X = r, Y = t, Z = 1, W = 1 },
                new QuadVertex { X = l, Y = b, Z = 1, W = 1 },
                new QuadVertex { X = r, Y = b, Z = 1, W = 1 },
            });
        }

        private void Clear() {
            AbortBuild();
            vertices?.Dispose();
            vertices = null;
            indices?.Dispose();
            indices = null;
            vertexCount = 0;
            triangleCount = 0;
            builtHome = uint.MaxValue;
            builtNear = new HashSet<uint>();
            nearTriangleCount = 0;
            Array.Clear(runTriangles, 0, runTriangles.Length);
        }

        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            WorldPass.Remove(inWorldPass);

            try {
                Clear();
                blocks.Clear();
                DeviceCalls.Release(vertexShader);
                DeviceCalls.Release(pixelShader);
                DisposeAtlas();
                Unhook();
                UnhookBackground();
            }
            catch (Exception ex) {
                PluginCore.LogFile("[far-land] " + ex);
            }
        }
    }
}
