using System.Collections.Generic;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Which items you carry or wear are on a hotbar you have showing, so the inventory can mark them, as the game's own
    /// pack marks what's on its shortcut bar.
    /// </summary>
    internal static partial class HotbarItems {
        private static readonly HashSet<int> onBars = new HashSet<int>();
        private static int onBarsFrame = -1;

        /// <summary>
        /// Whether the item is on a hotbar that's showing (any of its pages), for this character. A used-up stack's slot
        /// counts as the stack it would use next, as the bar itself does; a spell isn't an item.
        /// </summary>
        public static bool OnShownBar(SettingsData data, int itemId) {
            if (itemId == 0) {
                return false;
            }

            Gather(data);
            return onBars.Contains(itemId);
        }

        /// <summary>Worked out once a frame, however many items ask.</summary>
        private static void Gather(SettingsData data) {
            var now = ImGui.GetFrameCount();

            if (onBarsFrame == now) {
                return;
            }

            onBarsFrame = now;
            onBars.Clear();

            var character = CoreManager.Current.CharacterFilter.Name ?? "";

            foreach (var bar in data.Hotbars.Bars) {
                // a hidden bar's items aren't anywhere to be seen, so marking them would only puzzle
                if (!data.Windows.TryGetValue($"hotbar-{bar.Id}", out var placement) || placement?.Visible != true) {
                    continue;
                }

                if (!bar.Slots.TryGetValue(character, out var slots) || slots == null) {
                    continue;
                }

                foreach (var slot in slots) {
                    if (slot == null || slot.IsSpell) {
                        continue;
                    }

                    var item = Pick(slot);

                    if (item != null) {
                        onBars.Add(item.Id);
                    }
                }
            }
        }
    }
}
