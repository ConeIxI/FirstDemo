using GameMain2.Scripts.UI;

namespace GameMain2.Scripts.Modules.Bag
{
    /// <summary>
    /// 背包拾取提示派发器，隔离背包规则层对 UI 表现层的直接调用。
    /// </summary>
    internal static class BagPickupTipDispatcher
    {
        /// <summary>背包成功接收地面掉落物后，向 UI 提交拾取成功提示。</summary>
        public static void Show(BagItemData item, int count)
        {
            PickupTipData data = new PickupTipData(item.ItemType, item.Id, item.Icon, item.Name, count);
            UIManager.Instance.ShowPickupTip(data);
        }
    }
}
