# Enemy CameraTarget Lock-On Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将敌人碰撞体检测结果统一解析为敌人根节点下唯一的 `CameraTarget` 锁定点。

**Architecture:** `LockOnManager` 保留现有物理范围检测，在候选过滤前新增一个私有静态解析步骤：Collider 向父级定位 `EnemyAgent`，再读取其直接子对象 `CameraTarget`，并按 Transform 去重。后续视野、遮挡、屏幕排序和锁定状态继续消费 Transform 列表，因此自然统一到唯一锁定点。

**Tech Stack:** Unity 2022.3.61f1c1、C# 9.0、NUnit EditMode Tests、AIBridge CLI

## Global Constraints

- Unity C# 语法必须兼容 C# 9.0。
- 每个新增函数必须添加简体中文注释，说明用途或关键行为。
- `CameraTarget` 必须是 `EnemyAgent` 根节点的直接子对象，名称区分大小写。
- 缺少 `EnemyAgent` 或 `CameraTarget` 时跳过该候选，不回退到碰撞体 Transform。
- Unity 编译验证只能使用 `$CLI compile unity`。

---

### Task 1: 将敌人碰撞体归并为唯一 CameraTarget

**Files:**
- Create: `Assets/Game/Editor/LockOnManagerEditModeTests.cs`
- Modify: `Assets/Game/Character/Player/LockOnManager.cs:1-5`
- Modify: `Assets/Game/Character/Player/LockOnManager.cs:324-364`

**Interfaces:**
- Consumes: `Game.Character.Enemy.Core.EnemyAgent`、`Collider.GetComponentInParent<T>()`、`Transform.Find(string)`。
- Produces: 私有静态方法 `List<Transform> CollectLockOnTargets(Collider[] colliders)`，供 `GetValidTargets()` 消费唯一锁定点列表。

- [ ] **Step 1: 编写失败的 EditMode 测试**

创建 `Assets/Game/Editor/LockOnManagerEditModeTests.cs`：

```csharp
using System.Collections.Generic;
using System.Reflection;
using Game.Character.Enemy.Core;
using NUnit.Framework;
using UnityEngine;

namespace GameMain2.Scripts.Character.Tests
{
    public sealed class LockOnManagerEditModeTests
    {
        private readonly List<GameObject> testObjects = new List<GameObject>();

        /// <summary>销毁测试创建的对象，避免对象跨用例残留。</summary>
        [TearDown]
        public void TearDown()
        {
            for (int i = testObjects.Count - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(testObjects[i]);
            }

            testObjects.Clear();
        }

        /// <summary>验证子碰撞体会解析为敌人根节点下的 CameraTarget。</summary>
        [Test]
        public void CollectLockOnTargets_ChildCollider_ReturnsCameraTarget()
        {
            GameObject enemy = CreateObject("Enemy");
            enemy.AddComponent<EnemyAgent>();
            Transform cameraTarget = CreateChild(enemy.transform, "CameraTarget").transform;
            Collider collider = CreateChild(enemy.transform, "HitBox").AddComponent<BoxCollider>();

            List<Transform> targets = InvokeCollectLockOnTargets(new[] { collider });

            CollectionAssert.AreEqual(new[] { cameraTarget }, targets);
        }

        /// <summary>验证同一敌人的多个碰撞体只生成一个锁定点。</summary>
        [Test]
        public void CollectLockOnTargets_MultipleColliders_ReturnsSingleTarget()
        {
            GameObject enemy = CreateObject("Enemy");
            enemy.AddComponent<EnemyAgent>();
            Transform cameraTarget = CreateChild(enemy.transform, "CameraTarget").transform;
            Collider first = CreateChild(enemy.transform, "FirstHitBox").AddComponent<BoxCollider>();
            Collider second = CreateChild(enemy.transform, "SecondHitBox").AddComponent<CapsuleCollider>();

            List<Transform> targets = InvokeCollectLockOnTargets(new[] { first, second });

            CollectionAssert.AreEqual(new[] { cameraTarget }, targets);
        }

        /// <summary>验证没有 EnemyAgent 归属的碰撞体不会成为锁定目标。</summary>
        [Test]
        public void CollectLockOnTargets_WithoutEnemyAgent_ReturnsEmpty()
        {
            Collider collider = CreateObject("HitBox").AddComponent<BoxCollider>();

            List<Transform> targets = InvokeCollectLockOnTargets(new[] { collider });

            Assert.IsEmpty(targets);
        }

        /// <summary>验证敌人缺少 CameraTarget 时不会回退锁定碰撞体。</summary>
        [Test]
        public void CollectLockOnTargets_WithoutCameraTarget_ReturnsEmpty()
        {
            GameObject enemy = CreateObject("Enemy");
            enemy.AddComponent<EnemyAgent>();
            Collider collider = CreateChild(enemy.transform, "HitBox").AddComponent<BoxCollider>();

            List<Transform> targets = InvokeCollectLockOnTargets(new[] { collider });

            Assert.IsEmpty(targets);
        }

        /// <summary>验证失活的 CameraTarget 不会被重新选为锁定目标。</summary>
        [Test]
        public void CollectLockOnTargets_InactiveCameraTarget_ReturnsEmpty()
        {
            GameObject enemy = CreateObject("Enemy");
            enemy.AddComponent<EnemyAgent>();
            GameObject cameraTarget = CreateChild(enemy.transform, "CameraTarget");
            cameraTarget.SetActive(false);
            Collider collider = CreateChild(enemy.transform, "HitBox").AddComponent<BoxCollider>();

            List<Transform> targets = InvokeCollectLockOnTargets(new[] { collider });

            Assert.IsEmpty(targets);
        }

        /// <summary>验证嵌套层级中的 CameraTarget 不满足直接子对象约束。</summary>
        [Test]
        public void CollectLockOnTargets_NestedCameraTarget_ReturnsEmpty()
        {
            GameObject enemy = CreateObject("Enemy");
            enemy.AddComponent<EnemyAgent>();
            GameObject model = CreateChild(enemy.transform, "Model");
            CreateChild(model.transform, "CameraTarget");
            Collider collider = CreateChild(model.transform, "HitBox").AddComponent<BoxCollider>();

            List<Transform> targets = InvokeCollectLockOnTargets(new[] { collider });

            Assert.IsEmpty(targets);
        }

        /// <summary>验证锁定点名称区分大小写。</summary>
        [Test]
        public void CollectLockOnTargets_WrongCaseCameraTarget_ReturnsEmpty()
        {
            GameObject enemy = CreateObject("Enemy");
            enemy.AddComponent<EnemyAgent>();
            CreateChild(enemy.transform, "cameraTarget");
            Collider collider = CreateChild(enemy.transform, "HitBox").AddComponent<BoxCollider>();

            List<Transform> targets = InvokeCollectLockOnTargets(new[] { collider });

            Assert.IsEmpty(targets);
        }

        /// <summary>调用锁定点收集方法，并在生产方法尚未实现时给出明确失败。</summary>
        private static List<Transform> InvokeCollectLockOnTargets(Collider[] colliders)
        {
            MethodInfo method = typeof(LockOnManager).GetMethod(
                "CollectLockOnTargets",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(method, "LockOnManager 应实现 CollectLockOnTargets 方法。");
            return (List<Transform>)method.Invoke(null, new object[] { colliders });
        }

        /// <summary>创建并记录测试对象，交由 TearDown 统一销毁。</summary>
        private GameObject CreateObject(string name)
        {
            GameObject gameObject = new GameObject(name);
            testObjects.Add(gameObject);
            return gameObject;
        }

        /// <summary>创建指定父节点下的测试子对象。</summary>
        private GameObject CreateChild(Transform parent, string name)
        {
            GameObject child = CreateObject(name);
            child.transform.SetParent(parent);
            return child;
        }
    }
}
```

- [ ] **Step 2: 运行测试并确认按预期失败**

Run:

```powershell
& './.aibridge/cli/AIBridgeCLI.exe' test run --mode EditMode --group-name GameMain2.Scripts.Character.Tests.LockOnManagerEditModeTests --timeout 120000
```

Expected: FAIL，失败信息包含 `LockOnManager 应实现 CollectLockOnTargets 方法。`。

- [ ] **Step 3: 实现唯一锁定点解析并接入候选过滤**

在 `LockOnManager.cs` 添加命名空间：

```csharp
using Game.Character.Enemy.Core;
```

在类内添加常量和解析方法：

```csharp
private const string CameraTargetName = "CameraTarget";

/// <summary>将敌人碰撞体解析为根节点下唯一的 CameraTarget 锁定点。</summary>
private static List<Transform> CollectLockOnTargets(Collider[] colliders)
{
    var targets = new List<Transform>();
    var uniqueTargets = new HashSet<Transform>();

    foreach (Collider collider in colliders)
    {
        EnemyAgent enemyAgent = collider.GetComponentInParent<EnemyAgent>();
        if (enemyAgent == null)
        {
            continue;
        }

        Transform target = enemyAgent.transform.Find(CameraTargetName);
        if (target == null || !target.gameObject.activeInHierarchy || !uniqueTargets.Add(target))
        {
            continue;
        }

        targets.Add(target);
    }

    return targets;
}
```

在 `GetValidTargets()` 中保留 `Physics.OverlapSphere`，随后生成唯一锁定点列表并遍历该列表：

```csharp
Collider[] colliders = Physics.OverlapSphere(transform.position, lockOnRange, enemyLayer);
List<Transform> lockOnTargets = CollectLockOnTargets(colliders);
Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

var results = new List<(Transform t, float screenDist)>();

foreach (Transform target in lockOnTargets)
{
    Vector3 dir = (target.position - transform.position).normalized;

    if (Vector3.Dot(mainCamera.transform.forward, dir) < lockOnFovDot)
    {
        continue;
    }

    if (Physics.Linecast(
        transform.position + Vector3.up * 1.5f,
        target.position,
        ~enemyLayer))
    {
        continue;
    }

    Vector2 screenPos = mainCamera.WorldToScreenPoint(target.position);
    float dist = Vector2.Distance(screenPos, screenCenter);
    results.Add((target, dist));
}
```

- [ ] **Step 4: 运行定向测试并确认通过**

Run:

```powershell
& './.aibridge/cli/AIBridgeCLI.exe' test run --mode EditMode --group-name GameMain2.Scripts.Character.Tests.LockOnManagerEditModeTests --timeout 120000
```

Expected: PASS，7 个测试全部通过。

- [ ] **Step 5: 执行 Unity 编译和错误日志验证**

Run:

```powershell
& './.aibridge/cli/AIBridgeCLI.exe' compile unity
& './.aibridge/cli/AIBridgeCLI.exe' get_logs --logType Error
```

Expected: Unity 编译成功，错误日志中没有本次修改引入的错误。

- [ ] **Step 6: 检查差异并提交实现**

```powershell
git diff --check
git add Assets/Game/Editor/LockOnManagerEditModeTests.cs Assets/Game/Editor/LockOnManagerEditModeTests.cs.meta Assets/Game/Character/Player/LockOnManager.cs docs/superpowers/plans/2026-07-16-lock-on-camera-target.md
git commit -m "fix: use unique enemy camera target for lock-on"
```
