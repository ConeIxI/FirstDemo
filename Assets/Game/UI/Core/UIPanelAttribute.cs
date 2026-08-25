using System;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 标记 UI 面板的注册信息，避免新增面板时反复修改 UIManager 的映射表。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class UIPanelAttribute : Attribute
    {
        public UIType Type { get; }
        public UILayer Layer { get; }
        public string Address { get; }
        public bool BlockGameplayInput { get; }

        public UIPanelAttribute(
            UIType type,
            UILayer layer = UILayer.Normal,
            string address = null,
            bool blockGameplayInput = false)
        {
            Type = type;
            Layer = layer;
            Address = address;
            BlockGameplayInput = blockGameplayInput;
        }
    }
}
