namespace DaraletPlayer.Feed {
    /// <summary>
    /// A creature's appraisal profile, as the server sends it (daralet-ACE's CreatureProfile): health always; the attributes,
    /// stamina and mana only when the appraisal succeeded (ShowAttributes); and which of them a spell has raised or lowered.
    /// </summary>
    public class CreatureStats {
        public const uint HasBuffsDebuffs = 0x1;
        public const uint ShowAttributes = 0x8;

        // AttributeMask: which bit is which, in Highlights and Colors
        public const int StrengthBit = 0x1;
        public const int EnduranceBit = 0x2;
        public const int QuicknessBit = 0x4;
        public const int CoordinationBit = 0x8;
        public const int FocusBit = 0x10;
        public const int SelfBit = 0x20;
        public const int HealthBit = 0x40;
        public const int StaminaBit = 0x80;
        public const int ManaBit = 0x100;

        public uint Flags;
        public uint Health;
        public uint HealthMax;
        public uint Strength;
        public uint Endurance;
        public uint Quickness;
        public uint Coordination;
        public uint Focus;
        public uint Self;
        public uint Stamina;
        public uint Mana;
        public uint StaminaMax;
        public uint ManaMax;

        /// <summary>The stats a spell has changed (AttributeMask bits).</summary>
        public int Highlights;

        /// <summary>For each highlighted stat, 1 raised (green), 0 lowered (red).</summary>
        public int Colors;

        public bool HasAttributes => (Flags & ShowAttributes) != 0;

        /// <summary>1 raised by a spell, -1 lowered, 0 neither.</summary>
        public int Effect(int bit) {
            return (Highlights & bit) == 0 ? 0 : (Colors & bit) != 0 ? 1 : -1;
        }
    }
}
