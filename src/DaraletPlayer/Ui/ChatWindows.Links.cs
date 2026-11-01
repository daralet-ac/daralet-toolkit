using System;
using System.Reflection;
using Decal.Adapter;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Links in the chat windows. A link the game or a plugin put in a line is handed to Decal's ChatNameClicked, as a click
    /// on it in the game's own chat would be, so every plugin's links work here as there (the plugin's own included,
    /// PluginCore.Core_ChatNameClicked); a player's name nobody took starts a tell to them, as the game does. Decal raises
    /// that event only from its hook on the game's chat, so it's raised here by calling the handler the hook calls
    /// (CoreManager's private myHooks_ChatClickIntercept), found by reflection.
    ///
    /// Items are linked with Link in chat in an item's right-click menu: @playerui shareitem keeps it on the server, and
    /// "[Name]" goes in the box you're typing in (ChatItemLink).
    /// </summary>
    internal sealed partial class ChatWindows {
        private static MethodInfo clickHandler;
        private static bool clickHandlerLookedUp;

        /// <summary>A click on a link: ChatNameClicked first, then a tell for a name nobody took.</summary>
        public void ClickLink(ChatSegment link, ChatView from) {
            if (link == null) {
                return;
            }

            if (RaiseNameClicked(link.LinkText, link.LinkId)) {
                return;
            }

            if (Data.ClickNames && ChatMarkup.LooksLikeName(link.LinkText)) {
                from.StartTell(link.LinkText.TrimStart('+'));
            }
        }

        /// <summary>
        /// Decal's ChatNameClicked, as a click in the game's chat raises it. True when a plugin took the click.
        /// </summary>
        private static bool RaiseNameClicked(string text, int id) {
            if (!clickHandlerLookedUp) {
                clickHandlerLookedUp = true;
                clickHandler = typeof(CoreManager).GetMethod("myHooks_ChatClickIntercept",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                if (clickHandler == null) {
                    PluginCore.LogFile("[chat] Decal's ChatNameClicked can't be raised: links of other plugins won't work in "
                        + "the chat windows");
                }
            }

            if (clickHandler == null) {
                return false;
            }

            try {
                var arguments = new object[] { text ?? "", id, false };
                clickHandler.Invoke(CoreManager.Current, arguments);
                return arguments[2] is bool eaten && eaten;
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[chat] ChatNameClicked: {ex.InnerException?.Message ?? ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Link in chat, in an item's right-click menu (the inventory lists, the bank, a hotbar slot), when the server can.
        /// Draw inside a menu already begun.
        /// </summary>
        public static void ShareMenuItem(uint guid) {
            var current = Current;

            if (current == null || guid == 0 || !current.feed.Supports(PlayerFeed.KindShareItem)) {
                return;
            }

            if (ImGui.MenuItem("Link in chat")) {
                current.Share(guid);
            }
            Widgets.Tip("Puts a link to this item in the chat box you're typing in, to send. Anyone with Daralet Toolkit "
                + "sees the item, every stat as it is now, when they hover the link; everyone else sees its name. Links "
                + "last a day.");
        }

        private void Share(uint guid) {
            if (!feed.ShareItem(guid)) {
                PluginCore.Chat("The item couldn't be linked just now: try again in a moment.");
            }
        }

        /// <summary>The server's answer to Link in chat: the link goes in the chat box, or onto the clipboard.</summary>
        private void Feed_ItemShared(ShareReplyInfo reply) {
            if (disposed || reply == null) {
                return;
            }

            if (reply.Shared == null) {
                PluginCore.Chat(reply.Problem ?? "The item couldn't be linked.");
                return;
            }

            var name = reply.Shared.Name;
            var link = $"[{name}]";

            if (Data.On) {
                Focus(TypingWindow(), link);
                return;
            }

            if (SystemClipboard.Set(link)) {
                PluginCore.Chat($"A link to {name} is on the clipboard ({link}): paste it into chat. Players "
                    + "with Daralet Toolkit see the item when they hover it.");
            }
            else {
                PluginCore.Chat($"Type this in chat to link the item: {link}");
            }
        }
    }
}
