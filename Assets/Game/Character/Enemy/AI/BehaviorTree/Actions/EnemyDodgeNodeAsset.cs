using Game.Battle.Ability;
using Game.Character.Enemy.AI.Combat;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Dodge")]
    public sealed class EnemyDodgeNodeAsset : ActionNodeAsset
    {
        /// <summary>创建闪避运行时节点，保存无敌标签和闪避动画的跨帧状态。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new EnemyDodgeNode(this);
        }

        /// <summary>资产层不直接执行，闪避流程由运行时节点维护无敌生命周期。</summary>
        protected override BehaviorTreeStatus Execute(BehaviorTreeContext context)
        {
            return BehaviorTreeStatus.Failure;
        }

        private sealed class EnemyDodgeNode : BehaviorTreeNode
        {
            private AIController activeController;
            private CombatAbilitySystem activeAbilitySystem;
            private string activeAnimationName;

            /// <summary>初始化闪避节点运行时状态。</summary>
            public EnemyDodgeNode(BehaviorTreeNodeAsset asset)
                : base(asset)
            {
            }

            /// <summary>消费待处理闪避反应，并在闪避动画结束后移除无敌标签。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (!TryGetDodgeController(context, out AIController controller))
                {
                    Reset();
                    return BehaviorTreeStatus.Failure;
                }

                if (activeController == null)
                {
                    return StartDodge(controller);
                }

                if (controller.Context.Animation != null
                    && controller.Context.Animation.IsPlaying(activeAnimationName, out float normalizedTime)
                    && normalizedTime < 1f)
                {
                    return BehaviorTreeStatus.Running;
                }

                FinishDodge();
                return BehaviorTreeStatus.Success;
            }

            /// <summary>重置闪避节点时移除无敌标签，避免被打断后残留无敌。</summary>
            public override void Reset()
            {
                FinishDodge();
            }

            /// <summary>行为树层退出时移除无敌标签，保证层级切换不会遗留闪避状态。</summary>
            public override void OnLayerExit()
            {
                FinishDodge();
            }

            /// <summary>校验闪避节点所需控制器、决策器和上下文。</summary>
            private static bool TryGetDodgeController(BehaviorTreeContext context, out AIController controller)
            {
                controller = null;
                if (!EnemyBehaviorTreeUtility.TryGetController(context, out controller))
                {
                    return false;
                }

                return controller.CombatDecision != null
                    && controller.Context != null;
            }

            /// <summary>启动闪避动画并添加无敌标签，随后等待动画生命周期结束。</summary>
            private BehaviorTreeStatus StartDodge(AIController controller)
            {
                if (controller.CombatDecision.PendingReaction != EnemyCombatReaction.Dodge)
                {
                    return BehaviorTreeStatus.Failure;
                }

                CombatAbilitySystem abilitySystem = controller.GetComponent<CombatAbilitySystem>();
                if (abilitySystem == null)
                {
                    return BehaviorTreeStatus.Failure;
                }

                activeController = controller;
                activeAbilitySystem = abilitySystem;
                activeAnimationName = controller.Definition != null
                    ? controller.Definition.AnimationConfig.retreatAnimation
                    : null;

                if (controller.Context.Movement != null)
                {
                    controller.Context.Movement.Stop();
                }

                controller.CombatDecision.EnterReactionState(EnemyCombatReaction.Dodge);
                activeAbilitySystem.AddTag(CombatTag.Invincible);
                controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Retreat);
                controller.Blackboard.MarkRetreatDecision(Time.time);
                EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);

                if (controller.Context.Animation == null
                    || !controller.Context.Animation.TryPlay(activeAnimationName))
                {
                    FinishDodge();
                    return BehaviorTreeStatus.Success;
                }

                return BehaviorTreeStatus.Running;
            }

            /// <summary>结束闪避状态、移除无敌标签并清理反应事实。</summary>
            private void FinishDodge()
            {
                if (activeAbilitySystem != null)
                {
                    activeAbilitySystem.RemoveTag(CombatTag.Invincible);
                }

                if (activeController != null)
                {
                    activeController.CombatDecision.ClearReaction();
                    activeController.Blackboard.ClearAttackIntent();
                    activeController.Blackboard.SetCombatIntent(EnemyCombatIntent.Idle);
                    EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(activeController);
                }

                activeAnimationName = null;
                activeAbilitySystem = null;
                activeController = null;
            }
        }
    }
}
