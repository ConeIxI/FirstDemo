# Enemy Sound Perception Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 给敌人增加基于范围的玩家动作声音感知。

**Architecture:** 扩展现有 `EnemyPerceptionConfig` 和 `EnemyPerceptionComponent`，由 `AIController.TickAI` 在普通感知刷新时触发声音扫描。声音扫描命中后写入现有警戒记忆，复用已有 `AlertLayer` 调查流程。

**Tech Stack:** Unity 2022.3.61f1c1，C# 9.0，现有敌人行为树和黑板系统。

## Global Constraints

- 使用简体中文注释。
- 每个新增函数必须添加简体中文注释。
- 不新增测试文件和测试代码。
- 不修改任何 `.controller` 文件。
- 不修改 `D:\MyGameProject\UnityGame\FirstGameDemo\.gitignore`。
- Unity 编译只能使用 `$CLI compile unity`。
- 不进行 Play Mode 验证。

---

### Task 1: 感知配置增加声音范围

**Files:**
- Modify: `Assets/Game/Character/Enemy/Config/EnemyPerceptionConfig.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyPerceptionComponent.cs`

**Interfaces:**
- Produces: `EnemyPerceptionConfig.soundRange`
- Produces: `EnemyPerceptionComponent.ApplyConfig(EnemyPerceptionConfig config)` 同步声音范围

- [ ] **Step 1: Add config field**

在 `EnemyPerceptionConfig` 中新增：

```csharp
public float soundRange = 6f;
```

- [ ] **Step 2: Add component field and ApplyConfig assignment**

在 `EnemyPerceptionComponent` 中新增序列化字段：

```csharp
[SerializeField] private float soundRange = 6f;
```

并在 `ApplyConfig` 中同步：

```csharp
soundRange = config.soundRange;
```

### Task 2: 敌人感知玩家动作声音

**Files:**
- Modify: `Assets/Game/Character/Enemy/Components/EnemyPerceptionComponent.cs`
- Modify: `Assets/Game/Character/Enemy/AI/AIController.cs`

**Interfaces:**
- Consumes: `EnemyPerceptionComponent.ScanSoundTarget() : Transform`
- Consumes: `EnemyBlackboard.ObserveTarget(Transform target, bool isInCombatRange, float combatDuration, float alertDuration)`

- [ ] **Step 1: Add sound scan method**

在 `EnemyPerceptionComponent` 中新增 `ScanSoundTarget()`，通过 `targetMask` 找到声音范围内玩家。玩家处于 `Locomotion` 且有移动输入，或处于 `Dodge`、`Attack` 时返回玩家 Transform。

- [ ] **Step 2: Add helper methods**

新增私有方法判断玩家是否发出声音、是否处于移动输入、是否在声音范围内。每个函数添加简体中文注释。

- [ ] **Step 3: Wire AIController**

在 `AIController.TickAI` 中：看不到目标时再调用 `ScanSoundTarget()`；命中且没有战斗目标时调用 `Blackboard.ObserveTarget(soundTarget, false, GetCombatMemoryDuration(), GetAlertMemoryDuration())`，并保持 `SetTargetVisible(false)`。

### Task 3: 编译验证

**Files:**
- No source changes.

**Interfaces:**
- Consumes: `$CLI compile unity`

- [ ] **Step 1: Run Unity compile**

执行 `$CLI compile unity`，确认没有编译错误。

- [ ] **Step 2: Do not enter Play Mode**

不执行 Play Mode、场景运行、AIBridge 输入或截图验证。
