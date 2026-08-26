using System.Collections.Generic;
using GameMain2.Scripts.UI;

namespace GameMain2.Scripts.Modules.Bag
{
    /// <summary>
    /// 背包功能的长期数据容器，只保存数据结构，不处理 UI 表现。
    /// </summary>
    public sealed class BagModel
    {
        public int BagCapacity { get; set; }

        public Dictionary<BagItemType, List<BagItemData>> BagItemsByType { get; } =
            new Dictionary<BagItemType, List<BagItemData>>();

        public Dictionary<BagSlotType, BagItemData[]> EquipmentSlots { get; } =
            new Dictionary<BagSlotType, BagItemData[]>();

        public HashSet<BagItemType> NewItemTypes { get; } = new HashSet<BagItemType>();

        /// <summary>
        /// 清空背包物品和装备槽数据，用于重新初始化容量或重置存档。
        /// </summary>
        public void Clear()
        {
            BagItemsByType.Clear();
            EquipmentSlots.Clear();
            NewItemTypes.Clear();
        }
    }
}
