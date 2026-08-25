using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Is Beyond Chase Range")]
    public sealed class EnemyIsBeyondChaseRangeNodeAsset : ConditionNodeAsset
    {
        /// <summary>判断战斗目标是否已经超出追击范围，需要使用追击移动。</summary>
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            return EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                && controller.Blackboard.HasCombatTarget
                && !controller.Blackboard.IsInChaseRange;
        }
    }
}
