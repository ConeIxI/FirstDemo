using System;
using UnityEngine;

namespace Game.Battle.Ability
{
    public readonly struct CombatAttributeChanged
    {
        /// <summary>创建一次战斗属性变化记录。</summary>
        public CombatAttributeChanged(CombatAttributeType type, int current, int max, int delta)
        {
            Type = type;
            Current = current;
            Max = max;
            Delta = delta;
        }

        public CombatAttributeType Type { get; }
        public int Current { get; }
        public int Max { get; }
        public int Delta { get; }
    }

    public interface ICombatAttributes
    {
        int Health { get; }
        int MaxHealth { get; }
        int Stability { get; }
        int MaxStability { get; }
        int Attack { get; }
        int Defense { get; }
        bool IsDead { get; }
        bool IsUnbalanced { get; }
        event Action<CombatAttributeChanged> AttributeChanged;

        /// <summary>扣除生命并返回实际扣除量。</summary>
        int ApplyHealthDamage(int value);

        /// <summary>恢复生命并返回实际恢复量。</summary>
        int RestoreHealth(int value);

        /// <summary>扣除稳定并返回实际扣除量。</summary>
        int ApplyStabilityDamage(int value);

        /// <summary>恢复稳定并返回实际恢复量。</summary>
        int RestoreStability(int value);
    }

    public interface ICombatResource
    {
        int BattleSpirit { get; }
        int MaxBattleSpirit { get; }

        /// <summary>战意足够时完整扣除指定数值。</summary>
        bool TryConsumeBattleSpirit(int value);

        /// <summary>增加战意并返回实际增加量。</summary>
        int AddBattleSpirit(int value);
    }

    public interface ICombatMotion
    {
        /// <summary>向战斗对象施加一次外部位移。</summary>
        void ApplyExternalDisplacement(Vector3 offset);
    }
}
