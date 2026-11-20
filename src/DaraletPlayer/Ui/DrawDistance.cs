using System;
using System.Runtime.InteropServices;
using DaraletPlayer.Feed;
using Decal.Adapter;
using Microsoft.DirectX.Direct3D;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Draw distance past the game's own settings (DrawDistanceOptions, the Options window's Config page; the user's
    /// call, November 2026). Read from acclient.exe (the end-of-retail build every client here is):
    /// <para>
    /// Scenery (trees, bushes, rocks) is only made for a landblock drawn in full detail: CLandBlock::init_static_objs
    /// (0x531780) returns unless the block's side_cell_count (+0x48) is 8. Which blocks are full detail is
    /// LScape::get_block_orient (0x5059F0): by the larger of the block's x and y offsets from yours in landblocks, 1 or
    /// less is full detail (a step of 1), 2 or less a step of 2, 4 or less 4, and the rest 8, side_cell_count being 8 over
    /// the step. So the game only ever has scenery in the 3x3 landblocks round you, whatever its Landscape Draw Distance.
    /// The three ring sizes are immediates in that function (the cmp and the mov after it, which the ring's edge
    /// directions are worked out from), patched here to R, R+1 and R+3, and put back when this is off or the plugin stops.
    /// </para>
    /// <para>
    /// The landscape's radius (LScape::mid_radius, how many landblocks out are loaded at all) is the game's Landscape Draw
    /// Distance, the int at 0x81FFB4 (its choices set 3, 5, 8, 11 or 15), applied with SmartBox::set_mid_radius
    /// (0x4531C0, thiscall on the SmartBox at [0x83DA58]), which releases every block and loads them again; any radius of
    /// 1 or more is taken (LScape::SetMidRadius, 0x505660). The game sets it again from 0x81FFB4 on some changes of
    /// region, so it's looked at once a second and set again. The patched rings need the same reload to take effect.
    /// </para>
    /// Before writing a byte, each patched instruction is checked to be what's expected; a client that differs is left
    /// alone. `[draw-distance]` lines in log.txt. Untested in the game when written.
    /// </summary>
    internal sealed partial class DrawDistance : IDisposable {
        /// <summary>Kept so the same one can be taken out of WorldPass.</summary>
        private readonly Action<Device> billboardsInWorldPass;

        private const uint SmartBoxPointer = 0x83DA58;
        private const uint SetMidRadiusAddress = 0x4531C0;
        private const int SmartBoxLScape = 0xB0;
        private const uint GameLandscapeRadius = 0x81FFB4;

        /// <summary>The landscape radius held through portal space (the game's least preset is 3).</summary>
        private const int PortalLandscape = 5;
        private const uint PageExecuteReadWrite = 0x40;

        /// <summary>Each ring's cmp and mov in get_block_orient: where, the opcode bytes, the value's size.</summary>
        private static readonly (uint At, byte[] Opcode, int Size, int Ring)[] Sites = {
            (0x505A16, new byte[] { 0x83, 0xF8 }, 1, 0), (0x505A25, new byte[] { 0xB8 }, 4, 0),
            (0x505A41, new byte[] { 0x83, 0xF8 }, 1, 1), (0x505A50, new byte[] { 0xB8 }, 4, 1),
            (0x505A57, new byte[] { 0x83, 0xF8 }, 1, 2), (0x505A66, new byte[] { 0xB8 }, 4, 2),
        };

        /// <summary>The game's ring sizes: full detail, a step of 2, a step of 4.</summary>
        private static readonly int[] GameRings = { 1, 2, 4 };

        private readonly Settings settings;
        private int patchedRadius = DrawDistanceOptions.GameSceneryRadius;
        private DateTime nextLook;
        private bool broken;
        private bool disposed;

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate int SetMidRadiusCall(IntPtr smartBox, int radius);

        public DrawDistance(Settings settings) {
            this.settings = settings;
            CoreManager.Current.RenderFrame += Core_RenderFrame;
            ListenForPortals();
            billboardsInWorldPass = DrawBillboardsInWorldPass;
            WorldPass.Add(billboardsInWorldPass);
        }

        private DrawDistanceOptions Options => settings.Data.DrawDistance;

        /// <summary>Why it can't be used on this client, or null.</summary>
        /// <summary>Why it can't be used at all on this client, or null.</summary>
        /// <summary>
        /// Whether the draw distance has reached what's set, as of the last look (a second at most): no step up still to
        /// come. For a test run's settle (Ui/TestRunner.cs).
        /// </summary>
        public bool AtTarget { get; private set; }

        /// <summary>The scenery and landscape radius loaded as of the last look, for a test run's results.</summary>
        public string Loaded => $"scenery {patchedRadius} at {appliedDensity}%";

        /// <summary>The game's own Landscape Draw Distance.</summary>
        public int GameLandscape => GameOwnLandscape;

        /// <summary>The landscape radius these settings load: theirs, else the game's Landscape Draw Distance.</summary>
        public int LandscapeFor(DrawDistanceOptions options) {
            return options.ClampedLandscapeRadius > 0 ? options.ClampedLandscapeRadius : GameOwnLandscape;
        }

        public string Problem => broken ? "This client isn't the one the draw distance was worked out for: it's left alone."
            : null;

        /// <summary>Why the far settings aren't all loaded for want of memory, or null.</summary>
        public string MemoryWarning => broken ? null : MemoryProblem;

        /// <summary>
        /// Looks again now, rather than within a second: a setting just changed. After running out of memory, that's
        /// trying again (most likely with less), so the cap is lifted; MayLoad still keeps out anything as costly.
        /// </summary>
        public void Soon() {
            nextLook = DateTime.MinValue;
            memoryCapped = false;
            memoryWarned = false;
        }

        private void Core_RenderFrame(object sender, EventArgs e) {
            try {
                if (disposed) {
                    return;
                }

                NoteFrame();
                UpdateProgressHud();
                PrepareBeforeLogin();
                QuietNow = Arriving;

                if (CoreManager.Current.CharacterFilter.LoginStatus == 3) {
                    BillboardFrame();
                }

                if (DateTime.UtcNow < nextLook || CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                    return;
                }

                // four times a second while stepping up, so a step follows the last as soon as it's settled
                nextLook = DateTime.UtcNow.AddSeconds(AtTarget ? 1 : 0.25);
                Apply();
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.LogFile("[draw-distance] " + ex);
            }
        }

        private unsafe void Apply() {
            var options = Options;
            var on = options.On && !broken;

            if (!on) {
                RestoreGameLandscape();
            }

            var reload = false;
            var ringsChanged = false;
            var gameLandscape = GameOwnLandscape;
            var wantedLandscape = options.ClampedLandscapeRadius > 0 ? options.ClampedLandscapeRadius : gameLandscape;
            var wanted = Cost(options.ClampedSceneryRadius, options.ClampedSceneryDensity, options.ClampedGroundRadius,
                wantedLandscape);
            MeasureLoad();

            if (loadedCost <= 0) {
                // at login what's loaded is the game's own: the first far settings are measured against it
                loadedCost = Cost(DrawDistanceOptions.GameSceneryRadius, 100, DrawDistanceOptions.GameSceneryRadius,
                    gameLandscape);
            }

            var costBefore = loadedCost;
            var far = on && !LowOnMemory(on, wanted);

            if (far && !MayLoad(wanted)) {
                // too little memory to draw further: what's loaded stays as it is, and the rest still applies; as far
                // as it'll go, so a test run's settle doesn't wait for what won't come
                AtTarget = true;
                NoteProgress(0, 0, 0, 0, true);
                ApplyFog(options.ClampedFog);

                FollowVideoMemory();
                MaybeFlushCache();

                // the billboards still follow you: after a teleport with memory short, they never came back
                var box = *(IntPtr*)SmartBoxPointer;
                var held = box == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)((byte*)box + SmartBoxLScape);
                UpdateBillboards(options.FarScenery && !inPortal && held != IntPtr.Zero,
                    held != IntPtr.Zero ? *(int*)held : 0);
                return;
            }

            if (!far) {
                raiseRefused = null;
            }

            var smartBox = *(IntPtr*)SmartBoxPointer;
            var lscape = smartBox == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)((byte*)smartBox + SmartBoxLScape);
            var aiming = far && !inPortal;
            var wantedDensity = far ? options.ClampedSceneryDensity : 100;

            // a density change remakes every block's scenery, which only a whole reload does, and a whole reload with
            // far scenery wanted overlaps old and new big, which crashed the game: so the reload is done with the
            // scenery at the game's (only the nine near blocks' trees), and then it steps up again in place (the user's
            // call, November 2026)
            var densityChanging = wantedDensity != appliedDensity;
            // the full-detail rings go out to Ground detail; the game's trees stop at Scenery (DrawDistance.Ground.cs)
            treeRadius = aiming ? options.ClampedSceneryRadius : DrawDistanceOptions.MaxSceneryRadius;
            var targetScenery = aiming && !densityChanging ? options.ClampedGroundRadius
                : DrawDistanceOptions.GameSceneryRadius;
            // in portal space, well under the game's own: a teleport loads a whole new landscape while the old one is still
            // held, and with Landscape 18 the two crashed or froze the game every time, even with 1.1 GB free (November
            // 2026). Small, the old place is mostly let go before the new one loads, and the steps bring it back after
            var targetLandscape = aiming ? wantedLandscape
                : far && inPortal ? Math.Min(gameLandscape, PortalLandscape) : gameLandscape;
            var loadedLandscape = lscape != IntPtr.Zero ? *(int*)lscape : targetLandscape;

            // the most the landscape wanted allows, so stepping up aims no higher
            targetScenery = DrawDistanceRamp.SceneryFor(targetScenery, targetLandscape);
            var (scenery, landscape) = NextStep(patchedRadius, targetScenery, loadedLandscape, targetLandscape);

            // the landscape's outer rings never in full detail, as the game has it: past its edge a full-detail block
            // has no neighbour to fit its edges to (DrawDistanceRamp.SceneryInsideLandscape); held before the landscape
            // loads, in this same pass
            scenery = DrawDistanceRamp.SceneryFor(scenery, landscape);

            // never step up in place onto blocks still holding objects they shouldn't (CountStaleBlocks)
            if (scenery > patchedRadius && lscape != IntPtr.Zero && !broken && StaleBlocksStopStep(lscape)) {
                scenery = patchedRadius;
                reload |= staleReloadNow;
                staleReloadNow = false;
            }

            // there once this pass is done, with nothing still to step up to and no density change waiting
            AtTarget = scenery == targetScenery && landscape == targetLandscape && !densityChanging;

            // a load only once the last has settled (LoadSettled): every black screen came from two close together
            var loads = scenery != patchedRadius || densityChanging || reload
                || lscape != IntPtr.Zero && *(int*)lscape != landscape;

            if (loads && !broken && !LoadSettled()) {
                AtTarget = false;
                return;
            }

            NoteProgress(patchedRadius, loadedLandscape, targetScenery, targetLandscape, AtTarget);

            if (scenery != patchedRadius && !broken) {
                if (!PatchRings(scenery)) {
                    return;
                }

                PluginCore.LogFile($"[draw-distance] scenery radius {patchedRadius} -> {scenery}"
                    + $" ({FreeMegabytes()} MB free)");

                // up, the new rings are made in place (a whole reload overlaps old and new, which crashed the game);
                // down, the whole landscape is loaded again, old big and new small: brought down in place and then
                // reloaded for a density change, the game drew next to nothing (120 fps, a black screen), made no
                // trees for the blocks stepped up again, and crashed in make_plane (0x539153), November 2026
                if (scenery > patchedRadius) {
                    ringsChanged = true;
                }
                else {
                    reload = true;
                }

                patchedRadius = scenery;
            }

            // a density change comes with the scenery going down to the game's (targetScenery), in the same reload
            if (ApplyDensity(wantedDensity)) {
                reload = true;

                // the scenery steps up again from here, after the reload's had its time
                nextStepAt = DateTime.UtcNow.AddSeconds(DrawDistanceRamp.StepSeconds);
            }

            KeepOnRoadHook(appliedDensity < 100 || aiming && options.ClampedGroundRadius > options.ClampedSceneryRadius);

            // what's loaded now, a step on the way or all of it, for the memory estimate and the way back
            var cost = Cost(Math.Min(scenery, treeRadius), appliedDensity, scenery, landscape);

            if (cost < costBefore - 0.5) {
                cacheFlushWanted = "the draw distance lowered";
            }

            if (aiming && scenery == targetScenery && landscape == targetLandscape) {
                NoteLoaded(options.ClampedSceneryRadius, options.ClampedSceneryDensity, options.ClampedLandscapeRadius,
                    cost);
            }
            else {
                loadedCost = cost;
            }

            if (lscape != IntPtr.Zero && landscape >= 1 && (reload || *(int*)lscape != landscape)) {
                PluginCore.LogFile($"[draw-distance] landscape radius {*(int*)lscape} -> {landscape}"
                    + $" ({FreeMegabytes()} MB free)");
                LoadStarting(costBefore);
                var setMidRadius = Marshal.GetDelegateForFunctionPointer<SetMidRadiusCall>((IntPtr)SetMidRadiusAddress);
                setMidRadius(smartBox, landscape);
            }
            else if (lscape != IntPtr.Zero && ringsChanged) {
                // only the scenery's rings: the blocks whose detail changed are remade where they are, the rest left
                LoadStarting(costBefore);
                RefreshBlocks(lscape);
            }
            else if (lscape != IntPtr.Zero && !broken && !inPortal) {
                // nothing loading this pass: blocks come into the trees' rings as you walk get the game's trees
                GrowTrees(lscape);
            }

            FollowVideoMemory();
            MaybeFlushCache();
            ApplyFog(broken ? 100 : options.ClampedFog);
            UpdateBillboards(on && options.FarScenery && !inPortal && lscape != IntPtr.Zero,
                lscape != IntPtr.Zero ? *(int*)lscape : 0);
        }

        /// <summary>Sets get_block_orient's rings for a full-detail radius; false if the code isn't as expected.</summary>
        private unsafe bool PatchRings(int radius) {
            var rings = radius == DrawDistanceOptions.GameSceneryRadius ? GameRings
                : new[] { radius, radius + 1, radius + 3 };

            foreach (var site in Sites) {
                var code = (byte*)site.At;

                for (var i = 0; i < site.Opcode.Length; i++) {
                    if (code[i] != site.Opcode[i]) {
                        broken = true;
                        PluginCore.LogFile($"[draw-distance] 0x{site.At:X} isn't the code expected: left alone");
                        return false;
                    }
                }

                // the value now has to be one this sets: the game's or one of ours, never anything else
                var value = site.Size == 1 ? code[site.Opcode.Length] : *(int*)(code + site.Opcode.Length);

                if (value < 1 || value > DrawDistanceOptions.MaxSceneryRadius + 3) {
                    broken = true;
                    PluginCore.LogFile($"[draw-distance] 0x{site.At:X} holds {value}, not a ring size: left alone");
                    return false;
                }
            }

            foreach (var site in Sites) {
                var target = (IntPtr)(site.At + (uint)site.Opcode.Length);

                if (!VirtualProtect(target, (UIntPtr)site.Size, PageExecuteReadWrite, out var old)) {
                    broken = true;
                    PluginCore.LogFile($"[draw-distance] couldn't unprotect 0x{site.At:X}: {Marshal.GetLastWin32Error()}");
                    return false;
                }

                if (site.Size == 1) {
                    *(byte*)target = (byte)rings[site.Ring];
                }
                else {
                    *(int*)target = rings[site.Ring];
                }

                VirtualProtect(target, (UIntPtr)site.Size, old, out _);
            }

            FlushInstructionCache(GetCurrentProcess(), (IntPtr)Sites[0].At, (UIntPtr)0x60);
            return true;
        }

        public void Dispose() {
            if (disposed) {
                return;
            }

            disposed = true;
            CoreManager.Current.RenderFrame -= Core_RenderFrame;
            WorldPass.Remove(billboardsInWorldPass);
            DisposeProgress();
            DisposeVideoMemory();
            StopListeningForPortals();
            RestoreGameLandscape();

            // the game's own back, whatever the reason this is going: a hot reload, the plugin turned off. The landscape is
            // only loaded again if the rings or its radius were this class's
            try {
                var reload = false;

                if (!broken && patchedRadius != DrawDistanceOptions.GameSceneryRadius
                    && PatchRings(DrawDistanceOptions.GameSceneryRadius)) {
                    patchedRadius = DrawDistanceOptions.GameSceneryRadius;
                    reload = true;
                }

                if (appliedDensity < 100) {
                    appliedDensity = 100;
                    reload = true;
                }

                DisposeBillboards();
                UnhookOnRoad();
                UnhookFog();
                ReloadAtGameRadius(reload);
            }
            catch (Exception ex) {
                PluginCore.LogFile("[draw-distance] " + ex);
            }
        }

        /// <summary>The landscape loaded again at the game's radius, if it isn't at it or the rings changed.</summary>
        private unsafe void ReloadAtGameRadius(bool ringsChanged) {
            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                return;
            }

            var smartBox = *(IntPtr*)SmartBoxPointer;
            var lscape = smartBox == IntPtr.Zero ? IntPtr.Zero : *(IntPtr*)((byte*)smartBox + SmartBoxLScape);
            var radius = GameOwnLandscape;

            if (lscape == IntPtr.Zero || radius < 1 || (!ringsChanged && *(int*)lscape == radius)) {
                return;
            }

            Marshal.GetDelegateForFunctionPointer<SetMidRadiusCall>((IntPtr)SetMidRadiusAddress)(smartBox, radius);
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint newProtect, out uint oldProtect);

        [DllImport("kernel32.dll")]
        private static extern bool FlushInstructionCache(IntPtr process, IntPtr address, UIntPtr size);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
    }
}
