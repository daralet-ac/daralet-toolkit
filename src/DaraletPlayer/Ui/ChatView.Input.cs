using System;
using System.Collections.Generic;
using System.Text;
using DaraletPlayer.Feed;
using ImGuiNET;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// A chat window's box to type in: who it goes to on its left (its talk focus, as the game's chat options box picks one:
    /// ChatTalk), its options on the right. Enter sends and, unless Keep typing is ticked, hands the keyboard back to the
    /// game, as the game's chat bar does; Escape leaves it. Up and Down go through what you've sent. Text put in while it's
    /// being typed in (a linked item) goes in through ImGui's callback, since ImGui keeps its own copy of the box's text
    /// while it's active.
    /// </summary>
    internal sealed unsafe partial class ChatView {
        private const int MaxHistory = 50;
        private const uint MaxLength = 500;

        private readonly List<string> sent = new List<string>();
        private readonly ImGuiInputTextCallback callback;

        private string input = "";
        private int historyAt = -1;
        private string historyDraft;
        private bool focusNext;
        private bool inputActive;

        /// <summary>Text waiting to go in the box, and whether it replaces what's there.</summary>
        private string pendingText;
        private bool pendingReplaces;

        /// <summary>
        /// The callback, kept in a field for as long as the view: ImGui holds on to it only while it's called.
        /// </summary>
        private ImGuiInputTextCallback MakeCallback() {
            return OnInputCallback;
        }

        /// <summary>Starts typing in the box, with text put in at its end (a linked item).</summary>
        public void Focus(string insert = null) {
            focusNext = true;

            if (!string.IsNullOrEmpty(insert)) {
                pendingText = insert;
                pendingReplaces = false;
            }
        }

        /// <summary>Starts a tell to someone: "/tell Name, " in the box, ready for the rest.</summary>
        public void StartTell(string name) {
            if (string.IsNullOrWhiteSpace(name)) {
                return;
            }

            owner.Focus(index);
            pendingText = $"/tell {name}, ";
            pendingReplaces = true;
        }

        private void RenderInput() {
            var window = Window;
            var talk = ChatTalk.For(window.Talk);
            var label = TalkLabel(talk, out var tip);

            if (ImGui.Button($"{label}##chat-talk")) {
                ImGui.OpenPopup("chat-talk");
            }
            Widgets.Tip(tip + " Click to pick who it goes to. A line you start with / or @ is a command, sent as typed.");

            RenderTalkMenu();

            var style = ImGui.GetStyle();
            ImGui.SameLine(0, style.ItemInnerSpacing.X);
            var options = ImGui.GetFrameHeight();
            ImGui.SetNextItemWidth(Math.Max(40, ImGui.GetContentRegionAvail().X - options - style.ItemInnerSpacing.X));

            // ImGui reads the text in only when the box isn't being typed in; while it is, the callback puts it in
            if (pendingText != null && !inputActive) {
                input = pendingReplaces ? pendingText : Joined(input, pendingText);
                pendingText = null;
            }

            if (focusNext) {
                focusNext = false;
                ImGui.SetKeyboardFocusHere();
            }

            var flags = ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.CallbackHistory
                | ImGuiInputTextFlags.CallbackAlways;
            var entered = ImGui.InputTextWithHint("##chat-input", Hint(talk), ref input, MaxLength, flags, callback);
            inputActive = ImGui.IsItemActive();

            if (inputActive) {
                LastTyped = index;
            }

            if (entered) {
                Submit();
            }

            ImGui.SameLine(0, style.ItemInnerSpacing.X);

            if (ImGui.Button("...##chat-options", new System.Numerics.Vector2(options, 0))) {
                ImGui.OpenPopup("chat-options");
            }
            Widgets.Tip($"{window.Name}'s options: what it shows, who typing in it goes to, its text size, and more. "
                + "The Chat tab of the /dap window has the rest (fonts, colours, times).");
        }

        /// <summary>What the talk button says, and its tooltip.</summary>
        private string TalkLabel(ChatTalk talk, out string tip) {
            tip = talk.Tip;

            switch (talk.Key) {
                case ChatTalk.TellSelected:
                    SocialActions.SelectedPlayer(out var selected);
                    tip = selected != null ? $"Tells go to {selected}, your selection." : "Select a player to send a tell to.";
                    return selected != null ? $"Tell {selected}" : "Tell (select)";
                case ChatTalk.Reply:
                    tip = owner.ReplyTo != null ? $"Tells go to {owner.ReplyTo}, who sent you the last one." : talk.Tip;
                    return owner.ReplyTo != null ? $"Reply {owner.ReplyTo}" : "Reply";
                case ChatTalk.Custom:
                    var prefix = (Window.CustomPrefix ?? "").Trim();
                    tip = prefix.Length > 0 ? $"Each line goes as {prefix} <what you typed>." : talk.Tip;
                    return prefix.Length > 0 ? prefix : talk.Short;
                default:
                    return talk.Short;
            }
        }

        private string Hint(ChatTalk talk) {
            if ((Data.HideGameChat || Data.EnterTypes) && !inputActive) {
                return "Press Enter to chat";
            }

            switch (talk.Key) {
                case ChatTalk.Say:
                    return "Say something...";
                case ChatTalk.TellSelected:
                case ChatTalk.Reply:
                    return "Send a tell...";
                default:
                    return $"To {talk.Label}...";
            }
        }

        private void RenderTalkMenu() {
            if (!ImGui.BeginPopup("chat-talk")) {
                return;
            }

            var window = Window;

            foreach (var talk in ChatTalk.All) {
                if (ImGui.Selectable(talk.Label, talk.Key == window.Talk)) {
                    window.Talk = talk.Key;
                    owner.Settings.Changed();
                }
                Widgets.Tip(talk.Tip);
            }

            if (window.Talk == ChatTalk.Custom) {
                ImGui.Separator();
                ImGui.SetNextItemWidth(ImGui.GetFontSize() * 10);

                if (ImGui.InputTextWithHint("##chat-prefix", "/e ", ref window.CustomPrefix, 60)) {
                    owner.Settings.Changed();
                }
                Widgets.Tip("What goes in front of each line: a command (/e to emote), or \"/tell Bob,\" to keep telling one "
                    + "person.");
            }

            ImGui.EndPopup();
        }

        /// <summary>Sends what's typed, to whoever the window's talk focus says, or says in the window why it can't.</summary>
        private void Submit() {
            var text = (input ?? "").Trim();
            historyAt = -1;
            historyDraft = null;

            if (text.Length == 0) {
                return;
            }

            var window = Window;
            SocialActions.SelectedPlayer(out var selected);
            var command = ChatTalk.Command(window.Talk, text, selected, owner.ReplyTo, window.CustomPrefix, out var problem);

            if (command == null) {
                if (problem != null) {
                    Note(problem);
                }

                return;
            }

            if (sent.Count == 0 || sent[sent.Count - 1] != text) {
                sent.Add(text);

                if (sent.Count > MaxHistory) {
                    sent.RemoveAt(0);
                }
            }

            input = "";
            scrollToBottom = true;
            owner.Send(command);

            if (Data.KeepTyping) {
                focusNext = true;
            }
        }

        private static string Joined(string text, string more) {
            if (string.IsNullOrEmpty(text)) {
                return more + " ";
            }

            return (text.EndsWith(" ", StringComparison.Ordinal) ? text : text + " ") + more + " ";
        }

        /// <summary>
        /// ImGui's call back while the box is typed in: puts waiting text in, and steps through what's been sent on Up and
        /// Down. An exception mustn't get back into ImGui, so nothing escapes.
        /// </summary>
        private int OnInputCallback(ImGuiInputTextCallbackData* raw) {
            try {
                var data = new ImGuiInputTextCallbackDataPtr(raw);

                if (data.EventFlag == ImGuiInputTextFlags.CallbackAlways) {
                    if (pendingText != null) {
                        var current = Encoding.UTF8.GetString((byte*)data.Buf, data.BufTextLen);
                        Replace(data, pendingReplaces ? pendingText : Joined(current, pendingText));
                        pendingText = null;
                    }

                    return 0;
                }

                if (data.EventFlag != ImGuiInputTextFlags.CallbackHistory || sent.Count == 0) {
                    return 0;
                }

                if (data.EventKey == ImGuiKey.UpArrow) {
                    if (historyAt < 0) {
                        historyDraft = Encoding.UTF8.GetString((byte*)data.Buf, data.BufTextLen);
                        historyAt = sent.Count - 1;
                    }
                    else if (historyAt > 0) {
                        historyAt--;
                    }
                }
                else if (data.EventKey == ImGuiKey.DownArrow) {
                    if (historyAt < 0) {
                        return 0;
                    }

                    historyAt = historyAt + 1 < sent.Count ? historyAt + 1 : -1;
                }
                else {
                    return 0;
                }

                Replace(data, historyAt < 0 ? historyDraft ?? "" : sent[historyAt]);
            }
            catch (Exception ex) {
                PluginCore.LogFile($"[chat] typing: {ex.Message}");
            }

            return 0;
        }

        private static void Replace(ImGuiInputTextCallbackDataPtr data, string text) {
            var bytes = Encoding.UTF8.GetByteCount(text);

            // the box holds so much: what doesn't fit is left off
            if (bytes >= data.BufSize) {
                text = text.Substring(0, Math.Max(0, Math.Min(text.Length, data.BufSize - 1)));
            }

            data.DeleteChars(0, data.BufTextLen);
            data.InsertChars(0, text);
        }
    }
}
