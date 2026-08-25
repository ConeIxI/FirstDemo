using GameMain2.Framework.Core;
using UnityEngine;

namespace Game.Character.Enemy.Events
{
    public sealed class EnemyCombatTargetChangedEventArgs : EventArgsBase
    {
        public static readonly int EventId = typeof(EnemyCombatTargetChangedEventArgs).GetHashCode();

        public override int Id => EventId;
        public Transform EnemyTransform { get; }
        public Transform PreviousTarget { get; }
        public Transform CurrentTarget { get; }
        public bool HasCurrentTarget => CurrentTarget != null;

        /// <summary>创建敌人战斗目标变更事件，记录敌人、旧目标和新目标。</summary>
        public EnemyCombatTargetChangedEventArgs(
            Transform enemyTransform,
            Transform previousTarget,
            Transform currentTarget)
        {
            EnemyTransform = enemyTransform;
            PreviousTarget = previousTarget;
            CurrentTarget = currentTarget;
        }

        /// <summary>生成带敌人 Transform 的事件副本，供 AIController 转发黑板事件。</summary>
        public EnemyCombatTargetChangedEventArgs WithEnemyTransform(Transform enemyTransform)
        {
            return new EnemyCombatTargetChangedEventArgs(enemyTransform, PreviousTarget, CurrentTarget);
        }
    }
}
