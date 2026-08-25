using System;
using UnityEngine;

namespace GameMain2.Scripts.UI
{
    internal sealed class ToastData
    {
        public string Message { get; }
        public float Duration { get; }

        /// <summary>创建 Toast 展示数据，并限制最短展示时间。</summary>
        public ToastData(string message, float duration)
        {
            Message = string.IsNullOrEmpty(message) ? string.Empty : message;
            Duration = Mathf.Max(0.2f, duration);
        }
    }

    internal readonly struct UIPanelDefinition
    {
        public UIType Type { get; }
        public UILayer Layer { get; }
        public string Address { get; }
        public Type PanelType { get; }
        public bool BlockGameplayInput { get; }

        /// <summary>保存单个 UI 面板的类型、层级、资源地址和输入阻断配置。</summary>
        public UIPanelDefinition(
            UIType type,
            UILayer layer,
            string address,
            Type panelType,
            bool blockGameplayInput)
        {
            Type = type;
            Layer = layer;
            Address = address;
            PanelType = panelType;
            BlockGameplayInput = blockGameplayInput;
        }
    }

    internal readonly struct UIShortcutDefinition
    {
        public UIType Type { get; }
        public KeyCode Key { get; }
        public string SceneName { get; }
        public bool PauseGame { get; }
        public bool UnlockCursor { get; }
        public bool Toggle { get; }

        /// <summary>保存单个 UI 快捷键的目标面板、场景限制和打开行为。</summary>
        public UIShortcutDefinition(
            UIType type,
            KeyCode key,
            string sceneName,
            bool pauseGame,
            bool unlockCursor,
            bool toggle)
        {
            Type = type;
            Key = key;
            SceneName = sceneName;
            PauseGame = pauseGame;
            UnlockCursor = unlockCursor;
            Toggle = toggle;
        }

        /// <summary>判断当前快捷键是否适用于指定场景。</summary>
        public bool MatchesScene(string sceneName)
        {
            return string.IsNullOrEmpty(SceneName) || string.Equals(SceneName, sceneName, StringComparison.Ordinal);
        }
    }
}
