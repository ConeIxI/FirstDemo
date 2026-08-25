using System;

namespace Game.Character.Enemy.Config
{
    [Serializable]
    public sealed class EnemyAttackConfig
    {
        public int skillId;
        public string animationName;
        public float weight = 1f;
        // 每个攻击动作独立控制是否校验技能攻击范围，默认保持原有距离检测行为。
        public bool enableAttackDistanceCheck = true;

        /// <summary>创建供 Unity 序列化使用的空攻击配置。</summary>
        public EnemyAttackConfig()
        {
        }

        /// <summary>创建指定技能、动画和选择权重的敌人攻击配置。</summary>
        public EnemyAttackConfig(int skillId, string animationName, float weight)
        {
            this.skillId = skillId;
            this.animationName = animationName;
            this.weight = weight;
        }
    }
}
