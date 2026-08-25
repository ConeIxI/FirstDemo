using Game.Battle.Ability;
using Game.Character.Enemy.AI;
using Game.Character.Enemy.Components;
using Game.Character.Enemy.Core;
using UnityEngine;

namespace Game.Character.Player.Execution
{
    public readonly struct ExecutionTarget
    {
        public readonly EnemyAgent Agent;
        public readonly Transform Root;
        public readonly Animator Animator;
        public readonly AIController AIController;
        public readonly EnemyMovementComponent Movement;
        public readonly EnemyCombatComponent Combat;
        public readonly EnemyAttributeComponent Attribute;
        public readonly CombatAbilitySystem AbilitySystem;

        /// <summary>保存一次处决目标所需的敌人运行时组件引用。</summary>
        public ExecutionTarget(
            EnemyAgent agent,
            Transform root,
            Animator animator,
            AIController aiController,
            EnemyMovementComponent movement,
            EnemyCombatComponent combat,
            EnemyAttributeComponent attribute,
            CombatAbilitySystem abilitySystem)
        {
            Agent = agent;
            Root = root;
            Animator = animator;
            AIController = aiController;
            Movement = movement;
            Combat = combat;
            Attribute = attribute;
            AbilitySystem = abilitySystem;
        }

        /// <summary>判断目标是否仍处于可处决的失衡、未死亡且 Humanoid 可播放状态。</summary>
        public bool IsValidUnbalancedTarget()
        {
            return Agent != null
                && Root != null
                && Animator != null
                && Animator.avatar != null
                && Animator.avatar.isHuman
                && Attribute != null
                && AbilitySystem != null
                && AIController != null
                && AIController.Blackboard != null
                && AIController.Blackboard.IsUnbalanced
                && AIController.Blackboard.IsInUnbalanceLoop
                && !Attribute.IsDead;
        }
    }
}
