using Game.Character.Enemy.AI.Combat;
using Game.Character.Enemy.Config;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Defense")]
    public sealed class EnemyDefenseNodeAsset : ActionNodeAsset
    {
        /// <summary>创建防御运行时节点，保存防御动画播放中的跨帧状态。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new EnemyDefenseNode(this);
        }

        /// <summary>资产层不直接执行，防御流程由运行时节点消费反应事实。</summary>
        protected override BehaviorTreeStatus Execute(BehaviorTreeContext context)
        {
            return BehaviorTreeStatus.Failure;
        }

        private sealed class EnemyDefenseNode : BehaviorTreeNode
        {
            private const float DefaultDefenseDuration = 1.5f;

            private AIController activeController;
            private string defenseAnimationName;
            private string defenseHitAnimationName;
            private string activeAnimationName;
            private bool isPlayingDefenseHit;
            private bool isRestartingDefenseAnimation;
            private float defenseRemainingTime;

            /// <summary>初始化防御节点运行时状态。</summary>
            public EnemyDefenseNode(BehaviorTreeNodeAsset asset)
                : base(asset)
            {
            }

            /// <summary>消费待处理防御反应，并在防御动画窗口结束后决定反击或退出防御。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (!TryGetDefenseController(context, out AIController controller))
                {
                    Reset();
                    return BehaviorTreeStatus.Failure;
                }

                if (activeController == null)
                {
                    return StartDefense(controller);
                }

                if (controller.Context.Combat.ConsumeDefenseHitReaction())
                {
                    controller.CombatDecision.RecordDefenseBlock();
                    RefreshDefenseDuration(controller);
                    if (TryStartImmediateCounterAttack(controller, out BehaviorTreeStatus counterStatus))
                    {
                        return counterStatus;
                    }

                    return StartDefenseHit(controller);
                }

                TickDefenseDuration(context);

                if (defenseRemainingTime <= 0f)
                {
                    return FinishDefenseWindow(controller);
                }

                if (controller.Context.Animation != null
                    && controller.Context.Animation.IsPlaying(activeAnimationName, out float normalizedTime)
                    && (normalizedTime < 1f || isRestartingDefenseAnimation))
                {
                    // 同名防御动画重播的过渡期可能仍读到旧状态进度，等待新状态真正落地后再允许下一次重播。
                    if (normalizedTime < 1f)
                    {
                        isRestartingDefenseAnimation = false;
                    }

                    return BehaviorTreeStatus.Running;
                }

                return ContinueDefense(controller);
            }

            /// <summary>重置防御节点时清理防御状态，避免被更高优先级分支打断后残留格挡。</summary>
            public override void Reset()
            {
                FinishDefense();
            }

            /// <summary>行为树层退出时清理防御状态，保证层级切换不会遗留格挡标签。</summary>
            public override void OnLayerExit()
            {
                FinishDefense();
            }

            /// <summary>校验防御节点所需控制器、决策器和战斗组件。</summary>
            private static bool TryGetDefenseController(BehaviorTreeContext context, out AIController controller)
            {
                controller = null;
                if (!EnemyBehaviorTreeUtility.TryGetController(context, out controller))
                {
                    return false;
                }

                return controller.CombatDecision != null
                    && controller.Context != null
                    && controller.Context.Combat != null;
            }

            /// <summary>启动防御动画和防御状态，并清除已被本节点消费的反应事实。</summary>
            private BehaviorTreeStatus StartDefense(AIController controller)
            {
                if (controller.CombatDecision.PendingReaction != EnemyCombatReaction.Defense)
                {
                    return BehaviorTreeStatus.Failure;
                }

                activeController = controller;
                defenseAnimationName = controller.Definition != null
                    ? controller.Definition.AnimationConfig.defenseAnimation
                    : null;
                defenseHitAnimationName = controller.Definition != null
                    ? controller.Definition.AnimationConfig.defenseHitAnimation
                    : null;
                activeAnimationName = defenseAnimationName;
                isPlayingDefenseHit = false;
                isRestartingDefenseAnimation = false;
                RefreshDefenseDuration(controller);

                if (controller.Context.Movement != null)
                {
                    controller.Context.Movement.Stop();
                }

                controller.CombatDecision.EnterReactionState(EnemyCombatReaction.Defense);
                controller.Context.Combat.StartDefense();
                controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Defense);
                controller.Blackboard.MarkDefenseDecision(Time.time);
                EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);

                if (controller.Context.Animation == null
                    || !controller.Context.Animation.TryPlay(activeAnimationName))
                {
                    FinishDefense();
                    return BehaviorTreeStatus.Success;
                }

                return BehaviorTreeStatus.Running;
            }

            /// <summary>在防御状态中播放一次防御受击动画，播放期间只累计格挡，不生成反击计划。</summary>
            private BehaviorTreeStatus StartDefenseHit(AIController controller)
            {
                if (controller.Context.Animation == null
                    || !controller.Context.Animation.TryPlay(defenseHitAnimationName, forceRestart: true))
                {
                    return ContinueDefense(controller);
                }

                activeAnimationName = defenseHitAnimationName;
                isPlayingDefenseHit = true;
                isRestartingDefenseAnimation = false;
                return BehaviorTreeStatus.Running;
            }

            /// <summary>防御时间未结束时重播防御动画，保持防御状态的持续表现。</summary>
            private BehaviorTreeStatus ContinueDefense(AIController controller)
            {
                activeAnimationName = defenseAnimationName;
                isPlayingDefenseHit = false;
                if (controller.Context.Animation == null
                    || !controller.Context.Animation.TryPlay(defenseAnimationName, forceRestart: true))
                {
                    FinishDefense();
                    return BehaviorTreeStatus.Success;
                }

                isRestartingDefenseAnimation = true;
                return BehaviorTreeStatus.Running;
            }

            /// <summary>按行为树时间推进防御倒计时，时间耗尽后本节点会退出防御。</summary>
            private void TickDefenseDuration(BehaviorTreeContext context)
            {
                defenseRemainingTime = Mathf.Max(0f, defenseRemainingTime - context.DeltaTime);
            }

            /// <summary>刷新防御持续时间，用于初次防御和防御中格挡命中续时。</summary>
            private void RefreshDefenseDuration(AIController controller)
            {
                defenseRemainingTime = Mathf.Max(0f, GetDefenseDuration(controller));
            }

            /// <summary>读取敌人配置的防御持续时间，缺少定义时使用运行时默认值。</summary>
            private static float GetDefenseDuration(AIController controller)
            {
                EnemyDecisionProfile profile = controller.DecisionProfile;
                return profile != null ? profile.defenseDuration : DefaultDefenseDuration;
            }

            /// <summary>格挡达到反击阈值时立即尝试反击，条件不足时继续保留防御状态。</summary>
            private bool TryStartImmediateCounterAttack(AIController controller, out BehaviorTreeStatus status)
            {
                status = BehaviorTreeStatus.Failure;
                if (!controller.CombatDecision.HasPendingCounter)
                {
                    return false;
                }

                if (!controller.CombatDecision.TryCreateCounterPlan(
                    controller.Blackboard.DistanceToTarget,
                    EnemyBehaviorTreeUtility.IsTargetInFront(controller)))
                {
                    return false;
                }

                HandOffCounterAttack(controller);
                status = BehaviorTreeStatus.Success;
                return true;
            }

            /// <summary>防御持续时间结束后，优先交给反击计划，否则正常退出防御。</summary>
            private BehaviorTreeStatus FinishDefenseWindow(AIController controller)
            {
                if (TryResolveCounterAttack(controller, out BehaviorTreeStatus counterStatus))
                {
                    return counterStatus;
                }

                FinishDefense();
                return BehaviorTreeStatus.Success;
            }

            /// <summary>按待反击标记、距离和朝向决定是否立即交给攻击分支。</summary>
            private bool TryResolveCounterAttack(AIController controller, out BehaviorTreeStatus status)
            {
                status = BehaviorTreeStatus.Failure;
                if (!controller.CombatDecision.HasPendingCounter)
                {
                    return false;
                }

                if (controller.CombatDecision.TryCreateCounterPlan(
                    controller.Blackboard.DistanceToTarget,
                    EnemyBehaviorTreeUtility.IsTargetInFront(controller)))
                {
                    HandOffCounterAttack(controller);
                    status = BehaviorTreeStatus.Success;
                    return true;
                }

                controller.CombatDecision.ResetDefense();
                FinishDefense();
                status = BehaviorTreeStatus.Success;
                return true;
            }

            /// <summary>停止防御并保留已创建的反击攻击计划，让攻击分支下一帧接管。</summary>
            private void HandOffCounterAttack(AIController controller)
            {
                controller.Context.Combat.StopDefense();
                controller.CombatDecision.ClearReaction();
                controller.Blackboard.SetAttackIntent(EnemyCombatIntent.Attack);
                controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Attack);
                EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
                ClearRuntimeState();
            }

            /// <summary>结束防御状态、清理反应和防御计数，并释放运行时缓存。</summary>
            private void FinishDefense()
            {
                if (activeController == null)
                {
                    return;
                }

                activeController.Context.Combat.StopDefense();
                activeController.CombatDecision.ClearReaction();
                activeController.CombatDecision.ResetDefense();
                activeController.Blackboard.SetCombatIntent(EnemyCombatIntent.Idle);
                EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(activeController);
                ClearRuntimeState();
            }

            /// <summary>释放防御节点本地缓存，不改动已经交给决策器的攻击计划。</summary>
            private void ClearRuntimeState()
            {
                defenseAnimationName = null;
                defenseHitAnimationName = null;
                activeAnimationName = null;
                isPlayingDefenseHit = false;
                isRestartingDefenseAnimation = false;
                defenseRemainingTime = 0f;
                activeController = null;
            }
        }
    }
}
