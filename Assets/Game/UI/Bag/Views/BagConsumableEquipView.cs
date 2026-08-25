namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 背包消耗品装备槽视图，负责绑定 expendablePanel 上的四个快捷消耗品槽并刷新显示。
    /// </summary>
    public sealed class BagConsumableEquipView : UIViewBase
    {
        private readonly BagPanel m_owner;
        private readonly BagSlotView[] m_consumableSlots;
        private BagInventoryManager m_inventory;

        /// <summary>
        /// 创建消耗品装备槽视图，并保存 prefab 上已经布好的槽位引用。
        /// </summary>
        public BagConsumableEquipView(BagPanel owner, BagInventoryManager inventory, BagSlotView[] consumableSlots)
        {
            m_owner = owner;
            m_inventory = inventory;
            m_consumableSlots = consumableSlots;
        }

        /// <summary>
        /// 更新背包数据门面引用，供面板重复打开时复用同一个视图对象。
        /// </summary>
        public void SetInventory(BagInventoryManager inventory)
        {
            m_inventory = inventory;
        }

        /// <summary>
        /// 显示消耗品装备槽视图，并把槽位绑定到功能层的 Consumable 槽地址。
        /// </summary>
        public override void Show()
        {
            BindConsumableSlots();
            Refresh();
        }

        /// <summary>
        /// 隐藏消耗品装备槽视图，当前没有额外状态需要处理。
        /// </summary>
        public override void Hide()
        {
        }

        /// <summary>
        /// 释放消耗品装备槽视图，当前没有事件订阅需要解绑。
        /// </summary>
        public override void Dispose()
        {
        }

        /// <summary>
        /// 刷新所有消耗品装备槽显示。
        /// </summary>
        public void Refresh()
        {
            if (m_consumableSlots == null)
            {
                return;
            }

            for (int i = 0; i < m_consumableSlots.Length; i++)
            {
                if (m_consumableSlots[i] != null)
                {
                    m_consumableSlots[i].Refresh();
                }
            }
        }

        /// <summary>
        /// 绑定 prefab 中的四个消耗品槽到功能层 Consumable 槽索引。
        /// </summary>
        private void BindConsumableSlots()
        {
            if (m_consumableSlots == null)
            {
                return;
            }

            for (int i = 0; i < m_consumableSlots.Length; i++)
            {
                BagSlotView slot = m_consumableSlots[i];
                if (slot == null)
                {
                    continue;
                }

                string emptyText = BagInventoryManager.GetSlotDisplayName(BagSlotType.Consumable, i);
                slot.Bind(m_owner, m_inventory, BagSlotType.Consumable, i, emptyText);
            }
        }
    }
}
