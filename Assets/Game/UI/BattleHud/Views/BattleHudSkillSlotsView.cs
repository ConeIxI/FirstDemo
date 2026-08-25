using Game.Character.Common;
using Game.Character.Equipment;
using UnityEngine;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 战斗 HUD 右下角技能槽和武器槽视图，只负责使用 prefab 中已配置的槽位显示。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class BattleHudSkillSlotsView : MonoBehaviour
    {
        [SerializeField] private BattleHudSlotView weaponSlot;
        [SerializeField] private BattleHudSlotView[] skillSlots;

        private BagInventoryManager m_inventory;
        private bool m_inventorySubscribed;
        private EquipmentManager m_equipmentManager;
        private bool m_equipmentSubscribed;

        /// <summary>
        /// 初始化 prefab 中已配置好的技能槽和武器槽引用。
        /// </summary>
        public void Init()
        {
            InitSlots();
            EnsureInventory();
            SubscribeInventory();
            RefreshWeaponSlots();
        }

        /// <summary>
        /// 显示技能槽视图。
        /// </summary>
        public void Show()
        {
            gameObject.SetActive(true);
            EnsureInventory();
            SubscribeInventory();
            SubscribeEquipment();
            RefreshWeaponSlots();
        }

        /// <summary>
        /// 隐藏技能槽视图。
        /// </summary>
        public void Hide()
        {
            UnsubscribeInventory();
            UnsubscribeEquipment();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 释放技能槽视图持有的装备事件订阅。
        /// </summary>
        public void Dispose()
        {
            UnsubscribeInventory();
            UnsubscribeEquipment();
            m_inventory = null;
            m_equipmentManager = null;
        }

        /// <summary>
        /// 绑定玩家装备管理器，并立即显示当前激活武器图标。
        /// </summary>
        public void BindEquipmentManager(EquipmentManager equipmentManager)
        {
            UnsubscribeEquipment();
            m_equipmentManager = equipmentManager;
            EnsureInventory();
            SubscribeInventory();
            SubscribeEquipment();
            RefreshWeaponSlots();
        }

        /// <summary>
        /// 外部恢复装备数据后主动刷新当前武器和技能图标。
        /// </summary>
        public void RefreshCurrentWeapon()
        {
            EnsureInventory();
            RefreshWeaponSlots();
        }

        /// <summary>
        /// 设置指定技能槽标签。
        /// </summary>
        public void SetSkillLabel(int index, string label)
        {
            if (skillSlots == null || index < 0 || index >= skillSlots.Length || skillSlots[index] == null)
            {
                return;
            }

            skillSlots[index].SetLabel(label);
        }

        /// <summary>
        /// 初始化已绑定的技能槽和武器槽，槽位数量和布局完全由 prefab 决定。
        /// </summary>
        private void InitSlots()
        {
            if (weaponSlot != null)
            {
                weaponSlot.Init();
            }

            if (skillSlots == null)
            {
                return;
            }

            for (int i = 0; i < skillSlots.Length; i++)
            {
                BattleHudSlotView slot = skillSlots[i];
                if (slot != null)
                {
                    slot.Init();
                }
            }
        }

        /// <summary>
        /// 获取背包兼容门面，保证 HUD 从同一份装备槽数据读取图标。
        /// </summary>
        private void EnsureInventory()
        {
            if (m_inventory != null)
            {
                return;
            }

            m_inventory = BagInventoryManager.Instance;
        }

        /// <summary>
        /// 订阅背包数据变化，装备槽恢复或换装后同步刷新武器和技能图标。
        /// </summary>
        private void SubscribeInventory()
        {
            if (m_inventorySubscribed || m_inventory == null)
            {
                return;
            }

            m_inventory.InventoryChanged += OnInventoryChanged;
            m_inventorySubscribed = true;
        }

        /// <summary>
        /// 解除背包数据变化订阅。
        /// </summary>
        private void UnsubscribeInventory()
        {
            if (!m_inventorySubscribed || m_inventory == null)
            {
                return;
            }

            m_inventory.InventoryChanged -= OnInventoryChanged;
            m_inventorySubscribed = false;
        }

        /// <summary>
        /// 背包装备槽变化后刷新当前 HUD 武器和技能图标。
        /// </summary>
        private void OnInventoryChanged()
        {
            RefreshWeaponSlots();
        }

        /// <summary>
        /// 订阅当前激活武器变化，避免普通装备槽变化误刷新 HUD。
        /// </summary>
        private void SubscribeEquipment()
        {
            if (m_equipmentSubscribed || m_equipmentManager == null)
            {
                return;
            }

            m_equipmentManager.ActiveWeaponChanged += OnActiveWeaponChanged;
            m_equipmentSubscribed = true;
        }

        /// <summary>
        /// 解除当前激活武器变化订阅。
        /// </summary>
        private void UnsubscribeEquipment()
        {
            if (!m_equipmentSubscribed)
            {
                return;
            }

            if (m_equipmentManager != null)
            {
                m_equipmentManager.ActiveWeaponChanged -= OnActiveWeaponChanged;
            }

            m_equipmentSubscribed = false;
        }

        /// <summary>
        /// 当前激活武器变化后刷新武器槽图标。
        /// </summary>
        private void OnActiveWeaponChanged(int slotIndex, WeaponData weapon, GameObject _)
        {
            RefreshWeaponSlots(slotIndex, weapon);
        }

        /// <summary>
        /// 读取装备管理器当前状态并同步刷新武器与技能槽图标。
        /// </summary>
        private void RefreshWeaponSlots()
        {
            EnsureInventory();
            int slotIndex = m_equipmentManager == null ? -1 : m_equipmentManager.ActiveWeaponIndex;
            WeaponData weapon = m_equipmentManager == null ? null : m_equipmentManager.ActiveWeapon;
            RefreshWeaponSlots(slotIndex, weapon);
        }

        /// <summary>
        /// 根据当前武器槽索引读取背包物品，并同步写入武器与技能槽图标。
        /// </summary>
        private void RefreshWeaponSlots(int slotIndex, WeaponData weapon)
        {
            BagItemData item = weapon == null || slotIndex < 0
                ? null
                : m_inventory.GetItem(BagSlotType.Weapon, slotIndex);
            if (weaponSlot != null)
            {
                weaponSlot.SetIcon(item == null ? null : item.Icon);
            }

            RefreshSkillIcons(item);
        }

        /// <summary>
        /// 将当前武器技能图标按索引写入对应技能槽，缺失图标的槽位清空显示。
        /// </summary>
        private void RefreshSkillIcons(BagItemData item)
        {
            for (int i = 0; i < skillSlots.Length; i++)
            {
                BattleHudSlotView slot = skillSlots[i];
                if (slot != null)
                {
                    Sprite icon = item == null || item.SkillIcons == null || i >= item.SkillIcons.Length
                        ? null
                        : item.SkillIcons[i];
                    slot.SetIcon(icon);
                }
            }
        }
    }
}
