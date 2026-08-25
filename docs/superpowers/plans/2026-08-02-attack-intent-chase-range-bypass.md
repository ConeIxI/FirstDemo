# 超出追击范围生成攻击意图实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** 当玩家超出敌人的追击范围时，攻击意图生成节点可以跳过攻击决策冷却并生成攻击意图。

**Architecture:** 由行为树生成节点读取黑板的 `IsInChaseRange`，将范围事实传入战斗决策器。决策器只在超出追击范围时跳过攻击冷却，低稳定值仍然阻止生成；追击范围内继续沿用原有冷却和攻击欲望规则。

**Tech Stack:** Unity 2022.3.61f1c1、C# 9.0、NUnit EditMode 测试、AIBridge CLI。

## Global Constraints

- Unity 编译只能使用 `$CLI compile unity`。
- C# 代码必须兼容 C# 9.0。
- 修改的函数使用简体中文注释，复杂业务逻辑添加必要简体中文注释。
- 保留用户已有的无关改动，不回滚 `Scene1.unity` 等文件。

---

### Task 1: 添加失败回归测试

**Files:**
- Modify: `Assets/Game/Editor/EnemyAttackIntentNodeEditModeTests.cs`

- [x] **Step 1: 添加测试**

新增测试先生成一次攻击意图并清理黑板意图，再把黑板设置为不在追击范围内，立即再次 Tick 生成节点。预期第二次返回 `Failure` 并再次写入攻击意图。

- [x] **Step 2: 运行 EditMode 测试确认测试失败**

使用 Unity 测试入口运行 `EnemyAttackIntentNodeEditModeTests`，预期新增测试因当前冷却判断返回 `Success` 而失败。

### Task 2: 实现超出追击范围时跳过冷却

**Files:**
- Modify: `Assets/Game/Character/Enemy/AI/Combat/EnemyCombatDecisionController.cs`
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyGenerateAttackIntentNodeAsset.cs`
- Modify: `Assets/Game/Editor/EnemyCombatDecisionControllerEditModeTests.cs`

- [x] **Step 1: 扩展决策器接口**

将 `TryCreateAttackIntent` 增加 `bool isInChaseRange` 参数。

- [x] **Step 2: 调整决策顺序**

当 `isInChaseRange` 为 `false` 时跳过攻击冷却，并将其作为生成攻击意图的强制条件；仍先执行低稳定值判断。范围内保持原有冷却与攻击欲望判定。

- [x] **Step 3: 让生成节点传入黑板事实**

调用决策器时传入 `controller.Blackboard.IsInChaseRange`，并更新中文注释。

- [x] **Step 4: 更新直接调用测试**

为决策器增加“冷却期间超出追击范围仍可生成”的测试，并给既有调用补充范围参数。

### Task 3: 验证

- [x] **Step 1: 运行相关 EditMode 测试**
- [x] **Step 2: 运行 `$CLI compile unity`**
- [x] **Step 3: 读取 Unity 错误日志并确认没有新增编译错误**
