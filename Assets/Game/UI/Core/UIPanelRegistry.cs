using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// UI 面板注册表，负责扫描面板特性、缓存面板定义并维护快捷键定义。
    /// </summary>
    internal sealed class UIPanelRegistry
    {
        private readonly Dictionary<UIType, UIPanelDefinition> m_panelDefinitions =
            new Dictionary<UIType, UIPanelDefinition>();
        private readonly List<UIShortcutDefinition> m_shortcutDefinitions = new List<UIShortcutDefinition>();

        /// <summary>返回已扫描出的快捷键定义集合。</summary>
        public IReadOnlyList<UIShortcutDefinition> ShortcutDefinitions
        {
            get
            {
                EnsureBuilt();
                return m_shortcutDefinitions;
            }
        }

        /// <summary>获取指定面板类型的注册信息，缺失时按约定兜底生成。</summary>
        public UIPanelDefinition GetDefinition(UIType type)
        {
            EnsureBuilt();
            if (m_panelDefinitions.TryGetValue(type, out UIPanelDefinition definition))
            {
                return definition;
            }

            Type panelType = FindConventionPanelType(type);
            definition = new UIPanelDefinition(type, UILayer.Normal, GetDefaultAddress(panelType, type), panelType, false);
            m_panelDefinitions[type] = definition;
            return definition;
        }

        /// <summary>扫描程序集并构建面板定义和快捷键定义缓存。</summary>
        private void EnsureBuilt()
        {
            if (m_panelDefinitions.Count > 0)
            {
                return;
            }

            Type panelBaseType = typeof(UIPanelBase);
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                foreach (Type type in GetLoadableTypes(assemblies[i]))
                {
                    if (type == null || type.IsAbstract || !panelBaseType.IsAssignableFrom(type))
                    {
                        continue;
                    }

                    UIPanelAttribute attribute = Attribute.GetCustomAttribute(type, typeof(UIPanelAttribute)) as UIPanelAttribute;
                    if (attribute == null)
                    {
                        continue;
                    }

                    string address = string.IsNullOrEmpty(attribute.Address)
                        ? GetDefaultAddress(type, attribute.Type)
                        : attribute.Address;
                    RegisterPanelDefinition(new UIPanelDefinition(
                        attribute.Type,
                        attribute.Layer,
                        address,
                        type,
                        attribute.BlockGameplayInput));
                    RegisterShortcutDefinitions(type, attribute.Type);
                }
            }
        }

        /// <summary>注册单个面板定义，重复定义时保留先注册项。</summary>
        private void RegisterPanelDefinition(UIPanelDefinition definition)
        {
            if (m_panelDefinitions.TryGetValue(definition.Type, out UIPanelDefinition oldDefinition))
            {
                Debug.LogWarning(
                    $"重复注册 UI 面板：{definition.Type}，保留 {oldDefinition.PanelType?.Name}，忽略 {definition.PanelType?.Name}");
                return;
            }

            m_panelDefinitions.Add(definition.Type, definition);
        }

        /// <summary>注册面板类型上的所有快捷键定义。</summary>
        private void RegisterShortcutDefinitions(Type panelType, UIType type)
        {
            object[] attributes = panelType.GetCustomAttributes(typeof(UIShortcutAttribute), false);
            for (int i = 0; i < attributes.Length; i++)
            {
                UIShortcutAttribute attribute = attributes[i] as UIShortcutAttribute;
                if (attribute == null)
                {
                    continue;
                }

                RegisterShortcutDefinition(new UIShortcutDefinition(
                    type,
                    attribute.Key,
                    attribute.SceneName,
                    attribute.PauseGame,
                    attribute.UnlockCursor,
                    attribute.Toggle));
            }
        }

        /// <summary>注册单个快捷键定义，重复快捷键会直接忽略后来的定义。</summary>
        private void RegisterShortcutDefinition(UIShortcutDefinition definition)
        {
            for (int i = 0; i < m_shortcutDefinitions.Count; i++)
            {
                UIShortcutDefinition oldDefinition = m_shortcutDefinitions[i];
                if (oldDefinition.Type == definition.Type
                    && oldDefinition.Key == definition.Key
                    && string.Equals(oldDefinition.SceneName, definition.SceneName, StringComparison.Ordinal))
                {
                    return;
                }

                if (oldDefinition.Key == definition.Key
                    && string.Equals(oldDefinition.SceneName, definition.SceneName, StringComparison.Ordinal))
                {
                    Debug.LogWarning(
                        $"UI 快捷键重复：{definition.Key} / {definition.SceneName}，保留 {oldDefinition.Type}，忽略 {definition.Type}");
                    return;
                }
            }

            m_shortcutDefinitions.Add(definition);
        }

        /// <summary>获取程序集内可加载的类型集合，跳过加载失败的部分结果。</summary>
        private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types;
            }
        }

        /// <summary>按约定查找面板类型名，格式为 `{UIType}Panel`。</summary>
        private static Type FindConventionPanelType(UIType type)
        {
            string panelTypeName = $"{type}Panel";
            Type panelBaseType = typeof(UIPanelBase);
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                foreach (Type candidate in GetLoadableTypes(assemblies[i]))
                {
                    if (candidate == null || candidate.IsAbstract || candidate.Name != panelTypeName)
                    {
                        continue;
                    }

                    if (panelBaseType.IsAssignableFrom(candidate))
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        /// <summary>获取面板的默认 Addressables 地址。</summary>
        private static string GetDefaultAddress(Type panelType, UIType type)
        {
            return panelType == null ? $"UI/{type}Panel" : $"UI/{panelType.Name}";
        }
    }
}
