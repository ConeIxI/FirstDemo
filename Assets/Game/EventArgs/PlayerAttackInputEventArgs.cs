using GameMain2.Framework.Core;
using UnityEngine;

namespace GameMain2.Game.EventArgs
{
    public sealed class PlayerAttackInputEventArgs : EventArgsBase
    {
        public static readonly int EventId = typeof(PlayerAttackInputEventArgs).GetHashCode();

        public override int Id => EventId;
        public Transform Player { get; }
        public float DefaultAttackRange { get; }

        /// <summary>创建一次玩家默认攻击按键事件。</summary>
        public PlayerAttackInputEventArgs(Transform player, float defaultAttackRange)
        {
            Player = player;
            DefaultAttackRange = defaultAttackRange;
        }
    }
}
