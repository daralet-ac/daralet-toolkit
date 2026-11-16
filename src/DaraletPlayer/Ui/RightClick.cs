using System;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Right-clicking a game object in the plugin's windows (an item in a list or on a hotbar, a creature on the radar, a
    /// fellow, a dungeon marker): with SettingsData.RightClickAppraises on, it appraises the object at once, and the
    /// object's right-click menu opens only while MenuKey is held (Alt to start; the user's call, November 2026). Off, a
    /// right-click opens the menu as ever. Right-clicks on anything else (a window's own menu, a rule, a place on the map)
    /// are left alone.
    /// </summary>
    internal static class RightClick {
        /// <summary>Whether a right-click on an object appraises it now, rather than opening its menu. Set by PlayerHud.</summary>
        public static Func<bool> AppraisesNow;

        public static bool Appraises => AppraisesNow?.Invoke() == true;

        /// <summary>
        /// In place of ImGui.BeginPopupContextItem(popupId) for the last item, an object: a right-click on it appraises it
        /// (appraise) or opens the menu, as Appraises says; true while the menu is open, to be drawn and ended as
        /// BeginPopupContextItem's. It opens on the button's release over the item, as BeginPopupContextItem does.
        /// </summary>
        public static bool BeginItemMenu(string popupId, Action appraise) {
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Right)
                && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByPopup)) {
                if (appraise != null && Appraises) {
                    Appraise(appraise);
                }
                else {
                    ImGui.OpenPopup(popupId);
                }
            }

            return ImGui.BeginPopup(popupId);
        }

        /// <summary>
        /// For a right-click the caller has seen on an object, where a menu would open: appraises it, and true, when
        /// Appraises says so; false, and the caller opens its menu.
        /// </summary>
        public static bool Appraised(Action appraise) {
            if (appraise == null || !Appraises) {
                return false;
            }

            Appraise(appraise);
            return true;
        }

        private static void Appraise(Action appraise) {
            try {
                appraise();
            }
            catch (Exception ex) {
                PluginCore.Log(ex);
            }
        }
    }
}
