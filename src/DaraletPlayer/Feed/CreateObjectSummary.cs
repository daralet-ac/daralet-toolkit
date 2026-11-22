using System;
using System.IO;
using System.Text;

namespace DaraletPlayer.Feed {
    /// <summary>
    /// What a CreateObject message (0xF745) says about an object's model and motion, for finding what the game was given
    /// before a teleport crash (Ui/ObjectWatch.cs; November 2026: the crashes were in the game reading animation and
    /// motion data on reaching places the dats unpack cleanly for). Read in daralet-ACE's order
    /// (WorldObject_Networking.SerializeCreateObject): the guid, the model data (aligned), the physics description (its
    /// flags, state, movement or animation frame, place, motion, sound and physics tables, setup, parent, children,
    /// scale and the rest, nine sequences, aligned), then the weenie header's flags, name, weenie class, icon and type.
    /// Alignment is from the message's start, its type included, as the server's writer counts.
    /// </summary>
    public sealed class CreateObjectSummary {
        public const uint Type = 0xF745;

        public uint Guid;
        public uint PhysicsFlags;
        public uint PhysicsState;
        public int MovementBytes = -1;
        public string MovementHead = "";
        public uint? Placement;
        public uint Setup;
        public uint MotionTable;
        public uint SoundTable;
        public uint PhysicsTable;
        public uint Parent;
        public int AnimPartChanges;
        public string Name = "";
        public uint Wcid;
        public uint ItemType;

        /// <summary>The message read, or null with why in problem.</summary>
        public static CreateObjectSummary Read(byte[] message, out string problem) {
            problem = null;

            if (message == null || message.Length < 16) {
                problem = "too short";
                return null;
            }

            try {
                using (var reader = new BinaryReader(new MemoryStream(message))) {
                    if (reader.ReadUInt32() != Type) {
                        problem = "not a CreateObject";
                        return null;
                    }

                    var o = new CreateObjectSummary { Guid = reader.ReadUInt32() };
                    ReadModel(reader, o);
                    ReadPhysics(reader, o);
                    reader.ReadUInt32(); // weenie header flags
                    o.Name = ReadString16L(reader);
                    o.Wcid = ReadPacked(reader);
                    ReadPacked(reader); // icon
                    o.ItemType = reader.ReadUInt32();
                    return o;
                }
            }
            catch (EndOfStreamException) {
                problem = "ran out";
                return null;
            }
        }

        private static void ReadModel(BinaryReader reader, CreateObjectSummary o) {
            reader.ReadByte(); // 0x11
            var palettes = reader.ReadByte();
            var textures = reader.ReadByte();
            var parts = reader.ReadByte();
            o.AnimPartChanges = parts;

            if (palettes > 0) {
                ReadPacked(reader);
            }

            for (var i = 0; i < palettes; i++) {
                ReadPacked(reader);
                reader.ReadByte();
                reader.ReadByte();
            }

            for (var i = 0; i < textures; i++) {
                reader.ReadByte();
                ReadPacked(reader);
                ReadPacked(reader);
            }

            for (var i = 0; i < parts; i++) {
                reader.ReadByte();
                ReadPacked(reader);
            }

            Align(reader);
        }

        private static void ReadPhysics(BinaryReader reader, CreateObjectSummary o) {
            var flags = o.PhysicsFlags = reader.ReadUInt32();
            o.PhysicsState = reader.ReadUInt32();

            if ((flags & 0x10000) != 0) {
                var length = reader.ReadInt32();
                o.MovementBytes = length;

                if (length > 0) {
                    var bytes = reader.ReadBytes(length);
                    o.MovementHead = BitConverter.ToString(bytes, 0, Math.Min(16, bytes.Length)).Replace("-", "");
                    reader.ReadUInt32(); // autonomous
                }
            }
            else if ((flags & 0x20000) != 0) {
                o.Placement = reader.ReadUInt32();
            }

            if ((flags & 0x8000) != 0) {
                reader.ReadBytes(32); // landblock, place, rotation
            }

            if ((flags & 0x2) != 0) {
                o.MotionTable = reader.ReadUInt32();
            }

            if ((flags & 0x800) != 0) {
                o.SoundTable = reader.ReadUInt32();
            }

            if ((flags & 0x1000) != 0) {
                o.PhysicsTable = reader.ReadUInt32();
            }

            if ((flags & 0x1) != 0) {
                o.Setup = reader.ReadUInt32();
            }

            if ((flags & 0x20) != 0) {
                o.Parent = reader.ReadUInt32();
                reader.ReadUInt32();
            }

            if ((flags & 0x40) != 0) {
                var children = reader.ReadInt32();
                reader.ReadBytes(children * 8);
            }

            // scale, friction, elasticity, translucency (a float each)
            foreach (var bit in new uint[] { 0x80, 0x100, 0x200, 0x40000 }) {
                if ((flags & bit) != 0) {
                    reader.ReadSingle();
                }
            }

            // velocity, acceleration, omega (three floats each)
            foreach (var bit in new uint[] { 0x4, 0x8, 0x10 }) {
                if ((flags & bit) != 0) {
                    reader.ReadBytes(12);
                }
            }

            if ((flags & 0x2000) != 0) {
                reader.ReadUInt32();
            }

            if ((flags & 0x4000) != 0) {
                reader.ReadSingle();
            }

            reader.ReadBytes(9 * 2); // the sequences
            Align(reader);
        }

        private static string ReadString16L(BinaryReader reader) {
            var length = reader.ReadUInt16();
            var text = Encoding.GetEncoding(1252).GetString(reader.ReadBytes(length));
            var used = (2 + length) % 4;

            if (used != 0) {
                reader.ReadBytes(4 - used);
            }

            return text;
        }

        /// <summary>A packed dword: 15 bits, or with the high bit set, 31 bits over two halves (high half first).</summary>
        private static uint ReadPacked(BinaryReader reader) {
            var first = reader.ReadUInt16();

            if ((first & 0x8000) == 0) {
                return first;
            }

            var second = reader.ReadUInt16();
            return ((uint)(first & 0x7FFF) << 16) | second;
        }

        private static void Align(BinaryReader reader) {
            var at = reader.BaseStream.Position % 4;

            if (at != 0) {
                reader.ReadBytes((int)(4 - at));
            }
        }

        /// <summary>One line for log.txt.</summary>
        public string Describe() {
            return $"{Guid:X8} '{Name}' wcid {Wcid} type {ItemType:X} setup {Setup:X8} mtable {MotionTable:X8} "
                + $"stable {SoundTable:X8} petable {PhysicsTable:X8} parts {AnimPartChanges} physics {PhysicsFlags:X} "
                + $"state {PhysicsState:X}"
                + (MovementBytes >= 0 ? $" movement {MovementBytes} {MovementHead}" : "")
                + (Placement.HasValue ? $" placement {Placement.Value}" : "")
                + (Parent != 0 ? $" parent {Parent:X8}" : "");
        }
    }
}
