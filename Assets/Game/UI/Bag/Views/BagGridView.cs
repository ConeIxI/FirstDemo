using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 背包普通格视图，负责分类页签、格子创建和当前分类刷新。
    /// </summary>
    public sealed class BagGridView : UIViewBase
    {
        private readonly BagPanel m_owner;
        private readonly RectTransform m_bagGrid;
        private readonly GridLayoutGroup m_bagGridLayout;
        private readonly BagSlotView m_bagSlotPrefab;
        private readonly Toggle[] m_toggles;
        private readonly int m_bagCapacity;
        private readonly int m_bagColumnCount;
        private readonly List<BagSlotView> m_bagSlots = new List<BagSlotView>();
        private readonly Dictionary<Toggle, UnityAction<bool>> m_toggleHandlers =
            new Dictionary<Toggle, UnityAction<bool>>();

        private BagInventoryManager m_inventory;
        private BagItemType m_currentCategory = BagItemType.Weapon;

        public BagItemType CurrentCategory => m_currentCategory;
        public RectTransform BagGrid => m_bagGrid;

        /// <summary>
        /// 创建背包格视图，并保存 prefab 引用和容量配置。
        /// </summary>
        public BagGridView(
            BagPanel owner,
            BagInventoryManager inventory,
            RectTransform bagGrid,
            GridLayoutGroup bagGridLayout,
            BagSlotView bagSlotPrefab,
            Toggle[] toggles,
            int bagCapacity,
            int bagColumnCount)
        {
            m_owner = owner;
            m_inventory = inventory;
            m_bagGrid = bagGrid;
            m_bagGridLayout = bagGridLayout;
            m_bagSlotPrefab = bagSlotPrefab;
            m_toggles = toggles;
            m_bagCapacity = bagCapacity;
            m_bagColumnCount = bagColumnCount;
        }

        /// <summary>
        /// 更新背包数据门面引用，供重复打开面板时复用同一视图对象。
        /// </summary>
        public void SetInventory(BagInventoryManager inventory)
        {
            m_inventory = inventory;
        }

        /// <summary>
        /// 初始化背包格布局约束。
        /// </summary>
        public override void Init()
        {
            ConfigureBagGridLayout();
        }

        /// <summary>
        /// 显示背包格视图，绑定分类页签并确保格子已经创建。
        /// </summary>
        public override void Show()
        {
            ConfigureBagGridLayout();
            EnsureBagSlots();
            BindButtons();
            ApplyCurrentCategorySelection();
            Refresh();
        }

        /// <summary>
        /// 隐藏背包格视图时解绑页签事件，格子对象保留以便下次打开复用。
        /// </summary>
        public override void Hide()
        {
            UnbindButtons();
        }

        /// <summary>
        /// 释放背包格视图持有的页签事件订阅。
        /// </summary>
        public override void Dispose()
        {
            UnbindButtons();
        }

        /// <summary>
        /// 按当前分类刷新所有背包格。
        /// </summary>
        public void Refresh()
        {
            Refresh(m_currentCategory);
        }

        /// <summary>
        /// 按指定分类刷新背包格，先清空再按 BagIndex 回填物品。
        /// </summary>
        public void Refresh(BagItemType category)
        {
            for (int i = 0; i < m_bagSlots.Count; i++)
            {
                BagSlotView slot = m_bagSlots[i];
                if (slot == null)
                {
                    continue;
                }

                bool visible = i < m_bagCapacity;
                slot.gameObject.SetActive(visible);
                if (visible)
                {
                    slot.ClearItemView();
                }
            }

            if (m_inventory == null)
            {
                return;
            }

            IReadOnlyList<BagItemData> currentItems = m_inventory.GetItems(category);
            for (int i = 0; i < currentItems.Count; i++)
            {
                BagItemData item = currentItems[i];
                if (item == null || item.BagIndex < 0 || item.BagIndex >= m_bagSlots.Count)
                {
                    continue;
                }

                BagSlotView slot = m_bagSlots[item.BagIndex];
                if (slot != null && item.BagIndex < m_bagCapacity)
                {
                    slot.SetBagItem(item);
                }
            }
        }

        /// <summary>
        /// 设置当前分类页，必要时同步 Toggle 状态并刷新背包格。
        /// </summary>
        public void SetCurrentCategory(BagItemType category, bool updateToggle, bool refresh)
        {
            if (category == BagItemType.None)
            {
                return;
            }

            m_currentCategory = category;
            if (updateToggle && m_toggles != null)
            {
                HashSet<BagItemType> selectedCategories = new HashSet<BagItemType>();
                for (int i = 0; i < m_toggles.Length; i++)
                {
                    Toggle toggle = m_toggles[i];
                    if (toggle == null || IsAllToggle(toggle))
                    {
                        continue;
                    }

                    bool matches = TryResolveToggleFilter(toggle, out BagItemType toggleType)
                                   && toggleType == category
                                   && toggle.gameObject.activeSelf
                                   && selectedCategories.Add(toggleType);
                    toggle.SetIsOnWithoutNotify(matches);
                }
            }

            if (refresh)
            {
                Refresh();
            }
        }

        /// <summary>
        /// 根据 prefab 上的 Toggle 当前状态选中分类，没有选中时回退到第一个可用分类。
        /// </summary>
        public void ApplyCurrentCategorySelection()
        {
            if (TryGetSelectedCategory(out BagItemType selectedType))
            {
                SetCurrentCategory(selectedType, false, false);
                return;
            }

            if (TryGetFirstCategory(out BagItemType firstType))
            {
                SetCurrentCategory(firstType, true, false);
                return;
            }

            m_currentCategory = BagItemType.Weapon;
        }

        /// <summary>
        /// 固定背包格列数，格子尺寸继续由 GridLayoutGroup.cellSize 控制。
        /// </summary>
        private void ConfigureBagGridLayout()
        {
            if (m_bagGridLayout == null)
            {
                return;
            }

            m_bagGridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            m_bagGridLayout.constraintCount = Mathf.Max(1, m_bagColumnCount);
        }

        /// <summary>
        /// 按容量创建普通背包格，已创建的格子会复用。
        /// </summary>
        private void EnsureBagSlots()
        {
            if (m_bagGrid == null)
            {
                Debug.LogWarning("BagPanel 缺少 bag/grid 容器，无法生成背包格子。");
                return;
            }

            while (m_bagSlots.Count < m_bagCapacity)
            {
                BagSlotView slot = CreateBagSlot(m_bagSlots.Count);
                m_bagSlots.Add(slot);
            }

            for (int i = 0; i < m_bagSlots.Count; i++)
            {
                if (m_bagSlots[i] != null && i < m_bagCapacity)
                {
                    m_bagSlots[i].Bind(m_owner, m_inventory, BagSlotType.Bag, i, string.Empty);
                }
            }
        }

        /// <summary>
        /// 创建单个背包格，优先使用 prefab，缺失时创建运行时兜底格。
        /// </summary>
        private BagSlotView CreateBagSlot(int index)
        {
            BagSlotView slot;
            if (m_bagSlotPrefab != null)
            {
                slot = Object.Instantiate(m_bagSlotPrefab, m_bagGrid);
            }
            else
            {
                slot = CreateRuntimeBagSlot();
            }

            slot.gameObject.name = $"BagSlot_{index:00}";
            slot.Bind(m_owner, m_inventory, BagSlotType.Bag, index, string.Empty);
            return slot;
        }

        /// <summary>
        /// 当 BagSlot prefab 引用缺失时创建基础可拖拽格子。
        /// </summary>
        private BagSlotView CreateRuntimeBagSlot()
        {
            RectTransform rect = UIElementFactory.CreateRect("BagSlot", m_bagGrid);
            rect.sizeDelta = m_bagGridLayout == null ? new Vector2(80f, 80f) : m_bagGridLayout.cellSize;
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.12f, 0.13f, 0.15f, 0.90f);
            BagSlotView view = rect.gameObject.AddComponent<BagSlotView>();
            view.EnsureVisuals();
            return view;
        }

        /// <summary>
        /// 绑定分类 Toggle，重复显示前会先解绑旧监听。
        /// </summary>
        private void BindButtons()
        {
            UnbindButtons();
            if (m_toggles == null)
            {
                return;
            }

            HashSet<BagItemType> visibleCategories = new HashSet<BagItemType>();
            for (int i = 0; i < m_toggles.Length; i++)
            {
                Toggle toggle = m_toggles[i];
                if (toggle == null)
                {
                    continue;
                }

                if (IsAllToggle(toggle))
                {
                    toggle.SetIsOnWithoutNotify(false);
                    toggle.interactable = false;
                    toggle.gameObject.SetActive(false);
                    continue;
                }

                if (!TryResolveToggleFilter(toggle, out BagItemType filterType))
                {
                    toggle.SetIsOnWithoutNotify(false);
                    toggle.interactable = false;
                    continue;
                }

                // 道具、物品等旧页签都会归一到 Consumable，这里只保留第一个分类入口。
                if (!visibleCategories.Add(filterType))
                {
                    toggle.SetIsOnWithoutNotify(false);
                    toggle.interactable = false;
                    toggle.gameObject.SetActive(false);
                    continue;
                }

                toggle.gameObject.SetActive(true);
                toggle.interactable = true;

                Toggle capturedToggle = toggle;
                UnityAction<bool> handler = isOn => OnChangedHandler(capturedToggle, isOn);
                m_toggleHandlers.Add(toggle, handler);
                toggle.onValueChanged.AddListener(handler);
            }
        }

        /// <summary>
        /// 解绑分类 Toggle 监听，避免重复打开面板导致事件叠加。
        /// </summary>
        private void UnbindButtons()
        {
            foreach (KeyValuePair<Toggle, UnityAction<bool>> pair in m_toggleHandlers)
            {
                if (pair.Key != null)
                {
                    pair.Key.onValueChanged.RemoveListener(pair.Value);
                }
            }

            m_toggleHandlers.Clear();
        }

        /// <summary>
        /// 响应分类 Toggle 切换，并刷新当前分类背包格。
        /// </summary>
        private void OnChangedHandler(Toggle toggle, bool isOn)
        {
            if (!isOn || !TryResolveToggleFilter(toggle, out BagItemType filterType))
            {
                return;
            }

            SetCurrentCategory(filterType, false, true);
        }

        /// <summary>
        /// 读取当前已选中的分类 Toggle。
        /// </summary>
        private bool TryGetSelectedCategory(out BagItemType selectedType)
        {
            selectedType = BagItemType.None;
            if (m_toggles == null)
            {
                return false;
            }

            for (int i = 0; i < m_toggles.Length; i++)
            {
                Toggle toggle = m_toggles[i];
                if (toggle != null
                    && toggle.gameObject.activeSelf
                    && toggle.isOn
                    && TryResolveToggleFilter(toggle, out selectedType))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 读取第一个可用分类 Toggle，作为没有选中状态时的兜底分类。
        /// </summary>
        private bool TryGetFirstCategory(out BagItemType firstType)
        {
            firstType = BagItemType.None;
            if (m_toggles == null)
            {
                return false;
            }

            for (int i = 0; i < m_toggles.Length; i++)
            {
                Toggle toggle = m_toggles[i];
                if (toggle != null
                    && toggle.gameObject.activeSelf
                    && TryResolveToggleFilter(toggle, out firstType))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 根据 Toggle 文本解析背包分类，兼容当前 prefab 只靠文字区分页签的结构。
        /// </summary>
        private bool TryResolveToggleFilter(Toggle toggle, out BagItemType filterType)
        {
            filterType = BagItemType.None;
            string label = NormalizeLabel(GetToggleLabel(toggle));
            if (string.IsNullOrWhiteSpace(label) || IsAllLabel(label))
            {
                return false;
            }

            if (label.Contains("武器") || label.Contains("Weapon"))
            {
                filterType = BagItemType.Weapon;
                return true;
            }

            if (label.Contains("头盔") || label.Contains("头") || label.Contains("盔") || label.Contains("Helmet"))
            {
                filterType = BagItemType.Helmet;
                return true;
            }

            if (label.Contains("腿甲") || label.Contains("护腿") || label.Contains("腿") || label.Contains("Leg"))
            {
                filterType = BagItemType.Leggings;
                return true;
            }

            if (label.Contains("臂铠") || label.Contains("手套") || label.Contains("臂") || label.Contains("Glove"))
            {
                filterType = BagItemType.Gloves;
                return true;
            }

            if (label.Contains("胸甲") || label.Contains("护甲") || label.Contains("胸") || label.Contains("Armor"))
            {
                filterType = BagItemType.Armor;
                return true;
            }

            if (label.Contains("药") || label.Contains("道具") || label.Contains("消耗") || label.Contains("物品") || label.Contains("Item"))
            {
                filterType = BagItemType.Consumable;
                return true;
            }

            return false;
        }

        /// <summary>
        /// 判断 Toggle 是否是旧 prefab 里的“全部”页签。
        /// </summary>
        private bool IsAllToggle(Toggle toggle)
        {
            return toggle != null && IsAllLabel(GetToggleLabel(toggle));
        }

        /// <summary>
        /// 判断页签文本是否表达全部分类。
        /// </summary>
        private static bool IsAllLabel(string label)
        {
            string normalized = NormalizeLabel(label);
            return !string.IsNullOrWhiteSpace(normalized)
                   && (normalized.Contains("全部") || normalized.Contains("所有") || normalized.Contains("All"));
        }

        /// <summary>
        /// 读取 Toggle 子节点上的 TMP 文本，缺失时回退到对象名。
        /// </summary>
        private static string GetToggleLabel(Toggle toggle)
        {
            if (toggle == null)
            {
                return string.Empty;
            }

            TextMeshProUGUI[] texts = toggle.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i] != null && !string.IsNullOrWhiteSpace(texts[i].text))
                {
                    return texts[i].text;
                }
            }

            return toggle.name;
        }

        /// <summary>
        /// 兼容 prefab 文本被按 Latin1/ANSI 错读后出现的 UTF-8 乱码。
        /// </summary>
        private static string NormalizeLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label))
            {
                return string.Empty;
            }

            for (int i = 0; i < label.Length; i++)
            {
                if (label[i] > byte.MaxValue)
                {
                    return label;
                }
            }

            byte[] bytes = new byte[label.Length];
            for (int i = 0; i < label.Length; i++)
            {
                bytes[i] = (byte)label[i];
            }

            string decoded = Encoding.UTF8.GetString(bytes);
            return label + "|" + decoded;
        }
    }
}
