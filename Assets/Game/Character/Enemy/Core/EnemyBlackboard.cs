using System;
using Game.Battle.Combat.Config;
using Game.Character.Enemy.AI;
using Game.Character.Enemy.AI.Combat;
using Game.Character.Enemy.Events;
using UnityEngine;

namespace Game.Character.Enemy.Core
{
    public enum EnemyHitDirection
    {
        Front = 0,
        Right = 1,
        Left = 2,
        Back = 3
    }

    public sealed class EnemyBlackboard
    {
        private const float DefaultCompatibilityMemoryDuration = 4f;
        private float cachedAlertMemoryDuration = DefaultCompatibilityMemoryDuration;

        public Transform CombatTarget { get; private set; }
        public bool HasCombatTarget => CombatTarget != null;
        public Transform Target => CombatTarget;
        public Vector3 AlertLastKnownPosition { get; private set; }
        public bool HasAlertMemory { get; private set; }
        public Vector3 LastKnownPosition => HasAlertMemory
            ? AlertLastKnownPosition
            : CombatTarget != null
                ? CombatTarget.position
                : default;
        public bool HasLastKnownPosition => HasAlertMemory || CombatTarget != null;
        public float CombatMemoryRemaining { get; private set; }
        public float AlertMemoryRemaining { get; private set; }
        public bool IsAlertExitPending { get; private set; }
        public bool NeedsReturnHome { get; private set; }
        public string PendingHitReactionAnimation { get; private set; }
        public SkillHitWeight PendingHitReactionHitWeight { get; private set; }
        public EnemyHitDirection PendingHitReactionDirection { get; private set; }
        public string PendingDefenseBreakAnimation { get; private set; }
        public string PendingGetUpAnimation { get; private set; }
        public bool DeathReactionIsCombat { get; private set; }
        public SkillHitWeight DeathReactionHitWeight { get; private set; }
        public bool IsTargetVisible { get; private set; }
        public bool IsSearching { get; private set; }
        public bool HasTargetMemory => HasAlertMemory || (HasCombatTarget && !IsTargetVisible && !IsSearching);
        public bool IsDead { get; private set; }
        public bool IsUnbalanced { get; private set; }
        public bool IsInUnbalanceLoop { get; private set; }
        public bool HasHitReaction { get; private set; }
        public bool IsHitReactionInProgress { get; private set; }
        public bool HasDefenseBreakReaction { get; private set; }
        public bool IsDefenseBreakReactionInProgress { get; private set; }
        public bool HasGetUpReaction { get; private set; }
        public bool IsGetUpReactionInProgress { get; private set; }
        public EnemyCombatIntent CurrentIntent { get; private set; }
        public EnemyCombatIntent AttackIntent { get; private set; }
        public bool HasAttackIntent => AttackIntent == EnemyCombatIntent.Attack;
        public EnemyCombatIntent LastIntent { get; private set; }
        public int SelectedSkillId { get; private set; }
        public float DistanceToTarget { get; private set; }
        public bool IsInCombatRange { get; private set; }
        public bool IsInChaseRange { get; private set; }
        public bool HasAttackPlan { get; private set; }
        public int AttackPlanSkillId { get; private set; }
        public string AttackPlanAnimationName { get; private set; }
        public EnemyAttackPlanType AttackPlanType { get; private set; }
        public EnemyAttackPreparationMode AttackPreparationMode { get; private set; }
        public float AttackPlanAttackRange { get; private set; }
        public float AttackPlanReleaseDistance { get; private set; }
        public EnemyCombatDecisionState CombatDecisionState { get; private set; } =
            EnemyCombatDecisionState.Confrontation;
        public EnemyAttackPhase AttackPhase { get; private set; }
        public EnemyCombatReaction PendingCombatReaction { get; private set; }
        public bool HasCombatStance { get; private set; }
        public bool IsInCombatState { get; private set; }
        public float LastAttackDecisionTime { get; private set; }
        public float LastDefenseDecisionTime { get; private set; }
        public float LastRetreatDecisionTime { get; private set; } = float.NegativeInfinity;

        public event EventHandler<EnemyCombatTargetChangedEventArgs> CombatTargetChanged;

        // 根据视野发现结果刷新目标记忆；只有战斗范围内目标会成为战斗目标。
        public void ObserveTarget(Transform target, bool isInCombatRange, float combatDuration, float alertDuration)
        {
            if (target == null)
            {
                return;
            }

            if (isInCombatRange)
            {
                SetCombatTarget(target, combatDuration, alertDuration);
                return;
            }

            SetAlertMemory(target.position, alertDuration);
        }

        // 根据玩家攻击事件刷新记忆；警戒中受击会直接进入战斗。
        public void RecordPlayerAttack(
            Transform attacker,
            bool wasAlertActive,
            bool isInCombatRange,
            float combatDuration,
            float alertDuration)
        {
            if (attacker == null)
            {
                return;
            }

            if (wasAlertActive || isInCombatRange)
            {
                SetCombatTarget(attacker, combatDuration, alertDuration);
                return;
            }

            SetAlertMemory(attacker.position, alertDuration);
        }

        // 推进战斗和警戒两段独立倒计时，战斗超时会降级为警戒记忆。
        public void TickMemories(float deltaTime)
        {
            if (HasCombatTarget)
            {
                CombatMemoryRemaining -= deltaTime;
                if (CombatMemoryRemaining <= 0f)
                {
                    ClearCombatTarget(CombatTarget.position, cachedAlertMemoryDuration);
                    return;
                }
            }

            if (!HasAlertMemory)
            {
                return;
            }

            AlertMemoryRemaining -= deltaTime;
            if (AlertMemoryRemaining <= 0f)
            {
                RequestAlertExit();
            }
        }

        /// <summary>清空战斗目标并用传入位置建立新的警戒记忆，同时发布目标释放事件。</summary>
        public void ClearCombatTarget(Vector3 lastKnownPosition, float alertDuration)
        {
            SetCombatTargetReference(null);
            CombatMemoryRemaining = 0f;
            IsTargetVisible = false;
            IsSearching = false;
            SetTargetDistanceFacts(0f, false, false);
            ClearAttackPlanFacts();
            SetAlertMemory(lastKnownPosition, alertDuration);
        }

        // 请求警戒层进入退出握手，记忆事实清空但收武器流程继续保留。
        public void RequestAlertExit()
        {
            ClearAlertMemory();
            IsAlertExitPending = true;
            IsSearching = false;
        }

        // 完成警戒退出握手，清理退出挂起标记。
        public void CompleteAlertExit()
        {
            IsAlertExitPending = false;
            ClearAlertMemory();
        }

        // 记录正常层是否需要返回原点。
        public void SetNeedsReturnHome(bool value)
        {
            NeedsReturnHome = value;
        }

        // 清理受击表现事实，供中断执行器统一收束受击状态。
        public void ClearHitReactionState()
        {
            HasHitReaction = false;
            IsHitReactionInProgress = false;
            PendingHitReactionAnimation = null;
            PendingHitReactionHitWeight = SkillHitWeight.Light;
            PendingHitReactionDirection = EnemyHitDirection.Front;
        }

        /// <summary>清理未失衡弹反破防事实，允许行为树恢复普通战斗决策。</summary>
        public void ClearDefenseBreakReactionState()
        {
            HasDefenseBreakReaction = false;
            IsDefenseBreakReactionInProgress = false;
            PendingDefenseBreakAnimation = null;
        }

        /// <summary>清理起身表现事实，允许起身动画结束后恢复普通 AI 行为。</summary>
        public void ClearGetUpReactionState()
        {
            HasGetUpReaction = false;
            IsGetUpReactionInProgress = false;
            PendingGetUpAnimation = null;
        }

        /// <summary>记录造成死亡这一击的战斗状态和轻重击类型，供 Dead BlendTree 播放前写入参数。</summary>
        public void SetDeathReactionParameters(bool isCombat, SkillHitWeight hitWeight)
        {
            DeathReactionIsCombat = isCombat;
            DeathReactionHitWeight = hitWeight;
        }

        /// <summary>记住当前目标并同步最后已知位置，只有目标变化时才发布事件。</summary>
        public void RememberTarget(Transform target)
        {
            SetCombatTargetReference(target);
            if (target != null)
            {
                AlertLastKnownPosition = target.position;
                CombatMemoryRemaining = Mathf.Max(CombatMemoryRemaining, GetCompatibilityMemoryDuration());
                ClearAlertMemory();
                IsAlertExitPending = false;
            }
        }

        /// <summary>清理目标、可见性和最后已知位置，结束本次追踪或搜索并发布目标释放事件。</summary>
        public void ForgetTarget()
        {
            SetCombatTargetReference(null);
            CombatMemoryRemaining = 0f;
            IsTargetVisible = false;
            ClearAlertMemory();
            IsAlertExitPending = false;
            ClearAttackIntent();
            SetTargetDistanceFacts(0f, false, false);
            ClearAttackPlanFacts();
        }

        // 直接设置最后已知位置，供感知和搜索逻辑写入。
        public void SetLastKnownPosition(Vector3 position)
        {
            SetAlertMemory(position, cachedAlertMemoryDuration);
        }

        // 记录当前目标是否处于可见状态。
        public void SetTargetVisible(bool isVisible)
        {
            IsTargetVisible = isVisible;
        }

        // 记录敌人是否正在执行搜索流程。
        public void SetSearching(bool isSearching)
        {
            IsSearching = isSearching;
        }

        // 写入当前战斗意图，并保留上一意图供调试状态切换。
        public void SetCombatIntent(EnemyCombatIntent intent)
        {
            if (CurrentIntent == intent)
            {
                return;
            }

            LastIntent = CurrentIntent;
            CurrentIntent = intent;
        }

        // 写入独立的攻击意图，避免闪避、防御和移动意图污染攻击状态。
        public void SetAttackIntent(EnemyCombatIntent intent)
        {
            AttackIntent = intent;
        }

        // 清理独立的攻击意图，供攻击层退出、攻击完成和闪避动作消费。
        public void ClearAttackIntent()
        {
            AttackIntent = EnemyCombatIntent.None;
            ClearAttackPlanFacts();
        }

        // 记录敌人是否已经拔出武器，避免警戒和战斗分支重复播放拔刀动画。
        public void SetCombatStance(bool hasCombatStance)
        {
            HasCombatStance = hasCombatStance;
        }

        // 记录敌人是否已经正式进入战斗，进入后保持该状态直到搜索失败或死亡。
        public void SetCombatState(bool isInCombatState)
        {
            IsInCombatState = isInCombatState;
        }

        // 记录本次决策选择的技能编号，供攻击动作节点消费。
        public void SetSelectedSkillId(int skillId)
        {
            SelectedSkillId = skillId;
        }

        /// <summary>记录目标距离、战斗范围和追击范围事实，攻击释放范围由当前攻击计划决定。</summary>
        public void SetTargetDistanceFacts(
            float distance,
            bool isInCombatRange,
            bool isInChaseRange)
        {
            DistanceToTarget = distance;
            IsInCombatRange = isInCombatRange;
            IsInChaseRange = isInChaseRange;
        }

        /// <summary>把当前攻击计划的只读快照同步到黑板，供行为树条件和调试面板读取。</summary>
        public void SetAttackPlanFacts(
            EnemyAttackPlanType planType,
            EnemyAttackPreparationMode preparationMode,
            int skillId,
            string animationName,
            float attackRange,
            float releaseDistance)
        {
            HasAttackPlan = true;
            AttackPlanType = planType;
            AttackPreparationMode = preparationMode;
            SelectedSkillId = skillId;
            AttackPlanSkillId = skillId;
            AttackPlanAnimationName = animationName;
            AttackPlanAttackRange = attackRange;
            AttackPlanReleaseDistance = releaseDistance;
        }

        /// <summary>清理攻击计划镜像，避免黑板持有已经失效的技能范围和准备模式。</summary>
        public void ClearAttackPlanFacts()
        {
            HasAttackPlan = false;
            SelectedSkillId = 0;
            AttackPlanSkillId = 0;
            AttackPlanAnimationName = null;
            AttackPlanType = EnemyAttackPlanType.Basic;
            AttackPreparationMode = EnemyAttackPreparationMode.Direct;
            AttackPlanAttackRange = 0f;
            AttackPlanReleaseDistance = 0f;
        }

        // 记录当前战斗决策状态、攻击阶段和待处理反应，供行为树条件节点读取。
        public void SetCombatDecisionFacts(
            EnemyCombatDecisionState state,
            EnemyAttackPhase attackPhase,
            EnemyCombatReaction pendingReaction)
        {
            CombatDecisionState = state;
            AttackPhase = attackPhase;
            PendingCombatReaction = pendingReaction;
        }

        // 记录攻击决策时间，供攻击冷却条件判断。
        public void MarkAttackDecision(float time)
        {
            LastAttackDecisionTime = time;
        }

        // 记录防御决策时间，供调试和后续冷却规则扩展。
        public void MarkDefenseDecision(float time)
        {
            LastDefenseDecisionTime = time;
        }

        // 记录后撤决策时间，供调试和后续冷却规则扩展。
        public void MarkRetreatDecision(float time)
        {
            LastRetreatDecisionTime = time;
        }

        // 写入待处理受击反应，后续由受击行为树动作消费。
        /// <summary>兼容旧受击入口，缺省按前方轻击写入 BlendTree 参数。</summary>
        public void SetHitReaction(string animationName)
        {
            SetHitReaction(animationName, SkillHitWeight.Light, EnemyHitDirection.Front);
        }

        /// <summary>写入待处理受击反应和 BlendTree 参数，供行为树受击节点消费。</summary>
        public void SetHitReaction(
            string animationName,
            SkillHitWeight hitWeight,
            EnemyHitDirection hitDirection)
        {
            PendingHitReactionAnimation = animationName;
            PendingHitReactionHitWeight = hitWeight;
            PendingHitReactionDirection = hitDirection;
            HasHitReaction = true;
        }

        // 消费待处理受击反应，并返回需要播放的动画名。
        public string ConsumeHitReaction()
        {
            string animationName = PendingHitReactionAnimation;
            PendingHitReactionAnimation = null;
            HasHitReaction = false;
            return animationName;
        }

        /// <summary>写入未失衡弹反破防动画请求，后续由中断执行器消费并等待播完。</summary>
        public void SetDefenseBreakReaction(string animationName)
        {
            PendingDefenseBreakAnimation = animationName;
            HasDefenseBreakReaction = true;
        }

        /// <summary>消费未失衡弹反破防动画请求，并返回需要播放的动画名。</summary>
        public string ConsumeDefenseBreakReaction()
        {
            string animationName = PendingDefenseBreakAnimation;
            PendingDefenseBreakAnimation = null;
            HasDefenseBreakReaction = false;
            return animationName;
        }

        /// <summary>请求播放起身动画，并清理会覆盖起身表现的低优先级受击事实。</summary>
        public void SetGetUpReaction(string animationName)
        {
            PendingGetUpAnimation = animationName;
            HasGetUpReaction = true;
            ClearHitReactionState();
            ClearDefenseBreakReactionState();
        }

        /// <summary>消费起身动画请求，并返回需要播放的动画名。</summary>
        public string ConsumeGetUpReaction()
        {
            string animationName = PendingGetUpAnimation;
            PendingGetUpAnimation = null;
            HasGetUpReaction = false;
            return animationName;
        }

        // 标记受击动画是否仍在播放，使行为树在动画结束前保持受击分支。
        public void SetHitReactionInProgress(bool isInProgress)
        {
            IsHitReactionInProgress = isInProgress;
        }

        /// <summary>标记未失衡弹反破防动画是否正在播放，防止普通 AI 分支提前覆盖动画。</summary>
        public void SetDefenseBreakReactionInProgress(bool isInProgress)
        {
            IsDefenseBreakReactionInProgress = isInProgress;
        }

        /// <summary>标记起身动画是否正在播放，防止受击或普通 AI 在起身完成前覆盖表现。</summary>
        public void SetGetUpReactionInProgress(bool isInProgress)
        {
            IsGetUpReactionInProgress = isInProgress;
        }

        // 记录敌人是否处于失衡状态。
        public void SetUnbalanced(bool isUnbalanced)
        {
            IsUnbalanced = isUnbalanced;
            IsInUnbalanceLoop = false;
            if (isUnbalanced)
            {
                ClearDefenseBreakReactionState();
            }
        }

        // 记录敌人是否已经进入失衡循环动画，处决只能在这个窗口内触发。
        public void SetUnbalanceLoop(bool isInUnbalanceLoop)
        {
            IsInUnbalanceLoop = isInUnbalanceLoop;
        }

        // 记录敌人是否死亡；死亡时同步清理普通交互事实。
        public void SetDead(bool isDead)
        {
            IsDead = isDead;
            if (isDead)
            {
                ForgetTarget();
                IsSearching = false;
                IsUnbalanced = false;
                IsInUnbalanceLoop = false;
                IsAlertExitPending = false;
                NeedsReturnHome = false;
                ClearHitReactionState();
                ClearDefenseBreakReactionState();
                ClearGetUpReactionState();
                HasCombatStance = false;
                IsInCombatState = false;
                ClearAttackIntent();
                SetCombatIntent(EnemyCombatIntent.Dead);
                ClearAttackPlanFacts();
                SetTargetDistanceFacts(0f, false, false);
            }
        }

        // 读取兼容入口使用的默认战斗记忆时长，避免旧入口产生零时长目标。
        private float GetCompatibilityMemoryDuration()
        {
            return cachedAlertMemoryDuration > 0f
                ? cachedAlertMemoryDuration
                : DefaultCompatibilityMemoryDuration;
        }
        /// <summary>写入战斗目标并刷新战斗记忆，进入战斗后清理警戒退出握手。</summary>
        private void SetCombatTarget(Transform target, float combatDuration, float alertDuration)
        {
            SetCombatTargetReference(target);
            CombatMemoryRemaining = Mathf.Max(0f, combatDuration);
            cachedAlertMemoryDuration = Mathf.Max(0f, alertDuration);
            ClearAlertMemory();
            IsAlertExitPending = false;
            IsSearching = false;
        }

        // 写入独立警戒记忆，并取消旧的警戒退出握手。
        private void SetAlertMemory(Vector3 position, float alertDuration)
        {
            AlertLastKnownPosition = position;
            HasAlertMemory = true;
            AlertMemoryRemaining = Mathf.Max(0f, alertDuration);
            cachedAlertMemoryDuration = Mathf.Max(0f, alertDuration);
            IsAlertExitPending = false;
        }

        // 清空警戒记忆本体，不影响警戒退出握手标记。
        private void ClearAlertMemory()
        {
            AlertLastKnownPosition = default;
            HasAlertMemory = false;
            AlertMemoryRemaining = 0f;
        }

        /// <summary>只在战斗目标引用真实变化时写入并通知监听者，避免重复目标产生重复事件。</summary>
        private void SetCombatTargetReference(Transform target)
        {
            if (CombatTarget == target)
            {
                return;
            }

            Transform previousTarget = CombatTarget;
            CombatTarget = target;
            CombatTargetChanged?.Invoke(
                this,
                new EnemyCombatTargetChangedEventArgs(null, previousTarget, target));
        }
    }
}
