using Game.Character.Enemy.Config;
using GameMain2.Framework.Core;
using UnityEngine;

namespace Game.Character.Enemy.Events
{
    public sealed class EnemyDeadEventArgs : EventArgsBase
    {
        public static readonly int EventId = typeof(EnemyDeadEventArgs).GetHashCode();

        public readonly EnemyDefinition Definition;
        public readonly Transform EnemyTransform;
        public readonly Vector3 DeathPosition;

        public override int Id => EventId;

        /// <summary>创建敌人死亡事件，携带死亡位置和敌人配置。</summary>
        public EnemyDeadEventArgs(EnemyDefinition definition, Transform enemyTransform, Vector3 deathPosition)
        {
            Definition = definition;
            EnemyTransform = enemyTransform;
            DeathPosition = deathPosition;
        }
    }
}
