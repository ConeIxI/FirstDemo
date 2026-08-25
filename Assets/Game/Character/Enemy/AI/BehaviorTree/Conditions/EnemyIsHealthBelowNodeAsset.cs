using Game.Character.Enemy.Components;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Is Health Below")]
    public sealed class EnemyIsHealthBelowNodeAsset : ConditionNodeAsset
    {
        [SerializeField] private int healthThreshold;

        /// <summary>根据统一敌人属性组件的当前生命值判断是否低于配置阈值。</summary>
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            if (context == null || context.Owner == null)
            {
                return false;
            }

            EnemyAttributeComponent attribute = context.Owner.GetComponent<EnemyAttributeComponent>();
            return attribute != null && attribute.Health <= healthThreshold;
        }

    }
}
