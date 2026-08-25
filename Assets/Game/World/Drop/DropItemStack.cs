using System;
using GameMain2.Scripts.UI;
using UnityEngine;

namespace Game.World.Drop
{
    /// <summary>
    /// 地面掉落批次中的单个道具堆叠数据。
    /// </summary>
    [Serializable]
    public struct DropItemStack
    {
        [SerializeField] private BagItemType itemType;
        [SerializeField] private int itemId;
        [SerializeField] private int count;
        [SerializeField] private int defense;

        public BagItemType ItemType => itemType;
        public int ItemId => itemId;
        public int Count => count;
        public int Defense => defense;

        /// <summary>
        /// 创建一个地面掉落道具堆叠。
        /// </summary>
        public DropItemStack(BagItemType itemType, int itemId, int count, int defense = 0)
        {
            this.itemType = itemType;
            this.itemId = itemId;
            this.count = Mathf.Max(1, count);
            this.defense = Mathf.Max(0, defense);
        }
    }
}
