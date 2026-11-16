using System;
using System.Collections.Generic;
using Decal.Adapter;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Hide game chat (ChatSettings.HideGameChat): the game's main chat window, which has its chat bar, and its four
    /// floating chat windows hidden (UIElement::SetVisible, ClientElements.SetVisible, by the ids Decal's UIElementType
    /// names), looked at again once a second in case the game shows one itself, and shown again as they were when it's
    /// turned off or the plugin stops. Nothing of it is saved: the game sets its UI up afresh at each login.
    /// Enter still opens the game's chat bar, hidden or not, so while Hide game chat (or Enter types here) is on, the
    /// moment the chat bar takes the keyboard (Decal's ChatState) it's taken from it (ClientElements.ClearFocus) and the
    /// main chat window's box gets it instead.
    /// </summary>
    internal sealed partial class ChatWindows {
        private static readonly TimeSpan ReapplyEvery = TimeSpan.FromSeconds(1);

        /// <summary>
        /// How soon after a mouse button the chat bar taking the keyboard counts as the click's doing, not a key's: closing
        /// one of the game's dialogs (the salvage refinery's info box, its Okay clicked) gives the keyboard back to the chat
        /// bar, which opened the plugin's box as Enter would.
        /// </summary>
        private static readonly TimeSpan ClickCausedWithin = TimeSpan.FromMilliseconds(500);

        private const int LeftButton = 0x01;
        private const int RightButton = 0x02;

        /// <summary>Which of the game's chat windows were showing when they were hidden, to show those again.</summary>
        private readonly Dictionary<uint, bool> gameChatWasShown = new Dictionary<uint, bool>();

        private bool gameChatHidden;
        private DateTime gameChatCheckAt;
        private bool chatBarWasActive;

        /// <summary>Whether the chat bar has been looked at since login (or since taking it was turned on).</summary>
        private bool chatBarSeen;
        private bool enterLogged;
        private bool otherBoxLogged;
        private bool clickLogged;

        /// <summary>When a mouse button was last seen down, from Windows.</summary>
        private DateTime mouseDownAt = DateTime.MinValue;

        private static IEnumerable<uint> GameChatElements() {
            yield return ClientElements.MainChat;

            foreach (var id in ClientElements.FloatingChats) {
                yield return id;
            }
        }

        private void StepGameChat() {
            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                // leaving the world: the game makes its chat windows again at the next login
                if (gameChatHidden) {
                    gameChatHidden = false;
                    gameChatWasShown.Clear();
                    PluginCore.LogFile("[chat] game chat back at logout");
                }

                chatBarWasActive = false;
                chatBarSeen = false;
                return;
            }

            var hide = Data.On && Data.HideGameChat && !HideGameUi.On;

            if (hide && (!gameChatHidden || DateTime.UtcNow >= gameChatCheckAt)) {
                HideGameChat();
            }
            else if (!hide && gameChatHidden && !HideGameUi.On) {
                RestoreGameChat();
            }

            TakeChatBar();
        }

        private void HideGameChat() {
            gameChatCheckAt = DateTime.UtcNow + ReapplyEvery;
            var first = !gameChatHidden;

            foreach (var id in GameChatElements()) {
                var shown = ClientElements.IsShown(id) == true;

                if (first) {
                    gameChatWasShown[id] = shown;
                }

                if (shown) {
                    ClientElements.SetVisible(id, false);
                }
            }

            if (first) {
                gameChatHidden = true;
                PluginCore.LogFile("[chat] game chat hidden (main " + (gameChatWasShown.TryGetValue(ClientElements.MainChat,
                    out var main) && main ? "shown" : "not shown") + " before)");
            }
        }

        /// <summary>The game's chat windows that were showing, shown again.</summary>
        private void RestoreGameChat() {
            if (!gameChatHidden) {
                return;
            }

            gameChatHidden = false;

            if (CoreManager.Current.CharacterFilter.LoginStatus == 3) {
                foreach (var pair in gameChatWasShown) {
                    if (pair.Value) {
                        ClientElements.SetVisible(pair.Key, true);
                    }
                }

                PluginCore.LogFile("[chat] game chat shown again");
            }

            gameChatWasShown.Clear();
        }

        /// <summary>
        /// When the game's chat bar takes the keyboard (Enter, or a key that starts a command), it's taken back and the main
        /// chat window's box gets it: the chat bar may be hidden, and typing blind into it would still send.
        /// </summary>
        private void TakeChatBar() {
            var active = CoreManager.Current.Actions.ChatState;

            if (Hotkeys.IsDown(LeftButton) || Hotkeys.IsDown(RightButton)) {
                mouseDownAt = DateTime.UtcNow;
            }
            // not while the game's UI is hidden for a screenshot, the chat windows with it
            var take = Data.On && (Data.HideGameChat || Data.EnterTypes) && !HideGameUi.On;

            // ChatState is true for any of the game's text boxes: the stack bar's number box lost every key typed in it.
            // Only a box in one of the chat windows is taken (when that can't be told, any is, as before).
            if (take && active && !chatBarWasActive && ClientElements.FocusIsIn(GameChatElements()) == false) {
                take = false;

                if (!otherBoxLogged) {
                    otherBoxLogged = true;
                    PluginCore.LogFile("[chat] a game text box outside the chat windows has the keyboard: left to it");
                }
            }

            // The game's chat bar has the keyboard already when the world comes up, so Enter never made it start taking
            // it, and nothing was taken until a click elsewhere cleared it. So the first look clears it as that click
            // would, without opening the plugin's box: the next Enter opens the chat bar, which is then taken as ever.
            if (!take) {
                chatBarSeen = false;
            }
            else if (!chatBarSeen) {
                chatBarSeen = true;

                if (active && ClientElements.FocusIsIn(GameChatElements()) != false) {
                    var cleared = ClientElements.ClearFocus();
                    active = CoreManager.Current.Actions.ChatState;
                    PluginCore.LogFile($"[chat] the chat bar had the keyboard at the start ({(cleared ? "cleared" : "nothing "
                        + "to clear")}), chat state now {active}");
                    chatBarWasActive = active;
                    return;
                }
            }

            // A click, not a key, gave the chat bar the keyboard (a game dialog closing hands it back): no typing starts.
            // With the game's chat hidden it's cleared, so keys don't go blind into it; shown, it's left to the game.
            if (take && active && !chatBarWasActive && DateTime.UtcNow - mouseDownAt < ClickCausedWithin) {
                var cleared = Data.HideGameChat && ClientElements.ClearFocus();

                if (!clickLogged) {
                    clickLogged = true;
                    PluginCore.LogFile("[chat] the chat bar took the keyboard after a click: no typing started ("
                        + (cleared ? "cleared" : "left to the game") + ")");
                }

                chatBarWasActive = CoreManager.Current.Actions.ChatState;
                return;
            }

            if (take && active && !chatBarWasActive) {
                var cleared = ClientElements.ClearFocus();
                Focus(TypingWindow());

                if (!enterLogged) {
                    enterLogged = true;
                    PluginCore.LogFile($"[chat] took the chat bar ({(cleared ? "cleared" : "nothing to clear")}), chat state "
                        + $"now {CoreManager.Current.Actions.ChatState}");
                }
            }

            chatBarWasActive = active;
        }
    }
}
