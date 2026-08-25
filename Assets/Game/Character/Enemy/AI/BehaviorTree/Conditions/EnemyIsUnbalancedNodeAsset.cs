using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Is Unbalanced")]
    public sealed class EnemyIsUnbalancedNodeAsset : ConditionNodeAsset
    {
        // 判断黑板是否标记敌人处于失衡状态。
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            return EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                && controller.Blackboard.IsUnbalanced;
        }
    }
}
