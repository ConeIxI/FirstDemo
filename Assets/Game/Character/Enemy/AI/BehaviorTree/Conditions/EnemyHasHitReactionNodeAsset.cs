using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Has Hit Reaction")]
    public sealed class EnemyHasHitReactionNodeAsset : ConditionNodeAsset
    {
        // 判断黑板中是否存在待消费或正在播放的受击反应。
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            return EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                && (controller.Blackboard.HasHitReaction || controller.Blackboard.IsHitReactionInProgress);
        }
    }
}
