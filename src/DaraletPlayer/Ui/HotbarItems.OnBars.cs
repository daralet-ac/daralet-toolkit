using System.Collections.Generic;
using Decal.Adapter;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Which items you carry or wear are on a hotbar you have showing, and the key that uses each, so the inventory can
    /// mark them as the game's pack marks what's on its shortcut bar.
    /// </summary>
    internal static partial class HotbarItems {
        /// <summary>Each item on a shown bar, by id, with the key that uses it ("" for none).</summary>
        private static readonly Dictionary<int, string> onBars = new Dictionary<int, string>();
        private static int onBarsFrame = -1;

        /// <summary>
        /// The key that uses the item from a hotbar that's showing (its short label: "1", "S2", "F3"), "" if it's on one
        /// but no key reaches it, or null if it isn't on one, for this character. A used-up stack's slot counts as the
        /// stack it would use next, as the bar itself does; a spell isn't an item.
        /// </summary>
        public static string KeyOnShownBar(SettingsData data, int itemId) {
            if (itemId == 0) {
                return null;
            }

            Gather(data);
            return onBars.TryGetValue(itemId, out var key) ? key : null;
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

                    if (item == null) {
                        continue;
                    }

                    // a place's key uses whichever page is showing, so only a slot on that page is reached by it
                    var key = bar.HotkeysOn && slot.Page == bar.Page ? bar.KeyAt(slot.Row, slot.Col)?.ShortLabel ?? "" : "";

                    // on several places: the first with a key
                    if (!onBars.TryGetValue(item.Id, out var known) || (known.Length == 0 && key.Length > 0)) {
                        onBars[item.Id] = key;
                    }
                }
            }
        }
    }
}
