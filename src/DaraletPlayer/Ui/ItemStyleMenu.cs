using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Style, in an item's right-click menu (the inventory lists, a hotbar slot): the server's /style, for the item
    /// right-clicked. /style works on the item last examined, which anything appraised in between would change, so the
    /// menu asks @playerui style, which names the item: opening the menu lists its looks, and picking one switches it.
    /// </summary>
    public static class ItemStyleMenu {
        /// <summary>Draw inside a menu already begun, for an item you carry or wear. Nothing on a server without it.</summary>
        public static void MenuItem(PlayerFeed feed, uint guid) {
            if (feed == null || guid == 0 || !feed.Supports(PlayerFeed.KindStyle)) {
                return;
            }

            if (!ImGui.BeginMenu("Style")) {
                Widgets.Tip("Switches its look, if it has more than one: what /style does.");
                return;
            }

            feed.ItemStyles.TryGetValue(guid, out var known);

            // asked each time the menu opens (a quest can unlock a look), and again if an answer never came
            if (ImGui.IsWindowAppearing() || (known == null && !feed.IsWaitingFor(PlayerFeed.KindStyle))) {
                feed.AskStyles(guid);
            }

            if (known == null) {
                Widgets.TextDisabled("Looking...");
            }
            else if (known.Problem != null) {
                Widgets.TextDisabled(known.Problem);
            }
            else if (known.Styles == null || known.Styles.Count < 2) {
                Widgets.TextDisabled("It has no other look you can use.");
            }
            else {
                for (var i = 0; i < known.Styles.Count; i++) {
                    var style = known.Styles[i];
                    var label = (style.Earned ? style.Name + " *" : style.Name) + "##style-" + i;

                    if (ImGui.MenuItem(label, null, style.Showing) && !style.Showing) {
                        feed.ChooseStyle(guid, i + 1);
                    }

                    if (style.Earned) {
                        Widgets.Tip("A look you earned with a quest.");
                    }
                }
            }

            ImGui.EndMenu();
        }
    }
}
