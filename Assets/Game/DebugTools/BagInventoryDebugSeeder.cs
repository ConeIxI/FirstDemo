using System;
using GameMain2.Scripts.UI;
using UnityEngine;

namespace GameMain2.Scripts.DebugTools
{
    /// <summary>
    /// Play Mode 背包调试投放器，用于把指定装备和消耗品放入玩家背包。
    /// </summary>
    public sealed class BagInventoryDebugSeeder : MonoBehaviour
    {
        [SerializeField] private int bagCapacity = 30;
        [SerializeField] private bool seedOnStart = true;
        [SerializeField] private bool seedOnce = true;
        [SerializeField] private BagSeedItemDefinition[] seedItems =
        {
            new BagSeedItemDefinition(BagItemType.Weapon, 1, 1),
            new BagSeedItemDefinition(BagItemType.Weapon, 2, 1),
            new BagSeedItemDefinition(BagItemType.Helmet, 1, 1),
            new BagSeedItemDefinition(BagItemType.Helmet, 2, 1),
            new BagSeedItemDefinition(BagItemType.Helmet, 3, 1),
            new BagSeedItemDefinition(BagItemType.Helmet, 4, 1),
            new BagSeedItemDefinition(BagItemType.Armor, 1, 1),
            new BagSeedItemDefinition(BagItemType.Armor, 2, 1),
            new BagSeedItemDefinition(BagItemType.Armor, 3, 1),
            new BagSeedItemDefinition(BagItemType.Armor, 4, 1),
            new BagSeedItemDefinition(BagItemType.Leggings, 1, 1),
            new BagSeedItemDefinition(BagItemType.Leggings, 2, 1),
            new BagSeedItemDefinition(BagItemType.Leggings, 3, 1),
            new BagSeedItemDefinition(BagItemType.Leggings, 4, 1),
            new BagSeedItemDefinition(BagItemType.Gloves, 1, 1),
            new BagSeedItemDefinition(BagItemType.Gloves, 2, 1),
            new BagSeedItemDefinition(BagItemType.Gloves, 3, 1),
            new BagSeedItemDefinition(BagItemType.Gloves, 4, 1),
            new BagSeedItemDefinition(BagItemType.Consumable, 1, 1),
            new BagSeedItemDefinition(BagItemType.Consumable, 2, 1),
            new BagSeedItemDefinition(BagItemType.Consumable, 3, 1)
        };

        /// <summary>
        /// Play Mode 开始时按配置自动投放调试物品。
        /// </summary>
        private void Start()
        {
            if (seedOnStart)
            {
                SeedBag();
            }
        }

        /// <summary>
        /// 按列表把装备和消耗品加入玩家背包，任何投放失败都会立即报错。
        /// </summary>
        [ContextMenu("Seed Bag")]
        public void SeedBag()
        {
            BagInventoryManager inventory = BagInventoryManager.Instance;
            if (seedOnce && inventory.IsDebugSeeded)
            {
                return;
            }

            inventory.Initialize(bagCapacity);

            for (int i = 0; i < seedItems.Length; i++)
            {
                BagSeedItemDefinition seedItem = seedItems[i];
                bool added = inventory.TryAddItem(seedItem.ItemType, seedItem.Id, seedItem.Count);
                if (!added)
                {
                    throw new InvalidOperationException(
                        $"背包调试物品投放失败：index={i}, type={seedItem.ItemType}, id={seedItem.Id}, count={seedItem.Count}");
                }
            }

            if (seedOnce)
            {
                inventory.MarkDebugSeeded();
            }
        }
    }

    /// <summary>
    /// 背包调试物品定义，指定物品分类、配置 Id 和投放数量。
    /// </summary>
    [Serializable]
    public struct BagSeedItemDefinition
    {
        public BagItemType ItemType;
        public int Id;
        public int Count;

        /// <summary>
        /// 创建一条背包调试物品投放定义。
        /// </summary>
        public BagSeedItemDefinition(BagItemType itemType, int id, int count)
        {
            ItemType = itemType;
            Id = id;
            Count = count;
        }
    }
}
