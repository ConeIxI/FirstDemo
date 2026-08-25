namespace Game.Battle.Ability
{
    public enum CombatFaction
    {
        Player,
        Enemy
    }

    public enum CombatTag
    {
        Dead,
        Unbalanced,
        Defending,
        ParryWindow,
        Invincible
    }

    public enum AbilityActivationResult
    {
        Success,
        Dead,
        Unbalanced,
        AlreadyActive,
        BlockedByTag,
        InsufficientResource
    }

    public enum CombatAttributeType
    {
        Health,
        Stability,
        BattleSpirit,
        Attack,
        Defense
    }
}
