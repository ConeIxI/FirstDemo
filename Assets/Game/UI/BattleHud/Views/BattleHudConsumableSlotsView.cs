using UnityEngine;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 战斗 HUD 左下角消耗品槽视图，读取背包功能层的四个已装备消耗品槽并刷新显示。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class BattleHudConsumableSlotsView : MonoBehaviour
    {
        [SerializeField] private BattleHudSlotView[] slots;

        private BagInventoryManager m_inventory;
        private bool m_inventorySubscribed;

        /// <summary>
        /// 初始化 prefab 中已经配置好的消耗品槽，并订阅背包数据变化。
        /// </summary>
        public void Init()
        {
            InitSlots();
            EnsureInventory();
            SubscribeInventory();
            RefreshSlots();
        }

        /// <summary>
        /// 显示消耗品槽视图，并刷新已装备消耗品。
        /// </summary>
        public void Show()
        {
            gameObject.SetActive(true);
            EnsureInventory();
            SubscribeInventory();
            RefreshSlots();
        }

        /// <summary>
        /// 隐藏消耗品槽视图，并解除背包事件订阅。
        /// </summary>
        public void Hide()
        {
            UnsubscribeInventory();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 释放消耗品槽视图持有的背包事件订阅。
        /// </summary>
        public void Dispose()
        {
            UnsubscribeInventory();
        }

        /// <summary>
        /// 设置消耗品槽位标签，外部手动覆盖时仍复用 prefab 中的文字节点。
        /// </summary>
        public void SetSlotLabel(int index, string label)
        {
            if (slots == null || index < 0 || index >= slots.Length || slots[index] == null)
            {
                return;
            }

            slots[index].SetLabel(label);
        }

        /// <summary>
        /// 初始化已绑定的消耗品槽，槽位数量和布局完全由 prefab 决定。
        /// </summary>
        private void InitSlots()
        {
            if (slots == null)
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                BattleHudSlotView slot = slots[i];
                if (slot != null)
                {
                    slot.Init();
                }
            }
        }

        /// <summary>
        /// 获取背包兼容门面，保证 HUD 和背包面板读取同一份消耗品槽数据。
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
        /// 订阅背包数据变化，避免重复打开 HUD 时重复监听。
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
        /// 背包数据变化后刷新 HUD 消耗品槽显示。
        /// </summary>
        private void OnInventoryChanged()
        {
            RefreshSlots();
        }

        /// <summary>
        /// 从功能层读取四个已装备消耗品槽，并写入 HUD 槽位标签。
        /// </summary>
        private void RefreshSlots()
        {
            if (slots == null)
            {
                return;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                BattleHudSlotView slot = slots[i];
                if (slot == null)
                {
                    continue;
                }

                BagItemData item = m_inventory == null ? null : m_inventory.GetItem(BagSlotType.Consumable, i);
                slot.SetLabel(GetConsumableLabel(item));
                slot.SetIcon(item == null ? null : item.Icon);
            }
        }

        /// <summary>
        /// 生成 HUD 消耗品槽显示文本，装备消耗品后始终显示真实数量。
        /// </summary>
        private static string GetConsumableLabel(BagItemData item)
        {
            if (item == null)
            {
                return string.Empty;
            }

            return item.Count.ToString();
        }
    }
}
