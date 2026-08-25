using System;
using Game.Battle.Buff;
using Game.Character.Equipment;
using GameMain2.Framework.Core;
using UnityEngine;

namespace Game.Battle.Ability
{
    public sealed class CombatAttributeSet : MonoBehaviour, ICombatAttributes, ICombatResource
    {
        [SerializeField] private int maxHealth = 100;
        [SerializeField] private int maxStability = 100;
        [SerializeField] private int attack = 10;
        [SerializeField] private int defense;
        [SerializeField] private int equipmentAttackBonus;
        [SerializeField] private int equipmentDefenseBonus;
        [SerializeField] private int maxBattleSpirit = 100;

        public int Health { get; private set; }
        public int MaxHealth => maxHealth;
        public int Stability { get; private set; }
        public int MaxStability => maxStability;
        public int Attack => GetModifiedAttack();
        public int Defense => GetModifiedDefense();
        public int EquipmentAttackBonus => equipmentAttackBonus;
        public int EquipmentDefenseBonus => equipmentDefenseBonus;
        public int BattleSpirit { get; private set; }
        public int MaxBattleSpirit => maxBattleSpirit;
        public bool IsDead => Health <= 0;
        public bool IsUnbalanced => Stability <= 0 && !IsDead;
        public event Action<CombatAttributeChanged> AttributeChanged;
        private CombatBuffController m_buffController;

        /// <summary>启用时监听属于当前对象的装备属性快照。</summary>
        private void OnEnable()
        {
            EventCenter.Instance.Subscribe(
                EquipmentAttributeChangedEventArgs.EventId,
                OnEquipmentAttributeChanged);
        }

        /// <summary>禁用时解除装备属性事件监听。</summary>
        private void OnDisable()
        {
            EventCenter.TryUnSubscribe(
                EquipmentAttributeChangedEventArgs.EventId,
                OnEquipmentAttributeChanged);
        }

        /// <summary>初始化生命、稳定值和战意。</summary>
        private void Start()
        {
            SetHealth(maxHealth);
            SetStability(maxStability);
            SetBattleSpirit(maxBattleSpirit);
        }

        /// <summary>接收属于当前对象的完整装备属性快照。</summary>
        private void OnEquipmentAttributeChanged(object sender, EventArgsBase eventArgs)
        {
            if (eventArgs is not EquipmentAttributeChangedEventArgs args || args.Target != gameObject)
            {
                return;
            }

            SetEquipmentAttributeBonus(args.AttackBonus, args.DefenseBonus);
        }

        /// <summary>获取 Buff 修正后的战斗对象攻击力。</summary>
        private int GetModifiedAttack()
        {
            int equippedAttack = attack + equipmentAttackBonus;
            CombatBuffController buffController = GetBuffController();
            return buffController != null ? buffController.CalculateAttack(equippedAttack) : equippedAttack;
        }

        /// <summary>获取 Buff 修正后的战斗对象防御力。</summary>
        private int GetModifiedDefense()
        {
            int equippedDefense = defense + equipmentDefenseBonus;
            CombatBuffController buffController = GetBuffController();
            return buffController != null ? buffController.CalculateDefense(equippedDefense) : equippedDefense;
        }

        /// <summary>写入装备提供的攻防加成，并在最终属性变化时发布事件。</summary>
        public void SetEquipmentAttributeBonus(int attackBonus, int defenseBonus)
        {
            equipmentAttackBonus = attackBonus;
            equipmentDefenseBonus = defenseBonus;
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
            SetHealth(Mathf.Clamp(Health - applied, 0, maxHealth));
            return applied;
        }

        /// <summary>恢复生命并返回实际恢复量。</summary>
        public int RestoreHealth(int value)
        {
            int restored = Mathf.Clamp(value, 0, maxHealth - Health);
            SetHealth(Mathf.Clamp(Health + restored, 0, maxHealth));
            return restored;
        }

        /// <summary>扣除稳定并返回实际扣除量。</summary>
        public int ApplyStabilityDamage(int value)
        {
            int applied = Mathf.Clamp(value, 0, Stability);
            SetStability(Mathf.Clamp(Stability - applied, 0, maxStability));
            return applied;
        }

        /// <summary>恢复稳定并返回实际恢复量。</summary>
        public int RestoreStability(int value)
        {
            int restored = Mathf.Clamp(value, 0, maxStability - Stability);
            SetStability(Mathf.Clamp(Stability + restored, 0, maxStability));
            return restored;
        }

        /// <summary>战意足够时完整扣除指定数值。</summary>
        public bool TryConsumeBattleSpirit(int value)
        {
            int requested = Mathf.Clamp(value, 0, int.MaxValue);
            if (requested > BattleSpirit)
            {
                return false;
            }

            SetBattleSpirit(Mathf.Clamp(BattleSpirit - requested, 0, maxBattleSpirit));
            return true;
        }

        /// <summary>增加战意并返回实际增加量。</summary>
        public int AddBattleSpirit(int value)
        {
            int added = Mathf.Clamp(value, 0, maxBattleSpirit - BattleSpirit);
            SetBattleSpirit(Mathf.Clamp(BattleSpirit + added, 0, maxBattleSpirit));
            return added;
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
            AttributeChanged?.Invoke(new CombatAttributeChanged(CombatAttributeType.Health, Health, maxHealth, delta));
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
            AttributeChanged?.Invoke(new CombatAttributeChanged(CombatAttributeType.Stability, Stability, maxStability, delta));
        }

        /// <summary>写入战意并在数值改变时发布事件。</summary>
        private void SetBattleSpirit(int value)
        {
            int delta = value - BattleSpirit;
            if (delta == 0)
            {
                return;
            }

            BattleSpirit = value;
            AttributeChanged?.Invoke(new CombatAttributeChanged(CombatAttributeType.BattleSpirit, BattleSpirit, maxBattleSpirit, delta));
        }

    }
}
