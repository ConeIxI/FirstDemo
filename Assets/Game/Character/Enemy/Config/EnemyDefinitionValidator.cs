using System;
using System.Collections.Generic;
using System.Linq;
using GameMain2.Scripts.UI;
using UnityEngine;

namespace Game.Character.Enemy.Config
{
    public sealed class EnemyDefinitionValidationResult
    {
        private readonly List<string> errors = new List<string>();

        public IReadOnlyList<string> Errors => errors;
        public bool IsValid => errors.Count == 0;

        // 添加校验错误，错误文本包含字段名便于测试和编辑器定位。
        public void AddError(string fieldName, string message)
        {
            errors.Add(fieldName + ": " + message);
        }

        // 判断是否存在指定字段的校验错误。
        public bool HasError(string fieldName)
        {
            return errors.Any(error => error.StartsWith(fieldName + ":", StringComparison.Ordinal));
        }
    }

    public static class EnemyDefinitionValidator
    {
        // 校验敌人定义的关键引用和数据约束，供编辑器保存前或测试使用。
        public static EnemyDefinitionValidationResult Validate(EnemyDefinition definition)
        {
            EnemyDefinitionValidationResult result = new EnemyDefinitionValidationResult();
            if (definition == null)
            {
                result.AddError("Definition", "敌人定义不能为空");
                return result;
            }

            if (string.IsNullOrWhiteSpace(definition.EnemyId))
            {
                result.AddError("EnemyId", "敌人 Id 不能为空");
            }

            if (definition.BehaviorTreeAsset == null)
            {
                result.AddError("BehaviorTreeAsset", "行为树资产不能为空");
            }

            ValidateMovementConfig(definition.MovementConfig, result);
            ValidatePerceptionConfig(definition.PerceptionConfig, result);
            ValidateCombatConfig(definition.CombatConfig, definition.PerceptionConfig, result);
            ValidateDecisionProfile(definition.DecisionProfile, result);
            ValidateAnimationConfig(definition.AnimationConfig, result);
            ValidateDecisionAnimationConfig(definition.AnimationConfig, result);
            ValidateAttributeConfig(definition.AttributeConfig, result);
            ValidateDropItems(definition.DropItems, result);

            return result;
        }

        // 校验移动配置是否存在，巡逻等待时间和攻击转向速度是否合法。
        private static void ValidateMovementConfig(EnemyMovementConfig movementConfig, EnemyDefinitionValidationResult result)
        {
            if (movementConfig == null)
            {
                result.AddError("MovementConfig", "移动配置不能为空");
                return;
            }

            if (movementConfig.patrolWaitDuration < 0f)
            {
                result.AddError("PatrolWaitDuration", "巡逻停留时间不能为负数");
            }

            if (movementConfig.attackRotateSpeed <= 0f)
            {
                result.AddError("AttackRotateSpeed", "攻击转向速度必须为正数");
            }
        }

        // 校验感知配置是否存在且警戒记忆和搜索观察时间合法。
        private static void ValidatePerceptionConfig(EnemyPerceptionConfig perceptionConfig, EnemyDefinitionValidationResult result)
        {
            if (perceptionConfig == null)
            {
                result.AddError("PerceptionConfig", "感知配置不能为空");
                return;
            }

            if (perceptionConfig.range <= 0f)
            {
                result.AddError("PerceptionRange", "感知范围必须为正数");
            }

            if (perceptionConfig.alertMemoryDuration <= 0f)
            {
                result.AddError("AlertMemoryDuration", "警戒记忆时间必须为正数");
            }

            if (perceptionConfig.searchObservationDuration < 0f)
            {
                result.AddError("SearchObservationDuration", "搜索观察时间不能为负数");
            }
        }

        // 校验战斗配置是否存在、攻击池与组合引用是否合法，且战斗范围严格递增。
        private static void ValidateCombatConfig(
            EnemyCombatConfig combatConfig,
            EnemyPerceptionConfig perceptionConfig,
            EnemyDefinitionValidationResult result)
        {
            if (combatConfig == null)
            {
                result.AddError("CombatConfig", "战斗配置不能为空");
                return;
            }

            HashSet<int> basicSkillIds = ValidateAttackPool("BasicAttacks", combatConfig.basicAttacks, true, result);
            HashSet<int> approachSkillIds = ValidateAttackPool("ApproachAttacks", combatConfig.approachAttacks, false, result);
            HashSet<int> pursuitSkillIds = ValidateAttackPool("PursuitAttacks", combatConfig.pursuitAttacks, false, result);
            HashSet<int> retreatSkillIds = ValidateAttackPool("RetreatAttacks", combatConfig.retreatAttacks, false, result);
            HashSet<int> comboSkillIds = new HashSet<int>(basicSkillIds);
            AddAttackPoolIdsToComboSet("ApproachAttacks", approachSkillIds, comboSkillIds, result);
            AddAttackPoolIdsToComboSet("PursuitAttacks", pursuitSkillIds, comboSkillIds, result);
            AddAttackPoolIdsToComboSet("RetreatAttacks", retreatSkillIds, comboSkillIds, result);
            ValidateCounterAttack(
                combatConfig.counterAttack,
                combatConfig.counterBlockThreshold,
                basicSkillIds,
                approachSkillIds,
                pursuitSkillIds,
                retreatSkillIds,
                result);
            ValidateComboBranches(combatConfig.comboBranches, comboSkillIds, result);

            if (combatConfig.combatMemoryDuration <= 0f)
            {
                result.AddError("CombatMemoryDuration", "战斗记忆时间必须为正数");
            }

            if (perceptionConfig != null
                && !(0f < combatConfig.combatEnterRange
                    && combatConfig.combatEnterRange < combatConfig.chaseRange
                    && combatConfig.chaseRange < perceptionConfig.range))
            {
                result.AddError("CombatRanges", "战斗范围、追击范围和视野范围必须严格递增");
            }
        }

        // 校验敌人决策配置的概率与冷却约束。
        private static void ValidateDecisionProfile(EnemyDecisionProfile profile, EnemyDefinitionValidationResult result)
        {
            if (profile == null)
            {
                result.AddError("DecisionProfile", "敌人决策配置不能为空");
                return;
            }

            AddErrorIfProbabilityInvalid(profile.attackDesire, "AttackDesire", result);
            AddErrorIfProbabilityInvalid(profile.defenseRate, "DefenseRate", result);
            AddErrorIfProbabilityInvalid(profile.dodgeRate, "DodgeRate", result);
            AddErrorIfProbabilityInvalid(profile.lowStabilityThreshold, "LowStabilityThreshold", result);

            if (profile.attackDecisionCooldown <= 0f)
            {
                result.AddError("AttackDecisionCooldown", "攻击决策冷却必须为正数");
            }

            if (profile.attackWeightCompensationPerMiss < 0f)
            {
                result.AddError("AttackWeightCompensationPerMiss", "攻击权重未选补偿不能为负数");
            }

            if (profile.attackWeightGuaranteeMissCount <= 0)
            {
                result.AddError("AttackWeightGuaranteeMissCount", "攻击权重保底次数必须为正数");
            }

            if (profile.defenseDuration <= 0f)
            {
                result.AddError("DefenseDuration", "防御持续时间必须为正数");
            }

            if (profile.dodgeCooldown < 0f)
            {
                result.AddError("DodgeCooldown", "闪避冷却不能为负数");
            }
        }

        // 校验概率值是否位于 0 到 1 之间。
        private static void AddErrorIfProbabilityInvalid(float value, string fieldName, EnemyDefinitionValidationResult result)
        {
            if (value < 0f || value > 1f)
            {
                result.AddError(fieldName, "概率值必须位于 0 到 1 之间");
            }
        }

        /// <summary>校验敌人掉落表，避免非法物品配置进入运行时生成流程。</summary>
        private static void ValidateDropItems(EnemyDropItemConfig[] dropItems, EnemyDefinitionValidationResult result)
        {
            if (dropItems == null)
            {
                return;
            }

            for (int i = 0; i < dropItems.Length; i++)
            {
                EnemyDropItemConfig item = dropItems[i];
                if (item == null)
                {
                    result.AddError("DropItems", "掉落项不能为空");
                    continue;
                }

                if (item.itemType == BagItemType.None)
                {
                    result.AddError("DropItems", "掉落物分类不能为 None");
                }

                if (item.itemId <= 0)
                {
                    result.AddError("DropItems", "掉落物 Id 必须为正数");
                }

                if (item.count <= 0)
                {
                    result.AddError("DropItems", "掉落数量必须为正数");
                }

                AddErrorIfProbabilityInvalid(item.dropChance, "DropItems", result);
            }
        }

        // 校验敌人攻击池中的必填字段和重复技能编号，并返回有效技能编号集合。
        private static HashSet<int> ValidateAttackPool(
            string fieldName,
            EnemyAttackConfig[] attacks,
            bool requireNonEmpty,
            EnemyDefinitionValidationResult result)
        {
            HashSet<int> skillIds = new HashSet<int>();
            if (attacks == null || attacks.Length == 0)
            {
                if (requireNonEmpty)
                {
                    result.AddError(fieldName, "攻击池不能为空");
                }

                return skillIds;
            }

            for (int i = 0; i < attacks.Length; i++)
            {
                EnemyAttackConfig attack = attacks[i];
                if (attack == null || attack.skillId <= 0 || string.IsNullOrWhiteSpace(attack.animationName) || attack.weight < 0f)
                {
                    result.AddError(fieldName, "攻击条目必须配置正数技能编号、动画名和非负权重");
                    continue;
                }

                if (!skillIds.Add(attack.skillId))
                {
                    result.AddError(fieldName, "存在重复技能编号：" + attack.skillId);
                }
            }

            return skillIds;
        }

        // 将攻击池技能编号并入组合可引用集合，并拒绝跨池重复带来的组合解析歧义。
        private static void AddAttackPoolIdsToComboSet(
            string fieldName,
            HashSet<int> sourceSkillIds,
            HashSet<int> comboSkillIds,
            EnemyDefinitionValidationResult result)
        {
            foreach (int skillId in sourceSkillIds)
            {
                if (!comboSkillIds.Add(skillId))
                {
                    result.AddError(fieldName, "普通攻击池之间存在重复技能编号：" + skillId);
                }
            }
        }

        // 校验反击攻击配置，并确保它不会混入普通候选攻击池。
        private static void ValidateCounterAttack(
            EnemyAttackConfig counterAttack,
            int counterBlockThreshold,
            HashSet<int> basicSkillIds,
            HashSet<int> approachSkillIds,
            HashSet<int> pursuitSkillIds,
            HashSet<int> retreatSkillIds,
            EnemyDefinitionValidationResult result)
        {
            if (counterAttack == null)
            {
                return;
            }

            if (counterAttack.skillId <= 0)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(counterAttack.animationName)
                || counterAttack.weight <= 0f)
            {
                result.AddError("CounterAttack", "反击条目必须配置正数技能编号、动画名和权重");
            }

            if (counterBlockThreshold <= 0)
            {
                result.AddError("CounterBlockThreshold", "反击格挡次数必须为正数");
            }

        }

        // 校验组合分支引用，确保每条分支只引用已声明的普通攻击池技能。
        private static void ValidateComboBranches(
            EnemyComboBranchConfig[] comboBranches,
            HashSet<int> comboSkillIds,
            EnemyDefinitionValidationResult result)
        {
            if (comboBranches == null)
            {
                return;
            }

            for (int i = 0; i < comboBranches.Length; i++)
            {
                EnemyComboBranchConfig branch = comboBranches[i];
                if (branch == null)
                {
                    result.AddError("ComboBranches", "组合分支不能为空");
                    continue;
                }

                if (!comboSkillIds.Contains(branch.startSkillId))
                {
                    result.AddError("ComboBranches", "组合起始技能必须引用普通攻击池");
                }

                if (branch.sequenceSkillIds == null || branch.sequenceSkillIds.Length == 0)
                {
                    result.AddError("ComboBranches", "组合后续序列不能为空");
                }
                else
                {
                    for (int skillIndex = 0; skillIndex < branch.sequenceSkillIds.Length; skillIndex++)
                    {
                        if (!comboSkillIds.Contains(branch.sequenceSkillIds[skillIndex]))
                        {
                            result.AddError("ComboBranches", "组合后续技能必须引用普通攻击池");
                        }
                    }
                }

                if (branch.probability <= 0f)
                {
                    result.AddError("ComboBranches", "组合分支概率必须为正数");
                }
            }
        }

        // 校验动画配置是否存在且关键动画名完整。
        private static void ValidateAnimationConfig(EnemyAnimationConfig animationConfig, EnemyDefinitionValidationResult result)
        {
            if (animationConfig == null)
            {
                result.AddError("AnimationConfig", "动画配置不能为空");
                return;
            }

            AddErrorIfEmpty(animationConfig.idleAnimation, "IdleAnimation", "待机动画名不能为空", result);
            AddErrorIfEmpty(animationConfig.combatIdleAnimation, "CombatIdleAnimation", "战斗待机动画名不能为空", result);
            AddErrorIfEmpty(animationConfig.combatIdleMoveLeftAnimation, "CombatIdleMoveLeftAnimation", "战斗待机左移动画名不能为空", result);
            AddErrorIfEmpty(animationConfig.combatIdleMoveRightAnimation, "CombatIdleMoveRightAnimation", "战斗待机右移动画名不能为空", result);
            AddErrorIfEmpty(animationConfig.alertMoveAnimation, "AlertMoveAnimation", "警戒移动动画名不能为空", result);
            AddErrorIfEmpty(animationConfig.moveAnimation, "MoveAnimation", "移动动画名不能为空", result);
            AddErrorIfEmpty(animationConfig.runAnimation, "RunAnimation", "奔跑动画名不能为空", result);
            AddErrorIfEmpty(animationConfig.getHitAnimation, "GetHitAnimation", "受击动画名不能为空", result);
            AddErrorIfEmpty(animationConfig.defenseBreakAnimation, "DefenseBreakAnimation", "失衡破防动画名不能为空", result);
            AddErrorIfEmpty(animationConfig.unbalanceStartAnimation, "UnbalanceStartAnimation", "失衡开始动画名不能为空", result);
            AddErrorIfEmpty(animationConfig.unbalanceStartTrigger, "UnbalanceStartTrigger", "失衡开始触发器不能为空", result);
            AddErrorIfEmpty(animationConfig.unbalanceLoopAnimation, "UnbalanceLoopAnimation", "失衡循环动画名不能为空", result);
            AddErrorIfEmpty(animationConfig.unbalanceEndAnimation, "UnbalanceEndAnimation", "失衡结束动画名不能为空", result);
            AddErrorIfEmpty(animationConfig.unbalanceEndTrigger, "UnbalanceEndTrigger", "失衡结束触发器不能为空", result);
            if (animationConfig.unbalanceLoopDuration <= 0f)
            {
                result.AddError("UnbalanceLoopDuration", "失衡循环保持时长必须为正数");
            }

            AddErrorIfEmpty(animationConfig.deadAnimation, "DeadAnimation", "死亡动画名不能为空", result);
        }

        // 校验决策层新增动画名，避免防御和后撤节点播放空动画。
        private static void ValidateDecisionAnimationConfig(EnemyAnimationConfig animationConfig, EnemyDefinitionValidationResult result)
        {
            if (animationConfig == null)
            {
                return;
            }

            AddErrorIfEmpty(animationConfig.defenseAnimation, "DefenseAnimation", "防御动画名不能为空", result);
            AddErrorIfEmpty(animationConfig.retreatAnimation, "RetreatAnimation", "后撤动画名不能为空", result);
        }

        // 校验属性配置是否存在且属性表 Id 不为空。
        private static void ValidateAttributeConfig(EnemyAttributeConfig attributeConfig, EnemyDefinitionValidationResult result)
        {
            if (attributeConfig == null)
            {
                result.AddError("AttributeConfig", "属性配置不能为空");
                return;
            }

        }

        // 在字符串为空时写入对应字段错误。
        private static void AddErrorIfEmpty(string value, string fieldName, string message, EnemyDefinitionValidationResult result)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                result.AddError(fieldName, message);
            }
        }
    }
}

