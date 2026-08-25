using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 背包拖拽控制器，负责拖拽状态、悬浮图标、落点解析和调用背包功能层移动物品。
    /// </summary>
    public sealed class BagDragController : UIViewBase
    {
        private static readonly Vector2 DragFloatingSize = new Vector2(144f, 96f);
        private static Sprite s_dragFloatingFallbackSprite;

        private readonly BagPanel m_owner;
        private readonly RectTransform m_bagGrid;
        private readonly Vector2 m_dragFloatingOffset;
        private readonly Func<BagItemType> m_getCurrentCategory;
        private readonly Action<BagItemType, bool, bool> m_setCurrentCategory;
        private readonly Action m_refreshSlots;

        private BagInventoryManager m_inventory;
        private RectTransform m_dragFloatingLayer;
        private Image m_dragFloatingImage;
        private TextMeshProUGUI m_dragFloatingText;
        private BagSlotView m_dragSource;
        private bool m_dragDropHandled;

        /// <summary>返回当前是否正在拖拽背包槽位。</summary>
        public bool IsDragging => m_dragSource != null;

        /// <summary>
        /// 创建拖拽控制器，并保存拖拽 UI 和分类访问回调。
        /// </summary>
        public BagDragController(
            BagPanel owner,
            BagInventoryManager inventory,
            RectTransform bagGrid,
            RectTransform dragFloatingLayer,
            Image dragFloatingImage,
            TextMeshProUGUI dragFloatingText,
            Vector2 dragFloatingOffset,
            Func<BagItemType> getCurrentCategory,
            Action<BagItemType, bool, bool> setCurrentCategory,
            Action refreshSlots)
        {
            m_owner = owner;
            m_inventory = inventory;
            m_bagGrid = bagGrid;
            m_dragFloatingLayer = dragFloatingLayer;
            m_dragFloatingImage = dragFloatingImage;
            m_dragFloatingText = dragFloatingText;
            m_dragFloatingOffset = dragFloatingOffset;
            m_getCurrentCategory = getCurrentCategory;
            m_setCurrentCategory = setCurrentCategory;
            m_refreshSlots = refreshSlots;
        }

        /// <summary>
        /// 更新背包数据门面引用，供重复打开面板时继续使用同一控制器。
        /// </summary>
        public void SetInventory(BagInventoryManager inventory)
        {
            m_inventory = inventory;
        }

        /// <summary>
        /// 初始化拖拽浮层引用，缺失时会在首次拖拽时延迟创建。
        /// </summary>
        public override void Init()
        {
        }

        /// <summary>
        /// 隐藏拖拽控制器状态，关闭面板时取消仍在进行的拖拽。
        /// </summary>
        public override void Hide()
        {
            CancelActiveDrag();
        }

        /// <summary>
        /// 释放拖拽控制器运行时状态。
        /// </summary>
        public override void Dispose()
        {
            CancelActiveDrag();
        }

        /// <summary>
        /// 槽位发起拖拽时调用，只有当前显示了物品的槽位才能进入拖拽流程。
        /// </summary>
        public bool BeginSlotDrag(BagSlotView source, PointerEventData eventData)
        {
            if (source == null || m_inventory == null)
            {
                return false;
            }

            BagItemData item = source.CurrentItem;
            if (item == null)
            {
                return false;
            }

            if (source.SlotType == BagSlotType.Bag && item.ItemType != GetCurrentCategory())
            {
                return false;
            }

            m_dragSource = source;
            m_dragDropHandled = false;
            source.SetDragging(true);
            ShowDragFloatingItem(item, eventData);
            return true;
        }

        /// <summary>
        /// 拖拽移动时更新悬浮图标位置。
        /// </summary>
        public void UpdateSlotDrag(BagSlotView source, PointerEventData eventData)
        {
            if (m_dragSource == source)
            {
                UpdateDragFloatingLayer(eventData);
            }
        }

        /// <summary>
        /// 目标槽接收拖拽时解析移动规则，数据变化由背包功能层触发刷新。
        /// </summary>
        public void DropSlotDrag(BagSlotView target, PointerEventData eventData)
        {
            if (m_dragSource == null || target == null || m_inventory == null)
            {
                return;
            }

            m_dragDropHandled = true;
            if (target == m_dragSource)
            {
                return;
            }

            bool moved = TryDropOnSlot(target);
            if (!moved)
            {
                LogDropFailed(target);
                Debug.Log("背包拖拽目标不匹配，已取消放置。");
            }
        }

        /// <summary>
        /// 拖拽结束时处理没有触发 Drop 的落点，例如拖回背包空白区域。
        /// </summary>
        public void EndSlotDrag(BagSlotView source, PointerEventData eventData)
        {
            if (m_dragSource != source)
            {
                return;
            }

            if (!m_dragDropHandled
                && m_dragSource.SlotType == BagSlotType.Bag
                && TryResolveDropTarget(eventData, out BagSlotView target)
                && target != m_dragSource)
            {
                m_dragDropHandled = true;
                bool moved = TryDropOnSlot(target);
                if (!moved)
                {
                    LogDropFailed(target);
                }
            }

            if (!m_dragDropHandled
                && BagInventoryManager.IsEquipmentSlot(m_dragSource.SlotType)
                && IsPointerInsideBagGrid(eventData))
            {
                bool moved = MoveEquipmentToFirstEmptyGridSlot(m_dragSource);
                if (!moved)
                {
                    Debug.Log("目标分类页没有空背包格，装备拖回背包失败。");
                }
            }

            CancelActiveDrag();
            RefreshSlots();
        }

        /// <summary>
        /// 取消当前拖拽并隐藏悬浮图标。
        /// </summary>
        public void CancelActiveDrag()
        {
            if (m_dragSource != null)
            {
                m_dragSource.SetDragging(false);
            }

            m_dragSource = null;
            m_dragDropHandled = false;
            HideDragFloatingLayer();
        }

        /// <summary>
        /// 根据源槽和目标槽类型分派背包移动、装备或互换规则。
        /// </summary>
        private bool TryDropOnSlot(BagSlotView target)
        {
            if (m_dragSource.SlotType == BagSlotType.Bag)
            {
                if (target.SlotType == BagSlotType.Bag)
                {
                    return m_inventory.MoveBagItem(GetCurrentCategory(), m_dragSource.SlotIndex, target.SlotIndex);
                }

                return MoveBagItemToEquipmentSlot(target);
            }

            if (target.SlotType == BagSlotType.Bag)
            {
                return MoveEquipmentToBagSlot(target);
            }

            return m_inventory.MoveEquipmentSlot(
                m_dragSource.SlotType,
                m_dragSource.SlotIndex,
                target.SlotType,
                target.SlotIndex);
        }

        /// <summary>
        /// 把普通背包格物品移动到目标装备槽。
        /// </summary>
        private bool MoveBagItemToEquipmentSlot(BagSlotView target)
        {
            BagItemData item = m_dragSource.CurrentItem;
            if (item == null)
            {
                return false;
            }

            int sourceIndex = item.BagIndex >= 0 ? item.BagIndex : m_dragSource.SlotIndex;
            return m_inventory.MoveBagItemToEquipment(
                item.ItemType,
                sourceIndex,
                target.SlotType,
                target.SlotIndex);
        }

        /// <summary>
        /// 从指针射线结果中解析背包槽目标。
        /// </summary>
        private bool TryResolveDropTarget(PointerEventData eventData, out BagSlotView target)
        {
            target = null;
            GameObject raycastObject = eventData == null ? null : eventData.pointerCurrentRaycast.gameObject;
            if (raycastObject == null)
            {
                return false;
            }

            target = raycastObject.GetComponentInParent<BagSlotView>();
            return target != null;
        }

        /// <summary>
        /// 输出拖拽失败的关键源和目标信息，方便后续排查规则问题。
        /// </summary>
        private void LogDropFailed(BagSlotView target)
        {
            BagItemData item = m_dragSource == null ? null : m_dragSource.CurrentItem;
            string itemName = item == null ? "空" : $"{item.Name}({item.ItemType}, BagIndex={item.BagIndex})";
            string source = m_dragSource == null ? "空" : $"{m_dragSource.SlotType}[{m_dragSource.SlotIndex}]";
            string targetName = target == null ? "空" : $"{target.SlotType}[{target.SlotIndex}]";
            Debug.LogWarning($"背包拖拽放置失败：item={itemName}, source={source}, target={targetName}, currentCategory={GetCurrentCategory()}");
        }

        /// <summary>
        /// 把装备槽物品拖回普通背包格，跨分类时切到物品所属页并放入第一个空格。
        /// </summary>
        private bool MoveEquipmentToBagSlot(BagSlotView target)
        {
            BagItemData item = m_dragSource.CurrentItem;
            if (item == null)
            {
                return false;
            }

            if (item.ItemType == GetCurrentCategory())
            {
                return m_inventory.MoveEquipmentToBagSlot(
                    m_dragSource.SlotType,
                    m_dragSource.SlotIndex,
                    GetCurrentCategory(),
                    target.SlotIndex);
            }

            SetCurrentCategory(item.ItemType, true, false);
            bool moved = m_inventory.MoveEquipmentToFirstEmptyBagSlot(m_dragSource.SlotType, m_dragSource.SlotIndex);
            if (!moved)
            {
                RefreshSlots();
            }

            return moved;
        }

        /// <summary>
        /// 把装备槽物品拖回当前背包区域时，切到物品分类并放入第一个空格。
        /// </summary>
        private bool MoveEquipmentToFirstEmptyGridSlot(BagSlotView source)
        {
            BagItemData item = source.CurrentItem;
            if (item == null)
            {
                return false;
            }

            SetCurrentCategory(item.ItemType, true, false);
            bool moved = m_inventory.MoveEquipmentToFirstEmptyBagSlot(source.SlotType, source.SlotIndex);
            if (!moved)
            {
                RefreshSlots();
            }

            return moved;
        }

        /// <summary>
        /// 显示拖拽悬浮物品，优先使用物品图标，缺失时显示物品名称。
        /// </summary>
        private void ShowDragFloatingItem(BagItemData item, PointerEventData eventData)
        {
            if (item == null || m_dragFloatingLayer == null)
            {
                HideDragFloatingLayer();
                return;
            }

            bool hasSprite = item.Icon != null;
            m_dragFloatingLayer.SetAsLastSibling();
            m_dragFloatingLayer.gameObject.SetActive(true);

            if (m_dragFloatingImage != null)
            {
                m_dragFloatingImage.sprite = hasSprite ? item.Icon : GetDragFloatingFallbackSprite();
                m_dragFloatingImage.color = Color.white;
                m_dragFloatingImage.preserveAspect = true;
                m_dragFloatingImage.raycastTarget = false;
            }

            if (m_dragFloatingText != null)
            {
                m_dragFloatingText.text = hasSprite ? string.Empty : item.Name;
                m_dragFloatingText.color = Color.black;
                m_dragFloatingText.raycastTarget = false;
            }

            CanvasGroup canvasGroup = m_dragFloatingLayer.GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
                canvasGroup.alpha = 1f;
            }

            UpdateDragFloatingLayer(eventData);
        }


        
        /// <summary>
        /// 根据指针位置更新拖拽悬浮层世界坐标。
        /// </summary>
        private void UpdateDragFloatingLayer(PointerEventData eventData)
        {
            if (m_dragFloatingLayer == null || eventData == null)
            {
                return;
            }

            RectTransform parent = m_dragFloatingLayer.parent as RectTransform;
            if (parent == null)
            {
                return;
            }

            Camera eventCamera = GetDragFloatingEventCamera(eventData);
            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
                    parent,
                    eventData.position,
                    eventCamera,
                    out Vector3 worldPosition))
            {
                m_dragFloatingLayer.position = worldPosition;
                if (m_dragFloatingOffset != Vector2.zero)
                {
                    Vector3 localPosition = m_dragFloatingLayer.localPosition;
                    localPosition.x += m_dragFloatingOffset.x;
                    localPosition.y += m_dragFloatingOffset.y;
                    m_dragFloatingLayer.localPosition = localPosition;
                }
            }
        }

        /// <summary>
        /// 根据 Canvas 模式选择拖拽悬浮层坐标转换使用的相机。
        /// </summary>
        private Camera GetDragFloatingEventCamera(PointerEventData eventData)
        {
            Canvas canvas = m_dragFloatingLayer == null
                ? m_owner.GetComponentInParent<Canvas>()
                : m_dragFloatingLayer.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return null;
            }

            if (eventData != null && eventData.pressEventCamera != null)
            {
                return eventData.pressEventCamera;
            }

            return canvas == null ? null : canvas.worldCamera;
        }

        /// <summary>
        /// 隐藏拖拽悬浮层。
        /// </summary>
        private void HideDragFloatingLayer()
        {
            if (m_dragFloatingLayer != null)
            {
                m_dragFloatingLayer.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 创建拖拽浮层缺图标时使用的纯白兜底 Sprite。
        /// </summary>
        private static Sprite GetDragFloatingFallbackSprite()
        {
            if (s_dragFloatingFallbackSprite != null)
            {
                return s_dragFloatingFallbackSprite;
            }

            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.name = "BagDragFloatingFallbackTexture";
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();

            s_dragFloatingFallbackSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f));
            s_dragFloatingFallbackSprite.name = "BagDragFloatingFallbackSprite";
            s_dragFloatingFallbackSprite.hideFlags = HideFlags.HideAndDontSave;
            return s_dragFloatingFallbackSprite;
        }

        /// <summary>
        /// 判断拖拽结束点是否落在普通背包格区域内。
        /// </summary>
        private bool IsPointerInsideBagGrid(PointerEventData eventData)
        {
            return m_bagGrid != null
                   && eventData != null
                   && RectTransformUtility.RectangleContainsScreenPoint(
                       m_bagGrid,
                       eventData.position,
                       eventData.pressEventCamera);
        }

        /// <summary>
        /// 读取当前分类，回调缺失时回退到武器页。
        /// </summary>
        private BagItemType GetCurrentCategory()
        {
            return m_getCurrentCategory == null ? BagItemType.Weapon : m_getCurrentCategory();
        }

        /// <summary>
        /// 更新当前分类，回调缺失时不做额外处理。
        /// </summary>
        private void SetCurrentCategory(BagItemType category, bool updateToggle, bool refresh)
        {
            if (m_setCurrentCategory != null)
            {
                m_setCurrentCategory(category, updateToggle, refresh);
            }
        }

        /// <summary>
        /// 请求外部刷新背包和装备槽显示。
        /// </summary>
        private void RefreshSlots()
        {
            if (m_refreshSlots != null)
            {
                m_refreshSlots();
            }
        }
    }
}
