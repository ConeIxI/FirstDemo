using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Has Attack Intent")]
    public sealed class EnemyHasAttackIntentNodeAsset : ConditionNodeAsset
    {
        /// <summary>判断黑板中是否存在独立的攻击意图。</summary>
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            return EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                && controller.Blackboard.HasAttackIntent;
        }
    }
}
