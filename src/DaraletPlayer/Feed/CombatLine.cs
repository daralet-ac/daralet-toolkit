using System;

namespace DaraletPlayer.Feed {
    /// <summary>One line of combat in chat, read by CombatLines.</summary>
    public class CombatLine {
        public CombatKind Kind;
        /// <summary>The line as the game wrote it.</summary>
        public string Text;
        /// <summary>The points of damage or healing, or null for a line with none (an evade, a kill).</summary>
        public int? Amount;
        /// <summary>What a line without an amount says happened ("Evaded", "Blocked"...), for the floating text.</summary>
        public string Outcome;
        public bool Critical;
        /// <summary>The name of who it landed on, where the line gives one and it isn't you ("yourself" for a heal).</summary>
        public string Target;
        public DateTime At;
    }
}
