using Game.Character.Enemy.Core;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Ensure Combat Stance")]
    public sealed class EnemyEnsureCombatStanceNodeAsset : ActionNodeAsset
    {
        /// <summary>创建确保战斗姿态的运行时节点，避免正常层直接进战斗时漏播拔刀。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new EnemyEnsureCombatStanceNode(this);
        }

        /// <summary>默认动作入口不会被使用，战斗姿态需要保存动画进度。</summary>
        protected override BehaviorTreeStatus Execute(BehaviorTreeContext context)
        {
            return BehaviorTreeStatus.Failure;
        }

        private sealed class EnemyEnsureCombatStanceNode : BehaviorTreeNode
        {
            private string activeEnterAnimation;

            /// <summary>初始化战斗姿态节点运行时状态。</summary>
            public EnemyEnsureCombatStanceNode(BehaviorTreeNodeAsset asset)
                : base(asset)
            {
            }

            /// <summary>确认敌人已拔刀；未拔刀时播放拔刀动画并在完成后返回 Success。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (!EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                    || !controller.Blackboard.HasCombatTarget)
                {
                    Reset();
                    return BehaviorTreeStatus.Failure;
                }

                if (controller.Blackboard.HasCombatStance)
                {
                    Reset();
                    return BehaviorTreeStatus.Success;
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
                        CompleteCombatStance(controller);
                        return BehaviorTreeStatus.Success;
                    }

                    return BehaviorTreeStatus.Running;
                }

                if (controller.Context != null
                    && controller.Context.Animation != null
                    && controller.Context.Animation.IsPlaying(activeEnterAnimation, out float normalizedTime)
                    && normalizedTime < 1f)
                {
                    return BehaviorTreeStatus.Running;
                }

                CompleteCombatStance(controller);
                return BehaviorTreeStatus.Success;
            }

            /// <summary>重置拔刀动画局部进度，不清理黑板战斗姿态事实。</summary>
            public override void Reset()
            {
                activeEnterAnimation = null;
            }

            /// <summary>完成拔刀流程并写入战斗姿态事实，武器显隐由动画事件负责。</summary>
            private void CompleteCombatStance(AIController controller)
            {
                controller.Blackboard.SetCombatStance(true);
                activeEnterAnimation = null;
            }

            /// <summary>动画无法播放时兜底切到手持武器，避免敌人进入战斗后仍显示背部武器。</summary>
            private static void ShowEnemyWeapons(AIController controller)
            {
                EnemyAgent agent = controller.Context != null ? controller.Context.Agent as EnemyAgent : null;
                if (agent != null)
                {
                    agent.ShowWeapons();
                }
            }

            /// <summary>进入拔刀表现前停止移动，避免动画期间继续滑行。</summary>
            private static void StopMovement(AIController controller)
            {
                if (controller.Context != null && controller.Context.Movement != null)
                {
                    controller.Context.Movement.Stop();
                }
            }
        }
    }
}
