using System;
using System.Numerics;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A tool used on something out in the world (a key on a door or chest, a lockpick, a kit on someone): with the
    /// reticle up, a click outside every plugin window picks what's under it, as the game's own reticle does. A plugin
    /// can't ask the game what's there, but the click reaches the game too, which selects it: whatever it selects is the
    /// target. The selection is cleared as the button goes down, unless the press itself already changed it, so a click on
    /// the thing already selected still counts, and a click on nothing picks nothing (the tool stays up). No click is
    /// posted to the game, as WorldDrop does: one straight after the player's own would be a double-click, which opens the
    /// door or chest instead.
    /// </summary>
    internal static partial class ItemUser {
        /// <summary>How long the game has to select what was clicked, after the button comes up.</summary>
        private static readonly TimeSpan PickWait = TimeSpan.FromMilliseconds(300);

        /// <summary>How far the mouse may move between press and release for it to be a click, not a turn of the camera.</summary>
        private const float ClickSlop = 5;

        /// <summary>Where the left button went down outside every window while targeting, or null.</summary>
        private static Vector2? pressedOutside;

        /// <summary>What was selected before the press, put back if the press turns out to be a drag.</summary>
        private static int selectedBeforePress;

        /// <summary>When the button came up, while waiting for the game to select something.</summary>
        private static DateTime? pickClickedAt;

        /// <summary>The game's selection at the end of the last frame looked at, to tell a press that changed it.</summary>
        private static int lastSelection;

        private static int worldPickFrame = -1;

        /// <summary>Once a frame while targeting: follows a click in the world, and uses the tool on what it selects.</summary>
        private static void StepWorldPick() {
            var frame = ImGui.GetFrameCount();

            if (worldPickFrame == frame) {
                return;
            }

            worldPickFrame = frame;
            var actions = CoreManager.Current.Actions;

            if (!Targeting) {
                pressedOutside = null;
                pickClickedAt = null;
                lastSelection = actions.CurrentSelection;
                return;
            }

            if (pickClickedAt.HasValue) {
                FinishWorldPick();
                lastSelection = actions.CurrentSelection;
                return;
            }

            var mouse = ImGui.GetMousePos();
            var overWindow = ImGui.IsWindowHovered(ImGuiHoveredFlags.AnyWindow
                | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem | ImGuiHoveredFlags.AllowWhenBlockedByPopup);

            // the click that raised the reticle (a double-click's second) isn't a pick
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left) && frame != startedFrame && !overWindow) {
                pressedOutside = mouse;
                selectedBeforePress = lastSelection;

                // a press that hasn't selected anything new yet: cleared, so what the release selects tells
                if (actions.CurrentSelection == lastSelection) {
                    actions.SelectItem(0);
                }
            }

            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left) && pressedOutside.HasValue) {
                var pressedAt = pressedOutside.Value;
                pressedOutside = null;

                if (Vector2.Distance(pressedAt, mouse) <= ClickSlop) {
                    pickClickedAt = DateTime.UtcNow;
                }
                else if (actions.CurrentSelection == 0 && selectedBeforePress != 0) {
                    // a drag (turning the camera, say): the selection it cleared goes back
                    actions.SelectItem(selectedBeforePress);
                }
            }

            lastSelection = actions.CurrentSelection;
        }

        private static void FinishWorldPick() {
            var target = CoreManager.Current.Actions.CurrentSelection;

            if (target == 0 && DateTime.UtcNow - pickClickedAt.Value < PickWait) {
                return;
            }

            pickClickedAt = null;

            if (target == 0 || target == tool) {
                if (target == 0 && selectedBeforePress != 0) {
                    CoreManager.Current.Actions.SelectItem(selectedBeforePress);
                }


                PluginCore.LogFile($"[use] {toolName} ({tool:X8}): nothing picked in the world");
                return;
            }

            var me = CoreManager.Current.CharacterFilter.Id;
            var item = CoreManager.Current.WorldFilter[target];

            if (target == me) {
                UseOn(me, UseRules.Targets(toolUsable, UsePlace.Self) && (toolTargetType & UseRules.CreatureType) != 0);
                return;
            }

            var place = item == null ? UsePlace.Remote : PlaceOf(item);
            UseOn(target, MayUseOn(target, place) || (place == UsePlace.Remote && MayUseOn(target, UsePlace.Viewed)));
        }

        /// <summary>
        /// Whether the tool may be used on something in the world: the place is one its flags allow, and the target's kind
        /// is one it works on, unless the client wasn't told one of the two (left to the server, which checks anyway).
        /// </summary>
        private static bool MayUseOn(int target, UsePlace place) {
            var item = CoreManager.Current.WorldFilter[target];

            if (!UseRules.Targets(toolUsable, place)) {
                return false;
            }

            var itemType = item == null ? 0 : ItemType(item);
            return toolTargetType == 0 || itemType == 0 || (toolTargetType & itemType) != 0;
        }
    }
}
