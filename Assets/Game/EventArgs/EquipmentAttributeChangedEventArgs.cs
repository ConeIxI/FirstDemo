using GameMain2.Framework.Core;
using UnityEngine;

namespace Game.Character.Equipment
{
    public sealed class EquipmentAttributeChangedEventArgs : EventArgsBase
    {
        public static readonly int EventId = typeof(EquipmentAttributeChangedEventArgs).GetHashCode();

        public override int Id => EventId;
        public GameObject Target { get; }
        public int AttackBonus { get; }
        public int DefenseBonus { get; }

        /// <summary>创建指定战斗对象的完整装备属性快照事件。</summary>
        public EquipmentAttributeChangedEventArgs(GameObject target, int attackBonus, int defenseBonus)
        {
            Target = target;
            AttackBonus = attackBonus;
            DefenseBonus = defenseBonus;
        }
    }
}
