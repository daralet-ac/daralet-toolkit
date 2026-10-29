using System.Collections.Generic;
using System.Linq;
using Decal.Adapter;
using Decal.Adapter.Wrappers;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// What you carry, read from Decal: grouped by each object's own Container, as the inventory lists are (Decal's
    /// GetInventory and GetByContainer go stale).
    /// </summary>
    internal static class Carried {
        /// <summary>
        /// Every item in your main pack and side packs, in pack order then slot order: not the packs themselves, and
        /// nothing worn.
        /// </summary>
        public static List<WorldObject> Items() {
            var me = CoreManager.Current.CharacterFilter.Id;
            var byContainer = ByContainer();
            var items = Loose(byContainer[me]).ToList();

            var packs = byContainer[me]
                .Where(o => o.ObjectClass == ObjectClass.Container)
                .OrderBy(o => o.Values(LongValueKey.Slot, 0));

            foreach (var pack in packs) {
                items.AddRange(Loose(byContainer[pack.Id]));
            }

            return items;
        }

        /// <summary>The items in one pack you carry (the main pack: not its side packs, nor anything worn), by slot.</summary>
        public static List<WorldObject> In(int pack) {
            return Loose(ByContainer()[pack]).ToList();
        }

        /// <summary>Whether an object is a pack: a side pack (or a bank pack, or a chest).</summary>
        public static bool IsPack(int id) {
            return CoreManager.Current.WorldFilter[id]?.ObjectClass == ObjectClass.Container;
        }

        /// <summary>
        /// Whether you carry an item, unworn: in your main pack, or in a pack in it.
        /// </summary>
        public static bool Has(int id) {
            var world = CoreManager.Current.WorldFilter;
            var me = CoreManager.Current.CharacterFilter.Id;
            var item = world[id];

            if (item == null || item.Values(LongValueKey.EquippedSlots, 0) != 0) {
                return false;
            }

            var container = item.Values(LongValueKey.Container, 0);
            return container == me || (container != 0 && world[container]?.Values(LongValueKey.Container, 0) == me);
        }

        /// <summary>The Ust's weenie class (W_TINKERINGTOOL_CLASS), as Decal's WorldObject.Type gives it.</summary>
        private const int UstWeenieClass = 20646;

        /// <summary>An Ust, the tool that salvages: by its weenie class, or its name.</summary>
        public static bool IsUst(WorldObject o) {
            return o.Type == UstWeenieClass || o.Name == "Ust";
        }

        /// <summary>What the client knows of an item, for ItemChecks; with the server's word on salvage once it's described.</summary>
        public static ItemFacts Facts(WorldObject o, PlayerFeed feed = null) {
            CorpseItemInfo described = null;
            feed?.Described.TryGetValue(unchecked((uint)o.Id), out described);

            return new ItemFacts {
                Id = o.Id,
                ItemType = o.Values(LongValueKey.Category, 0),
                Value = o.Values(LongValueKey.Value, 0),
                Material = o.Values(LongValueKey.Material, 0),
                Retained = o.Values(BoolValueKey.Retained, false),
                Attuned = o.Values(LongValueKey.Attuned, 0) != 0,
                IsSalvage = o.ObjectClass == ObjectClass.Salvage,
                IsPack = o.ObjectClass == ObjectClass.Container,
                ServerSalvageable = described?.Salvageable,
            };
        }

        private static ILookup<int, WorldObject> ByContainer() {
            return CoreManager.Current.WorldFilter.GetAll().Cast<WorldObject>().ToLookup(o => o.Values(LongValueKey.Container, 0));
        }

        private static IEnumerable<WorldObject> Loose(IEnumerable<WorldObject> objects) {
            return objects
                .Where(o => o.ObjectClass != ObjectClass.Container && o.Values(LongValueKey.EquippedSlots, 0) == 0)
                .OrderBy(o => o.Values(LongValueKey.Slot, 0));
        }
    }
}
