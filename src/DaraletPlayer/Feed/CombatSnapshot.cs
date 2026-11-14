namespace DaraletPlayer.Feed {
    /// <summary>What the client's combat system says now (Ui/ClientCombat.cs): what the game's combat panel shows.</summary>
    public struct CombatSnapshot {
        /// <summary>The client's COMBAT_MODE (PowerBarOptions.NonCombat, Melee, Missile, Magic).</summary>
        public int Mode;

        /// <summary>The attack height last asked for (PowerBarOptions.High, Medium, Low).</summary>
        public int Height;

        /// <summary>The bar is filling: an attack is being built.</summary>
        public bool Building;

        /// <summary>The bar's level, 0 to 1.</summary>
        public float Level;

        /// <summary>An attack button or key is held.</summary>
        public bool AttackHeld;

        /// <summary>The desired power, 0 to 1: the game's slider, which no attack goes past.</summary>
        public float Desired;

        /// <summary>The game's advanced combat option: attacks build to the desired power by themselves.</summary>
        public bool Advanced;
    }
}
