using System;
using System.Collections.Generic;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// What the Social tab's buttons send, and the bits they share: who's selected, and the options (Appear Offline and the
    /// fellowship ones) as they'll be once the server has heard. Friends, squelches, allegiance and creating a fellowship go
    /// through @playerui (PlayerUiSocialData in daralet-ACE), which hands them to the game's own handlers: the client's
    /// messages for these carry a name, which a plugin can't send. Options are the client's own message
    /// (CM_Character.Event_PlayerOptionChangedEvent), the one its option checkboxes send.
    /// </summary>
    internal static class SocialActions {
        /// <summary>How long an option ticked here shows as ticked before the server's word is taken over it.</summary>
        private static readonly TimeSpan PendingFor = TimeSpan.FromSeconds(4);

        /// <summary>The social reply is asked for this often while something shows it.</summary>
        public static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(2);

        /// <summary>Options changed here, and when, until the server's reply catches up.</summary>
        private static readonly Dictionary<AcClient.PlayerOption, (bool Value, DateTime At)> pending =
            new Dictionary<AcClient.PlayerOption, (bool, DateTime)>();

        /// <summary>The selected object, when it's another player (not you), or 0. Nothing is read before login is over.</summary>
        public static int SelectedPlayer(out string name) {
            name = null;

            if (CoreManager.Current.CharacterFilter.LoginStatus != 3) {
                return 0;
            }

            var id = CoreManager.Current.Actions.CurrentSelection;

            if (id == 0 || id == CoreManager.Current.CharacterFilter.Id) {
                return 0;
            }

            var target = CoreManager.Current.WorldFilter[id];

            if (target == null || target.ObjectClass != ObjectClass.Player) {
                return 0;
            }

            name = target.Name;
            return id;
        }

        /// <summary>The selection, whatever it is, or 0. Nothing is read before login is over.</summary>
        public static int Selection() {
            return CoreManager.Current.CharacterFilter.LoginStatus == 3 ? CoreManager.Current.Actions.CurrentSelection : 0;
        }

        public static void Select(uint guid) {
            CoreManager.Current.Actions.SelectItem(unchecked((int)guid));
        }

        /// <summary>An option as the server last said, or as it was just changed here while the server hasn't caught up.</summary>
        public static bool Option(PlayerFeed feed, AcClient.PlayerOption option, bool fromServer) {
            if (pending.TryGetValue(option, out var change)) {
                if (DateTime.UtcNow - change.At < PendingFor && feed.SocialUpdatedAt < change.At.AddSeconds(1)) {
                    return change.Value;
                }

                pending.Remove(option);
            }

            return fromServer;
        }

        /// <summary>Changes one of your options, as the game's own checkbox for it does.</summary>
        public static void SetOption(PlayerFeed feed, AcClient.PlayerOption option, bool value) {
            AcClient.CM_Character.Event_PlayerOptionChangedEvent(option, value ? 1 : 0);
            pending[option] = (value, DateTime.UtcNow);
            feed.RefreshSocialSoon();
            PluginCore.LogFile($"[social] option {option} {(value ? "on" : "off")}");
        }

        /// <summary>A @playerui social action (friend, squelch, allegiance, fellowcreate), then the lists asked for again.</summary>
        public static void Send(PlayerFeed feed, string kind, string words) {
            feed.SendCommand($"@playerui {kind} {words}");
            feed.RefreshSocialSoon();
        }

        /// <summary>A tell, as /tell would send it.</summary>
        public static void Tell(string name, string text) {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(text)) {
                return;
            }

            CoreManager.Current.Actions.InvokeChatParser($"/tell {name}, {text.Trim()}");
        }

        /// <summary>Whether you can bring someone into your fellowship now: the leader, or anyone in an open one, with room.</summary>
        public static bool CanRecruit(FellowshipInfo fellowship, out string why) {
            why = null;

            if (fellowship == null) {
                why = "You aren't in a fellowship.";
            }
            else if (fellowship.Locked) {
                why = "Your fellowship is locked: it can't take new members.";
            }
            else if ((fellowship.Members?.Count ?? 0) >= FellowshipOrder.MaxMembers(fellowship)) {
                why = $"Your fellowship is full ({FellowshipOrder.MaxMembers(fellowship)}).";
            }
            else if (!IsLeader(fellowship) && !fellowship.Open) {
                why = "Only the leader can recruit, unless the fellowship is open.";
            }

            return why == null;
        }

        public static bool IsLeader(FellowshipInfo fellowship) {
            return fellowship != null && fellowship.Leader == unchecked((uint)CoreManager.Current.CharacterFilter.Id);
        }

        /// <summary>A server too old for a request: what to say instead of a button that does nothing.</summary>
        public static string TooOld(string what) {
            return $"This server is too old for {what}.";
        }
    }
}
