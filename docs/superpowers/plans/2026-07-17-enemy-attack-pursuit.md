# Enemy Attack Pursuit Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让敌人在攻击欲望判定成功后追向玩家，并且只在进入攻击范围后启动现有攻击流程。

**Architecture:** `EnemyShouldAttackNodeAsset` 只负责攻击意图和冷却判定；`EnemySetIntentNodeAsset` 的现有攻击动作负责范围外追击、范围内出手和追击取消。保持 `AttackSequence` 资产不变，避免新增黑板持久状态和行为树资产引用。

**Tech Stack:** Unity 2022.3.61f1c1、C# 9.0、NUnit EditMode Tests、AIBridge CLI

## Global Constraints

- 所有新增或修改的函数必须添加简体中文注释，说明用途或关键行为。
- Unity 编译只能使用 `./.aibridge/cli/AIBridgeCLI.exe compile unity`。
- 不修改 `GuardMeleeEnemyDefinition.asset`、`Scene1.unity` 或行为树序列化资产。
- 不调整攻击欲望、冷却、攻击范围和最大追击距离的配置值。
- 实际攻击启动后继续沿用现有 `canInterruptAttack` 和攻击生命周期规则。

---

### Task 1: 解除攻击决策的距离限制

**Files:**
- Modify: `Assets/Game/Editor/EnemyBehaviorDecisionNodeEditModeTests.cs`
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyShouldAttackNodeAsset.cs:11-33`

**Interfaces:**
- Consumes: `EnemyBlackboard.SetTargetDistanceFacts(float distance, bool isInAttackRange, bool isTooCloseToTarget)`
- Produces: `EnemyShouldAttackNodeAsset.Evaluate(BehaviorTreeContext context)` 在目标位于攻击范围外时也可通过攻击欲望判定。

- [ ] **Step 1: 写入范围外攻击决策失败测试**

在 `EnemyBehaviorDecisionNodeEditModeTests` 中新增以下测试函数：

```csharp
/// <summary>验证攻击意图不依赖攻击范围，敌人可以先决定攻击再接近目标。</summary>
[Test]
public void ShouldAttack_TargetOutsideAttackRange_CanPassDecision()
{
    GameObject owner = new GameObject("EnemyShouldAttackOutsideRangeTest");
    GameObject target = new GameObject("Target");
    EnemyShouldAttackNodeAsset asset = ScriptableObject.CreateInstance<EnemyShouldAttackNodeAsset>();
    EnemyDefinition definition = ScriptableObject.CreateInstance<EnemyDefinition>();
    try
    {
        EnemyDecisionProfile profile = new EnemyDecisionProfile();
        profile.attackDesire = 1f;
        profile.attackCooldown = 1f;
        definition.SetDecisionProfile(profile);

        EnemyBlackboard blackboard = new EnemyBlackboard();
        blackboard.RememberTarget(target.transform);
        blackboard.SetTargetVisible(true);
        blackboard.SetTargetDistanceFacts(4f, false, false);
        blackboard.MarkAttackDecision(Time.time - 2f);

        AIController controller = owner.AddComponent<AIController>();
        controller.SetBlackboardForTests(blackboard);
        controller.StartAI(null, definition);

        BehaviorTreeContext context = new BehaviorTreeContext(owner, new BehaviorTreeBlackboard());
        MethodInfo evaluate = typeof(EnemyShouldAttackNodeAsset).GetMethod(
            "Evaluate",
            BindingFlags.Instance | BindingFlags.NonPublic);

        bool result = (bool)evaluate.Invoke(asset, new object[] { context });

        Assert.IsTrue(result);
    }
    finally
    {
        Object.DestroyImmediate(owner);
        Object.DestroyImmediate(target);
        Object.DestroyImmediate(asset);
        Object.DestroyImmediate(definition);
    }
}
```

- [ ] **Step 2: 运行单测并确认 RED**

Run:

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --test-name 'Game.Character.Enemy.Tests.EnemyBehaviorDecisionNodeEditModeTests.ShouldAttack_TargetOutsideAttackRange_CanPassDecision' --timeout 120000
```

Expected: FAIL，`Assert.IsTrue` 得到 `false`，失败原因是 `EnemyShouldAttackNodeAsset` 仍要求 `IsInAttackRange`。

- [ ] **Step 3: 删除攻击范围前置条件**

将 `EnemyShouldAttackNodeAsset.Evaluate` 中的配置检查改为：

```csharp
/// <summary>根据攻击冷却和攻击欲望判断是否生成攻击意图。</summary>
protected override bool Evaluate(BehaviorTreeContext context)
{
    if (!EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller))
    {
        return false;
    }

    EnemyDecisionProfile profile = controller.DecisionProfile;
    if (profile == null)
    {
        return false;
    }

    float elapsed = Time.time - controller.Blackboard.LastAttackDecisionTime;
    if (elapsed < profile.attackCooldown)
    {
        return false;
    }

    // 冷却窗口到达后立即记录本次尝试，避免失败随机在每帧反复重投。
    controller.Blackboard.MarkAttackDecision(Time.time);
    return EnemyDecisionRandom.Passes(profile.attackDesire, Random.value);
}
```

- [ ] **Step 4: 运行决策节点测试并确认 GREEN**

Run:

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --group-name 'Game.Character.Enemy.Tests.EnemyBehaviorDecisionNodeEditModeTests' --timeout 120000
```

Expected: PASS，现有“概率失败也消耗决策窗口”测试和新增范围外测试均通过。

- [ ] **Step 5: 提交攻击决策改动**

```powershell
git add -- 'Assets/Game/Editor/EnemyBehaviorDecisionNodeEditModeTests.cs' 'Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyShouldAttackNodeAsset.cs'
git commit -m '修复：解除攻击决策的距离限制'
```

### Task 2: 攻击动作在范围外追向目标

**Files:**
- Create: `Assets/Game/Editor/EnemyAttackPursuitEditModeTests.cs`
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySetIntentNodeAsset.cs:245-300`

**Interfaces:**
- Consumes: `EnemyCombatComponent.IsInAttackRange(Transform target)`、`EnemyMovementComponent.MoveTo(Transform target)`。
- Produces: `TickAttackPursuit(AIController controller) -> BehaviorTreeStatus`；范围外返回 `Running`，不调用 `StartAttack`。

- [ ] **Step 1: 创建攻击追击测试夹具和失败测试**

创建 `EnemyAttackPursuitEditModeTests.cs`：

```csharp
using Game.Character.Enemy.AI;
using Game.Character.Enemy.AI.BehaviorTree;
using Game.Character.Enemy.Components;
using Game.Character.Enemy.Config;
using Game.Character.Enemy.Core;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace Game.Character.Enemy.Tests
{
    public sealed class EnemyAttackPursuitEditModeTests
    {
        private GameObject enemy;
        private GameObject target;
        private EnemyDefinition definition;
        private EnemySetIntentNodeAsset attackAsset;
        private BehaviorTreeNode attackNode;
        private BehaviorTreeContext behaviorContext;
        private EnemyBlackboard blackboard;
        private EnemyMovementComponent movement;
        private EnemyCombatComponent combat;

        /// <summary>创建只验证攻击追击所需的敌人、目标和行为树动作上下文。</summary>
        [SetUp]
        public void SetUp()
        {
            enemy = new GameObject("EnemyAttackPursuitTest");
            target = new GameObject("Target");
            enemy.transform.position = Vector3.zero;
            target.transform.position = Vector3.forward * 4f;

            movement = enemy.AddComponent<EnemyMovementComponent>();
            combat = enemy.AddComponent<EnemyCombatComponent>();
            AIController controller = enemy.AddComponent<AIController>();

            definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            EnemyCombatConfig combatConfig = new EnemyCombatConfig();
            combatConfig.defaultAttackRange = 2f;
            definition.SetCombatConfig(combatConfig);
            EnemyDecisionProfile profile = new EnemyDecisionProfile();
            profile.maxChaseDistance = 12f;
            definition.SetDecisionProfile(profile);

            blackboard = new EnemyBlackboard();
            blackboard.RememberTarget(target.transform);
            blackboard.SetTargetVisible(true);
            blackboard.SetTargetDistanceFacts(4f, false, false);
            controller.SetBlackboardForTests(blackboard);
            controller.StartAI(null, definition);

            // 阻止旧实现进入技能配置加载，让失败只体现“未追击而直接出手”。
            combat.StartDefense(10f);
            attackAsset = ScriptableObject.CreateInstance<EnemySetIntentNodeAsset>();
            attackAsset.SetIntentForTests(EnemyBehaviorActionType.Attack);
            attackAsset.SetSkillIdForTests(20001);
            attackNode = attackAsset.CreateRuntimeNode();
            behaviorContext = new BehaviorTreeContext(enemy, new BehaviorTreeBlackboard());
        }

        /// <summary>销毁测试创建的运行时对象和 ScriptableObject。</summary>
        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(enemy);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(attackAsset);
            Object.DestroyImmediate(definition);
        }

        /// <summary>验证攻击动作在范围外只追向目标，不立即启动攻击。</summary>
        [Test]
        public void AttackAction_TargetOutsideRange_PursuesWithoutAttacking()
        {
            BehaviorTreeStatus status = attackNode.Tick(behaviorContext);

            Assert.AreEqual(BehaviorTreeStatus.Running, status);
            Assert.IsTrue(movement.HasDestination);
            Assert.IsFalse(combat.IsActing);
        }
    }
}
```

- [ ] **Step 2: 运行攻击追击测试并确认 RED**

Run:

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --test-name 'Game.Character.Enemy.Tests.EnemyAttackPursuitEditModeTests.AttackAction_TargetOutsideRange_PursuesWithoutAttacking' --timeout 120000
```

Expected: FAIL，旧实现停止移动并尝试启动攻击，返回 `Failure`，`movement.HasDestination` 为 `false`。

- [ ] **Step 3: 在攻击启动前加入范围外追击**

在 `TickAttack` 清理 `activeAttackAnimation` 后、计算技能编号前插入：

```csharp
Transform target = controller.Blackboard.Target;
if (target == null)
{
    ResetAttackProgress();
    return BehaviorTreeStatus.Failure;
}

if (!controller.Context.Combat.IsInAttackRange(target))
{
    return TickAttackPursuit(controller);
}
```

在 `StartAttack` 前新增：

```csharp
// 攻击意图成立但距离不足时持续接近目标，不提前启动战斗动作。
private static BehaviorTreeStatus TickAttackPursuit(AIController controller)
{
    if (controller.Context.Movement == null)
    {
        return BehaviorTreeStatus.Failure;
    }

    if (controller.Context.Animation != null)
    {
        controller.Context.Animation.TryPlay(
            controller.Definition != null ? controller.Definition.AnimationConfig.moveAnimation : null);
    }

    controller.Context.Movement.MoveTo(controller.Blackboard.Target);
    return BehaviorTreeStatus.Running;
}
```

- [ ] **Step 4: 运行攻击追击测试并确认 GREEN**

Run:

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --group-name 'Game.Character.Enemy.Tests.EnemyAttackPursuitEditModeTests' --timeout 120000
```

Expected: PASS，攻击动作返回 `Running`，移动组件持有追击目的地，战斗动作未启动。

- [ ] **Step 5: 提交攻击追击改动**

```powershell
git add -- 'Assets/Game/Editor/EnemyAttackPursuitEditModeTests.cs' 'Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySetIntentNodeAsset.cs'
git commit -m '功能：攻击意图成立后追向玩家'
```

### Task 3: 取消无效的攻击追击

**Files:**
- Modify: `Assets/Game/Editor/EnemyAttackPursuitEditModeTests.cs`
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySetIntentNodeAsset.cs:245-320`

**Interfaces:**
- Consumes: `EnemyBlackboard` 的目标、可见性、死亡、受击和失衡事实，以及 `EnemyDecisionProfile.maxChaseDistance`。
- Produces: `ShouldCancelAttackPursuit(AIController controller) -> bool`、`CancelAttackPursuit(AIController controller) -> BehaviorTreeStatus`。

- [ ] **Step 1: 添加取消追击测试辅助函数**

在测试类中新增：

```csharp
/// <summary>先建立追击，再触发指定黑板变化并验证动作取消且停止移动。</summary>
private void AssertPursuitCancellation(System.Action<EnemyBlackboard> triggerCancellation)
{
    Assert.AreEqual(BehaviorTreeStatus.Running, attackNode.Tick(behaviorContext));

    triggerCancellation(blackboard);

    Assert.AreEqual(BehaviorTreeStatus.Failure, attackNode.Tick(behaviorContext));
    Assert.IsFalse(movement.HasDestination);
}
```

- [ ] **Step 2: 添加目标和高优先级状态取消测试**

在测试类中新增：

```csharp
/// <summary>验证目标丢失后立即取消尚未出手的攻击追击。</summary>
[Test]
public void AttackAction_TargetLost_CancelsPursuit()
{
    AssertPursuitCancellation(board => board.ForgetTarget());
}

/// <summary>验证目标不可见且没有攻击者记忆时取消攻击追击。</summary>
[Test]
public void AttackAction_TargetVisibilityLost_CancelsPursuit()
{
    AssertPursuitCancellation(board => board.SetTargetVisible(false));
}

/// <summary>验证收到待处理受击反应时取消尚未出手的攻击追击。</summary>
[Test]
public void AttackAction_HitReactionPending_CancelsPursuit()
{
    AssertPursuitCancellation(board => board.SetHitReaction("GetHit"));
}

/// <summary>验证进入失衡状态时取消尚未出手的攻击追击。</summary>
[Test]
public void AttackAction_Unbalanced_CancelsPursuit()
{
    AssertPursuitCancellation(board => board.SetUnbalanced(true));
}

/// <summary>验证死亡时取消尚未出手的攻击追击。</summary>
[Test]
public void AttackAction_Dead_CancelsPursuit()
{
    AssertPursuitCancellation(board => board.SetDead(true));
}
```

- [ ] **Step 3: 添加超出最大追击距离的取消测试**

```csharp
/// <summary>验证目标超过决策配置的最大追击距离时取消攻击追击。</summary>
[Test]
public void AttackAction_TargetBeyondMaxChaseDistance_CancelsPursuit()
{
    Assert.AreEqual(BehaviorTreeStatus.Running, attackNode.Tick(behaviorContext));

    target.transform.position = Vector3.forward * 13f;
    blackboard.SetTargetDistanceFacts(13f, false, false);

    Assert.AreEqual(BehaviorTreeStatus.Failure, attackNode.Tick(behaviorContext));
    Assert.IsFalse(movement.HasDestination);
}
```

- [ ] **Step 4: 运行取消测试并确认 RED**

Run:

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --group-name 'Game.Character.Enemy.Tests.EnemyAttackPursuitEditModeTests' --timeout 120000
```

Expected: FAIL；Task 2 实现仍会继续追逐不可见、过远、受击或失衡目标，目标丢失时也未清理已有移动目的地。

- [ ] **Step 5: 实现追击取消条件**

将 Task 2 中的空目标判断和范围外判断替换为：

```csharp
if (ShouldCancelAttackPursuit(controller))
{
    return CancelAttackPursuit(controller);
}

if (!controller.Context.Combat.IsInAttackRange(controller.Blackboard.Target))
{
    return TickAttackPursuit(controller);
}
```

在 `TickAttackPursuit` 后新增：

```csharp
// 判断尚未出手的攻击追击是否因目标或高优先级战斗事实失效。
private static bool ShouldCancelAttackPursuit(AIController controller)
{
    if (controller.Blackboard.Target == null
        || controller.Blackboard.IsDead
        || controller.Blackboard.IsUnbalanced
        || controller.Blackboard.HasHitReaction
        || controller.Blackboard.IsHitReactionInProgress)
    {
        return true;
    }

    if (!controller.Blackboard.IsTargetVisible && !controller.HasAttackerMemory)
    {
        return true;
    }

    EnemyDecisionProfile profile = controller.DecisionProfile;
    return profile != null && controller.Blackboard.DistanceToTarget > profile.maxChaseDistance;
}

// 取消尚未出手的攻击追击，并清理移动目的地和本次攻击进度。
private BehaviorTreeStatus CancelAttackPursuit(AIController controller)
{
    if (controller.Context.Movement != null)
    {
        controller.Context.Movement.Stop();
    }

    ResetAttackProgress();
    return BehaviorTreeStatus.Failure;
}
```

- [ ] **Step 6: 运行攻击追击测试并确认 GREEN**

Run:

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --group-name 'Game.Character.Enemy.Tests.EnemyAttackPursuitEditModeTests' --timeout 120000
```

Expected: PASS，范围外追击和全部取消路径均通过。

- [ ] **Step 7: 运行敌人 EditMode 回归测试**

Run:

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --group-name 'Game.Character.Enemy.Tests' --timeout 120000
```

Expected: PASS，敌人决策、黑板、移动、防御和攻击追击测试全部通过。

- [ ] **Step 8: 提交取消逻辑**

```powershell
git add -- 'Assets/Game/Editor/EnemyAttackPursuitEditModeTests.cs' 'Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySetIntentNodeAsset.cs'
git commit -m '修复：攻击追击失效时及时取消'
```

### Task 4: Unity 编译与日志验证

**Files:**
- Verify only: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyShouldAttackNodeAsset.cs`
- Verify only: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySetIntentNodeAsset.cs`
- Verify only: `Assets/Game/Editor/EnemyBehaviorDecisionNodeEditModeTests.cs`
- Verify only: `Assets/Game/Editor/EnemyAttackPursuitEditModeTests.cs`

**Interfaces:**
- Consumes: Tasks 1-3 的全部提交。
- Produces: Unity 编译、Error 日志和工作树范围验证结果。

- [ ] **Step 1: 执行 Unity 编译**

Run:

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' compile unity
```

Expected: `success: true`，Unity 2022.3.61f1c1 编译完成且无编译错误。

- [ ] **Step 2: 检查 Unity Error 日志**

Run:

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' get_logs --logType Error
```

Expected: 没有由本次敌人攻击追击改动产生的新 Error。

- [ ] **Step 3: 检查最终工作树范围**

Run:

```powershell
git status --short
```

Expected: 用户原有的 `GuardMeleeEnemyDefinition.asset`、`Scene1.unity` 改动仍保留且未被提交；本计划涉及的代码和测试文件没有未提交改动。
