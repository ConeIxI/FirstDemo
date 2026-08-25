using Game.Character.Enemy.AI.Combat;
using GameMain2.Framework.Core.BehaviorTree;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    public static class EnemyBehaviorTreeUtility
    {
        /// <summary>从行为树上下文持有者上获取敌人 AI 控制器。</summary>
        public static bool TryGetController(BehaviorTreeContext context, out AIController controller)
        {
            controller = null;
            if (context == null || context.Owner == null)
            {
                return false;
            }

            controller = context.Owner.GetComponent<AIController>();
            return controller != null;
        }

        /// <summary>把战斗决策器的当前状态同步到黑板，供条件节点和后续动作读取。</summary>
        public static void SyncCombatDecisionFacts(AIController controller)
        {
            if (controller.CombatDecision == null)
            {
                controller.Blackboard.SetCombatDecisionFacts(
                    EnemyCombatDecisionState.Confrontation,
                    EnemyAttackPhase.None,
                    EnemyCombatReaction.None);
                controller.Blackboard.ClearAttackPlanFacts();
                return;
            }

            controller.Blackboard.SetCombatDecisionFacts(
                controller.CombatDecision.State,
                controller.CombatDecision.AttackPhase,
                controller.CombatDecision.PendingReaction);

            EnemyAttackPlan plan = controller.CombatDecision.CurrentPlan;
            if (plan == null)
            {
                controller.Blackboard.ClearAttackPlanFacts();
                return;
            }

            controller.Blackboard.SetAttackPlanFacts(
                plan.Type,
                plan.PreparationMode,
                plan.CurrentAttack.SkillId,
                plan.CurrentAttack.AnimationName,
                plan.CurrentAttack.AttackRange,
                plan.ReleaseDistance);
        }

        /// <summary>判断当前战斗目标是否位于敌人前方半区。</summary>
        public static bool IsTargetInFront(AIController controller)
        {
            Vector3 direction = controller.Blackboard.CombatTarget.position - controller.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return true;
            }

            Vector3 forward = controller.transform.forward;
            forward.y = 0f;
            return Vector3.Dot(forward.normalized, direction.normalized) >= 0f;
        }
    }
}
