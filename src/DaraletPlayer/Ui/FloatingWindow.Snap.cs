using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Snapping (SettingsData.SnapWindows, the Config tab's Window snapping): a window being dragged snaps to the screen's
    /// edges, to the edges of the plugin's other windows showing, and, with the grid on, to the grid (WindowSnap). Shift
    /// held places it freely. ImGui moves an unlocked window itself, from its title bar, so it's taken to be dragged while
    /// the button is down, it's focused and it has moved since the last frame without its size changing (a resize from
    /// its top or left moves it too), and its snapped place is put on it there and then (SetWindowPos), before it's
    /// saved; ImGui works the place out from the mouse again every frame, so nothing builds up. A locked one's Ctrl drag
    /// (CtrlDrag) snaps the same way. While one is dragged the grid is drawn faintly behind everything.
    /// </summary>
    internal partial class FloatingWindow {
        /// <summary>Every window made and not yet disposed, for the others' edges.</summary>
        private static readonly List<FloatingWindow> Showing = new List<FloatingWindow>();

        // packed by hand: ImGui.GetColorU32 crashes before ImGui has a context
        private static readonly uint GridColor = PackedColor.Pack(255, 255, 255, 26);
        private static readonly uint MiddleColor = PackedColor.Pack(255, 255, 255, 80);
        private static readonly uint GuideColorFaint = PackedColor.Pack(255, 255, 255, 55);
        private static readonly uint GuideLitColor = PackedColor.Pack(255, 205, 90, 235);

        /// <summary>The frame the grid was last drawn, so two windows dragged at once don't draw it twice.</summary>
        private static int gridFrame = -1;

        /// <summary>Where the window was drawn, and when (ImGui's frame), for the others to snap to.</summary>
        private Vector2 boxMin;
        private Vector2 boxMax;
        private int boxFrame = -1;

        /// <summary>A press begun on the window's edge, held (TrackResizePress).</summary>
        private bool resizePress;

        /// <summary>Last frame's place and size, to tell a drag by ImGui from a resize.</summary>
        private Vector2 lastPos;
        private Vector2 lastSize;

        /// <summary>SnapSize as cimgui calls it, kept alive with the window (SetSizeConstraints).</summary>
        private SizeCallbackCdecl sizeCallback;
        private IntPtr sizeCallbackPointer;

        /// <summary>
        /// ImGui's size callback as cimgui calls it, cdecl. ImGui.NET's own ImGuiSizeCallback says no calling convention, so
        /// on x86 .NET makes it stdcall: the callback took its argument off the stack and cimgui took it off again, and
        /// the game died in igBegin (an AccessViolationException) the first frame a window drew with it.
        /// </summary>
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private unsafe delegate void SizeCallbackCdecl(ImGuiSizeCallbackData* data);

        [DllImport("cimgui", CallingConvention = CallingConvention.Cdecl)]
        private static extern void igSetNextWindowSizeConstraints(Vector2 min, Vector2 max, IntPtr callback, IntPtr data);

        /// <summary>
        /// Where the window was drawn, top left and size, this frame or the last; null for one not drawn since (hidden,
        /// closed) or never.
        /// </summary>
        public (Vector2 Position, Vector2 Size)? DrawnBox => boxFrame < 0 || boxFrame < ImGui.GetFrameCount() - 1 ? null
            : ((Vector2, Vector2)?)(boxMin, boxMax - boxMin);

        /// <summary>
        /// Notes whether a press began on the window's edge, where ImGui takes hold of it to resize it (4 pixels outside
        /// it to a few inside, more at the corners), kept till the button's let go: SnapSize only snaps such a resize.
        /// </summary>
        private void TrackResizePress() {
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                resizePress = false;
                return;
            }

            if (!ImGui.IsMouseClicked(ImGuiMouseButton.Left) || !defaultSize.HasValue || boxFrame < 0) {
                return;
            }

            var mouse = ImGui.GetMousePos();
            var outside = 4f;
            var inside = Math.Max(6f, ImGui.GetFontSize() * 0.5f);
            var corner = ImGui.GetFontSize() * 1.35f;
            var inBox = mouse.X >= boxMin.X - outside && mouse.X <= boxMax.X + outside
                && mouse.Y >= boxMin.Y - outside && mouse.Y <= boxMax.Y + outside;
            var nearSide = mouse.X <= boxMin.X + inside || mouse.X >= boxMax.X - inside
                || mouse.Y <= boxMin.Y + inside || mouse.Y >= boxMax.Y - inside;
            var nearCorner = (mouse.X <= boxMin.X + corner || mouse.X >= boxMax.X - corner)
                && (mouse.Y <= boxMin.Y + corner || mouse.Y >= boxMax.Y - corner);
            resizePress = inBox && (nearSide || nearCorner);
        }

        /// <summary>Notes where the window is, for the others to snap to.</summary>
        private void NoteBox() {
            boxMin = ImGui.GetWindowPos();
            boxMax = boxMin + ImGui.GetWindowSize();
            boxFrame = ImGui.GetFrameCount();
        }

        /// <summary>Where a window of this size dragged to this place snaps to; the place itself with snapping off.</summary>
        /// <param name="dragged">It's being dragged now: the grid is drawn.</param>
        private Vector2 Snapped(Vector2 at, Vector2 size, bool dragged) {
            var data = settings.Data;

            if (!data.SnapWindows || ImGui.GetIO().KeyShift) {
                return at;
            }

            var grid = GridSpacing();
            var snapped = WindowSnap.Snap(at, size, ImGui.GetIO().DisplaySize, Others(), Math.Max(1, data.SnapDistance),
                grid);
            snapped = new Vector2((float)Math.Round(snapped.X), (float)Math.Round(snapped.Y));

            // its middle lights the guide line it's on
            if (dragged) {
                var middle = snapped + size / 2;
                ShowGrid(grid, new[] { middle.X }, new[] { middle.Y });
            }

            return snapped;
        }

        /// <summary>The grid's spacing for moving a window, 0 with the grid off.</summary>
        private float GridSpacing() {
            var data = settings.Data;
            return data.SnapGrid ? Math.Max(2, data.SnapGridSize) : 0;
        }

        /// <summary>The grid's spacing for resizing one (SnapResizeGrid, on its own setting), 0 with that off.</summary>
        private float ResizeGridSpacing() {
            var data = settings.Data;
            return data.SnapResizeGrid ? Math.Max(2, data.SnapGridSize) : 0;
        }

        /// <summary>The window's right edge on screen, as drawn this frame or the last; null when it hasn't been.</summary>
        public float? RightEdge {
            get {
                var box = DrawnBox;
                return box.HasValue ? box.Value.Position.X + box.Value.Size.X : (float?)null;
            }
        }

        /// <summary>
        /// Where a width grip dragging one of the window's sides to here has it snap (a window that sizes itself, made wider
        /// by what's in it: GamePanel.WidthGrip, the XP bar's sides), as a resized edge snaps: the screen's edges, the
        /// other windows' beside it, and with Snap resizing to the grid the grid and its guide lines, lit when it's on one.
        /// The place itself with snapping off, or Shift held.
        /// </summary>
        public float SnapSideEdge(float side) {
            var options = settings.Data;

            if (!options.SnapWindows || !options.SnapResize || ImGui.GetIO().KeyShift || boxFrame < 0) {
                return side;
            }

            var grid = ResizeGridSpacing();
            var snapped = WindowSnap.SnapEdge(side, true, boxMin.Y, boxMax.Y - boxMin.Y, ImGui.GetIO().DisplaySize,
                Others(), Math.Max(1, options.SnapDistance), grid);
            ShowGrid(grid, new[] { snapped }, new float[0]);
            return (float)Math.Round(snapped);
        }

        /// <summary>The grid, if it's on and to be shown while dragging, with the guide lines at these places lit.</summary>
        private void ShowGrid(float grid, float[] litX, float[] litY) {
            if (grid > 0 && settings.Data.SnapGridShown) {
                DrawGrid(grid, litX, litY);
            }
        }

        /// <summary>
        /// The other windows drawn this frame or the last, each its top left and bottom right; not those held to this one
        /// (Follows), which move with it: its edge against theirs held it where it was, so it moved in jumps.
        /// </summary>
        private List<(Vector2 Min, Vector2 Max)> Others() {
            var frame = ImGui.GetFrameCount();
            return Showing
                .Where(w => w != this && !w.disposed && w.boxFrame >= frame - 1 && w.Follows?.Invoke() != this)
                .Select(w => (w.boxMin, w.boxMax))
                .ToList();
        }

        /// <summary>
        /// A window ImGui is dragging or resizing, with the button down and the window focused: moved since last frame with
        /// its size the same (an unlocked one, by its title bar), or its size changed (one the player sizes, by an edge or
        /// corner, a locked one while Ctrl has hold of an edge). Its snapped place or edges are put on it now, before
        /// RememberPosition saves them.
        /// </summary>
        private void SnapWhileDragged() {
            var pos = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            var held = positioned && ImGui.IsMouseDown(ImGuiMouseButton.Left)
                && ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
            // not while a handle of the contents' has the mouse (the XP bar's left side moves the window as it widens it)
            var moving = held && !Locked && !CtrlHandleHeld && pos != lastPos && size == lastSize;

            if (moving) {
                var snapped = Snapped(pos, size, true);

                if (snapped != pos) {
                    ImGui.SetWindowPos(snapped);
                    pos = snapped;
                }
            }

            lastPos = pos;
            lastSize = size;
        }

        /// <summary>
        /// The window's least size for its next Begin, with SnapSize as its size callback, straight through cimgui: never
        /// ImGui.NET's SetNextWindowSizeConstraints with a callback (SizeCallbackCdecl says why). The delegate is kept for as
        /// long as the window, since native code calls it.
        /// </summary>
        private unsafe void SetSizeConstraints(Vector2 least) {
            if (sizeCallback == null) {
                sizeCallback = SnapSize;
                sizeCallbackPointer = Marshal.GetFunctionPointerForDelegate(sizeCallback);
            }

            igSetNextWindowSizeConstraints(least, new Vector2(float.MaxValue, float.MaxValue), sizeCallbackPointer,
                IntPtr.Zero);
        }

        /// <summary>
        /// A resize's edges snapped as it's dragged (WindowSnap.SnapEdge), with the button down and the size ImGui wants
        /// not the one it has: ImGui works a resize out from the mouse inside Begin, every frame, and asks this before it
        /// uses it, so the snapped edge is what's drawn. Done after Begin instead, the first build's snap only showed once
        /// the button was let go. The edge moving is the one nearer the mouse, left or right, top or bottom; ImGui keeps
        /// the other put (moving the window for a left or top edge). Held to the window's least size.
        /// </summary>
        private unsafe void SnapSize(ImGuiSizeCallbackData* data) {
            try {
                var options = settings.Data;
                var desired = data->DesiredSize;
                var current = data->CurrentSize;

                // only a resize the player's making: a window they size, the press begun on its edge (ImGui asks
                // this of a window that sizes itself too, whose wanted size can differ from its own on any click,
                // and the grid showed for every press)
                if (!options.SnapWindows || !options.SnapResize || ImGui.GetIO().KeyShift || !defaultSize.HasValue
                    || !resizePress || !ImGui.IsMouseDown(ImGuiMouseButton.Left) || desired == current) {
                    return;
                }

                var pos = data->Pos;
                var mouse = ImGui.GetMousePos();
                var grid = ResizeGridSpacing();
                var others = Others();
                var litX = new float[0];
                var litY = new float[0];
                var screen = ImGui.GetIO().DisplaySize;
                var distance = Math.Max(1, options.SnapDistance);

                if (desired.X != current.X) {
                    var right = pos.X + current.X;

                    if (Math.Abs(mouse.X - pos.X) < Math.Abs(mouse.X - right)) {
                        var left = WindowSnap.SnapEdge(right - desired.X, true, pos.Y, desired.Y, screen, others,
                            distance, grid);
                        desired.X = right - left;
                        litX = new[] { left };
                    }
                    else {
                        var edge = WindowSnap.SnapEdge(pos.X + desired.X, true, pos.Y, desired.Y, screen, others, distance,
                            grid);
                        desired.X = edge - pos.X;
                        litX = new[] { edge };
                    }
                }

                if (desired.Y != current.Y) {
                    var bottom = pos.Y + current.Y;

                    if (Math.Abs(mouse.Y - pos.Y) < Math.Abs(mouse.Y - bottom)) {
                        var top = WindowSnap.SnapEdge(bottom - desired.Y, false, pos.X, desired.X, screen, others,
                            distance, grid);
                        desired.Y = bottom - top;
                        litY = new[] { top };
                    }
                    else {
                        var edge = WindowSnap.SnapEdge(pos.Y + desired.Y, false, pos.X, desired.X, screen, others,
                            distance, grid);
                        desired.Y = edge - pos.Y;
                        litY = new[] { edge };
                    }
                }

                // the edge being dragged lights the guide line it's on
                ShowGrid(grid, litX, litY);

                var least = new Vector2(minWidth, MinHeight ?? minSize?.Y ?? 200);
                desired = Vector2.Max(least, new Vector2((float)Math.Round(desired.X), (float)Math.Round(desired.Y)));
                data->DesiredSize = desired;
            }
            catch (Exception ex) {
                PluginCore.LogFile("[snap] " + ex);
            }
        }

        /// <summary>
        /// The grid, faintly, over the whole screen and behind every window, and over it the guide lines (WindowSnap.Guides),
        /// thicker: the middle thickest, then the thirds and quarters. A guide line with a place in litX (or litY) on it,
        /// the dragged window's middle or the edge being resized, is drawn lit.
        /// </summary>
        private static void DrawGrid(float spacing, float[] litX, float[] litY) {
            var frame = ImGui.GetFrameCount();

            if (gridFrame == frame) {
                return;
            }

            gridFrame = frame;
            var drawList = ImGui.GetBackgroundDrawList();
            var screen = ImGui.GetIO().DisplaySize;

            for (var x = spacing; x < screen.X; x += spacing) {
                drawList.AddLine(new Vector2(x, 0), new Vector2(x, screen.Y), GridColor);
            }

            for (var y = spacing; y < screen.Y; y += spacing) {
                drawList.AddLine(new Vector2(0, y), new Vector2(screen.X, y), GridColor);
            }

            var guidesX = WindowSnap.Guides(screen.X);
            var guidesY = WindowSnap.Guides(screen.Y);

            for (var i = 0; i < guidesX.Length; i++) {
                var x = guidesX[i];
                var lit = litX.Any(place => WindowSnap.OnGuide(place, x));
                drawList.AddLine(new Vector2(x, 0), new Vector2(x, screen.Y), GuideColor(i, lit), GuideThickness(i, lit));
            }

            for (var i = 0; i < guidesY.Length; i++) {
                var y = guidesY[i];
                var lit = litY.Any(place => WindowSnap.OnGuide(place, y));
                drawList.AddLine(new Vector2(0, y), new Vector2(screen.X, y), GuideColor(i, lit), GuideThickness(i, lit));
            }
        }

        /// <summary>The middle (the first guide) brighter than the thirds and quarters; any lit one gold.</summary>
        private static uint GuideColor(int index, bool lit) => lit ? GuideLitColor : index == 0 ? MiddleColor : GuideColorFaint;

        private static float GuideThickness(int index, bool lit) => (index == 0 ? 3 : 2) + (lit ? 1 : 0);
    }
}
