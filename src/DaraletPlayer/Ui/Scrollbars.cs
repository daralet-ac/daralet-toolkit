using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The plugin's scroll bars, as the game's (layout 0x2100003E). Vertical, 0x10000367, 16 wide: the gold chain track
    /// 06004C5F, a blue thumb of a 3-pixel top 06004C60, a middle 06004C63 and a 3-pixel bottom 06004C66, and a gem arrow at
    /// each end, up 06004C6C and down 06004C69. Horizontal, 0x1000036D, 16 high: the chain 06004C7F, the thumb's left
    /// 06004C80, middle 06004C83 and right 06004C86, the left arrow 06004C8C and right 06004C89. Each part's next two
    /// pictures are its hover (light blue) and pressed states. ImGui still makes the bars (their room, the mouse wheel);
    /// this draws the game's over them, opaque, at the very box ImGui uses (igGetWindowScrollbarRect, one of ImGui's own
    /// functions that UtilityBelt's cimgui.dll exports but its ImGui.NET doesn't wrap), from the end of each window that
    /// can scroll: FloatingWindow, the main window, Widgets.EndChild and Widgets.EndScrollingTable. It takes the bars'
    /// clicks as the game's does: the arrows step (and again while held), the track pages, the thumb drags, all along the
    /// travel between the arrows. ImGui's own bar sees the press first, as its window begins, so its hold is let go
    /// (igClearActiveID) and the scroll it jumped to is replaced by the one before. Game thread only.
    /// </summary>
    internal static class Scrollbars {
        private const string Cimgui = "cimgui";

        /// <summary>ImGuiWindow.Flags, after its Name (a pointer) and ID, in ImGui 1.89.7.</summary>
        private const int WindowFlagsOffset = 8;

        private const int AxisX = 0;
        private const int AxisY = 1;

        /// <summary>The arrows' first step repeats after this, then every RepeatEvery while held.</summary>
        private const double RepeatDelay = 0.4;
        private const double RepeatEvery = 0.06;

        // packed by hand: ImGui.GetColorU32 crashes before ImGui has a context
        private static readonly uint Backing = PackedColor.Pack(18, 14, 10);

        private static readonly Dictionary<(IntPtr Window, int Axis), BarState> states =
            new Dictionary<(IntPtr Window, int Axis), BarState>();

        private static bool broken;

        /// <summary>Windows already logged for scrolling with no game bar drawn, by name and why (once each).</summary>
        private static readonly HashSet<string> loggedSkips = new HashSet<string>();

        [StructLayout(LayoutKind.Sequential)]
        private struct Rect {
            public Vector2 Min;
            public Vector2 Max;
        }

        private sealed class BarState {
            /// <summary>The scroll as this frame ends: what a click takes back to, ImGui having jumped by then.</summary>
            public float Scroll;

            /// <summary>Where on the thumb the mouse took hold of it, while it's dragged.</summary>
            public float? Grab;

            /// <summary>-1 the up (left) arrow held, 1 the down (right) arrow, 0 neither.</summary>
            public int Arrow;

            public double RepeatAt;
        }

        [DllImport(Cimgui, CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr igGetCurrentWindow();

        [DllImport(Cimgui, CallingConvention = CallingConvention.Cdecl)]
        private static extern void igGetWindowScrollbarRect(out Rect rect, IntPtr window, int axis);

        [DllImport(Cimgui, CallingConvention = CallingConvention.Cdecl)]
        private static extern uint igGetActiveID();

        [DllImport(Cimgui, CallingConvention = CallingConvention.Cdecl)]
        private static extern void igClearActiveID();

        /// <summary>The current window's scroll bars, drawn and worked as the game's, where it has them.</summary>
        public static void Draw((Vector2 Min, Vector2 Max)? clip = null) {
            if (broken) {
                return;
            }

            try {
                var window = igGetCurrentWindow();

                if (window == IntPtr.Zero || !ItemIcons.CanDrawArt) {
                    return;
                }

                var flags = (ImGuiWindowFlags)Marshal.ReadInt32(window, WindowFlagsOffset);
                var none = (flags & ImGuiWindowFlags.NoScrollbar) != 0;
                var horizontal = (flags & (ImGuiWindowFlags.HorizontalScrollbar
                    | ImGuiWindowFlags.AlwaysHorizontalScrollbar)) != 0;
                var (barWidth, barHeight) = ShownBars();

                if (!none && barWidth <= 0.5f && ImGui.GetScrollMaxY() > 0) {
                    LogSkip(window, "no vertical bar found", clip);
                }

                Bar(window, AxisY, !none && barWidth > 0.5f && ImGui.GetScrollMaxY() > 0, clip);
                Bar(window, AxisX, !none && horizontal && barHeight > 0.5f && ImGui.GetScrollMaxX() > 0, clip);
            }
            catch (Exception ex) {
                broken = true;
                PluginCore.LogFile("[scrollbars] ImGui's own scroll bars from now on: " + ex);
            }
        }

        /// <summary>
        /// The current window's bars as ImGui made them this frame: the vertical one's width and the horizontal one's
        /// height, 0 for one it hasn't. Read from where its content stops: ImGui takes a bar's room off that side of the
        /// content region (the right for the vertical bar, the bottom for the horizontal), past the window's padding,
        /// which the left edge of the content region shows. Not "it has room to scroll": a window that sizes itself can,
        /// with no bar, and igGetWindowScrollbarRect asserts that the bar is there (ImGui's "scrollbar_size > 0.0f",
        /// which stopped the game at login).
        /// </summary>
        private static (float Width, float Height) ShownBars() {
            // the content region moves with the scroll: taken back out, its left edge is the padding
            var scroll = new Vector2(ImGui.GetScrollX(), ImGui.GetScrollY());
            var min = ImGui.GetWindowContentRegionMin() + scroll;
            var max = ImGui.GetWindowContentRegionMax() + scroll;
            var size = ImGui.GetWindowSize();
            var width = size.X - max.X - min.X;
            // the padding below is taken to be the padding at the side (ImGui's style has them alike, and a child window
            // without a border has none)
            var height = size.Y - max.Y - min.X;
            var least = ImGui.GetStyle().ScrollbarSize * 0.75f;

            // the sides' padding is always alike, so any width left over is the vertical bar (a drop-down's list, padded
            // by the frame's padding, wasn't found at three quarters of the style's size); only the bottom is a guess
            return (width >= 4 ? width : 0, height >= least ? height : 0);
        }

        /// <summary>
        /// A line in log.txt, once a window and reason, when a window scrolls but the game's bar isn't drawn over ImGui's:
        /// what ImGui's content region and the clip were, to find out why.
        /// </summary>
        private static void LogSkip(IntPtr window, string why, (Vector2 Min, Vector2 Max)? clip) {
            // ImGuiWindow starts with its Name, a char*
            var name = Marshal.PtrToStringAnsi(Marshal.ReadIntPtr(window)) ?? "?";

            if (!loggedSkips.Add(name + "|" + why)) {
                return;
            }

            PluginCore.LogFile($"[scrollbars] {name}: {why}; size {ImGui.GetWindowSize()}, content "
                + $"{ImGui.GetWindowContentRegionMin()} to {ImGui.GetWindowContentRegionMax()}, scroll "
                + $"{ImGui.GetScrollY()} of {ImGui.GetScrollMaxY()}, clip {clip?.Min} to {clip?.Max}, bar size "
                + $"{ImGui.GetStyle().ScrollbarSize}");
        }

        private static void Bar(IntPtr window, int axis, bool shown, (Vector2 Min, Vector2 Max)? clip) {
            if (!states.TryGetValue((window, axis), out var state)) {
                state = new BarState();
                states[(window, axis)] = state;
            }

            var vertical = axis == AxisY;
            var scroll = vertical ? ImGui.GetScrollY() : ImGui.GetScrollX();

            if (!shown) {
                state.Scroll = scroll;
                state.Grab = null;
                state.Arrow = 0;
                return;
            }

            igGetWindowScrollbarRect(out var bar, window, axis);
            var thickness = Across(bar.Max - bar.Min, vertical);
            var length = Along(bar.Max - bar.Min, vertical);

            if (thickness < 4 || length < thickness * 2 + 4) {
                LogSkip(window, $"bar too small, {bar.Min} to {bar.Max}", clip);
                state.Scroll = scroll;
                return;
            }

            var most = vertical ? ImGui.GetScrollMaxY() : ImGui.GetScrollMaxX();
            var start = Along(bar.Min, vertical) + thickness;
            var travelLength = length - thickness * 2;
            var thumbLength = Math.Min(travelLength, Math.Max(thickness, travelLength * length / (length + most)));
            var travel = Math.Max(1, travelLength - thumbLength);

            var target = Input(state, bar, vertical, start, travelLength, thumbLength, travel, most, length);
            var shownScroll = target ?? scroll;
            var thumbStart = start + travel * Clamp(shownScroll / most);

            Paint(state, bar, vertical, thickness, start, travelLength, thumbStart, thumbLength, clip);
            state.Scroll = shownScroll;
        }

        /// <summary>A bar's clicks, the thumb's drag and the arrows' repeat: the scroll to go to, or null to leave it.</summary>
        private static float? Input(BarState state, Rect bar, bool vertical, float start, float travelLength,
            float thumbLength, float travel, float most, float page) {
            var mouse = ImGui.GetMousePos();
            var at = Along(mouse, vertical);
            var down = ImGui.IsMouseDown(ImGuiMouseButton.Left);
            var over = Inside(bar, mouse) && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
            var step = vertical ? ImGui.GetTextLineHeightWithSpacing() * 3 : ThicknessStep(bar);
            var now = ImGui.GetTime();
            float? target = null;

            if (over && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                // ImGui's own bar took the press as the window began, and may have jumped: let go of it, from the scroll before
                if (igGetActiveID() != 0) {
                    igClearActiveID();
                }

                var from = state.Scroll;
                var thumbStart = start + travel * Clamp(from / most);

                if (at < start) {
                    state.Arrow = -1;
                    state.RepeatAt = now + RepeatDelay;
                    target = from - step;
                }
                else if (at >= start + travelLength) {
                    state.Arrow = 1;
                    state.RepeatAt = now + RepeatDelay;
                    target = from + step;
                }
                else if (at >= thumbStart && at < thumbStart + thumbLength) {
                    state.Grab = at - thumbStart;
                    target = from;
                }
                else {
                    target = at < thumbStart ? from - page : from + page;
                }
            }
            else if (state.Grab.HasValue) {
                if (down) {
                    target = Clamp((at - state.Grab.Value - start) / travel) * most;
                }
                else {
                    state.Grab = null;
                }
            }
            else if (state.Arrow != 0) {
                if (!down) {
                    state.Arrow = 0;
                }
                else if (now >= state.RepeatAt && over) {
                    state.RepeatAt = now + RepeatEvery;
                    target = state.Scroll + state.Arrow * step;
                }
            }

            if (target.HasValue) {
                target = Math.Max(0, Math.Min(most, target.Value));

                if (vertical) {
                    ImGui.SetScrollY(target.Value);
                }
                else {
                    ImGui.SetScrollX(target.Value);
                }
            }

            return target;
        }

        /// <summary>A sideways arrow steps two of the bar's thickness: about an item in the Vendor window's row.</summary>
        private static float ThicknessStep(Rect bar) {
            return (bar.Max.Y - bar.Min.Y) * 2;
        }

        /// <summary>The game's bar over ImGui's: an opaque backing, the chain, the arrows and the thumb.</summary>
        private static void Paint(BarState state, Rect bar, bool vertical, float thickness, float start, float travelLength,
            float thumbStart, float thumbLength, (Vector2 Min, Vector2 Max)? clip) {
            var drawList = ImGui.GetWindowDrawList();
            var mouse = ImGui.GetMousePos();
            var at = Along(mouse, vertical);
            var over = Inside(bar, mouse) && ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
            var down = ImGui.IsMouseDown(ImGuiMouseButton.Left);
            var prefix = vertical ? "scrollbar-" : "scrollbar-h-";

            // the bar is outside the window's own clip (its contents'), so it's drawn with one of its own: the bar cut
            // to where the parent window can draw, for a child, as ImGui's own bar is, or one out past the window showed
            var visibleMin = clip.HasValue ? Vector2.Max(bar.Min, clip.Value.Min) : bar.Min;
            var visibleMax = clip.HasValue ? Vector2.Min(bar.Max, clip.Value.Max) : bar.Max;

            if (visibleMax.X <= visibleMin.X || visibleMax.Y <= visibleMin.Y) {
                LogSkip(igGetCurrentWindow(), $"bar {bar.Min} to {bar.Max} wholly clipped", clip);
                return;
            }

            drawList.PushClipRect(visibleMin, visibleMax, false);
            drawList.AddRectFilled(bar.Min, bar.Max, Backing);

            // the chain, a link twice as long as the bar is thick, repeated along the travel
            drawList.PushClipRect(Box(bar, vertical, start, start + travelLength).Min,
                Box(bar, vertical, start, start + travelLength).Max, true);

            for (var p = start; p < start + travelLength; p += thickness * 2) {
                var link = Box(bar, vertical, p, p + thickness * 2);
                ItemIcons.DrawArtBox(drawList, prefix + "track", link.Min, link.Max);
            }

            drawList.PopClipRect();

            var barStart = Along(bar.Min, vertical);
            var barEnd = Along(bar.Max, vertical);
            var first = Box(bar, vertical, barStart, start);
            var last = Box(bar, vertical, barEnd - thickness, barEnd);
            ItemIcons.DrawArtBox(drawList, prefix + (vertical ? "up" : "left")
                + Suffix(over && at < start, state.Arrow == -1 && down), first.Min, first.Max);
            ItemIcons.DrawArtBox(drawList, prefix + (vertical ? "down" : "right")
                + Suffix(over && at >= start + travelLength, state.Arrow == 1 && down), last.Min, last.Max);

            // the thumb: its 3-pixel ends (scaled with the bar's thickness) and its middle stretched between
            var suffix = Suffix(over && at >= thumbStart && at < thumbStart + thumbLength, state.Grab.HasValue);
            var cap = (float)Math.Round(thickness * 3 / 16);
            var from = (float)Math.Round(thumbStart);
            var to = (float)Math.Round(thumbStart + thumbLength);
            var middle = Box(bar, vertical, from + cap, to - cap);
            var head = Box(bar, vertical, from, from + cap);
            var tail = Box(bar, vertical, to - cap, to);
            ItemIcons.DrawArtBox(drawList, prefix + "thumb-middle" + suffix, middle.Min, middle.Max);
            ItemIcons.DrawArtBox(drawList, prefix + (vertical ? "thumb-top" : "thumb-left") + suffix, head.Min, head.Max);
            ItemIcons.DrawArtBox(drawList, prefix + (vertical ? "thumb-bottom" : "thumb-right") + suffix, tail.Min,
                tail.Max);

            drawList.PopClipRect();
        }

        /// <summary>The part of the bar between two places along it, the bar's whole thickness across.</summary>
        private static Rect Box(Rect bar, bool vertical, float from, float to) {
            return vertical
                ? new Rect { Min = new Vector2(bar.Min.X, from), Max = new Vector2(bar.Max.X, to) }
                : new Rect { Min = new Vector2(from, bar.Min.Y), Max = new Vector2(to, bar.Max.Y) };
        }

        private static float Along(Vector2 v, bool vertical) => vertical ? v.Y : v.X;

        private static float Across(Vector2 v, bool vertical) => vertical ? v.X : v.Y;

        /// <summary>A part's picture: pressed while held, hover (light) while the mouse is on it, else its own.</summary>
        private static string Suffix(bool hovered, bool pressed) {
            return pressed ? "-pressed" : hovered ? "-hover" : "";
        }

        private static bool Inside(Rect rect, Vector2 at) {
            return at.X >= rect.Min.X && at.X < rect.Max.X && at.Y >= rect.Min.Y && at.Y < rect.Max.Y;
        }

        private static float Clamp(float fraction) {
            return fraction < 0 ? 0 : fraction > 1 ? 1 : fraction;
        }
    }
}
