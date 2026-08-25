using Game.Battle.Combat.Config;
using Game.Character.Enemy.Components;
using Game.Character.Enemy.Config;
using Game.Character.Enemy.Core;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    public enum EnemyBehaviorActionType
    {
        Idle = 1,
        Patrol = 2,
        AlertChase = 3,
        Search = 4,
        CombatIdle = 9,
        HoldTargetMemory = 10,
        GetHit = 50,
        Unbalance = 60,
        Dead = 100
    }

    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Action")]
    public sealed class EnemySetIntentNodeAsset : ActionNodeAsset
    {
        [SerializeField] private EnemyBehaviorActionType intentType;

        // 创建持有巡逻、搜索与死亡进度的敌人动作运行时节点。
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new EnemyActionNode(this);
        }

        // 默认动作节点不会被本资产创建，保留实现以满足 ActionNodeAsset 的抽象契约。
        protected override BehaviorTreeStatus Execute(BehaviorTreeContext context)
        {
            return BehaviorTreeStatus.Failure;
        }

        private sealed class EnemyActionNode : BehaviorTreeNode
        {
            private const float DestinationRefreshSqrThreshold = 0.0001f;

            private readonly EnemySetIntentNodeAsset asset;
            private int patrolIndex;
            private Vector3[] searchPoints = new Vector3[0];
            private Vector3 activeSearchOrigin;
            private int searchIndex;
            private bool hasStartedSearch;
            private bool hasReachedLastKnownPosition;
            private bool hasStartedEnterCombat;
            private bool hasStartedExitCombat;
            private bool hasHandledDeath;
            private bool hasEnteredDeathAnimation;
            private bool hasCompletedDeathAnimation;
            private string activeEnterCombatAnimation;
            private string activeExitCombatAnimation;
            private string activeHitReactionAnimation;
            private string combatIdleMoveAnimation;
            private bool hasStartedHitReaction;
            private bool hasStartedUnbalance;
            private bool hasEnteredUnbalanceLoop;
            private bool hasTriggeredUnbalanceEnd;
            private bool hasEnteredUnbalanceEnd;
            private bool wasExecutionLockedDuringUnbalance;
            private float unbalanceLoopStartTime;
            private bool hasActiveMoveDestination;
            private Vector3 activeMoveDestination;

            // 绑定动作资产，供运行时读取序列化行为类型和技能编号。
            public EnemyActionNode(EnemySetIntentNodeAsset asset)
                : base(asset)
            {
                this.asset = asset;
            }

            // 根据资产配置直接执行敌人行为，不再写入意图或请求 FSM 状态。
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (!EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller))
                {
                    return BehaviorTreeStatus.Failure;
                }

                switch (asset.intentType)
                {
                    case EnemyBehaviorActionType.Idle:
                        return TickIdle(controller);
                    case EnemyBehaviorActionType.Patrol:
                        return TickPatrol(controller);
                    case EnemyBehaviorActionType.AlertChase:
                        return TickAlertChase(controller);
                    case EnemyBehaviorActionType.Search:
                        return TickSearch(controller);
                    case EnemyBehaviorActionType.CombatIdle:
                        return TickCombatIdle(controller);
                    case EnemyBehaviorActionType.HoldTargetMemory:
                        return TickHoldTargetMemory(controller);
                    case EnemyBehaviorActionType.GetHit:
                        return TickGetHit(controller);                              
                    case EnemyBehaviorActionType.Unbalance:
                        return TickUnbalance(controller);
                    case EnemyBehaviorActionType.Dead:
                        return TickDead(controller);
                    default:
                        return BehaviorTreeStatus.Failure;
                }
            }

            // 重置只清理中断动作局部状态，不破坏巡逻和搜索运行进度。
            public override void Reset()
            {
                ResetInterruptProgress();
                ResetMoveDestinationCache();
            }

            // 清理受击、失衡和死亡动作的运行时状态，避免中断层再次进入时沿用旧动画进度。
            private void ResetInterruptProgress()
            {
                activeHitReactionAnimation = null;
                hasStartedHitReaction = false;
                hasStartedUnbalance = false;
                hasEnteredUnbalanceLoop = false;
                hasTriggeredUnbalanceEnd = false;
                hasEnteredUnbalanceEnd = false;
                wasExecutionLockedDuringUnbalance = false;
                unbalanceLoopStartTime = 0f;
                hasHandledDeath = false;
                hasEnteredDeathAnimation = false;
                hasCompletedDeathAnimation = false;
            }

            // 停止移动并播放待机动画。
            private BehaviorTreeStatus TickIdle(AIController controller)
            {
                if (controller.Context != null && controller.Context.Movement != null)
                {
                    controller.Context.Movement.Stop();
                    ResetMoveDestinationCache();
                }

                if (controller.Context != null && controller.Context.Animation != null)
                {
                    controller.Context.Animation.TryPlay(controller.Definition != null ? controller.Definition.AnimationConfig.idleAnimation : null);
                }

                return BehaviorTreeStatus.Success;
            }

            /// <summary>停止寻路、持续朝向战斗目标，并播放当前选中的战斗待机侧移动画。</summary>
            private BehaviorTreeStatus TickCombatIdle(AIController controller)
            {
                if (controller.Context == null
                    || controller.Context.Movement == null
                    || controller.Blackboard.Target == null
                    || !controller.Blackboard.IsTargetVisible)
                {
                    return BehaviorTreeStatus.Failure;
                }

                EnsureCombatIdleMoveAnimationSelected(controller);
                controller.Context.Movement.Stop();
                ResetMoveDestinationCache();
                controller.Context.Movement.LookAt(controller.Blackboard.Target.position);
                if (controller.Context.Animation != null)
                {
                    controller.Context.Animation.TryPlay(combatIdleMoveAnimation);
                }

                controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Idle);
                return BehaviorTreeStatus.Success;
            }

            // 目标短暂丢失时只追到最后已知位置，避免用不可见目标的实时坐标进行超视距追踪。
            private BehaviorTreeStatus TickHoldTargetMemory(AIController controller)
            {
                if (!controller.Blackboard.HasTargetMemory || !controller.Blackboard.HasLastKnownPosition)
                {
                    return BehaviorTreeStatus.Failure;
                }

                if (controller.Context == null || controller.Context.Movement == null)
                {
                    return BehaviorTreeStatus.Success;
                }

                controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Alert);
                Vector3 destination = controller.Blackboard.LastKnownPosition;
                EnemyMovementComponent movement = controller.Context.Movement;
                if (!movement.HasReached(destination, 1.1f))
                {
                    if (controller.Context.Animation != null)
                    {
                        controller.Context.Animation.TryPlay(
                            controller.Definition != null ? controller.Definition.AnimationConfig.runAnimation : null);
                    }

                    MoveByNavMesh(controller, movement, destination);
                    return BehaviorTreeStatus.Success;
                }

                movement.Stop();
                ResetMoveDestinationCache();
                return BehaviorTreeStatus.Success;
            }

            // 看到目标后先拔出武器进入警戒；正式进入战斗前持续用 Move 动画追向目标。
            private BehaviorTreeStatus TickAlertChase(AIController controller)
            {
                // if (controller.Context == null
                //     || controller.Context.Movement == null
                //     || controller.Blackboard.Target == null
                //     || !controller.Blackboard.IsTargetVisible)
                // {
                //     Debug.Log("失败");
                //     ResetEnterCombat();
                //     return BehaviorTreeStatus.Failure;
                // }
                if (!controller.Blackboard.HasCombatStance)
                {
                    BehaviorTreeStatus res = TickEnterCombat(controller);
                    if (res == BehaviorTreeStatus.Running)
                    {
                        return res;
                    }
                }

                ResetEnterCombat();
                if (controller.Blackboard.IsInCombatState)
                {
                    return BehaviorTreeStatus.Failure;
                }

                if (controller.Blackboard.IsInCombatRange)
                {
                    SetCombatState(controller, true);
                    return BehaviorTreeStatus.Failure;
                }

                if (controller.Context.Animation != null)
                {
                    controller.Context.Animation.TryPlay(
                        controller.Definition != null ? controller.Definition.AnimationConfig.moveAnimation : null);
                }

                controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Alert);
                MoveByNavMesh(controller, controller.Context.Movement, controller.Blackboard.Target.position);
                return BehaviorTreeStatus.Success;
            }

            // 播放拔出武器动画，武器显隐由动画事件控制，动画结束后才允许追击或进入战斗分支。
            private BehaviorTreeStatus TickEnterCombat(AIController controller)
            {
                if (!hasStartedEnterCombat)
                {
                    hasStartedEnterCombat = true;
                    activeEnterCombatAnimation = controller.Definition != null
                        ? controller.Definition.AnimationConfig.enterCombatAnimation
                        : "EnterCombat";
                    if (controller.Context.Movement != null)
                    {
                        controller.Context.Movement.Stop();
                        ResetMoveDestinationCache();
                    }

                    controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Alert);
                    if (controller.Context.Animation == null
                        || !controller.Context.Animation.TryPlay(activeEnterCombatAnimation))
                    {
                        ShowEnemyWeapons(controller);
                        controller.Blackboard.SetCombatStance(true);
                        ResetEnterCombat();
                        return BehaviorTreeStatus.Success;
                    }

                    return BehaviorTreeStatus.Running;
                }

                if (controller.Context.Animation != null
                    && controller.Context.Animation.IsPlaying(activeEnterCombatAnimation, out float normalizedTime)
                    && normalizedTime < 1f)
                {
                    controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Alert);
                    return BehaviorTreeStatus.Running;
                }

                controller.Blackboard.SetCombatStance(true);
                ResetEnterCombat();
                return BehaviorTreeStatus.Success;
            }

            // 首次进入战斗待机时补选一个侧移方向，后续只在攻击结束后重新选择。
            private void EnsureCombatIdleMoveAnimationSelected(AIController controller)
            {
                if (string.IsNullOrEmpty(combatIdleMoveAnimation))
                {
                    SelectCombatIdleMoveAnimation(controller);
                }
            }

            // 随机选择左移或右移动画，作为下一段战斗待机的表现方向。
            private void SelectCombatIdleMoveAnimation(AIController controller)
            {
                EnemyAnimationConfig animationConfig = controller.Definition != null
                    ? controller.Definition.AnimationConfig
                    : null;
                bool moveLeft = Random.value < 0.5f;
                if (moveLeft)
                {
                    combatIdleMoveAnimation = animationConfig != null
                        ? animationConfig.combatIdleMoveLeftAnimation
                        : "MoveLeft";
                    return;
                }

                combatIdleMoveAnimation = animationConfig != null
                    ? animationConfig.combatIdleMoveRightAnimation
                    : "MoveRight";
            }

            // 按场景巡逻路线持续推进当前路点，并在抵达后循环到下一个路点。
            private BehaviorTreeStatus TickPatrol(AIController controller)
            {
                Transform[] route = controller.PatrolRoute;
                if (route == null || route.Length == 0 || controller.Context == null || controller.Context.Movement == null)
                {
                    return TickIdle(controller);
                }

                patrolIndex %= route.Length;
                Transform currentPoint = route[patrolIndex];
                EnemyMovementComponent movement = controller.Context.Movement;
                if (currentPoint != null && movement.HasReached(currentPoint.position, 1.1f))
                {
                    patrolIndex = (patrolIndex + 1) % route.Length;
                }

                Transform destination = route[patrolIndex];
                if (destination != null)
                {
                    if (controller.Context.Animation != null)
                    {
                        controller.Context.Animation.TryPlay(controller.Definition != null ? controller.Definition.AnimationConfig.moveAnimation : null);
                    }

                    MoveByNavMesh(controller, movement, destination.position);
                }

                return BehaviorTreeStatus.Success;
            }

            // 先抵达最后已知位置，再依次巡查感知组件生成的搜索点。
            private BehaviorTreeStatus TickSearch(AIController controller)
            {
                if (controller.Blackboard.IsTargetVisible
                    || !controller.Blackboard.HasLastKnownPosition)
                {
                    ResetSearch();
                    return BehaviorTreeStatus.Success;
                }

                if (controller.Context == null || controller.Context.Movement == null)
                {
                    return BehaviorTreeStatus.Failure;
                }

                // 收武器动画一旦开始，后续帧必须持续回到退出流程，否则会掉到 Success 导致清理永远不执行。
                if (hasStartedExitCombat)
                {
                    return TickExitCombatAfterSearch(controller);
                }

                // 目标重新暴露后可能刷新最后已知位置，重新丢失时需要基于新位置重建搜索路径。
                if (hasStartedSearch
                    && (activeSearchOrigin - controller.Blackboard.LastKnownPosition).sqrMagnitude > 0.01f)
                {
                    ResetSearch();
                }

                if (!hasStartedSearch)
                {
                    hasStartedSearch = true;
                    activeSearchOrigin = controller.Blackboard.LastKnownPosition;
                    searchIndex = 0;
                    hasReachedLastKnownPosition = false;
                    searchPoints = controller.Context.Perception != null
                        ? controller.Context.Perception.GenerateSearchPoints(controller.Blackboard.LastKnownPosition)
                        : new Vector3[0];
                    if (controller.Context.Animation != null)
                    {
                        controller.Context.Animation.TryPlay(controller.Definition != null ? controller.Definition.AnimationConfig.moveAnimation : null);
                    }

                    MoveByNavMesh(controller, controller.Context.Movement, controller.Blackboard.LastKnownPosition);
                    return BehaviorTreeStatus.Success;
                }

                if (!hasReachedLastKnownPosition)
                {
                    if (!controller.Context.Movement.HasReached(controller.Blackboard.LastKnownPosition, 1.1f))
                    {
                        MoveByNavMesh(controller, controller.Context.Movement, controller.Blackboard.LastKnownPosition);
                        return BehaviorTreeStatus.Success;
                    }

                    hasReachedLastKnownPosition = true;
                    if (!MoveToCurrentSearchPoint(controller))
                    {
                        return TickExitCombatAfterSearch(controller);
                    }

                    return BehaviorTreeStatus.Success;
                }

                if (searchIndex < searchPoints.Length
                    && controller.Context.Movement.HasReached(searchPoints[searchIndex], 1.1f))
                {
                    searchIndex++;
                    if (!MoveToCurrentSearchPoint(controller))
                    {
                        return TickExitCombatAfterSearch(controller);
                    }
                }

                return BehaviorTreeStatus.Success;
            }

            // 搜索失败后播放收起武器动画，并在完成时清理目标回到待机。
            private BehaviorTreeStatus TickExitCombatAfterSearch(AIController controller) 
            {
                if (!controller.Blackboard.HasCombatStance)
                {
                    FinishSearchFailure(controller);
                    return BehaviorTreeStatus.Success;
                }

                if (!hasStartedExitCombat)
                {
                    hasStartedExitCombat = true;
                    activeExitCombatAnimation = controller.Definition != null
                        ? controller.Definition.AnimationConfig.exitCombatAnimation
                        : "ExitCombat";
                    if (controller.Context.Movement != null)
                    {
                        controller.Context.Movement.Stop();
                        ResetMoveDestinationCache();
                    }

                    if (controller.Context.Animation == null
                        || !controller.Context.Animation.TryPlay(activeExitCombatAnimation))
                    {
                        FinishSearchFailure(controller);
                        return BehaviorTreeStatus.Success;
                    }

                    return BehaviorTreeStatus.Running;
                }

                if (controller.Context.Animation != null
                    && controller.Context.Animation.IsPlaying(activeExitCombatAnimation, out float normalizedTime)
                    && normalizedTime < 1f)
                {
                    return BehaviorTreeStatus.Running;
                }

                FinishSearchFailure(controller);
                return BehaviorTreeStatus.Success;
            }

            // 搜索结束时统一清理目标、姿态和搜索进度。
            private void FinishSearchFailure(AIController controller)
            {
                HideEnemyWeapons(controller);
                if (controller.Context != null && controller.Context.Movement != null)
                {
                    controller.Context.Movement.Stop();
                    ResetMoveDestinationCache();
                }

                controller.Blackboard.SetCombatStance(false);
                SetCombatState(controller, false);
                controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Idle);
                PlayIdleAnimation(controller);
                if (controller.Context.Perception != null)
                {
                    controller.Context.Perception.ForgetTarget();
                }

                ResetExitCombat();
                ResetSearch();
            }

            // 显式播放普通待机，避免无自动过渡的 Animator 停留在一次性动作末帧。
            private static void PlayIdleAnimation(AIController controller)
            {
                if (controller.Context == null || controller.Context.Animation == null)
                {
                    return;
                }

                controller.Context.Animation.TryPlay(
                    controller.Definition != null ? controller.Definition.AnimationConfig.idleAnimation : null);
            }

            // 动画无法播放时兜底切到手持武器。
            private static void ShowEnemyWeapons(AIController controller)
            {
                EnemyAgent agent = controller.Context != null
                    ? controller.Context.Agent as EnemyAgent
                    : null;
                if (agent != null)
                {
                    agent.ShowWeapons();
                }
            }

            // 从敌人上下文读取 EnemyAgent，并隐藏其配置的全部武器外观。
            private static void HideEnemyWeapons(AIController controller)
            {
                EnemyAgent agent = controller.Context != null
                    ? controller.Context.Agent as EnemyAgent
                    : null;
                if (agent != null)
                {
                    agent.HideWeapons();
                }
            }

            // 在受击动画结束前保持运行，避免待机或移动行为覆盖当前动画。
            private BehaviorTreeStatus TickGetHit(AIController controller)
            {
                if (!hasStartedHitReaction)
                {
                    return StartHitReaction(controller);
                }

                if (controller.Context != null
                    && controller.Context.Animation != null
                    && controller.Context.Animation.IsPlaying(activeHitReactionAnimation, out float normalizedTime)
                    && normalizedTime < 1f)
                {
                    return BehaviorTreeStatus.Running;
                }

                FinishHitReaction(controller);
                return BehaviorTreeStatus.Success;
            }

            // 消费最新受击请求并开始播放；连续受击时会从最新受击动画重新开始。
            private BehaviorTreeStatus StartHitReaction(AIController controller)
            {
                if (controller.Context != null && controller.Context.Movement != null)
                {
                    controller.Context.Movement.Stop();
                    ResetMoveDestinationCache();
                }

                SkillHitWeight hitWeight = controller.Blackboard.PendingHitReactionHitWeight;
                EnemyHitDirection hitDirection = controller.Blackboard.PendingHitReactionDirection;
                string animationName = controller.Blackboard.ConsumeHitReaction();
                if (string.IsNullOrEmpty(animationName) && controller.Definition != null)
                {
                    animationName = controller.Definition.AnimationConfig.getHitAnimation;
                }

                if (controller.Context == null || controller.Context.Animation == null)
                {
                    FinishHitReaction(controller);
                    return BehaviorTreeStatus.Success;
                }

                controller.Context.Animation.SetHitReactionParameters(
                    controller.Blackboard.IsInCombatState,
                    hitWeight,
                    hitDirection);
                if (!controller.Context.Animation.TryPlay(animationName, interruptCurrentAction: true, forceRestart: true))
                {
                    FinishHitReaction(controller);
                    return BehaviorTreeStatus.Failure;
                }

                activeHitReactionAnimation = animationName;
                hasStartedHitReaction = true;
                controller.Blackboard.SetHitReactionInProgress(true);
                return BehaviorTreeStatus.Running;
            }

            // 清理受击运行时状态，使行为树可以回到普通决策分支。
            private void FinishHitReaction(AIController controller)
            {
                activeHitReactionAnimation = null;
                hasStartedHitReaction = false;
                controller.Blackboard.SetHitReactionInProgress(false);
            }

            /// <summary>失衡动画播放结束后恢复稳定值；处决锁定期间冻结 Loop 剩余时间。</summary>
            private BehaviorTreeStatus TickUnbalance(AIController controller)
            {
                if (!hasStartedUnbalance)
                {
                    return StartUnbalance(controller);
                }

                if (controller.Context == null || controller.Context.Attribute == null)
                {
                    FinishUnbalance(controller);
                    return BehaviorTreeStatus.Failure;
                }

                if (controller.IsExecutionLocked)
                {
                    wasExecutionLockedDuringUnbalance = true;
                    PauseUnbalanceLoopTimer();
                    return BehaviorTreeStatus.Running;
                }

                if (wasExecutionLockedDuringUnbalance)
                {
                    ResumeUnbalanceAfterExecution(controller);
                }

                if (!hasTriggeredUnbalanceEnd)
                {
                    TickUnbalanceLoop(controller);
                    return BehaviorTreeStatus.Running;
                }

                if (!HasUnbalanceEndFinished(controller))
                {
                    return BehaviorTreeStatus.Running;
                }

                FinishUnbalance(controller);
                return BehaviorTreeStatus.Success;
            }

            // 首次进入失衡时停止移动并启动失衡动画，后续帧只等待动画自然结束。
            private BehaviorTreeStatus StartUnbalance(AIController controller)
            {
                if (controller.Context == null || controller.Context.Attribute == null)
                {
                    FinishUnbalance(controller);
                    return BehaviorTreeStatus.Failure;
                }

                if (controller.Context.Movement != null)
                {
                    controller.Context.Movement.Stop();
                    ResetMoveDestinationCache();
                }

                string animationName = GetDefenseBreakAnimation(controller);
                if (controller.Context.Animation == null || !controller.Context.Animation.TryPlay(animationName))
                {
                    FinishUnbalance(controller);
                    return BehaviorTreeStatus.Success;
                }

                controller.Context.Animation.SetTrigger(GetUnbalanceStartTrigger(controller));

                hasStartedUnbalance = true;
                controller.Blackboard.SetUnbalanced(true);
                return BehaviorTreeStatus.Running;
            }

            /// <summary>失衡进入 Loop 状态后计时，达到配置时长后触发结束过渡。</summary>
            private void TickUnbalanceLoop(AIController controller)
            {
                if (controller.Context.Animation == null
                    || !controller.Context.Animation.IsPlaying(GetUnbalanceLoopAnimation(controller), out _))
                {
                    controller.Blackboard.SetUnbalanceLoop(false);
                    return;
                }

                if (!hasEnteredUnbalanceLoop)
                {
                    hasEnteredUnbalanceLoop = true;
                    unbalanceLoopStartTime = Time.time;
                }

                controller.Blackboard.SetUnbalanceLoop(true);

                if (Time.time - unbalanceLoopStartTime < GetUnbalanceLoopDuration(controller))
                {
                    return;
                }

                controller.Context.Animation.SetTrigger(GetUnbalanceEndTrigger(controller));
                controller.Blackboard.SetUnbalanceLoop(false);
                hasTriggeredUnbalanceEnd = true;
            }

            /// <summary>处决期间把失衡 Loop 起始时间向后平移，使剩余失衡时间保持不变。</summary>
            private void PauseUnbalanceLoopTimer()
            {
                if (hasEnteredUnbalanceLoop)
                {
                    unbalanceLoopStartTime += Time.deltaTime;
                }
            }

            /// <summary>处决结束后恢复敌人失衡动画通道，避免 Timeline 覆盖后行为树卡在等待状态。</summary>
            private void ResumeUnbalanceAfterExecution(AIController controller)
            {
                wasExecutionLockedDuringUnbalance = false;
                if (controller.Context.Animation == null || controller.Blackboard.IsDead)
                {
                    return;
                }

                if (hasTriggeredUnbalanceEnd)
                {
                    hasEnteredUnbalanceEnd = false;
                    controller.Blackboard.SetUnbalanceLoop(false);
                    controller.Context.Animation.SetTrigger(GetUnbalanceEndTrigger(controller));
                    return;
                }

                controller.Context.Animation.TryPlay(
                    GetUnbalanceLoopAnimation(controller),
                    interruptCurrentAction: false,
                    forceRestart: false);
                if (!hasEnteredUnbalanceLoop)
                {
                    hasEnteredUnbalanceLoop = true;
                    unbalanceLoopStartTime = Time.time;
                }

                controller.Blackboard.SetUnbalanceLoop(true);
            }

            /// <summary>等待 UnbalanceEnd 动画真正进入并播放完成，结束后允许退出失衡状态。</summary>
            private bool HasUnbalanceEndFinished(AIController controller)
            {
                if (controller.Context.Animation == null)
                {
                    return true;
                }

                if (!controller.Context.Animation.IsPlaying(GetUnbalanceEndAnimation(controller), out float normalizedTime))
                {
                    return hasEnteredUnbalanceEnd;
                }

                controller.Blackboard.SetUnbalanceLoop(false);
                hasEnteredUnbalanceEnd = true;
                return normalizedTime >= 1f;
            }

            /// <summary>读取失衡起始动画名，缺少配置时使用约定状态名。</summary>
            private static string GetDefenseBreakAnimation(AIController controller)
            {
                return controller.Definition != null && controller.Definition.AnimationConfig != null
                    ? controller.Definition.AnimationConfig.defenseBreakAnimation
                    : "DefenseBreak";
            }

            /// <summary>读取失衡 Loop 动画名，缺少配置时使用约定状态名。</summary>
            private static string GetUnbalanceLoopAnimation(AIController controller)
            {
                return controller.Definition != null && controller.Definition.AnimationConfig != null
                    ? controller.Definition.AnimationConfig.unbalanceLoopAnimation
                    : "UnbalanceLoop";
            }

            /// <summary>读取失衡结束动画名，仅用于检测播放完成，不直接播放。</summary>
            private static string GetUnbalanceEndAnimation(AIController controller)
            {
                return controller.Definition != null && controller.Definition.AnimationConfig != null
                    ? controller.Definition.AnimationConfig.unbalanceEndAnimation
                    : "UnbalanceEnd";
            }

            /// <summary>读取失衡结束 Trigger 参数名，缺少配置时使用约定参数名。</summary>
            private static string GetUnbalanceEndTrigger(AIController controller)
            {
                return controller.Definition != null && controller.Definition.AnimationConfig != null
                    ? controller.Definition.AnimationConfig.unbalanceEndTrigger
                    : "UnbalanceEnd";
            }

            /// <summary>读取失衡开始 Trigger 参数名，缺少配置时使用约定参数名。</summary>
            private static string GetUnbalanceStartTrigger(AIController controller)
            {
                return controller.Definition != null && controller.Definition.AnimationConfig != null
                    ? controller.Definition.AnimationConfig.unbalanceStartTrigger
                    : "UnbalanceStart";
            }

            /// <summary>同步行为树战斗状态到黑板和 Animator，避免 IsCombat 参数只在受击瞬间才变化。</summary>
            private static void SetCombatState(AIController controller, bool isInCombat)
            {
                controller.Blackboard.SetCombatState(isInCombat);
                if (controller.Context != null && controller.Context.Animation != null)
                {
                    controller.Context.Animation.SetCombatStateParameter(isInCombat);
                }
            }

            /// <summary>读取失衡 Loop 保持时长，缺少配置时默认保持三秒。</summary>
            private static float GetUnbalanceLoopDuration(AIController controller)
            {
                return controller.Definition != null && controller.Definition.AnimationConfig != null
                    ? controller.Definition.AnimationConfig.unbalanceLoopDuration
                    : 3f;
            }

            // 清理失衡运行时状态，并恢复稳定值避免行为树继续卡在失衡条件。
            private void FinishUnbalance(AIController controller)
            {
                hasStartedUnbalance = false;
                hasEnteredUnbalanceLoop = false;
                hasTriggeredUnbalanceEnd = false;
                hasEnteredUnbalanceEnd = false;
                unbalanceLoopStartTime = 0f;
                if (controller.Context != null
                    && controller.Context.Attribute != null
                    && controller.Context.Attribute.IsUnbalanced)
                {
                    controller.Context.Attribute.RestoreStability(controller.Context.Attribute.MaxStability);
                }

                controller.Blackboard.SetUnbalanced(false);
            }

            // 首次死亡时停止全部活动，后续保持运行以阻断选择器的低优先级行为。
            private BehaviorTreeStatus TickDead(AIController controller)
            {
                if (!hasHandledDeath)
                {
                    controller.Blackboard.SetDead(true);
                    if (controller.Context != null && controller.Context.Movement != null)
                    {
                        controller.Context.Movement.Stop();
                        ResetMoveDestinationCache();
                    }

                    if (controller.Context != null && controller.Context.Combat != null)
                    {
                        controller.Context.Combat.InterruptAction();
                    }

                    if (controller.Context != null && controller.Context.Animation != null)
                    {
                        controller.Context.Animation.SetDeathParameters(
                            controller.Blackboard.DeathReactionIsCombat,
                            controller.Blackboard.DeathReactionHitWeight);
                        controller.Context.Animation.TryPlay(controller.Definition != null ? controller.Definition.AnimationConfig.deadAnimation : null);
                    }

                    hasHandledDeath = true;
                }

                TryCompleteDeathAnimation(controller);
                return BehaviorTreeStatus.Running;
            }

            // 死亡动画完整播放后通知生命组件处理胜利界面等死亡收尾逻辑。
            private void TryCompleteDeathAnimation(AIController controller)
            {
                if (hasCompletedDeathAnimation)
                {
                    return;
                }

                if (controller.Context == null || controller.Context.Life == null)
                {
                    return;
                }

                string deathAnimation = GetDeathAnimation(controller);
                if (controller.Context.Animation == null || string.IsNullOrEmpty(deathAnimation))
                {
                    hasCompletedDeathAnimation = true;
                    controller.Context.Life.CompleteDeathAnimation();
                    return;
                }

                if (!controller.Context.Animation.IsPlaying(deathAnimation, out float normalizedTime))
                {
                    if (hasEnteredDeathAnimation)
                    {
                        hasCompletedDeathAnimation = true;
                        controller.Context.Life.CompleteDeathAnimation();
                    }

                    return;
                }

                hasEnteredDeathAnimation = true;
                if (normalizedTime < 1f)
                {
                    return;
                }

                hasCompletedDeathAnimation = true;
                controller.Context.Life.CompleteDeathAnimation();
            }

            // 读取死亡动画名，缺少配置时交给生命组件立即完成死亡收尾。
            private static string GetDeathAnimation(AIController controller)
            {
                return controller.Definition != null && controller.Definition.AnimationConfig != null
                    ? controller.Definition.AnimationConfig.deadAnimation
                    : null;
            }

            // 将搜索点推进到下一个有效位置，全部完成时停止移动并清理搜索事实。
            private bool MoveToCurrentSearchPoint(AIController controller)
            {
                if (searchIndex >= searchPoints.Length)
                {
                    return false;
                }

                MoveByNavMesh(controller, controller.Context.Movement, searchPoints[searchIndex]);
                return true;
            }

            /// <summary>下发旧意图动作的寻路目标，并让移动动画 RootMotion 步幅沿 NavMesh 路径消耗。</summary>
            private void MoveByNavMesh(
                AIController controller,
                EnemyMovementComponent movement,
                Vector3 destination)
            {
                if (controller.Context != null && controller.Context.Animation != null)
                {
                    controller.Context.Animation.SetRootMotionSuppressed(true);
                }

                if (hasActiveMoveDestination
                    && (activeMoveDestination - destination).sqrMagnitude <= DestinationRefreshSqrThreshold)
                {
                    return;
                }

                hasActiveMoveDestination = true;
                activeMoveDestination = destination;
                movement.MoveTo(destination);
            }

            /// <summary>清理移动目的地缓存，使下一次移动行为重新下发 NavMesh 路径。</summary>
            private void ResetMoveDestinationCache()
            {
                hasActiveMoveDestination = false;
                activeMoveDestination = default;
            }

            // 清理拔出武器动画的运行时进度。
            private void ResetEnterCombat()
            {
                hasStartedEnterCombat = false;
                activeEnterCombatAnimation = null;
            }

            // 清理收起武器动画的运行时进度。
            private void ResetExitCombat()
            {
                hasStartedExitCombat = false;
                activeExitCombatAnimation = null;
            }

            // 清理本次搜索的运行时进度，等待下次搜索分支重新初始化。
            private void ResetSearch()
            {
                searchPoints = new Vector3[0];
                activeSearchOrigin = default;
                searchIndex = 0;
                hasStartedSearch = false;
                hasReachedLastKnownPosition = false;
                ResetMoveDestinationCache();
                ResetExitCombat();
            }
        }
    }
}
