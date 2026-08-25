using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Is Dead")]
    public sealed class EnemyIsDeadNodeAsset : ConditionNodeAsset
    {
        // 判断黑板是否标记敌人已死亡。
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            return EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                && controller.Blackboard.IsDead;
        }
    }
}
