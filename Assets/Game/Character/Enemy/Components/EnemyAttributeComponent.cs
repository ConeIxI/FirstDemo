using System;
using Game.Battle.Ability;
using Game.Battle.Buff;
using Game.Character.Enemy.Config;
using UnityEngine;

namespace Game.Character.Enemy.Components
{
    public sealed class EnemyAttributeComponent : MonoBehaviour, ICombatAttributes
    {
        public int Health { get; private set; }
        public int MaxHealth { get; private set; }
        public int Stability { get; private set; }
        public int MaxStability { get; private set; }
        public int Attack => GetModifiedAttack();
        public int Defense => GetModifiedDefense();
        public float Perception { get; private set; }
        public float Movement { get; private set; }
        public bool IsDead => Health <= 0;
        public bool IsUnbalanced => Stability <= 0 && !IsDead;
        public event Action<CombatAttributeChanged> AttributeChanged;
        private int BaseAttack { get; set; }
        private int BaseDefense { get; set; }
        private CombatBuffController m_buffController;

        /// <summary>按敌人定义中的属性配置 Id 加载运行时只读属性。</summary>
        public void LoadFromDefinition(EnemyDefinition definition)
        {
            EnemyAttributeConfig config = definition.AttributeConfig;
            MaxHealth = config.maxHealth;
            Health = MaxHealth;
            MaxStability = config.maxStability;
            Stability = MaxStability;
            BaseAttack = config.attack;
            BaseDefense = config.defense;
            Perception = config.perceptionMultiplier;
            Movement = config.moveSpeedMultiplier;
        }
        /// <summary>获取 Buff 修正后的敌人攻击力。</summary>
        private int GetModifiedAttack()
        {
            CombatBuffController buffController = GetBuffController();
            return buffController != null ? buffController.CalculateAttack(BaseAttack) : BaseAttack;
        }

        /// <summary>获取 Buff 修正后的敌人防御力。</summary>
        private int GetModifiedDefense()
        {
            CombatBuffController buffController = GetBuffController();
            return buffController != null ? buffController.CalculateDefense(BaseDefense) : BaseDefense;
        }

        /// <summary>懒加载同对象上的 Buff 控制器。</summary>
        private CombatBuffController GetBuffController()
        {
            if (m_buffController == null)
            {
                TryGetComponent(out m_buffController);
            }

            return m_buffController;
        }

        /// <summary>扣除生命并返回实际扣除量。</summary>
        public int ApplyHealthDamage(int value)
        {
            int applied = Mathf.Clamp(value, 0, Health);
            SetHealth(Mathf.Clamp(Health - applied, 0, MaxHealth));
            return applied;
        }

        /// <summary>恢复生命并返回实际恢复量。</summary>
        public int RestoreHealth(int value)
        {
            int restored = Mathf.Clamp(value, 0, MaxHealth - Health);
            SetHealth(Mathf.Clamp(Health + restored, 0, MaxHealth));
            return restored;
        }

        /// <summary>扣除稳定并返回实际扣除量。</summary>
        public int ApplyStabilityDamage(int value)
        {
            int applied = Mathf.Clamp(value, 0, Stability);
            SetStability(Mathf.Clamp(Stability - applied, 0, MaxStability));
            return applied;
        }

        /// <summary>恢复稳定并返回实际恢复量。</summary>
        public int RestoreStability(int value)
        {
            int restored = Mathf.Clamp(value, 0, MaxStability - Stability);
            SetStability(Mathf.Clamp(Stability + restored, 0, MaxStability));
            return restored;
        }

        /// <summary>写入生命并在数值改变时发布事件。</summary>
        private void SetHealth(int value)
        {
            int delta = value - Health;
            if (delta == 0)
            {
                return;
            }

            Health = value;
            AttributeChanged?.Invoke(new CombatAttributeChanged(CombatAttributeType.Health, Health, MaxHealth, delta));
        }

        /// <summary>写入稳定并在数值改变时发布事件。</summary>
        private void SetStability(int value)
        {
            int delta = value - Stability;
            if (delta == 0)
            {
                return;
            }

            Stability = value;
            AttributeChanged?.Invoke(new CombatAttributeChanged(CombatAttributeType.Stability, Stability, MaxStability, delta));
        }
    }
}
