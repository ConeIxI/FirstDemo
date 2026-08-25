using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Has Combat Target")]
    public sealed class EnemyHasCombatTargetNodeAsset : ConditionNodeAsset
    {
        /// <summary>判断黑板是否持有战斗目标，CombatLayer 只在该事实有效时运行。</summary>
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            return EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                && controller.Blackboard.HasCombatTarget;
        }
    }
}
