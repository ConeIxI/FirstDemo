namespace Game.Character.Enemy.AI.Combat
{
    public enum EnemyCombatDecisionState
    {
        Confrontation,
        Attack,
        Defense,
        Dodge
    }

    public enum EnemyAttackPhase
    {
        None,
        Start,
        Active,
        End
    }

    public enum EnemyCombatReaction
    {
        None,
        Defense,
        Dodge
    }
}
