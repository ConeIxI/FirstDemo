using System;
using System.Collections.Generic;
using Game.Battle.Skill.Common;
using Game.Character.Enemy.Config;

namespace Game.Character.Enemy.AI.Combat
{
    public sealed class EnemyCombatDecisionController
    {
        private const float CompatibilityAttackRange = 1.6f;

        private readonly EnemyCombatConfig combatConfig;
        private readonly EnemyDecisionProfile profile;
        private readonly EnemyAttackCatalog attackCatalog;
        private readonly Dictionary<EnemyAttackRuntimeConfig, int> attackMissCounts =
            new Dictionary<EnemyAttackRuntimeConfig, int>();
        private readonly Dictionary<int, EnemyComboBranchConfig[]> comboBranchesByStartSkillId =
            new Dictionary<int, EnemyComboBranchConfig[]>();
        private int defenseBlockCount;
        private float lastAttackDecisionTime = float.NegativeInfinity;
        private float lastDodgeTime = float.NegativeInfinity;
        private float retreatWeightBonus;

        public EnemyCombatDecisionState State { get; private set; } = EnemyCombatDecisionState.Confrontation;
        public EnemyAttackPhase AttackPhase { get; private set; }
        public EnemyCombatReaction PendingReaction { get; private set; }
        public bool HasPendingCounter { get; private set; }
        public EnemyAttackPlan CurrentPlan { get; private set; }
        public int CurrentSkillId { get; private set; }
        public string CurrentAnimationName { get; private set; }

        /// <summary>创建兼容旧行为树入口的战斗决策器，临时使用固定范围构建攻击目录。</summary>
        public EnemyCombatDecisionController(EnemyCombatConfig combatConfig, EnemyDecisionProfile profile)
            : this(combatConfig, profile, CreateCompatibilityCatalog(combatConfig))
        {
        }

        /// <summary>使用战斗配置、决策配置和已解析攻击目录创建决策器。</summary>
        public EnemyCombatDecisionController(
            EnemyCombatConfig combatConfig,
            EnemyDecisionProfile profile,
            EnemyAttackCatalog attackCatalog)
        {
            this.combatConfig = combatConfig ?? throw new ArgumentNullException(nameof(combatConfig));
            this.profile = profile ?? throw new ArgumentNullException(nameof(profile));
            this.attackCatalog = attackCatalog ?? throw new ArgumentNullException(nameof(attackCatalog));
            BuildComboBranchMap(combatConfig.comboBranches);
        }

        /// <summary>根据稳定值、距离条件、攻击冷却和攻击欲望尝试生成旧攻击意图。</summary>
        public bool TryCreateAttackIntent(
            float time,
            float stabilityRatio,
            bool isInChaseRange,
            float randomValue)
        {
            if (profile.IsLowStability(stabilityRatio))
            {
                return false;
            }

            if (State == EnemyCombatDecisionState.Attack && AttackPhase != EnemyAttackPhase.None)
            {
                return true;
            }

            bool isOutsideChaseRange = !isInChaseRange;
            if (!isOutsideChaseRange
                && time - lastAttackDecisionTime < profile.attackDecisionCooldown)
            {
                return false;
            }

            lastAttackDecisionTime = time;
            if (isOutsideChaseRange || EnemyDecisionRandom.Passes(profile.attackDesire, randomValue))
            {
                State = EnemyCombatDecisionState.Attack;
                AttackPhase = EnemyAttackPhase.None;
                PendingReaction = EnemyCombatReaction.None;
                CurrentPlan = null;
                CurrentSkillId = 0;
                CurrentAnimationName = null;
                return true;
            }

            return false;
        }

        /// <summary>记录一次防御格挡命中，达到阈值后标记本轮防御可以转入反击。</summary>
        public void RecordDefenseBlock()
        {
            defenseBlockCount++;
            if (defenseBlockCount >= combatConfig.counterBlockThreshold)
            {
                HasPendingCounter = true;
            }
        }

        /// <summary>在待反击、距离和朝向都满足时创建反击攻击计划。</summary>
        public bool TryCreateCounterPlan(float distanceToTarget, bool isTargetInFront)
        {
            if (!HasPendingCounter || !isTargetInFront)
            {
                return false;
            }

            EnemyAttackRuntimeConfig counterAttack = attackCatalog.CounterAttack;
            if (counterAttack == null)
            {
                return false;
            }

            if (counterAttack.Weight <= 0f)
            {
                return false;
            }

            if (counterAttack.EnableAttackDistanceCheck && distanceToTarget > counterAttack.AttackRange)
            {
                return false;
            }

            bool created = CreateAttackPlan(
                EnemyAttackPlanType.Counter,
                EnemyAttackPreparationMode.Direct,
                counterAttack,
                counterAttack.AttackRange);
            if (created)
            {
                ResetDefense();
            }

            return created;
        }

        /// <summary>按距离、追击边界和权重生成并锁定攻击计划。</summary>
        public bool TryCreateAttackPlan(
            float time,
            float stabilityRatio,
            float distanceToTarget,
            float chaseRange,
            float randomValue)
        {
            return TryCreateAttackPlan(
                time,
                stabilityRatio,
                distanceToTarget,
                chaseRange,
                randomValue,
                randomValue,
                randomValue);
        }

        /// <summary>按独立随机值生成攻击计划，避免攻击欲望、攻击池和技能选择互相污染。</summary>
        public bool TryCreateAttackPlan(
            float time,
            float stabilityRatio,
            float distanceToTarget,
            float chaseRange,
            float attackDesireRoll,
            float poolSelectionRoll,
            float attackSelectionRoll)
        {
            if (profile.IsLowStability(stabilityRatio))
            {
                return false;
            }

            if (CurrentPlan != null)
            {
                return true;
            }

            if (distanceToTarget > chaseRange)
            {
                EnemyAttackRuntimeConfig pursuitAttack = SelectAttackWithCompensation(
                    attackCatalog.PursuitAttacks,
                    attackSelectionRoll);
                if (pursuitAttack != null)
                {
                    return CreateAttackPlan(
                        EnemyAttackPlanType.Pursuit,
                        EnemyAttackPreparationMode.Pursuit,
                        pursuitAttack,
                        pursuitAttack.AttackRange * 0.8f);
                }

                return CreateFallbackBasicPursuitPlan(attackSelectionRoll);
            }

            if (time - lastAttackDecisionTime < profile.attackDecisionCooldown)
            {
                return false;
            }

            lastAttackDecisionTime = time;
            if (!EnemyDecisionRandom.Passes(profile.attackDesire, attackDesireRoll))
            {
                return false;
            }

            if (TryCreateApproachPlan(distanceToTarget, attackSelectionRoll))
            {
                return true;
            }

            return TryCreateCloseRangeRhythmPlan(distanceToTarget, poolSelectionRoll, attackSelectionRoll);
        }

        /// <summary>开始当前攻击流程的首段攻击，后续重复调用不会重新投随机。</summary>
        public bool TryBeginCurrentAttack(float randomValue)
        {
            if (State != EnemyCombatDecisionState.Attack)
            {
                return false;
            }

            if (CurrentSkillId > 0)
            {
                return true;
            }

            EnemyAttackRuntimeConfig attack = CurrentPlan != null
                ? CurrentPlan.CurrentAttack
                : SelectAttackWithCompensation(attackCatalog.BasicAttacks, randomValue);
            if (attack == null)
            {
                return false;
            }

            if (CurrentPlan == null)
            {
                CurrentPlan = new EnemyAttackPlan(
                    EnemyAttackPlanType.Basic,
                    EnemyAttackPreparationMode.Direct,
                    attack,
                    attack.AttackRange);
            }

            SetCurrentAttack(attack);
            AttackPhase = EnemyAttackPhase.Start;
            return true;
        }

        /// <summary>在距离和朝向满足时推进已锁定的组合路线。</summary>
        public bool TryAdvanceCombo(float randomValue, float distanceToTarget, bool isTargetInFront)
        {
            if (CurrentPlan == null || CurrentSkillId <= 0)
            {
                return false;
            }

            if (!isTargetInFront)
            {
                ResetAttack();
                return false;
            }

            if (!CurrentPlan.HasComboRoute && !TryLockComboRoute(CurrentSkillId, randomValue))
            {
                ResetAttack();
                return false;
            }

            if (!CurrentPlan.TryPeekNextComboAttack(out EnemyAttackRuntimeConfig nextAttack))
            {
                ResetAttack();
                return false;
            }

            if (nextAttack.EnableAttackDistanceCheck && distanceToTarget > nextAttack.AttackRange)
            {
                ResetAttack();
                return false;
            }

            CurrentPlan.AdvanceToNextComboAttack();
            SetCurrentAttack(nextAttack);
            AttackPhase = EnemyAttackPhase.Start;
            return true;
        }

        /// <summary>动画提前连招窗口尝试推进组合段；条件不成立时保留当前攻击，交给动画结束收尾处理。</summary>
        public bool TryAdvanceComboFromAnimationEvent(float randomValue, float distanceToTarget, bool isTargetInFront)
        {
            if (CurrentPlan == null || CurrentSkillId <= 0 || !isTargetInFront)
            {
                return false;
            }

            if (!CurrentPlan.HasComboRoute && !TryLockComboRoute(CurrentSkillId, randomValue))
            {
                return false;
            }

            if (!CurrentPlan.TryPeekNextComboAttack(out EnemyAttackRuntimeConfig nextAttack))
            {
                return false;
            }

            if (nextAttack.EnableAttackDistanceCheck && distanceToTarget > nextAttack.AttackRange)
            {
                return false;
            }

            CurrentPlan.AdvanceToNextComboAttack();
            SetCurrentAttack(nextAttack);
            AttackPhase = EnemyAttackPhase.Start;
            return true;
        }

        /// <summary>按当前状态、距离和朝向即时处理玩家默认攻击输入。</summary>
        public EnemyCombatReaction TryHandlePlayerAttackInput(
            float time,
            float stabilityRatio,
            float distanceToPlayer,
            float playerDefaultAttackRange,
            bool isPlayerInFront,
            float randomValue)
        {
            if (!CanReceivePlayerAttackInput()
                || distanceToPlayer > playerDefaultAttackRange
                || !isPlayerInFront)
            {
                return EnemyCombatReaction.None;
            }

            if (profile.IsLowStability(stabilityRatio)
                && IsDodgeCooldownReady(time)
                && EnemyDecisionRandom.Passes(profile.dodgeRate, randomValue))
            {
                return SetReaction(EnemyCombatReaction.Dodge, time);
            }

            if (EnemyDecisionRandom.Passes(profile.defenseRate, randomValue))
            {
                return SetReaction(EnemyCombatReaction.Defense, time);
            }

            return EnemyCombatReaction.None;
        }

        /// <summary>显式设置攻击阶段，供行为树动作节点同步动画生命周期。</summary>
        public void SetAttackPhase(EnemyAttackPhase phase)
        {
            AttackPhase = phase;
            if (phase != EnemyAttackPhase.None)
            {
                State = EnemyCombatDecisionState.Attack;
            }
        }

        /// <summary>进入防御或闪避运行态，并清掉已被节点消费的待处理反应。</summary>
        public void EnterReactionState(EnemyCombatReaction reaction)
        {
            PendingReaction = EnemyCombatReaction.None;
            if (reaction == EnemyCombatReaction.Defense)
            {
                ResetDefense();
                State = EnemyCombatDecisionState.Defense;
                return;
            }

            if (reaction == EnemyCombatReaction.Dodge)
            {
                State = EnemyCombatDecisionState.Dodge;
            }
        }

        /// <summary>重置攻击意图、阶段、当前动作和锁定组合路线。</summary>
        public void ResetAttack()
        {
            State = EnemyCombatDecisionState.Confrontation;
            AttackPhase = EnemyAttackPhase.None;
            CurrentPlan = null;
            CurrentSkillId = 0;
            CurrentAnimationName = null;
        }

        /// <summary>清理待执行反应，并在反应状态结束后回到对峙。</summary>
        /// <summary>记录当前攻击计划已经完整执行，用于推进攻击池节奏状态。</summary>
        public void CompleteCurrentPlan()
        {
            EnemyAttackPlan plan = CurrentPlan;
            if (plan == null)
            {
                return;
            }

            RecordCompletedAttackPlan(plan.Type, plan.PreparationMode);
        }

        /// <summary>根据已完成计划类型更新远离池权重加成。</summary>
        private void RecordCompletedAttackPlan(
            EnemyAttackPlanType type,
            EnemyAttackPreparationMode preparationMode)
        {
            if (type == EnemyAttackPlanType.Basic && preparationMode == EnemyAttackPreparationMode.Direct)
            {
                retreatWeightBonus = Math.Min(
                    retreatWeightBonus + combatConfig.retreatWeightBonusAfterCloseAttack,
                    combatConfig.retreatWeightBonusLimit);
                return;
            }

            if (type == EnemyAttackPlanType.Retreat && combatConfig.resetRetreatBonusAfterRetreat)
            {
                retreatWeightBonus = 0f;
            }
        }

        /// <summary>清理待执行反应，并在反应状态结束后回到对峙。</summary>
        public void ClearReaction()
        {
            PendingReaction = EnemyCombatReaction.None;
            if (State == EnemyCombatDecisionState.Defense || State == EnemyCombatDecisionState.Dodge)
            {
                State = EnemyCombatDecisionState.Confrontation;
            }
        }

        /// <summary>清理防御期间累计的格挡次数和待反击标记。</summary>
        public void ResetDefense()
        {
            defenseBlockCount = 0;
            HasPendingCounter = false;
        }

        /// <summary>清理当前敌人的攻击动作频率记录，进入下一次战斗时重新计算补偿。</summary>
        public void ResetAttackSelectionHistory()
        {
            attackMissCounts.Clear();
            retreatWeightBonus = 0f;
        }

        /// <summary>按起始技能分组组合分支，运行时只在攻击结束事件上选择一次。</summary>
        private void BuildComboBranchMap(EnemyComboBranchConfig[] comboBranches)
        {
            if (comboBranches == null || comboBranches.Length == 0)
            {
                return;
            }

            Dictionary<int, List<EnemyComboBranchConfig>> groupedBranches =
                new Dictionary<int, List<EnemyComboBranchConfig>>();
            for (int i = 0; i < comboBranches.Length; i++)
            {
                EnemyComboBranchConfig branch = comboBranches[i];
                if (branch == null)
                {
                    throw new InvalidOperationException("组合分支不能为空");
                }

                if (!groupedBranches.TryGetValue(branch.startSkillId, out List<EnemyComboBranchConfig> branches))
                {
                    branches = new List<EnemyComboBranchConfig>();
                    groupedBranches.Add(branch.startSkillId, branches);
                }

                branches.Add(branch);
            }

            foreach (KeyValuePair<int, List<EnemyComboBranchConfig>> pair in groupedBranches)
            {
                comboBranchesByStartSkillId.Add(pair.Key, pair.Value.ToArray());
            }
        }

        /// <summary>按当前技能锁定一条组合路线，锁定失败表示没有可续段分支。</summary>
        private bool TryLockComboRoute(int startSkillId, float randomValue)
        {
            if (!comboBranchesByStartSkillId.TryGetValue(startSkillId, out EnemyComboBranchConfig[] branches))
            {
                return false;
            }

            EnemyComboBranchConfig branch = EnemyDecisionRandom.SelectComboBranch(branches, randomValue);
            if (branch == null || branch.sequenceSkillIds == null || branch.sequenceSkillIds.Length == 0)
            {
                return false;
            }

            EnemyAttackRuntimeConfig[] route = new EnemyAttackRuntimeConfig[branch.sequenceSkillIds.Length];
            for (int i = 0; i < branch.sequenceSkillIds.Length; i++)
            {
                route[i] = attackCatalog.GetRequiredComboAttack(branch.sequenceSkillIds[i]);
            }

            CurrentPlan.SetComboRoute(route);
            return true;
        }

        /// <summary>保存当前攻击动作，直到攻击流程结束或被中断。</summary>
        private void SetCurrentAttack(EnemyAttackRuntimeConfig attack)
        {
            CurrentSkillId = attack.SkillId;
            CurrentAnimationName = attack.AnimationName;
        }

        /// <summary>创建并保存当前攻击计划及首段攻击动作。</summary>
        private bool CreateAttackPlan(
            EnemyAttackPlanType type,
            EnemyAttackPreparationMode preparationMode,
            EnemyAttackRuntimeConfig attack,
            float releaseDistance)
        {
            if (attack == null)
            {
                return false;
            }

            State = EnemyCombatDecisionState.Attack;
            AttackPhase = EnemyAttackPhase.None;
            PendingReaction = EnemyCombatReaction.None;
            CurrentPlan = new EnemyAttackPlan(type, preparationMode, attack, releaseDistance);
            SetCurrentAttack(attack);
            return true;
        }

        /// <summary>缺少进身或追击技能时，选择基础攻击并要求普通接近。</summary>
        /// <summary>当前距离超出近距离池覆盖时，优先尝试创建进身攻击计划。</summary>
        private bool TryCreateApproachPlan(float distanceToTarget, float randomValue)
        {
            if (HasAttackCoveringDistance(attackCatalog.BasicAttacks, distanceToTarget))
            {
                return false;
            }

            EnemyAttackRuntimeConfig approachAttack = SelectAttackCoveringDistance(
                attackCatalog.ApproachAttacks,
                distanceToTarget,
                randomValue);
            if (approachAttack == null)
            {
                return false;
            }

            return CreateAttackPlan(
                EnemyAttackPlanType.Approach,
                EnemyAttackPreparationMode.Direct,
                approachAttack,
                approachAttack.AttackRange);
        }

        /// <summary>在近距离节奏中用独立随机值选择攻击池和池内技能。</summary>
        private bool TryCreateCloseRangeRhythmPlan(
            float distanceToTarget,
            float poolSelectionRoll,
            float attackSelectionRoll)
        {
            bool hasClosePool = HasSelectableAttack(attackCatalog.BasicAttacks);
            bool hasRetreatPool = HasAttackCoveringDistance(attackCatalog.RetreatAttacks, distanceToTarget);
            if (!hasClosePool && !hasRetreatPool)
            {
                return false;
            }

            if (ShouldSelectRetreatPool(hasClosePool, hasRetreatPool, poolSelectionRoll))
            {
                EnemyAttackRuntimeConfig retreatAttack = SelectAttackCoveringDistance(
                    attackCatalog.RetreatAttacks,
                    distanceToTarget,
                    attackSelectionRoll);
                if (retreatAttack != null)
                {
                    return CreateAttackPlan(
                        EnemyAttackPlanType.Retreat,
                        EnemyAttackPreparationMode.Direct,
                        retreatAttack,
                        retreatAttack.AttackRange);
                }
            }

            EnemyAttackRuntimeConfig basicAttack = SelectAttackCoveringDistance(
                attackCatalog.BasicAttacks,
                distanceToTarget,
                attackSelectionRoll);
            if (basicAttack != null)
            {
                return CreateAttackPlan(
                    EnemyAttackPlanType.Basic,
                    EnemyAttackPreparationMode.Direct,
                    basicAttack,
                    basicAttack.AttackRange);
            }

            return CreateFallbackBasicApproachPlan(attackSelectionRoll);
        }

        /// <summary>判断指定攻击池是否至少有一个可被权重选择的技能。</summary>
        private static bool HasSelectableAttack(IReadOnlyList<EnemyAttackRuntimeConfig> attacks)
        {
            for (int i = 0; i < attacks.Count; i++)
            {
                EnemyAttackRuntimeConfig attack = attacks[i];
                if (attack != null && attack.SkillId > 0 && attack.Weight > 0f)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>判断指定攻击池是否存在能覆盖当前距离或关闭距离检测的技能。</summary>
        private static bool HasAttackCoveringDistance(
            IReadOnlyList<EnemyAttackRuntimeConfig> attacks,
            float distanceToTarget)
        {
            for (int i = 0; i < attacks.Count; i++)
            {
                EnemyAttackRuntimeConfig attack = attacks[i];
                if (attack != null
                    && attack.SkillId > 0
                    && attack.Weight > 0f
                    && (!attack.EnableAttackDistanceCheck || attack.AttackRange >= distanceToTarget))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>根据近距离池和远离池的当前有效权重判断是否选择远离池。</summary>
        private bool ShouldSelectRetreatPool(bool hasClosePool, bool hasRetreatPool, float randomValue)
        {
            if (!hasRetreatPool)
            {
                return false;
            }

            float retreatWeight = combatConfig.retreatAttackPoolWeight + retreatWeightBonus;
            if (retreatWeight <= 0f)
            {
                return false;
            }

            if (!hasClosePool || combatConfig.closeAttackPoolWeight <= 0f)
            {
                return true;
            }

            float totalWeight = combatConfig.closeAttackPoolWeight + retreatWeight;
            return randomValue * totalWeight > combatConfig.closeAttackPoolWeight;
        }

        /// <summary>缺少进身技能时，选择基础攻击并要求普通接近。</summary>
        private bool CreateFallbackBasicApproachPlan(float randomValue)
        {
            return CreateFallbackBasicPlan(randomValue, EnemyAttackPreparationMode.Approach);
        }

        /// <summary>缺少追击技能时，选择基础攻击并要求追击接近，确保范围外播放跑步动画。</summary>
        private bool CreateFallbackBasicPursuitPlan(float randomValue)
        {
            return CreateFallbackBasicPlan(randomValue, EnemyAttackPreparationMode.Pursuit);
        }

        /// <summary>按指定准备模式创建基础攻击兜底计划。</summary>
        private bool CreateFallbackBasicPlan(float randomValue, EnemyAttackPreparationMode preparationMode)
        {
            EnemyAttackRuntimeConfig basicAttack = SelectAttackWithCompensation(
                attackCatalog.BasicAttacks,
                randomValue);
            if (basicAttack == null)
            {
                return false;
            }

            return CreateAttackPlan(
                EnemyAttackPlanType.Basic,
                preparationMode,
                basicAttack,
                basicAttack.AttackRange);
        }

        /// <summary>从攻击池中过滤能覆盖当前距离或关闭距离检测的技能并按权重选择。</summary>
        private EnemyAttackRuntimeConfig SelectAttackCoveringDistance(
            IReadOnlyList<EnemyAttackRuntimeConfig> attacks,
            float distanceToTarget,
            float randomValue)
        {
            List<EnemyAttackRuntimeConfig> candidates = new List<EnemyAttackRuntimeConfig>();
            for (int i = 0; i < attacks.Count; i++)
            {
                if (!attacks[i].EnableAttackDistanceCheck || attacks[i].AttackRange >= distanceToTarget)
                {
                    candidates.Add(attacks[i]);
                }
            }

            return SelectAttackWithCompensation(candidates, randomValue);
        }

        /// <summary>从满足条件且权重有效的攻击动作中执行补偿权重选择和次数保底。</summary>
        private EnemyAttackRuntimeConfig SelectAttackWithCompensation(
            IReadOnlyList<EnemyAttackRuntimeConfig> attacks,
            float randomValue)
        {
            List<EnemyAttackRuntimeConfig> candidates = new List<EnemyAttackRuntimeConfig>();
            for (int i = 0; i < attacks.Count; i++)
            {
                EnemyAttackRuntimeConfig attack = attacks[i];
                if (attack != null && attack.SkillId > 0 && attack.Weight > 0f)
                {
                    candidates.Add(attack);
                }
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            List<EnemyAttackRuntimeConfig> guaranteedCandidates = new List<EnemyAttackRuntimeConfig>();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (GetAttackMissCount(candidates[i]) >= profile.attackWeightGuaranteeMissCount)
                {
                    guaranteedCandidates.Add(candidates[i]);
                }
            }

            EnemyAttackRuntimeConfig selectedAttack = guaranteedCandidates.Count > 0
                ? EnemyDecisionRandom.SelectAttack(guaranteedCandidates, randomValue)
                : EnemyDecisionRandom.SelectAttack(
                    candidates,
                    randomValue,
                    GetEffectiveAttackWeight);
            UpdateAttackMissCounts(candidates, selectedAttack);
            return selectedAttack;
        }

        /// <summary>计算攻击动作当前有效权重，基础权重为零的动作不会进入该方法。</summary>
        private float GetEffectiveAttackWeight(EnemyAttackRuntimeConfig attack)
        {
            return attack.Weight
                + GetAttackMissCount(attack) * profile.attackWeightCompensationPerMiss;
        }

        /// <summary>读取攻击动作连续满足条件但未被选中的次数。</summary>
        private int GetAttackMissCount(EnemyAttackRuntimeConfig attack)
        {
            return attackMissCounts.TryGetValue(attack, out int missCount)
                ? missCount
                : 0;
        }

        /// <summary>更新本次候选池内所有动作的未选次数，选中动作清零，其余动作累计。</summary>
        private void UpdateAttackMissCounts(
            IReadOnlyList<EnemyAttackRuntimeConfig> candidates,
            EnemyAttackRuntimeConfig selectedAttack)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                EnemyAttackRuntimeConfig candidate = candidates[i];
                if (ReferenceEquals(candidate, selectedAttack))
                {
                    attackMissCounts[candidate] = 0;
                    continue;
                }

                int nextMissCount = Math.Min(
                    GetAttackMissCount(candidate) + 1,
                    profile.attackWeightGuaranteeMissCount);
                attackMissCounts[candidate] = nextMissCount;
            }
        }

        /// <summary>为旧构造入口创建固定范围攻击目录，待 AI 启动接入真实技能表后移除。</summary>
        private static EnemyAttackCatalog CreateCompatibilityCatalog(EnemyCombatConfig combatConfig)
        {
            return EnemyAttackCatalog.Create(
                combatConfig,
                skillId => new SkillConfig { skillId = skillId, attackRange = CompatibilityAttackRange });
        }

        /// <summary>判断当前阶段是否允许把玩家攻击输入转为即时反应请求。</summary>
        private bool CanReceivePlayerAttackInput()
        {
            return State == EnemyCombatDecisionState.Confrontation
                || (State == EnemyCombatDecisionState.Attack && AttackPhase == EnemyAttackPhase.End);
        }

        /// <summary>判断闪避冷却是否结束，防止低稳定值输入反应连续闪避。</summary>
        private bool IsDodgeCooldownReady(float time)
        {
            return time - lastDodgeTime >= profile.dodgeCooldown;
        }

        /// <summary>保存待执行反应，并同步当前战斗决策状态。</summary>
        private EnemyCombatReaction SetReaction(EnemyCombatReaction reaction, float time)
        {
            PendingReaction = reaction;
            if (reaction == EnemyCombatReaction.Dodge)
            {
                State = EnemyCombatDecisionState.Dodge;
                lastDodgeTime = time;
                return reaction;
            }

            State = EnemyCombatDecisionState.Defense;
            return reaction;
        }
    }
}
