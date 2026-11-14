using System;
using DaraletPlayer.Feed;
using Decal.Adapter.Wrappers;

namespace DaraletPlayer.Ui {
    /// <summary>One line of the Attributes window's list (AttributesPanel): an attribute, a vital, a skill, or a group's heading.</summary>
    internal sealed class StatRow {
        /// <summary>What tells it from the others: selection and waiting raises go by it.</summary>
        public string Key;

        public string Name;

        /// <summary>A group's heading (Specialized...) rather than something to raise: its bar's picture.</summary>
        public string HeaderArt;

        /// <summary>Its icon from the portal dat, or null.</summary>
        public IconLayers Icon;

        /// <summary>Its value now, buffs and all, and without them: green above, red below.</summary>
        public int Value;
        public int Base;

        /// <summary>What's written for the value, when it isn't just the number (a vital's "current/max").</summary>
        public string ValueText;

        /// <summary>The XP spent on it, the table its raises cost from (null: it can't be raised with XP), and the raise.</summary>
        public long Spent;
        public XpTable Table;
        public Func<long, bool> Raise;

        /// <summary>An untrained skill: trained with skill credits instead.</summary>
        public bool Untrained;
        public CharFilterSkillType Skill;
        public int TrainCost;

        public string Tip;

        public bool IsHeader => HeaderArt != null;

        /// <summary>Above its base (buffed), below it (debuffed), or neither.</summary>
        public int Buff => Value - Base;
    }
}
