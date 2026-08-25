# Enemy Combat Idle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为 GuardMelee 敌人增加独立战斗待机分支，使其在战斗距离内停止寻路和 RootMotion、持续面向目标，并记录 `EnemyCombatIntent.Idle`。

**Architecture:** 新增一个只负责判定战斗待机资格的 `ConditionNodeAsset`，并在现有 `EnemySetIntentNodeAsset` 中增加 `CombatIdle` 动作分支。GuardMelee 行为树通过新的 `SequenceNodeAsset` 把条件与动作组合，并插入 Attack 与 Chase 之间；资源使用幂等 Unity Editor 脚本创建和接线，避免手写 Unity YAML/GUID。

**Tech Stack:** Unity 2022.3.61f1c1、C# 9.0、项目自研 BehaviorTree、NUnit EditMode、AIBridge CLI、UnityEditor AssetDatabase

## Global Constraints

- Unity 版本固定为 `2022.3.61f1c1`，C# 语法不得高于 9.0。
- Unity 编译只能执行 `$CLI compile unity`；`compile dotnet` 不能替代或回退 Unity 编译。
- 所有新增或修改的函数必须添加简体中文注释，说明用途或关键行为。
- 不新增依赖，不恢复已删除的 KeepDistance，不修改攻击、防御、后撤、受击、失衡、死亡和巡逻规则。
- 复用 `EnemyCombatIntent.Idle`；`EnemyMovementComponent.Stop()` 仍只清理移动目的地和 NavMeshAgent，不依赖动画组件。
- CombatIdle 优先级固定为 Attack 之后、Chase 之前；距离超过 `preferredDistance` 时必须在同一帧回落到 Chase。
- 缺少决策配置时使用 `EnemyMovementComponent.StoppingDistance`；缺少上下文、移动组件或目标时条件/动作返回失败。
- 缺少动画组件时仍执行停止、转向和 Idle 意图写入；`idleAnimation` 负责终止移动 RootMotion。
- 执行前必须记录 `git status --short` 和目标文件 diff 作为用户改动基线；提交步骤仅可暂存本计划新增内容。若同一文件的新旧改动无法可靠隔离，跳过该任务提交并保留工作树，不得整文件暂存用户基线。

---

## File Map

- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyShouldCombatIdleNodeAsset.cs` — 战斗待机资格判定。
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySetIntentNodeAsset.cs` — 新增 `CombatIdle` 枚举和运行时动作。
- Create: `Assets/Game/Editor/EnemyCombatIdleEditModeTests.cs` — 条件、动作和实际行为树资产回归测试。
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/ShouldCombatIdle.asset` — GuardMelee 战斗待机条件资产。
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/SetIntentCombatIdle.asset` — GuardMelee 战斗待机动作资产。
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/CombatIdleSequence.asset` — 条件与动作的 Sequence。
- Modify: `Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/GuardMeleeBehaviorTree.asset` — 在 AttackSequence 与 ChaseSequence 之间插入 CombatIdleSequence。
- Create temporarily, then delete: `.aibridge/code/create_enemy_combat_idle_assets.csx` — 使用 Unity API 幂等生成和接线资源，不提交该临时脚本。

### Task 1: 战斗待机条件节点

**Files:**
- Create: `Assets/Game/Editor/EnemyCombatIdleEditModeTests.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyShouldCombatIdleNodeAsset.cs`

**Interfaces:**
- Consumes: `AIController.Blackboard`、`AIController.DecisionProfile`、`AIController.HasAttackerMemory`、`EnemyMovementComponent.StoppingDistance`。
- Produces: `EnemyShouldCombatIdleNodeAsset : ConditionNodeAsset`，由 Task 3 的 `ShouldCombatIdle.asset` 使用。

- [ ] **Step 1: 写入失败的条件节点 EditMode 测试**

创建 `Assets/Game/Editor/EnemyCombatIdleEditModeTests.cs`：

```csharp
using System.Reflection;
using Game.Character.Enemy.AI;
using Game.Character.Enemy.AI.BehaviorTree;
using Game.Character.Enemy.Components;
using Game.Character.Enemy.Config;
using Game.Character.Enemy.Core;
using GameMain2.Framework.Core.BehaviorTree;
using NUnit.Framework;
using UnityEngine;

namespace Game.Character.Enemy.Tests
{
    public sealed class EnemyCombatIdleEditModeTests
    {
        private GameObject enemy;
        private GameObject target;
        private EnemyDefinition definition;
        private EnemyDecisionProfile profile;
        private EnemyBlackboard blackboard;
        private EnemyMovementComponent movement;
        private EnemyMemoryComponent memory;
        private AIController controller;
        private BehaviorTreeContext behaviorContext;
        private EnemyShouldCombatIdleNodeAsset conditionAsset;

        /// <summary>创建战斗待机条件测试所需的敌人、目标、黑板和决策配置。</summary>
        [SetUp]
        public void SetUp()
        {
            enemy = new GameObject("EnemyCombatIdleTest");
            target = new GameObject("Target");
            enemy.transform.position = Vector3.zero;
            target.transform.position = Vector3.forward * 1.5f;

            movement = enemy.AddComponent<EnemyMovementComponent>();
            memory = enemy.AddComponent<EnemyMemoryComponent>();
            controller = enemy.AddComponent<AIController>();

            definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            profile = new EnemyDecisionProfile();
            profile.preferredDistance = 2f;
            definition.SetDecisionProfile(profile);

            blackboard = new EnemyBlackboard();
            blackboard.RememberTarget(target.transform);
            blackboard.SetTargetVisible(true);
            blackboard.SetTargetDistanceFacts(1.5f, true, false);
            controller.SetBlackboardForTests(blackboard);
            controller.StartAI(null, definition);

            behaviorContext = new BehaviorTreeContext(enemy, new BehaviorTreeBlackboard());
            conditionAsset = ScriptableObject.CreateInstance<EnemyShouldCombatIdleNodeAsset>();
        }

        /// <summary>销毁每个测试创建的场景对象和 ScriptableObject。</summary>
        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(enemy);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(conditionAsset);
            Object.DestroyImmediate(definition);
        }

        /// <summary>通过条件资产的受保护入口执行判定，保持测试不依赖序列化资源。</summary>
        private bool EvaluateCondition()
        {
            MethodInfo evaluate = typeof(EnemyShouldCombatIdleNodeAsset).GetMethod(
                "Evaluate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (bool)evaluate.Invoke(conditionAsset, new object[] { behaviorContext });
        }

        /// <summary>验证可见目标位于期望距离内时进入战斗待机。</summary>
        [Test]
        public void CombatIdleCondition_VisibleTargetWithinPreferredDistance_Passes()
        {
            Assert.IsTrue(EvaluateCondition());
        }

        /// <summary>验证仅保留攻击者记忆时仍可在期望距离内进入战斗待机。</summary>
        [Test]
        public void CombatIdleCondition_AttackerMemoryWithinPreferredDistance_Passes()
        {
            blackboard.SetTargetVisible(false);
            memory.RememberAttacker(target.transform);

            Assert.IsTrue(EvaluateCondition());
        }

        /// <summary>验证目标超出期望距离时条件失败，让 Selector 继续执行 Chase。</summary>
        [Test]
        public void CombatIdleCondition_TargetBeyondPreferredDistance_Fails()
        {
            target.transform.position = Vector3.forward * 2.5f;
            blackboard.SetTargetDistanceFacts(2.5f, false, false);

            Assert.IsFalse(EvaluateCondition());
        }

        /// <summary>验证缺少决策配置时使用移动组件的停止距离作为回退阈值。</summary>
        [Test]
        public void CombatIdleCondition_MissingDecisionProfile_UsesStoppingDistance()
        {
            definition.SetDecisionProfile(null);
            blackboard.SetTargetDistanceFacts(movement.StoppingDistance, true, false);

            Assert.IsTrue(EvaluateCondition());
        }
    }
}
```

- [ ] **Step 2: 运行 Unity 编译，确认测试因缺少条件类型而失败**

Run: `$CLI compile unity`

Expected: FAIL，编译错误包含 `EnemyShouldCombatIdleNodeAsset` 类型不存在。

- [ ] **Step 3: 实现最小条件节点**

创建 `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyShouldCombatIdleNodeAsset.cs`：

```csharp
using Game.Character.Enemy.Config;
using Game.Character.Enemy.Core;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Should Combat Idle")]
    public sealed class EnemyShouldCombatIdleNodeAsset : ConditionNodeAsset
    {
        /// <summary>判断敌人是否仍在战斗中且已经位于当前待机距离阈值内。</summary>
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            if (!EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                || controller.Context == null
                || controller.Context.Movement == null)
            {
                return false;
            }

            EnemyBlackboard blackboard = controller.Blackboard;
            if (blackboard.Target == null
                || (!blackboard.IsTargetVisible && !controller.HasAttackerMemory))
            {
                return false;
            }

            EnemyDecisionProfile profile = controller.DecisionProfile;
            float preferredDistance = profile != null
                ? profile.preferredDistance
                : controller.Context.Movement.StoppingDistance;
            return blackboard.DistanceToTarget <= preferredDistance;
        }
    }
}
```

- [ ] **Step 4: 编译并运行条件测试**

Run: `$CLI compile unity`

Expected: `success: true`，无编译错误。

Run: `$CLI test run --mode EditMode --group-name Game.Character.Enemy.Tests.EnemyCombatIdleEditModeTests`

Expected: 4 个测试 PASS。

- [ ] **Step 5: 提交条件节点**

```bash
git add Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyShouldCombatIdleNodeAsset.cs Assets/Game/Editor/EnemyCombatIdleEditModeTests.cs
git commit -m "新增敌人战斗待机条件节点"
```

### Task 2: 战斗待机动作

**Files:**
- Modify: `Assets/Game/Editor/EnemyCombatIdleEditModeTests.cs`
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySetIntentNodeAsset.cs:10`

**Interfaces:**
- Consumes: Task 1 的测试上下文、`EnemyMovementComponent.Stop()`、`EnemyMovementComponent.LookAt(Vector3)`、`EnemyBlackboard.SetCombatIntent(EnemyCombatIntent)`。
- Produces: `EnemyBehaviorActionType.CombatIdle = 9` 和运行时 `TickCombatIdle(AIController)`，由 Task 3 的 `SetIntentCombatIdle.asset` 使用。

- [ ] **Step 1: 扩展测试夹具并写入失败的动作测试**

在测试类字段中加入：

```csharp
private EnemySetIntentNodeAsset actionAsset;
private BehaviorTreeNode actionNode;
```

在 `SetUp()` 末尾加入：

```csharp
actionAsset = ScriptableObject.CreateInstance<EnemySetIntentNodeAsset>();
actionAsset.SetIntentForTests(EnemyBehaviorActionType.CombatIdle);
actionNode = actionAsset.CreateRuntimeNode();
```

在 `TearDown()` 中销毁 `conditionAsset` 后加入：

```csharp
Object.DestroyImmediate(actionAsset);
```

在测试类中加入：

```csharp
/// <summary>验证战斗待机动作停止已有移动并写入 Idle 战斗意图。</summary>
[Test]
public void CombatIdleAction_StopsDestinationAndWritesIdleIntent()
{
    movement.MoveTo(target.transform);
    Assert.IsTrue(movement.HasDestination);

    BehaviorTreeStatus status = actionNode.Tick(behaviorContext);

    Assert.AreEqual(BehaviorTreeStatus.Success, status);
    Assert.IsFalse(movement.HasDestination);
    Assert.AreEqual(EnemyCombatIntent.Idle, blackboard.CurrentIntent);
}

/// <summary>验证战斗待机动作不会在没有目的地时创建新的移动目的地。</summary>
[Test]
public void CombatIdleAction_WithoutDestination_DoesNotStartMovement()
{
    Assert.IsFalse(movement.HasDestination);

    BehaviorTreeStatus status = actionNode.Tick(behaviorContext);

    Assert.AreEqual(BehaviorTreeStatus.Success, status);
    Assert.IsFalse(movement.HasDestination);
}

/// <summary>验证缺少移动组件时战斗待机动作失败，不伪造成功状态。</summary>
[Test]
public void CombatIdleAction_MissingMovement_Fails()
{
    GameObject owner = new GameObject("EnemyCombatIdleWithoutMovement");
    EnemySetIntentNodeAsset asset = ScriptableObject.CreateInstance<EnemySetIntentNodeAsset>();
    try
    {
        EnemyBlackboard board = new EnemyBlackboard();
        board.RememberTarget(target.transform);
        AIController ownerController = owner.AddComponent<AIController>();
        ownerController.SetBlackboardForTests(board);
        ownerController.StartAI(null, definition);
        asset.SetIntentForTests(EnemyBehaviorActionType.CombatIdle);
        BehaviorTreeNode node = asset.CreateRuntimeNode();
        BehaviorTreeContext context = new BehaviorTreeContext(owner, new BehaviorTreeBlackboard());

        Assert.AreEqual(BehaviorTreeStatus.Failure, node.Tick(context));
    }
    finally
    {
        Object.DestroyImmediate(owner);
        Object.DestroyImmediate(asset);
    }
}
```

- [ ] **Step 2: 运行 Unity 编译，确认测试因缺少 CombatIdle 枚举而失败**

Run: `$CLI compile unity`

Expected: FAIL，错误包含 `EnemyBehaviorActionType` 不包含 `CombatIdle`。

- [ ] **Step 3: 在现有动作资产中实现 CombatIdle**

在 `EnemyBehaviorActionType` 中追加稳定数值，避免改变现有序列化枚举值：

```csharp
Retreat = 8,
CombatIdle = 9,
GetHit = 50,
```

在 `EnemyActionNode.Tick()` 的 `Retreat` 分支后加入：

```csharp
case EnemyBehaviorActionType.CombatIdle:
    return TickCombatIdle(controller);
```

在 `TickIdle()` 后加入：

```csharp
/// <summary>停止寻路、持续朝向战斗目标、播放待机动画并写入 Idle 战斗意图。</summary>
private static BehaviorTreeStatus TickCombatIdle(AIController controller)
{
    if (controller.Context == null
        || controller.Context.Movement == null
        || controller.Blackboard.Target == null)
    {
        return BehaviorTreeStatus.Failure;
    }

    controller.Context.Movement.Stop();
    controller.Context.Movement.LookAt(controller.Blackboard.Target.position);
    if (controller.Context.Animation != null)
    {
        controller.Context.Animation.TryPlay(
            controller.Definition != null ? controller.Definition.AnimationConfig.idleAnimation : null);
    }

    controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Idle);
    return BehaviorTreeStatus.Success;
}
```

- [ ] **Step 4: 编译并运行完整战斗待机单元测试**

Run: `$CLI compile unity`

Expected: `success: true`。

Run: `$CLI test run --mode EditMode --group-name Game.Character.Enemy.Tests.EnemyCombatIdleEditModeTests`

Expected: 7 个测试 PASS；动作测试在没有 `EnemyAnimationComponent` 的夹具中仍 PASS，证明表现组件缺失不会阻止停步和意图写入。

- [ ] **Step 5: 提交战斗待机动作**

```bash
git add Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySetIntentNodeAsset.cs Assets/Game/Editor/EnemyCombatIdleEditModeTests.cs
git commit -m "实现敌人战斗待机动作"
```

### Task 3: GuardMelee 行为树资产接线

**Files:**
- Modify: `Assets/Game/Editor/EnemyCombatIdleEditModeTests.cs`
- Create temporarily, then delete: `.aibridge/code/create_enemy_combat_idle_assets.csx`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/ShouldCombatIdle.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/SetIntentCombatIdle.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/CombatIdleSequence.asset`
- Modify: `Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/GuardMeleeBehaviorTree.asset`

**Interfaces:**
- Consumes: Task 1 的 `EnemyShouldCombatIdleNodeAsset`、Task 2 的 `EnemyBehaviorActionType.CombatIdle`、现有 `CompositeNodeAsset.SetChildren(...)`。
- Produces: GuardMelee 根 Selector 顺序 `AttackSequence -> CombatIdleSequence -> ChaseSequence`，CombatIdleSequence 子节点顺序 `ShouldCombatIdle -> SetIntentCombatIdle`。

- [ ] **Step 1: 写入失败的实际资产结构测试**

在测试文件顶部加入：

```csharp
using System.Collections.Generic;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEditor;
```

在同一命名空间内新增测试类：

```csharp
public sealed class EnemyCombatIdleAssetEditModeTests
{
    private const string TreePath =
        "Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/GuardMeleeBehaviorTree.asset";
    private const string SequencePath =
        "Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/CombatIdleSequence.asset";
    private const string ActionPath =
        "Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/SetIntentCombatIdle.asset";

    /// <summary>查找指定名称的根 Selector 子节点索引。</summary>
    private static int FindChildIndex(IReadOnlyList<BehaviorTreeNodeAsset> children, string assetName)
    {
        for (int i = 0; i < children.Count; i++)
        {
            if (children[i] != null && children[i].name == assetName)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>验证 CombatIdle Sequence 位于 Attack 与 Chase 之间。</summary>
    [Test]
    public void GuardMeleeTree_CombatIdleSequence_IsBetweenAttackAndChase()
    {
        BehaviorTreeAsset tree = AssetDatabase.LoadAssetAtPath<BehaviorTreeAsset>(TreePath);
        CompositeNodeAsset root = tree.Root as CompositeNodeAsset;

        Assert.NotNull(root);
        int attackIndex = FindChildIndex(root.Children, "AttackSequence");
        int combatIdleIndex = FindChildIndex(root.Children, "CombatIdleSequence");
        int chaseIndex = FindChildIndex(root.Children, "ChaseSequence");

        Assert.AreEqual(attackIndex + 1, combatIdleIndex);
        Assert.AreEqual(combatIdleIndex + 1, chaseIndex);
    }

    /// <summary>验证 CombatIdle Sequence 依次执行专用条件和 CombatIdle 动作。</summary>
    [Test]
    public void CombatIdleSequence_ContainsConditionThenAction()
    {
        SequenceNodeAsset sequence = AssetDatabase.LoadAssetAtPath<SequenceNodeAsset>(SequencePath);
        EnemySetIntentNodeAsset action = AssetDatabase.LoadAssetAtPath<EnemySetIntentNodeAsset>(ActionPath);

        Assert.NotNull(sequence);
        Assert.AreEqual(2, sequence.Children.Count);
        Assert.IsInstanceOf<EnemyShouldCombatIdleNodeAsset>(sequence.Children[0]);
        Assert.AreSame(action, sequence.Children[1]);

        SerializedObject serializedAction = new SerializedObject(action);
        Assert.AreEqual(
            (int)EnemyBehaviorActionType.CombatIdle,
            serializedAction.FindProperty("intentType").intValue);
    }
}
```

- [ ] **Step 2: 运行资产测试，确认新资产尚不存在**

Run: `$CLI test run --mode EditMode --group-name Game.Character.Enemy.Tests.EnemyCombatIdleAssetEditModeTests`

Expected: FAIL，`CombatIdleSequence` 或 `SetIntentCombatIdle` 加载结果为 null。

- [ ] **Step 3: 编写幂等 Editor 资产生成脚本**

创建 `.aibridge/code/create_enemy_combat_idle_assets.csx`：

```csharp
using System;
using System.Collections.Generic;
using Game.Character.Enemy.AI.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEditor;
using UnityEngine;

const string Root = "Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee";
const string ConditionPath = Root + "/ShouldCombatIdle.asset";
const string ActionPath = Root + "/SetIntentCombatIdle.asset";
const string SequencePath = Root + "/CombatIdleSequence.asset";
const string AttackPath = Root + "/AttackSequence.asset";
const string ChasePath = "Assets/Game/Character/Enemy/Config/BehaviorTrees/Common/Sequence/ChaseSequence.asset";
const string TreePath = Root + "/GuardMeleeBehaviorTree.asset";

EnemyShouldCombatIdleNodeAsset condition =
    AssetDatabase.LoadAssetAtPath<EnemyShouldCombatIdleNodeAsset>(ConditionPath);
if (condition == null)
{
    condition = ScriptableObject.CreateInstance<EnemyShouldCombatIdleNodeAsset>();
    condition.name = "ShouldCombatIdle";
    AssetDatabase.CreateAsset(condition, ConditionPath);
}

EnemySetIntentNodeAsset action = AssetDatabase.LoadAssetAtPath<EnemySetIntentNodeAsset>(ActionPath);
if (action == null)
{
    action = ScriptableObject.CreateInstance<EnemySetIntentNodeAsset>();
    action.name = "SetIntentCombatIdle";
    AssetDatabase.CreateAsset(action, ActionPath);
}
action.SetIntentForTests(EnemyBehaviorActionType.CombatIdle);

SequenceNodeAsset sequence = AssetDatabase.LoadAssetAtPath<SequenceNodeAsset>(SequencePath);
if (sequence == null)
{
    sequence = ScriptableObject.CreateInstance<SequenceNodeAsset>();
    sequence.name = "CombatIdleSequence";
    AssetDatabase.CreateAsset(sequence, SequencePath);
}
sequence.SetChildren(condition, action);

SequenceNodeAsset attack = AssetDatabase.LoadAssetAtPath<SequenceNodeAsset>(AttackPath);
SequenceNodeAsset chase = AssetDatabase.LoadAssetAtPath<SequenceNodeAsset>(ChasePath);
BehaviorTreeAsset tree = AssetDatabase.LoadAssetAtPath<BehaviorTreeAsset>(TreePath);
CompositeNodeAsset root = tree != null ? tree.Root as CompositeNodeAsset : null;
if (attack == null || chase == null || root == null)
{
    throw new InvalidOperationException("GuardMelee 行为树基础资产缺失，无法接入 CombatIdle。");
}

List<BehaviorTreeNodeAsset> children = new List<BehaviorTreeNodeAsset>(root.Children);
children.Remove(sequence);
int attackIndex = children.IndexOf(attack);
int chaseIndex = children.IndexOf(chase);
if (attackIndex < 0 || chaseIndex <= attackIndex)
{
    throw new InvalidOperationException("GuardMelee 根节点中 Attack/Chase 顺序不符合预期。");
}
children.Insert(attackIndex + 1, sequence);
root.SetChildren(children.ToArray());

EditorUtility.SetDirty(condition);
EditorUtility.SetDirty(action);
EditorUtility.SetDirty(sequence);
EditorUtility.SetDirty(root);
AssetDatabase.SaveAssets();
AssetDatabase.Refresh();

return new
{
    assets = new[] { ConditionPath, ActionPath, SequencePath, TreePath },
    warnings = new string[0]
};
```

- [ ] **Step 4: 执行脚本并检查序列化结果**

Run: `$CLI code execute --file .aibridge/code/create_enemy_combat_idle_assets.csx --timeout 30000`

Expected: `assets` 返回四个目标路径且 `warnings` 为空。

Run: `$CLI inspector get_properties --assetPath Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/CombatIdleSequence.asset --includeChildren true`

Expected: `children.Array.size` 为 2，Element 0 是 `ShouldCombatIdle`，Element 1 是 `SetIntentCombatIdle`。

使用 `apply_patch` 删除 `.aibridge/code/create_enemy_combat_idle_assets.csx`；不要提交临时生成脚本。

- [ ] **Step 5: 编译并运行资产回归测试**

Run: `$CLI compile unity`

Expected: `success: true`。

Run: `$CLI test run --mode EditMode --group-name Game.Character.Enemy.Tests.EnemyCombatIdleAssetEditModeTests`

Expected: 2 个测试 PASS。

- [ ] **Step 6: 提交 GuardMelee 资产接线**

```bash
git add Assets/Game/Editor/EnemyCombatIdleEditModeTests.cs Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/ShouldCombatIdle.asset Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/ShouldCombatIdle.asset.meta Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/SetIntentCombatIdle.asset Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/SetIntentCombatIdle.asset.meta Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/CombatIdleSequence.asset Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/CombatIdleSequence.asset.meta Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/GuardMeleeBehaviorTree.asset
git commit -m "接入近战守卫战斗待机分支"
```

### Task 4: 完整验证与 Play Mode 验收

**Files:**
- Verify only: `Assets/Game/Character/Enemy/Prefabs/GuardMeleeEnemy.prefab`
- Verify only: `Assets/Scenes/TestScene.unity`

**Interfaces:**
- Consumes: Task 1-3 的代码、测试和行为树资源。
- Produces: 可重复的编译、日志、EditMode 和 Play Mode 验收证据。

- [ ] **Step 1: 执行最终 Unity 编译与错误日志检查**

Run: `$CLI compile unity`

Expected: `success: true`，Unity 无编译错误。

Run: `$CLI get_logs --logType Error`

Expected: 不包含本次改动产生的错误、Missing Script、丢失资产引用或序列化异常。

- [ ] **Step 2: 执行完整战斗待机 EditMode 测试**

Run: `$CLI test run --mode EditMode --group-name Game.Character.Enemy.Tests.EnemyCombatIdle`

Expected: 条件、动作、资源结构共 9 个测试全部 PASS。

- [ ] **Step 3: 在 TestScene 进行 Play Mode 行为验收**

Run: `$CLI scene load --scenePath Assets/Scenes/TestScene.unity --mode single`

Run: `$CLI editor play`

在 Game View 中让玩家进入 `GuardMeleeEnemy` 的 `preferredDistance`：敌人必须清除寻路目的地、播放 `idleAnimation`、停止水平 RootMotion，并持续转向移动中的玩家。随后让玩家离开 `preferredDistance` 但保持在 `maxChaseDistance` 内：同一帧 Selector 应跳过 CombatIdle 并恢复 Chase，不应出现一帧站桩或继续播放移动 RootMotion。

Run: `$CLI screenshot capture`

Expected: 截图和现场观察能够确认近距离待机与远离后追击两个状态；Console 无新 Error。

Run: `$CLI editor stop`

- [ ] **Step 4: 复查最终改动范围**

Run: `git status --short`

Expected: 相对执行前记录的用户改动基线，只新增计划列出的代码、测试、三个新资产及其 `.meta`、行为树资产；不存在 `.aibridge/code/create_enemy_combat_idle_assets.csx`。

Run: `git diff --check`

Expected: 无空白错误。

---

## Self-Review Results

- Spec coverage: 战斗判定、Attack/CombatIdle/Chase 优先级、停步、转向、Idle 动画、Idle 意图、配置回退、缺失组件处理、EditMode 与 Play Mode 验收均有对应任务。
- Placeholder scan: 无占位标记、笼统“补测试”或未定义接口。
- Type consistency: `EnemyShouldCombatIdleNodeAsset`、`EnemyBehaviorActionType.CombatIdle`、`ShouldCombatIdle.asset`、`SetIntentCombatIdle.asset`、`CombatIdleSequence.asset` 在测试、代码和资源脚本中保持一致。
