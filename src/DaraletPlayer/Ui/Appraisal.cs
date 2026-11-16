using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Appraising an item, from its right-click menu in the plugin's item lists (the Corpse window, the bank). With Plugin
    /// appraisal on (SettingsData.AppraiseInPlugin), the plugin's Appraisal windows show it: Appraise in the first, Appraise in
    /// new window in another, so several can be open at once. Otherwise the game's own appraisal panel opens, through the
    /// client's own notice, as its magnifier does (ClientToolbar.Examine: Decal's RequestId only sends the request, and the
    /// client shows only appraisals it started); if that can't be called, the item is selected for the game's appraise key.
    /// A right-click on an object appraises it straight away (RightClick).
    /// </summary>
    internal static class Appraisal {
        /// <summary>The most windows open at once: past this, a new one reuses the oldest extra.</summary>
        private const int MaxWindows = 6;

        /// <summary>Each new window opens this far down and right of the last, so they don't sit on top of each other.</summary>
        private static readonly Vector2 Cascade = new Vector2(28, 28);
        private static readonly Vector2 FirstPosition = new Vector2(600, 150);

        private static readonly List<AppraisalWindow> windows = new List<AppraisalWindow>();
        private static PlayerFeed feed;
        private static Settings settings;
        private static ItemIcons icons;

        /// <summary>The once-a-session reminder that the game's appraisal is the appraise key.</summary>
        private static bool hinted;

        /// <summary>Made by PlayerHud, with what the windows need; the first window is made now.</summary>
        public static void Start(PlayerFeed playerFeed, Settings playerSettings, ItemIcons itemIcons) {
            feed = playerFeed;
            settings = playerSettings;
            icons = itemIcons;
            windows.Add(new AppraisalWindow(feed, settings, icons, "appraisal", "Appraisal", FirstPosition));
        }

        /// <summary>The first window, the one Appraise reuses (it shares the appraisals' window, AppraisalGroup).</summary>
        public static FloatingWindow FirstWindow => windows.FirstOrDefault()?.Window;

        /// <summary>Closes every window, for the plugin shutting down: a right-click after this does nothing.</summary>
        public static void Stop() {
            foreach (var window in windows) {
                try {
                    window.Dispose();
                }
                catch (Exception ex) {
                    PluginCore.Log(ex);
                }
            }

            windows.Clear();
            feed = null;
        }

        /// <summary>Whether items are appraised in the plugin's Appraisal window (Plugin appraisal), not the game's panel.</summary>
        public static bool InPlugin => settings?.Data.AppraiseInPlugin != false && feed != null;

        /// <summary>
        /// Whether the server describes items that aren't yours (on the ground, wielded or held by someone else), so the
        /// Appraisal window can show them; an older server only describes your own.
        /// </summary>
        public static bool DescribesWorldItems => feed?.HasFeature(PlayerFeed.FeatureWorldDescribe) == true;

        /// <summary>Whether the first Appraisal window is on screen showing this item.</summary>
        public static bool Showing(int id) {
            var first = windows.FirstOrDefault();
            return first != null && first.Window.Shown && first.Guid == unchecked((uint)id);
        }

        /// <summary>Closes the first Appraisal window, for an appraisal the game's panel shows instead.</summary>
        public static void DismissFirst() {
            var window = FirstWindow;

            if (window != null && window.Shown) {
                window.Dismiss();
            }
        }

        /// <param name="described">The item as a corpse described it, for one in a corpse; null for one you carry or have
        /// banked, which the Appraisal window has the server describe.</param>
        /// <param name="newWindow">In a window of its own, leaving the others as they are.</param>
        /// <param name="slots">Where it's worn (EquipMask), for an item the client doesn't have: one linked in chat.</param>
        /// <param name="linkedBy">Who linked it in chat, for one that was.</param>
        public static void Show(uint guid, CorpseItemInfo described = null, bool newWindow = false, int slots = 0,
            string linkedBy = null) {
            if (guid == 0) {
                return;
            }

            if (InPlugin) {
                (newWindow ? FreeWindow() : windows[0]).Show(guid, described, slots, linkedBy);
                return;
            }

            // someone else's item, linked in chat: there's nothing in the world to select for the game's appraisal
            if (linkedBy != null) {
                PluginCore.Chat("Plugin appraisal is off, so a linked item shows only when hovered. (Plugin appraisal is on "
                    + "the Daralet Toolkit window's top line.)");
                return;
            }

            // the game's own appraisal panel, as its magnifier opens it (through ClientExamine's routing first, which closes
            // the plugin's appraisal windows); selecting it for the appraise key if that fails
            if (ClientExamine.TryTakeOver(unchecked((int)guid)) || ClientToolbar.Examine(unchecked((int)guid))) {
                return;
            }

            CoreManager.Current.Actions.SelectItem(unchecked((int)guid));

            if (!hinted) {
                hinted = true;
                PluginCore.Chat("Selected it: press your appraise key for the game's appraisal. "
                    + "(Plugin appraisal, on the Daralet Toolkit window's top line, shows it in the plugin instead.)");
            }
        }

        /// <summary>
        /// Appraises anything in the world by its id, as the Toolbar window's magnifier and a right-click do, where
        /// ClientExamine's routing (CreatureAppraisalPanel.Route, which the game's own appraisals go through too, but which a
        /// call straight to the game's notice, as this makes, skips) sends it: an item in the Appraisal window while Plugin
        /// appraisal is on; anything else in the world in Creature Appraisal while it Replaces the game's; the rest in the
        /// game's own appraisal panel.
        /// </summary>
        public static void Object(int id) {
            if (id == 0 || ClientExamine.TryTakeOver(id)) {
                return;
            }

            var known = CoreManager.Current.WorldFilter[id];

            // no routing (the Creature Appraisal panel isn't made): items still go to the plugin's window
            if (!ClientExamine.Routing && InPlugin && known != null && IsItem(known.ObjectClass)) {
                Show(unchecked((uint)id));
                return;
            }

            ClientToolbar.Examine(id);
        }

        /// <summary>Whether the plugin's Appraisal window appraises this kind: not a creature or anything in the world.</summary>
        public static bool IsItem(ObjectClass kind) {
            switch (kind) {
                case ObjectClass.Monster:
                case ObjectClass.Player:
                case ObjectClass.Npc:
                case ObjectClass.Vendor:
                case ObjectClass.Door:
                case ObjectClass.Portal:
                case ObjectClass.Lifestone:
                case ObjectClass.Corpse:
                case ObjectClass.Housing:
                case ObjectClass.Unknown:
                    return false;
                default:
                    return true;
            }
        }

        /// <summary>
        /// A window for Appraise in new window: an extra one that's closed, or a new one, or, with MaxWindows open, the oldest
        /// extra. Window titles are numbered, since UtilityBelt knows a window by its title.
        /// </summary>
        private static AppraisalWindow FreeWindow() {
            var free = windows.Skip(1).FirstOrDefault(w => !w.InUse);

            if (free != null) {
                return free;
            }

            if (windows.Count >= MaxWindows) {
                return windows[1];
            }

            var number = windows.Count + 1;
            var position = FirstPosition + Cascade * (number - 1);
            var window = new AppraisalWindow(feed, settings, icons, $"appraisal-{number}", $"Appraisal {number}", position);
            windows.Add(window);
            return window;
        }

        /// <summary>
        /// The Appraise items of an item's right-click menu, inside a menu already begun (ImGui.BeginPopup...): Appraise, and
        /// Appraise in new window with Plugin appraisal on.
        /// </summary>
        /// <param name="slots">Where it's worn, for an item Decal can't say of (a vendor's): the compare goes by it.</param>
        public static void MenuItems(uint guid, CorpseItemInfo described = null, int slots = 0) {
            if (ImGui.MenuItem("Appraise")) {
                Show(guid, described, slots: slots);
            }

            if (InPlugin && ImGui.MenuItem("Appraise in new window")) {
                Show(guid, described, newWindow: true, slots: slots);
            }
        }
    }
}
