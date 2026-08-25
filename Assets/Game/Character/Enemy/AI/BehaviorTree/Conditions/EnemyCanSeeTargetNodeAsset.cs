using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Can See Target")]
    public sealed class EnemyCanSeeTargetNodeAsset : ConditionNodeAsset
    {
        // 判断黑板目标当前是否真实可见，目标记忆不能冒充视野命中。
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            return EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                && controller.Blackboard.IsTargetVisible;
        }
    }
}
