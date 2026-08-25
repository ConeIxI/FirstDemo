# Pickup Tip Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 制作玩家拾取掉落物成功后的右侧中部拾取提示 UI 面板，支持图标、名称、数量、4 秒顺序播放和等待队列同道具合并。

**Architecture:** 采用独立 `PickupTipPanel` 承载 UI、队列和动效，`BagLogic` 只在背包成功写入后提交提示数据。数据以 `PickupTipData` 在 UI 层传递，合并依据 `BagItemType + ItemId`，当前播放项不参与合并。

**Tech Stack:** Unity 2022.3.61f1c1、C# 9.0、UGUI、TextMeshPro、现有 `UIManager` / `UIPanelBase` / `UIElementFactory`。

---

## File Structure

- Create: `Assets/Game/UI/Common/PickupTipData.cs`
  - 定义拾取提示数据，保存物品类型、ID、图标、名称和数量。
- Create: `Assets/Game/UI/Panels/PickupTipPanel.cs`
  - 独立 UI 面板，负责预制体控件校验、队列合并、顺序播放、右侧滑入和原地淡出。
- Create: `Assets/Res/Prefabs/UI/PickupTipPanel.prefab`
  - 拾取提示面板预制体，提供图标、名称、数量和卡片布局节点。
- Modify: `Assets/Game/UI/Core/UIType.cs`
  - 新增 `PickupTip` 面板类型。
- Modify: `Assets/Game/UI/Core/UIManager.cs`
  - 新增 `ShowPickupTip(PickupTipData data)` 入口，用当前场景和阻塞面板控制是否显示，并要求 `PickupTip` 必须通过 prefab 加载。
- Modify: `Assets/Game/Modules/Bag/BagLogic.cs`
  - 背包写入成功后创建 `BagItemData` 并提交拾取提示，失败时不提示。
- Modify: `Assets/AddressableAssetsData/AssetGroups/UIPrefabGroup.asset`
  - 注册 `Assets/Res/Prefabs/UI/PickupTipPanel.prefab`，地址为 `UI/PickupTipPanel`。

## Task 1: Add Pickup Tip Data Type

**Files:**
- Create: `Assets/Game/UI/Common/PickupTipData.cs`

- [x] **Step 1: Create immutable pickup tip data**

Create `PickupTipData` in namespace `GameMain2.Scripts.UI` with fields/properties:

```csharp
using UnityEngine;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 拾取成功提示的数据快照，避免 UI 播放期间反查背包状态。
    /// </summary>
    public sealed class PickupTipData
    {
        public BagItemType ItemType { get; }
        public int ItemId { get; }
        public Sprite Icon { get; }
        public string Name { get; }
        public int Count { get; private set; }

        /// <summary>
        /// 创建一条拾取提示数据，数量用于等待队列中的同道具累计。
        /// </summary>
        public PickupTipData(BagItemType itemType, int itemId, Sprite icon, string name, int count)
        {
            ItemType = itemType;
            ItemId = itemId;
            Icon = icon;
            Name = name;
            Count = count;
        }

        /// <summary>
        /// 判断另一条提示是否表示同一个可合并道具。
        /// </summary>
        public bool IsSameItem(PickupTipData other)
        {
            return other != null && ItemType == other.ItemType && ItemId == other.ItemId;
        }

        /// <summary>
        /// 累加等待队列中的同道具数量。
        /// </summary>
        public void AddCount(int count)
        {
            Count += count;
        }
    }
}
```

## Task 2: Register UI Type and Manager Entry

**Files:**
- Modify: `Assets/Game/UI/Core/UIType.cs`
- Modify: `Assets/Game/UI/Core/UIManager.cs`

- [x] **Step 1: Add the UI type**

Add `PickupTip` to `UIType` after `Toast` and before `Bag`:

```csharp
public enum UIType
{
    MainMenu,
    BattleHud,
    Pause,
    Settings,
    ConfirmDialog,
    Loading,
    Toast,
    PickupTip,
    Bag
}
```

- [x] **Step 2: Add public show method**

In `UIManager`, add a public method near `ShowToast`:

```csharp
/// <summary>在战斗 HUD 可见且没有阻塞面板时显示拾取成功提示。</summary>
public void ShowPickupTip(PickupTipData data)
{
    if (!CanShowPickupTip())
    {
        return;
    }

    OpenPanel(UIType.PickupTip, data);
}
```

- [x] **Step 3: Add display gate**

In `UIManager`, add a private helper near `RefreshBattleCursorState`:

```csharp
/// <summary>判断当前 UI 状态是否允许播放拾取提示。</summary>
private bool CanShowPickupTip()
{
    if (SceneManager.GetActiveScene().name != BattleSceneName)
    {
        return false;
    }

    return IsPanelOpen(UIType.BattleHud)
        && !IsPanelOpen(UIType.Bag)
        && !IsPanelOpen(UIType.Pause)
        && !IsPanelOpen(UIType.Settings)
        && !IsPanelOpen(UIType.ConfirmDialog);
}
```

## Task 3: Build Pickup Tip Panel Prefab

**Files:**
- Create: `Assets/Game/UI/Panels/PickupTipPanel.cs`
- Create: `Assets/Res/Prefabs/UI/PickupTipPanel.prefab`
- Modify: `Assets/AddressableAssetsData/AssetGroups/UIPrefabGroup.asset`

- [x] **Step 1: Create panel shell and controls**

Create `PickupTipPanel` with `[UIPanel(UIType.PickupTip, UILayer.Toast)]`, serialized fields for card root, `CanvasGroup`, icon image, name text, count text, and constants:

```csharp
private const float TotalDuration = 4f;
private const float SlideDuration = 0.25f;
private const float FadeDuration = 0.55f;
private static readonly Vector2 HiddenPosition = new Vector2(520f, 0f);
private static readonly Vector2 VisiblePosition = new Vector2(-72f, 0f);
```

- [x] **Step 2: Create prefab view**

Create `PickupTipPanel.prefab` under `Assets/Res/Prefabs/UI/` with `PickupTipCard` anchored to right center, with icon block, name text, and count text. Register it to `UIPrefabGroup` with address `UI/PickupTipPanel`.

- [x] **Step 3: Cache and validate controls**

In `CacheControls`, find `PickupTipCard/IconFrame/Icon`, `PickupTipCard/Name`, and `PickupTipCard/CountBox/Count` when serialized references are empty. In `ValidateControls`, throw immediately if prefab references are incomplete.

- [x] **Step 4: Implement enqueue and merge**

Override `OnOpen(object userData)` so `PickupTipData` either starts playback immediately or merges into the waiting queue. Current item is never modified.

- [x] **Step 5: Implement animation routine**

Use an unscaled coroutine. Each item should:

```text
reset alpha and hidden position
bind icon/name/count
slide from hidden to visible for 0.25s
wait for 3.2s
fade alpha to 0 for 0.55s at visible position
play next queued item or close panel
```

- [x] **Step 6: Close cleanly**

Override `OnClose()` to stop active coroutine, clear queue, reset playing state, and hide through `base.OnClose()`.

## Task 4: Emit Tip After Successful Pickup

**Files:**
- Modify: `Assets/Game/Modules/Bag/BagLogic.cs`

- [x] **Step 1: Keep created item data available after add**

Change `TryAddItem` so it creates `BagItemData item = CreateBagItemData(...)`, calls `AddBagItem(item)`, and then calls a new private helper `ShowPickupTip(item)` before `NotifyInventoryChanged()`.

- [x] **Step 2: Add helper to submit UI data**

Add this private method to `BagLogic`:

```csharp
/// <summary>
/// 背包成功接收地面掉落物后，向 UI 提交拾取成功提示。
/// </summary>
private static void ShowPickupTip(BagItemData item)
{
    PickupTipData data = new PickupTipData(item.ItemType, item.Id, item.Icon, item.Name, item.Count);
    UIManager.Instance.ShowPickupTip(data);
}
```

## Task 5: Verify

**Files:**
- No test files are created.

- [x] **Step 1: Compile with project-approved command**

Run:

```bash
$CLI compile unity
```

Expected: Unity compile succeeds with no C# errors.

- [ ] **Step 2: Manual play verification**

Status: 待 Unity Play Mode 中手动拾取道具验证，当前会话已完成 Unity 编译验证。

In Unity Play Mode, verify:

- Single pickup shows icon/name/`x数量` on right center and disappears after 4 seconds.
- Different items picked quickly play one after another.
- Same items picked while waiting merge into one later queue item.
- Same item picked while currently displayed does not update the current displayed quantity.
- Bag, Pause, Settings, and ConfirmDialog states do not display the tip immediately.

---

## Self-Review

- Spec coverage: Covers independent panel, icon/name/count, 4-second display, right-center slide-in, original-position fade-out, ordered queue, waiting-queue item merge, current item not updating, HUD-only display gate, and Unity compile verification.
- Placeholder scan: No placeholder tasks are left.
- Type consistency: `PickupTipData`, `UIType.PickupTip`, `UIManager.ShowPickupTip`, and `BagLogic.ShowPickupTip` names are consistent across tasks.
- Project constraints: No `.controller` files, no test files, no C# syntax beyond C# 9.0, and all new functions require 简体中文注释.
