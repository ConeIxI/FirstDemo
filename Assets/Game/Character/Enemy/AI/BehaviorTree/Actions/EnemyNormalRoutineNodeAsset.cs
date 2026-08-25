using Game.Character.Enemy.Components;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Normal Routine")]
    public sealed class EnemyNormalRoutineNodeAsset : ActionNodeAsset
    {
        /// <summary>创建正常状态层运行时节点，运行时独立保存巡逻阶段和等待时间。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new EnemyNormalRoutineNode(this);
        }

        /// <summary>默认动作入口不会被使用，正常层需要自定义运行时阶段机。</summary>
        protected override BehaviorTreeStatus Execute(BehaviorTreeContext context)
        {
            return BehaviorTreeStatus.Failure;
        }

        private enum NormalRoutineStage
        {
            Uninitialized,
            ReturningOrigin,
            WaitingAtOrigin,
            MovingToWaypoint,
            WaitingAtWaypoint,
            IdleAtOrigin
        }

        private sealed class EnemyNormalRoutineNode : BehaviorTreeNode
        {
            private const float DestinationRefreshSqrThreshold = 0.0001f;

            private NormalRoutineStage stage = NormalRoutineStage.Uninitialized;
            private int waypointIndex;
            private float waitRemaining;
            private bool hasEnteredOnce;
            private bool hasActiveMoveDestination;
            private Vector3 activeMoveDestination;

            /// <summary>绑定正常层资产，初始化阶段机为未启动状态。</summary>
            public EnemyNormalRoutineNode(BehaviorTreeNodeAsset asset)
                : base(asset)
            {
            }

            /// <summary>执行正常层返程、待机和巡逻循环；有效状态下始终保持 Running。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (!EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller))
                {
                    Reset();
                    return BehaviorTreeStatus.Failure;
                }

                if (!TryGetMovement(controller, out EnemyMovementComponent movement))
                {
                    return BehaviorTreeStatus.Failure;
                }

                Transform[] route = controller.PatrolRoute;
                if (HasInvalidRoute(controller, route))
                {
                    Reset();
                    return BehaviorTreeStatus.Failure;
                }

                if (controller.Blackboard.NeedsReturnHome)
                {
                    BeginReturnToOrigin(route);
                }

                if (stage == NormalRoutineStage.Uninitialized)
                {
                    BeginInitialStage(controller, movement, route);
                }

                switch (stage)
                {
                    case NormalRoutineStage.ReturningOrigin:
                        return TickReturningOrigin(controller, movement, route);
                    case NormalRoutineStage.WaitingAtOrigin:
                        return TickWaitingAtOrigin(context, controller, movement, route);
                    case NormalRoutineStage.MovingToWaypoint:
                        return TickMovingToWaypoint(controller, movement, route);
                    case NormalRoutineStage.WaitingAtWaypoint:
                        return TickWaitingAtWaypoint(context, controller, movement, route);
                    case NormalRoutineStage.IdleAtOrigin:
                        return TickIdleAtOrigin(controller, movement);
                    default:
                        Reset();
                        return BehaviorTreeStatus.Failure;
                }
            }

            /// <summary>重置正常层局部阶段机，下次进入时重新从原点流程开始。</summary>
            public override void Reset()
            {
                stage = NormalRoutineStage.Uninitialized;
                waypointIndex = 0;
                waitRemaining = 0f;
                hasActiveMoveDestination = false;
                activeMoveDestination = default;
            }

            /// <summary>首次进入正常层会对齐原点；后续被普通中断重置时不隐式触发返程。</summary>
            private void BeginInitialStage(
                AIController controller,
                EnemyMovementComponent movement,
                Transform[] route)
            {
                if (!hasEnteredOnce)
                {
                    hasEnteredOnce = true;
                    BeginReturnToOrigin(route);
                    return;
                }

                if (route.Length > 1)
                {
                    BeginMoveToWaypoint(controller, movement, route, 1);
                    return;
                }

                stage = NormalRoutineStage.IdleAtOrigin;
            }

            /// <summary>进入返程阶段，多路点巡逻返程完成后从索引 1 开始推进。</summary>
            private void BeginReturnToOrigin(Transform[] route)
            {
                stage = NormalRoutineStage.ReturningOrigin;
                waypointIndex = route.Length > 1 ? 1 : 0;
                waitRemaining = 0f;
                hasActiveMoveDestination = false;
            }

            /// <summary>移动回正常层原点，抵达后清理返程请求并进入待机或巡逻等待。</summary>
            private BehaviorTreeStatus TickReturningOrigin(
                AIController controller,
                EnemyMovementComponent movement,
                Transform[] route)
            {
                Vector3 origin = controller.NormalOriginPosition;
                PlayMoveAnimation(controller);
                MoveByNavMesh(controller, movement, origin);
                if (!movement.HasReached(origin, movement.StoppingDistance))
                {
                    return BehaviorTreeStatus.Running;
                }

                StopAtOrigin(controller, movement);
                hasActiveMoveDestination = false;
                controller.Blackboard.SetNeedsReturnHome(false);
                if (route.Length <= 1)
                {
                    stage = NormalRoutineStage.IdleAtOrigin;
                    return BehaviorTreeStatus.Running;
                }

                BeginWait(NormalRoutineStage.WaitingAtOrigin, controller);
                return BehaviorTreeStatus.Running;
            }

            /// <summary>在原点等待；等待结束后多路点路线从第二个路点开始巡逻。</summary>
            private BehaviorTreeStatus TickWaitingAtOrigin(
                BehaviorTreeContext context,
                AIController controller,
                EnemyMovementComponent movement,
                Transform[] route)
            {
                StopAtOrigin(controller, movement);
                if (TickWait(context))
                {
                    return BehaviorTreeStatus.Running;
                }

                if (route.Length <= 1)
                {
                    stage = NormalRoutineStage.IdleAtOrigin;
                    return BehaviorTreeStatus.Running;
                }

                BeginMoveToWaypoint(controller, movement, route, waypointIndex);
                return BehaviorTreeStatus.Running;
            }

            /// <summary>移动到当前巡逻路点，抵达后进入该路点等待阶段。</summary>
            private BehaviorTreeStatus TickMovingToWaypoint(
                AIController controller,
                EnemyMovementComponent movement,
                Transform[] route)
            {
                waypointIndex = Mathf.Clamp(waypointIndex, 0, route.Length - 1);
                Vector3 destination = route[waypointIndex].position;
                PlayMoveAnimation(controller);
                MoveByNavMesh(controller, movement, destination);
                if (!movement.HasReached(destination, movement.StoppingDistance))
                {
                    return BehaviorTreeStatus.Running;
                }

                movement.Stop();
                hasActiveMoveDestination = false;
                BeginWait(NormalRoutineStage.WaitingAtWaypoint, controller);
                return BehaviorTreeStatus.Running;
            }

            /// <summary>在巡逻路点等待，等待结束后按数组顺序循环到下一个路点。</summary>
            private BehaviorTreeStatus TickWaitingAtWaypoint(
                BehaviorTreeContext context,
                AIController controller,
                EnemyMovementComponent movement,
                Transform[] route)
            {
                movement.Stop();
                PlayIdleAnimation(controller);
                if (TickWait(context))
                {
                    return BehaviorTreeStatus.Running;
                }

                int nextIndex = (waypointIndex + 1) % route.Length;
                BeginMoveToWaypoint(controller, movement, route, nextIndex);
                return BehaviorTreeStatus.Running;
            }

            /// <summary>无巡逻或单路点时保持原点待机，等待其它高优先级层抢占。</summary>
            private BehaviorTreeStatus TickIdleAtOrigin(AIController controller, EnemyMovementComponent movement)
            {
                StopAtOrigin(controller, movement);
                controller.Blackboard.SetNeedsReturnHome(false);
                return BehaviorTreeStatus.Running;
            }

            /// <summary>记录等待阶段和等待时长，巡逻停留由敌人移动配置统一驱动。</summary>
            private void BeginWait(NormalRoutineStage waitStage, AIController controller)
            {
                stage = waitStage;
                waitRemaining = Mathf.Max(0f, controller.PatrolWaitDuration);
            }

            /// <summary>设置当前巡逻目标，并让 Root Motion 移动动画朝目标推进。</summary>
            private void BeginMoveToWaypoint(
                AIController controller,
                EnemyMovementComponent movement,
                Transform[] route,
                int index)
            {
                waypointIndex = index;
                stage = NormalRoutineStage.MovingToWaypoint;
                PlayMoveAnimation(controller);
                MoveByNavMesh(controller, movement, route[waypointIndex].position);
            }

            /// <summary>推进等待倒计时，返回 true 表示仍需继续等待。</summary>
            private bool TickWait(BehaviorTreeContext context)
            {
                if (waitRemaining <= 0f)
                {
                    return false;
                }

                waitRemaining -= GetDeltaTime(context);
                return waitRemaining > 0f;
            }

            /// <summary>停止移动并恢复原点朝向和待机动画。</summary>
            private static void StopAtOrigin(AIController controller, EnemyMovementComponent movement)
            {
                movement.Stop();
                controller.transform.rotation = controller.NormalOriginRotation;
                PlayIdleAnimation(controller);
            }

            /// <summary>播放普通待机动画，缺少动画组件时仅保持移动状态。</summary>
            private static void PlayIdleAnimation(AIController controller)
            {
                if (controller.Context != null && controller.Context.Animation != null)
                {
                    controller.Context.Animation.TryPlay(
                        controller.Definition != null ? controller.Definition.AnimationConfig.idleAnimation : null);
                }
            }

            /// <summary>播放普通移动动画，供返程和巡逻移动共用。</summary>
            private static void PlayMoveAnimation(AIController controller)
            {
                if (controller.Context != null && controller.Context.Animation != null)
                {
                    controller.Context.Animation.TryPlay(
                        controller.Definition != null ? controller.Definition.AnimationConfig.moveAnimation : null);
                }
            }

            /// <summary>下发 NavMesh 目的地，并让普通移动动画 RootMotion 步幅沿路径消耗。</summary>
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

            /// <summary>读取行为树上下文时间步长；测试未设置时回退到 Unity 时间。</summary>
            private static float GetDeltaTime(BehaviorTreeContext context)
            {
                return context != null && context.DeltaTime > 0f ? context.DeltaTime : Time.deltaTime;
            }

            /// <summary>读取敌人移动组件，缺失时记录错误并让层失败退出。</summary>
            private static bool TryGetMovement(AIController controller, out EnemyMovementComponent movement)
            {
                movement = controller.Context != null ? controller.Context.Movement : null;
                if (movement != null)
                {
                    return true;
                }

                Debug.LogError("正常状态层缺少 EnemyMovementComponent，无法执行返程或巡逻。", controller);
                return false;
            }

            /// <summary>校验巡逻路线是否包含空路点，避免运行时移动到无效引用。</summary>
            private static bool HasInvalidRoute(AIController controller, Transform[] route)
            {
                for (int i = 0; i < route.Length; i++)
                {
                    if (route[i] == null)
                    {
                        Debug.LogError("正常状态层巡逻路线包含空路点。", controller);
                        return true;
                    }
                }

                return false;
            }
        }
    }
}

