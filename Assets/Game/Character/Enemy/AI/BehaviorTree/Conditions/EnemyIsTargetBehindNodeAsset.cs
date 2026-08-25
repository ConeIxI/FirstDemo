using Game.Character.Enemy.AI.Combat;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Is Target Behind")]
    public sealed class EnemyIsTargetBehindNodeAsset : ConditionNodeAsset
    {
        /// <summary>判断战斗目标是否位于敌人身后半区。</summary>
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            return EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                && controller.Blackboard.HasCombatTarget
                && controller.Blackboard.AttackPhase == EnemyAttackPhase.None
                && IsTargetBehind(controller.transform, controller.Blackboard.CombatTarget.position);
        }

        /// <summary>按水平朝向点积判断目标是否在角色背后。</summary>
        private static bool IsTargetBehind(Transform self, Vector3 targetPosition)
        {
            Vector3 direction = targetPosition - self.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            Vector3 forward = self.forward;
            forward.y = 0f;
            return Vector3.Dot(forward.normalized, direction.normalized) < 0f;
        }
    }
}
