using System;
using System.Collections.Generic;
using System.Numerics;
using DaraletPlayer.Feed;
using DaraletPlayer.Map;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    internal sealed partial class RadarPanel {
        /// <summary>
        /// How often the blips are gathered from Decal: every frame would be wasted on a crowded town, and a tenth of a
        /// second keeps a running monster smooth enough.
        /// </summary>
        private static readonly TimeSpan BlipRefresh = TimeSpan.FromMilliseconds(100);

        /// <summary>How often your fellowship is asked for while fellows are shown (who's in it).</summary>
        private static readonly TimeSpan FellowshipRefresh = TimeSpan.FromSeconds(5);

        /// <summary>Indoors, how far above or below you a blip is before it's drawn faint: half a floor.</summary>
        private const float OtherFloor = DungeonGeometry.FloorHeight / 2;

        private const float HoverDistance = 7;

        private static readonly uint Outline = MapView.Col(0f, 0f, 0f, 0.85f);
        private static readonly uint SelectedRing = MapView.Col(1f, 1f, 1f, 1f);

        private readonly List<RadarBlip> blips = new List<RadarBlip>();
        private readonly HashSet<uint> fellows = new HashSet<uint>();
        private DateTime nextBlips;
        private readonly HashSet<int> loggedPortalIds = new HashSet<int>();
        private readonly HashSet<int> loggedNeverIds = new HashSet<int>();
        private int loggedPortals;

        /// <summary>
        /// What the radar shows, of what the client knows of (Decal's objects outside any container): each kind ticked,
        /// within the range shown (and never past the game's own radar's), but you.
        /// </summary>
        private void RefreshBlips(uint landcell, Vector2 selfWorld, float range, RadarOptions data) {
            var now = DateTime.UtcNow;

            if (now < nextBlips) {
                return;
            }

            nextBlips = now + BlipRefresh;
            blips.Clear();
            RefreshFellows(data);

            var selfId = CoreManager.Current.CharacterFilter.Id;
            // a little past the edge, so one walking in shows before the next refresh
            var reach = range * 1.1f;

            using (var landscape = CoreManager.Current.WorldFilter.GetLandscape()) {
                foreach (var wo in landscape) {
                    if (wo.Id == selfId) {
                        continue;
                    }

                    var behavior = wo.Values(LongValueKey.Behavior, 0);

                    if (RadarMath.Hidden(behavior)) {
                        continue;
                    }

                    // what the server keeps off the game's radar (invisible creatures, spawners, pets) stays off this one
                    if (ClientRadar.Behavior(wo.Id) == ClientRadar.ShowNever) {
                        LogNeverShown(wo);
                        continue;
                    }

                    var kind = KindOf(wo, behavior);

                    if (kind == null || !data.Shows(RadarKindInfo.For(kind.Value))) {
                        continue;
                    }

                    var cell = unchecked((uint)wo.Values(LongValueKey.Landblock, 0));

                    if (cell == 0) {
                        cell = landcell;
                    }

                    var raw = wo.RawCoordinates();
                    var at = RadarMath.WorldMeters(cell, raw.X, raw.Y);

                    if (Vector2.Distance(at, selfWorld) > reach) {
                        continue;
                    }

                    blips.Add(new RadarBlip {
                        Id = wo.Id, Name = wo.Name, Kind = kind.Value, At = at, Z = (float)raw.Z,
                        ServerColor = data.ServerColors ? ClientRadar.BlipColor(wo.Id) : 0,
                    });
                }
            }
        }

        /// <summary>Your fellows' guids, from the server's fellowship reply, asked for while they're shown.</summary>
        private void RefreshFellows(RadarOptions data) {
            fellows.Clear();

            if (!data.Shows(RadarKindInfo.For(RadarKind.Fellow))) {
                return;
            }

            if (feed.Supports(PlayerFeed.KindFellowship)) {
                feed.RefreshFellowship(FellowshipRefresh);
            }

            var members = feed.Fellowship?.Members;

            if (members == null) {
                return;
            }

            foreach (var member in members) {
                fellows.Add(member.Guid);
            }
        }

        private RadarKind? KindOf(WorldObject wo, int behavior) {
            var itemType = wo.Values(LongValueKey.Category, 0);
            var byFlags = RadarMath.KindByFlags(behavior, itemType);
            LogPortal(wo, behavior, itemType, byFlags);

            if (byFlags != null) {
                return byFlags;
            }

            switch (wo.ObjectClass) {
                case ObjectClass.Monster:
                    return RadarKind.Monster;
                case ObjectClass.Player:
                    return RadarMath.PlayerKind(behavior, fellows.Contains(unchecked((uint)wo.Id)));
                case ObjectClass.Npc:
                case ObjectClass.Vendor:
                    return RadarKind.Npc;
                case ObjectClass.Portal:
                    return RadarKind.Portal;
                case ObjectClass.Lifestone:
                    return RadarKind.Lifestone;
                case ObjectClass.Corpse:
                    return RadarKind.Corpse;
                case ObjectClass.Door:
                    return RadarKind.Door;
                case ObjectClass.Container:
                    return RadarKind.Chest;
                default:
                    return null;
            }
        }

        /// <summary>
        /// The first few portals of a session in log.txt, by their flags or by Decal's class or by name, with what each
        /// says, to check the radar's kinds against (Decal's ObjectClass once showed them as NPCs).
        /// </summary>
        /// <summary>The first few objects a session left off for the server's Show never, in log.txt.</summary>
        private void LogNeverShown(WorldObject wo) {
            if (loggedNeverIds.Count >= 10 || !loggedNeverIds.Add(wo.Id)) {
                return;
            }

            PluginCore.LogFile($"[radar] left off {wo.Name} 0x{wo.Id:X8} ({wo.ObjectClass}): the server says never "
                + "show it");
        }

        private void LogPortal(WorldObject wo, int behavior, int itemType, RadarKind? byFlags) {
            if (loggedPortals >= 5) {
                return;
            }

            var portalish = byFlags == RadarKind.Portal || wo.ObjectClass == ObjectClass.Portal
                || (wo.Name ?? "").IndexOf("portal", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!portalish || !loggedPortalIds.Add(wo.Id)) {
                return;
            }

            loggedPortals++;
            PluginCore.LogFile($"[radar] {wo.Name} 0x{wo.Id:X8}: Decal's class {wo.ObjectClass}, flags 0x{behavior:X8}, "
                + $"item type 0x{itemType:X8}, server colour {ClientRadar.BlipColor(wo.Id)}, shown as "
                + (byFlags?.ToString() ?? "by its class"));
        }

        /// <summary>Draws the blips within the range, the selection ringed. Returns the one under the mouse.</summary>
        private RadarBlip DrawBlips(ImDrawListPtr drawList, Vector2 mid, float radius, float range, Vector2 selfWorld,
            float selfZ, double heading, bool indoors, RadarOptions data, Vector2? mouse) {
            var selected = CoreManager.Current.Actions.CurrentSelection;
            var scale = data.ClampedBlipScale * Math.Max(0.75f, Math.Min(1.5f, radius / 100));
            RadarBlip hover = null;
            var hoverDistance = HoverDistance;

            foreach (var blip in blips) {
                var offset = blip.At - selfWorld;

                // never past the game's own reach: a square radar's corners stay empty
                if (offset.Length() > range) {
                    continue;
                }

                var info = RadarKindInfo.For(blip.Kind);
                var p = mid + RadarMath.ToRadar(offset, range, heading, data.Turn) * radius;
                // the server's own colour for it first, as the game's radar has it (a portal device's purple)
                var color = RadarKindInfo.ServerColor(blip.ServerColor) ?? data.ColorOf(info);

                if (indoors && data.DimOtherFloors && Math.Abs(blip.Z - selfZ) > OtherFloor) {
                    color = PackedColor.Fade(color, 0.35f);
                }

                DrawShape(drawList, p, info.Shape, 3 * scale, color);

                if (blip.Id == selected) {
                    drawList.AddCircle(p, 6 * scale, Outline, 16, 3);
                    // in the blip's own colour, solid, as the game's radar rings it; or white, if asked for
                    var ring = data.WhiteSelection ? SelectedRing : color | 0xFF000000;
                    drawList.AddCircle(p, 6 * scale, ring, 16, 1.5f);
                }

                if (!mouse.HasValue) {
                    continue;
                }

                var distance = Vector2.Distance(mouse.Value, p);

                if (distance <= hoverDistance) {
                    hover = blip;
                    hoverDistance = distance;
                }
            }

            return hover;
        }

        private static void DrawShape(ImDrawListPtr drawList, Vector2 p, RadarShape shape, float size, uint color) {
            switch (shape) {
                case RadarShape.Square:
                    drawList.AddRectFilled(p - new Vector2(size + 1), p + new Vector2(size + 1), Outline);
                    drawList.AddRectFilled(p - new Vector2(size), p + new Vector2(size), color);
                    break;
                case RadarShape.Diamond: {
                    var big = size * 1.4f;
                    drawList.AddQuadFilled(p + new Vector2(0, -big - 1.5f), p + new Vector2(big + 1.5f, 0),
                        p + new Vector2(0, big + 1.5f), p + new Vector2(-big - 1.5f, 0), Outline);
                    drawList.AddQuadFilled(p + new Vector2(0, -big), p + new Vector2(big, 0), p + new Vector2(0, big),
                        p + new Vector2(-big, 0), color);
                    break;
                }
                case RadarShape.Ring:
                    drawList.AddCircle(p, size + 1.5f, Outline, 16, 3.5f);
                    drawList.AddCircle(p, size + 1.5f, color, 16, 2);
                    drawList.AddCircleFilled(p, size * 0.4f, color);
                    break;
                case RadarShape.Cross: {
                    var arm = size + 1.5f;
                    drawList.AddLine(p - new Vector2(arm, 0), p + new Vector2(arm, 0), Outline, 4);
                    drawList.AddLine(p - new Vector2(0, arm), p + new Vector2(0, arm), Outline, 4);
                    drawList.AddLine(p - new Vector2(arm, 0), p + new Vector2(arm, 0), color, 2);
                    drawList.AddLine(p - new Vector2(0, arm), p + new Vector2(0, arm), color, 2);
                    break;
                }
                case RadarShape.X: {
                    var arm = size;
                    drawList.AddLine(p - new Vector2(arm), p + new Vector2(arm), Outline, 4);
                    drawList.AddLine(p + new Vector2(-arm, arm), p + new Vector2(arm, -arm), Outline, 4);
                    drawList.AddLine(p - new Vector2(arm), p + new Vector2(arm), color, 2);
                    drawList.AddLine(p + new Vector2(-arm, arm), p + new Vector2(arm, -arm), color, 2);
                    break;
                }
                default:
                    drawList.AddCircleFilled(p, size + 1, Outline, 12);
                    drawList.AddCircleFilled(p, size, color, 12);
                    break;
            }
        }

        /// <summary>A click on a blip selects it; a right-click anywhere opens the window's menu.</summary>
        private void HandleClicks(bool hovered, RadarBlip hover) {
            if (!hovered) {
                return;
            }

            if (hover != null && ImGui.IsMouseClicked(ImGuiMouseButton.Left)) {
                CoreManager.Current.Actions.SelectItem(hover.Id);
            }

            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right)) {
                Window.OpenMenu();
            }
        }

        private static void BlipTooltip(RadarBlip blip, Vector2 selfWorld) {
            Widgets.BeginTooltip();
            Widgets.Text(blip.Name);
            Widgets.TextDisabled($"{RadarKindInfo.For(blip.Kind).Label}, {Vector2.Distance(blip.At, selfWorld):0} m. "
                + "Click to select.");
            ImGui.EndTooltip();
        }
    }
}
