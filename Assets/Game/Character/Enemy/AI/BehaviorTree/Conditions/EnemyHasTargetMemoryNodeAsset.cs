using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Has Target Memory")]
    public sealed class EnemyHasTargetMemoryNodeAsset : ConditionNodeAsset
    {
        // 判断黑板是否仍保留目标或最后已知位置。
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            return EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                && controller.Blackboard.HasTargetMemory;
        }
    }
}
