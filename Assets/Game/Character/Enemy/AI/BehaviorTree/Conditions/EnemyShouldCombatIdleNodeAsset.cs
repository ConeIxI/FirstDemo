using Game.Character.Enemy.Core;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Should Combat Idle")]
    public sealed class EnemyShouldCombatIdleNodeAsset : ConditionNodeAsset
    {
        /// <summary>判断敌人是否仍在战斗中且已经位于当前待机距离阈值内。</summary>
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            if (!EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                || controller.Context == null
                || controller.Context.Movement == null)
            {
                return false;
            }

            EnemyBlackboard blackboard = controller.Blackboard;
            if (!blackboard.HasCombatTarget)
            {
                return false;
            }

            if (blackboard.CurrentIntent == EnemyCombatIntent.Approach
                || blackboard.CurrentIntent == EnemyCombatIntent.Attack)
            {
                return false;
            }

            return blackboard.IsInChaseRange;
        }
    }
}
