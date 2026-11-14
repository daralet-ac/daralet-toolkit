using System;
using System.Collections.Generic;
using DaraletPlayer.Feed;

namespace DaraletPlayer.Ui {
    /// <summary>
    /// Raising attributes, vitals and skills as the game's Attributes and Skills panels do: the costs from the client's own
    /// experience tables (ExperienceSystem, the portal dat's ExperienceTable as the client loaded it), read once and kept,
    /// and the raise itself the client's own message (CM_Train, every one static cdecl), which the server checks as ever.
    /// Game thread only, once logged in.
    /// </summary>
    internal static class ClientTraining {
        // the client's SKILL_ADVANCEMENT_CLASS
        public const uint Trained = 2;
        public const uint Specialized = 3;

        /// <summary>The most steps read from a table, in case a call says something silly.</summary>
        private const int MaxSteps = 1000;

        private static XpTable attributes;
        private static XpTable vitals;
        private static XpTable trained;
        private static XpTable specialized;
        private static bool broken;

        public static bool Broken => broken;

        public static XpTable Attributes => attributes ?? (attributes = Read("attribute", MaxAttributeLevel,
            ExperienceToAttributeLevel));

        public static XpTable Vitals => vitals ?? (vitals = Read("vital", MaxAttribute2ndLevel, ExperienceToAttribute2ndLevel));

        public static XpTable TrainedSkills => trained ?? (trained = Read("trained skill", MaxTrainedSkillLevel,
            level => ExperienceToSkillLevel(Trained, level)));

        public static XpTable SpecializedSkills => specialized ?? (specialized = Read("specialized skill",
            MaxSpecializedSkillLevel, level => ExperienceToSkillLevel(Specialized, level)));

        /// <summary>Spends XP on an attribute (STypeAttribute, 1 Strength to 6 Self): CM_Train::Event_TrainAttribute.</summary>
        public static unsafe bool RaiseAttribute(uint attribute, long xp) {
            return Send("attribute", attribute, xp, () =>
                ((delegate* unmanaged[Cdecl]<uint, uint, byte>)0x6A8E90)(attribute, (uint)xp));
        }

        /// <summary>
        /// Spends XP on a vital (STypeAttribute2nd: 1 health, 3 stamina, 5 mana, their maximums):
        /// CM_Train::Event_TrainAttribute2nd.
        /// </summary>
        public static unsafe bool RaiseVital(uint vital, long xp) {
            return Send("vital", vital, xp, () =>
                ((delegate* unmanaged[Cdecl]<uint, uint, byte>)0x6A8FA0)(vital, (uint)xp));
        }

        /// <summary>Spends XP on a trained or specialized skill (STypeSkill): CM_Train::Event_TrainSkill.</summary>
        public static unsafe bool RaiseSkill(uint skill, long xp) {
            return Send("skill", skill, xp, () =>
                ((delegate* unmanaged[Cdecl]<uint, uint, byte>)0x6A90B0)(skill, (uint)xp));
        }

        /// <summary>
        /// Spends skill credits to train an untrained skill: CM_Train::Event_TrainSkillAdvancementClass, with the credits
        /// the skill table says training costs.
        /// </summary>
        public static unsafe bool TrainSkill(uint skill, int credits) {
            return Send("train skill", skill, credits, () =>
                ((delegate* unmanaged[Cdecl]<uint, uint, byte>)0x6A91C0)(skill, (uint)credits));
        }

        private static bool Send(string what, uint type, long amount, Func<byte> call) {
            if (broken || amount <= 0 || amount > uint.MaxValue) {
                return false;
            }

            try {
                call();
                PluginCore.LogFile($"[training] {what} {type}: {amount} spent");
                return true;
            }
            catch (Exception ex) {
                Fail(what, ex);
                return false;
            }
        }

        /// <summary>A table read step by step, 0 raises to the most; null when it can't be (and it isn't tried again).</summary>
        private static XpTable Read(string what, Func<uint> max, Func<uint, uint> experience) {
            if (broken) {
                return null;
            }

            try {
                var most = Math.Min(MaxSteps, (int)max());
                var totals = new List<long>();

                for (var level = 0; level <= most; level++) {
                    var xp = experience((uint)level);

                    // past the table's end the client says -1
                    if (xp == uint.MaxValue) {
                        break;
                    }

                    totals.Add(xp);
                }

                // nothing to go on yet (the table not loaded): asked again next time, rather than kept as "can't raise"
                if (totals.Count < 2) {
                    return null;
                }

                PluginCore.LogFile($"[training] {what} table: {totals.Count} steps, the last {totals[totals.Count - 1]} XP");
                return new XpTable(totals);
            }
            catch (Exception ex) {
                Fail("read the " + what + " table", ex);
                return null;
            }
        }

        // ExperienceSystem's statics (0x5C9330...), each finding the portal dat's ExperienceTable and asking it

        private static unsafe uint ExperienceToAttributeLevel(uint level) =>
            ((delegate* unmanaged[Cdecl]<uint, uint>)0x5C9330)(level);

        private static unsafe uint ExperienceToAttribute2ndLevel(uint level) =>
            ((delegate* unmanaged[Cdecl]<uint, uint>)0x5C9370)(level);

        private static unsafe uint ExperienceToSkillLevel(uint advancementClass, uint level) =>
            ((delegate* unmanaged[Cdecl]<uint, uint, uint>)0x5C93B0)(advancementClass, level);

        private static unsafe uint MaxAttributeLevel() => ((delegate* unmanaged[Cdecl]<uint>)0x5C95B0)();

        private static unsafe uint MaxAttribute2ndLevel() => ((delegate* unmanaged[Cdecl]<uint>)0x5C95E0)();

        private static unsafe uint MaxTrainedSkillLevel() => ((delegate* unmanaged[Cdecl]<uint>)0x5C9580)();

        private static unsafe uint MaxSpecializedSkillLevel() => ((delegate* unmanaged[Cdecl]<uint>)0x5C9550)();

        private static void Fail(string what, Exception ex) {
            broken = true;
            PluginCore.Log($"The game's training can't be used ({what}), so raising is off until the plugin next starts: "
                + ex);
        }
    }
}
