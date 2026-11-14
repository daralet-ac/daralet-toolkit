using System;
using System.Numerics;
using DaraletPlayer.Feed;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// The character at the top of both pages, as the game's (layout 0x21000045's 0x10000230, 300x105): name, then gender,
    /// race and template, then player killer status, centred; Total Experience and the XP bar to the next level (the meter
    /// 0x10000236, its track 060011A6 and red fill 060011A5) under them; and on the right, past a gold divider (06004CB8),
    /// "Character Level" and the level, big.
    /// </summary>
    internal sealed partial class AttributesPanel {
        /// <summary>The server's word on the level's bounds (PlayerFeed.KindXp) is asked for no oftener than this.</summary>
        private static readonly TimeSpan XpRefresh = TimeSpan.FromSeconds(5);

        /// <summary>PropertyInt 134, PlayerKillerStatus, and its PK and PK Lite bits.</summary>
        private const int PlayerKillerStatus = 134;
        private const int PlayerKiller = 0x4;
        private const int PlayerKillerLite = 0x40;

        /// <summary>The game's level box is 65 wide past a 5-pixel divider.</summary>
        private const float LevelBoxWidth = 70;

        // packed by hand: ImGui.GetColorU32 crashes before ImGui has a context
        private static readonly uint LevelColor = PackedColor.Pack(235, 222, 150);
        private static readonly uint BarText = PackedColor.Pack(255, 255, 255);
        private static readonly uint BarOutline = PackedColor.Pack(0, 0, 0, 220);
        private static readonly uint ModernFill = PackedColor.Pack(170, 40, 30);

        private void RenderHeader(bool retail) {
            var character = CoreManager.Current.CharacterFilter;
            feed.Refresh(PlayerFeed.KindXp, XpRefresh);

            var avail = ImGui.GetContentRegionAvail().X;
            var boxWidth = Widgets.Scaled(LevelBoxWidth);
            var leftWidth = Math.Max(Widgets.Scaled(120), avail - boxWidth - ImGui.GetStyle().ItemSpacing.X);

            ImGui.BeginGroup();
            GamePanelArt.Centred(character.Name, leftWidth, Colors.Text);
            GamePanelArt.Centred($"{character.Gender} {character.Race} {character.ClassTemplate}".Trim(), leftWidth,
                Colors.Detail);
            GamePanelArt.Centred(PlayerKillerText(), leftWidth, Colors.Detail);

            GamePanelArt.LeftRight("Total Experience (XP):", AttributesOptions.Xp(character.TotalXP), leftWidth,
                Colors.Text);
            RenderXpBar(character.TotalXP, leftWidth, retail);
            ImGui.EndGroup();

            var height = ImGui.GetItemRectSize().Y;
            ImGui.SameLine();
            RenderLevelBox(character.Level, boxWidth, height, retail);
        }

        /// <summary>Player Killer, Player Killer Lite or Non-Player Killer, as the game's panel says.</summary>
        private static string PlayerKillerText() {
            var status = CoreManager.Current.CharacterFilter.GetCharProperty(PlayerKillerStatus);
            return (status & PlayerKiller) != 0 ? "Player Killer"
                : (status & PlayerKillerLite) != 0 ? "Player Killer Lite" : "Non-Player Killer";
        }

        /// <summary>
        /// The bar through your level, with "XP for next level:" and what's left on it: the level's bounds are the server's
        /// (@playerui xp, as the XP bar has them), Infinity! at the top level as the game says; without them, Decal's count
        /// and an empty bar.
        /// </summary>
        private void RenderXpBar(long total, float width, bool retail) {
            var xp = feed.Xp;
            var fraction = 0f;
            string left;

            if (xp != null) {
                fraction = xp.MaxLevel ? 1f : xp.LevelCost == 0 ? 0f : (float)xp.IntoLevel(total) / xp.LevelCost;
                left = xp.MaxLevel ? "Infinity!" : AttributesOptions.Xp(xp.ToNextLevel(total));
            }
            else {
                left = AttributesOptions.Xp(CoreManager.Current.CharacterFilter.XPToNextLevel);
            }

            var height = Math.Max(Widgets.Scaled(17), ImGui.GetTextLineHeight() + 2);
            var min = ImGui.GetCursorScreenPos();
            var max = min + new Vector2(width, height);
            var drawList = ImGui.GetWindowDrawList();
            var cut = (float)Math.Round(min.X + width * PowerBarOptions.Clamp(fraction));

            if (!retail || !ItemIcons.DrawArtBox(drawList, "xp-track", min, max)) {
                drawList.AddRectFilled(min, max, ImGui.GetColorU32(ImGuiCol.FrameBg));

                if (cut > min.X) {
                    drawList.AddRectFilled(min, new Vector2(cut, max.Y), ModernFill);
                }
            }
            else if (cut > min.X) {
                drawList.PushClipRect(min, new Vector2(cut, max.Y), true);
                ItemIcons.DrawArtBox(drawList, "xp-fill", min, max);
                drawList.PopClipRect();
            }

            var y = (float)Math.Round(min.Y + (height - ImGui.GetTextLineHeight()) / 2);
            Outlined(drawList, "XP for next level:", new Vector2(min.X + 3, y));
            var leftSize = ImGui.CalcTextSize(left);
            Outlined(drawList, left, new Vector2(max.X - leftSize.X - 3, y));

            ImGui.Dummy(new Vector2(width, height));
            Widgets.Tip(xp == null ? "XP to your next level, as Decal has it." : xp.MaxLevel ? "You're at the top level."
                : $"{AttributesOptions.Xp(xp.IntoLevel(total))} of {AttributesOptions.Xp(xp.LevelCost)} through level "
                    + $"{xp.Level}.");
        }

        /// <summary>The gold divider, "Character Level" and the level, big, as the game's box on the right.</summary>
        private void RenderLevelBox(int level, float width, float height, bool retail) {
            var min = ImGui.GetCursorScreenPos();
            var drawList = ImGui.GetWindowDrawList();
            var divider = Widgets.Scaled(5);

            if (retail) {
                ItemIcons.DrawArtBox(drawList, "panel-divider", min, new Vector2(min.X + divider, min.Y + height));
            }
            else {
                drawList.AddLine(min, new Vector2(min.X, min.Y + height), ImGui.GetColorU32(ImGuiCol.Separator));
            }

            var inner = new Vector2(min.X + divider, min.Y);
            var innerWidth = width - divider;
            var line = ImGui.GetTextLineHeight();
            CentredAt(drawList, "Character", inner, innerWidth, ImGui.GetColorU32(ImGuiCol.Text));
            CentredAt(drawList, "Level", inner + new Vector2(0, line), innerWidth, ImGui.GetColorU32(ImGuiCol.Text));

            // the level as big as the room left: GDI+ text at its own size (ItemIcons.DrawLabel), ImGui's stretched if not
            var text = level.ToString();
            var room = Math.Max(line, height - line * 2);
            // as tall as the room allows, then narrowed until the label as drawn (its bold Segoe UI is much wider than
            // ImGui's font: an estimate from ImGui's measure ran "999" off the box) fits the box's width
            var maxWidth = innerWidth - Widgets.Scaled(6);
            var pixels = (int)Math.Max(10, room * 0.9f);
            var size = LevelSize(text, pixels);

            for (var tries = 0; tries < 4 && size.X > maxWidth && pixels > 10; tries++) {
                pixels = (int)Math.Max(10, Math.Floor(pixels * maxWidth / size.X));
                size = LevelSize(text, pixels);
            }

            var at = new Vector2((float)Math.Round(inner.X + Math.Max(0, (innerWidth - size.X) / 2)),
                (float)Math.Round(inner.Y + line * 2 + (room - size.Y) / 2));

            // and kept inside the box, whatever happens
            drawList.PushClipRect(inner, inner + new Vector2(innerWidth, height), true);

            if (!icons.DrawLabel(drawList, text, at, pixels, LevelColor, outlined: false)) {
                drawList.AddText(ImGui.GetFont(), pixels, at, LevelColor, text);
            }

            drawList.PopClipRect();

            ImGui.Dummy(new Vector2(width, height));
        }

        /// <summary>How big the level is drawn at this size: the label's own size, or ImGui's text's when there's no label.</summary>
        private Vector2 LevelSize(string text, int pixels) {
            return icons.LabelSize(text, pixels, outlined: false) ?? ImGui.CalcTextSize(text) * (pixels / ImGui.GetFontSize());
        }

        private static void CentredAt(ImDrawListPtr drawList, string text, Vector2 at, float width, uint color) {
            var size = ImGui.CalcTextSize(text).X;
            drawList.AddText(new Vector2((float)Math.Round(at.X + Math.Max(0, (width - size) / 2)), at.Y), color, text);
        }

        private static void Outlined(ImDrawListPtr drawList, string text, Vector2 at) {
            foreach (var offset in new[] { new Vector2(-1, 0), new Vector2(1, 0), new Vector2(0, -1), new Vector2(0, 1) }) {
                drawList.AddText(at + offset, BarOutline, text);
            }

            drawList.AddText(at, BarText, text);
        }
    }
}
