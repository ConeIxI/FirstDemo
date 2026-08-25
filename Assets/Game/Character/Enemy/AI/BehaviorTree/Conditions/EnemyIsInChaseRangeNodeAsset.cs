using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Is In Chase Range")]
    public sealed class EnemyIsInChaseRangeNodeAsset : ConditionNodeAsset
    {
        /// <summary>判断战斗目标是否处于追击范围内，供中距保持分支使用。</summary>
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            return EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                && controller.Blackboard.HasCombatTarget
                && controller.Blackboard.IsInChaseRange;
        }
    }
}
