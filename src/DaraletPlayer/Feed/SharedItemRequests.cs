using System;
using System.Collections.Generic;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// The items linked in chat, by key (ChatItemLink.Key): those the server has sent, those it says it has no item for, and
    /// the keys to ask it about, up to 10 to a @playerui shared request (PlayerUiItemShares.MaxLookup) and as many as make a
    /// short enough command, one request at a time. A request the server's rate limit dropped gets no answer at all, so one
    /// unanswered for long enough is sent again, MaxTries in all; after that its keys are let be until something asks for
    /// them again a while later. Kept apart from PlayerFeed, which needs Decal, so tests can reach it.
    /// </summary>
    public class SharedItemRequests {
        public const int Batch = 10;
        public const int MaxTries = 3;

        /// <summary>The most letters of keys in one request: a key carries two names, so they aren't short.</summary>
        public const int MaxLetters = 400;

        /// <summary>A key given up on is asked about again after this long, if something still wants it.</summary>
        public static readonly TimeSpan RetryAfter = TimeSpan.FromSeconds(20);

        private readonly TimeSpan timeout;
        private readonly Dictionary<string, SharedItemInfo> known = new Dictionary<string, SharedItemInfo>();
        private readonly HashSet<string> missing = new HashSet<string>();
        private readonly List<string> queue = new List<string>();
        private readonly Dictionary<string, DateTime> givenUp = new Dictionary<string, DateTime>();
        private readonly Dictionary<string, int> tries = new Dictionary<string, int>();
        private string[] inFlight;
        private DateTime sentAt = DateTime.MinValue;

        public SharedItemRequests(TimeSpan timeout) {
            this.timeout = timeout;
        }

        public DateTime SentAt => sentAt;

        /// <summary>The item a key names, or null while it isn't known.</summary>
        public SharedItemInfo Find(string key) {
            return key != null && known.TryGetValue(key, out var item) ? item : null;
        }

        /// <summary>Whether the server said there's no item for the key (a day old, or just text in brackets).</summary>
        public bool IsMissing(string key) {
            return key != null && missing.Contains(key);
        }

        /// <summary>Whether the key is waiting for the server (asked, or about to be).</summary>
        public bool IsWaiting(string key) {
            return queue.Contains(key) || (inFlight != null && inFlight.Contains(key));
        }

        /// <summary>
        /// Asks about a key, unless it's known, missing, already asked, or given up on too lately. Safe every frame.
        /// </summary>
        public void Ask(string key, DateTime now) {
            if (string.IsNullOrEmpty(key) || known.ContainsKey(key) || missing.Contains(key) || IsWaiting(key)) {
                return;
            }

            if (givenUp.TryGetValue(key, out var at)) {
                if (now - at < RetryAfter) {
                    return;
                }

                givenUp.Remove(key);
            }

            tries.Remove(key);
            queue.Add(key);
        }

        /// <summary>
        /// Forgets what's known of a key, for a new line naming it: the same player may have linked another item by that name.
        /// </summary>
        public void Forget(string key) {
            if (key == null) {
                return;
            }

            known.Remove(key);
            missing.Remove(key);
            givenUp.Remove(key);
        }

        /// <summary>An item the server sent, known from now on.</summary>
        public void Add(SharedItemInfo item) {
            if (item?.Key == null) {
                return;
            }

            known[item.Key] = item;
            missing.Remove(item.Key);
            queue.Remove(item.Key);
        }

        /// <summary>The next request's keys, or null while one waits for its answer or there's nothing to ask.</summary>
        public string[] Next(DateTime now) {
            if (inFlight != null) {
                if (now - sentAt < timeout) {
                    return null;
                }

                // no answer: back to the front, a try nearer being given up on
                var again = new List<string>();

                foreach (var key in inFlight) {
                    tries[key] = (tries.TryGetValue(key, out var count) ? count : 0) + 1;

                    if (tries[key] >= MaxTries) {
                        tries.Remove(key);
                        givenUp[key] = now;
                    }
                    else {
                        again.Add(key);
                    }
                }

                queue.InsertRange(0, again);
                inFlight = null;
            }

            if (queue.Count == 0) {
                return null;
            }

            var take = 1;
            var letters = queue[0].Length;

            while (take < queue.Count && take < Batch && letters + queue[take].Length <= MaxLetters) {
                letters += queue[take].Length;
                take++;
            }

            inFlight = queue.Take(take).ToArray();
            queue.RemoveRange(0, take);
            sentAt = now;
            return inFlight;
        }

        /// <summary>The server's answer: its items are known, its missing keys missing, and the next request can go.</summary>
        public void Answered(SharedLookupInfo lookup, DateTime now) {
            foreach (var item in lookup?.Items ?? new List<SharedItemInfo>()) {
                Add(item);
            }

            foreach (var key in lookup?.Missing ?? new List<string>()) {
                missing.Add(key);
                queue.Remove(key);
            }

            // whatever the request asked that the answer didn't mention can be asked again
            if (inFlight != null) {
                foreach (var key in inFlight.Where(c => !known.ContainsKey(c) && !missing.Contains(c))) {
                    givenUp[key] = now;
                }
            }

            inFlight = null;
        }
    }
}
