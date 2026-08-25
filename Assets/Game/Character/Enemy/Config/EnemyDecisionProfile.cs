using System;

namespace Game.Character.Enemy.Config
{
    [Serializable]
    public sealed class EnemyDecisionProfile
    {
        public float attackDesire = 0.45f;
        public float defenseRate = 0.2f;
        /// <summary>敌人进入防御后保持防御状态的时长，防御中格挡命中会刷新该时间。</summary>
        public float defenseDuration = 1.5f;
        public float attackDecisionCooldown = 1.2f;
        // 每次满足条件但未被选中的攻击动作增加的有效权重。
        public float attackWeightCompensationPerMiss = 0.5f;
        // 攻击动作连续满足条件但未被选中达到该次数后，下一次优先保底。
        public int attackWeightGuaranteeMissCount = 3;
        public float dodgeRate = 0.35f;
        public float dodgeCooldown = 2.5f;
        public float lowStabilityThreshold = 0.25f;

        /// <summary>判断稳定值比例是否低于自保阈值。</summary>
        public bool IsLowStability(float stabilityRatio)
        {
            return stabilityRatio <= lowStabilityThreshold;
        }
    }
}
