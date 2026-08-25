using System;

namespace Game.Character.Enemy.Config
{
    [Serializable]
    public sealed class EnemyCombatConfig
    {
        public EnemyAttackConfig[] basicAttacks =
        {
            new EnemyAttackConfig(20001, "Attack1", 1f)
        };
        public EnemyAttackConfig[] approachAttacks = new EnemyAttackConfig[0];
        public EnemyAttackConfig[] pursuitAttacks = new EnemyAttackConfig[0];
        public EnemyAttackConfig[] retreatAttacks = new EnemyAttackConfig[0];
        public float closeAttackPoolWeight = 1f;
        public float retreatAttackPoolWeight;
        public float retreatWeightBonusAfterCloseAttack;
        public float retreatWeightBonusLimit;
        public bool resetRetreatBonusAfterRetreat = true;
        public EnemyAttackConfig counterAttack;
        public EnemyComboBranchConfig[] comboBranches = new EnemyComboBranchConfig[0];
        public int counterBlockThreshold = 2;
        public float combatEnterRange = 4f;
        public float chaseRange = 6f;
        public float combatMemoryDuration = 4f;
        public bool canInterruptAttack;
    }
}
