using Game.Character.Enemy.Components;
using Game.Character.Enemy.Core;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Alert Routine")]
    public sealed class EnemyAlertRoutineNodeAsset : ActionNodeAsset
    {
        /// <summary>创建警戒层运行时节点，保存拔刀、调查、搜索和收刀阶段。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new EnemyAlertRoutineNode(this);
        }

        /// <summary>默认动作入口不会被使用，警戒层需要自定义阶段机。</summary>
        protected override BehaviorTreeStatus Execute(BehaviorTreeContext context)
        {
            return BehaviorTreeStatus.Failure;
        }

        private enum AlertRoutineStage
        {
            Uninitialized,
            EnteringCombatStance,
            MovingToLastKnownPosition,
            InspectingLastKnownPosition,
            MovingToSearchPoint,
            InspectingSearchPoint,
            ExitingCombatStance
        }

        private sealed class EnemyAlertRoutineNode : BehaviorTreeNode
        {
            private const float PositionRefreshThreshold = 0.01f;
            private const float DestinationRefreshSqrThreshold = 0.0001f;

            private AlertRoutineStage stage = AlertRoutineStage.Uninitialized;
            private Vector3 activeLastKnownPosition;
            private Vector3[] searchPoints = new Vector3[0];
            private int searchIndex;
            private float waitRemaining;
            private string activeEnterAnimation;
            private string activeExitAnimation;
            private bool hasActiveMoveDestination;
            private Vector3 activeMoveDestination;

            /// <summary>绑定警戒层资产，运行时从未初始化阶段开始。</summary>
            public EnemyAlertRoutineNode(BehaviorTreeNodeAsset asset)
                : base(asset)
            {
            }

            /// <summary>执行警戒层调查流程；战斗目标出现时交给根节点抢占。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (!EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller))
                {
                    Reset();
                    return BehaviorTreeStatus.Failure;
                }

                if (controller.Blackboard.HasCombatTarget)
                {
                    Reset();
                    return BehaviorTreeStatus.Failure;
                }

                if (!TryGetMovement(controller, out EnemyMovementComponent movement))
                {
                    Reset();
                    return BehaviorTreeStatus.Failure;
                }

                if (controller.Blackboard.HasAlertMemory && ShouldRestartInvestigation(controller))
                {
                    BeginInvestigation(controller);
                }

                if (stage == AlertRoutineStage.Uninitialized)
                {
                    BeginInitialStage(controller);
                }

                switch (stage)
                {
                    case AlertRoutineStage.EnteringCombatStance:
                        return TickEnteringCombatStance(controller);
                    case AlertRoutineStage.MovingToLastKnownPosition:
                        return TickMovingToLastKnownPosition(controller, movement);
                    case AlertRoutineStage.InspectingLastKnownPosition:
                        return TickInspectingLastKnownPosition(context, controller, movement);
                    case AlertRoutineStage.MovingToSearchPoint:
                        return TickMovingToSearchPoint(controller, movement);
                    case AlertRoutineStage.InspectingSearchPoint:
                        return TickInspectingSearchPoint(context, controller, movement);
                    case AlertRoutineStage.ExitingCombatStance:
                        return TickExitingCombatStance(controller, movement);
                    default:
                        Reset();
                        return BehaviorTreeStatus.Failure;
                }
            }

            /// <summary>重置警戒层局部阶段，不清理黑板中的警戒记忆和退出握手。</summary>
            public override void Reset()
            {
                stage = AlertRoutineStage.Uninitialized;
                activeLastKnownPosition = default;
                searchPoints = new Vector3[0];
                searchIndex = 0;
                waitRemaining = 0f;
                activeEnterAnimation = null;
                activeExitAnimation = null;
                hasActiveMoveDestination = false;
                activeMoveDestination = default;
            }

            /// <summary>根据当前黑板事实选择初始阶段：新警戒调查或继续收刀退出。</summary>
            private void BeginInitialStage(AIController controller)
            {
                if (controller.Blackboard.HasAlertMemory)
                {
                    BeginInvestigation(controller);
                    return;
                }

                BeginExit();
            }

            /// <summary>开始调查最新警戒位置；已经拔刀时直接移动，否则先进入拔刀阶段。</summary>
            private void BeginInvestigation(AIController controller)
            {
                activeLastKnownPosition = controller.Blackboard.AlertLastKnownPosition;
                searchPoints = new Vector3[0];
                searchIndex = 0;
                waitRemaining = 0f;
                activeExitAnimation = null;
                hasActiveMoveDestination = false;
                stage = controller.Blackboard.HasCombatStance
                    ? AlertRoutineStage.MovingToLastKnownPosition
                    : AlertRoutineStage.EnteringCombatStance;
            }

            /// <summary>进入警戒退出阶段，后续完成收刀并请求正常层返程。</summary>
            private void BeginExit()
            {
                stage = AlertRoutineStage.ExitingCombatStance;
                waitRemaining = 0f;
                searchPoints = new Vector3[0];
                searchIndex = 0;
                hasActiveMoveDestination = false;
            }

            /// <summary>播放拔刀动画；缺少动画时立即标记已拔刀并进入调查移动。</summary>
            private BehaviorTreeStatus TickEnteringCombatStance(AIController controller)
            {
                if (controller.Blackboard.HasCombatStance)
                {
                    stage = AlertRoutineStage.MovingToLastKnownPosition;
                    return BehaviorTreeStatus.Running;
                }

                if (string.IsNullOrEmpty(activeEnterAnimation))
                {
                    activeEnterAnimation = controller.Definition != null
                        ? controller.Definition.AnimationConfig.enterCombatAnimation
                        : null;
                    StopMovement(controller);
                    controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Alert);
                    if (controller.Context == null
                        || controller.Context.Animation == null
                        || !controller.Context.Animation.TryPlay(activeEnterAnimation))
                    {
                        ShowEnemyWeapons(controller);
                        CompleteEnterCombatStance(controller);
                    }

                    return BehaviorTreeStatus.Running;
                }

                if (controller.Context != null
                    && controller.Context.Animation != null
                    && controller.Context.Animation.IsPlaying(activeEnterAnimation, out float normalizedTime)
                    && normalizedTime < 1f)
                {
                    controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Alert);
                    return BehaviorTreeStatus.Running;
                }

                CompleteEnterCombatStance(controller);
                return BehaviorTreeStatus.Running;
            }

            /// <summary>完成拔刀表现并进入最后已知位置调查，武器显隐由动画事件负责。</summary>
            private void CompleteEnterCombatStance(AIController controller)
            {
                controller.Blackboard.SetCombatStance(true);
                activeEnterAnimation = null;
                stage = AlertRoutineStage.MovingToLastKnownPosition;
            }

            /// <summary>移动到警戒记忆中的最后已知位置，记忆过期则转入收刀退出。</summary>
            private BehaviorTreeStatus TickMovingToLastKnownPosition(
                AIController controller,
                EnemyMovementComponent movement)
            {
                if (!controller.Blackboard.HasAlertMemory)
                {
                    BeginExit();
                    return BehaviorTreeStatus.Running;
                }

                activeLastKnownPosition = controller.Blackboard.AlertLastKnownPosition;
                PlayMoveAnimation(controller);
                controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Alert);
                MoveByNavMesh(controller, movement, activeLastKnownPosition);
                if (!movement.HasReached(activeLastKnownPosition, movement.StoppingDistance))
                {
                    return BehaviorTreeStatus.Running;
                }

                movement.Stop();
                hasActiveMoveDestination = false;
                BeginWait(AlertRoutineStage.InspectingLastKnownPosition, controller);
                return BehaviorTreeStatus.Running;
            }

            /// <summary>在最后已知位置观察，观察结束后生成有限搜索点。</summary>
            private BehaviorTreeStatus TickInspectingLastKnownPosition(
                BehaviorTreeContext context,
                AIController controller,
                EnemyMovementComponent movement)
            {
                movement.Stop();
                PlayIdleAnimation(controller);
                if (!controller.Blackboard.HasAlertMemory)
                {
                    BeginExit();
                    return BehaviorTreeStatus.Running;
                }

                if (ShouldRestartInvestigation(controller))
                {
                    BeginInvestigation(controller);
                    return BehaviorTreeStatus.Running;
                }

                if (TickWait(context))
                {
                    return BehaviorTreeStatus.Running;
                }

                controller.Blackboard.SetSearching(true);
                searchPoints = controller.Context != null && controller.Context.Perception != null
                    ? controller.Context.Perception.GenerateSearchPoints(activeLastKnownPosition)
                    : new[] { activeLastKnownPosition };
                searchIndex = 0;
                stage = AlertRoutineStage.MovingToSearchPoint;
                return BehaviorTreeStatus.Running;
            }

            /// <summary>移动到当前搜索点；搜索点耗尽或记忆过期时进入退出流程。</summary>
            private BehaviorTreeStatus TickMovingToSearchPoint(
                AIController controller,
                EnemyMovementComponent movement)
            {
                if (!controller.Blackboard.HasAlertMemory || searchIndex >= searchPoints.Length)
                {
                    RequestExit(controller);
                    return BehaviorTreeStatus.Running;
                }

                if (ShouldRestartInvestigation(controller))
                {
                    BeginInvestigation(controller);
                    return BehaviorTreeStatus.Running;
                }

                Vector3 destination = searchPoints[searchIndex];
                PlayMoveAnimation(controller);
                controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Alert);
                MoveByNavMesh(controller, movement, destination);
                if (!movement.HasReached(destination, movement.StoppingDistance))
                {
                    return BehaviorTreeStatus.Running;
                }

                movement.Stop();
                hasActiveMoveDestination = false;
                BeginWait(AlertRoutineStage.InspectingSearchPoint, controller);
                return BehaviorTreeStatus.Running;
            }

            /// <summary>在搜索点观察，完成后推进到下一个点或退出警戒层。</summary>
            private BehaviorTreeStatus TickInspectingSearchPoint(
                BehaviorTreeContext context,
                AIController controller,
                EnemyMovementComponent movement)
            {
                movement.Stop();
                PlayIdleAnimation(controller);
                if (!controller.Blackboard.HasAlertMemory)
                {
                    RequestExit(controller);
                    return BehaviorTreeStatus.Running;
                }

                if (ShouldRestartInvestigation(controller))
                {
                    BeginInvestigation(controller);
                    return BehaviorTreeStatus.Running;
                }

                if (TickWait(context))
                {
                    return BehaviorTreeStatus.Running;
                }

                searchIndex++;
                stage = AlertRoutineStage.MovingToSearchPoint;
                return BehaviorTreeStatus.Running;
            }

            /// <summary>播放收刀退出；完成后清理警戒握手并请求正常层返程。</summary>
            private BehaviorTreeStatus TickExitingCombatStance(
                AIController controller,
                EnemyMovementComponent movement)
            {
                if (controller.Blackboard.HasAlertMemory)
                {
                    BeginInvestigation(controller);
                    return BehaviorTreeStatus.Running;
                }

                movement.Stop();
                if (!controller.Blackboard.HasCombatStance)
                {
                    CompleteExit(controller);
                    return BehaviorTreeStatus.Running;
                }

                if (string.IsNullOrEmpty(activeExitAnimation))
                {
                    activeExitAnimation = controller.Definition != null
                        ? controller.Definition.AnimationConfig.exitCombatAnimation
                        : null;
                    controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Alert);
                    if (controller.Context == null
                        || controller.Context.Animation == null
                        || !controller.Context.Animation.TryPlay(activeExitAnimation))
                    {
                        HideEnemyWeapons(controller);
                        CompleteExit(controller);
                    }

                    return BehaviorTreeStatus.Running;
                }

                if (controller.Context != null
                    && controller.Context.Animation != null
                    && controller.Context.Animation.IsPlaying(activeExitAnimation, out float normalizedTime)
                    && normalizedTime < 1f)
                {
                    return BehaviorTreeStatus.Running;
                }

                CompleteExit(controller);
                return BehaviorTreeStatus.Running;
            }

            /// <summary>完成警戒退出并要求正常层返回原点，武器显隐由动画事件负责。</summary>
            private void CompleteExit(AIController controller)
            {
                controller.Blackboard.SetCombatStance(false);
                SetCombatState(controller, false);
                controller.Blackboard.SetSearching(false);
                controller.Blackboard.CompleteAlertExit();
                controller.Blackboard.SetNeedsReturnHome(true);
                controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Idle);
                activeExitAnimation = null;
            }

            /// <summary>同步警戒退出后的战斗状态到黑板和 Animator，保证 IsCombat 参数及时归零。</summary>
            private static void SetCombatState(AIController controller, bool isInCombat)
            {
                controller.Blackboard.SetCombatState(isInCombat);
                if (controller.Context != null && controller.Context.Animation != null)
                {
                    controller.Context.Animation.SetCombatStateParameter(isInCombat);
                }
            }

            /// <summary>搜索点耗尽时主动请求警戒退出，统一走收刀和返程握手。</summary>
            private void RequestExit(AIController controller)
            {
                controller.Blackboard.RequestAlertExit();
                controller.Blackboard.SetSearching(false);
                BeginExit();
            }

            /// <summary>进入观察等待阶段，等待时长读取警戒观察配置。</summary>
            private void BeginWait(AlertRoutineStage waitStage, AIController controller)
            {
                stage = waitStage;
                waitRemaining = Mathf.Max(0f, controller.SearchObservationDuration);
            }

            /// <summary>推进观察等待倒计时，返回 true 表示仍需等待。</summary>
            private bool TickWait(BehaviorTreeContext context)
            {
                if (waitRemaining <= 0f)
                {
                    return false;
                }

                waitRemaining -= GetDeltaTime(context);
                return waitRemaining > 0f;
            }

            /// <summary>判断最后已知位置是否被新情报刷新，需要废弃旧搜索点。</summary>
            private bool ShouldRestartInvestigation(AIController controller)
            {
                return controller.Blackboard.HasAlertMemory
                    && (activeLastKnownPosition - controller.Blackboard.AlertLastKnownPosition).sqrMagnitude
                    > PositionRefreshThreshold;
            }

            /// <summary>读取行为树上下文时间步长；测试未设置时回退到 Unity 时间。</summary>
            private static float GetDeltaTime(BehaviorTreeContext context)
            {
                return context != null && context.DeltaTime > 0f ? context.DeltaTime : Time.deltaTime;
            }

            /// <summary>读取移动组件，缺失时警戒层失败退出。</summary>
            private static bool TryGetMovement(AIController controller, out EnemyMovementComponent movement)
            {
                movement = controller.Context != null ? controller.Context.Movement : null;
                if (movement != null)
                {
                    return true;
                }

                Debug.LogError("警戒状态层缺少 EnemyMovementComponent，无法执行调查或退出。", controller);
                return false;
            }

            /// <summary>停止当前移动，用于拔刀、观察和收刀阶段。</summary>
            private static void StopMovement(AIController controller)
            {
                if (controller.Context != null && controller.Context.Movement != null)
                {
                    controller.Context.Movement.Stop();
                }
            }

            /// <summary>播放普通待机动画，缺少动画组件时不影响警戒流程。</summary>
            private static void PlayIdleAnimation(AIController controller)
            {
                if (controller.Context != null && controller.Context.Animation != null)
                {
                    controller.Context.Animation.TryPlay(
                        controller.Definition != null ? controller.Definition.AnimationConfig.idleAnimation : null);
                }
            }

            /// <summary>播放警戒专用移动动画，实际水平位移由移动组件按 NavMesh 路径驱动。</summary>
            private static void PlayMoveAnimation(AIController controller)
            {
                if (controller.Context != null && controller.Context.Animation != null)
                {
                    controller.Context.Animation.TryPlay(
                        controller.Definition != null ? controller.Definition.AnimationConfig.alertMoveAnimation : null);
                }
            }

            /// <summary>下发警戒移动寻路目标，并让警戒移动动画 RootMotion 步幅沿 NavMesh 路径消耗。</summary>
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

            /// <summary>动画无法播放时兜底切到手持武器。</summary>
            private static void ShowEnemyWeapons(AIController controller)
            {
                EnemyAgent agent = controller.Context != null ? controller.Context.Agent as EnemyAgent : null;
                if (agent != null)
                {
                    agent.ShowWeapons();
                }
            }

            /// <summary>动画无法播放时兜底切到背负武器。</summary>
            private static void HideEnemyWeapons(AIController controller)
            {
                EnemyAgent agent = controller.Context != null ? controller.Context.Agent as EnemyAgent : null;
                if (agent != null)
                {
                    agent.HideWeapons();
                }
            }
        }
    }
}
