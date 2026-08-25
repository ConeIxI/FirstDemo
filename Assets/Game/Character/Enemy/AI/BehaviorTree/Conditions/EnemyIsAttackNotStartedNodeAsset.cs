using Game.Character.Enemy.AI.Combat;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Is Attack Not Started")]
    public sealed class EnemyIsAttackNotStartedNodeAsset : ConditionNodeAsset
    {
        /// <summary>判断攻击意图是否尚未进入起手、进行或收尾阶段，防止追击分支抢占已开始的攻击动作。</summary>
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            return EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                && controller.Blackboard.AttackPhase == EnemyAttackPhase.None;
        }
    }
}
