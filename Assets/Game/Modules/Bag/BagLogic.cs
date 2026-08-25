using System;
using System.Collections.Generic;
using Game.Character.Common;
using Game.Character.Equipment;
using Game.World.Drop;
using GameMain2.Framework.Core;
using GameMain2.Scripts.UI;
using UnityEngine;

namespace GameMain2.Scripts.Modules.Bag
{
    /// <summary>
    /// 背包功能业务入口，负责物品存放、装备槽移动和拾取事件，不依赖具体 UI 面板。
    /// </summary>
    public sealed class BagLogic : BaseGameService
    {
        private const int DefaultBagCapacity = 30;
        private const int MergeExistingConsumableIndex = -2;

        private static readonly BagItemData[] EmptyItems = new BagItemData[0];

        private readonly BagModel m_model = new BagModel();
        private bool m_initialized;
        private bool m_debugSeeded;
        private bool m_pickupEventSubscribed;

        public event Action InventoryChanged;

        public int BagCapacity => m_model.BagCapacity;

        /// <summary>
        /// 返回当前长生命周期背包是否已经完成本次运行的调试种子投放。
        /// </summary>
        public bool IsDebugSeeded => m_debugSeeded;

        /// <summary>
        /// 初始化背包业务事件订阅，使 UI 未打开时也可以接收地面拾取。
        /// </summary>
        public override void Init()
        {
            SubscribePickupEvent();
        }

        /// <summary>
        /// 清理背包数据并解除拾取事件订阅。
        /// </summary>
        public override void ClearModel()
        {
            UnsubscribePickupEvent();
            m_model.Clear();
            m_initialized = false;
            m_debugSeeded = false;
        }

        /// <summary>
        /// 标记本次运行已经完成调试种子投放，返回主菜单重置背包时会清除该状态。
        /// </summary>
        public void MarkDebugSeeded()
        {
            m_debugSeeded = true;
        }

        /// <summary>
        /// 初始化背包分类数据和装备槽数据，重复初始化只调整容量和补齐结构，不清空已有物品。
        /// </summary>
        public void Initialize(int bagCapacity)
        {
            int resolvedCapacity = Mathf.Max(1, bagCapacity);
            m_model.BagCapacity = m_initialized ? Mathf.Max(m_model.BagCapacity, resolvedCapacity) : resolvedCapacity;
            EnsureBagCategory(BagItemType.Weapon);
            EnsureBagCategory(BagItemType.Helmet);
            EnsureBagCategory(BagItemType.Armor);
            EnsureBagCategory(BagItemType.Leggings);
            EnsureBagCategory(BagItemType.Gloves);
            EnsureBagCategory(BagItemType.Consumable);

            EnsureEquipmentSlot(BagSlotType.Weapon, 2);
            EnsureEquipmentSlot(BagSlotType.Helmet, 1);
            EnsureEquipmentSlot(BagSlotType.Armor, 1);
            EnsureEquipmentSlot(BagSlotType.Leggings, 1);
            EnsureEquipmentSlot(BagSlotType.Gloves, 1);
            EnsureEquipmentSlot(BagSlotType.Consumable, 4);

            m_initialized = true;
            NotifyInventoryChanged();
        }

        /// <summary>
        /// 把外部拾取到的物品放入背包；消耗品已有同 ID 堆叠时直接合并数量。
        /// </summary>
        public bool TryAddItem(BagItemType itemType, int id, int count)
        {
            if (itemType == BagItemType.None || id <= 0 || count <= 0)
            {
                return false;
            }

            return TryAddItems(new[] { new DropItemStack(itemType, id, count) });
        }

        /// <summary>
        /// 批量放入地面掉落物，消耗品优先合并已有堆叠，新增物品全部有空格时才写入。
        /// </summary>
        public bool TryAddItems(IReadOnlyList<DropItemStack> items)
        {
            EnsureInitializedForPickup();
            int[] bagIndexes = new int[items.Count];
            if (!TryResolveBatchBagIndexes(items, bagIndexes))
            {
                return false;
            }

            BagItemData[] changedItems = new BagItemData[items.Count];
            for (int i = 0; i < items.Count; i++)
            {
                DropItemStack item = items[i];
                BagItemData mergeTarget = FindPickupMergeTarget(item, changedItems, i);
                if (mergeTarget != null)
                {
                    mergeTarget.Count += item.Count;
                    changedItems[i] = mergeTarget;
                    continue;
                }

                BagItemData addedItem = BagItemFactory.Create(
                    item.ItemId,
                    item.ItemType,
                    bagIndexes[i],
                    item.Count,
                    item.Defense);
                AddBagItem(addedItem);
                changedItems[i] = addedItem;
            }

            for (int i = 0; i < changedItems.Length; i++)
            {
                BagPickupTipDispatcher.Show(changedItems[i], items[i].Count);
            }

            NotifyInventoryChanged();
            return true;
        }

        /// <summary>
        /// 返回指定槽位类型可显示的槽位数量。
        /// </summary>
        public int GetSlotCount(BagSlotType slotType)
        {
            if (slotType == BagSlotType.Bag)
            {
                return m_model.BagCapacity;
            }

            return m_model.EquipmentSlots.TryGetValue(slotType, out BagItemData[] items) ? items.Length : 0;
        }

        /// <summary>
        /// 读取某一物品分类下的背包物品列表。
        /// </summary>
        public IReadOnlyList<BagItemData> GetItems(BagItemType itemType)
        {
            return m_model.BagItemsByType.TryGetValue(itemType, out List<BagItemData> items)
                ? items
                : EmptyItems;
        }

        /// <summary>
        /// 按分类和背包格索引读取指定背包物品。
        /// </summary>
        public BagItemData GetBagItem(BagItemType itemType, int bagIndex)
        {
            if (!IsValidBagIndex(bagIndex)
                || !m_model.BagItemsByType.TryGetValue(itemType, out List<BagItemData> items))
            {
                return null;
            }

            for (int i = 0; i < items.Count; i++)
            {
                BagItemData item = items[i];
                if (item != null && item.BagIndex == bagIndex)
                {
                    return item;
                }
            }

            return null;
        }

        /// <summary>
        /// 读取装备槽中的物品，普通背包格由 GetBagItem 按当前分类读取。
        /// </summary>
        public BagItemData GetItem(BagSlotType slotType, int index)
        {
            if (slotType == BagSlotType.Bag)
            {
                return null;
            }

            if (!TryGetEquipmentItems(slotType, out BagItemData[] items) || index < 0 || index >= items.Length)
            {
                return null;
            }

            return items[index];
        }

        /// <summary>
        /// 在当前分类页内移动或交换背包物品。
        /// </summary>
        public bool MoveBagItem(BagItemType pageType, int sourceIndex, int targetIndex)
        {
            if (sourceIndex == targetIndex || !IsValidBagIndex(sourceIndex) || !IsValidBagIndex(targetIndex))
            {
                return false;
            }

            BagItemData sourceItem = GetBagItem(pageType, sourceIndex);
            if (sourceItem == null)
            {
                return false;
            }

            BagItemData targetItem = GetBagItem(pageType, targetIndex);
            sourceItem.BagIndex = targetIndex;

            if (targetItem != null)
            {
                targetItem.BagIndex = sourceIndex;
            }

            NotifyInventoryChanged();
            return true;
        }

        /// <summary>
        /// 把背包物品移动到装备槽；目标已有同类物品时交换回原背包格。
        /// </summary>
        public bool MoveBagItemToEquipment(
            BagItemType pageType,
            int sourceIndex,
            BagSlotType targetType,
            int targetIndex)
        {
            if (!TryGetEquipmentItems(targetType, out BagItemData[] targetItems)
                || targetIndex < 0
                || targetIndex >= targetItems.Length
                || !IsValidBagIndex(sourceIndex))
            {
                return false;
            }

            BagItemData sourceItem = GetBagItem(pageType, sourceIndex);
            if (sourceItem == null || !CanPlace(targetType, sourceItem))
            {
                return false;
            }

            BagItemData targetItem = targetItems[targetIndex];
            if (targetItem != null && targetItem.ItemType != sourceItem.ItemType)
            {
                return false;
            }

            RemoveBagItem(sourceItem);
            targetItems[targetIndex] = sourceItem;
            sourceItem.BagIndex = -1;

            if (targetItem != null)
            {
                targetItem.BagIndex = sourceIndex;
                AddBagItem(targetItem);
            }

            NotifyInventoryChanged();
            return true;
        }

        /// <summary>
        /// 把装备槽物品拖回当前分类页的指定背包格，目标格已有可交换物品时互换。
        /// </summary>
        public bool MoveEquipmentToBagSlot(
            BagSlotType sourceType,
            int sourceIndex,
            BagItemType currentPageType,
            int targetIndex)
        {
            if (!TryGetEquipmentItems(sourceType, out BagItemData[] sourceItems)
                || sourceIndex < 0
                || sourceIndex >= sourceItems.Length
                || !IsValidBagIndex(targetIndex))
            {
                return false;
            }

            BagItemData sourceItem = sourceItems[sourceIndex];
            if (sourceItem == null || sourceItem.ItemType != currentPageType)
            {
                return false;
            }

            BagItemData targetItem = GetBagItem(currentPageType, targetIndex);
            if (targetItem != null && !CanPlace(sourceType, targetItem))
            {
                return false;
            }

            sourceItems[sourceIndex] = targetItem;
            if (targetItem != null)
            {
                RemoveBagItem(targetItem);
                targetItem.BagIndex = -1;
            }

            sourceItem.BagIndex = targetIndex;
            AddBagItem(sourceItem);

            NotifyInventoryChanged();
            return true;
        }

        /// <summary>
        /// 把装备槽物品放回其所属分类的第一个空背包格。
        /// </summary>
        public bool MoveEquipmentToFirstEmptyBagSlot(BagSlotType sourceType, int sourceIndex)
        {
            if (!TryGetEquipmentItems(sourceType, out BagItemData[] sourceItems)
                || sourceIndex < 0
                || sourceIndex >= sourceItems.Length)
            {
                return false;
            }

            BagItemData sourceItem = sourceItems[sourceIndex];
            if (sourceItem == null)
            {
                return false;
            }

            int emptyIndex = FindFirstEmptyBagIndex(sourceItem.ItemType);
            if (emptyIndex < 0)
            {
                return false;
            }

            sourceItems[sourceIndex] = null;
            sourceItem.BagIndex = emptyIndex;
            AddBagItem(sourceItem);

            NotifyInventoryChanged();
            return true;
        }

        /// <summary>
        /// 在两个装备槽之间移动或交换物品，并校验双方物品是否允许放入目标槽。
        /// </summary>
        public bool MoveEquipmentSlot(
            BagSlotType sourceType,
            int sourceIndex,
            BagSlotType targetType,
            int targetIndex)
        {
            BagSlotAddress source = new BagSlotAddress(sourceType, sourceIndex);
            BagSlotAddress target = new BagSlotAddress(targetType, targetIndex);
            if (source.Equals(target)
                || !TryGetEquipmentItems(sourceType, out BagItemData[] sourceItems)
                || !TryGetEquipmentItems(targetType, out BagItemData[] targetItems)
                || sourceIndex < 0
                || sourceIndex >= sourceItems.Length
                || targetIndex < 0
                || targetIndex >= targetItems.Length)
            {
                return false;
            }

            BagItemData sourceItem = sourceItems[sourceIndex];
            BagItemData targetItem = targetItems[targetIndex];
            if (sourceItem == null || !CanPlace(targetType, sourceItem))
            {
                return false;
            }

            if (targetItem != null && !CanPlace(sourceType, targetItem))
            {
                return false;
            }

            sourceItems[sourceIndex] = targetItem;
            targetItems[targetIndex] = sourceItem;
            NotifyInventoryChanged();
            return true;
        }

        /// <summary>
        /// 消耗指定消耗品装备槽中的一个物品，数量归零时清空槽位。
        /// </summary>
        public bool TryConsumeEquipmentItem(BagSlotType slotType, int index)
        {
            if (slotType != BagSlotType.Consumable
                || !TryGetEquipmentItems(slotType, out BagItemData[] items)
                || index < 0
                || index >= items.Length)
            {
                return false;
            }

            BagItemData item = items[index];
            if (item == null)
            {
                return false;
            }

            if (item.Count > 1)
            {
                item.Count--;
            }
            else
            {
                items[index] = null;
            }

            NotifyInventoryChanged();
            return true;
        }

        /// <summary>创建玩家死亡重开快照，只缓存穿戴装备、激活武器和携带药水。</summary>
        public PlayerRestartSnapshot CreateRestartSnapshot(int activeWeaponIndex)
        {
            EnsureInitializedForPickup();
            return new PlayerRestartSnapshot(
                CloneEquipmentSlot(BagSlotType.Weapon),
                CloneFirstEquipmentItem(BagSlotType.Helmet),
                CloneFirstEquipmentItem(BagSlotType.Armor),
                CloneFirstEquipmentItem(BagSlotType.Leggings),
                CloneFirstEquipmentItem(BagSlotType.Gloves),
                CloneEquipmentSlot(BagSlotType.Consumable),
                activeWeaponIndex);
        }

        /// <summary>把死亡重开快照写回背包装备槽和消耗品槽。</summary>
        public void ApplyRestartSnapshot(PlayerRestartSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            EnsureInitializedForPickup();
            ApplyEquipmentSlotSnapshot(BagSlotType.Weapon, snapshot.WeaponSlots);
            ApplySingleEquipmentSlotSnapshot(BagSlotType.Helmet, snapshot.Helmet);
            ApplySingleEquipmentSlotSnapshot(BagSlotType.Armor, snapshot.Armor);
            ApplySingleEquipmentSlotSnapshot(BagSlotType.Leggings, snapshot.Leggings);
            ApplySingleEquipmentSlotSnapshot(BagSlotType.Gloves, snapshot.Gloves);
            ApplyEquipmentSlotSnapshot(BagSlotType.Consumable, snapshot.ConsumableSlots);
            NotifyInventoryChanged();
        }

        /// <summary>根据背包装备槽刷新新玩家装备外观、技能、属性和激活武器。</summary>
        public void ApplyEquipmentSlotsToPlayer(EquipmentManager equipmentManager, int activeWeaponIndex)
        {
            if (equipmentManager == null)
            {
                return;
            }

            EnsureInitializedForPickup();
            ApplyWeaponSlotsToPlayer(equipmentManager);
            ApplyEquipmentSlotToPlayer(equipmentManager, BagSlotType.Helmet, EquipmentType.Helmet);
            ApplyEquipmentSlotToPlayer(equipmentManager, BagSlotType.Armor, EquipmentType.Armor);
            ApplyEquipmentSlotToPlayer(equipmentManager, BagSlotType.Leggings, EquipmentType.Leggings);
            ApplyEquipmentSlotToPlayer(equipmentManager, BagSlotType.Gloves, EquipmentType.Gloves);

            if (activeWeaponIndex >= 0)
            {
                equipmentManager.ActivateWeapon(activeWeaponIndex);
            }

            equipmentManager.ApplyWeaponAppearance(false);
        }

        /// <summary>
        /// 判断指定物品是否允许放入目标槽位。
        /// </summary>
        public bool CanPlace(BagSlotType targetType, BagItemData item)
        {
            if (item == null)
            {
                return true;
            }

            switch (targetType)
            {
                case BagSlotType.Bag:
                    return true;
                case BagSlotType.Weapon:
                    return item.ItemType == BagItemType.Weapon;
                case BagSlotType.Helmet:
                    return item.ItemType == BagItemType.Helmet;
                case BagSlotType.Armor:
                    return item.ItemType == BagItemType.Armor;
                case BagSlotType.Leggings:
                    return item.ItemType == BagItemType.Leggings;
                case BagSlotType.Gloves:
                    return item.ItemType == BagItemType.Gloves;
                case BagSlotType.Consumable:
                    return item.ItemType == BagItemType.Consumable;
                default:
                    return false;
            }
        }

        /// <summary>
        /// 订阅地面掉落物拾取请求，避免背包 UI 未打开时丢失拾取。
        /// </summary>
        private void SubscribePickupEvent()
        {
            if (m_pickupEventSubscribed)
            {
                return;
            }

            EventCenter.Instance.Subscribe(DropItemPickupRequestEventArgs.EventId, OnDropItemPickupRequested);
            m_pickupEventSubscribed = true;
        }

        /// <summary>
        /// 解除地面掉落物拾取请求订阅。
        /// </summary>
        private void UnsubscribePickupEvent()
        {
            if (!m_pickupEventSubscribed)
            {
                return;
            }

            EventCenter.TryUnSubscribe(DropItemPickupRequestEventArgs.EventId, OnDropItemPickupRequested);
            m_pickupEventSubscribed = false;
        }

        /// <summary>
        /// 响应地面掉落物拾取请求，并把添加结果回传给掉落物。
        /// </summary>
        private void OnDropItemPickupRequested(object sender, EventArgsBase eventArgs)
        {
            DropItemPickupRequestEventArgs request = eventArgs as DropItemPickupRequestEventArgs;
            if (request == null)
            {
                return;
            }

            bool success = TryAddItems(request.Items);
            request.Complete(success);
        }

        /// <summary>
        /// 拾取发生在背包 UI 打开前时，用默认容量准备背包数据。
        /// </summary>
        private void EnsureInitializedForPickup()
        {
            if (!m_initialized)
            {
                Initialize(DefaultBagCapacity);
            }
        }

        /// <summary>
        /// 确保指定背包分类存在列表容器。
        /// </summary>
        private void EnsureBagCategory(BagItemType itemType)
        {
            if (itemType != BagItemType.None && !m_model.BagItemsByType.ContainsKey(itemType))
            {
                m_model.BagItemsByType.Add(itemType, new List<BagItemData>());
            }
        }

        /// <summary>
        /// 确保装备槽数组存在；槽位数量变化时保留原有槽位中的物品引用。
        /// </summary>
        private void EnsureEquipmentSlot(BagSlotType slotType, int slotCount)
        {
            if (m_model.EquipmentSlots.TryGetValue(slotType, out BagItemData[] currentItems))
            {
                if (currentItems.Length == slotCount)
                {
                    return;
                }

                BagItemData[] resizedItems = new BagItemData[slotCount];
                Array.Copy(currentItems, resizedItems, Mathf.Min(currentItems.Length, resizedItems.Length));
                m_model.EquipmentSlots[slotType] = resizedItems;
                return;
            }

            m_model.EquipmentSlots[slotType] = new BagItemData[slotCount];
        }

        /// <summary>复制指定装备槽数组，供死亡重开快照保存当前槽位状态。</summary>
        private BagItemData[] CloneEquipmentSlot(BagSlotType slotType)
        {
            return TryGetEquipmentItems(slotType, out BagItemData[] items)
                ? PlayerRestartSnapshot.CloneItems(items)
                : Array.Empty<BagItemData>();
        }

        /// <summary>复制单槽装备物品，供死亡重开快照保存防具槽状态。</summary>
        private BagItemData CloneFirstEquipmentItem(BagSlotType slotType)
        {
            return TryGetEquipmentItems(slotType, out BagItemData[] items) && items.Length > 0
                ? PlayerRestartSnapshot.CloneItem(items[0])
                : null;
        }

        /// <summary>把快照数组覆盖到指定装备槽，槽位数量以当前背包模型为准。</summary>
        private void ApplyEquipmentSlotSnapshot(BagSlotType slotType, BagItemData[] snapshotItems)
        {
            if (!TryGetEquipmentItems(slotType, out BagItemData[] items))
            {
                return;
            }

            for (int i = 0; i < items.Length; i++)
            {
                items[i] = snapshotItems != null && i < snapshotItems.Length
                    ? PlayerRestartSnapshot.CloneItem(snapshotItems[i])
                    : null;
            }
        }

        /// <summary>把单槽防具快照覆盖到指定装备槽。</summary>
        private void ApplySingleEquipmentSlotSnapshot(BagSlotType slotType, BagItemData snapshotItem)
        {
            if (!TryGetEquipmentItems(slotType, out BagItemData[] items) || items.Length == 0)
            {
                return;
            }

            items[0] = PlayerRestartSnapshot.CloneItem(snapshotItem);
        }

        /// <summary>把两个武器装备槽同步到玩家装备管理器。</summary>
        private void ApplyWeaponSlotsToPlayer(EquipmentManager equipmentManager)
        {
            if (!TryGetEquipmentItems(BagSlotType.Weapon, out BagItemData[] weaponItems))
            {
                return;
            }

            for (int i = 0; i < weaponItems.Length; i++)
            {
                BagItemData item = weaponItems[i];
                if (item == null || string.IsNullOrWhiteSpace(item.ObjectName))
                {
                    equipmentManager.ClearWeaponSlot(i);
                    continue;
                }

                equipmentManager.SetWeaponObject(i, item.ObjectName, item.AttackBonus);
            }
        }

        /// <summary>把单个防具装备槽同步到玩家装备管理器。</summary>
        private void ApplyEquipmentSlotToPlayer(
            EquipmentManager equipmentManager,
            BagSlotType slotType,
            EquipmentType equipmentType)
        {
            BagItemData item = GetItem(slotType, 0);
            if (item == null || string.IsNullOrWhiteSpace(item.ObjectName))
            {
                equipmentManager.ClearEquipment(equipmentType);
                return;
            }

            equipmentManager.SetEquipmentObject(equipmentType, item.ObjectName, item.DefenseBonus);
        }

        /// <summary>
        /// 读取指定装备槽数组。
        /// </summary>
        private bool TryGetEquipmentItems(BagSlotType slotType, out BagItemData[] items)
        {
            return m_model.EquipmentSlots.TryGetValue(slotType, out items);
        }

        /// <summary>
        /// 判断背包格索引是否在当前容量内。
        /// </summary>
        private bool IsValidBagIndex(int index)
        {
            return index >= 0 && index < m_model.BagCapacity;
        }

        /// <summary>
        /// 查找指定分类的第一个空背包格。
        /// </summary>
        private int FindFirstEmptyBagIndex(BagItemType itemType)
        {
            for (int i = 0; i < m_model.BagCapacity; i++)
            {
                if (GetBagItem(itemType, i) == null)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// 为批量掉落物预分配背包格，消耗品可复用已有或本批次同 ID 堆叠格。
        /// </summary>
        private bool TryResolveBatchBagIndexes(IReadOnlyList<DropItemStack> items, int[] bagIndexes)
        {
            for (int i = 0; i < bagIndexes.Length; i++)
            {
                bagIndexes[i] = -1;
            }

            for (int i = 0; i < items.Count; i++)
            {
                DropItemStack item = items[i];
                int bagIndex = FindPickupMergeBagIndex(item, items, bagIndexes, i);
                if (bagIndex >= 0 || bagIndex == MergeExistingConsumableIndex)
                {
                    bagIndexes[i] = bagIndex;
                    continue;
                }

                bagIndex = FindFirstBatchEmptyBagIndex(item.ItemType, items, bagIndexes, i);
                if (bagIndex < 0)
                {
                    return false;
                }

                bagIndexes[i] = bagIndex;
            }

            return true;
        }

        /// <summary>
        /// 查找拾取消耗品可以合并到的现有背包格或本批次已预占格。
        /// </summary>
        private int FindPickupMergeBagIndex(
            DropItemStack item,
            IReadOnlyList<DropItemStack> items,
            int[] bagIndexes,
            int resolvedCount)
        {
            if (item.ItemType != BagItemType.Consumable)
            {
                return -1;
            }

            BagItemData existingItem = FindStoredConsumableItem(item.ItemId);
            if (existingItem != null)
            {
                return MergeExistingConsumableIndex;
            }

            for (int i = 0; i < resolvedCount; i++)
            {
                if (items[i].ItemType == BagItemType.Consumable && items[i].ItemId == item.ItemId)
                {
                    return bagIndexes[i];
                }
            }

            return -1;
        }

        /// <summary>
        /// 查找当前批量拾取过程中尚未被预占的第一个空背包格。
        /// </summary>
        private int FindFirstBatchEmptyBagIndex(
            BagItemType itemType,
            IReadOnlyList<DropItemStack> items,
            int[] bagIndexes,
            int resolvedCount)
        {
            for (int i = 0; i < m_model.BagCapacity; i++)
            {
                if (GetBagItem(itemType, i) != null)
                {
                    continue;
                }

                if (IsBatchBagIndexReserved(itemType, i, items, bagIndexes, resolvedCount))
                {
                    continue;
                }

                return i;
            }

            return -1;
        }

        /// <summary>
        /// 判断背包格是否已经被本次批量拾取中更早的同分类物品预占。
        /// </summary>
        private static bool IsBatchBagIndexReserved(
            BagItemType itemType,
            int bagIndex,
            IReadOnlyList<DropItemStack> items,
            int[] bagIndexes,
            int resolvedCount)
        {
            for (int i = 0; i < resolvedCount; i++)
            {
                if (items[i].ItemType == itemType && bagIndexes[i] == bagIndex)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 查找本次拾取已经创建或合并过的同 ID 消耗品堆叠。
        /// </summary>
        private BagItemData FindPickupMergeTarget(DropItemStack item, BagItemData[] changedItems, int resolvedCount)
        {
            if (item.ItemType != BagItemType.Consumable)
            {
                return null;
            }

            for (int i = 0; i < resolvedCount; i++)
            {
                BagItemData changedItem = changedItems[i];
                if (changedItem != null
                    && changedItem.ItemType == BagItemType.Consumable
                    && changedItem.Id == item.ItemId)
                {
                    return changedItem;
                }
            }

            return FindStoredConsumableItem(item.ItemId);
        }

        /// <summary>
        /// 在普通背包和快捷消耗槽中查找指定配置 ID 的堆叠。
        /// </summary>
        private BagItemData FindStoredConsumableItem(int itemId)
        {
            if (!m_model.BagItemsByType.TryGetValue(BagItemType.Consumable, out List<BagItemData> items))
            {
                return FindEquipmentConsumableItem(itemId);
            }

            for (int i = 0; i < items.Count; i++)
            {
                BagItemData item = items[i];
                if (item != null && item.Id == itemId)
                {
                    return item;
                }
            }

            return FindEquipmentConsumableItem(itemId);
        }

        /// <summary>
        /// 在快捷消耗品槽中查找指定配置 ID 的堆叠。
        /// </summary>
        private BagItemData FindEquipmentConsumableItem(int itemId)
        {
            if (!TryGetEquipmentItems(BagSlotType.Consumable, out BagItemData[] items))
            {
                return null;
            }

            for (int i = 0; i < items.Length; i++)
            {
                BagItemData item = items[i];
                if (item != null && item.Id == itemId)
                {
                    return item;
                }
            }

            return null;
        }

        /// <summary>
        /// 把物品加入对应分类列表，并处理同格旧物品替换。
        /// </summary>
        private void AddBagItem(BagItemData item)
        {
            if (item == null || item.ItemType == BagItemType.None || !IsValidBagIndex(item.BagIndex))
            {
                return;
            }

            EnsureBagCategory(item.ItemType);
            List<BagItemData> items = m_model.BagItemsByType[item.ItemType];
            BagItemData oldItem = GetBagItem(item.ItemType, item.BagIndex);
            if (oldItem != null && oldItem != item)
            {
                items.Remove(oldItem);
            }

            if (!items.Contains(item))
            {
                items.Add(item);
            }
        }

        /// <summary>
        /// 从所属分类列表中移除指定物品。
        /// </summary>
        private void RemoveBagItem(BagItemData item)
        {
            if (item == null || !m_model.BagItemsByType.TryGetValue(item.ItemType, out List<BagItemData> items))
            {
                return;
            }

            items.Remove(item);
        }

        /// <summary>
        /// 通知所有监听者背包数据已经变化。
        /// </summary>
        private void NotifyInventoryChanged()
        {
            if (InventoryChanged != null)
            {
                InventoryChanged.Invoke();
            }
        }
    }
}
