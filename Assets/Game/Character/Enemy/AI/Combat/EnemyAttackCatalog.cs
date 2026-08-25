using System;
using System.Collections.Generic;
using Game.Battle.Skill.Common;
using Game.Character.Enemy.Config;

namespace Game.Character.Enemy.AI.Combat
{
    public sealed class EnemyAttackCatalog
    {
        private readonly Dictionary<int, EnemyAttackRuntimeConfig> comboAttacksBySkillId;

        public IReadOnlyList<EnemyAttackRuntimeConfig> BasicAttacks { get; }
        public IReadOnlyList<EnemyAttackRuntimeConfig> ApproachAttacks { get; }
        public IReadOnlyList<EnemyAttackRuntimeConfig> PursuitAttacks { get; }
        public IReadOnlyList<EnemyAttackRuntimeConfig> RetreatAttacks { get; }
        public EnemyAttackRuntimeConfig CounterAttack { get; }
        public float BasicAttackRange { get; }

        /// <summary>保存已解析的攻击池，并建立组合攻击可引用的普通攻击索引。</summary>
        private EnemyAttackCatalog(
            EnemyAttackRuntimeConfig[] basicAttacks,
            EnemyAttackRuntimeConfig[] approachAttacks,
            EnemyAttackRuntimeConfig[] pursuitAttacks,
            EnemyAttackRuntimeConfig[] retreatAttacks,
            EnemyAttackRuntimeConfig counterAttack)
        {
            BasicAttacks = basicAttacks;
            ApproachAttacks = approachAttacks;
            PursuitAttacks = pursuitAttacks;
            RetreatAttacks = retreatAttacks;
            CounterAttack = counterAttack;
            comboAttacksBySkillId = BuildComboAttackMap(basicAttacks, approachAttacks, pursuitAttacks, retreatAttacks);
            BasicAttackRange = CalculateBasicAttackRange(basicAttacks);
        }

        /// <summary>从敌人表现配置和全局技能解析器创建运行时攻击目录。</summary>
        public static EnemyAttackCatalog Create(EnemyCombatConfig config, Func<int, SkillConfig> skillResolver)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            if (skillResolver == null)
            {
                throw new ArgumentNullException(nameof(skillResolver));
            }

            EnemyAttackRuntimeConfig[] basicAttacks = BindAttackPool(config.basicAttacks, skillResolver);
            EnemyAttackRuntimeConfig[] approachAttacks = BindAttackPool(config.approachAttacks, skillResolver);
            EnemyAttackRuntimeConfig[] pursuitAttacks = BindAttackPool(config.pursuitAttacks, skillResolver);
            EnemyAttackRuntimeConfig[] retreatAttacks = BindAttackPool(config.retreatAttacks, skillResolver);
            EnemyAttackRuntimeConfig counterAttack = config.counterAttack != null && config.counterAttack.skillId > 0
                ? BindAttack(config.counterAttack, skillResolver)
                : null;
            return new EnemyAttackCatalog(basicAttacks, approachAttacks, pursuitAttacks, retreatAttacks, counterAttack);
        }

        /// <summary>按技能编号读取组合攻击可用动作，缺失代表配置没有通过结构校验。</summary>
        public EnemyAttackRuntimeConfig GetRequiredComboAttack(int skillId)
        {
            if (!comboAttacksBySkillId.TryGetValue(skillId, out EnemyAttackRuntimeConfig attack))
            {
                throw new InvalidOperationException("组合分支引用了未声明的普通攻击技能：" + skillId);
            }

            return attack;
        }

        /// <summary>绑定一个攻击池内的全部敌人攻击配置。</summary>
        private static EnemyAttackRuntimeConfig[] BindAttackPool(
            EnemyAttackConfig[] attacks,
            Func<int, SkillConfig> skillResolver)
        {
            if (attacks == null || attacks.Length == 0)
            {
                return new EnemyAttackRuntimeConfig[0];
            }

            EnemyAttackRuntimeConfig[] result = new EnemyAttackRuntimeConfig[attacks.Length];
            for (int i = 0; i < attacks.Length; i++)
            {
                result[i] = BindAttack(attacks[i], skillResolver);
            }

            return result;
        }

        /// <summary>绑定单个敌人攻击配置和对应全局技能配置。</summary>
        private static EnemyAttackRuntimeConfig BindAttack(
            EnemyAttackConfig attack,
            Func<int, SkillConfig> skillResolver)
        {
            if (attack == null)
            {
                throw new InvalidOperationException("敌人攻击配置不能为空");
            }

            try
            {
                SkillConfig skillConfig = skillResolver(attack.skillId);
                if (skillConfig == null)
                {
                    throw new InvalidOperationException("全局技能配置不能为空");
                }

                return new EnemyAttackRuntimeConfig(attack, skillConfig);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException("敌人攻击技能解析失败：" + attack.skillId, exception);
            }
        }

        /// <summary>建立全部普通攻击池技能编号到运行时攻击条目的索引，供组合分支解析。</summary>
        private static Dictionary<int, EnemyAttackRuntimeConfig> BuildComboAttackMap(
            params EnemyAttackRuntimeConfig[][] attackPools)
        {
            Dictionary<int, EnemyAttackRuntimeConfig> result = new Dictionary<int, EnemyAttackRuntimeConfig>();
            for (int poolIndex = 0; poolIndex < attackPools.Length; poolIndex++)
            {
                EnemyAttackRuntimeConfig[] attacks = attackPools[poolIndex];
                for (int attackIndex = 0; attackIndex < attacks.Length; attackIndex++)
                {
                    int skillId = attacks[attackIndex].SkillId;
                    if (result.ContainsKey(skillId))
                    {
                        throw new InvalidOperationException("普通攻击池之间存在重复技能编号：" + skillId);
                    }

                    result.Add(skillId, attacks[attackIndex]);
                }
            }

            return result;
        }

        /// <summary>计算基础攻击池中覆盖范围最大的技能范围。</summary>
        private static float CalculateBasicAttackRange(EnemyAttackRuntimeConfig[] basicAttacks)
        {
            if (basicAttacks.Length == 0)
            {
                throw new InvalidOperationException("基础攻击池不能为空");
            }

            float result = 0f;
            for (int i = 0; i < basicAttacks.Length; i++)
            {
                result = Math.Max(result, basicAttacks[i].AttackRange);
            }

            return result;
        }
    }
}
