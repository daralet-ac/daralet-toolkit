using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The game's low-detail rings taken over (DrawDistanceOptions.FarLandNear; the user's call, November 2026: the
    /// game draws its landscape past the full-detail rings from every 2nd, 4th or 8th height point, so a river's shape
    /// blurs there, and Ground detail, which makes more of them full detail, costs about 1.8 MB a block). The game draws
    /// a landblock's ground with RenderDeviceD3D::DrawBlock (0x5A28D0, thiscall, the CLandBlock as its one argument,
    /// ret 4: its cells' DrawLandCell, vtable +0x54, then each cell's sort cell, +0x58), called through the device's
    /// vtable, the slot at 0x7E6638. That slot is pointed here: a block in less than full detail (side_cell_count, +0x48,
    /// under 8; its id at +0x28) that the far land has built is skipped, so its pixels stay empty, and the far land draws
    /// it in full detail with the game's own camera and real depths (DrawNear), so the trees, billboards and buildings
    /// standing there sort against it as against the game's ground. Blocks it hasn't built yet the game draws as ever.
    /// The coarse blocks seen each frame say where the full-detail rings end (nearestCoarseRing), which is where the far
    /// land then starts. What else a skipped block's cells would draw (anything standing in them: a creature or player
    /// half a kilometer off) isn't drawn. Put back when the setting goes off and at shutdown. `[far-land]` lines in
    /// log.txt. Untested in the game when written.
    /// </summary>
    internal sealed partial class FarLand {
        private const uint DrawBlockSlot = 0x7E6638;
        private const uint DrawBlockEntry = 0x5A28D0;
        private const uint DrawLandCellSlot = 0x7E663C;
        private const uint DrawLandCellEntry = 0x5A0230;
        private const int SideCellCount = 0x48;
        private const int BlockId = 0x28;

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate void DrawBlockCall(IntPtr device, IntPtr block);

        [DllImport("kernel32.dll")]
        private static extern bool VirtualProtect(IntPtr address, UIntPtr size, uint protect, out uint old);

        // kept while the slot points at it, so it's never collected while the game can call it
        private DrawBlockCall drawBlockHook;
        private DrawBlockCall gameDrawBlock;
        private DrawBlockCall drawLandCellHook;
        private DrawBlockCall gameDrawLandCell;

        /// <summary>Whether the block being drawn is the far land's: its cells' ground is left out.</summary>
        private bool skipCells;

        // the device's own calls for the scissor test (IDirect3DDevice9: SetRenderState 57, GetRenderState 58,
        // SetScissorRect 75, GetScissorRect 76), made once and kept (the vtable is the same for every device)
        private const int ScissorTestEnable = 174;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int SetRenderStateFn(IntPtr device, int state, int value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate int GetRenderStateFn(IntPtr device, int state, out int value);

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private unsafe delegate int ScissorFn(IntPtr device, int* rect);

        private static SetRenderStateFn SetRenderStateCall;
        private static GetRenderStateFn GetRenderStateCall;
        private static ScissorFn SetScissorCall;
        private static ScissorFn GetScissorCall;

        /// <summary>The game's Direct3D device (its COM pointer), noted each frame for the cell hook.</summary>
        private IntPtr comDevice;
        private bool hooked;
        private bool hookFailed;

        /// <summary>The near blocks built (0xXXYY): only these are skipped, so the game never leaves a hole.</summary>
        private HashSet<uint> builtNear = new HashSet<uint>();
        private HashSet<uint> wantedNear = new HashSet<uint>();
        private int nearTriangleCount;

        /// <summary>Whether the near blocks are being drawn this frame: outdoors, built, the hook in.</summary>
        private bool nearActive;

        /// <summary>The ring of the nearest block the game drew (or would have) in less than full detail lately.</summary>
        private int nearestCoarseRing;
        private int coarseThisSecond = int.MaxValue;
        private int coarseSeenAt = Environment.TickCount;
        private int skippedThisSecond;
        private int skippedLogged = -1;
        private uint hookHome;

        private static unsafe IntPtr DeviceCom(Microsoft.DirectX.Direct3D.Device device) {
            return (IntPtr)device.UnmanagedComPointer;
        }

        private bool WantsNear => Options.ClampedFarLand > 0 && Options.FarLandNear;

        /// <summary>The hook put in or taken out to follow the setting, once a frame.</summary>
        private unsafe void FollowTakeover() {
            if (WantsNear == hooked || hookFailed) {
                return;
            }

            try {
                var slot = (IntPtr)DrawBlockSlot;

                if (WantsNear) {
                    if (*(uint*)slot != DrawBlockEntry || *(uint*)DrawLandCellSlot != DrawLandCellEntry) {
                        hookFailed = true;
                        PluginCore.LogFile($"[far-land] the device's slots hold {*(uint*)slot:X8} and "
                            + $"{*(uint*)DrawLandCellSlot:X8}, not {DrawBlockEntry:X8} and {DrawLandCellEntry:X8}: "
                            + "the game's own rings are left to it");
                        return;
                    }

                    gameDrawBlock = Marshal.GetDelegateForFunctionPointer<DrawBlockCall>((IntPtr)DrawBlockEntry);
                    drawBlockHook = DrawBlockHook;
                    gameDrawLandCell = Marshal.GetDelegateForFunctionPointer<DrawBlockCall>((IntPtr)DrawLandCellEntry);
                    drawLandCellHook = DrawLandCellHook;
                    Write((IntPtr)DrawLandCellSlot, (uint)Marshal.GetFunctionPointerForDelegate(drawLandCellHook));
                    Write(slot, (uint)Marshal.GetFunctionPointerForDelegate(drawBlockHook));
                    hooked = true;
                    PluginCore.LogFile("[far-land] taking over the game's low-detail rings");
                }
                else {
                    Unhook();
                }
            }
            catch (Exception ex) {
                hookFailed = true;
                PluginCore.LogFile("[far-land] " + ex);
            }
        }

        private void Unhook() {
            if (!hooked) {
                return;
            }

            Write((IntPtr)DrawBlockSlot, DrawBlockEntry);
            Write((IntPtr)DrawLandCellSlot, DrawLandCellEntry);
            hooked = false;
            skipCells = false;
            nearestCoarseRing = 0;
            PluginCore.LogFile("[far-land] the game's low-detail rings are its own again");
        }

        private static unsafe void Write(IntPtr at, uint value) {
            VirtualProtect(at, (UIntPtr)4, 0x04, out var old);
            *(uint*)at = value;
            VirtualProtect(at, (UIntPtr)4, old, out _);
        }

        /// <summary>
        /// Every block drawn as the game would; one the far land has built has its cells' ground left out
        /// (DrawLandCellHook), the rest of its draw (its cells' objects, the game's bookkeeping) going ahead.
        /// </summary>
        private unsafe void DrawBlockHook(IntPtr device, IntPtr block) {
            try {
                if (block != IntPtr.Zero && *(int*)((byte*)block + SideCellCount) < 8) {
                    var id = *(uint*)((byte*)block + BlockId) >> 16;
                    var ring = Math.Max(Math.Abs((int)(id >> 8) - (int)(hookHome >> 8)),
                        Math.Abs((int)(id & 0xFF) - (int)(hookHome & 0xFF)));
                    coarseThisSecond = Math.Min(coarseThisSecond, ring);

                    if (nearActive && builtNear.Contains(id)) {
                        skippedThisSecond++;
                        skipCells = true;
                    }
                }
            }
            catch (Exception) {
                // nothing may escape into the game's frame: drawn as the game would
                skipCells = false;
            }

            try {
                gameDrawBlock(device, block);
            }
            finally {
                skipCells = false;
            }
        }

        /// <summary>
        /// A cell's ground (RenderDeviceD3D::DrawLandCell): drawn by the game as ever, but while its block is the far
        /// land's, through an empty scissor rectangle, so nothing of it reaches the screen. Skipping the call, as the first
        /// two builds did (the whole block, then its cells' ground), crashed the game's loading within a few teleports
        /// every time, in its cache (0x58712F, 0x526A66, 0x4189A1) or building a block's ground (0x539153), while the same
        /// test run without the takeover had none in 21 teleports (November 2026): the draw does work the game relies on
        /// later. The device's own scissor test and rectangle are put back after.
        /// </summary>
        private unsafe void DrawLandCellHook(IntPtr device, IntPtr cell) {
            if (!skipCells || comDevice == IntPtr.Zero) {
                gameDrawLandCell(device, cell);
                return;
            }

            var vtable = *(IntPtr**)comDevice;
            var getState = GetRenderStateCall ?? (GetRenderStateCall =
                Marshal.GetDelegateForFunctionPointer<GetRenderStateFn>(vtable[58]));
            var setState = SetRenderStateCall ?? (SetRenderStateCall =
                Marshal.GetDelegateForFunctionPointer<SetRenderStateFn>(vtable[57]));
            var getRect = GetScissorCall ?? (GetScissorCall =
                Marshal.GetDelegateForFunctionPointer<ScissorFn>(vtable[76]));
            var setRect = SetScissorCall ?? (SetScissorCall =
                Marshal.GetDelegateForFunctionPointer<ScissorFn>(vtable[75]));

            getState(comDevice, ScissorTestEnable, out var wasOn);
            var oldRect = stackalloc int[4];
            getRect(comDevice, oldRect);
            var empty = stackalloc int[4];
            setRect(comDevice, empty);
            setState(comDevice, ScissorTestEnable, 1);

            try {
                gameDrawLandCell(device, cell);
            }
            finally {
                setState(comDevice, ScissorTestEnable, wasOn);
                setRect(comDevice, oldRect);
            }
        }

        /// <summary>Once a frame: where the coarse rings start, from what the hook saw over the last second.</summary>
        private void NoteCoarseRings(uint home) {
            hookHome = home;

            if (unchecked(Environment.TickCount - coarseSeenAt) < 1000) {
                return;
            }

            coarseSeenAt = Environment.TickCount;

            if (coarseThisSecond != int.MaxValue) {
                nearestCoarseRing = coarseThisSecond;
            }

            if (skippedThisSecond > 0 && skippedLogged < 0 || skippedLogged == 0 && skippedThisSecond > 0) {
                PluginCore.LogFile($"[far-land] the game's coarse rings start at {nearestCoarseRing}; "
                    + $"{skippedThisSecond} block draws skipped in a second");
            }

            skippedLogged = skippedThisSecond;
            coarseThisSecond = int.MaxValue;
            skippedThisSecond = 0;
        }
    }
}
