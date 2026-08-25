using Game.Character.Enemy.AI.Combat;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Attack Flow")]
    public sealed class EnemyAttackFlowNodeAsset : ActionNodeAsset
    {
        /// <summary>创建攻击流程运行时节点，保存攻击阶段内的跨帧状态。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new EnemyAttackFlowNode(this);
        }

        /// <summary>资产层不直接执行，攻击流程需要运行时节点维护阶段。</summary>
        protected override BehaviorTreeStatus Execute(BehaviorTreeContext context)
        {
            return BehaviorTreeStatus.Failure;
        }

        private sealed class EnemyAttackFlowNode : BehaviorTreeNode
        {
            private AIController activeController;

            /// <summary>初始化攻击流程节点实例。</summary>
            public EnemyAttackFlowNode(BehaviorTreeNodeAsset asset)
                : base(asset)
            {
            }

            /// <summary>按攻击意图和攻击阶段推进起手、动画等待和收尾。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (!TryGetAttackController(context, out AIController controller))
                {
                    return BehaviorTreeStatus.Failure;
                }

                if (!controller.Blackboard.HasAttackIntent)
                {
                    return BehaviorTreeStatus.Failure;
                }

                activeController = controller;
                EnemyCombatDecisionController decision = controller.CombatDecision;

                switch (decision.AttackPhase)
                {
                    case EnemyAttackPhase.None:
                        if (!decision.TryBeginCurrentAttack(Random.value))
                        {
                            ClearAttackLayerState();
                            return BehaviorTreeStatus.Failure;
                        }

                        EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
                        return TickStart(controller, decision);
                    case EnemyAttackPhase.Start:
                        return TickStart(controller, decision);
                    case EnemyAttackPhase.Active:
                        return TickActive(controller, decision);
                    case EnemyAttackPhase.End:
                        return TickEnd(controller, decision);
                    default:
                        return BehaviorTreeStatus.Failure;
                }
            }

            /// <summary>重置攻击流运行状态，并清理仍由攻击流持有的攻击层状态。</summary>
            public override void Reset()
            {
                ClearAttackLayerState();
            }

            /// <summary>攻击层退出时清理攻击层状态和独立攻击意图。</summary>
            public override void OnLayerExit()
            {
                ClearAttackLayerState();
            }

            /// <summary>校验攻击流程所需控制器、上下文、战斗组件和目标。</summary>
            private static bool TryGetAttackController(BehaviorTreeContext context, out AIController controller)
            {
                controller = null;
                if (!EnemyBehaviorTreeUtility.TryGetController(context, out controller))
                {
                    return false;
                }

                return controller.CombatDecision != null
                    && controller.Context != null
                    && controller.Context.Combat != null
                    && controller.Blackboard.HasCombatTarget;
            }

            /// <summary>起手阶段停止移动、面向目标、启动技能并播放当前攻击动画。</summary>
            private static BehaviorTreeStatus TickStart(
                AIController controller,
                EnemyCombatDecisionController decision)
            {
                EnemyAttackPlan plan = decision.CurrentPlan;
                if (plan == null
                    || plan.CurrentAttack == null
                    || plan.CurrentAttack.SkillId <= 0
                    || string.IsNullOrEmpty(plan.CurrentAttack.AnimationName))
                {
                    ClearAttackLayerState(controller);
                    EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
                    return BehaviorTreeStatus.Failure;
                }

                EnemyAttackRuntimeConfig attack = plan.CurrentAttack;

                if (controller.Context.Movement != null)
                {
                    controller.Context.Movement.Stop();
                    controller.Context.Movement.LookAtInstant(controller.Blackboard.CombatTarget.position);
                }

                if (!controller.Context.Combat.TryStartAttack(attack.SkillId)
                    || controller.Context.Animation == null
                    || !controller.Context.Animation.TryPlay(attack.AnimationName, interruptCurrentAction: false))
                {
                    controller.Context.Combat.InterruptAction();
                    ClearAttackLayerState(controller);
                    EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
                    return BehaviorTreeStatus.Failure;
                }

                controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Attack);
                decision.SetAttackPhase(EnemyAttackPhase.Active);
                EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
                return BehaviorTreeStatus.Running;
            }

            /// <summary>攻击进行阶段优先响应提前连招窗口，否则等待当前动画播完后进入收尾。</summary>
            private BehaviorTreeStatus TickActive(
                AIController controller,
                EnemyCombatDecisionController decision)
            {
                if (controller.Context.Movement != null)
                {
                    controller.Context.Movement.LookAtForAttack(controller.Blackboard.CombatTarget.position);
                }

                if (controller.Context.Combat.ConsumeComboAdvanceRequest()
                    && TryAdvanceComboFromAnimationEvent(controller, decision))
                {
                    return BehaviorTreeStatus.Running;
                }

                if (controller.Context.Animation != null)
                {
                    if (controller.Context.Animation.IsPlaying(decision.CurrentAnimationName, out float normalizedTime)
                        && normalizedTime < 1f)
                    {
                        return BehaviorTreeStatus.Running;
                    }
                }

                decision.SetAttackPhase(EnemyAttackPhase.End);
                EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
                return TickEnd(controller, decision);
            }

            /// <summary>动画事件打开提前连招窗口时尝试切到下一段，失败时不重置当前攻击。</summary>
            private static bool TryAdvanceComboFromAnimationEvent(
                AIController controller,
                EnemyCombatDecisionController decision)
            {
                if (decision.PendingReaction != EnemyCombatReaction.None)
                {
                    return false;
                }

                if (!decision.TryAdvanceComboFromAnimationEvent(
                    Random.value,
                    controller.Blackboard.DistanceToTarget,
                    EnemyBehaviorTreeUtility.IsTargetInFront(controller)))
                {
                    return false;
                }

                controller.Context.Combat.InterruptAction();
                EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
                return true;
            }

            /// <summary>攻击收尾阶段结束动作，优先交出待处理反应，否则尝试推进组合段或接力追击动画。</summary>
            private BehaviorTreeStatus TickEnd(
                AIController controller,
                EnemyCombatDecisionController decision)
            {
                EnemyAttackRuntimeConfig currentAttack = GetCurrentAttack(decision);
                controller.Context.Combat.InterruptAction();
                if (decision.PendingReaction != EnemyCombatReaction.None)
                {
                    EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
                    return BehaviorTreeStatus.Success;
                }

                if (ShouldFollowUpWithMovement(controller, currentAttack))
                {
                    ConsumeCurrentAttackAndPlayFollowUpMovement(controller);
                    return BehaviorTreeStatus.Success;
                }

                if (decision.TryAdvanceCombo(
                    Random.value,
                    controller.Blackboard.DistanceToTarget,
                    EnemyBehaviorTreeUtility.IsTargetInFront(controller)))
                {
                    EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
                    return BehaviorTreeStatus.Running;
                }

                decision.CompleteCurrentPlan();
                ClearAttackLayerState(controller);
                controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Idle);
                EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
                return BehaviorTreeStatus.Success;
            }

            /// <summary>读取当前攻击动作，供攻击结束阶段判断动作级距离规则。</summary>
            private static EnemyAttackRuntimeConfig GetCurrentAttack(EnemyCombatDecisionController decision)
            {
                EnemyAttackPlan plan = decision.CurrentPlan;
                return plan != null ? plan.CurrentAttack : null;
            }

            /// <summary>判断当前攻击动作是否需要在目标超出范围后接力移动。</summary>
            private static bool ShouldFollowUpWithMovement(
                AIController controller,
                EnemyAttackRuntimeConfig attack)
            {
                return attack != null
                    && attack.EnableAttackDistanceCheck
                    && attack.AttackRange > 0f
                    && controller.Blackboard.DistanceToTarget > attack.AttackRange
                    && controller.Context.Movement != null;
            }

            /// <summary>消费已经播完的攻击意图和计划，再交给移动动画接力追击目标。</summary>
            private void ConsumeCurrentAttackAndPlayFollowUpMovement(AIController controller)
            {
                controller.CombatDecision.CompleteCurrentPlan();
                ClearAttackLayerState(controller);
                activeController = null;
                PlayAttackFollowUpMovement(controller);
                EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
            }

            /// <summary>攻击动画结束后立即播放接近或追击动画，避免画面停在攻击动画末帧。</summary>
            private static void PlayAttackFollowUpMovement(AIController controller)
            {
                string animationName = controller.Blackboard.IsInChaseRange
                    ? controller.Definition.AnimationConfig.moveAnimation
                    : controller.Definition.AnimationConfig.runAnimation;
                if (controller.Context.Animation != null)
                {
                    controller.Context.Animation.TryPlay(animationName);
                    // 移动动画提供 RootMotion 步幅，实际方向和碰撞由 Movement 按 NavMesh 路径处理。
                    controller.Context.Animation.SetRootMotionSuppressed(true);
                }

                controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Approach);
                controller.Context.Movement.MoveTo(controller.Blackboard.CombatTarget);
            }

            /// <summary>清理攻击层的决策器阶段、组合路线和黑板攻击意图。</summary>
            private void ClearAttackLayerState()
            {
                if (activeController == null)
                {
                    return;
                }

                ClearAttackLayerState(activeController);
                activeController = null;
            }

            /// <summary>清理指定敌人的攻击阶段、组合路线和攻击意图。</summary>
            private static void ClearAttackLayerState(AIController controller)
            {
                if (controller.Context.Combat.IsActing)
                {
                    // 攻击层被父节点打断时，必须同步结束 Combat 动作，避免动画停在最后一帧后无法切换状态。
                    controller.Context.Combat.InterruptAction();
                }

                controller.CombatDecision.ResetAttack();
                controller.Blackboard.ClearAttackIntent();
                controller.Blackboard.SetCombatIntent(EnemyCombatIntent.None);
                EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
            }
        }
    }
}
