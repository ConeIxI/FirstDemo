using Game.Character.Enemy.AI.Combat;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Has Combat Reaction")]
    public sealed class EnemyHasCombatReactionNodeAsset : ConditionNodeAsset
    {
        [SerializeField] private EnemyCombatReaction reaction = EnemyCombatReaction.Defense;

        /// <summary>判断黑板中是否存在指定类型的待处理战斗反应。</summary>
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            return EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                && controller.Blackboard.PendingCombatReaction == reaction;
        }

    }
}
