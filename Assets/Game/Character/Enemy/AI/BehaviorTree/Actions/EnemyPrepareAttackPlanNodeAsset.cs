using Game.Character.Enemy.AI.Combat;
using Game.Character.Enemy.Components;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Prepare Attack Plan")]
    public sealed class EnemyPrepareAttackPlanNodeAsset : ActionNodeAsset
    {
        /// <summary>创建攻击计划准备节点的运行时实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new EnemyPrepareAttackPlanNode(this);
        }

        /// <summary>资产节点不直接执行，攻击计划准备逻辑由运行时节点逐帧处理。</summary>
        protected override BehaviorTreeStatus Execute(BehaviorTreeContext context)
        {
            return BehaviorTreeStatus.Failure;
        }

        private sealed class EnemyPrepareAttackPlanNode : BehaviorTreeNode
        {
            /// <summary>绑定攻击计划准备资产，运行时节点本身不持有跨帧状态。</summary>
            public EnemyPrepareAttackPlanNode(BehaviorTreeNodeAsset asset)
                : base(asset)
            {
            }

            /// <summary>按当前攻击计划判断是否可释放；不足释放距离时驱动接近或追击移动。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (!TryGetController(context, out AIController controller))
                {
                    return BehaviorTreeStatus.Failure;
                }

                EnemyAttackPlan plan = controller.CombatDecision.CurrentPlan;
                if (CanReleaseAttack(controller, plan))
                {
                    PrepareRelease(controller);
                    return BehaviorTreeStatus.Success;
                }

                if (plan.PreparationMode == EnemyAttackPreparationMode.Direct)
                {
                    ClearAttackLayerState(controller);
                    return BehaviorTreeStatus.Failure;
                }

                MoveTowardTarget(controller);
                EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
                return BehaviorTreeStatus.Running;
            }

            /// <summary>准备节点没有本地状态，重置时不改动已锁定的攻击计划。</summary>
            public override void Reset()
            {
            }

            /// <summary>读取准备节点所需的控制器、目标、决策器和移动组件。</summary>
            private static bool TryGetController(BehaviorTreeContext context, out AIController controller)
            {
                controller = null;
                if (!EnemyBehaviorTreeUtility.TryGetController(context, out controller))
                {
                    return false;
                }

                return controller.Blackboard != null
                    && controller.Blackboard.HasCombatTarget
                    && controller.CombatDecision != null
                    && controller.CombatDecision.CurrentPlan != null
                    && controller.Context != null
                    && controller.Context.Movement != null;
            }

            /// <summary>判断目标距离和朝向是否已满足当前计划的释放条件。</summary>
            private static bool CanReleaseAttack(AIController controller, EnemyAttackPlan plan)
            {
                return (!plan.CurrentAttack.EnableAttackDistanceCheck
                        || controller.Blackboard.DistanceToTarget <= plan.ReleaseDistance)
                    && EnemyBehaviorTreeUtility.IsTargetInFront(controller);
            }

            /// <summary>停止位移并面向目标，攻击阶段由攻击流节点统一初始化。</summary>
            private static void PrepareRelease(AIController controller)
            {
                controller.Context.Movement.Stop();
                controller.Context.Movement.LookAtInstant(controller.Blackboard.CombatTarget.position);
                EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
            }

            /// <summary>播放攻击前接近跑步动画，并持续移动到战斗目标。</summary>
            private static void MoveTowardTarget(AIController controller)
            {
                PlayMoveAnimation(controller);
                if (controller.Context.Animation != null)
                {
                    // 移动动画提供 RootMotion 步幅，实际方向和碰撞由 Movement 按 NavMesh 路径处理。
                    controller.Context.Animation.SetRootMotionSuppressed(true);
                }

                controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Approach);
                controller.Context.Movement.MoveTo(controller.Blackboard.CombatTarget);
            }

            /// <summary>攻击准备阶段需要补距离时统一播放跑步动画。</summary>
            private static void PlayMoveAnimation(AIController controller)
            {
                EnemyAnimationComponent animation = controller.Context.Animation;
                if (animation == null || controller.Definition == null)
                {
                    return;
                }

                animation.TryPlay(controller.Definition.AnimationConfig.runAnimation);
            }

            /// <summary>清理无法直接释放的攻击计划，避免攻击层在无效距离上反复尝试。</summary>
            private static void ClearAttackLayerState(AIController controller)
            {
                controller.CombatDecision.ResetAttack();
                controller.Blackboard.ClearAttackIntent();
                controller.Blackboard.SetCombatIntent(EnemyCombatIntent.None);
                EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
            }
        }
    }
}
