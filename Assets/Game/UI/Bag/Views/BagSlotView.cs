using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 单个背包/装备槽的 UI 视图。
    /// 它只负责显示和转发拖拽事件，真正的数据移动由 BagPanel 和 BagInventoryManager 处理。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class BagSlotView : MonoBehaviour,
        IBeginDragHandler,
        IDragHandler,
        IEndDragHandler,
        IDropHandler,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerMoveHandler
    {
        private const string PreservedConsumableTextName = "Text (TMP)";

        [SerializeField] private BagSlotType slotType = BagSlotType.Bag;
        [SerializeField] private int slotIndex;
        [SerializeField] private Image backgroundImage;
        [SerializeField] private Image defaultIconImage;
        [SerializeField] private Image itemIconImage;
        [SerializeField] private TextMeshProUGUI itemNameText;
        [SerializeField] private TextMeshProUGUI countText;

        private BagPanel m_owner;
        private BagInventoryManager m_inventory;
        private CanvasGroup m_canvasGroup;
        private string m_emptyText;
        private BagItemData m_displayedBagItem;
        private Color m_defaultIconVisibleColor;
        private bool m_hasDefaultIconVisibleColor;

        public BagSlotType SlotType => slotType;
        public int SlotIndex => slotIndex;
        public BagItemData CurrentItem => GetCurrentItem();

        /// <summary>
        /// 初始化槽位显示组件，确保 prefab 绑定和运行时兜底节点都可用。
        /// </summary>
        private void Awake()
        {
            EnsureVisuals();
        }

        /// <summary>
        /// 绑定槽位所属数据地址。背包格显示哪件物品由当前分类页刷新时另行注入。
        /// </summary>
        public void Bind(BagPanel owner, BagInventoryManager inventory, BagSlotType type, int index, string emptyText)
        {
            m_owner = owner;
            m_inventory = inventory;
            slotType = type;
            slotIndex = index;
            m_emptyText = emptyText;
            if (slotType == BagSlotType.Bag)
            {
                m_displayedBagItem = null;
            }

            EnsureVisuals();
            Refresh();
        }

        /// <summary>
        /// 当前分类页刷新时，把指定物品显示到这个背包格。
        /// </summary>
        public void SetBagItem(BagItemData item)
        {
            if (slotType != BagSlotType.Bag)
            {
                return;
            }

            m_displayedBagItem = item;
            Refresh();
        }

        /// <summary>
        /// 清空普通背包格当前分类页缓存的显示物品，并刷新槽位表现。
        /// </summary>
        public void ClearItemView()
        {
            if (slotType == BagSlotType.Bag)
            {
                m_displayedBagItem = null;
            }

            Refresh();
        }

        /// <summary>
        /// 根据当前显示物品刷新图标、文字和数量。
        /// </summary>
        public void Refresh()
        {
            EnsureVisuals();
            BagItemData item = GetCurrentItem();
            bool hasItem = item != null;
            bool hasIcon = hasItem && item.Icon != null;
            bool showDefaultIcon = IsPlayerEquipmentSlot() && !hasItem;

            SetDefaultIconVisible(showDefaultIcon);

            if (itemIconImage != null)
            {
                itemIconImage.sprite = hasIcon ? item.Icon : null;
                itemIconImage.gameObject.SetActive(hasIcon);
            }

            if (itemNameText != null && !ShouldPreserveItemNameText())
            {
                itemNameText.text = !hasItem ? (showDefaultIcon ? string.Empty : m_emptyText) : (hasIcon ? string.Empty : item.Name);
                itemNameText.color = hasItem ? UIElementFactory.TextColor : UIElementFactory.MutedTextColor;
            }

            if (countText != null)
            {
                countText.text = hasItem && item.Count > 1 ? item.Count.ToString() : string.Empty;
            }

        }

        /// <summary>
        /// 拖拽过程中降低源槽透明度，让玩家能看出被拿起的是哪一个格子。
        /// </summary>
        public void SetDragging(bool dragging)
        {
            EnsureVisuals();
            if (m_canvasGroup != null)
            {
                m_canvasGroup.alpha = dragging ? 0.45f : 1f;
            }
        }

        /// <summary>
        /// 开始拖拽时把当前槽位交给背包面板解析拖拽源。
        /// </summary>
        public void OnBeginDrag(PointerEventData eventData)
        {
            if (m_owner == null || !m_owner.BeginSlotDrag(this, eventData))
            {
                eventData.pointerDrag = null;
            }
        }

        /// <summary>
        /// 拖拽移动时把指针事件转发给背包面板更新悬浮层。
        /// </summary>
        public void OnDrag(PointerEventData eventData)
        {
            if (m_owner != null)
            {
                m_owner.UpdateSlotDrag(this, eventData);
            }
        }

        /// <summary>
        /// 结束拖拽时通知背包面板完成放置或回滚显示。
        /// </summary>
        public void OnEndDrag(PointerEventData eventData)
        {
            if (m_owner != null)
            {
                m_owner.EndSlotDrag(this, eventData);
            }
        }

        /// <summary>
        /// 槽位接收拖拽落点时通知背包面板执行移动规则。
        /// </summary>
        public void OnDrop(PointerEventData eventData)
        {
            if (m_owner != null)
            {
                m_owner.DropSlotDrag(this, eventData);
            }
        }

        /// <summary>
        /// 鼠标进入槽位时请求背包面板显示当前物品详情。
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (m_owner != null)
            {
                m_owner.ShowSlotDetail(this, eventData);
            }
        }

        /// <summary>
        /// 鼠标离开槽位时请求背包面板隐藏详情。
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            if (m_owner != null)
            {
                m_owner.HideSlotDetail(this);
            }
        }

        /// <summary>
        /// 鼠标在槽位上移动时同步详情面板跟随位置。
        /// </summary>
        public void OnPointerMove(PointerEventData eventData)
        {
            if (m_owner != null)
            {
                m_owner.MoveSlotDetail(this, eventData);
            }
        }

        /// <summary>
        /// 确保槽位有基础显示组件。这样 BagSlot.prefab 和运行时兜底格子都能复用同一脚本。
        /// </summary>
        public void EnsureVisuals()
        {
            if (backgroundImage == null)
            {
                Transform bgChild = transform.Find("Bg");
                backgroundImage = bgChild == null ? null : bgChild.GetComponent<Image>();
            }

            if (backgroundImage == null)
            {
                backgroundImage = GetComponent<Image>();
            }

            if (backgroundImage == null)
            {
                backgroundImage = gameObject.AddComponent<Image>();
            }

            backgroundImage.raycastTarget = true;

            if (m_canvasGroup == null)
            {
                m_canvasGroup = GetComponent<CanvasGroup>();
                if (m_canvasGroup == null)
                {
                    m_canvasGroup = gameObject.AddComponent<CanvasGroup>();
                }
            }

            EnsureDefaultIconImage();
            EnsureItemIconImage();
            EnsureNameText();
            EnsureCountText();
        }

        /// <summary>
        /// 读取当前槽位实际显示的物品，普通背包格使用当前分类页缓存，其它槽位直接读功能层。
        /// </summary>
        private BagItemData GetCurrentItem()
        {
            if (slotType == BagSlotType.Bag)
            {
                return m_displayedBagItem;
            }

            return m_inventory == null ? null : m_inventory.GetItem(slotType, slotIndex);
        }

        /// <summary>
        /// 使用 prefab 已绑定的 DefaultSprite 默认占位图标，空槽显示它，装备后隐藏它。
        /// </summary>
        private void EnsureDefaultIconImage()
        {
            CacheDefaultIconColor();
        }

        /// <summary>
        /// 记录默认装备图标原始颜色，重新显示时恢复 prefab 美术设定。
        /// </summary>
        private void CacheDefaultIconColor()
        {
            if (defaultIconImage != null && !m_hasDefaultIconVisibleColor)
            {
                m_defaultIconVisibleColor = defaultIconImage.color;
                m_hasDefaultIconVisibleColor = true;
            }
        }

        /// <summary>
        /// 切换默认装备图标显隐；只启停 DefaultSprite 子物体，装备槽自身背景继续接收拖拽射线。
        /// </summary>
        private void SetDefaultIconVisible(bool visible)
        {
            if (defaultIconImage == null)
            {
                return;
            }

            CacheDefaultIconColor();
            if (visible)
            {
                defaultIconImage.color = m_defaultIconVisibleColor;
            }

            defaultIconImage.gameObject.SetActive(visible);
        }

        /// <summary>
        /// 判断当前槽位是否属于玩家装备栏，消耗品快捷栏不使用装备默认图标规则。
        /// </summary>
        private bool IsPlayerEquipmentSlot()
        {
            return slotType != BagSlotType.Bag && slotType != BagSlotType.Consumable;
        }

        /// <summary>
        /// 查找或创建物品图标节点，物品图标独立于默认装备占位图显示。
        /// </summary>
        private void EnsureItemIconImage()
        {
            if (itemIconImage != null && itemIconImage == defaultIconImage)
            {
                itemIconImage = null;
            }

            if (itemIconImage == null)
            {
                Transform child = transform.Find("ItemIcon");
                itemIconImage = child == null ? null : child.GetComponent<Image>();
            }

            if (itemIconImage == null)
            {
                Image image = UIElementFactory.CreateImage("ItemIcon", transform, Color.white);
                itemIconImage = image;
                UIElementFactory.Stretch(itemIconImage.rectTransform);
                itemIconImage.rectTransform.offsetMin = new Vector2(8f, 8f);
                itemIconImage.rectTransform.offsetMax = new Vector2(-8f, -8f);
            }

            itemIconImage.raycastTarget = false;
            itemIconImage.preserveAspect = true;
            itemIconImage.gameObject.SetActive(itemIconImage.sprite != null);
        }

        /// <summary>
        /// 查找或创建物品名称文本；消耗品快捷栏的 Text (TMP) 属于槽位自身显示，不能接管。
        /// </summary>
        private void EnsureNameText()
        {
            if (ShouldPreserveItemNameText())
            {
                return;
            }

            if (itemNameText == null)
            {
                Transform child = transform.Find("ItemName");
                itemNameText = child == null ? null : child.GetComponent<TextMeshProUGUI>();
            }

            if (itemNameText == null && !IsConsumableEquipmentSlot())
            {
                Transform child = transform.Find(PreservedConsumableTextName);
                itemNameText = child == null ? null : child.GetComponent<TextMeshProUGUI>();
            }

            if (itemNameText == null && !IsConsumableEquipmentSlot())
            {
                itemNameText = UIElementFactory.CreateText(
                    "ItemName",
                    transform,
                    string.Empty,
                    18,
                    TextAlignmentOptions.Center,
                    UIElementFactory.MutedTextColor);
                UIElementFactory.Stretch(itemNameText.rectTransform);
                itemNameText.rectTransform.offsetMin = new Vector2(4f, 4f);
                itemNameText.rectTransform.offsetMax = new Vector2(-4f, -4f);
            }

            if (itemNameText == null)
            {
                return;
            }

            itemNameText.raycastTarget = false;
            itemNameText.enableWordWrapping = true;
        }

        /// <summary>
        /// 判断当前槽位是否是消耗品快捷栏槽位。
        /// </summary>
        private bool IsConsumableEquipmentSlot()
        {
            return slotType == BagSlotType.Consumable;
        }

        /// <summary>
        /// 判断当前名称文本是否是消耗品槽自带文本；该文本由 prefab 自身控制，背包物品名逻辑不能修改。
        /// </summary>
        private bool ShouldPreserveItemNameText()
        {
            return IsConsumableEquipmentSlot()
                   && itemNameText != null
                   && itemNameText.gameObject.name == PreservedConsumableTextName;
        }

        /// <summary>
        /// 查找或创建物品数量文本。
        /// </summary>
        private void EnsureCountText()
        {
            if (countText == null)
            {
                Transform child = transform.Find("Count");
                countText = child == null ? null : child.GetComponent<TextMeshProUGUI>();
            }

            if (countText == null)
            {
                countText = UIElementFactory.CreateText(
                    "Count",
                    transform,
                    string.Empty,
                    16,
                    TextAlignmentOptions.BottomRight,
                    UIElementFactory.TextColor);
                UIElementFactory.Stretch(countText.rectTransform);
                countText.rectTransform.offsetMin = new Vector2(4f, 4f);
                countText.rectTransform.offsetMax = new Vector2(-6f, -4f);
            }

            countText.raycastTarget = false;
            countText.enableWordWrapping = false;
        }
    }
}
