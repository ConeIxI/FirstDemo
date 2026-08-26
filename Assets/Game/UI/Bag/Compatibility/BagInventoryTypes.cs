using System;
using Game.Config.Item;
using GameMain2.Framework.Manager;
using UnityEngine;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 背包物品粗分类，用于 UI 分类页、装备槽规则和背包业务判断。
    /// </summary>
    public enum BagItemType
    {
        None,
        Weapon,
        Helmet,
        Armor,
        Leggings,
        Gloves,
        Consumable
    }

    /// <summary>
    /// 背包槽位类型，Bag 表示普通背包格，其它类型表示角色装备栏槽位。
    /// </summary>
    public enum BagSlotType
    {
        Bag,
        Weapon,
        Helmet,
        Armor,
        Leggings,
        Gloves,
        Consumable
    }

    /// <summary>
    /// 背包和装备槽共用的轻量物品数据。
    /// </summary>
    [Serializable]
    public sealed class BagItemData
    {
        public int Id;
        public BagItemType ItemType;
        public int Count;
        public int BagIndex;
        public int Defense;
        public bool IsNew;
        public Sprite Icon;
        public Sprite[] SkillIcons;

        public string Name => Config.name;
        public string IconAddress => Config.iconAddress;
        public string[] SkillIconAddresses => GetSkillIconAddresses(Config);
        public string PrefabAddress => Config.prefabAddress;
        public string ObjectName => GetObjectName(Config);
        public int BuffId => GetBuffId(Config);
        public int AttackBonus => GetAttackBonus(Config);
        public int DefenseBonus => IsDefenseEquipment(ItemType) ? Defense : 0;

        private ItemConfigBase Config => GetConfig(ItemType, Id);

        /// <summary>
        /// 创建默认放在背包第 0 格的物品数据。
        /// </summary>
        public BagItemData(int id, BagItemType itemType, int count = 1, Sprite icon = null)
            : this(id, itemType, 0, count, icon)
        {
        }

        /// <summary>
        /// 创建指定背包格、数量和图标缓存的物品数据。
        /// </summary>
        public BagItemData(
            int id,
            BagItemType itemType,
            int bagIndex,
            int count,
            Sprite icon = null,
            int defense = 0)
        {
            Id = id;
            ItemType = itemType;
            BagIndex = Mathf.Max(0, bagIndex);
            Count = Mathf.Max(1, count);
            Defense = ResolveDefenseValue(itemType, id, defense);
            Icon = icon;
            SkillIcons = Array.Empty<Sprite>();
        }

        /// <summary>
        /// 根据物品类型和配置 ID 读取对应配置。
        /// </summary>
        private static ItemConfigBase GetConfig(BagItemType itemType, int id)
        {
            switch (itemType)
            {
                case BagItemType.Weapon:
                    return ConfigManager.Instance.GetWeaponItemConfig(id);
                case BagItemType.Helmet:
                    return ConfigManager.Instance.GetHelmetItemConfig(id);
                case BagItemType.Armor:
                    return ConfigManager.Instance.GetArmorItemConfig(id);
                case BagItemType.Leggings:
                    return ConfigManager.Instance.GetLeggingsItemConfig(id);
                case BagItemType.Gloves:
                    return ConfigManager.Instance.GetGlovesItemConfig(id);
                case BagItemType.Consumable:
                    return ConfigManager.Instance.GetConsumableItemConfig(id);
                default:
                    throw new Exception($"未知背包物品分类：Id：{id}，分类：{itemType}");
            }
        }

        /// <summary>
        /// 从装备配置中读取外观对象名，非装备类型返回空字符串。
        /// </summary>
        private static string GetObjectName(ItemConfigBase config)
        {
            switch (config)
            {
                case WeaponItemConfig weaponConfig:
                    return weaponConfig.objectName;
                case HelmetItemConfig helmetConfig:
                    return helmetConfig.objectName;
                case ArmorItemConfig armorConfig:
                    return armorConfig.objectName;
                case LeggingsItemConfig leggingsConfig:
                    return leggingsConfig.objectName;
                case GlovesItemConfig glovesConfig:
                    return glovesConfig.objectName;
                default:
                    return string.Empty;
            }
        }

        /// <summary>
        /// 从消耗品配置中读取 BuffId，非消耗品固定返回 0。
        /// </summary>
        private static int GetBuffId(ItemConfigBase config)
        {
            ConsumableItemConfig consumableConfig = config as ConsumableItemConfig;
            return consumableConfig == null ? 0 : consumableConfig.buffId;
        }

        /// <summary>
        /// 读取武器配置提供的攻击力，非武器固定返回 0。
        /// </summary>
        private static int GetAttackBonus(ItemConfigBase config)
        {
            WeaponItemConfig weaponConfig = config as WeaponItemConfig;
            return weaponConfig == null ? 0 : weaponConfig.attack;
        }

        /// <summary>
        /// 读取武器配置提供的三个技能图标地址，非武器返回空数组。
        /// </summary>
        private static string[] GetSkillIconAddresses(ItemConfigBase config)
        {
            WeaponItemConfig weaponConfig = config as WeaponItemConfig;
            return weaponConfig == null ? Array.Empty<string>() : weaponConfig.skillIconAddresses;
        }

        /// <summary>
        /// 判断物品分类是否属于拥有防御力的防具。
        /// </summary>
        private static bool IsDefenseEquipment(BagItemType itemType)
        {
            return itemType == BagItemType.Helmet
                || itemType == BagItemType.Armor
                || itemType == BagItemType.Leggings
                || itemType == BagItemType.Gloves;
        }

        /// <summary>
        /// 解析防具实例防御力，敌人掉落传入实际值，其它来源使用配置下限作为默认值。
        /// </summary>
        private static int ResolveDefenseValue(BagItemType itemType, int id, int defense)
        {
            if (!IsDefenseEquipment(itemType))
            {
                return 0;
            }

            if (defense > 0)
            {
                return defense;
            }

            DefenseEquipmentItemConfig defenseConfig = GetConfig(itemType, id) as DefenseEquipmentItemConfig;
            return defenseConfig == null ? 0 : defenseConfig.minDefense;
        }
    }

    /// <summary>
    /// 玩家死亡重开快照，只保存允许跨重开的装备槽和消耗品槽数据。
    /// </summary>
    public sealed class PlayerRestartSnapshot
    {
        public BagItemData[] WeaponSlots { get; }
        public BagItemData Helmet { get; }
        public BagItemData Armor { get; }
        public BagItemData Leggings { get; }
        public BagItemData Gloves { get; }
        public BagItemData[] ConsumableSlots { get; }
        public int ActiveWeaponIndex { get; }

        /// <summary>创建死亡重开快照，并复制可变物品数据避免后续运行时状态污染。</summary>
        public PlayerRestartSnapshot(
            BagItemData[] weaponSlots,
            BagItemData helmet,
            BagItemData armor,
            BagItemData leggings,
            BagItemData gloves,
            BagItemData[] consumableSlots,
            int activeWeaponIndex)
        {
            WeaponSlots = CloneItems(weaponSlots);
            Helmet = CloneItem(helmet);
            Armor = CloneItem(armor);
            Leggings = CloneItem(leggings);
            Gloves = CloneItem(gloves);
            ConsumableSlots = CloneItems(consumableSlots);
            ActiveWeaponIndex = activeWeaponIndex;
        }

        /// <summary>复制一组槽位物品，保留空槽位。</summary>
        public static BagItemData[] CloneItems(BagItemData[] items)
        {
            if (items == null)
            {
                return Array.Empty<BagItemData>();
            }

            BagItemData[] clonedItems = new BagItemData[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                clonedItems[i] = CloneItem(items[i]);
            }

            return clonedItems;
        }

        /// <summary>复制单个背包物品，保留图标缓存和装备槽标记。</summary>
        public static BagItemData CloneItem(BagItemData item)
        {
            if (item == null)
            {
                return null;
            }

            BagItemData clonedItem = new BagItemData(item.Id, item.ItemType, item.BagIndex, item.Count, item.Icon);
            clonedItem.BagIndex = item.BagIndex;
            clonedItem.Defense = item.Defense;
            clonedItem.IsNew = item.IsNew;
            clonedItem.SkillIcons = item.SkillIcons == null
                ? Array.Empty<Sprite>()
                : (Sprite[])item.SkillIcons.Clone();
            return clonedItem;
        }
    }

    /// <summary>
    /// 一个槽位的唯一地址，用于判断拖拽源和目标是否是同一个格子。
    /// </summary>
    public readonly struct BagSlotAddress
    {
        public readonly BagSlotType SlotType;
        public readonly int Index;

        /// <summary>
        /// 创建指定槽位类型和索引的槽位地址。
        /// </summary>
        public BagSlotAddress(BagSlotType slotType, int index)
        {
            SlotType = slotType;
            Index = index;
        }

        /// <summary>
        /// 判断两个槽位地址是否指向同一个槽位。
        /// </summary>
        public bool Equals(BagSlotAddress other)
        {
            return SlotType == other.SlotType && Index == other.Index;
        }
    }
}
