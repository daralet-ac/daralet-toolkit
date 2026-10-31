namespace DaraletPlayer.Feed {
    /// <summary>What a combat line in chat says happened. Saved by name (CombatKindInfo.Key), so keep the keys when renaming.</summary>
    public enum CombatKind {
        YouHit,
        YouSpell,
        YouPeriodic,
        YourMiss,
        HitYou,
        SpellOnYou,
        YouAvoid,
        Healing,
        Kill,
        Death,
    }
}
