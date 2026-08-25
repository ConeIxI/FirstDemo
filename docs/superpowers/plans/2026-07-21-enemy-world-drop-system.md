# Enemy World Drop System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现敌人死亡后由敌人自身的掉落物管理组件生成地面掉落物，玩家靠近后按 F 通过事件请求背包接收，成功后销毁掉落物。

**Architecture:** `EnemyDropComponent` 是挂在敌人 GameObject 上的掉落物管理组件，负责接收 `EnemyDefinition.DropItems`、按概率抽取、通过 Addressables 生成 `WorldDropItem`。`EnemyLifeComponent` 首次死亡时写入死亡状态、发布新的 `EnemyDeadEventArgs`，并调用同对象上的 `EnemyDropComponent.SpawnDrops(...)`。`WorldDropItem` 只发布拾取请求事件，常驻 `BagInventoryManager` 订阅并回调成功或失败。

**Tech Stack:** Unity 2022.3.61f1c1，C# 9.0，AIBridge CLI，Unity Addressables，现有 `EventCenter`，现有背包 UI 数据结构。

## Global Constraints

- 尽量使用简体中文回复和注释。
- 修改复杂业务逻辑时，必须用简体中文添加必要注释。
- 编写代码时，必须给每个函数添加简体中文注释，说明函数用途或关键行为。
- C# 必须兼容 C# 9.0，禁止使用文件范围命名空间、`required`、主构造函数等更高版本语法。
- Unity 编译只能使用 `$CLI compile unity`；`compile dotnet` 不能替代 Unity 编译。
- 不恢复 `EnemySpawnManager`。
- 不使用旧的 `MonsterDeadEventArgs`，新建 `EnemyDeadEventArgs`。
- 不新增 `PlayerInventory`。
- 新增 `EnemyDropComponent`，但本计划不包含把该组件写入任何敌人 Prefab 的步骤。
- 不创建 `EnemyDropResult.cs`、`EnemyDropRoller.cs`、`EnemyDropSpawnSystem.cs`。
- `WorldDropItem` 不直接引用 `BagInventoryManager`，拾取通过事件通知完成。
- 新 Prefab 路径固定为 `Assets/Res/Prefabs/Other/WorldDropItem.prefab`。
- `WorldDropItem` Addressable 地址固定为 `WorldDropItem`。
- 保留用户已有改动，不回滚无关文件。

---

## File Structure

- Create `Assets/Game/Character/Enemy/Config/EnemyDropItemConfig.cs`
  - 单条敌人掉落配置，序列化在 `EnemyDefinition` 内。
- Modify `Assets/Game/Character/Enemy/Config/EnemyDefinition.cs`
  - 增加 `dropItems` 字段和 `DropItems` 只读访问器。
- Modify `Assets/Game/Character/Enemy/Config/EnemyDefinitionValidator.cs`
  - 校验掉落物分类、物品 Id、数量和概率。
- Create `Assets/Game/Character/Enemy/Components/EnemyDropComponent.cs`
  - 敌人身上的掉落物管理组件，负责配置接收、概率抽取和地面掉落物生成。
- Create `Assets/Game/Character/Enemy/Events/EnemyDeadEventArgs.cs`
  - 新敌人死亡事件，替代已删除的 `MonsterDeadEventArgs`。
- Modify `Assets/Game/Character/Enemy/Components/EnemyLifeComponent.cs`
  - 首次死亡时发布 `EnemyDeadEventArgs`，并调用同对象上的 `EnemyDropComponent`。
- Modify `Assets/Game/Character/Enemy/AI/AIController.cs`
  - 启动 AI 时把 `EnemyDefinition.DropItems` 传给同对象上的 `EnemyDropComponent`。
- Create `Assets/Game/World/Drop/WorldDropItem.cs`
  - 地面掉落物组件，检测附近玩家按 F 并发布拾取请求。
- Create `Assets/Game/World/Drop/DropItemPickupRequestEventArgs.cs`
  - 地面掉落物拾取请求事件，带成功/失败回调。
- Modify `Assets/Game/UI/BagInventoryManager.cs`
  - 改为常驻背包数据管理器，新增 `TryAddItem` 并订阅拾取请求。
- Modify `Assets/Game/UI/BagPanel.cs`
  - 复用常驻 `BagInventoryManager.Instance`。
- Create `Assets/Game/Editor/EnemyDropSystemEditModeTests.cs`
  - 覆盖掉落配置、掉落组件抽取、死亡上下文、拾取初始化、背包新增入口。
- Create `Assets/Res/Prefabs/Other/WorldDropItem.prefab`
  - Cube 临时模型 Prefab，挂 `WorldDropItem`、Trigger Collider、Kinematic Rigidbody。

---

### Task 1: 掉落配置接入 EnemyDefinition

**Files:**
- Create: `Assets/Game/Character/Enemy/Config/EnemyDropItemConfig.cs`
- Modify: `Assets/Game/Character/Enemy/Config/EnemyDefinition.cs`
- Modify: `Assets/Game/Character/Enemy/Config/EnemyDefinitionValidator.cs`
- Test: `Assets/Game/Editor/EnemyDropSystemEditModeTests.cs`

**Interfaces:**
- Produces: `EnemyDefinition.DropItems : EnemyDropItemConfig[]`
- Produces: `EnemyDefinition.SetDropItems(EnemyDropItemConfig[] value)` inside `#if UNITY_EDITOR`
- Consumes later: `EnemyDropComponent.ApplyConfig(EnemyDropItemConfig[] value)`

- [ ] **Step 1: Write failing compile test for enemy drop config**

Create `Assets/Game/Editor/EnemyDropSystemEditModeTests.cs`:

```csharp
using Game.Character.Enemy.Config;
using GameMain2.Scripts.UI;
using NUnit.Framework;
using UnityEngine;

namespace Game.Editor
{
    public sealed partial class EnemyDropSystemEditModeTests
    {
        /// <summary>敌人定义可以保存掉落配置，供敌人掉落组件读取。</summary>
        [Test]
        public void EnemyDefinitionStoresDropItems()
        {
            EnemyDefinition definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            EnemyDropItemConfig[] dropItems =
            {
                new EnemyDropItemConfig
                {
                    itemType = BagItemType.Consumable,
                    itemId = 101,
                    count = 2,
                    dropChance = 1f
                }
            };

            definition.SetDropItems(dropItems);

            Assert.AreEqual(1, definition.DropItems.Length);
            Assert.AreEqual(101, definition.DropItems[0].itemId);

            Object.DestroyImmediate(definition);
        }
    }
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: FAIL because `EnemyDropItemConfig`, `EnemyDefinition.DropItems`, or `SetDropItems` does not exist yet.

- [ ] **Step 3: Add EnemyDropItemConfig**

Create `Assets/Game/Character/Enemy/Config/EnemyDropItemConfig.cs`:

```csharp
using System;
using GameMain2.Scripts.UI;
using UnityEngine;

namespace Game.Character.Enemy.Config
{
    [Serializable]
    public sealed class EnemyDropItemConfig
    {
        public BagItemType itemType = BagItemType.Consumable;
        public int itemId = 1;
        public int count = 1;
        [Range(0f, 1f)] public float dropChance = 1f;
    }
}
```

- [ ] **Step 4: Add drop items to EnemyDefinition**

Modify `Assets/Game/Character/Enemy/Config/EnemyDefinition.cs`:

```csharp
[SerializeField] private EnemyDropItemConfig[] dropItems = new EnemyDropItemConfig[0];
public EnemyDropItemConfig[] DropItems => dropItems;
```

Inside `#if UNITY_EDITOR`, add:

```csharp
// 设置敌人掉落配置，供编辑器工具或测试构造定义。
public void SetDropItems(EnemyDropItemConfig[] value)
{
    dropItems = value;
}
```

- [ ] **Step 5: Validate drop items**

In `EnemyDefinitionValidator.Validate`, call:

```csharp
ValidateDropItems(definition.DropItems, result);
```

Add:

```csharp
// 校验敌人掉落表，避免非法物品配置进入运行时生成流程。
private static void ValidateDropItems(EnemyDropItemConfig[] dropItems, EnemyDefinitionValidationResult result)
{
    if (dropItems == null)
    {
        return;
    }

    for (int i = 0; i < dropItems.Length; i++)
    {
        EnemyDropItemConfig item = dropItems[i];
        if (item == null)
        {
            result.AddError("DropItems", "掉落项不能为空");
            continue;
        }

        if (item.itemType == GameMain2.Scripts.UI.BagItemType.None)
        {
            result.AddError("DropItems", "掉落物分类不能为 None");
        }

        if (item.itemId <= 0)
        {
            result.AddError("DropItems", "掉落物 Id 必须为正数");
        }

        if (item.count <= 0)
        {
            result.AddError("DropItems", "掉落数量必须为正数");
        }

        AddErrorIfProbabilityInvalid(item.dropChance, "DropItems", result);
    }
}
```

- [ ] **Step 6: Verify GREEN**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: PASS for drop config and definition wiring.

---

### Task 2: 新增 EnemyDropComponent 并集成抽取/生成

**Files:**
- Create: `Assets/Game/Character/Enemy/Components/EnemyDropComponent.cs`
- Modify: `Assets/Game/Character/Enemy/AI/AIController.cs`
- Test: `Assets/Game/Editor/EnemyDropSystemEditModeTests.cs`

**Interfaces:**
- Produces: `EnemyDropComponent.ApplyConfig(EnemyDropItemConfig[] value)`
- Produces: `EnemyDropComponent.RollDropItems(Func<float> randomValueProvider) : List<EnemyDropItemConfig>`
- Produces: `EnemyDropComponent.SpawnDrops(Vector3 position)`
- Consumes later: `EnemyLifeComponent` calls `EnemyDropComponent.SpawnDrops(...)` on death

- [ ] **Step 1: Write failing test for drop component rolling**

Append to `Assets/Game/Editor/EnemyDropSystemEditModeTests.cs`:

```csharp
using System.Collections.Generic;
using Game.Character.Enemy.Components;

namespace Game.Editor
{
    public sealed partial class EnemyDropSystemEditModeTests
    {
        /// <summary>敌人掉落组件按概率返回命中的掉落配置。</summary>
        [Test]
        public void EnemyDropComponentRollsConfiguredDrops()
        {
            GameObject enemy = new GameObject("Enemy");
            EnemyDropComponent dropComponent = enemy.AddComponent<EnemyDropComponent>();
            EnemyDropItemConfig[] dropItems =
            {
                new EnemyDropItemConfig
                {
                    itemType = BagItemType.Consumable,
                    itemId = 101,
                    count = 2,
                    dropChance = 0.75f
                }
            };

            dropComponent.ApplyConfig(dropItems);
            List<EnemyDropItemConfig> results = dropComponent.RollDropItems(() => 0.5f);

            Assert.AreEqual(1, results.Count);
            Assert.AreEqual(101, results[0].itemId);

            Object.DestroyImmediate(enemy);
        }
    }
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: FAIL because `EnemyDropComponent` does not exist.

- [ ] **Step 3: Implement EnemyDropComponent**

Create `Assets/Game/Character/Enemy/Components/EnemyDropComponent.cs`:

```csharp
using System;
using System.Collections.Generic;
using Game.Character.Enemy.Config;
using Game.World.Drop;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Game.Character.Enemy.Components
{
    public sealed class EnemyDropComponent : MonoBehaviour
    {
        private const string WorldDropItemAddress = "WorldDropItem";
        private const float DropSpacing = 0.75f;

        [SerializeField] private EnemyDropItemConfig[] dropItems = new EnemyDropItemConfig[0];
        private bool hasSpawnedDrops;

        /// <summary>从敌人定义应用掉落配置。</summary>
        public void ApplyConfig(EnemyDropItemConfig[] value)
        {
            dropItems = value ?? new EnemyDropItemConfig[0];
            hasSpawnedDrops = false;
        }

        /// <summary>按当前掉落配置和随机值抽取命中的掉落项。</summary>
        public List<EnemyDropItemConfig> RollDropItems(Func<float> randomValueProvider)
        {
            List<EnemyDropItemConfig> results = new List<EnemyDropItemConfig>();
            for (int i = 0; i < dropItems.Length; i++)
            {
                EnemyDropItemConfig item = dropItems[i];
                if (item != null && randomValueProvider() <= item.dropChance)
                {
                    results.Add(item);
                }
            }

            return results;
        }

        /// <summary>在指定位置生成本次死亡抽中的地面掉落物，且同一敌人只生成一次。</summary>
        public void SpawnDrops(Vector3 position)
        {
            if (hasSpawnedDrops)
            {
                return;
            }

            hasSpawnedDrops = true;
            List<EnemyDropItemConfig> results = RollDropItems(() => UnityEngine.Random.value);
            for (int i = 0; i < results.Count; i++)
            {
                Vector3 dropPosition = position + Vector3.right * (DropSpacing * i);
                SpawnDropAsync(results[i], dropPosition);
            }
        }

        /// <summary>异步实例化地面掉落物 Prefab，并写入物品数据。</summary>
        private async void SpawnDropAsync(EnemyDropItemConfig item, Vector3 position)
        {
            AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(
                WorldDropItemAddress,
                position,
                Quaternion.identity);

            GameObject instance = await handle.Task;
            if (handle.Status != AsyncOperationStatus.Succeeded || instance == null)
            {
                return;
            }

            WorldDropItem worldDropItem = instance.GetComponent<WorldDropItem>();
            if (worldDropItem != null)
            {
                worldDropItem.Initialize(item.itemType, item.itemId, item.count);
            }
        }
    }
}
```

- [ ] **Step 4: Wire config from AIController**

Modify `Assets/Game/Character/Enemy/AI/AIController.cs`:

```csharp
EnemyDropComponent drop = GetComponent<EnemyDropComponent>();
```

Inside the `definition != null` block, add:

```csharp
if (drop != null)
{
    drop.ApplyConfig(definition.DropItems);
}
```

- [ ] **Step 5: Verify GREEN**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: PASS for `EnemyDropComponent` and AI config wiring.

---

### Task 3: 新敌人死亡事件与死亡触发掉落

**Files:**
- Create: `Assets/Game/Character/Enemy/Events/EnemyDeadEventArgs.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyLifeComponent.cs`
- Test: `Assets/Game/Editor/EnemyDropSystemEditModeTests.cs`

**Interfaces:**
- Produces: `EnemyDeadEventArgs.EventId`
- Produces: `EnemyLifeComponent.BindDeathContext(EnemyDefinition value, Transform owner)`
- Consumes: `EnemyDropComponent.SpawnDrops(Vector3 position)`

- [ ] **Step 1: Write failing compile test for death context method**

Append to `Assets/Game/Editor/EnemyDropSystemEditModeTests.cs`:

```csharp
using Game.Character.Enemy.Events;

namespace Game.Editor
{
    public sealed partial class EnemyDropSystemEditModeTests
    {
        /// <summary>生命组件公开死亡上下文绑定入口供 AI 初始化链路调用。</summary>
        [Test]
        public void EnemyLifeComponentExposesDeathContextBinding()
        {
            GameObject enemy = new GameObject("Enemy");
            EnemyLifeComponent life = enemy.AddComponent<EnemyLifeComponent>();
            EnemyDefinition definition = ScriptableObject.CreateInstance<EnemyDefinition>();

            life.BindDeathContext(definition, enemy.transform);
            Assert.AreNotEqual(0, EnemyDeadEventArgs.EventId);

            Object.DestroyImmediate(enemy);
            Object.DestroyImmediate(definition);
        }
    }
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: FAIL because `EnemyDeadEventArgs` or `BindDeathContext` does not exist.

- [ ] **Step 3: Create EnemyDeadEventArgs**

Create `Assets/Game/Character/Enemy/Events/EnemyDeadEventArgs.cs`:

```csharp
using Game.Character.Enemy.Config;
using GameMain2.Framework.Core;
using UnityEngine;

namespace Game.Character.Enemy.Events
{
    public sealed class EnemyDeadEventArgs : EventArgsBase
    {
        public static readonly int EventId = typeof(EnemyDeadEventArgs).GetHashCode();

        public readonly EnemyDefinition Definition;
        public readonly Transform EnemyTransform;
        public readonly Vector3 DeathPosition;

        public override int Id => EventId;

        /// <summary>创建敌人死亡事件，携带死亡位置和敌人配置。</summary>
        public EnemyDeadEventArgs(EnemyDefinition definition, Transform enemyTransform, Vector3 deathPosition)
        {
            Definition = definition;
            EnemyTransform = enemyTransform;
            DeathPosition = deathPosition;
        }
    }
}
```

- [ ] **Step 4: Publish death and call EnemyDropComponent**

Modify `EnemyLifeComponent`:

```csharp
using Game.Character.Enemy.Events;
```

Add fields:

```csharp
private EnemyDefinition definition;
private Transform ownerTransform;
private EnemyDropComponent dropComponent;
private bool deathHandled;
```

In `Awake`, add:

```csharp
TryGetComponent(out dropComponent);
```

Add method:

```csharp
/// <summary>绑定死亡事件所需的敌人定义和实例 Transform。</summary>
public void BindDeathContext(EnemyDefinition value, Transform owner)
{
    definition = value;
    ownerTransform = owner;
}
```

Replace `HandleDeath()` with:

```csharp
/// <summary>处理死亡反应，首次死亡时写入黑板、发布事件并触发敌人自身掉落组件。</summary>
public void HandleDeath()
{
    if (deathHandled)
    {
        return;
    }

    deathHandled = true;
    if (allowDeathReaction && blackboard != null)
    {
        blackboard.SetDead(true);
    }

    Transform eventTransform = ownerTransform != null ? ownerTransform : transform;
    Vector3 deathPosition = eventTransform.position;
    if (dropComponent != null)
    {
        dropComponent.SpawnDrops(deathPosition);
    }

    EventCenter.Instance.Fire(
        this,
        new EnemyDeadEventArgs(definition, eventTransform, deathPosition));
}
```

- [ ] **Step 5: Bind death context from AIController**

In `AIController.StartAI`, after `life.ApplyConfig(definition.LifeConfig);`, add:

```csharp
life.BindDeathContext(definition, transform);
```

- [ ] **Step 6: Verify GREEN**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: PASS for new death event and death-triggered component drop.

---

### Task 4: 地面掉落物拾取事件与行为

**Files:**
- Create: `Assets/Game/World/Drop/DropItemPickupRequestEventArgs.cs`
- Create: `Assets/Game/World/Drop/WorldDropItem.cs`
- Test: `Assets/Game/Editor/EnemyDropSystemEditModeTests.cs`

**Interfaces:**
- Produces: `DropItemPickupRequestEventArgs.EventId`
- Produces: `WorldDropItem.Initialize(BagItemType itemType, int itemId, int count)`
- Consumes later: `BagInventoryManager` subscribes to pickup requests

- [ ] **Step 1: Write failing compile test for WorldDropItem initialization**

Append to `Assets/Game/Editor/EnemyDropSystemEditModeTests.cs`:

```csharp
using Game.World.Drop;

namespace Game.Editor
{
    public sealed partial class EnemyDropSystemEditModeTests
    {
        /// <summary>地面掉落物公开初始化入口供敌人掉落组件写入物品数据。</summary>
        [Test]
        public void WorldDropItemCanReceiveItemData()
        {
            GameObject dropObject = new GameObject("WorldDropItem");
            WorldDropItem dropItem = dropObject.AddComponent<WorldDropItem>();

            dropItem.Initialize(BagItemType.Consumable, 101, 1);

            Object.DestroyImmediate(dropObject);
        }
    }
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: FAIL because `WorldDropItem` does not exist.

- [ ] **Step 3: Add pickup request event**

Create `Assets/Game/World/Drop/DropItemPickupRequestEventArgs.cs`:

```csharp
using System;
using GameMain2.Framework.Core;
using GameMain2.Scripts.UI;

namespace Game.World.Drop
{
    public sealed class DropItemPickupRequestEventArgs : EventArgsBase
    {
        public static readonly int EventId = typeof(DropItemPickupRequestEventArgs).GetHashCode();

        public readonly BagItemType ItemType;
        public readonly int ItemId;
        public readonly int Count;
        private readonly Action<bool> onCompleted;

        public override int Id => EventId;

        /// <summary>创建拾取请求事件，并保存背包处理完成后的回调。</summary>
        public DropItemPickupRequestEventArgs(
            BagItemType itemType,
            int itemId,
            int count,
            Action<bool> onCompleted)
        {
            ItemType = itemType;
            ItemId = itemId;
            Count = count;
            this.onCompleted = onCompleted;
        }

        /// <summary>由背包系统通知本次拾取是否成功。</summary>
        public void Complete(bool success)
        {
            if (onCompleted != null)
            {
                onCompleted(success);
            }
        }
    }
}
```

- [ ] **Step 4: Add WorldDropItem behavior**

Create `Assets/Game/World/Drop/WorldDropItem.cs` with:

- serialized fields `BagItemType itemType`, `int itemId`, `int count`, `Collider pickupTrigger`
- `Awake()` to cache and mark trigger collider
- `Update()` to check `Input.GetKeyDown(KeyCode.F)` while player is in range
- `Initialize(BagItemType itemTypeValue, int itemIdValue, int countValue)`
- `OnTriggerEnter/OnTriggerExit` using `Player` tag
- `TryPickUp()` firing `DropItemPickupRequestEventArgs`
- `OnPickupCompleted(bool success)` destroying the GameObject only on success

- [ ] **Step 5: Verify GREEN**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: PASS for pickup request event and `WorldDropItem`.

---

### Task 5: 背包事件接收与公开添加入口

**Files:**
- Modify: `Assets/Game/UI/BagInventoryManager.cs`
- Modify: `Assets/Game/UI/BagPanel.cs`
- Test: `Assets/Game/Editor/EnemyDropSystemEditModeTests.cs`

**Interfaces:**
- Produces: `BagInventoryManager.TryAddItem(BagItemType itemType, int id, int count) : bool`
- Consumes: `DropItemPickupRequestEventArgs`
- Produces: `BagInventoryManager.Instance` 常驻背包数据入口

- [ ] **Step 1: Write failing test for TryAddItem**

Append to `Assets/Game/Editor/EnemyDropSystemEditModeTests.cs`:

```csharp
namespace Game.Editor
{
    public sealed partial class EnemyDropSystemEditModeTests
    {
        /// <summary>背包公开入口可以把拾取物放入对应分类第一个空格。</summary>
        [Test]
        public void TryAddItemAddsItemToFirstEmptyBagSlot()
        {
            GameObject inventoryObject = new GameObject("BagInventoryManager");
            BagInventoryManager inventory = inventoryObject.AddComponent<BagInventoryManager>();
            inventory.Initialize(2);

            bool added = inventory.TryAddItem(BagItemType.Consumable, 101, 3);
            BagItemData item = inventory.GetBagItem(BagItemType.Consumable, 0);

            Assert.IsTrue(added);
            Assert.IsNotNull(item);
            Assert.AreEqual(101, item.Id);
            Assert.AreEqual(3, item.Count);

            Object.DestroyImmediate(inventoryObject);
        }
    }
}
```

- [ ] **Step 2: Verify RED**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: FAIL because `TryAddItem` does not exist.

- [ ] **Step 3: Make BagInventoryManager persistent**

Change class declaration:

```csharp
public sealed class BagInventoryManager : SingletonManager<BagInventoryManager>
```

Add lifecycle methods to subscribe/unsubscribe `DropItemPickupRequestEventArgs.EventId` and keep inventory available before the bag UI opens.

- [ ] **Step 4: Add TryAddItem and pickup handler**

Implement `TryAddItem(...)` to initialize bag data if needed, find the first empty bag index for the item type, add `BagItemData`, invoke `InventoryChanged`, and return success/failure.

Implement `OnDropItemPickupRequested(...)` to cast `DropItemPickupRequestEventArgs`, call `TryAddItem(...)`, then call `request.Complete(success)`.

- [ ] **Step 5: Make BagPanel reuse the persistent manager**

Replace `BagPanel.EnsureInventory()` with:

```csharp
/// <summary>绑定常驻背包数据管理器，确保 UI 打开前拾取的物品也能保留。</summary>
private void EnsureInventory()
{
    if (m_inventory != null)
    {
        return;
    }

    m_inventory = BagInventoryManager.Instance;
}
```

- [ ] **Step 6: Verify GREEN**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: PASS for persistent inventory and pickup request handling.

---

### Task 6: 创建 WorldDropItem Prefab 并设置 Addressable

**Files:**
- Create: `Assets/Res/Prefabs/Other/WorldDropItem.prefab`
- Create: `Assets/Res/Prefabs/Other/WorldDropItem.prefab.meta`
- Modify: `Assets/AddressableAssetsData/AddressableAssetSettings.asset`
- Modify: one Addressables group asset selected by `AddressableAssetSettingsDefaultObject.Settings.DefaultGroup`

**Interfaces:**
- Produces: Addressable address `WorldDropItem`
- Consumes: `WorldDropItem` component from Task 4

- [ ] **Step 1: Create a temporary editor script for prefab generation**

Create `.aibridge/code/CreateWorldDropItemPrefab.csx` that:

- creates a Cube root named `WorldDropItem`
- scales it to `0.5, 0.5, 0.5`
- sets `BoxCollider.isTrigger = true`
- adds `Rigidbody` with `isKinematic = true` and `useGravity = false`
- adds `WorldDropItem`
- saves to `Assets/Res/Prefabs/Other/WorldDropItem.prefab`
- registers Addressables address `WorldDropItem`

- [ ] **Step 2: Execute prefab generation through Unity**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe code execute --file .aibridge/code/CreateWorldDropItemPrefab.csx --timeout 10000
```

Expected: `Assets/Res/Prefabs/Other/WorldDropItem.prefab` exists and Addressables has entry address `WorldDropItem`.

- [ ] **Step 3: Inspect prefab**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe asset find --name WorldDropItem --format paths
```

Expected: includes `Assets/Res/Prefabs/Other/WorldDropItem.prefab`.

Confirm the Prefab has:

- root name `WorldDropItem`
- `WorldDropItem`
- `BoxCollider` with `isTrigger = true`
- `Rigidbody` with `isKinematic = true` and `useGravity = false`

- [ ] **Step 4: Clean temporary script**

Delete `.aibridge/code/CreateWorldDropItemPrefab.csx` after the Prefab and Addressables entry are generated. Do not commit the temporary generation script.

---

### Task 7: 最终验证与风险检查

**Files:**
- Verify all files changed in previous tasks.

**Interfaces:**
- Verifies: no task changed enemy Prefab files to attach `EnemyDropComponent`
- Verifies: death calls same-object `EnemyDropComponent` once when present
- Verifies: no `EnemyDropResult.cs`、`EnemyDropRoller.cs`、`EnemyDropSpawnSystem.cs`
- Verifies: `WorldDropItem` can be instantiated by Addressables address `WorldDropItem`
- Verifies: pickup event can add item to persistent bag data

- [ ] **Step 1: Run Unity compile**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: exit code 0.

- [ ] **Step 2: Check Unity Error logs**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe get_logs --logType Error
```

Expected: no new errors caused by the drop system.

- [ ] **Step 3: Verify prefab path**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe asset find --name WorldDropItem --format paths
```

Expected: `Assets/Res/Prefabs/Other/WorldDropItem.prefab`.

- [ ] **Step 4: Review git diff**

Run:

```powershell
git status --short
git --no-pager diff -- Assets/Game/Character/Enemy Assets/Game/World Assets/Game/UI/BagInventoryManager.cs Assets/Game/UI/BagPanel.cs Assets/Res/Prefabs/Other Assets/AddressableAssetsData
```

Expected:
- no changes that restore `EnemySpawnManager`
- no use of `MonsterDeadEventArgs`
- no files named `EnemyDropResult.cs`、`EnemyDropRoller.cs`、`EnemyDropSpawnSystem.cs`
- no enemy Prefab modifications made only to attach `EnemyDropComponent`
- no unrelated rollback of user changes in `Assets/Scenes/Scene1.unity`, `Assets/Framework/Interface.meta`, or `docs/combat-hud/`

- [ ] **Step 5: Commit with Chinese message**

After verification passes:

```powershell
git add Assets/Game/Character/Enemy Assets/Game/World Assets/Game/UI/BagInventoryManager.cs Assets/Game/UI/BagPanel.cs Assets/Game/Editor/EnemyDropSystemEditModeTests.cs Assets/Res/Prefabs/Other Assets/AddressableAssetsData docs/superpowers/plans/2026-07-21-enemy-world-drop-system.md
git commit -m "实现敌人死亡地面掉落物系统"
```

---

## Self-Review

- Spec coverage: 覆盖敌人掉落物管理组件、掉落配置、死亡触发掉落、地面掉落物、附近按 F 拾取、事件通知背包、成功后销毁、Cube Prefab、指定路径和 Addressable 地址。
- Placeholder scan: 本计划不包含待填字段；每个任务都给出目标文件、接口、测试或编译验证步骤。
- Type consistency: `EnemyDropComponent`、`EnemyDropItemConfig`、`EnemyDeadEventArgs`、`DropItemPickupRequestEventArgs`、`WorldDropItem.Initialize`、`BagInventoryManager.TryAddItem` 在任务间命名一致。
- Scope check: 不恢复 `EnemySpawnManager`，不使用旧 `MonsterDeadEventArgs`，不新增 `PlayerInventory`，不创建 `EnemyDropResult.cs`、`EnemyDropRoller.cs`、`EnemyDropSpawnSystem.cs`，不加入敌人 Prefab 挂组件步骤，不做复杂 UI 提示。

