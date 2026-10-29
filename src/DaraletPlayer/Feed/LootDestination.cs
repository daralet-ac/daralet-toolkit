using System.Collections.Generic;
using System.Linq;

namespace DaraletPlayer.Feed {
    /// <summary>A pack you carry, as the looter sees it: how many items are in it and how many fit.</summary>
    public class PackRoom {
        public int Id;
        public int Count;
        public int Capacity;

        public bool HasRoom => Capacity > 0 && Count < Capacity;
    }

    /// <summary>
    /// Where the looter sends an item. The server puts a picked-up item only in the pack it was sent to
    /// (DoHandleActionPutItemInContainer's TryAddToInventory with limitToMainPackOnly in daralet-ACE), so a full main pack
    /// refuses it even with room in your side packs: the game's own client picks a pack with room itself.
    /// </summary>
    public static class LootDestination {
        /// <summary>The server won't let you pick anything up past 300% burden (HasEnoughBurdenToAddToInventory).</summary>
        public const int MaxBurdenPercent = 300;

        /// <summary>
        /// The packs to try, in order: your main pack while it has room, then each side pack with room, in the order they
        /// sit. A pack goes in your main pack whatever, as the server puts packs in its pack slots, not its item slots.
        /// </summary>
        public static List<int> Candidates(PackRoom main, IEnumerable<PackRoom> sidePacks, bool itemIsPack) {
            if (itemIsPack) {
                return new List<int> { main.Id };
            }

            var packs = new List<PackRoom> { main }.Concat(sidePacks ?? Enumerable.Empty<PackRoom>());
            return packs.Where(p => p.HasRoom).Select(p => p.Id).ToList();
        }

        /// <summary>Why an item that was given up on most likely wasn't looted, for the chat line.</summary>
        public static string WhyNot(bool anyRoom, int burdenPercent) {
            if (!anyRoom) {
                return "your packs are full";
            }

            if (burdenPercent >= MaxBurdenPercent) {
                return "you're carrying too much";
            }

            return "the server didn't hand it over (out of reach, or not yours to loot?)";
        }
    }
}
