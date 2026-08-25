namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 背包装备槽视图，负责绑定 prefab 上已有装备槽并刷新显示。
    /// </summary>
    public sealed class BagEquipmentView : UIViewBase
    {
        private readonly BagPanel m_owner;
        private readonly BagSlotView[] m_equipmentSlots;
        private BagInventoryManager m_inventory;

        /// <summary>
        /// 创建装备槽视图，并保存 prefab 上的槽位引用。
        /// </summary>
        public BagEquipmentView(BagPanel owner, BagInventoryManager inventory, BagSlotView[] equipmentSlots)
        {
            m_owner = owner;
            m_inventory = inventory;
            m_equipmentSlots = equipmentSlots;
        }

        /// <summary>
        /// 更新背包数据门面引用，供重复打开面板时刷新装备槽。
        /// </summary>
        public void SetInventory(BagInventoryManager inventory)
        {
            m_inventory = inventory;
        }

        /// <summary>
        /// 显示装备槽视图，并把槽位绑定到对应装备数据地址。
        /// </summary>
        public override void Show()
        {
            BindEquipmentSlots();
            Refresh();
        }

        /// <summary>
        /// 隐藏装备槽视图，当前没有额外状态需要处理。
        /// </summary>
        public override void Hide()
        {
        }

        /// <summary>
        /// 释放装备槽视图，当前没有事件订阅需要解绑。
        /// </summary>
        public override void Dispose()
        {
        }

        /// <summary>
        /// 刷新所有装备槽显示。
        /// </summary>
        public void Refresh()
        {
            if (m_equipmentSlots == null)
            {
                return;
            }

            for (int i = 0; i < m_equipmentSlots.Length; i++)
            {
                if (m_equipmentSlots[i] != null)
                {
                    m_equipmentSlots[i].Refresh();
                }
            }
        }

        /// <summary>
        /// 绑定 prefab 上已有装备槽到对应的装备槽类型和索引。
        /// </summary>
        private void BindEquipmentSlots()
        {
            if (m_equipmentSlots == null)
            {
                return;
            }

            for (int i = 0; i < m_equipmentSlots.Length; i++)
            {
                BagSlotView slot = m_equipmentSlots[i];
                if (slot == null)
                {
                    continue;
                }

                string emptyText = BagInventoryManager.GetSlotDisplayName(slot.SlotType, slot.SlotIndex);
                slot.Bind(m_owner, m_inventory, slot.SlotType, slot.SlotIndex, emptyText);
            }
        }
    }
}
