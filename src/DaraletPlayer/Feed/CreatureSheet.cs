using System.Collections.Generic;
using System.Globalization;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// What the Creature Appraisal window shows of an appraisal (CreatureAppraisal), as the game's examination panel has
    /// it: the name and "Level 50 Olthoi" (or a player's heritage and gender) at the top, the vitals and attributes, a spell's
    /// change in green or red, then what the server adds (ratings, a player's armor, allegiance and character), and the
    /// description.
    /// </summary>
    public static class CreatureSheet {
        // PropertyInt
        public const uint CreatureType = 2;
        public const uint Level = 25;
        public const uint AllegianceRank = 30;
        public const uint AllegianceFollowers = 35;
        public const uint NumDeaths = 43;
        public const uint Age = 125;
        public const uint PlayerKillerStatus = 134;
        public const uint ChessRank = 181;
        public const uint FakeFishingSkill = 192;
        public const uint NumCharacterTitles = 262;

        /// <summary>
        /// Gender (1 male, 2 female) and HeritageGroup, the numbers the game's panel writes "Male Gharu'ndim" from; both
        /// AssessmentProperties, so sent for players and NPCs alike. The strings Sex and HeritageGroup aren't.
        /// </summary>
        public const uint Gender = 113;
        public const uint HeritageGroupId = 188;

        // PropertyString
        public const uint Name = 1;
        public const uint Title = 2;
        public const uint Sex = 3;
        public const uint HeritageGroup = 4;
        public const uint Template = 5;
        public const uint Fellowship = 10;
        public const uint LongDesc = 16;
        public const uint MonarchsTitle = 21;
        public const uint PatronsTitle = 35;
        public const uint DateOfBirth = 43;
        public const uint AllegianceName = 47;

        private const int PlayerKiller = 0x4;
        private const int PlayerKillerLite = 0x40;

        private static readonly (uint Key, string Name)[] Ratings = {
            (307, "Damage Rating"), (308, "Damage Resistance Rating"), (313, "Critical Rating"),
            (314, "Critical Damage Rating"), (315, "Critical Resistance Rating"),
            (316, "Critical Damage Resistance Rating"), (323, "Healing Boost Rating"), (331, "Nether Resistance Rating"),
            (350, "Damage Over Time Resistance Rating"), (351, "Life Resistance Rating"), (379, "Gear Max Health"),
            (381, "PK Damage Rating"), (382, "PK Damage Resistance Rating"),
        };

        /// <summary>The armor's body parts three to a line, as the game's panel writes them, in ArmorLevels' order.</summary>
        private static readonly string[] ArmorLines = { "Head/Chest/Groin", "Bicep/Wrist/Hand", "Thigh/Shin/Foot" };

        /// <summary>
        /// The heritages' names as retail had them (HeritageGroup 1 to 13), for when the portal dat's character creation
        /// table (which has the server's own) can't be read.
        /// </summary>
        private static readonly string[] RetailHeritages = {
            null, "Aluvian", "Gharu'ndim", "Sho", "Viamontian", "Umbraen", "Gear Knight", "Tumerok", "Lugian", "Empyrean",
            "Penumbraen", "Undead", "Olthoi", "Olthoi",
        };

        /// <summary>
        /// A player's or a humanlike NPC's appraisal carries a heritage (the number; the string in older tests too); a
        /// creature's doesn't.
        /// </summary>
        public static bool IsPlayer(CreatureAppraisal appraisal) {
            return (appraisal.Int(HeritageGroupId) ?? 0) > 0 || !string.IsNullOrEmpty(appraisal.String(HeritageGroup));
        }

        /// <summary>A heritage's name as retail had it, or null for one it hadn't.</summary>
        public static string RetailHeritage(int id) {
            return id > 0 && id < RetailHeritages.Length ? RetailHeritages[id] : null;
        }

        /// <summary>
        /// The lines under the name, as the game's panel has them: "Male Gharu'ndim" (or a creature's kind), the template
        /// ("Spellsword", "Humble Servant"), and whether they're a player killer; each only when there's something to say.
        /// </summary>
        /// <param name="heritageName">A heritage's name by number (the portal dat's), else retail's are used.</param>
        public static List<string> HeaderLines(CreatureAppraisal appraisal, System.Func<int, string> heritageName = null) {
            var lines = new List<string>();
            var kind = KindOf(appraisal, heritageName);

            if (kind.Length > 0) {
                lines.Add(kind);
            }

            var template = appraisal.String(Template);

            if (!string.IsNullOrWhiteSpace(template)) {
                lines.Add(template.Trim());
            }

            var status = appraisal.Int(PlayerKillerStatus);

            if (status.HasValue && IsPlayer(appraisal)) {
                lines.Add(PlayerKillerText(status.Value));
            }

            return lines;
        }

        /// <summary>The name the server sent, else the one the client has.</summary>
        public static string NameOf(CreatureAppraisal appraisal, string known) {
            var name = appraisal.String(Name);
            return string.IsNullOrEmpty(name) ? known ?? "" : name;
        }

        /// <summary>"Male Gharu'ndim" for a player or a humanlike NPC, as the game has it; "Olthoi" for a creature.</summary>
        public static string KindOf(CreatureAppraisal appraisal, System.Func<int, string> heritageName = null) {
            var heritageId = appraisal.Int(HeritageGroupId) ?? 0;

            if (heritageId > 0) {
                var gender = appraisal.Int(Gender);
                var sex = gender == 1 ? "Male" : gender == 2 ? "Female" : null;
                var heritage = heritageName?.Invoke(heritageId) ?? RetailHeritage(heritageId);
                return $"{sex} {heritage}".Trim();
            }

            if (!string.IsNullOrEmpty(appraisal.String(HeritageGroup))) {
                return $"{appraisal.String(Sex)} {appraisal.String(HeritageGroup)}".Trim();
            }

            return CreatureTypes.Name(appraisal.Int(CreatureType) ?? 0) ?? "";
        }

        /// <summary>The level, or null when the appraisal doesn't say (a failed one).</summary>
        public static int? LevelOf(CreatureAppraisal appraisal) {
            return appraisal.Int(Level);
        }

        /// <summary>Health, stamina and mana (the last two only when the appraisal succeeded).</summary>
        public static List<AppraisalRow> Vitals(CreatureStats stats) {
            var rows = new List<AppraisalRow>();

            if (stats == null) {
                return rows;
            }

            rows.Add(Vital("Health", stats.Health, stats.HealthMax, stats.Effect(CreatureStats.HealthBit)));

            if (stats.HasAttributes) {
                rows.Add(Vital("Stamina", stats.Stamina, stats.StaminaMax, stats.Effect(CreatureStats.StaminaBit)));
                rows.Add(Vital("Mana", stats.Mana, stats.ManaMax, stats.Effect(CreatureStats.ManaBit)));
            }

            return rows;
        }

        /// <summary>The six attributes, when the appraisal succeeded.</summary>
        public static List<AppraisalRow> Attributes(CreatureStats stats) {
            var rows = new List<AppraisalRow>();

            if (stats == null || !stats.HasAttributes) {
                return rows;
            }

            rows.Add(Attribute("Strength", stats.Strength, stats.Effect(CreatureStats.StrengthBit)));
            rows.Add(Attribute("Endurance", stats.Endurance, stats.Effect(CreatureStats.EnduranceBit)));
            rows.Add(Attribute("Coordination", stats.Coordination, stats.Effect(CreatureStats.CoordinationBit)));
            rows.Add(Attribute("Quickness", stats.Quickness, stats.Effect(CreatureStats.QuicknessBit)));
            rows.Add(Attribute("Focus", stats.Focus, stats.Effect(CreatureStats.FocusBit)));
            rows.Add(Attribute("Self", stats.Self, stats.Effect(CreatureStats.SelfBit)));
            return rows;
        }

        /// <summary>
        /// Everything after the vitals and attributes, each a section with something in it: ratings, armor, allegiance,
        /// character.
        /// </summary>
        public static List<AppraisalSection> Sections(CreatureAppraisal appraisal) {
            var sections = new List<AppraisalSection>();
            Add(sections, "Ratings", RatingRows(appraisal));
            Add(sections, "Armor", ArmorRows(appraisal));
            Add(sections, "Allegiance", AllegianceRows(appraisal));
            Add(sections, "Character", CharacterRows(appraisal));
            return sections;
        }

        /// <summary>The long description, or "" for none.</summary>
        public static string Description(CreatureAppraisal appraisal) {
            return (appraisal.String(LongDesc) ?? "").Trim();
        }

        /// <summary>Player Killer, Player Killer Lite or Non-Player Killer, as the game says it.</summary>
        public static string PlayerKillerText(int status) {
            return (status & PlayerKiller) != 0 ? "Player Killer"
                : (status & PlayerKillerLite) != 0 ? "Player Killer Lite" : "Non-Player Killer";
        }

        private static void Add(List<AppraisalSection> sections, string title, List<AppraisalRow> rows) {
            if (rows.Count > 0) {
                sections.Add(new AppraisalSection(title, rows));
            }
        }

        private static AppraisalRow Vital(string name, uint current, uint max, int effect) {
            return new AppraisalRow(name, $"{Number(current)} / {Number(max)}", effect);
        }

        private static AppraisalRow Attribute(string name, uint value, int effect) {
            return new AppraisalRow(name, Number(value), effect);
        }

        private static List<AppraisalRow> RatingRows(CreatureAppraisal appraisal) {
            var rows = new List<AppraisalRow>();

            foreach (var (key, name) in Ratings) {
                var value = appraisal.Int(key);

                if (value.HasValue && value.Value != 0) {
                    rows.Add(new AppraisalRow(name, Number(value.Value)));
                }
            }

            return rows;
        }

        /// <summary>
        /// Armor by body part, three to a line as the game's panel writes it: "Head/Chest/Groin" and "AL: 90/95/95".
        /// </summary>
        private static List<AppraisalRow> ArmorRows(CreatureAppraisal appraisal) {
            var rows = new List<AppraisalRow>();
            var levels = appraisal.ArmorLevels;

            if (levels == null) {
                return rows;
            }

            for (var line = 0; line < ArmorLines.Length && line * 3 < levels.Length; line++) {
                var values = new List<string>();

                for (var i = line * 3; i < line * 3 + 3 && i < levels.Length; i++) {
                    values.Add(Number(levels[i]));
                }

                rows.Add(new AppraisalRow(ArmorLines[line], "AL: " + string.Join("/", values)));
            }

            return rows;
        }

        private static List<AppraisalRow> AllegianceRows(CreatureAppraisal appraisal) {
            var rows = new List<AppraisalRow>();
            AddText(rows, "Allegiance", appraisal.String(AllegianceName));
            AddNumber(rows, "Rank", appraisal.Int(AllegianceRank), showZero: false);
            AddNumber(rows, "Followers", appraisal.Int(AllegianceFollowers), showZero: false);
            AddText(rows, "Monarch", appraisal.String(MonarchsTitle));
            AddText(rows, "Patron", appraisal.String(PatronsTitle));
            AddText(rows, "Fellowship", appraisal.String(Fellowship));
            return rows;
        }

        private static List<AppraisalRow> CharacterRows(CreatureAppraisal appraisal) {
            var rows = new List<AppraisalRow>();

            if (!IsPlayer(appraisal)) {
                return rows;
            }

            AddText(rows, "Title", appraisal.String(Title));
            AddText(rows, "Born", appraisal.String(DateOfBirth));

            var age = appraisal.Int(Age);

            if (age.HasValue) {
                rows.Add(new AppraisalRow("Age", RetailCharacterInfo.AgeText(age.Value)));
            }

            AddNumber(rows, "Deaths", appraisal.Int(NumDeaths), showZero: true);
            AddNumber(rows, "Titles", appraisal.Int(NumCharacterTitles), showZero: false);
            AddNumber(rows, "Chess Rank", appraisal.Int(ChessRank), showZero: false);
            AddNumber(rows, "Fishing Skill", appraisal.Int(FakeFishingSkill), showZero: false);
            return rows;
        }

        private static void AddText(List<AppraisalRow> rows, string label, string value) {
            if (!string.IsNullOrWhiteSpace(value)) {
                rows.Add(new AppraisalRow(label, value.Trim()));
            }
        }

        private static void AddNumber(List<AppraisalRow> rows, string label, int? value, bool showZero) {
            if (value.HasValue && (showZero || value.Value != 0)) {
                rows.Add(new AppraisalRow(label, Number(value.Value)));
            }
        }

        private static string Number(long value) {
            return value.ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
