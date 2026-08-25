using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;
using Game.Character.Enemy.Config;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Interrupt Executor")]
    public sealed class EnemyInterruptExecutorNodeAsset : BehaviorTreeNodeAsset
    {
        [SerializeField] private BehaviorTreeNodeAsset deadSequence;
        [SerializeField] private BehaviorTreeNodeAsset unbalanceSequence;
        [SerializeField] private BehaviorTreeNodeAsset getHitSequence;

        /// <summary>创建统一中断执行器运行时节点，每个中断子树都有独立运行时实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new EnemyInterruptExecutorNode(
                this,
                deadSequence != null ? deadSequence.CreateRuntimeNode() : null,
                unbalanceSequence != null ? unbalanceSequence.CreateRuntimeNode() : null,
                getHitSequence != null ? getHitSequence.CreateRuntimeNode() : null);
        }

        private enum InterruptType
        {
            None,
            Dead,
            GetUp,
            Unbalance,
            DefenseBreak,
            Hit
        }

        private sealed class EnemyInterruptExecutorNode : BehaviorTreeNode
        {
            private readonly BehaviorTreeNode deadNode;
            private readonly BehaviorTreeNode unbalanceNode;
            private readonly BehaviorTreeNode hitNode;
            private string activeDefenseBreakAnimation;
            private string activeGetUpAnimation;
            private bool hasStartedDefenseBreak;
            private bool hasStartedGetUp;
            private InterruptType currentType;

            /// <summary>初始化统一中断执行器，并缓存三个独立中断运行时子树。</summary>
            public EnemyInterruptExecutorNode(
                BehaviorTreeNodeAsset asset,
                BehaviorTreeNode deadNode,
                BehaviorTreeNode unbalanceNode,
                BehaviorTreeNode hitNode)
                : base(asset)
            {
                this.deadNode = deadNode;
                this.unbalanceNode = unbalanceNode;
                this.hitNode = hitNode;
            }

            /// <summary>按死亡、失衡、受击优先级选择中断子树；无中断时让低优先级层继续。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (!EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller))
                {
                    Reset();
                    return BehaviorTreeStatus.Failure;
                }

                InterruptType nextType = ResolveInterruptType(controller);
                if (nextType == InterruptType.None)
                {
                    ResetCurrentSubtree();
                    currentType = InterruptType.None;
                    return BehaviorTreeStatus.Failure;
                }

                if (nextType != currentType)
                {
                    PrepareTypeSwitch(controller, nextType);
                }

                return TickCurrentSubtree(controller, nextType, context);
            }

            /// <summary>重置三个中断子树的局部运行时状态，不清理黑板权威事实。</summary>
            public override void Reset()
            {
                deadNode?.Reset();
                unbalanceNode?.Reset();
                hitNode?.Reset();
                ResetDefenseBreakRuntime();
                ResetGetUpRuntime();
                currentType = InterruptType.None;
            }

            /// <summary>根据黑板事实解析当前应执行的最高优先级中断类型。</summary>
            private static InterruptType ResolveInterruptType(AIController controller)
            {
                if (controller.Blackboard.IsDead)
                {
                    return InterruptType.Dead;
                }

                if (controller.Blackboard.IsGetUpReactionInProgress || controller.Blackboard.HasGetUpReaction)
                {
                    return InterruptType.GetUp;
                }

                if (controller.Blackboard.IsUnbalanced)
                {
                    return InterruptType.Unbalance;
                }

                if (controller.Blackboard.IsDefenseBreakReactionInProgress
                    || controller.Blackboard.HasDefenseBreakReaction)
                {
                    return InterruptType.DefenseBreak;
                }

                return controller.Blackboard.IsHitReactionInProgress || controller.Blackboard.HasHitReaction
                    ? InterruptType.Hit
                    : InterruptType.None;
            }

            /// <summary>切换中断类型前清理低优先级事实，并重置旧子树局部进度。</summary>
            private void PrepareTypeSwitch(AIController controller, InterruptType nextType)
            {
                ResetCurrentSubtree();
                if (nextType == InterruptType.GetUp || nextType == InterruptType.Unbalance || nextType == InterruptType.Dead)
                {
                    controller.Blackboard.ClearHitReactionState();
                    controller.Blackboard.ClearDefenseBreakReactionState();
                }

                if (nextType == InterruptType.DefenseBreak)
                {
                    controller.Blackboard.ClearHitReactionState();
                }

                if (nextType == InterruptType.Dead)
                {
                    controller.Blackboard.SetUnbalanced(false);
                    controller.Blackboard.ClearGetUpReactionState();
                }

                currentType = nextType;
            }

            /// <summary>执行当前中断子树，并按子树结果收束对应黑板事实。</summary>
            private BehaviorTreeStatus TickCurrentSubtree(
                AIController controller,
                InterruptType type,
                BehaviorTreeContext context)
            {
                if (type == InterruptType.DefenseBreak)
                {
                    BehaviorTreeStatus defenseBreakStatus = TickDefenseBreak(controller);
                    if (defenseBreakStatus == BehaviorTreeStatus.Running)
                    {
                        return BehaviorTreeStatus.Running;
                    }

                    FinishNonDeadInterrupt(controller, type, defenseBreakStatus);
                    currentType = InterruptType.None;
                    return BehaviorTreeStatus.Failure;
                }

                if (type == InterruptType.GetUp)
                {
                    BehaviorTreeStatus getUpStatus = TickGetUp(controller);
                    if (getUpStatus == BehaviorTreeStatus.Running)
                    {
                        return BehaviorTreeStatus.Running;
                    }

                    FinishGetUp(controller);
                    currentType = InterruptType.None;
                    return BehaviorTreeStatus.Failure;
                }

                BehaviorTreeNode node = GetRuntimeNode(type);
                if (node == null)
                {
                    ClearFactsForMissingSubtree(controller, type);
                    currentType = InterruptType.None;
                    return BehaviorTreeStatus.Failure;
                }

                BehaviorTreeStatus status = node.Tick(context);
                if (type == InterruptType.Dead)
                {
                    return BehaviorTreeStatus.Running;
                }

                if (status == BehaviorTreeStatus.Running)
                {
                    return BehaviorTreeStatus.Running;
                }

                FinishNonDeadInterrupt(controller, type, status);
                currentType = InterruptType.None;
                return status == BehaviorTreeStatus.Success && type == InterruptType.Hit && controller.Blackboard.HasHitReaction
                    ? BehaviorTreeStatus.Running
                    : BehaviorTreeStatus.Failure;
            }

            /// <summary>根据中断类型返回对应运行时子树。</summary>
            private BehaviorTreeNode GetRuntimeNode(InterruptType type)
            {
                switch (type)
                {
                    case InterruptType.Dead:
                        return deadNode;
                    case InterruptType.Unbalance:
                        return unbalanceNode;
                    case InterruptType.Hit:
                        return hitNode;
                    default:
                        return null;
                }
            }

            /// <summary>重置当前正在运行的子树，不影响其它中断子树缓存。</summary>
            private void ResetCurrentSubtree()
            {
                if (currentType == InterruptType.DefenseBreak)
                {
                    ResetDefenseBreakRuntime();
                    return;
                }

                if (currentType == InterruptType.GetUp)
                {
                    ResetGetUpRuntime();
                    return;
                }

                GetRuntimeNode(currentType)?.Reset();
            }

            /// <summary>子树缺失时清理对应事实，避免执行器每帧重复进入失败中断。</summary>
            private static void ClearFactsForMissingSubtree(AIController controller, InterruptType type)
            {
                if (type == InterruptType.Unbalance)
                {
                    controller.Blackboard.SetUnbalanced(false);
                    return;
                }

                if (type == InterruptType.GetUp)
                {
                    controller.Blackboard.ClearGetUpReactionState();
                    return;
                }

                if (type == InterruptType.Hit)
                {
                    controller.Blackboard.ClearHitReactionState();
                }
            }

            /// <summary>执行未失衡弹反破防动画，并在动画播完前保持中断层运行。</summary>
            private BehaviorTreeStatus TickDefenseBreak(AIController controller)
            {
                if (!hasStartedDefenseBreak)
                {
                    return StartDefenseBreak(controller);
                }

                if (controller.Context != null
                    && controller.Context.Animation != null
                    && controller.Context.Animation.IsPlaying(activeDefenseBreakAnimation, out float normalizedTime)
                    && normalizedTime < 1f)
                {
                    return BehaviorTreeStatus.Running;
                }

                FinishDefenseBreak(controller);
                return BehaviorTreeStatus.Success;
            }

            /// <summary>首次进入未失衡弹反破防时停止移动并播放 DefenseBreak，不触发失衡开始参数。</summary>
            private BehaviorTreeStatus StartDefenseBreak(AIController controller)
            {
                if (controller.Context != null && controller.Context.Movement != null)
                {
                    controller.Context.Movement.Stop();
                }

                string animationName = controller.Blackboard.ConsumeDefenseBreakReaction();
                if (string.IsNullOrEmpty(animationName))
                {
                    animationName = GetDefenseBreakAnimation(controller);
                }

                if (controller.Context == null || controller.Context.Animation == null)
                {
                    FinishDefenseBreak(controller);
                    return BehaviorTreeStatus.Success;
                }

                if (!controller.Context.Animation.TryPlay(
                        animationName,
                        interruptCurrentAction: false,
                        forceRestart: true))
                {
                    FinishDefenseBreak(controller);
                    return BehaviorTreeStatus.Failure;
                }

                activeDefenseBreakAnimation = animationName;
                hasStartedDefenseBreak = true;
                controller.Blackboard.SetDefenseBreakReactionInProgress(true);
                return BehaviorTreeStatus.Running;
            }

            /// <summary>结束未失衡弹反破防运行时状态，交还控制给普通行为树分支。</summary>
            private void FinishDefenseBreak(AIController controller)
            {
                ResetDefenseBreakRuntime();
                controller.Blackboard.SetDefenseBreakReactionInProgress(false);
            }

            /// <summary>执行处决后的起身动画，动画结束前阻断普通 AI 和受击表现覆盖。</summary>
            private BehaviorTreeStatus TickGetUp(AIController controller)
            {
                if (!hasStartedGetUp)
                {
                    return StartGetUp(controller);
                }

                if (controller.Context != null
                    && controller.Context.Animation != null
                    && controller.Context.Animation.IsPlaying(activeGetUpAnimation, out float normalizedTime)
                    && normalizedTime < 1f)
                {
                    return BehaviorTreeStatus.Running;
                }

                return BehaviorTreeStatus.Success;
            }

            /// <summary>首次进入起身时停止移动并播放 GetUp，战斗伤害仍可结算但不切换受击动画。</summary>
            private BehaviorTreeStatus StartGetUp(AIController controller)
            {
                if (controller.Context != null && controller.Context.Movement != null)
                {
                    controller.Context.Movement.Stop();
                }

                string animationName = controller.Blackboard.ConsumeGetUpReaction();
                if (string.IsNullOrEmpty(animationName))
                {
                    animationName = "GetUp";
                }

                if (controller.Context == null || controller.Context.Animation == null)
                {
                    return BehaviorTreeStatus.Success;
                }

                if (!controller.Context.Animation.TryPlay(
                        animationName,
                        interruptCurrentAction: false,
                        forceRestart: true))
                {
                    return BehaviorTreeStatus.Failure;
                }

                activeGetUpAnimation = animationName;
                hasStartedGetUp = true;
                controller.Blackboard.SetGetUpReactionInProgress(true);
                return BehaviorTreeStatus.Running;
            }

            /// <summary>结束起身运行时状态，交还控制给普通行为树分支。</summary>
            private void FinishGetUp(AIController controller)
            {
                ResetGetUpRuntime();
                controller.Blackboard.SetGetUpReactionInProgress(false);
            }

            /// <summary>重置未失衡弹反破防运行时缓存，不直接改写黑板事实。</summary>
            private void ResetDefenseBreakRuntime()
            {
                activeDefenseBreakAnimation = null;
                hasStartedDefenseBreak = false;
            }

            /// <summary>重置起身运行时缓存，不直接改写黑板权威事实。</summary>
            private void ResetGetUpRuntime()
            {
                activeGetUpAnimation = null;
                hasStartedGetUp = false;
            }

            /// <summary>读取破防入口动画名，定义缺失时使用约定状态名。</summary>
            private static string GetDefenseBreakAnimation(AIController controller)
            {
                EnemyAnimationConfig animationConfig = controller.Definition != null
                    ? controller.Definition.AnimationConfig
                    : null;
                return animationConfig != null
                    ? animationConfig.defenseBreakAnimation
                    : "DefenseBreak";
            }

            /// <summary>非死亡中断结束后清理对应事实，并在有待处理受击时重置受击子树等待下一帧继续。</summary>
            private void FinishNonDeadInterrupt(
                AIController controller,
                InterruptType type,
                BehaviorTreeStatus status)
            {
                if (type == InterruptType.Unbalance)
                {
                    controller.Blackboard.SetUnbalanced(false);
                    controller.Blackboard.ClearHitReactionState();
                    controller.Blackboard.ClearDefenseBreakReactionState();
                    unbalanceNode?.Reset();
                    return;
                }

                if (type == InterruptType.DefenseBreak)
                {
                    controller.Blackboard.ClearDefenseBreakReactionState();
                    ResetDefenseBreakRuntime();
                    return;
                }

                if (type != InterruptType.Hit)
                {
                    return;
                }

                if (status == BehaviorTreeStatus.Success && controller.Blackboard.HasHitReaction)
                {
                    hitNode?.Reset();
                    return;
                }

                controller.Blackboard.ClearHitReactionState();
                hitNode?.Reset();
            }
        }
    }
}
