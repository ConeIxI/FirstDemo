using System;
using System.Collections.Generic;
using Game.Character.Enemy.AI.Combat;
using Game.Character.Enemy.Config;

namespace Game.Character.Enemy.AI
{
    public static class EnemyDecisionRandom
    {
        /// <summary>根据概率和随机值判断本次决策是否通过。</summary>
        public static bool Passes(float chance, float roll)
        {
            return roll <= chance;
        }

        /// <summary>按基础攻击权重选择技能，权重为空或总和无效时返回兜底技能。</summary>
        public static int SelectBasicAttack(EnemyAttackConfig[] attacks, float roll, int fallbackSkillId)
        {
            EnemyAttackConfig attack = SelectBasicAttackConfig(attacks, roll);
            return attack != null ? attack.skillId : fallbackSkillId;
        }

        /// <summary>按基础攻击权重选择完整动作配置，供兼容流程保存技能和动画。</summary>
        public static EnemyAttackConfig SelectBasicAttackConfig(EnemyAttackConfig[] attacks, float roll)
        {
            if (attacks == null || attacks.Length == 0)
            {
                return null;
            }

            float totalWeight = 0f;
            for (int i = 0; i < attacks.Length; i++)
            {
                if (attacks[i] != null && attacks[i].weight > 0f && attacks[i].skillId > 0)
                {
                    totalWeight += attacks[i].weight;
                }
            }

            if (totalWeight <= 0f)
            {
                return null;
            }

            float threshold = roll * totalWeight;
            float accumulated = 0f;
            for (int i = 0; i < attacks.Length; i++)
            {
                EnemyAttackConfig attack = attacks[i];
                if (attack == null || attack.weight <= 0f || attack.skillId <= 0)
                {
                    continue;
                }

                accumulated += attack.weight;
                if (threshold <= accumulated)
                {
                    return attack;
                }
            }

            return null;
        }

        /// <summary>从已过滤的攻击候选中按正数权重选择一个条目。</summary>
        public static EnemyAttackRuntimeConfig SelectAttack(
            IReadOnlyList<EnemyAttackRuntimeConfig> attacks,
            float roll)
        {
            return SelectAttack(attacks, roll, attack => attack.Weight);
        }

        /// <summary>按外部权重读取器从攻击候选中选择动作，保留权重为零动作不可选的规则。</summary>
        public static EnemyAttackRuntimeConfig SelectAttack(
            IReadOnlyList<EnemyAttackRuntimeConfig> attacks,
            float roll,
            Func<EnemyAttackRuntimeConfig, float> weightResolver)
        {
            if (attacks == null || attacks.Count == 0)
            {
                return null;
            }

            if (weightResolver == null)
            {
                throw new ArgumentNullException(nameof(weightResolver));
            }

            float totalWeight = 0f;
            for (int i = 0; i < attacks.Count; i++)
            {
                if (attacks[i] != null
                    && attacks[i].Weight > 0f
                    && attacks[i].SkillId > 0
                    && weightResolver(attacks[i]) > 0f)
                {
                    totalWeight += weightResolver(attacks[i]);
                }
            }

            if (totalWeight <= 0f)
            {
                return null;
            }

            float threshold = roll * totalWeight;
            float accumulated = 0f;
            for (int i = 0; i < attacks.Count; i++)
            {
                EnemyAttackRuntimeConfig attack = attacks[i];
                float weight = attack != null && attack.Weight > 0f && attack.SkillId > 0
                    ? weightResolver(attack)
                    : 0f;
                if (weight <= 0f)
                {
                    continue;
                }

                accumulated += weight;
                if (threshold <= accumulated)
                {
                    return attack;
                }
            }

            return null;
        }

        /// <summary>按组合分支概率选择一条路线，概率总和由运行时即时归一化。</summary>
        public static EnemyComboBranchConfig SelectComboBranch(EnemyComboBranchConfig[] branches, float roll)
        {
            if (branches == null || branches.Length == 0)
            {
                return null;
            }

            float totalProbability = 0f;
            for (int i = 0; i < branches.Length; i++)
            {
                if (branches[i] != null && branches[i].probability > 0f)
                {
                    totalProbability += branches[i].probability;
                }
            }

            if (totalProbability <= 0f)
            {
                return null;
            }

            float threshold = roll * totalProbability;
            float accumulated = 0f;
            for (int i = 0; i < branches.Length; i++)
            {
                EnemyComboBranchConfig branch = branches[i];
                if (branch == null || branch.probability <= 0f)
                {
                    continue;
                }

                accumulated += branch.probability;
                if (threshold <= accumulated)
                {
                    return branch;
                }
            }

            return null;
        }
    }
}
