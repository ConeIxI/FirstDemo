using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Should Search Last Known Position")]
    public sealed class EnemyShouldSearchLastKnownPositionNodeAsset : ConditionNodeAsset
    {
        // 判断目标记忆已经转入搜索态且仍有最后已知位置，需要进入真正的搜索流程。
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            return EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                && controller.Blackboard.IsSearching
                && controller.Blackboard.HasLastKnownPosition;
        }
    }
}
