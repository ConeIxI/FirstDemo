using System;
using System.Collections.Generic;
using Game.Character.Common;
using UnityEngine;

namespace Game.Character.Equipment
{
    [DisallowMultipleComponent]
    public sealed class PlayerEquipmentAppearance : MonoBehaviour
    {
        private const int WeaponSlotCount = 2;

        [SerializeField] private GameObject m_ArmorRoot;
        [SerializeField] private GameObject m_CuirassRoot;
        [SerializeField] private GameObject m_HelmetRoot;
        [SerializeField] private GameObject m_LegRoot;
        [SerializeField] private PlayerWeaponAppearanceEntry[] m_weaponAppearances;

        private readonly Dictionary<EquipmentType, EquipmentModelGroup> m_groups =
            new Dictionary<EquipmentType, EquipmentModelGroup>();
        private readonly Dictionary<EquipmentType, string> m_currentObjectNames =
            new Dictionary<EquipmentType, string>();
        private readonly string[] m_weaponObjectNames = new string[WeaponSlotCount];
        private readonly WeaponData[] m_weaponDataBySlot = new WeaponData[WeaponSlotCount];
        private readonly GameObject[] m_weaponObjectsBySlot = new GameObject[WeaponSlotCount];
        private readonly PlayerWeaponAppearanceEntry[] m_activeEntries =
            new PlayerWeaponAppearanceEntry[WeaponSlotCount];

        private bool m_initialized;

        /// <summary>Unity 唤醒时缓存装备外观配置。</summary>
        private void Awake()
        {
            Initialize();
        }

        /// <summary>初始化防具模型组并隐藏所有武器槽表现。</summary>
        public void Initialize()
        {
            if (m_initialized)
            {
                return;
            }

            m_groups.Clear();
            m_currentObjectNames.Clear();

            CacheGroup(EquipmentType.Helmet, GetRootTransform(m_HelmetRoot), "Helmet00");
            CacheGroup(EquipmentType.Armor, GetRootTransform(m_CuirassRoot), "Cuirass00");
            CacheGroup(EquipmentType.Leggings, GetRootTransform(m_LegRoot), "Leg00");
            CacheGroup(EquipmentType.Gloves, GetRootTransform(m_ArmorRoot), "Armor00");
            HideAllWeaponAppearances();
            m_initialized = true;
        }

        /// <summary>设置指定防具类型的显示对象，找不到目标时回退到默认模型。</summary>
        public string SetEquipmentObject(EquipmentType type, string objectName)
        {
            Initialize();
            if (!m_groups.TryGetValue(type, out EquipmentModelGroup group))
            {
                Debug.LogWarning($"未找到 {type} 对应的装备模型组，无法切换到 {objectName}。");
                return null;
            }

            string targetName = string.IsNullOrWhiteSpace(objectName) ? group.DefaultObjectName : objectName;
            GameObject target = group.FindModel(targetName);
            if (target == null)
            {
                Debug.LogWarning($"{type} 装备模型 {targetName} 不存在，已恢复默认模型 {group.DefaultObjectName}。");
                targetName = group.DefaultObjectName;
                target = group.FindModel(targetName);
            }

            if (target != null
                && m_currentObjectNames.TryGetValue(type, out string currentObjectName)
                && string.Equals(currentObjectName, targetName, StringComparison.Ordinal)
                && group.IsActiveModel(target))
            {
                return targetName;
            }

            group.SetActiveOnly(target);
            if (target != null)
            {
                m_currentObjectNames[type] = targetName;
                return targetName;
            }

            m_currentObjectNames.Remove(type);
            return null;
        }

        /// <summary>清空指定防具类型并恢复该类型默认模型。</summary>
        public string ClearEquipment(EquipmentType type)
        {
            Initialize();
            if (!m_groups.TryGetValue(type, out EquipmentModelGroup group))
            {
                return null;
            }

            return SetEquipmentObject(type, group.DefaultObjectName);
        }

        /// <summary>读取指定防具类型当前显示的对象名。</summary>
        public string GetEquipmentObjectName(EquipmentType type)
        {
            Initialize();
            if (m_currentObjectNames.TryGetValue(type, out string objectName))
            {
                return objectName;
            }

            return m_groups.TryGetValue(type, out EquipmentModelGroup group) ? group.DefaultObjectName : null;
        }

        /// <summary>设置指定武器槽的逻辑对象，并默认显示为收纳表现。</summary>
        public string SetWeaponObject(int slotIndex, string objectName)
        {
            Initialize();
            if (!IsValidWeaponSlot(slotIndex))
            {
                Debug.LogWarning($"武器槽索引无效：{slotIndex}");
                return null;
            }

            if (string.IsNullOrWhiteSpace(objectName))
            {
                return ClearWeaponObject(slotIndex);
            }

            PlayerWeaponAppearanceEntry previousEntry = m_activeEntries[slotIndex];
            PlayerWeaponAppearanceEntry entry = FindWeaponEntry(objectName);
            if (entry == null)
            {
                Debug.LogWarning($"未找到完整武器表现 {objectName}，已清空武器槽 {slotIndex}。");
                ClearWeaponRuntimeSlot(slotIndex);
                HideEntryIfUnused(previousEntry);
                return null;
            }

            m_weaponObjectNames[slotIndex] = objectName;
            m_weaponDataBySlot[slotIndex] = entry.WeaponData;
            m_weaponObjectsBySlot[slotIndex] = entry.HandObject;
            m_activeEntries[slotIndex] = entry;
            HideEntryIfUnused(previousEntry);
            entry.ShowSheathed();
            return objectName;
        }

        /// <summary>清空指定武器槽并隐藏该槽全部手持和收纳对象。</summary>
        public string ClearWeaponObject(int slotIndex)
        {
            Initialize();
            if (!IsValidWeaponSlot(slotIndex))
            {
                Debug.LogWarning($"武器槽索引无效：{slotIndex}");
                return null;
            }

            PlayerWeaponAppearanceEntry previousEntry = m_activeEntries[slotIndex];
            ClearWeaponRuntimeSlot(slotIndex);
            HideEntryIfUnused(previousEntry);
            return null;
        }

        /// <summary>兼容旧装备管理入口，把指定槽切到手持，其余已装备槽切到收纳。</summary>
        public bool SetActiveWeaponIndex(int slotIndex)
        {
            Initialize();
            if (slotIndex < 0)
            {
                HideAllWeaponAppearances();
                return true;
            }

            if (!IsValidWeaponSlot(slotIndex))
            {
                Debug.LogWarning($"武器槽索引无效：{slotIndex}");
                return false;
            }

            if (m_activeEntries[slotIndex] == null)
            {
                return false;
            }

            ApplyCombatAppearance(slotIndex, true);
            return true;
        }

        /// <summary>按当前战斗姿态统一刷新两个武器槽的最终表现。</summary>
        public void ApplyCombatAppearance(int activeSlotIndex, bool isCombat)
        {
            Initialize();
            HideAllWeaponAppearances();
            HashSet<PlayerWeaponAppearanceEntry> shownEntries = new HashSet<PlayerWeaponAppearanceEntry>();
            PlayerWeaponAppearanceEntry activeEntry = isCombat && IsValidWeaponSlot(activeSlotIndex)
                ? m_activeEntries[activeSlotIndex]
                : null;

            for (int i = 0; i < WeaponSlotCount; i++)
            {
                PlayerWeaponAppearanceEntry entry = m_activeEntries[i];
                if (entry == null)
                {
                    continue;
                }

                if (entry == activeEntry)
                {
                    entry.ShowInHand();
                    shownEntries.Add(entry);
                }
                else if (shownEntries.Add(entry))
                {
                    entry.ShowSheathed();
                }
            }
        }

        /// <summary>把全部已装备武器统一切到收纳表现，供背包预览模型使用。</summary>
        public void ShowAllWeaponsSheathed()
        {
            Initialize();
            HideAllWeaponAppearances();
            HashSet<PlayerWeaponAppearanceEntry> shownEntries = new HashSet<PlayerWeaponAppearanceEntry>();
            for (int i = 0; i < m_activeEntries.Length; i++)
            {
                PlayerWeaponAppearanceEntry entry = m_activeEntries[i];
                if (entry != null && shownEntries.Add(entry))
                {
                    entry.ShowSheathed();
                }
            }
        }

        /// <summary>把指定槽位当前武器切到手持表现，供动画事件调用。</summary>
        public void ShowWeaponInHand(int slotIndex)
        {
            Initialize();
            if (IsValidWeaponSlot(slotIndex) && m_activeEntries[slotIndex] != null)
            {
                m_activeEntries[slotIndex].ShowInHand();
            }
        }

        /// <summary>把指定槽位当前武器切到收纳表现，供动画事件调用。</summary>
        public void ShowWeaponSheathed(int slotIndex)
        {
            Initialize();
            if (IsValidWeaponSlot(slotIndex) && m_activeEntries[slotIndex] != null)
            {
                m_activeEntries[slotIndex].ShowSheathed();
            }
        }

        /// <summary>隐藏指定槽位当前引用的共享武器对象，仍被其他槽引用时不会隐藏。</summary>
        public void HideWeapon(int slotIndex)
        {
            if (IsValidWeaponSlot(slotIndex))
            {
                HideEntryIfUnused(m_activeEntries[slotIndex]);
            }
        }

        /// <summary>读取指定槽位当前武器数据。</summary>
        public WeaponData GetWeaponData(int slotIndex)
        {
            Initialize();
            return IsValidWeaponSlot(slotIndex) ? m_weaponDataBySlot[slotIndex] : null;
        }

        /// <summary>读取指定槽位当前手持武器对象。</summary>
        public GameObject GetWeaponGameObject(int slotIndex)
        {
            Initialize();
            return IsValidWeaponSlot(slotIndex) ? m_weaponObjectsBySlot[slotIndex] : null;
        }

        /// <summary>读取指定槽位当前武器对象名。</summary>
        public string GetWeaponObjectName(int slotIndex)
        {
            Initialize();
            return IsValidWeaponSlot(slotIndex) ? m_weaponObjectNames[slotIndex] : null;
        }

        /// <summary>缓存指定防具类型的模型组。</summary>
        private void CacheGroup(EquipmentType type, Transform groupRoot, string defaultObjectName)
        {
            if (groupRoot == null)
            {
                Debug.LogWarning($"{name} 未配置 {type} 装备根节点，{type} 换装不会生效。");
                return;
            }

            EquipmentModelGroup group = new EquipmentModelGroup(groupRoot, defaultObjectName);
            m_groups[type] = group;
            m_currentObjectNames[type] = defaultObjectName;
        }

        /// <summary>按对象名查找完整的共享武器表现项。</summary>
        private PlayerWeaponAppearanceEntry FindWeaponEntry(string objectName)
        {
            if (m_weaponAppearances == null)
            {
                return null;
            }

            for (int i = 0; i < m_weaponAppearances.Length; i++)
            {
                PlayerWeaponAppearanceEntry entry = m_weaponAppearances[i];
                if (entry != null
                    && entry.IsComplete
                    && string.Equals(entry.ObjectName, objectName, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>隐藏所有共享武器表现对象。</summary>
        private void HideAllWeaponAppearances()
        {
            if (m_weaponAppearances == null)
            {
                return;
            }

            for (int i = 0; i < m_weaponAppearances.Length; i++)
            {
                if (m_weaponAppearances[i] != null && m_weaponAppearances[i].IsComplete)
                {
                    m_weaponAppearances[i].Hide();
                }
            }
        }

        /// <summary>共享武器表现未被任何槽继续引用时才隐藏它。</summary>
        private void HideEntryIfUnused(PlayerWeaponAppearanceEntry entry)
        {
            if (entry == null)
            {
                return;
            }

            for (int i = 0; i < m_activeEntries.Length; i++)
            {
                if (m_activeEntries[i] == entry)
                {
                    return;
                }
            }

            entry.Hide();
        }

        /// <summary>清除指定武器槽的运行时表现缓存。</summary>
        private void ClearWeaponRuntimeSlot(int slotIndex)
        {
            if (!IsValidWeaponSlot(slotIndex))
            {
                return;
            }

            m_weaponObjectNames[slotIndex] = null;
            m_weaponDataBySlot[slotIndex] = null;
            m_weaponObjectsBySlot[slotIndex] = null;
            m_activeEntries[slotIndex] = null;
        }

        /// <summary>判断武器槽索引是否处于玩家双武器槽范围内。</summary>
        private static bool IsValidWeaponSlot(int slotIndex)
        {
            return slotIndex >= 0 && slotIndex < WeaponSlotCount;
        }

        /// <summary>读取可选根对象的 Transform。</summary>
        private static Transform GetRootTransform(GameObject root)
        {
            return root == null ? null : root.transform;
        }

        /// <summary>启用根节点到目标对象之间的父链。</summary>
        private static void SetParentPathActive(Transform root, Transform target)
        {
            if (root != null)
            {
                root.gameObject.SetActive(true);
            }

            Transform current = target;
            while (current != null && current != root)
            {
                current.gameObject.SetActive(true);
                current = current.parent;
            }
        }

        private sealed class EquipmentModelGroup
        {
            private readonly Transform m_root;
            private readonly Dictionary<string, GameObject> m_modelCache =
                new Dictionary<string, GameObject>(StringComparer.Ordinal);
            private readonly List<GameObject> m_modelVariants = new List<GameObject>();
            private readonly string m_variantPrefix;

            private GameObject m_activeModel;

            public string DefaultObjectName { get; }

            /// <summary>创建防具模型组并缓存根节点下的全部候选模型。</summary>
            public EquipmentModelGroup(Transform root, string defaultObjectName)
            {
                m_root = root;
                DefaultObjectName = defaultObjectName;
                m_variantPrefix = GetModelNamePrefix(defaultObjectName);
                CacheModels(m_root);
            }

            /// <summary>按对象名查找防具模型。</summary>
            public GameObject FindModel(string objectName)
            {
                if (string.IsNullOrWhiteSpace(objectName))
                {
                    return null;
                }

                return m_modelCache.TryGetValue(objectName, out GameObject model) ? model : null;
            }

            /// <summary>判断指定模型是否为当前激活模型。</summary>
            public bool IsActiveModel(GameObject model)
            {
                return model != null && m_activeModel == model && model.activeInHierarchy;
            }

            /// <summary>只显示指定防具模型并关闭同组变体。</summary>
            public void SetActiveOnly(GameObject activeModel)
            {
                if (m_root == null)
                {
                    return;
                }

                if (activeModel != null && m_activeModel == activeModel && activeModel.activeInHierarchy)
                {
                    return;
                }

                SetVariantModelsActive(false);
                if (activeModel == null)
                {
                    m_activeModel = null;
                    return;
                }

                SetParentPathActive(m_root, activeModel.transform);
                activeModel.SetActive(true);
                m_activeModel = activeModel;
            }

            /// <summary>递归缓存根节点下的防具模型对象。</summary>
            private void CacheModels(Transform root)
            {
                if (root == null)
                {
                    return;
                }

                if (!m_modelCache.ContainsKey(root.name))
                {
                    m_modelCache.Add(root.name, root.gameObject);
                }

                if (IsVariantModelName(root.name) && !m_modelVariants.Contains(root.gameObject))
                {
                    m_modelVariants.Add(root.gameObject);
                }

                for (int i = 0; i < root.childCount; i++)
                {
                    CacheModels(root.GetChild(i));
                }
            }

            /// <summary>批量切换同组变体模型显隐。</summary>
            private void SetVariantModelsActive(bool active)
            {
                for (int i = 0; i < m_modelVariants.Count; i++)
                {
                    if (m_modelVariants[i] != null)
                    {
                        m_modelVariants[i].SetActive(active);
                    }
                }
            }

            /// <summary>判断对象名是否属于默认模型同前缀的数字变体。</summary>
            private bool IsVariantModelName(string objectName)
            {
                if (string.IsNullOrWhiteSpace(m_variantPrefix)
                    || string.IsNullOrWhiteSpace(objectName)
                    || !objectName.StartsWith(m_variantPrefix, StringComparison.Ordinal)
                    || objectName.Length == m_variantPrefix.Length)
                {
                    return false;
                }

                for (int i = m_variantPrefix.Length; i < objectName.Length; i++)
                {
                    if (!char.IsDigit(objectName[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            /// <summary>读取模型名去掉尾部数字后的前缀。</summary>
            private static string GetModelNamePrefix(string objectName)
            {
                if (string.IsNullOrWhiteSpace(objectName))
                {
                    return string.Empty;
                }

                int index = objectName.Length;
                while (index > 0 && char.IsDigit(objectName[index - 1]))
                {
                    index--;
                }

                return objectName.Substring(0, index);
            }
        }
    }
}
