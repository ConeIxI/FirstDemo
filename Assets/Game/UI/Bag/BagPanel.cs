using System;
using System.Collections.Generic;
using Game.Character.Common;
using Game.Character.Equipment;
using GameMain2.Scripts.Character;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 背包面板负责组合背包子视图、连接背包功能层，并同步玩家装备外观。
    /// </summary>
    [UIPanel(UIType.Bag, UILayer.Normal, blockGameplayInput: true)]
    [UIShortcut(UnityEngine.KeyCode.B, SceneNames.BattleScene, false, true)]
    [UIShortcut(UnityEngine.KeyCode.B, SceneNames.BossScene, false, true)]
    public sealed class BagPanel : UIPanelBase
    {
        private const int WeaponSlotCount = 2;

        [SerializeField] private RectTransform bagGrid;
        [SerializeField] private GridLayoutGroup bagGridLayout;
        [SerializeField] private BagSlotView bagSlotPrefab;
        [SerializeField] private BagSlotView[] equipmentSlots;
        [SerializeField] private BagSlotView[] consumableSlots;
        [SerializeField] private int bagCapacity = 30;
        [SerializeField] private int bagColumnCount = 5;
        [SerializeField] private Toggle[] toggles;
        [SerializeField] private RectTransform dragFloatingLayer;
        [SerializeField] private Image dragFloatingImage;
        [SerializeField] private TextMeshProUGUI dragFloatingText;
        [SerializeField] private Vector2 dragFloatingOffset = Vector2.zero;
        [SerializeField] private EquipmentDetailPanel detailPanelPrefab;
        [SerializeField] private GameObject playerPreviewModel;

        private readonly Dictionary<EquipmentType, string> m_syncedPlayerEquipmentObjectNames =
            new Dictionary<EquipmentType, string>();
        private readonly Dictionary<EquipmentType, string> m_syncedPreviewEquipmentObjectNames =
            new Dictionary<EquipmentType, string>();
        private readonly Dictionary<int, string> m_syncedPlayerWeaponObjectNames =
            new Dictionary<int, string>();
        private readonly Dictionary<int, string> m_syncedPreviewWeaponObjectNames =
            new Dictionary<int, string>();

        private BagInventoryManager m_inventory;
        private EquipmentManager m_equipmentManager;
        private PlayerEquipmentAppearance m_playerPreviewAppearance;
        private BagGridView m_gridView;
        private BagEquipmentView m_equipmentView;
        private BagConsumableEquipView m_consumableEquipView;
        private BagDragController m_dragController;
        private EquipmentDetailPanel m_detailPanel;
        private BagSlotView m_detailSlot;
        private bool m_missingPlayerPreviewWarned;
        private bool m_missingEquipmentManagerWarned;

        /// <summary>
        /// 初始化面板缓存引用和子视图对象。
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            CacheControls();
            EnsureInventory();
            EnsureUiParts();
        }

        /// <summary>
        /// 打开背包面板时初始化背包逻辑、绑定子视图、刷新显示并同步装备外观。
        /// </summary>
        public override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            CacheControls();
            EnsureInventory();
            EnsureUiParts();

            m_inventory.Initialize(bagCapacity);
            m_inventory.InventoryChanged -= OnInventoryChanged;
            m_inventory.InventoryChanged += OnInventoryChanged;

            m_gridView.Show();
            m_equipmentView.Show();
            m_consumableEquipView.Show();
            EnsurePlayerPreview();
            RefreshInventoryViews();
            SyncEquipmentAppearanceFromSlots();
        }

        /// <summary>
        /// 关闭背包面板时取消拖拽、解绑事件并隐藏预览模型。
        /// </summary>
        public override void OnClose()
        {
            HideDetailPanel();
            if (m_dragController != null)
            {
                m_dragController.Hide();
            }

            if (m_gridView != null)
            {
                m_gridView.ResetCurrentCategory(BagItemType.Weapon, true, false);
                m_gridView.Hide();
            }

            if (m_equipmentView != null)
            {
                m_equipmentView.Hide();
            }

            if (m_consumableEquipView != null)
            {
                m_consumableEquipView.Hide();
            }

            if (m_inventory != null)
            {
                m_inventory.InventoryChanged -= OnInventoryChanged;
            }

            SetPlayerPreviewVisible(false);
            base.OnClose();
        }

        /// <summary>
        /// 销毁面板时释放子视图事件订阅和预览引用。
        /// </summary>
        private void OnDestroy()
        {
            if (m_dragController != null)
            {
                m_dragController.Dispose();
            }

            if (m_gridView != null)
            {
                m_gridView.Dispose();
            }

            if (m_equipmentView != null)
            {
                m_equipmentView.Dispose();
            }

            if (m_consumableEquipView != null)
            {
                m_consumableEquipView.Dispose();
            }

            if (m_inventory != null)
            {
                m_inventory.InventoryChanged -= OnInventoryChanged;
            }

            SetPlayerPreviewVisible(false);
            m_playerPreviewAppearance = null;
        }

        /// <summary>
        /// 背包功能层数据变化时刷新子视图，并同步装备外观。
        /// </summary>
        private void OnInventoryChanged()
        {
            HideDetailPanel();
            RefreshInventoryViews();
            SyncEquipmentAppearanceFromSlots();
        }

        /// <summary>
        /// 槽位发起拖拽时转交给拖拽控制器。
        /// </summary>
        public bool BeginSlotDrag(BagSlotView source, PointerEventData eventData)
        {
            HideDetailPanel();
            return m_dragController != null && m_dragController.BeginSlotDrag(source, eventData);
        }

        /// <summary>
        /// 槽位拖拽移动时转交给拖拽控制器。
        /// </summary>
        public void UpdateSlotDrag(BagSlotView source, PointerEventData eventData)
        {
            if (m_dragController != null)
            {
                m_dragController.UpdateSlotDrag(source, eventData);
            }
        }

        /// <summary>
        /// 槽位接收 Drop 时转交给拖拽控制器。
        /// </summary>
        public void DropSlotDrag(BagSlotView target, PointerEventData eventData)
        {
            if (m_dragController != null)
            {
                m_dragController.DropSlotDrag(target, eventData);
            }
        }

        /// <summary>
        /// 槽位结束拖拽时转交给拖拽控制器。
        /// </summary>
        public void EndSlotDrag(BagSlotView source, PointerEventData eventData)
        {
            if (m_dragController != null)
            {
                m_dragController.EndSlotDrag(source, eventData);
            }
        }

        /// <summary>
        /// 槽位悬停时显示当前物品详情，拖拽中不显示详情避免遮挡操作。
        /// </summary>
        public void ShowSlotDetail(BagSlotView slot, PointerEventData eventData)
        {
            if (slot == null || IsSlotDetailBlocked())
            {
                HideDetailPanel();
                return;
            }

            BagItemData item = slot.CurrentItem;
            if (item == null)
            {
                HideDetailPanel();
                return;
            }

            EnsureDetailPanel();
            if (m_detailPanel == null)
            {
                return;
            }

            m_detailSlot = slot;
            m_detailPanel.Show(item, ResolveEquippedComparisonItem(item), eventData);
        }

        /// <summary>
        /// 鼠标在槽位内移动时同步详情面板位置。
        /// </summary>
        public void MoveSlotDetail(BagSlotView slot, PointerEventData eventData)
        {
            if (m_detailPanel != null && m_detailSlot == slot && !IsSlotDetailBlocked())
            {
                m_detailPanel.UpdatePosition(eventData);
            }
        }

        /// <summary>
        /// 鼠标离开当前详情槽位时隐藏详情面板。
        /// </summary>
        public void HideSlotDetail(BagSlotView slot)
        {
            if (m_detailSlot == slot)
            {
                HideDetailPanel();
            }
        }

        /// <summary>
        /// 鼠标经过槽位时清除当前物品的新获得格子红点。
        /// </summary>
        public void ClearSlotNewMark(BagSlotView slot)
        {
            if (slot != null && m_inventory != null)
            {
                m_inventory.ClearNewItem(slot.CurrentItem);
            }
        }

        /// <summary>
        /// 响应关闭按钮点击，关闭背包面板。
        /// </summary>
        public void OnClickedCloseBtn()
        {
            UIManager.Instance.ClosePanel(UIType.Bag);
        }

        /// <summary>
        /// 兼容 prefab 引用丢失的情况，优先读序列化字段，缺失时按当前层级名称查找。
        /// </summary>
        private void CacheControls()
        {
            if (bagGrid == null)
            {
                bagGrid = transform.Find("bag/grid") as RectTransform;
            }

            if (bagGridLayout == null && bagGrid != null)
            {
                bagGridLayout = bagGrid.GetComponent<GridLayoutGroup>();
            }

            if (toggles == null || toggles.Length == 0)
            {
                Transform toggleRoot = transform.Find("bag/ButtonSlot");
                toggles = toggleRoot == null
                    ? GetComponentsInChildren<Toggle>(true)
                    : toggleRoot.GetComponentsInChildren<Toggle>(true);
            }

            if (equipmentSlots == null || equipmentSlots.Length == 0)
            {
                Transform charRoot = transform.Find("Char");
                equipmentSlots = charRoot == null ? new BagSlotView[0] : charRoot.GetComponentsInChildren<BagSlotView>(true);
            }

            if (consumableSlots == null || consumableSlots.Length == 0)
            {
                Transform consumableRoot = transform.Find("expendablePanel");
                consumableSlots = consumableRoot == null ? new BagSlotView[0] : consumableRoot.GetComponentsInChildren<BagSlotView>(true);
            }

            if (dragFloatingLayer == null)
            {
                dragFloatingLayer = transform.Find("DragFloatingLayer") as RectTransform;
            }

            if (dragFloatingImage == null && dragFloatingLayer != null)
            {
                dragFloatingImage = dragFloatingLayer.GetComponent<Image>();
            }

            if (dragFloatingText == null && dragFloatingLayer != null)
            {
                dragFloatingText = dragFloatingLayer.GetComponentInChildren<TextMeshProUGUI>(true);
            }
        }

        /// <summary>
        /// 绑定常驻背包数据管理器，确保 UI 打开前拾取的物品也能保留。
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
        /// 创建并初始化背包子视图和拖拽控制器，重复调用只更新数据引用。
        /// </summary>
        private void EnsureUiParts()
        {
            if (m_gridView == null)
            {
                m_gridView = new BagGridView(
                    this,
                    m_inventory,
                    bagGrid,
                    bagGridLayout,
                    bagSlotPrefab,
                    toggles,
                    bagCapacity,
                    bagColumnCount);
                m_gridView.Init();
            }
            else
            {
                m_gridView.SetInventory(m_inventory);
            }

            if (m_equipmentView == null)
            {
                m_equipmentView = new BagEquipmentView(this, m_inventory, equipmentSlots);
                m_equipmentView.Init();
            }
            else
            {
                m_equipmentView.SetInventory(m_inventory);
            }

            if (m_consumableEquipView == null)
            {
                m_consumableEquipView = new BagConsumableEquipView(this, m_inventory, consumableSlots);
                m_consumableEquipView.Init();
            }
            else
            {
                m_consumableEquipView.SetInventory(m_inventory);
            }

            if (m_dragController == null)
            {
                m_dragController = new BagDragController(
                    this,
                    m_inventory,
                    bagGrid,
                    dragFloatingLayer,
                    dragFloatingImage,
                    dragFloatingText,
                    dragFloatingOffset,
                    GetCurrentCategory,
                    SetCurrentCategory,
                    RefreshInventoryViews);
                m_dragController.Init();
            }
            else
            {
                m_dragController.SetInventory(m_inventory);
            }

            EnsureDetailPanel();
        }

        /// <summary>
        /// 确保装备详情面板实例存在，并保持初始隐藏。
        /// </summary>
        private void EnsureDetailPanel()
        {
            if (m_detailPanel != null)
            {
                return;
            }

            m_detailPanel = GetComponentInChildren<EquipmentDetailPanel>(true);
            if (m_detailPanel == null && detailPanelPrefab != null)
            {
                m_detailPanel = Instantiate(detailPanelPrefab, transform);
                m_detailPanel.gameObject.name = detailPanelPrefab.gameObject.name;
            }

            if (m_detailPanel != null)
            {
                m_detailPanel.Hide();
            }
        }

        /// <summary>
        /// 隐藏详情面板并清理当前悬停槽位缓存。
        /// </summary>
        private void HideDetailPanel()
        {
            m_detailSlot = null;
            if (m_detailPanel != null)
            {
                m_detailPanel.Hide();
            }
        }

        /// <summary>
        /// 根据悬停物品类型查找当前玩家已装备的同类物品，用于详情面板数值对比。
        /// </summary>
        private BagItemData ResolveEquippedComparisonItem(BagItemData item)
        {
            if (item == null || m_inventory == null)
            {
                return null;
            }

            if (item.ItemType == BagItemType.Weapon)
            {
                return ResolveEquippedWeaponComparisonItem();
            }

            BagSlotType slotType = GetEquipmentSlotType(item.ItemType);
            return slotType == BagSlotType.Bag ? null : m_inventory.GetItem(slotType, 0);
        }

        /// <summary>
        /// 查找当前激活武器槽中的物品，缺少激活槽时回退到第一个已装备武器。
        /// </summary>
        private BagItemData ResolveEquippedWeaponComparisonItem()
        {
            EnsureEquipmentManager();
            if (m_equipmentManager != null && m_equipmentManager.ActiveWeaponIndex >= 0)
            {
                BagItemData activeWeapon = m_inventory.GetItem(BagSlotType.Weapon, m_equipmentManager.ActiveWeaponIndex);
                if (activeWeapon != null)
                {
                    return activeWeapon;
                }
            }

            for (int i = 0; i < WeaponSlotCount; i++)
            {
                BagItemData weapon = m_inventory.GetItem(BagSlotType.Weapon, i);
                if (weapon != null)
                {
                    return weapon;
                }
            }

            return null;
        }

        /// <summary>
        /// 把背包物品类型映射到对应装备槽类型。
        /// </summary>
        private static BagSlotType GetEquipmentSlotType(BagItemType itemType)
        {
            switch (itemType)
            {
                case BagItemType.Helmet:
                    return BagSlotType.Helmet;
                case BagItemType.Armor:
                    return BagSlotType.Armor;
                case BagItemType.Leggings:
                    return BagSlotType.Leggings;
                case BagItemType.Gloves:
                    return BagSlotType.Gloves;
                default:
                    return BagSlotType.Bag;
            }
        }

        /// <summary>
        /// 判断详情面板是否应该被拖拽等高优先级交互屏蔽。
        /// </summary>
        private bool IsSlotDetailBlocked()
        {
            return m_dragController != null && m_dragController.IsDragging;
        }

        /// <summary>
        /// 刷新背包格和装备槽显示。
        /// </summary>
        private void RefreshInventoryViews()
        {
            if (m_gridView != null)
            {
                m_gridView.Refresh();
            }

            if (m_equipmentView != null)
            {
                m_equipmentView.Refresh();
            }

            if (m_consumableEquipView != null)
            {
                m_consumableEquipView.Refresh();
            }
        }

        /// <summary>
        /// 读取当前背包分类页，供拖拽控制器判断拖拽来源。
        /// </summary>
        private BagItemType GetCurrentCategory()
        {
            return m_gridView == null ? BagItemType.Weapon : m_gridView.CurrentCategory;
        }

        /// <summary>
        /// 设置当前背包分类页，供拖拽控制器在装备拖回背包时切页。
        /// </summary>
        private void SetCurrentCategory(BagItemType category, bool updateToggle, bool refresh)
        {
            if (m_gridView != null)
            {
                m_gridView.SetCurrentCategory(category, updateToggle, refresh);
            }
        }

        /// <summary>
        /// 确保背包人物预览模型存在并已绑定外观控制器。
        /// </summary>
        private void EnsurePlayerPreview()
        {
            if (playerPreviewModel == null)
            {
                playerPreviewModel = ResolvePlayerPreviewModel();
            }

            if (playerPreviewModel == null)
            {
                if (!m_missingPlayerPreviewWarned)
                {
                    Debug.LogWarning("BagPanel 未找到场景中的 PlayerPreview，背包人物预览暂时无法同步换装。");
                    m_missingPlayerPreviewWarned = true;
                }

                return;
            }

            if (m_playerPreviewAppearance == null)
            {
                m_playerPreviewAppearance = playerPreviewModel.GetComponent<PlayerEquipmentAppearance>();
                if (m_playerPreviewAppearance == null)
                {
                    m_playerPreviewAppearance = playerPreviewModel.AddComponent<PlayerEquipmentAppearance>();
                }
            }

            m_playerPreviewAppearance.Initialize();
            SetPlayerPreviewVisible(true);
        }

        /// <summary>
        /// 查找真实玩家装备管理器，避免把预览模型误当作真实角色。
        /// </summary>
        private void EnsureEquipmentManager()
        {
            if (m_equipmentManager != null)
            {
                return;
            }

            m_equipmentManager = ResolveRealPlayerEquipmentManager();
            if (m_equipmentManager == null && !m_missingEquipmentManagerWarned)
            {
                Debug.LogWarning("BagPanel 未找到真实 Player 的 EquipmentManager，装备外观暂时无法同步到真实角色。");
                m_missingEquipmentManagerWarned = true;
            }
        }

        /// <summary>
        /// 从背包装备槽数据同步真实玩家和预览模型的外观。
        /// </summary>
        private void SyncEquipmentAppearanceFromSlots()
        {
            if (m_inventory == null)
            {
                return;
            }

            EnsureEquipmentManager();
            EnsurePlayerPreview();
            SyncWeaponSlots();
            SyncEquipmentSlot(BagSlotType.Helmet, EquipmentType.Helmet);
            SyncEquipmentSlot(BagSlotType.Armor, EquipmentType.Armor);
            SyncEquipmentSlot(BagSlotType.Leggings, EquipmentType.Leggings);
            SyncEquipmentSlot(BagSlotType.Gloves, EquipmentType.Gloves);
            SyncPreviewSheathedWeapons();
        }

        /// <summary>
        /// 同步两个武器槽的外观。
        /// </summary>
        private void SyncWeaponSlots()
        {
            for (int i = 0; i < WeaponSlotCount; i++)
            {
                SyncWeaponSlot(i);
            }
        }

        /// <summary>
        /// 同步单个武器槽的外观和攻击力到真实玩家与预览模型。
        /// </summary>
        private void SyncWeaponSlot(int slotIndex)
        {
            BagItemData item = m_inventory.GetItem(BagSlotType.Weapon, slotIndex);
            string objectName = NormalizeEquipmentObjectName(item == null ? null : item.ObjectName);

            if (m_equipmentManager != null
                && !IsEquipmentSyncCurrent(m_syncedPlayerWeaponObjectNames, slotIndex, objectName))
            {
                if (string.IsNullOrWhiteSpace(objectName))
                {
                    m_equipmentManager.ClearWeaponSlot(slotIndex);
                }
                else
                {
                    m_equipmentManager.SetWeaponObject(slotIndex, objectName, item.AttackBonus);
                }

                MarkEquipmentSynced(m_syncedPlayerWeaponObjectNames, slotIndex, objectName);
            }

            if (m_playerPreviewAppearance != null
                && !IsEquipmentSyncCurrent(m_syncedPreviewWeaponObjectNames, slotIndex, objectName))
            {
                if (string.IsNullOrWhiteSpace(objectName))
                {
                    m_playerPreviewAppearance.ClearWeaponObject(slotIndex);
                }
                else
                {
                    m_playerPreviewAppearance.SetWeaponObject(slotIndex, objectName);
                }

                MarkEquipmentSynced(m_syncedPreviewWeaponObjectNames, slotIndex, objectName);
            }
        }

        /// <summary>
        /// 同步单个防具槽的外观和防御力到真实玩家与预览模型。
        /// </summary>
        private void SyncEquipmentSlot(BagSlotType slotType, EquipmentType equipmentType)
        {
            BagItemData item = m_inventory.GetItem(slotType, 0);
            string objectName = NormalizeEquipmentObjectName(item == null ? null : item.ObjectName);

            if (m_equipmentManager != null
                && !IsEquipmentSyncCurrent(m_syncedPlayerEquipmentObjectNames, equipmentType, objectName))
            {
                if (string.IsNullOrWhiteSpace(objectName))
                {
                    m_equipmentManager.ClearEquipment(equipmentType);
                }
                else
                {
                    m_equipmentManager.SetEquipmentObject(equipmentType, objectName, item.DefenseBonus);
                }

                MarkEquipmentSynced(m_syncedPlayerEquipmentObjectNames, equipmentType, objectName);
            }

            if (m_playerPreviewAppearance != null
                && !IsEquipmentSyncCurrent(m_syncedPreviewEquipmentObjectNames, equipmentType, objectName))
            {
                ApplyPreviewEquipment(equipmentType, objectName);
                MarkEquipmentSynced(m_syncedPreviewEquipmentObjectNames, equipmentType, objectName);
            }
        }

        /// <summary>
        /// 规范化装备外观对象名，空白字符串统一视为未装备。
        /// </summary>
        private static string NormalizeEquipmentObjectName(string objectName)
        {
            return string.IsNullOrWhiteSpace(objectName) ? null : objectName;
        }

        /// <summary>
        /// 判断防具外观是否已经同步到目标对象。
        /// </summary>
        private static bool IsEquipmentSyncCurrent(
            Dictionary<EquipmentType, string> syncedNames,
            EquipmentType equipmentType,
            string objectName)
        {
            return syncedNames.TryGetValue(equipmentType, out string syncedObjectName)
                   && string.Equals(syncedObjectName, objectName, StringComparison.Ordinal);
        }

        /// <summary>
        /// 记录防具外观已经同步到目标对象。
        /// </summary>
        private static void MarkEquipmentSynced(
            Dictionary<EquipmentType, string> syncedNames,
            EquipmentType equipmentType,
            string objectName)
        {
            syncedNames[equipmentType] = objectName;
        }

        /// <summary>
        /// 判断指定武器槽外观是否已经同步到目标对象。
        /// </summary>
        private static bool IsEquipmentSyncCurrent(
            Dictionary<int, string> syncedNames,
            int slotIndex,
            string objectName)
        {
            return syncedNames.TryGetValue(slotIndex, out string syncedObjectName)
                   && string.Equals(syncedObjectName, objectName, StringComparison.Ordinal);
        }

        /// <summary>
        /// 记录指定武器槽外观已经同步到目标对象。
        /// </summary>
        private static void MarkEquipmentSynced(
            Dictionary<int, string> syncedNames,
            int slotIndex,
            string objectName)
        {
            syncedNames[slotIndex] = objectName;
        }

        /// <summary>
        /// 应用防具外观到预览模型。
        /// </summary>
        private void ApplyPreviewEquipment(EquipmentType equipmentType, string objectName)
        {
            if (m_playerPreviewAppearance == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(objectName))
            {
                m_playerPreviewAppearance.ClearEquipment(equipmentType);
                return;
            }

            m_playerPreviewAppearance.SetEquipmentObject(equipmentType, objectName);
        }

        /// <summary>
        /// 同步预览模型武器显示，背包预览始终只显示收纳状态下的武器。
        /// </summary>
        private void SyncPreviewSheathedWeapons()
        {
            if (m_playerPreviewAppearance == null)
            {
                return;
            }

            m_playerPreviewAppearance.ShowAllWeaponsSheathed();
        }

        /// <summary>
        /// 控制背包人物预览模型显示状态。
        /// </summary>
        private void SetPlayerPreviewVisible(bool visible)
        {
            if (playerPreviewModel != null)
            {
                playerPreviewModel.SetActive(visible);
            }
        }

        /// <summary>
        /// 解析场景中的背包人物预览模型。
        /// </summary>
        private GameObject ResolvePlayerPreviewModel()
        {
            GameObject namedPreview = FindSceneObjectByName("PlayerPreview");
            if (namedPreview != null)
            {
                return namedPreview;
            }

            return FindPlayerPreviewCandidate();
        }

        /// <summary>
        /// 在活动和非活动场景对象中按名称查找对象。
        /// </summary>
        private static GameObject FindSceneObjectByName(string objectName)
        {
            if (string.IsNullOrWhiteSpace(objectName))
            {
                return null;
            }

            GameObject activeObject = GameObject.Find(objectName);
            if (activeObject != null)
            {
                return activeObject;
            }

            Transform[] transforms = FindObjectsOfType<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform current = transforms[i];
                if (current != null
                    && current.gameObject.scene.IsValid()
                    && current.name == objectName)
                {
                    return current.gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// 查找拥有装备挂点且不属于真实玩家的预览模型候选。
        /// </summary>
        private static GameObject FindPlayerPreviewCandidate()
        {
            Transform[] transforms = FindObjectsOfType<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                Transform current = transforms[i];
                if (current == null
                    || !current.gameObject.scene.IsValid()
                    || IsRealPlayer(current.gameObject)
                    || IsUnderRealPlayer(current))
                {
                    continue;
                }

                if (FindFirstChild(current, "Armos") != null || FindFirstChild(current, "Armors") != null)
                {
                    return current.gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// 判断对象是否是真实玩家根对象。
        /// </summary>
        private static bool IsRealPlayer(GameObject go)
        {
            if (go == null)
            {
                return false;
            }

            bool taggedPlayer;
            try
            {
                taggedPlayer = go.CompareTag("Player");
            }
            catch (UnityException)
            {
                taggedPlayer = false;
            }

            return taggedPlayer || go.name == "Player";
        }

        /// <summary>
        /// 查找真实玩家身上的装备管理器，过滤掉预览模型。
        /// </summary>
        private EquipmentManager ResolveRealPlayerEquipmentManager()
        {
            PlayerController[] controllers = FindObjectsOfType<PlayerController>(true);
            for (int i = 0; i < controllers.Length; i++)
            {
                PlayerController controller = controllers[i];
                if (controller == null || IsPreviewObject(controller.gameObject))
                {
                    continue;
                }

                EquipmentManager manager = controller.EquipmentManager != null
                    ? controller.EquipmentManager
                    : ResolveEquipmentManagerFrom(controller.gameObject);
                if (manager != null && !IsPreviewObject(manager.gameObject))
                {
                    return manager;
                }
            }

            EquipmentManager[] managers = FindObjectsOfType<EquipmentManager>(true);
            EquipmentManager fallback = null;
            for (int i = 0; i < managers.Length; i++)
            {
                EquipmentManager manager = managers[i];
                if (manager == null || IsPreviewObject(manager.gameObject))
                {
                    continue;
                }

                if (IsRealPlayer(manager.gameObject) || manager.GetComponent<PlayerController>() != null)
                {
                    return manager;
                }

                if (fallback == null)
                {
                    fallback = manager;
                }
            }

            return fallback;
        }

        /// <summary>
        /// 从指定角色对象的自身、子级或父级查找装备管理器。
        /// </summary>
        private static EquipmentManager ResolveEquipmentManagerFrom(GameObject root)
        {
            if (root == null)
            {
                return null;
            }

            EquipmentManager manager = root.GetComponent<EquipmentManager>();
            if (manager != null)
            {
                return manager;
            }

            manager = root.GetComponentInChildren<EquipmentManager>(true);
            if (manager != null)
            {
                return manager;
            }

            return root.GetComponentInParent<EquipmentManager>(true);
        }

        /// <summary>
        /// 判断对象是否属于背包预览模型。
        /// </summary>
        private bool IsPreviewObject(GameObject go)
        {
            if (go == null)
            {
                return false;
            }

            if (playerPreviewModel != null
                && (go == playerPreviewModel || go.transform.IsChildOf(playerPreviewModel.transform)))
            {
                return true;
            }

            Transform current = go.transform;
            while (current != null)
            {
                if (current.name == "PlayerPreview")
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        /// <summary>
        /// 判断某个 Transform 是否处于真实玩家层级下。
        /// </summary>
        private static bool IsUnderRealPlayer(Transform target)
        {
            Transform current = target;
            while (current != null)
            {
                if (IsRealPlayer(current.gameObject))
                {
                    return true;
                }

                current = current.parent;
            }

            return false;
        }

        /// <summary>
        /// 递归查找指定名称的第一个子节点。
        /// </summary>
        private static Transform FindFirstChild(Transform root, string childName)
        {
            if (root == null || string.IsNullOrWhiteSpace(childName))
            {
                return null;
            }

            if (root.name == childName)
            {
                return root;
            }

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindFirstChild(root.GetChild(i), childName);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
