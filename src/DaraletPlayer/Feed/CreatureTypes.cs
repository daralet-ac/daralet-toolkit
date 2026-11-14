using System.Text;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// A creature's kind by its CreatureType (PropertyInt 2), as daralet-ACE's enum names them, with spaces put between the
    /// words ("PhyntosWasp" is Phyntos Wasp), as the game's appraisal writes them.
    /// </summary>
    public static class CreatureTypes {
        private static readonly string[] Names = {
            "", "Olthoi", "Banderling", "Drudge", "Mosswart", "Lugian", "Tumerok", "Mite", "Tusker", "PhyntosWasp", "Rat",
            "Auroch", "Cow", "Golem", "Undead", "Gromnie", "Reedshark", "Armoredillo", "Fae", "Virindi", "Wisp",
            "Knathtead", "Shadow", "Mattekar", "Mumiyah", "Rabbit", "Sclavus", "ShallowsShark", "Monouga", "Zefir",
            "Skeleton", "Human", "Shreth", "Chittick", "Moarsman", "OlthoiLarvae", "Slithis", "Deru", "FireElemental",
            "Snowman", "Unknown", "Bunny", "LightningElemental", "Rockslide", "Grievver", "Niffis", "Ursuin", "Crystal",
            "HollowMinion", "Scarecrow", "Idol", "Empyrean", "Hopeslayer", "Doll", "Marionette", "Carenzi", "Siraluun",
            "AunTumerok", "HeaTumerok", "Simulacrum", "AcidElemental", "FrostElemental", "Elemental", "Statue", "Wall",
            "AlteredHuman", "Device", "Harbinger", "DarkSarcophagus", "Chicken", "GotrokLugian", "Margul",
            "BleachedRabbit", "NastyRabbit", "GrimacingRabbit", "Burun", "Target", "Ghost", "Fiun", "Eater", "Penguin",
            "Ruschk", "Thrungus", "ViamontianKnight", "Remoran", "Swarm", "Moar", "EnchantedArms", "Sleech", "Mukkir",
            "Merwart", "Food", "ParadoxOlthoi", "Harvest", "Energy", "Apparition", "Aerbax", "Touched",
            "BlightedMoarsman", "GearKnight", "Gurog", "Anekshay",
        };

        /// <summary>The kind's name, or null for none (0) or one this doesn't know.</summary>
        public static string Name(int creatureType) {
            if (creatureType <= 0 || creatureType >= Names.Length) {
                return null;
            }

            return Spaced(Names[creatureType]);
        }

        /// <summary>"PhyntosWasp" as "Phyntos Wasp": a space before each capital after the first letter.</summary>
        public static string Spaced(string name) {
            var spaced = new StringBuilder();

            for (var i = 0; i < name.Length; i++) {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) {
                    spaced.Append(' ');
                }

                spaced.Append(name[i]);
            }

            return spaced.ToString();
        }
    }
}
