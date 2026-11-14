using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// A creature's or player's appraisal, read from the server's own message (game event 0x00C9, SetAppraiseInfo), since
    /// Decal doesn't pass on what makes a creature's: its profile (health, attributes, stamina and mana), which of those a
    /// spell has changed, and a player's armor by body part. Read in daralet-ACE's AppraiseInfo.Write order: the object,
    /// the flags saying what follows, whether the appraisal succeeded, then each table and profile the flags name.
    /// </summary>
    public class CreatureAppraisal {
        public const uint SetAppraiseInfo = 0x00C9;

        // IdentifyResponseFlags
        private const uint IntStats = 0x1;
        private const uint BoolStats = 0x2;
        private const uint FloatStats = 0x4;
        private const uint StringStats = 0x8;
        private const uint SpellBook = 0x10;
        private const uint WeaponProfile = 0x20;
        private const uint HookProfile = 0x40;
        private const uint ArmorProfile = 0x80;
        private const uint CreatureProfile = 0x100;
        private const uint ArmorEnchantments = 0x200;
        private const uint ResistEnchantments = 0x400;
        private const uint WeaponEnchantments = 0x800;
        private const uint DidStats = 0x1000;
        private const uint Int64Stats = 0x2000;
        private const uint ArmorLevelsFlag = 0x4000;

        private static readonly Encoding Text = Encoding.GetEncoding(1252);

        public uint Guid;
        public bool Success;
        public readonly Dictionary<uint, int> Ints = new Dictionary<uint, int>();
        public readonly Dictionary<uint, long> Int64s = new Dictionary<uint, long>();
        public readonly Dictionary<uint, bool> Bools = new Dictionary<uint, bool>();
        public readonly Dictionary<uint, double> Floats = new Dictionary<uint, double>();
        public readonly Dictionary<uint, string> Strings = new Dictionary<uint, string>();
        public readonly Dictionary<uint, uint> Dids = new Dictionary<uint, uint>();
        public readonly List<uint> Spells = new List<uint>();
        public CreatureStats Creature;

        /// <summary>Which resistances a spell has changed, and which way (ResistMask bits); 0 for none.</summary>
        public int ResistHighlights;
        public int ResistColors;

        /// <summary>
        /// Armor by body part (head, chest, abdomen, upper arm, lower arm, hand, upper leg, lower leg, foot), sent for a
        /// player or a creature that can't be attacked; null for none.
        /// </summary>
        public uint[] ArmorLevels;

        public int? Int(uint key) => Ints.TryGetValue(key, out var value) ? value : (int?)null;

        public string String(uint key) => Strings.TryGetValue(key, out var value) ? value : null;

        /// <summary>
        /// The appraisal a message carries, if it's one of a creature (it has a creature profile); null for any other
        /// message, an item's appraisal, or one too short to be what it says.
        /// </summary>
        public static CreatureAppraisal Read(byte[] message) {
            if (message == null || message.Length < 28) {
                return null;
            }

            try {
                using (var reader = new BinaryReader(new MemoryStream(message))) {
                    if (reader.ReadUInt32() != CooldownMessages.GameEvent) {
                        return null;
                    }

                    reader.ReadUInt32(); // your guid
                    reader.ReadUInt32(); // the event's sequence number

                    if (reader.ReadUInt32() != SetAppraiseInfo) {
                        return null;
                    }

                    var appraisal = new CreatureAppraisal { Guid = reader.ReadUInt32() };
                    var flags = reader.ReadUInt32();

                    if ((flags & CreatureProfile) == 0) {
                        return null;
                    }

                    appraisal.Success = reader.ReadUInt32() != 0;
                    appraisal.ReadBody(reader, flags);
                    return appraisal;
                }
            }
            catch (EndOfStreamException) {
                return null;
            }
        }

        private void ReadBody(BinaryReader reader, uint flags) {
            if ((flags & IntStats) != 0) {
                ReadTable(reader, r => Ints[r.ReadUInt32()] = r.ReadInt32());
            }

            if ((flags & Int64Stats) != 0) {
                ReadTable(reader, r => Int64s[r.ReadUInt32()] = r.ReadInt64());
            }

            if ((flags & BoolStats) != 0) {
                ReadTable(reader, r => Bools[r.ReadUInt32()] = r.ReadUInt32() != 0);
            }

            if ((flags & FloatStats) != 0) {
                ReadTable(reader, r => Floats[r.ReadUInt32()] = r.ReadDouble());
            }

            if ((flags & StringStats) != 0) {
                ReadTable(reader, r => Strings[r.ReadUInt32()] = ReadString(r));
            }

            if ((flags & DidStats) != 0) {
                ReadTable(reader, r => Dids[r.ReadUInt32()] = r.ReadUInt32());
            }

            if ((flags & SpellBook) != 0) {
                for (var count = reader.ReadUInt32(); count > 0; count--) {
                    Spells.Add(reader.ReadUInt32());
                }
            }

            if ((flags & ArmorProfile) != 0) {
                reader.ReadBytes(8 * 4); // eight protections, as floats
            }

            Creature = ReadCreature(reader);

            if ((flags & WeaponProfile) != 0) {
                // damage type, speed, skill, damage; variance, mod, length, velocity, offense; estimated velocity
                reader.ReadBytes(4 * 4 + 5 * 8 + 4);
            }

            if ((flags & HookProfile) != 0) {
                reader.ReadBytes(3 * 4);
            }

            if ((flags & ArmorEnchantments) != 0) {
                reader.ReadBytes(2 * 2);
            }

            if ((flags & WeaponEnchantments) != 0) {
                reader.ReadBytes(2 * 2);
            }

            if ((flags & ResistEnchantments) != 0) {
                ResistHighlights = reader.ReadUInt16();
                ResistColors = reader.ReadUInt16();
            }

            if ((flags & ArmorLevelsFlag) != 0) {
                ArmorLevels = new uint[9];

                for (var i = 0; i < ArmorLevels.Length; i++) {
                    ArmorLevels[i] = reader.ReadUInt32();
                }
            }
        }

        private static CreatureStats ReadCreature(BinaryReader reader) {
            var stats = new CreatureStats {
                Flags = reader.ReadUInt32(),
                Health = reader.ReadUInt32(),
                HealthMax = reader.ReadUInt32(),
            };

            if (stats.HasAttributes) {
                stats.Strength = reader.ReadUInt32();
                stats.Endurance = reader.ReadUInt32();
                stats.Quickness = reader.ReadUInt32();
                stats.Coordination = reader.ReadUInt32();
                stats.Focus = reader.ReadUInt32();
                stats.Self = reader.ReadUInt32();
                stats.Stamina = reader.ReadUInt32();
                stats.Mana = reader.ReadUInt32();
                stats.StaminaMax = reader.ReadUInt32();
                stats.ManaMax = reader.ReadUInt32();
            }

            if ((stats.Flags & CreatureStats.HasBuffsDebuffs) != 0) {
                stats.Highlights = reader.ReadUInt16();
                stats.Colors = reader.ReadUInt16();
            }

            return stats;
        }

        /// <summary>A PackableHashTable: a count and a bucket count (two ushorts), then each entry.</summary>
        private static void ReadTable(BinaryReader reader, Action<BinaryReader> entry) {
            int count = reader.ReadUInt16();
            reader.ReadUInt16();

            for (var i = 0; i < count; i++) {
                entry(reader);
            }
        }

        /// <summary>A String16L: its length, its bytes (code page 1252), padded so the whole is a multiple of 4.</summary>
        private static string ReadString(BinaryReader reader) {
            int length = reader.ReadUInt16();
            var bytes = reader.ReadBytes(length);

            if (bytes.Length < length) {
                throw new EndOfStreamException();
            }

            var pad = (4 - (2 + length) % 4) % 4;
            reader.ReadBytes(pad);
            return Text.GetString(bytes);
        }
    }
}
