using Game.Battle.Buff;
using GameMain2.Framework.Manager;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 背包装备详情悬浮面板，负责展示物品图标、名称和核心效果数值。
    /// </summary>
    public sealed class EquipmentDetailPanel : MonoBehaviour
    {
        private static readonly Vector2 TopLeftPivot = new Vector2(0f, 1f);

        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI detailText;

        private RectTransform m_rectTransform;
        private CanvasGroup m_canvasGroup;

        /// <summary>初始化面板引用，并确保悬浮面板不阻挡鼠标射线。</summary>
        private void Awake()
        {
            EnsureVisuals();
            Hide();
        }

        /// <summary>显示指定物品详情，并立即根据鼠标位置摆放面板。</summary>
        public void Show(BagItemData item, PointerEventData eventData)
        {
            EnsureVisuals();
            ApplyItem(item);
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            UpdatePosition(eventData);
        }

        /// <summary>隐藏详情面板。</summary>
        public void Hide()
        {
            EnsureVisuals();
            gameObject.SetActive(false);
        }

        /// <summary>根据鼠标位置更新面板左上角，并限制在根 Canvas 可见区域内。</summary>
        public void UpdatePosition(PointerEventData eventData)
        {
            EnsureVisuals();
            RectTransform bounds = ResolvePositionBounds();
            if (eventData == null || bounds == null)
            {
                return;
            }

            Camera eventCamera = ResolveEventCamera(eventData);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    bounds,
                    eventData.position,
                    eventCamera,
                    out Vector2 localPoint))
            {
                return;
            }

            Vector2 clampedPoint = ClampToBounds(bounds, localPoint);
            m_rectTransform.position = bounds.TransformPoint(clampedPoint);
        }

        /// <summary>确保 prefab 或运行时兜底对象拥有详情面板所需的基础 UI 引用。</summary>
        public void EnsureVisuals()
        {
            if (m_rectTransform == null)
            {
                m_rectTransform = GetComponent<RectTransform>();
            }

            if (m_rectTransform == null)
            {
                m_rectTransform = gameObject.AddComponent<RectTransform>();
            }

            m_rectTransform.pivot = TopLeftPivot;

            if (m_canvasGroup == null)
            {
                m_canvasGroup = GetComponent<CanvasGroup>();
                if (m_canvasGroup == null)
                {
                    m_canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            m_canvasGroup.blocksRaycasts = false;
            m_canvasGroup.interactable = false;
            CachePrefabReferences();
        }

        /// <summary>读取 prefab 中约定命名的图标、名称和详情文本组件。</summary>
        private void CachePrefabReferences()
        {
            if (iconImage == null)
            {
                Transform icon = transform.Find("Header/IconFrame/Icon");
                iconImage = icon == null ? null : icon.GetComponent<Image>();
            }

            if (nameText == null)
            {
                Transform label = transform.Find("Header/NameFrame/NameText");
                nameText = label == null ? null : label.GetComponent<TextMeshProUGUI>();
            }

            if (detailText == null)
            {
                Transform detail = transform.Find("DetailText");
                detailText = detail == null ? null : detail.GetComponent<TextMeshProUGUI>();
            }
        }

        /// <summary>把物品数据写入面板图标、名称和详情文本。</summary>
        private void ApplyItem(BagItemData item)
        {
            if (iconImage != null)
            {
                iconImage.sprite = item.Icon;
                iconImage.enabled = item.Icon != null;
            }

            if (nameText != null)
            {
                nameText.text = item.Name;
            }

            if (detailText != null)
            {
                detailText.text = BuildDetailText(item);
            }
        }

        /// <summary>根据物品分类生成详情正文，防具显示防御、武器显示伤害、消耗品显示 Buff 效果。</summary>
        private static string BuildDetailText(BagItemData item)
        {
            switch (item.ItemType)
            {
                case BagItemType.Weapon:
                    return $"伤害：{item.AttackBonus}";
                case BagItemType.Helmet:
                case BagItemType.Armor:
                case BagItemType.Leggings:
                case BagItemType.Gloves:
                    return $"防御：{item.DefenseBonus}";
                case BagItemType.Consumable:
                    return BuildConsumableEffectText(item);
                default:
                    return string.Empty;
            }
        }

        /// <summary>根据消耗品绑定的 Buff 配置自动拼接效果文案。</summary>
        private static string BuildConsumableEffectText(BagItemData item)
        {
            CombatBuffConfig config = ConfigManager.Instance.GetBuffConfig(item.BuffId);
            if (config == null)
            {
                return "效果：未知";
            }

            string buffName = string.IsNullOrWhiteSpace(config.buffName) ? "效果" : config.buffName;
            return $"效果：{buffName}\n{BuildBuffValueText(config)}";
        }

        /// <summary>按 Buff 类型生成具体数值描述。</summary>
        private static string BuildBuffValueText(CombatBuffConfig config)
        {
            switch (config.type)
            {
                case CombatBuffType.AttackModifier:
                    return BuildModifierText("攻击", config);
                case CombatBuffType.DefenseModifier:
                    return BuildModifierText("防御", config);
                case CombatBuffType.HealthRegen:
                    return $"每 {FormatSeconds(config.tickInterval)} 回复 {config.tickValue} 生命，持续 {FormatSeconds(config.duration)}";
                case CombatBuffType.HealthDamage:
                    return $"每 {FormatSeconds(config.tickInterval)} 损失 {config.tickValue} 生命，持续 {FormatSeconds(config.duration)}";
                default:
                    return $"持续 {FormatSeconds(config.duration)}";
            }
        }

        /// <summary>生成攻击或防御强化的数值描述。</summary>
        private static string BuildModifierText(string attributeName, CombatBuffConfig config)
        {
            string flatText = config.flatValue == 0 ? string.Empty : FormatSigned(config.flatValue);
            string percentText = Mathf.Approximately(config.percentValue, 0f)
                ? string.Empty
                : FormatSignedPercent(config.percentValue);
            string joiner = string.IsNullOrEmpty(flatText) || string.IsNullOrEmpty(percentText) ? string.Empty : "，";
            string valueText = string.IsNullOrEmpty(flatText) && string.IsNullOrEmpty(percentText)
                ? "无变化"
                : flatText + joiner + percentText;
            return $"{attributeName}{valueText}，持续 {FormatSeconds(config.duration)}";
        }

        /// <summary>格式化带正负号的整数。</summary>
        private static string FormatSigned(int value)
        {
            return value > 0 ? $"+{value}" : value.ToString();
        }

        /// <summary>格式化带正负号的百分比。</summary>
        private static string FormatSignedPercent(float value)
        {
            int percent = Mathf.RoundToInt(value * 100f);
            return percent > 0 ? $"+{percent}%" : $"{percent}%";
        }

        /// <summary>格式化秒数，整数秒不显示小数。</summary>
        private static string FormatSeconds(float seconds)
        {
            return Mathf.Approximately(seconds, Mathf.Round(seconds))
                ? $"{Mathf.RoundToInt(seconds)}秒"
                : $"{seconds:0.0}秒";
        }

        /// <summary>选择根 Canvas 作为定位边界，避免被背包面板自身坐标系挤到远离鼠标的位置。</summary>
        private RectTransform ResolvePositionBounds()
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.rootCanvas != null && canvas.rootCanvas.transform is RectTransform canvasRect)
            {
                return canvasRect;
            }

            return transform.parent as RectTransform;
        }

        /// <summary>按边界矩形夹紧面板左上角坐标，避免悬浮面板超出屏幕。</summary>
        private Vector2 ClampToBounds(RectTransform bounds, Vector2 targetPosition)
        {
            Vector2 size = m_rectTransform.rect.size;
            Rect boundsRect = bounds.rect;
            float x = Mathf.Clamp(targetPosition.x, boundsRect.xMin, boundsRect.xMax - size.x);
            float y = Mathf.Clamp(targetPosition.y, boundsRect.yMin + size.y, boundsRect.yMax);
            return new Vector2(x, y);
        }

        /// <summary>根据 Canvas 模式选择指针坐标转换使用的相机。</summary>
        private Camera ResolveEventCamera(PointerEventData eventData)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            if (eventData.enterEventCamera != null)
            {
                return eventData.enterEventCamera;
            }

            if (eventData.pressEventCamera != null)
            {
                return eventData.pressEventCamera;
            }

            return canvas == null ? null : canvas.worldCamera;
        }
    }
}
