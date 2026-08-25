# 场景加载拆解 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将场景加载职责从 `UIManager` 中拆出到独立的 `SceneFlowManager`，并统一场景名常量。

**Architecture:** `SceneFlowManager` 负责触发场景切换、转发加载状态和场景完成事件；`UIManager` 只负责 UI 响应、loading 面板、默认面板切换和鼠标状态。`SceneNames` 作为单一真相来源，供面板属性和运行时逻辑共用。

**Tech Stack:** Unity 2022.3.61f1c1, C# 9.0, UGUI, TextMeshPro

---

### Task 1: 新增场景常量与场景流程管理器

**Files:**
- Create: `Assets/Game/Scene/SceneNames.cs`
- Create: `Assets/Game/Scene/SceneFlowManager.cs`

- [ ] **Step 1: 新建 `SceneNames` 常量类**

```csharp
namespace GameMain2.Scripts.UI
{
    public static class SceneNames
    {
        public const string MenuScene = "MenuScene";
        public const string BattleScene = "Scene1";
    }
}
```

- [ ] **Step 2: 新建 `SceneFlowManager`，负责启动时订阅 `SceneManager.sceneLoaded` 并转发场景事件**

```csharp
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameMain2.Scripts.UI
{
    public sealed class SceneFlowManager : MonoBehaviour
    {
        public static SceneFlowManager Instance { get; }
    }
}
```

### Task 2: 收拢 `UIManager` 的职责边界

**Files:**
- Modify: `Assets/Game/UI/Core/UIManager.cs`

- [ ] **Step 1: 删除场景加载相关字段与方法**

```csharp
private const string MenuSceneName = "MenuScene";
private const string BattleSceneName = "Scene1";
private Coroutine m_sceneLoadCoroutine;
public void LoadScene(string sceneName) { }
public void ReturnToMainMenu() { }
private IEnumerator LoadSceneRoutine(string sceneName) { }
private void OnSceneLoaded(Scene scene, LoadSceneMode mode) { }
```

- [ ] **Step 2: 改为订阅 `SceneFlowManager` 的加载开始、加载结束和加载失败事件**

```csharp
SceneFlowManager.Instance.SceneLoadStarted += HandleSceneLoadStarted;
SceneFlowManager.Instance.SceneLoaded += HandleSceneLoaded;
SceneFlowManager.Instance.SceneLoadFailed += HandleSceneLoadFailed;
```

- [ ] **Step 3: 保留 `ApplySceneUI`，改用 `SceneNames` 判断场景名**

```csharp
if (sceneName == SceneNames.MenuScene) { }
if (sceneName == SceneNames.BattleScene) { }
```

### Task 3: 更新面板入口

**Files:**
- Modify: `Assets/Game/UI/Panels/MainMenuPanel.cs`
- Modify: `Assets/Game/UI/Panels/PausePanel.cs`
- Modify: `Assets/Game/UI/Bag/BagPanel.cs`

- [ ] **Step 1: 开始游戏按钮改为调用 `SceneFlowManager.Instance.LoadScene(SceneNames.BattleScene)`**

```csharp
SceneFlowManager.Instance.LoadScene(SceneNames.BattleScene);
```

- [ ] **Step 2: 返回主菜单改为调用 `SceneFlowManager.Instance.ReturnToMainMenu()`**

```csharp
UIManager.Instance.ShowConfirm("返回主菜单", "当前进度不会保存，确定返回主菜单吗？", SceneFlowManager.Instance.ReturnToMainMenu);
```

- [ ] **Step 3: `UIShortcut` 属性改用 `SceneNames.BattleScene`**

```csharp
[UIShortcut(KeyCode.Escape, SceneNames.BattleScene, true, true)]
```

### Task 4: 自检收口

**Files:**
- Modify: `Assets/Game/UI/Core/UIManager.cs`
- Modify: `Assets/Game/Scene/SceneFlowManager.cs`

- [ ] **Step 1: 全局搜索确认 `UIManager` 不再包含 `SceneManager.LoadSceneAsync`**
- [ ] **Step 2: 全局搜索确认场景名只剩 `SceneNames` 的常量引用**
- [ ] **Step 3: 检查事件解绑，保证重复切场景时不会重复订阅**

