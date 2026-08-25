# 敌人攻击距离检测开关 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 `EnemyDefinition` 的战斗配置中，让每个 `EnemyAttackConfig` 独立配置“启用攻击距离检测”，默认开启，关闭后当前攻击动作不再因距离条件被阻挡。

**Architecture:** 把开关放进每个攻击动作配置，并通过 `EnemyAttackRuntimeConfig` 传递到战斗决策与攻击行为树。释放、连招续段和攻击结束接力只读取当前动作的开关；默认保持现有行为，关闭时只绕过当前动作的距离门槛，不改朝向和其他攻击阶段逻辑。

**Tech Stack:** Unity 2022.3.61f1, C# 9.0, EditMode Tests

---

### Task 1: 扩展攻击动作配置

**Files:**
- Modify: `Assets/Game/Character/Enemy/Config/EnemyAttackConfig.cs`
- Modify: `Assets/Game/Character/Enemy/AI/Combat/EnemyAttackRuntimeConfig.cs`
- Modify: `Assets/Game/Editor/EnemyDefinitionEditor.cs`

- [x] **Step 1: Add the new config field**

`EnemyAttackConfig` 增加 `enableAttackDistanceCheck = true`，编辑器递归绘制攻击数组元素时显示该字段。

- [x] **Step 2: Expose the field in the Chinese inspector**

运行时攻击配置暴露同名只读属性，供当前攻击动作读取。

- [x] **Step 3: Keep the field visible under combat config in the existing inspector layout**

Run: no extra command, the serialized field is drawn through the existing nested config drawer.

### Task 2: 按当前攻击动作接入距离判断

**Files:**
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyPrepareAttackPlanNodeAsset.cs`
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyAttackFlowNodeAsset.cs`
- Modify: `Assets/Game/Character/Enemy/AI/Combat/EnemyCombatDecisionController.cs`
- Modify: `Assets/Game/Editor/EnemyBehaviorTreeAssetBuilder.cs`

- [x] **Step 1: Remove the combat-level distance switch**

距离事实仍按战斗组件计算，不能在尚未选出攻击动作时使用某个动作的开关。

- [x] **Step 2: Use each selected action's switch**

攻击池筛选、反击计划、攻击释放、连招下一段和攻击结束接力分别读取动作级开关；关闭时只跳过当前动作的距离门槛。

### Task 3: Add regression tests

**Files:**
- Modify: `Assets/Game/Editor/EnemyCombatLayerEditModeTests.cs`
- Modify: `Assets/Game/Editor/EnemyDefinitionValidatorEditModeTests.cs` if validation needs a coverage touch

- [x] **Step 1: Add a test for default-on behavior**

```csharp
Assert.IsTrue(definition.CombatConfig.enableAttackDistanceCheck);
```

- [x] **Step 2: Add tests proving disabled distance checks still allow attack flow and combo continuation**

```csharp
definition.CombatConfig.basicAttacks[0].enableAttackDistanceCheck = false;
```

- [x] **Step 3: Run the relevant EditMode tests**

Run: `Unity compile test editmode`
Expected: the enemy combat and definition tests pass.
