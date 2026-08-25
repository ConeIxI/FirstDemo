using UnityEngine;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 拾取成功提示的数据快照，避免 UI 播放期间反查背包状态。
    /// </summary>
    public sealed class PickupTipData
    {
        public BagItemType ItemType { get; }
        public int ItemId { get; }
        public Sprite Icon { get; }
        public string Name { get; }
        public int Count { get; private set; }

        /// <summary>
        /// 创建一条拾取提示数据，数量用于等待队列中的同道具累计。
        /// </summary>
        public PickupTipData(BagItemType itemType, int itemId, Sprite icon, string name, int count)
        {
            ItemType = itemType;
            ItemId = itemId;
            Icon = icon;
            Name = name;
            Count = count;
        }

        /// <summary>
        /// 判断另一条提示是否表示同一个可合并道具。
        /// </summary>
        public bool IsSameItem(PickupTipData other)
        {
            return other != null && ItemType == other.ItemType && ItemId == other.ItemId;
        }

        /// <summary>
        /// 累加等待队列中的同道具数量。
        /// </summary>
        public void AddCount(int count)
        {
            Count += count;
        }
    }
}
