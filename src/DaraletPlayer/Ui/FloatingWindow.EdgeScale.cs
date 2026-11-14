using System;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Sizing a window that sizes itself by its edges (the user's call, November 2026: nearly every window adjustable by
    /// dragging its edges). Its size is its contents', so ImGui's edges can't size it; dragging its left, right or bottom
    /// edge (or a bottom corner) changes its Size instead, the scale its contents are drawn at, by as much as the edge has
    /// gone (the right edge twice as far makes it twice the size), held to Size's range. The top edge is still the move
    /// (EdgeDrag). The left edge keeps the right one where it was (MoveTo, from the window's width each frame). The edge
    /// snaps as a resized edge does. Not for a window whose contents have grips of their own on its sides (SideGrips: the
    /// bars, whose width they set), nor one held to a box on screen. Unlocked it works as it is; locked, with Ctrl held.
    /// Worked by hand as SideGrips are, with HandleHovered and CtrlHandleHeld so the move and Ctrl's move leave it alone.
    /// The strip is the window's padding, at least 3 pixels, so a hotbar's slots keep their clicks.
    /// </summary>
    internal partial class FloatingWindow {
        private const int EdgeLeft = 1;
        private const int EdgeRight = 2;
        private const int EdgeBottom = 4;

        /// <summary>The edges being dragged (EdgeLeft...), 0 for none, and the mouse, box and Size at the press.</summary>
        private int scaleEdges;
        private Vector2 scaleGrab;
        private Vector2 scaleMin;
        private Vector2 scaleMax;
        private float scaleAtGrab;

        /// <summary>Set by SideGrips: the window's sides are its contents', and EdgeScale leaves them alone.</summary>
        public bool ContentGrips { get; set; }

        private void EdgeScale() {
            if (defaultSize.HasValue || ContentGrips || Anchor?.Invoke() != null) {
                scaleEdges = 0;
                return;
            }

            var pos = ImGui.GetWindowPos();
            var size = ImGui.GetWindowSize();
            var mouse = ImGui.GetMousePos();
            var ctrl = ImGui.GetIO().KeyCtrl;
            var canGrip = ctrl || !Locked;
            var strip = Math.Max(3, (float)Math.Floor(ImGui.GetStyle().WindowPadding.X));
            var inWindow = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows
                | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
            var edges = 0;

            if (inWindow && !closeHovered) {
                edges |= mouse.X < pos.X + strip ? EdgeLeft : 0;
                edges |= mouse.X >= pos.X + size.X - strip ? EdgeRight : 0;
                edges |= mouse.Y >= pos.Y + size.Y - strip ? EdgeBottom : 0;
            }

            if (scaleEdges == 0 && canGrip && edges != 0 && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                scaleEdges = edges;
                scaleGrab = mouse;
                scaleMin = pos;
                scaleMax = pos + size;
                scaleAtGrab = Scale;
            }
            else if (scaleEdges != 0 && !ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                scaleEdges = 0;
            }

            var dragging = scaleEdges != 0;
            CtrlHandleHeld = dragging;
            HandleHovered = dragging || canGrip && edges != 0;

            if (!HandleHovered) {
                return;
            }

            var shown = dragging ? scaleEdges : edges;
            ImGui.SetMouseCursor(EdgeCursor(shown));
            DrawEdgeLines(shown, pos, size, dragging);

            if (dragging) {
                ScaleByEdges(mouse, size);
            }
        }

        /// <summary>The Size the dragged edges ask for, and the window kept where its other edges were.</summary>
        private void ScaleByEdges(Vector2 mouse, Vector2 size) {
            var width = Math.Max(1, scaleMax.X - scaleMin.X);
            var height = Math.Max(1, scaleMax.Y - scaleMin.Y);
            var ratio = 1f;

            void Take(float candidate) {
                if (Math.Abs(candidate - 1) > Math.Abs(ratio - 1)) {
                    ratio = candidate;
                }
            }

            if ((scaleEdges & EdgeRight) != 0) {
                Take((SnapSideEdge(scaleMax.X + mouse.X - scaleGrab.X) - scaleMin.X) / width);
            }
            else if ((scaleEdges & EdgeLeft) != 0) {
                Take((scaleMax.X - SnapSideEdge(scaleMin.X + mouse.X - scaleGrab.X)) / width);
            }

            if ((scaleEdges & EdgeBottom) != 0) {
                Take((SnapBottomEdge(scaleMax.Y + mouse.Y - scaleGrab.Y) - scaleMin.Y) / height);
            }

            Scale = (float)Math.Round(Math.Max(0.05f, scaleAtGrab * ratio), 2);

            // the left edge: the right one stays put, as wide as the window is now
            if ((scaleEdges & EdgeLeft) != 0) {
                MoveTo(new Vector2((float)Math.Round(scaleMax.X - size.X), scaleMin.Y));
            }
        }

        /// <summary>The bottom edge dragged to here, snapped as a resized edge is (SnapSideEdge, the other way).</summary>
        private float SnapBottomEdge(float bottom) {
            var options = settings.Data;

            if (!options.SnapWindows || !options.SnapResize || ImGui.GetIO().KeyShift || boxFrame < 0) {
                return bottom;
            }

            var grid = ResizeGridSpacing();
            var snapped = WindowSnap.SnapEdge(bottom, false, boxMin.X, boxMax.X - boxMin.X, ImGui.GetIO().DisplaySize,
                Others(), Math.Max(1, options.SnapDistance), grid);
            ShowGrid(grid, new float[0], new[] { snapped });
            return (float)Math.Round(snapped);
        }

        private static ImGuiMouseCursor EdgeCursor(int edges) {
            if ((edges & EdgeBottom) != 0) {
                return (edges & EdgeLeft) != 0 ? ImGuiMouseCursor.ResizeNESW
                    : (edges & EdgeRight) != 0 ? ImGuiMouseCursor.ResizeNWSE : ImGuiMouseCursor.ResizeNS;
            }

            return ImGuiMouseCursor.ResizeEW;
        }

        /// <summary>A line along each edge under the mouse or held, brighter while held.</summary>
        private static void DrawEdgeLines(int edges, Vector2 pos, Vector2 size, bool held) {
            var color = ImGui.GetColorU32(held ? ImGuiCol.ResizeGripActive : ImGuiCol.ResizeGripHovered);
            var drawList = ImGui.GetWindowDrawList();
            var max = pos + size;
            drawList.PushClipRect(pos, max, false);

            if ((edges & EdgeLeft) != 0) {
                drawList.AddLine(new Vector2(pos.X + 1, pos.Y), new Vector2(pos.X + 1, max.Y), color, 2);
            }

            if ((edges & EdgeRight) != 0) {
                drawList.AddLine(new Vector2(max.X - 1, pos.Y), new Vector2(max.X - 1, max.Y), color, 2);
            }

            if ((edges & EdgeBottom) != 0) {
                drawList.AddLine(new Vector2(pos.X, max.Y - 1), new Vector2(max.X, max.Y - 1), color, 2);
            }

            drawList.PopClipRect();
        }
    }
}
