using Game.Character.Enemy.AI;
using Game.Character.Enemy.AI.Combat;
using Game.Character.Enemy.Components;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Generate Attack Intent")]
    public sealed class EnemyGenerateAttackIntentNodeAsset : ActionNodeAsset
    {
        /// <summary>创建生成攻击意图节点的运行时实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new EnemyGenerateAttackIntentNode(this);
        }

        /// <summary>资产节点不直接执行，攻击意图由运行时节点按帧生成。</summary>
        protected override BehaviorTreeStatus Execute(BehaviorTreeContext context)
        {
            return BehaviorTreeStatus.Failure;
        }

        private sealed class EnemyGenerateAttackIntentNode : BehaviorTreeNode
        {
            /// <summary>初始化攻击意图生成节点。</summary>
            public EnemyGenerateAttackIntentNode(BehaviorTreeNodeAsset asset)
                : base(asset)
            {
            }

            /// <summary>已有攻击意图时交给攻击分支；新生成意图时保持战斗层运行，避免同帧掉到普通层重置朝向。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (!TryGetController(context, out AIController controller))
                {
                    return BehaviorTreeStatus.Success;
                }

                if (controller.Blackboard.HasAttackIntent)
                {
                    return BehaviorTreeStatus.Failure;
                }

                // 生成新攻击意图前清掉上一次动画事件标记，避免旧连招窗口误触发新技能衔接。
                controller.Context.Combat.ClearComboAdvanceRequest();

                bool created = controller.CombatDecision.TryCreateAttackPlan(
                    Time.time,
                    GetStabilityRatio(controller),
                    controller.Blackboard.DistanceToTarget,
                    controller.Context.Combat.ChaseRange,
                    Random.value,
                    Random.value,
                    Random.value);
                if (created)
                {
                    controller.Blackboard.SetAttackIntent(EnemyCombatIntent.Attack);
                    EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
                    return BehaviorTreeStatus.Running;
                }

                EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
                return BehaviorTreeStatus.Success;
            }

            /// <summary>生成节点不持有跨帧运行状态，重置时无需额外处理。</summary>
            public override void Reset()
            {
            }

            /// <summary>获取生成攻击意图所需的敌人控制器和战斗决策器。</summary>
            private static bool TryGetController(BehaviorTreeContext context, out AIController controller)
            {
                controller = null;
                if (!EnemyBehaviorTreeUtility.TryGetController(context, out controller))
                {
                    return false;
                }

                return controller.Blackboard != null
                    && controller.CombatDecision != null
                    && controller.Context != null
                    && controller.Context.Combat != null;
            }

            /// <summary>读取敌人当前稳定值比例，缺少属性组件时按满稳定值处理。</summary>
            private static float GetStabilityRatio(AIController controller)
            {
                EnemyAttributeComponent attribute = controller.Context.Attribute;
                if (attribute == null || attribute.MaxStability <= 0)
                {
                    return 1f;
                }

                return (float)attribute.Stability / attribute.MaxStability;
            }
        }
    }
}
