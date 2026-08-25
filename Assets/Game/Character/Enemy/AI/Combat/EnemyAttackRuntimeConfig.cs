using Game.Battle.Skill.Common;
using Game.Character.Enemy.Config;

namespace Game.Character.Enemy.AI.Combat
{
    public sealed class EnemyAttackRuntimeConfig
    {
        public EnemyAttackConfig EnemyConfig { get; }
        public SkillConfig SkillConfig { get; }
        public int SkillId => EnemyConfig.skillId;
        public string AnimationName => EnemyConfig.animationName;
        public float Weight => EnemyConfig.weight;
        public bool EnableAttackDistanceCheck => EnemyConfig.enableAttackDistanceCheck;
        public float AttackRange => SkillConfig.attackRange;

        /// <summary>绑定敌人攻击表现配置和全局技能战斗配置。</summary>
        public EnemyAttackRuntimeConfig(EnemyAttackConfig enemyConfig, SkillConfig skillConfig)
        {
            EnemyConfig = enemyConfig;
            SkillConfig = skillConfig;
        }
    }
}
