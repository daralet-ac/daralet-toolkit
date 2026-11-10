using System;
using System.Numerics;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>What's drawn over an icon: a loot rule's mark and a stack's count.</summary>
    internal partial class ItemIcons {
        /// <summary>
        /// The plugin's one set of icons, for the marks' letters, which need its labels: the marks are drawn from static
        /// code all over. Null before it's made and once it's disposed.
        /// </summary>
        private static ItemIcons current;

        /// <summary>
        /// A loot rule's mark over an icon: its highlight as a frame, its symbol in the top left corner (the bottom right is
        /// a stack's count). Nothing for a rule without one.
        /// </summary>
        public static void DrawMark(ImDrawListPtr drawList, LootRule rule, Vector2 at, float size) {
            if (rule == null) {
                return;
            }

            var highlight = LootMarks.HighlightColor(rule);

            if (highlight.HasValue) {
                // inside the icon's edge, so neighbouring rows and cells don't cover it
                var thickness = Math.Max(2f, (float)Math.Round(size / 14));
                var inset = new Vector2(thickness / 2, thickness / 2);
                drawList.AddRect(at + inset, at + new Vector2(size, size) - inset, ColorU32(highlight.Value), 0, ImDrawFlags.None, thickness);
            }

            var icon = LootMarks.IconOf(rule);

            if (icon != null) {
                var symbol = Math.Max(8f, size * 0.45f);
                DrawSymbol(drawList, icon, at + new Vector2(1, 1), symbol, ColorU32(LootMarks.IconColor(rule)));
            }
        }

        /// <summary>A stack's size in an icon's bottom right corner.</summary>
        /// <summary>
        /// The mark of an item marked for sale (VendorSale): a green $ in the bottom left corner, clear of a loot mark's
        /// symbol (top left) and a stack's count (bottom right).
        /// </summary>
        public static void DrawSaleMark(ImDrawListPtr drawList, Vector2 at, float size) {
            var symbol = Math.Max(8f, size * 0.45f);
            DrawSymbol(drawList, LootMarks.Dollar, at + new Vector2(1, size - symbol - 1), symbol,
                ImGui.GetColorU32(Colors.Good));
        }

        // the game's shortcut mark: a pale gold, dithered over the icon, and a cream square with the key in black
        private static readonly uint HotbarHatch = PackedColor.Pack(236, 212, 120, 255);
        private static readonly uint HotbarKeyFill = PackedColor.Pack(240, 234, 210, 255);
        private static readonly uint HotbarKeyEdge = PackedColor.Pack(60, 55, 45, 255);
        private static readonly uint HotbarKeyText = PackedColor.Pack(0, 0, 0, 255);

        /// <summary>
        /// A loot rule's mark and the hotbar mark together, since they share the top left corner: the hotbar's gold under
        /// the rule's frame and symbol, and its key square over them, so the key always shows. Either can be null (no
        /// rule; not on a hotbar).
        /// </summary>
        public void DrawMarks(ImDrawListPtr drawList, Vector2 at, float size, LootRule rule, string hotbarKey) {
            if (hotbarKey != null) {
                DrawHotbarCorner(drawList, at, size);
            }

            DrawMark(drawList, rule, at, size);

            if (hotbarKey != null) {
                DrawHotbarKey(drawList, at, size, hotbarKey);
            }
        }

        /// <summary>The icon's own pixels: 1 in a 32-pixel square, so the hotbar mark is the game's at its size.</summary>
        private static float HotbarPixel(float size) => Math.Max(1f, (float)Math.Round(size / 32));

        /// <summary>
        /// The hotbar mark's ground, as the game's pack marks what's on its shortcut bar: the icon's top left corner
        /// dithered in pale gold.
        /// </summary>
        private static void DrawHotbarCorner(ImDrawListPtr drawList, Vector2 at, float size) {
            var pixel = HotbarPixel(size);
            var cells = (int)Math.Round(size * 0.47f / pixel);

            for (var y = 0; y < cells; y++) {
                for (var x = (y & 1); x + y < cells; x += 2) {
                    var spot = at + new Vector2(x * pixel, y * pixel);
                    drawList.AddRectFilled(spot, spot + new Vector2(pixel, pixel), HotbarHatch);
                }
            }
        }

        /// <summary>The hotbar mark's square in the corner, cream, with the key that uses the item in black ("" for none).</summary>
        private void DrawHotbarKey(ImDrawListPtr drawList, Vector2 at, float size, string key) {
            var pixel = HotbarPixel(size);
            var square = (float)Math.Max(5, Math.Round(size * 0.28f));
            var pixels = (int)Math.Round(square - 1);
            var text = string.IsNullOrEmpty(key) ? null : LabelSize(key, pixels, outlined: false);
            var width = text.HasValue ? Math.Max(square, text.Value.X + 2) : square;
            var topLeft = at + new Vector2(pixel, pixel);
            var corner = topLeft + new Vector2((float)Math.Round(width), square);

            drawList.AddRectFilled(topLeft, corner, HotbarKeyFill);
            drawList.AddRect(topLeft, corner, HotbarKeyEdge);

            if (text.HasValue) {
                var middle = (topLeft + corner) / 2;
                DrawLabel(drawList, key, middle - text.Value / 2, pixels, HotbarKeyText, outlined: false);
            }
        }

        public static void StackCount(ImDrawListPtr drawList, int stack, Vector2 at, float cell) {
            var text = StackText.Short(stack);
            var size = Widgets.SquareTextSize(text);

            // "50.1k" too wide for a small square: whole thousands
            if (size.X > cell - 3) {
                text = StackText.Short(stack, tenths: false);
                size = Widgets.SquareTextSize(text);
            }

            var spot = new Vector2(at.X + cell - size.X - 2, at.Y + cell - size.Y - 1);
            OutlinedText(drawList, text, spot, new Vector4(1, 1, 1, 1));
        }

        /// <summary>
        /// Text on a black outline, so it reads on any icon, at whole pixels. At SquareFontSize: the icons don't grow with
        /// the Text size.
        /// </summary>
        public static void OutlinedText(ImDrawListPtr drawList, string text, Vector2 at, Vector4 color) {
            var spot = new Vector2((float)Math.Round(at.X), (float)Math.Round(at.Y));
            var black = ImGui.GetColorU32(new Vector4(0, 0, 0, 1));
            var font = ImGui.GetFont();
            var fontSize = Widgets.SquareFontSize();

            foreach (var offset in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1) }) {
                drawList.AddText(font, fontSize, spot + offset, black, text);
            }

            drawList.AddText(font, fontSize, spot, ImGui.GetColorU32(color), text);
        }

        /// <summary>A mark with no icon under it: the symbol, full size, or a dot when the rule has no symbol.</summary>
        private static void DrawMarkAlone(ImDrawListPtr drawList, LootRule rule, Vector2 at, float size) {
            var symbol = size * 0.8f;
            var spot = at + new Vector2((size - symbol) / 2, (size - symbol) / 2);
            DrawSymbol(drawList, LootMarks.IconOf(rule) ?? LootMarks.Circle, spot, symbol, ColorU32(LootMarks.IconColor(rule)));
        }

        /// <summary>One of LootMarks.Icons in a square, filled with the colour and outlined in black.</summary>
        public static void DrawSymbol(ImDrawListPtr drawList, string icon, Vector2 at, float size, uint color) {
            var black = ImGui.GetColorU32(new Vector4(0, 0, 0, 1));
            var center = at + new Vector2(size / 2, size / 2);
            var radius = size / 2;
            var letter = LootMarks.LetterOf(icon);

            if (letter != null) {
                DrawLetter(drawList, letter, center, size, color);
                return;
            }

            switch (icon) {
                case LootMarks.Square: {
                    var half = new Vector2(radius * 0.72f, radius * 0.72f);
                    drawList.AddRectFilled(center - half, center + half, color);
                    drawList.AddRect(center - half, center + half, black);
                    break;
                }

                case LootMarks.Heart:
                    Outlined(drawList, color, black, (c, o) => {
                        var lobe = radius * 0.47f;
                        drawList.AddCircleFilled(center + o + new Vector2(-radius * 0.45f, -radius * 0.25f), lobe, c);
                        drawList.AddCircleFilled(center + o + new Vector2(radius * 0.45f, -radius * 0.25f), lobe, c);
                        drawList.AddTriangleFilled(center + o + new Vector2(-radius * 0.9f, -radius * 0.08f),
                            center + o + new Vector2(radius * 0.9f, -radius * 0.08f), center + o + new Vector2(0, radius * 0.9f), c);
                    });
                    break;

                case LootMarks.Plus:
                    Outlined(drawList, color, black, (c, o) => {
                        var arm = radius * 0.85f;
                        var half = Math.Max(1f, radius * 0.24f);
                        drawList.AddRectFilled(center + o - new Vector2(arm, half), center + o + new Vector2(arm, half), c);
                        drawList.AddRectFilled(center + o - new Vector2(half, arm), center + o + new Vector2(half, arm), c);
                    });
                    break;

                case LootMarks.Cross: {
                    var arm = radius * 0.62f;
                    var thickness = Math.Max(2f, size * 0.2f);
                    Stroke(drawList, new[] { center - new Vector2(arm, arm), center + new Vector2(arm, arm) }, thickness, color, black);
                    Stroke(drawList, new[] { center + new Vector2(arm, -arm), center + new Vector2(-arm, arm) }, thickness, color, black);
                    break;
                }

                case LootMarks.Check: {
                    var thickness = Math.Max(2f, size * 0.2f);
                    Stroke(drawList, new[] {
                        center + new Vector2(-radius * 0.7f, 0), center + new Vector2(-radius * 0.2f, radius * 0.55f),
                        center + new Vector2(radius * 0.75f, -radius * 0.6f),
                    }, thickness, color, black);
                    break;
                }

                case LootMarks.Arrow:
                    Outlined(drawList, color, black, (c, o) => {
                        var shaft = Math.Max(1f, radius * 0.26f);
                        drawList.AddTriangleFilled(center + o + new Vector2(0, -radius * 0.9f),
                            center + o + new Vector2(radius * 0.8f, -radius * 0.05f), center + o + new Vector2(-radius * 0.8f, -radius * 0.05f), c);
                        drawList.AddRectFilled(center + o + new Vector2(-shaft, -radius * 0.1f), center + o + new Vector2(shaft, radius * 0.9f), c);
                    });
                    break;

                case LootMarks.Star:
                    DrawStar(drawList, center, radius, color, black);
                    break;

                case LootMarks.Diamond: {
                    var points = new[] {
                        center + new Vector2(0, -radius), center + new Vector2(radius * 0.75f, 0),
                        center + new Vector2(0, radius), center + new Vector2(-radius * 0.75f, 0),
                    };
                    drawList.AddConvexPolyFilled(ref points[0], points.Length, color);
                    drawList.AddPolyline(ref points[0], points.Length, black, ImDrawFlags.Closed, 1);
                    break;
                }

                case LootMarks.Triangle: {
                    var a = center + new Vector2(0, -radius);
                    var b = center + new Vector2(radius, radius * 0.8f);
                    var c = center + new Vector2(-radius, radius * 0.8f);
                    drawList.AddTriangleFilled(a, b, c, color);
                    drawList.AddTriangle(a, b, c, black, 1);
                    break;
                }

                case LootMarks.Exclamation: {
                    var half = Math.Max(1.5f, size * 0.13f);
                    var barTop = at + new Vector2(size / 2 - half, 0);
                    var barBottom = at + new Vector2(size / 2 + half, size * 0.62f);
                    var dotTop = at + new Vector2(size / 2 - half, size * 0.76f);
                    var dotBottom = at + new Vector2(size / 2 + half, size);
                    drawList.AddRectFilled(barTop, barBottom, color);
                    drawList.AddRect(barTop, barBottom, black);
                    drawList.AddRectFilled(dotTop, dotBottom, color);
                    drawList.AddRect(dotTop, dotBottom, black);
                    break;
                }

                case LootMarks.Dollar:
                    DrawDollar(drawList, at, size, color, black);
                    break;

                default:
                    drawList.AddCircleFilled(center, radius * 0.7f, color);
                    drawList.AddCircle(center, radius * 0.7f, black);
                    break;
            }
        }

        /// <summary>A star isn't convex, so it's filled as its inner pentagon plus a triangle for each point.</summary>
        private static void DrawStar(ImDrawListPtr drawList, Vector2 center, float radius, uint color, uint outline) {
            var points = new Vector2[10];

            // alternating outer points and inner corners
            for (var i = 0; i < 10; i++) {
                var angle = -Math.PI / 2 + i * Math.PI / 5;
                var r = i % 2 == 0 ? radius : radius * 0.45f;
                points[i] = center + new Vector2((float)(Math.Cos(angle) * r), (float)(Math.Sin(angle) * r));
            }

            var inner = new[] { points[1], points[3], points[5], points[7], points[9] };
            drawList.AddConvexPolyFilled(ref inner[0], inner.Length, color);

            for (var i = 0; i < 10; i += 2) {
                drawList.AddTriangleFilled(points[(i + 9) % 10], points[i], points[i + 1], color);
            }

            drawList.AddPolyline(ref points[0], points.Length, outline, ImDrawFlags.Closed, 1);
        }

        /// <summary>
        /// A shape outlined by drawing it in black a pixel off each way, then in its colour: for shapes made of several
        /// pieces, whose own outlines would cross inside it. The callback draws the shape in a colour at an offset.
        /// </summary>
        private static void Outlined(ImDrawListPtr drawList, uint color, uint outline, Action<uint, Vector2> shape) {
            foreach (var offset in new[] {
                new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1),
                new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1),
            }) {
                shape(outline, offset);
            }

            shape(color, Vector2.Zero);
        }

        /// <summary>A thick line through the points, black under a thinner coloured one, with round ends.</summary>
        private static void Stroke(ImDrawListPtr drawList, Vector2[] points, float thickness, uint color, uint outline) {
            drawList.AddPolyline(ref points[0], points.Length, outline, ImDrawFlags.None, thickness + 2);
            drawList.AddCircleFilled(points[0], thickness / 2 + 1, outline);
            drawList.AddCircleFilled(points[points.Length - 1], thickness / 2 + 1, outline);
            drawList.AddPolyline(ref points[0], points.Length, color, ImDrawFlags.None, thickness);
            drawList.AddCircleFilled(points[0], thickness / 2, color);
            drawList.AddCircleFilled(points[points.Length - 1], thickness / 2, color);
        }

        /// <summary>
        /// A grade letter, bold on a black outline, filling the square: drawn sharp by GDI+ (DrawLabel) when the plugin's
        /// icons are there, else ImGui's text.
        /// </summary>
        private static void DrawLetter(ImDrawListPtr drawList, string letter, Vector2 center, float size, uint color) {
            // a capital is about 0.7 of the font's size, and the label adds its outline round it
            var pixels = Math.Max(6, (int)Math.Round(size * 1.15f));
            var labelSize = current?.LabelSize(letter, pixels);

            if (labelSize.HasValue && current.DrawLabel(drawList, letter, center - labelSize.Value / 2, pixels, color)) {
                return;
            }

            var textSize = Widgets.SquareTextSize(letter);
            var (r, g, b, a) = PackedColor.Unpack(color);
            OutlinedText(drawList, letter, center - textSize / 2, new Vector4(r, g, b, a));
        }

        /// <summary>
        /// A $ drawn with lines, not a font (ImGui's would be stretched and blurred at these sizes): an S of two
        /// half-ellipses and a bar down the middle, each black under a thinner coloured one for the outline. The bar runs
        /// well past the S at both ends, so the $ doesn't read as an S.
        /// </summary>
        private static void DrawDollar(ImDrawListPtr drawList, Vector2 at, float size, uint color, uint outline) {
            var thickness = Math.Max(1.5f, size * 0.13f);
            var center = at + new Vector2(size / 2, size / 2);
            var rx = size * 0.27f;
            var ry = size * 0.17f;

            // the top bowl from its upper right, over the top and down the left to the middle, then the bottom bowl from
            // the middle, round the right and the bottom to its lower left (angles clockwise from the right, y down)
            const int steps = 12;
            var points = new Vector2[steps * 2 - 1];
            var top = center - new Vector2(0, ry);
            var bottom = center + new Vector2(0, ry);

            for (var i = 0; i < steps; i++) {
                var t = i / (float)(steps - 1);
                var upper = (-25 - t * 245) * Math.PI / 180;
                points[i] = top + new Vector2((float)Math.Cos(upper) * rx, (float)Math.Sin(upper) * ry);
            }

            // the bowls meet in the middle: the bottom one starts a step past it, or a point twice over pinches the line
            for (var i = 1; i < steps; i++) {
                var t = i / (float)(steps - 1);
                var lower = (-90 + t * 245) * Math.PI / 180;
                points[steps - 1 + i] = bottom + new Vector2((float)Math.Cos(lower) * rx, (float)Math.Sin(lower) * ry);
            }

            var first = points[0];
            var last = points[points.Length - 1];
            var bar = Math.Max(1.5f, thickness * 0.8f);
            var barTop = new Vector2(center.X, at.Y + bar / 2 + 1);
            var barBottom = new Vector2(center.X, at.Y + size - bar / 2 - 1);

            // every outline first, then every fill, so the S and the bar share one outline round the whole $
            drawList.AddPolyline(ref points[0], points.Length, outline, ImDrawFlags.None, thickness + 2);
            drawList.AddCircleFilled(first, thickness / 2 + 1, outline);
            drawList.AddCircleFilled(last, thickness / 2 + 1, outline);
            drawList.AddLine(barTop - new Vector2(0, 1), barBottom + new Vector2(0, 1), outline, bar + 2);

            drawList.AddPolyline(ref points[0], points.Length, color, ImDrawFlags.None, thickness);
            drawList.AddCircleFilled(first, thickness / 2, color);
            drawList.AddCircleFilled(last, thickness / 2, color);
            drawList.AddLine(barTop, barBottom, color, bar);
        }

        private static uint ColorU32((byte R, byte G, byte B) color) {
            return ImGui.GetColorU32(new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, 1));
        }
    }
}
