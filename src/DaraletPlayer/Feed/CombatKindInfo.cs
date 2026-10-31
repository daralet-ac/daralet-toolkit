namespace DaraletPlayer.Feed {
    /// <summary>One kind of combat line: its name and how it starts out.</summary>
    public class CombatKindInfo {
        public CombatKind Kind;
        /// <summary>Its name in settings.json: never change one, or players lose their choices.</summary>
        public string Key;
        public string Label;
        public string Tip;
        /// <summary>Floating text goes on your side (what you did) rather than the side for what's done to you.</summary>
        public bool Yours;
        /// <summary>Written with this in front of the number in floating text ("-" for damage to you, "+" for healing).</summary>
        public string Sign;
        public uint Color;

        public static readonly CombatKindInfo[] All = {
            new CombatKindInfo { Kind = CombatKind.YouHit, Key = "youHit", Label = "Your attacks", Yours = true, Sign = "",
                Color = PackedColor.Pack((byte)255, (byte)255, (byte)255),
                Tip = "Damage your weapons do, and your ripostes and deflections." },
            new CombatKindInfo { Kind = CombatKind.YouSpell, Key = "youSpell", Label = "Your spells", Yours = true, Sign = "",
                Color = PackedColor.Pack((byte)120, (byte)190, (byte)255),
                Tip = "Damage your spells do, and vitals they drain." },
            new CombatKindInfo { Kind = CombatKind.YouPeriodic, Key = "youPeriodic", Label = "Your periodic damage",
                Yours = true, Sign = "", Color = PackedColor.Pack((byte)200, (byte)150, (byte)255),
                Tip = "Bleeds and nether damage over time." },
            new CombatKindInfo { Kind = CombatKind.YourMiss, Key = "yourMiss", Label = "Your misses", Yours = true, Sign = "",
                Color = PackedColor.Pack((byte)170, (byte)170, (byte)170),
                Tip = "What evaded, blocked, parried or resisted you." },
            new CombatKindInfo { Kind = CombatKind.HitYou, Key = "hitYou", Label = "Attacks on you", Sign = "-",
                Color = PackedColor.Pack((byte)255, (byte)85, (byte)75),
                Tip = "Weapon damage and anything else that hits you, such as the cold." },
            new CombatKindInfo { Kind = CombatKind.SpellOnYou, Key = "spellOnYou", Label = "Spells on you", Sign = "-",
                Color = PackedColor.Pack((byte)255, (byte)120, (byte)190),
                Tip = "Spell damage to you, and your vitals drained." },
            new CombatKindInfo { Kind = CombatKind.YouAvoid, Key = "youAvoid", Label = "Your defenses", Sign = "",
                Color = PackedColor.Pack((byte)255, (byte)225, (byte)120),
                Tip = "What you evaded, blocked, parried or resisted." },
            new CombatKindInfo { Kind = CombatKind.Healing, Key = "healing", Label = "Healing", Sign = "+",
                Color = PackedColor.Pack((byte)110, (byte)235, (byte)120),
                Tip = "Vitals healed or restored, by kits, spells and effects, on you or by you." },
            new CombatKindInfo { Kind = CombatKind.Kill, Key = "kill", Label = "Your kills", Yours = true, Sign = "",
                Color = PackedColor.Pack((byte)255, (byte)190, (byte)60),
                Tip = "What you killed." },
            new CombatKindInfo { Kind = CombatKind.Death, Key = "death", Label = "Your deaths", Sign = "",
                Color = PackedColor.Pack((byte)255, (byte)50, (byte)50),
                Tip = "What killed you." },
        };

        public static CombatKindInfo For(CombatKind kind) {
            return All[(int)kind];
        }
    }
}
