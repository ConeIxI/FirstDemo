using System;
using Game.Character.Common;
using GameMain2.Framework.Core;
using GameMain2.Scripts.Character;
using UnityEngine;

namespace Game.Character.Equipment
{
    public class EquipmentManager : MonoBehaviour
    {
        private const int WeaponSlotCount = 2;

        public event Action<EquipmentType, string> EquipmentChanged;
        public event Action<int, string> WeaponChanged;
        public event Action<int, WeaponData, GameObject> ActiveWeaponChanged;

        private readonly WeaponData[] m_weapons = new WeaponData[WeaponSlotCount];
        private readonly GameObject[] m_weaponModels = new GameObject[WeaponSlotCount];
        private readonly string[] m_weaponObjectNames = new string[WeaponSlotCount];
        private readonly int[] m_weaponAttackBonuses = new int[WeaponSlotCount];
        private readonly string[] m_equipmentObjectNames = new string[4];
        private readonly int[] m_equipmentDefenseBonuses = new int[4];

        private int m_currentWeaponIndex = -1;
        private PlayerEquipmentAppearance m_appearance;

        public int ActiveWeaponIndex => m_currentWeaponIndex;
        public bool CanSwitchWeapon => CountEquippedWeapons() > 1;

        public WeaponData ActiveWeapon => IsValidWeaponSlot(m_currentWeaponIndex)
            ? m_weapons[m_currentWeaponIndex]
            : null;

        public GameObject ActiveWeaponModel => IsValidWeaponSlot(m_currentWeaponIndex)
            ? m_weaponModels[m_currentWeaponIndex]
            : null;

        /// <summary>初始化装备外观并发布初始装备属性快照。</summary>
        private void Awake()
        {
            EnsureAppearance();
            ApplyActiveWeaponData();
        }

        /// <summary>读取指定武器槽的武器数据。</summary>
        public WeaponData GetWeapon(int slotIndex)
        {
            return IsValidWeaponSlot(slotIndex) ? m_weapons[slotIndex] : null;
        }

        /// <summary>设置武器槽的模型和攻击力，并按非战斗姿态刷新收纳表现。</summary>
        public string SetWeaponObject(int slotIndex, string objectName, int attackBonus)
        {
            EnsureAppearance();
            if (!IsValidWeaponSlot(slotIndex))
            {
                Debug.LogWarning($"武器槽索引无效：{slotIndex}");
                return null;
            }

            string activeObjectName = m_appearance == null
                ? objectName
                : m_appearance.SetWeaponObject(slotIndex, objectName);

            m_weaponObjectNames[slotIndex] = activeObjectName;
            m_weaponAttackBonuses[slotIndex] = activeObjectName == null ? 0 : attackBonus;
            m_weapons[slotIndex] = m_appearance == null ? null : m_appearance.GetWeaponData(slotIndex);
            m_weaponModels[slotIndex] = m_appearance == null ? null : m_appearance.GetWeaponGameObject(slotIndex);
            WeaponChanged?.Invoke(slotIndex, activeObjectName);

            if (!IsValidWeaponSlot(m_currentWeaponIndex) || m_weapons[m_currentWeaponIndex] == null)
            {
                m_currentWeaponIndex = m_weapons[slotIndex] == null ? FindFirstEquippedWeaponSlot() : slotIndex;
                ApplyActiveWeaponData();
                ApplyWeaponAppearance(false);
                return activeObjectName;
            }

            if (m_currentWeaponIndex == slotIndex)
            {
                ApplyActiveWeaponData();
                ApplyWeaponAppearance(false);
                return activeObjectName;
            }

            ApplyWeaponAppearance(false);
            PublishEquipmentAttributeSnapshot();
            return activeObjectName;
        }

        /// <summary>清空武器槽的模型和攻击力，并隐藏该槽武器表现。</summary>
        public void ClearWeaponSlot(int slotIndex)
        {
            EnsureAppearance();
            if (!IsValidWeaponSlot(slotIndex))
            {
                Debug.LogWarning($"武器槽索引无效：{slotIndex}");
                return;
            }

            if (m_appearance != null)
            {
                m_appearance.ClearWeaponObject(slotIndex);
            }

            bool wasActiveSlot = m_currentWeaponIndex == slotIndex;
            ClearWeaponRuntimeSlot(slotIndex);
            WeaponChanged?.Invoke(slotIndex, null);

            if (wasActiveSlot)
            {
                m_currentWeaponIndex = FindFirstEquippedWeaponSlot();
                ApplyActiveWeaponData();
                ApplyWeaponAppearance(false);
                return;
            }

            ApplyWeaponAppearance(false);
            PublishEquipmentAttributeSnapshot();
        }

        /// <summary>旧的动态实例化入口已停用，武器由背包装备槽通过已有模型对象接入。</summary>
        public void InitWeapons()
        {
        }

        /// <summary>切换到下一个已装备武器槽，只同步当前武器数据。</summary>
        public bool SwitchWeapon()
        {
            if (!CanSwitchWeapon)
            {
                return false;
            }

            int nextIndex = GetNextEquippedWeaponIndex();
            if (!IsValidWeaponSlot(nextIndex) || nextIndex == m_currentWeaponIndex)
            {
                return false;
            }

            return ActivateWeapon(nextIndex);
        }

        /// <summary>兼容旧入口，只切换当前武器数据，不直接改变模型姿态。</summary>
        public bool SetActiveWeaponIndex(int targetIndex)
        {
            return ActivateWeapon(targetIndex);
        }

        /// <summary>返回当前槽之后的下一个已装备武器槽，不修改任何状态。</summary>
        public int GetNextEquippedWeaponIndex()
        {
            return FindNextEquippedWeaponSlot();
        }

        /// <summary>只切换当前武器数据并同步命中检测器、技能、动画覆盖、事件和属性。</summary>
        public bool ActivateWeapon(int targetIndex)
        {
            EnsureAppearance();
            if (targetIndex < 0)
            {
                m_currentWeaponIndex = -1;
                ApplyActiveWeaponData();
                return true;
            }

            if (!IsValidWeaponSlot(targetIndex))
            {
                Debug.LogWarning($"武器槽索引无效：{targetIndex}");
                return false;
            }

            if (m_weapons[targetIndex] == null)
            {
                return false;
            }

            m_currentWeaponIndex = targetIndex;
            ApplyActiveWeaponData();
            return true;
        }

        /// <summary>根据稳定战斗姿态刷新两个武器槽的手持或收纳表现。</summary>
        public void ApplyWeaponAppearance(bool isCombat)
        {
            EnsureAppearance();
            if (m_appearance != null)
            {
                m_appearance.ApplyCombatAppearance(m_currentWeaponIndex, isCombat);
            }
        }

        /// <summary>把指定槽位当前武器显示在手中。</summary>
        public void ShowWeaponInHand(int slotIndex)
        {
            EnsureAppearance();
            m_appearance?.ShowWeaponInHand(slotIndex);
        }

        /// <summary>把指定槽位当前武器显示在收纳位置。</summary>
        public void ShowWeaponSheathed(int slotIndex)
        {
            EnsureAppearance();
            m_appearance?.ShowWeaponSheathed(slotIndex);
        }

        /// <summary>设置防具槽的模型和防御力，并发布完整装备属性。</summary>
        public void SetEquipmentObject(EquipmentType type, string objectName, int defenseBonus)
        {
            EnsureAppearance();
            string activeObjectName = m_appearance == null
                ? objectName
                : m_appearance.SetEquipmentObject(type, objectName);
            SetEquipmentState(type, activeObjectName, defenseBonus);
        }

        /// <summary>清空防具槽的属性，并保留外观系统定义的默认模型。</summary>
        public void ClearEquipment(EquipmentType type)
        {
            EnsureAppearance();
            string activeObjectName = m_appearance == null ? null : m_appearance.ClearEquipment(type);
            SetEquipmentState(type, activeObjectName, 0);
        }

        /// <summary>读取指定防具类型当前对象名。</summary>
        public string GetEquipmentObjectName(EquipmentType type)
        {
            int index = (int)type;
            if (index < 0 || index >= m_equipmentObjectNames.Length)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(m_equipmentObjectNames[index]))
            {
                return m_equipmentObjectNames[index];
            }

            EnsureAppearance();
            return m_appearance == null ? null : m_appearance.GetEquipmentObjectName(type);
        }

        /// <summary>应用当前武器的数据、技能和动画，不改变武器手持或收纳表现。</summary>
        private void ApplyActiveWeaponData()
        {
            EnsureAppearance();

            WeaponData activeWeapon = ActiveWeapon;
            GameObject activeModel = ActiveWeaponModel;
            WeaponHandler handler = GetWeaponHandler();
            PlayerSkillManager skillManager = GetSkillManager();
            PlayerStateMachine stateMachine = GetStateMachine();

            if (activeWeapon == null || activeModel == null)
            {
                if (handler != null)
                {
                    handler.RemoveWeapon();
                }

                if (skillManager != null)
                {
                    skillManager.ClearSkills();
                }

                if (stateMachine != null)
                {
                    stateMachine.ResetAnimatorController();
                    stateMachine.ForceExitCombatIfNoWeapon();
                }

                ActiveWeaponChanged?.Invoke(-1, null, null);
                PublishEquipmentAttributeSnapshot();
                return;
            }

            if (handler != null)
            {
                handler.ApplyWeapon(activeWeapon);
            }

            if (skillManager != null)
            {
                skillManager.LoadSkillsForWeapon(activeWeapon);
            }

            if (stateMachine != null)
            {
                stateMachine.SwitchAnimatorController(activeWeapon);
            }

            ActiveWeaponChanged?.Invoke(m_currentWeaponIndex, activeWeapon, activeModel);
            PublishEquipmentAttributeSnapshot();
        }

        /// <summary>写入单个防具槽状态并发布完整装备属性快照。</summary>
        private void SetEquipmentState(EquipmentType type, string objectName, int defenseBonus)
        {
            int index = (int)type;
            if (index < 0 || index >= m_equipmentObjectNames.Length)
            {
                return;
            }

            if (m_equipmentObjectNames[index] == objectName
                && m_equipmentDefenseBonuses[index] == defenseBonus)
            {
                return;
            }

            m_equipmentObjectNames[index] = objectName;
            m_equipmentDefenseBonuses[index] = defenseBonus;
            EquipmentChanged?.Invoke(type, objectName);
            PublishEquipmentAttributeSnapshot();
        }

        /// <summary>确保玩家身上存在装备外观组件并完成初始化。</summary>
        private void EnsureAppearance()
        {
            if (m_appearance != null)
            {
                return;
            }

            m_appearance = GetComponent<PlayerEquipmentAppearance>();
            if (m_appearance == null)
            {
                m_appearance = gameObject.AddComponent<PlayerEquipmentAppearance>();
            }

            m_appearance.Initialize();
        }

        /// <summary>读取玩家武器命中处理器。</summary>
        private WeaponHandler GetWeaponHandler()
        {
            WeaponHandler handler = GetComponent<WeaponHandler>();
            if (handler != null)
            {
                return handler;
            }

            PlayerController playerController = GetComponent<PlayerController>();
            return playerController == null ? null : playerController.WeaponHandler;
        }

        /// <summary>读取玩家技能管理器。</summary>
        private PlayerSkillManager GetSkillManager()
        {
            PlayerSkillManager skillManager = GetComponent<PlayerSkillManager>();
            if (skillManager != null)
            {
                return skillManager;
            }

            PlayerController playerController = GetComponent<PlayerController>();
            return playerController == null ? null : playerController.SkillManager;
        }

        /// <summary>读取玩家状态机，用于同步武器动画覆盖。</summary>
        private PlayerStateMachine GetStateMachine()
        {
            return GetComponentInChildren<PlayerStateMachine>(true);
        }

        /// <summary>查找当前槽之后的下一个已装备武器槽。</summary>
        private int FindNextEquippedWeaponSlot()
        {
            if (CountEquippedWeapons() == 0)
            {
                return -1;
            }

            int startIndex = IsValidWeaponSlot(m_currentWeaponIndex) ? m_currentWeaponIndex : -1;
            for (int offset = 1; offset <= WeaponSlotCount; offset++)
            {
                int index = (startIndex + offset) % WeaponSlotCount;
                if (m_weapons[index] != null)
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>查找第一个已装备武器槽。</summary>
        private int FindFirstEquippedWeaponSlot()
        {
            for (int i = 0; i < WeaponSlotCount; i++)
            {
                if (m_weapons[i] != null)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>统计当前已装备武器槽数量。</summary>
        private int CountEquippedWeapons()
        {
            int count = 0;
            for (int i = 0; i < WeaponSlotCount; i++)
            {
                if (m_weapons[i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>清除指定武器槽的运行时对象和攻击力。</summary>
        private void ClearWeaponRuntimeSlot(int slotIndex)
        {
            if (!IsValidWeaponSlot(slotIndex))
            {
                return;
            }

            m_weapons[slotIndex] = null;
            m_weaponModels[slotIndex] = null;
            m_weaponObjectNames[slotIndex] = null;
            m_weaponAttackBonuses[slotIndex] = 0;
        }

        /// <summary>读取当前激活武器槽提供的攻击力。</summary>
        private int GetActiveWeaponAttackBonus()
        {
            return IsValidWeaponSlot(m_currentWeaponIndex)
                ? m_weaponAttackBonuses[m_currentWeaponIndex]
                : 0;
        }

        /// <summary>累加全部防具槽提供的防御力。</summary>
        private int GetTotalDefenseBonus()
        {
            int totalDefenseBonus = 0;
            for (int i = 0; i < m_equipmentDefenseBonuses.Length; i++)
            {
                totalDefenseBonus += m_equipmentDefenseBonuses[i];
            }

            return totalDefenseBonus;
        }

        /// <summary>通过事件中心发布当前对象的完整装备攻防快照。</summary>
        private void PublishEquipmentAttributeSnapshot()
        {
            EventCenter.Instance.Fire(
                this,
                new EquipmentAttributeChangedEventArgs(
                    gameObject,
                    GetActiveWeaponAttackBonus(),
                    GetTotalDefenseBonus()));
        }

        /// <summary>判断武器槽索引是否处于玩家双武器槽范围内。</summary>
        private static bool IsValidWeaponSlot(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < WeaponSlotCount;
        }
    }
}
