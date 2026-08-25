using System;
using UnityEngine;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 标记 UI 面板的快捷键行为，减少新增界面时对 UIManager 的手工改动。
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class UIShortcutAttribute : Attribute
    {
        public KeyCode Key { get; }
        public string SceneName { get; }
        public bool PauseGame { get; }
        public bool UnlockCursor { get; }
        public bool Toggle { get; }

        public UIShortcutAttribute(
            KeyCode key,
            string sceneName = null,
            bool pauseGame = false,
            bool unlockCursor = true,
            bool toggle = true)
        {
            Key = key;
            SceneName = sceneName;
            PauseGame = pauseGame;
            UnlockCursor = unlockCursor;
            Toggle = toggle;
        }
    }
}
