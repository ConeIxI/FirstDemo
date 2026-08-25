using Game.Character.Enemy.Components;
using Game.Character.Enemy.AI.Combat;
using UnityEngine;

namespace Game.Character.Enemy.Core
{
    public sealed class EnemyStateContext
    {
        public Component Agent { get; }
        public EnemyBlackboard Blackboard { get; }
        public EnemyMovementComponent Movement { get; }
        public EnemyPerceptionComponent Perception { get; }
        public EnemyAnimationComponent Animation { get; }
        public EnemyCombatComponent Combat { get; }
        public EnemyLifeComponent Life { get; }
        public EnemyAttributeComponent Attribute { get; }
        public EnemyCombatDecisionController CombatDecision { get; }

        // 收拢行为树动作执行所需组件引用，避免动作节点到处 GetComponent。
        public EnemyStateContext(
            Component agent,
            EnemyBlackboard blackboard,
            EnemyMovementComponent movement,
            EnemyPerceptionComponent perception,
            EnemyAnimationComponent animation,
            EnemyCombatComponent combat,
            EnemyLifeComponent life,
            EnemyAttributeComponent attribute,
            EnemyCombatDecisionController combatDecision)
        {
            Agent = agent;
            Blackboard = blackboard;
            Movement = movement;
            Perception = perception;
            Animation = animation;
            Combat = combat;
            Life = life;
            Attribute = attribute;
            CombatDecision = combatDecision;
        }
    }
}
