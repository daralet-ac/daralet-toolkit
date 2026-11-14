using System;
using System.Numerics;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// What's under an open section header (ImGui.CollapsingHeader), set in from the left with a line down its left
    /// side, so it's plain which section it belongs to and where it ends (the user's call, November 2026). Begun with
    /// Form.Folded() straight after the header, ended when disposed: <c>using var fold = Form.Folded();</c> once the
    /// header has said it's open, so the rest of the block or method is the section. The line comes down from the point
    /// of the header's arrow, and the contents start a little to its right.
    /// </summary>
    internal readonly struct Fold : IDisposable {
        private readonly ImDrawListPtr drawList;
        private readonly Vector2 top;
        private readonly float lineX;
        private readonly float indent;
        private readonly bool begun;

        private Fold(ImDrawListPtr drawList, Vector2 top, float lineX, float indent) {
            this.drawList = drawList;
            this.top = top;
            this.lineX = lineX;
            this.indent = indent;
            begun = true;
        }

        public static Fold Begin() {
            var top = ImGui.GetCursorScreenPos();
            var font = ImGui.GetFontSize();

            // the header is the last item: ImGui draws its arrow at the frame's left plus the frame padding, a font size
            // wide, and the open arrow's point is in the middle of that
            var arrowPoint = ImGui.GetItemRectMin().X + ImGui.GetStyle().FramePadding.X + font * 0.5f;
            var lineX = (float)Math.Round(arrowPoint) + 0.5f;
            var indent = (float)Math.Round(Math.Max(8, lineX - top.X + font * 0.5f));
            var fold = new Fold(ImGui.GetWindowDrawList(), top, lineX, indent);
            ImGui.Indent(indent);

            // ImGui's separators run from the window's left edge, over the line, unless they're in a group of the
            // window's, where they start at the indent
            ImGui.BeginGroup();
            return fold;
        }

        public void Dispose() {
            if (!begun) {
                return;
            }

            ImGui.EndGroup();
            ImGui.Unindent(indent);

            // from just under the header to the last line drawn, not the gap after it
            var bottom = ImGui.GetCursorScreenPos().Y - ImGui.GetStyle().ItemSpacing.Y;

            if (bottom <= top.Y) {
                return;
            }

            var color = ImGui.GetStyle().Colors[(int)ImGuiCol.Header];
            color.W = 1;
            drawList.AddLine(new Vector2(lineX, top.Y), new Vector2(lineX, bottom), ImGui.GetColorU32(color),
                Math.Max(1, (float)Math.Round(ImGui.GetFontSize() / 12)));
        }
    }
}
