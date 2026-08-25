using System;
using System.Collections.Generic;
using Game.Character.Equipment;
using GameMain2.Scripts.Modules;
using GameMain2.Scripts.Modules.Bag;
using UnityEngine;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 背包旧入口兼容门面，保留原有 API 并把真实数据规则转发到 BagLogic。
    /// </summary>
    public sealed class BagInventoryManager : SingletonManager<BagInventoryManager>
    {
        private BagLogic m_bagLogic;
        private bool m_logicEventSubscribed;

        public event Action InventoryChanged;

        public int BagCapacity => EnsureBagLogic().BagCapacity;

        /// <summary>
        /// 返回当前背包是否已经完成本次运行的调试种子投放。
        /// </summary>
        public bool IsDebugSeeded => EnsureBagLogic().IsDebugSeeded;

        /// <summary>
        /// 运行时启动时创建兼容门面，确保旧代码访问 Instance 时仍然可用。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            _ = Instance;
        }

        /// <summary>
        /// 初始化兼容门面生命周期，并订阅功能层背包事件。
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            if (!IsSingletonInstance)
            {
                return;
            }

            EnsureBagLogic();
            SubscribeLogicEvent();
        }

        /// <summary>
        /// 销毁兼容门面时解除功能层背包事件订阅。
        /// </summary>
        protected override void OnDestroy()
        {
            UnsubscribeLogicEvent();
            base.OnDestroy();
        }

        /// <summary>
        /// 初始化背包容量和槽位数据，真实逻辑由 BagLogic 执行。
        /// </summary>
        public void Initialize(int bagCapacity)
        {
            EnsureBagLogic().Initialize(bagCapacity);
        }

        /// <summary>
        /// 标记当前运行已经完成调试种子投放。
        /// </summary>
        public void MarkDebugSeeded()
        {
            EnsureBagLogic().MarkDebugSeeded();
        }

        /// <summary>
        /// 添加拾取或奖励物品，真实逻辑由 BagLogic 执行。
        /// </summary>
        public bool TryAddItem(BagItemType itemType, int id, int count)
        {
            return EnsureBagLogic().TryAddItem(itemType, id, count);
        }

        /// <summary>
        /// 返回指定槽位类型的槽位数量。
        /// </summary>
        public int GetSlotCount(BagSlotType slotType)
        {
            return EnsureBagLogic().GetSlotCount(slotType);
        }

        /// <summary>
        /// 读取某一物品分类下的背包物品列表。
        /// </summary>
        public IReadOnlyList<BagItemData> GetItems(BagItemType itemType)
        {
            return EnsureBagLogic().GetItems(itemType);
        }

        /// <summary>
        /// 按分类和背包格索引读取指定物品。
        /// </summary>
        public BagItemData GetBagItem(BagItemType itemType, int bagIndex)
        {
            return EnsureBagLogic().GetBagItem(itemType, bagIndex);
        }

        /// <summary>
        /// 读取装备槽中的物品。
        /// </summary>
        public BagItemData GetItem(BagSlotType slotType, int index)
        {
            return EnsureBagLogic().GetItem(slotType, index);
        }

        /// <summary>
        /// 在当前分类页内移动或交换背包物品。
        /// </summary>
        public bool MoveBagItem(BagItemType pageType, int sourceIndex, int targetIndex)
        {
            return EnsureBagLogic().MoveBagItem(pageType, sourceIndex, targetIndex);
        }

        /// <summary>
        /// 把背包物品移动到装备槽。
        /// </summary>
        public bool MoveBagItemToEquipment(
            BagItemType pageType,
            int sourceIndex,
            BagSlotType targetType,
            int targetIndex)
        {
            return EnsureBagLogic().MoveBagItemToEquipment(pageType, sourceIndex, targetType, targetIndex);
        }

        /// <summary>
        /// 把装备槽物品移动到当前分类页的指定背包格。
        /// </summary>
        public bool MoveEquipmentToBagSlot(
            BagSlotType sourceType,
            int sourceIndex,
            BagItemType currentPageType,
            int targetIndex)
        {
            return EnsureBagLogic().MoveEquipmentToBagSlot(sourceType, sourceIndex, currentPageType, targetIndex);
        }

        /// <summary>
        /// 把装备槽物品放回其所属分类的第一个空背包格。
        /// </summary>
        public bool MoveEquipmentToFirstEmptyBagSlot(BagSlotType sourceType, int sourceIndex)
        {
            return EnsureBagLogic().MoveEquipmentToFirstEmptyBagSlot(sourceType, sourceIndex);
        }

        /// <summary>
        /// 在两个装备槽之间移动或交换物品。
        /// </summary>
        public bool MoveEquipmentSlot(
            BagSlotType sourceType,
            int sourceIndex,
            BagSlotType targetType,
            int targetIndex)
        {
            return EnsureBagLogic().MoveEquipmentSlot(sourceType, sourceIndex, targetType, targetIndex);
        }

        /// <summary>
        /// 消耗指定装备槽中的一个物品，当前仅用于玩家消耗品快捷槽。
        /// </summary>
        public bool TryConsumeEquipmentItem(BagSlotType slotType, int index)
        {
            return EnsureBagLogic().TryConsumeEquipmentItem(slotType, index);
        }

        /// <summary>
        /// 创建玩家死亡重开快照，只保留装备栏和消耗品快捷槽数据。
        /// </summary>
        public PlayerRestartSnapshot CreateRestartSnapshot(int activeWeaponIndex)
        {
            return EnsureBagLogic().CreateRestartSnapshot(activeWeaponIndex);
        }

        /// <summary>
        /// 把死亡重开快照恢复到背包装备栏和消耗品快捷槽。
        /// </summary>
        public void ApplyRestartSnapshot(PlayerRestartSnapshot snapshot)
        {
            EnsureBagLogic().ApplyRestartSnapshot(snapshot);
        }

        /// <summary>
        /// 根据当前背包装备栏刷新新玩家的装备外观、技能、属性和激活武器。
        /// </summary>
        public void ApplyEquipmentSlotsToPlayer(EquipmentManager equipmentManager, int activeWeaponIndex)
        {
            EnsureBagLogic().ApplyEquipmentSlotsToPlayer(equipmentManager, activeWeaponIndex);
        }

        /// <summary>返回主菜单时清空背包运行态，并重新绑定新的背包业务实例。</summary>
        public void ResetRuntimeStateForMainMenu()
        {
            UnsubscribeLogicEvent();
            ModulesManager.ResetRuntimeState();
            m_bagLogic = ModulesManager.Bag;
            SubscribeLogicEvent();
            if (InventoryChanged != null)
            {
                InventoryChanged.Invoke();
            }
        }

        /// <summary>
        /// 判断指定物品是否允许放入目标槽位。
        /// </summary>
        public bool CanPlace(BagSlotType targetType, BagItemData item)
        {
            return EnsureBagLogic().CanPlace(targetType, item);
        }

        /// <summary>
        /// 判断槽位类型是否属于装备栏。
        /// </summary>
        public static bool IsEquipmentSlot(BagSlotType slotType)
        {
            return slotType != BagSlotType.Bag;
        }

        /// <summary>
        /// 获取装备空槽位显示名称。
        /// </summary>
        public static string GetSlotDisplayName(BagSlotType slotType, int index)
        {
            switch (slotType)
            {
                case BagSlotType.Weapon:
                    return index == 0 ? "武器 1" : "武器 2";
                case BagSlotType.Helmet:
                    return "头盔";
                case BagSlotType.Armor:
                    return "胸甲";
                case BagSlotType.Leggings:
                    return "腿甲";
                case BagSlotType.Gloves:
                    return "臂铠";
                case BagSlotType.Consumable:
                    return $"消耗品 {index + 1}";
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// 确保功能层背包逻辑存在，并返回同一个长期 Logic 实例。
        /// </summary>
        private BagLogic EnsureBagLogic()
        {
            ModulesManager.Init();
            if (m_bagLogic == null)
            {
                m_bagLogic = ModulesManager.Bag;
            }

            return m_bagLogic;
        }

        /// <summary>
        /// 订阅功能层背包变更事件，并避免重复订阅。
        /// </summary>
        private void SubscribeLogicEvent()
        {
            if (m_logicEventSubscribed)
            {
                return;
            }

            EnsureBagLogic().InventoryChanged += OnBagLogicInventoryChanged;
            m_logicEventSubscribed = true;
        }

        /// <summary>
        /// 解除功能层背包变更事件订阅。
        /// </summary>
        private void UnsubscribeLogicEvent()
        {
            if (!m_logicEventSubscribed || m_bagLogic == null)
            {
                return;
            }

            m_bagLogic.InventoryChanged -= OnBagLogicInventoryChanged;
            m_logicEventSubscribed = false;
        }

        /// <summary>
        /// 把功能层背包变更事件转发给仍使用旧 Manager API 的 UI。
        /// </summary>
        private void OnBagLogicInventoryChanged()
        {
            if (InventoryChanged != null)
            {
                InventoryChanged.Invoke();
            }
        }
    }
}
