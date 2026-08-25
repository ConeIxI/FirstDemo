using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 战斗 HUD 单个槽位视图，负责槽位标签显示，背景和边框由 prefab 配置。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class BattleHudSlotView : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI labelText;
        [FormerlySerializedAs("consumableIconImage")]
        [SerializeField] private Image iconImage;

        /// <summary>
        /// 初始化槽位显示组件，显示结构完全由 prefab 提前配置。
        /// </summary>
        public void Init()
        {
            SetIcon(null);
        }

        /// <summary>
        /// 显示槽位。
        /// </summary>
        public void Show()
        {
            gameObject.SetActive(true);
        }

        /// <summary>
        /// 隐藏槽位。
        /// </summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 释放槽位视图，目前没有事件订阅需要解绑。
        /// </summary>
        public void Dispose()
        {
        }

        /// <summary>
        /// 设置槽位显示文案。
        /// </summary>
        public void SetLabel(string label)
        {
            if (labelText != null)
            {
                labelText.text = label;
            }
        }

        /// <summary>
        /// 设置 HUD 槽位图标；空图标表示当前槽位没有可显示内容。
        /// </summary>
        public void SetIcon(Sprite icon)
        {
            if (iconImage == null)
            {
                return;
            }

            bool hasIcon = icon != null;
            iconImage.sprite = icon;
            iconImage.preserveAspect = true;
            iconImage.gameObject.SetActive(true);

            Color iconColor = iconImage.color;
            iconColor.a = hasIcon ? 1f : 0f;
            iconImage.color = iconColor;
        }
    }
}
