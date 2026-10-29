using System;
using System.Collections.Generic;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// Which fellows the fellowship window shows, and in what order, for big fellowships (a Town Defense one has no cap):
    /// pinned members first, in the order they were pinned, then the rest sorted, those too far away left out if asked,
    /// and at most so many.
    /// </summary>
    public static class FellowshipOrder {
        // the window's layouts (SettingsData.FellowshipLayout)
        public const string List = "list";
        public const string Pages = "pages";
        public const string Grid = "grid";

        // its sorts (SettingsData.FellowshipSort)
        /// <summary>The server's order: the leader, then by name.</summary>
        public const string ByFellowship = "fellowship";
        public const string ByName = "name";
        /// <summary>Lowest health first, as a fraction of their most: who needs healing.</summary>
        public const string ByHealth = "health";
        /// <summary>Nearest first; those too far away to say last.</summary>
        public const string ByDistance = "distance";

        /// <summary>A fellowship's size limit, when the server doesn't say (one too old to send it).</summary>
        public const int DefaultMaxMembers = 9;

        public static List<FellowInfo> Arrange(IEnumerable<FellowInfo> members, IList<string> pinned, string sort,
            bool hideFar, int showUpTo, uint me) {
            var all = (members ?? Enumerable.Empty<FellowInfo>()).Where(m => m != null).ToList();
            var pins = pinned ?? new List<string>();

            // you're always near yourself, and the server says so (0) anyway
            var shown = all.Where(m => !hideFar || m.Guid == me || m.Distance.HasValue).ToList();

            var first = pins
                .Select(name => shown.FirstOrDefault(m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase)))
                .Where(m => m != null)
                .Distinct()
                .ToList();

            var rest = Sorted(shown.Except(first), sort);
            var arranged = first.Concat(rest).ToList();
            return showUpTo > 0 ? arranged.Take(showUpTo).ToList() : arranged;
        }

        private static IEnumerable<FellowInfo> Sorted(IEnumerable<FellowInfo> members, string sort) {
            switch (sort) {
                case ByName:
                    return members.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase);
                case ByHealth:
                    return members.OrderBy(HealthFraction).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase);
                case ByDistance:
                    return members.OrderBy(m => m.Distance ?? float.MaxValue)
                        .ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase);
                default:
                    return members;
            }
        }

        public static float HealthFraction(FellowInfo member) {
            return member.MaxHealth == 0 ? 1f : Math.Min(1f, (float)member.Health / member.MaxHealth);
        }

        /// <summary>A fellowship's size limit: the server's, or the usual one from a server that doesn't say.</summary>
        public static int MaxMembers(FellowshipInfo fellowship) {
            return fellowship?.MaxMembers > 0 ? fellowship.MaxMembers : DefaultMaxMembers;
        }

        /// <summary>The members on one page (from 0) of so many, and how many pages there are.</summary>
        public static List<FellowInfo> Page(List<FellowInfo> members, int pageSize, int page, out int pages) {
            pageSize = Math.Max(1, pageSize);
            pages = Math.Max(1, (members.Count + pageSize - 1) / pageSize);
            page = Math.Max(0, Math.Min(pages - 1, page));
            return members.Skip(page * pageSize).Take(pageSize).ToList();
        }
    }
}
