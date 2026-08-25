# Enemy AI Architecture Refactor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 重构敌人 AI 为“通用行为树 + 项目 FSM + 组件化执行”的数据驱动架构，并彻底替换旧 `EnemyBrain`、`EnemyActor`、`EnemyStateMachine` 敌人链路。

**Architecture:** `EnemyAgent` 负责装配和统一 Tick，`AIController` 负责行为树决策和 FSM 状态切换。通用 `BehaviorTreeRunner` 返回 `BehaviorTreeStatus`，敌人行为树节点只写入 `EnemyIntent`；`FsmBase<AIController>` 执行动作状态，动作状态通过 `EnemyStateContext` 调用移动、感知、动画、战斗、生命、属性组件。

**Tech Stack:** Unity 2022.3.61f1c1、C# 9.0、ScriptableObject、Newtonsoft JSON、NavMeshAgent、CharacterController、`Assets/Framework/Core/BehaviorTree`、`Assets/Framework/Core/FSM`、`SkillRunner`、`Combatant`、`DamageResolver`、AIBridge CLI。

---

## 执行规则

- 每个新增或修改的函数必须写简体中文注释，说明用途或关键行为。
- Unity 编译只使用 `.\.aibridge\cli\AIBridgeCLI.exe compile unity`。
- 测试使用 `.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name <fixture>`。
- 不使用 `dotnet build` 替代 Unity 编译。
- 执行前用独立 worktree 或干净分支，当前主工作区存在敌人相关未提交改动，不能直接覆盖用户改动。
- 删除旧架构只在新架构测试和 Unity 编译通过后执行，并用 `git add -A` 暂存删除。

## 当前代码事实

- 通用行为树已在 `Assets/Framework/Core/BehaviorTree/` 实现，核心类为 `BehaviorTreeAsset`、`BehaviorTreeRunner`、`BehaviorTreeStatus`、`BehaviorTreeBlackboard`、`BehaviorTreeContext`、`BehaviorTreeNodeAsset`、`ActionNodeAsset`、`ConditionNodeAsset`。
- 通用 FSM 已在 `Assets/Framework/Core/FSM/` 实现，`FsmBase<T>` 支持 `SetStartState(Type)` 和泛型 `ChangeState<T>()`，缺少运行时 `Type` 切换接口。
- 旧敌人链路包含 `EnemyStateMachine`、`EnemyBrain`、`EnemyFsm/Common/*State`、`EnemyMovement`、`EnemyPerception`、`EnemyCombat`、`EnemySkillManager`。
- 中间方案链路包含 `Assets/Game/Character/Enemy/Actor/*`，其中 `EnemyActor` 集成过多职责，计划不沿用。
- 战斗链路继续复用 `SkillRunner.Cast`、`SkillRunner.BeginHitWindow`、`SkillRunner.CancelCurrentSkill`、`Combatant`、`DamageResolver`、`CombatReaction`。
- 当前场景 `Assets/Scenes/Scene1.unity` 中有 `Boss`、`Boss (1)`、`Boss (2)`，它们挂有旧敌人组件或中间方案组件。

## 文件结构

### Framework

- Modify: `Assets/Framework/Core/FSM/FsmBase.cs`
- Test: `Assets/Game/Editor/FsmRuntimeTypeTransitionEditModeTests.cs`

### Enemy Core

- Create: `Assets/Game/Character/Enemy/Core/EnemyAgent.cs`
- Create: `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs`
- Create: `Assets/Game/Character/Enemy/Core/EnemyStateContext.cs`
- Create: `Assets/Game/Character/Enemy/AI/EnemyIntent.cs`
- Create: `Assets/Game/Character/Enemy/AI/EnemyStateId.cs`
- Create: `Assets/Game/Character/Enemy/AI/AIController.cs`
- Test: `Assets/Game/Editor/EnemyBlackboardEditModeTests.cs`
- Test: `Assets/Game/Editor/AIControllerEditModeTests.cs`

### Enemy Config

- Create: `Assets/Game/Character/Enemy/Config/EnemyDefinition.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyMovementConfig.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyPerceptionConfig.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyAnimationConfig.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyLifeConfig.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyAttributeConfig.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyAttributeConfigRow.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyAttributeConfigTable.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyDefinitionValidator.cs`
- Create: `Assets/Data/EnemyAttributeConfig.json`
- Test: `Assets/Game/Editor/EnemyDefinitionValidatorEditModeTests.cs`
- Test: `Assets/Game/Editor/EnemyAttributeConfigTableEditModeTests.cs`

### Enemy Behavior Tree Nodes

- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyBehaviorTreeUtility.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyCanSeeTargetNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyHasTargetMemoryNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyIsInAttackRangeNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyIsDeadNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyHasHitReactionNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyIsUnbalancedNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyIsHealthBelowNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemySetIntentNodeAsset.cs`
- Test: `Assets/Game/Editor/EnemyBehaviorTreeNodeEditModeTests.cs`

### Enemy Components

- Create: `Assets/Game/Character/Enemy/Components/EnemyMovementComponent.cs`
- Create: `Assets/Game/Character/Enemy/Components/EnemyPerceptionComponent.cs`
- Create: `Assets/Game/Character/Enemy/Components/EnemyAnimationComponent.cs`
- Create: `Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs`
- Create: `Assets/Game/Character/Enemy/Components/EnemyLifeComponent.cs`
- Create: `Assets/Game/Character/Enemy/Components/EnemyAttributeComponent.cs`
- Test: `Assets/Game/Editor/EnemyComponentEditModeTests.cs`

### Enemy States

- Create: `Assets/Game/Character/Enemy/AI/States/EnemyStateBase.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/IdleState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/PatrolState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/ChaseState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/SearchState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/EnemyCombatActionState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/AttackState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/SkillState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/GetHitState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/UnbalanceState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/DeadState.cs`
- Test: `Assets/Game/Editor/EnemyStateEditModeTests.cs`

### Combat Integration

- Modify: `Assets/Game/Battle/Combat/CombatReaction.cs`
- Test: `Assets/Game/Editor/EnemyCombatReactionEditModeTests.cs`

### Data Assets And Scene

- Create: `Assets/Game/Character/Enemy/Config/Definitions/GuardMeleeEnemyDefinition.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMeleeBehaviorTree.asset`
- Create: `Assets/Game/Character/Enemy/Prefabs/GuardMeleeEnemy.prefab`
- Modify: `Assets/Scenes/Scene1.unity`

### Cleanup

- Delete: `Assets/Game/Character/Enemy/Actor/`
- Delete: `Assets/Game/Character/Enemy/EnemyBrain.cs`
- Delete: `Assets/Game/Character/Enemy/EnemyStateMachine.cs`
- Delete: `Assets/Game/Character/Enemy/EnemyFsm/`
- Delete: `Assets/Game/Character/Enemy/EnemyMovement.cs`
- Delete: `Assets/Game/Character/Enemy/EnemyPerception.cs`
- Delete: `Assets/Game/Character/Enemy/EnemyCombat.cs`
- Delete: `Assets/Game/Character/Enemy/EnemySkillManager.cs`
- Delete: `Assets/Game/Character/Enemy/EnemyController.cs`
- Delete: old enemy tests tied to deleted classes: `Assets/Game/Editor/EnemyActionControllerEditModeTests.cs`, `Assets/Game/Editor/EnemyActorCoreEditModeTests.cs`, `Assets/Game/Editor/EnemyActorRuntimeEditModeTests.cs`, `Assets/Game/Editor/EnemyBrainEditModeTests.cs`, `Assets/Game/Editor/EnemyDecisionAssetEditModeTests.cs`

---

### Task 0: Prepare Isolated Execution

**Files:**
- No code files

- [ ] **Step 1: Create isolated branch or worktree**

Use `superpowers:using-git-worktrees` at execution time. Branch name:

```bash
codex/enemy-ai-architecture-refactor
```

- [ ] **Step 2: Confirm baseline**

Run:

```bash
git status --short
.\.aibridge\cli\AIBridgeCLI.exe compile unity --timeout 120000
```

Expected:
- Worktree contains only this task's branch changes.
- Unity compile command exits `0`.

- [ ] **Step 3: Commit baseline marker only if the worktree was created with generated metadata**

If no files changed, do not commit.

---

### Task 1: Add Runtime Type Transition To FSM

**Files:**
- Modify: `Assets/Framework/Core/FSM/FsmBase.cs`
- Create: `Assets/Game/Editor/FsmRuntimeTypeTransitionEditModeTests.cs`

- [ ] **Step 1: Write failing test**

Create `Assets/Game/Editor/FsmRuntimeTypeTransitionEditModeTests.cs`:

```csharp
using GameMain2.Framework.Core.FSM;
using NUnit.Framework;
using System;

namespace Game.Tests.EditMode
{
    public sealed class FsmRuntimeTypeTransitionEditModeTests
    {
        [Test]
        public void ChangeState_ByRuntimeType_ExitsCurrentAndEntersTarget()
        {
            TestOwner owner = new TestOwner();
            FirstState first = new FirstState();
            SecondState second = new SecondState();
            FsmBase<TestOwner> fsm = new FsmBase<TestOwner>(owner, new FsmStateBase<TestOwner>[] { first, second });

            fsm.SetStartState(typeof(FirstState));
            fsm.ChangeState(typeof(SecondState));

            Assert.AreSame(second, fsm.CurState);
            Assert.AreEqual(1, first.ExitCount);
            Assert.AreEqual(1, second.EnterCount);
        }

        private sealed class TestOwner { }

        private abstract class TestState : FsmStateBase<TestOwner>
        {
            public int EnterCount { get; private set; }
            public int ExitCount { get; private set; }

            // 记录状态进入次数，便于验证运行时类型切换。
            public override void Enter(FsmBase<TestOwner> fsm)
            {
                EnterCount++;
            }

            // 测试状态不需要每帧行为。
            public override void Update(FsmBase<TestOwner> fsm, float deltaTime) { }

            // 记录状态退出次数，便于验证当前状态被正确退出。
            public override void Exit(FsmBase<TestOwner> fsm)
            {
                ExitCount++;
            }
        }

        private sealed class FirstState : TestState { }
        private sealed class SecondState : TestState { }
    }
}
```

- [ ] **Step 2: Verify test fails**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name FsmRuntimeTypeTransitionEditModeTests --timeout 120000
```

Expected: compile failure because `FsmBase<T>.ChangeState(Type)` does not exist.

- [ ] **Step 3: Implement runtime type transition**

Add this method to `FsmBase<T>`:

```csharp
// 按运行时类型切换状态，供数据驱动 AI 将配置状态映射到 FSM 状态类。
public void ChangeState(Type type)
{
    if (type == null)
    {
        throw new Exception("type is null");
    }

    if (m_CurState == null)
    {
        throw new Exception("CurrentState is null");
    }

    if (!m_States.ContainsKey(type))
    {
        return;
    }

    m_CurState.Exit(this);
    m_CurState = m_States[type];
    m_CurState.Enter(this);
}
```

- [ ] **Step 4: Verify test passes**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name FsmRuntimeTypeTransitionEditModeTests --timeout 120000
```

Expected: command exits `0`, test fixture passes.

- [ ] **Step 5: Commit**

```bash
git add Assets/Framework/Core/FSM/FsmBase.cs Assets/Game/Editor/FsmRuntimeTypeTransitionEditModeTests.cs
git commit -m "feat: support runtime fsm state transitions"
```

---

### Task 2: Add Enemy Core Model

**Files:**
- Create: `Assets/Game/Character/Enemy/AI/EnemyStateId.cs`
- Create: `Assets/Game/Character/Enemy/AI/EnemyIntent.cs`
- Create: `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs`
- Create: `Assets/Game/Editor/EnemyBlackboardEditModeTests.cs`

- [ ] **Step 1: Write failing tests**

Create `Assets/Game/Editor/EnemyBlackboardEditModeTests.cs`:

```csharp
using Game.Character.Enemy.AI;
using Game.Character.Enemy.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class EnemyBlackboardEditModeTests
    {
        [Test]
        public void SetIntent_StoresCurrentIntentUntilCleared()
        {
            EnemyBlackboard blackboard = new EnemyBlackboard();

            blackboard.SetIntent(EnemyIntent.Chase());

            Assert.AreEqual(EnemyIntentType.Chase, blackboard.CurrentIntent.Type);
            blackboard.ClearIntent();
            Assert.AreEqual(EnemyIntentType.None, blackboard.CurrentIntent.Type);
        }

        [Test]
        public void RememberTarget_StoresTargetAndLastKnownPosition()
        {
            GameObject target = new GameObject("Target");
            try
            {
                target.transform.position = new Vector3(1f, 2f, 3f);
                EnemyBlackboard blackboard = new EnemyBlackboard();

                blackboard.RememberTarget(target.transform);

                Assert.AreSame(target.transform, blackboard.Target);
                Assert.IsTrue(blackboard.HasLastKnownPosition);
                Assert.AreEqual(target.transform.position, blackboard.LastKnownPosition);
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
```

- [ ] **Step 2: Verify tests fail**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name EnemyBlackboardEditModeTests --timeout 120000
```

Expected: compile failure because `EnemyBlackboard` and `EnemyIntent` do not exist.

- [ ] **Step 3: Implement `EnemyStateId`**

Create `Assets/Game/Character/Enemy/AI/EnemyStateId.cs`:

```csharp
namespace Game.Character.Enemy.AI
{
    public enum EnemyStateId
    {
        None = 0,
        Idle = 1,
        Patrol = 2,
        Chase = 3,
        Search = 4,
        Attack = 5,
        Skill = 6,
        Flee = 7,
        KeepDistance = 8,
        RangedAttack = 9,
        Summon = 10,
        GetHit = 50,
        Unbalance = 60,
        Dead = 100
    }
}
```

- [ ] **Step 4: Implement `EnemyIntent`**

Create `Assets/Game/Character/Enemy/AI/EnemyIntent.cs`:

```csharp
using UnityEngine;

namespace Game.Character.Enemy.AI
{
    public enum EnemyIntentType
    {
        None = 0,
        Idle = 1,
        Patrol = 2,
        Chase = 3,
        Search = 4,
        Attack = 5,
        Skill = 6,
        Flee = 7,
        GetHit = 50,
        Unbalance = 60,
        Dead = 100
    }

    public readonly struct EnemyIntent
    {
        public EnemyIntentType Type { get; }
        public Transform Target { get; }
        public Vector3 Position { get; }
        public bool HasPosition { get; }
        public int SkillId { get; }
        public string AnimationName { get; }

        // 保存行为树产生的高层意图，AIController 会把它映射成 FSM 状态。
        private EnemyIntent(EnemyIntentType type, Transform target, Vector3 position, bool hasPosition, int skillId, string animationName)
        {
            Type = type;
            Target = target;
            Position = position;
            HasPosition = hasPosition;
            SkillId = skillId;
            AnimationName = animationName;
        }

        // 创建空意图，表示行为树本帧不请求状态变化。
        public static EnemyIntent None()
        {
            return new EnemyIntent(EnemyIntentType.None, null, Vector3.zero, false, 0, null);
        }

        // 创建指定类型的无目标意图。
        public static EnemyIntent Simple(EnemyIntentType type)
        {
            return new EnemyIntent(type, null, Vector3.zero, false, 0, null);
        }

        // 创建追击目标意图。
        public static EnemyIntent Chase(Transform target = null)
        {
            return new EnemyIntent(EnemyIntentType.Chase, target, Vector3.zero, false, 0, null);
        }

        // 创建搜索位置意图。
        public static EnemyIntent Search(Vector3 position)
        {
            return new EnemyIntent(EnemyIntentType.Search, null, position, true, 0, null);
        }

        // 创建攻击或技能意图。
        public static EnemyIntent Skill(EnemyIntentType type, int skillId, Transform target)
        {
            return new EnemyIntent(type, target, Vector3.zero, false, skillId, null);
        }

        // 创建受击意图，携带待播放动画名。
        public static EnemyIntent Reaction(EnemyIntentType type, string animationName, Transform attacker)
        {
            return new EnemyIntent(type, attacker, Vector3.zero, false, 0, animationName);
        }
    }
}
```

- [ ] **Step 5: Implement `EnemyBlackboard`**

Create `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs`:

```csharp
using Game.Character.Enemy.AI;
using UnityEngine;

namespace Game.Character.Enemy.Core
{
    public sealed class EnemyBlackboard
    {
        public Transform Target { get; private set; }
        public Vector3 LastKnownPosition { get; private set; }
        public bool HasLastKnownPosition { get; private set; }
        public EnemyIntent CurrentIntent { get; private set; }
        public EnemyStateId CurrentState { get; private set; }
        public int CurrentSkillId { get; private set; }
        public string PendingHitReactionAnimation { get; private set; }
        public bool IsTargetVisible { get; private set; }
        public bool IsSearching { get; private set; }
        public bool IsDead { get; private set; }
        public bool IsUnbalanced { get; private set; }
        public bool HasHitReaction { get; private set; }

        // 记录行为树本帧决策出的意图。
        public void SetIntent(EnemyIntent intent)
        {
            CurrentIntent = intent;
            CurrentSkillId = intent.SkillId;
        }

        // 清理本帧意图，避免旧意图被下一帧重复消费。
        public void ClearIntent()
        {
            CurrentIntent = EnemyIntent.None();
        }

        // 记住当前目标，并同步最后已知位置。
        public void RememberTarget(Transform target)
        {
            Target = target;
            if (target != null)
            {
                LastKnownPosition = target.position;
                HasLastKnownPosition = true;
            }
        }

        // 清理目标引用，但保留最后已知位置供 Search 使用。
        public void ForgetTarget()
        {
            Target = null;
            IsTargetVisible = false;
        }

        // 写入最后已知位置。
        public void SetLastKnownPosition(Vector3 position)
        {
            LastKnownPosition = position;
            HasLastKnownPosition = true;
        }

        // 写入当前 FSM 状态，供调试和行为树条件读取。
        public void SetCurrentState(EnemyStateId stateId)
        {
            CurrentState = stateId;
        }

        // 写入目标可见性事实。
        public void SetTargetVisible(bool isVisible)
        {
            IsTargetVisible = isVisible;
        }

        // 写入搜索状态事实。
        public void SetSearching(bool isSearching)
        {
            IsSearching = isSearching;
        }

        // 写入受击事实。
        public void SetHitReaction(string animationName)
        {
            PendingHitReactionAnimation = animationName;
            HasHitReaction = true;
        }

        // 消费受击动画名，进入 GetHit 状态后调用。
        public string ConsumeHitReaction()
        {
            string animationName = PendingHitReactionAnimation;
            PendingHitReactionAnimation = null;
            HasHitReaction = false;
            return animationName;
        }

        // 写入失衡事实。
        public void SetUnbalanced(bool isUnbalanced)
        {
            IsUnbalanced = isUnbalanced;
        }

        // 写入死亡事实。
        public void SetDead(bool isDead)
        {
            IsDead = isDead;
        }
    }
}
```

- [ ] **Step 6: Verify tests pass**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name EnemyBlackboardEditModeTests --timeout 120000
```

Expected: command exits `0`.

- [ ] **Step 7: Commit**

```bash
git add Assets/Game/Character/Enemy/AI/EnemyStateId.cs Assets/Game/Character/Enemy/AI/EnemyIntent.cs Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs Assets/Game/Editor/EnemyBlackboardEditModeTests.cs
git commit -m "feat: add enemy ai core model"
```

---

### Task 3: Add Enemy Definition And Validator

**Files:**
- Create: `Assets/Game/Character/Enemy/Config/EnemyDefinition.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyMovementConfig.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyPerceptionConfig.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyAnimationConfig.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyLifeConfig.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyAttributeConfig.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyDefinitionValidator.cs`
- Create: `Assets/Game/Editor/EnemyDefinitionValidatorEditModeTests.cs`

- [ ] **Step 1: Write failing validator tests**

Create tests that build `EnemyDefinition` with `ScriptableObject.CreateInstance<EnemyDefinition>()`:

```csharp
using Game.Character.Enemy.AI;
using Game.Character.Enemy.Config;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class EnemyDefinitionValidatorEditModeTests
    {
        [Test]
        public void Validate_ReturnsErrorWhenBehaviorTreeMissing()
        {
            EnemyDefinition definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            try
            {
                definition.SetEnemyId("guard");
                definition.SetEnabledStates(new[] { EnemyStateId.Idle, EnemyStateId.Patrol });

                EnemyDefinitionValidationResult result = EnemyDefinitionValidator.Validate(definition);

                Assert.IsFalse(result.IsValid);
                Assert.IsTrue(result.HasError("BehaviorTreeAsset"));
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }

        [Test]
        public void Validate_ReturnsErrorWhenEnabledStateIsMissingStartState()
        {
            EnemyDefinition definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            try
            {
                definition.SetEnemyId("guard");
                definition.SetStartState(EnemyStateId.Patrol);
                definition.SetEnabledStates(new[] { EnemyStateId.Idle });

                EnemyDefinitionValidationResult result = EnemyDefinitionValidator.Validate(definition);

                Assert.IsFalse(result.IsValid);
                Assert.IsTrue(result.HasError("StartState"));
            }
            finally
            {
                Object.DestroyImmediate(definition);
            }
        }
    }
}
```

Create `Assets/Game/Editor/EnemyAttributeConfigTableEditModeTests.cs`:

```csharp
using Game.Character.Enemy.Config;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public sealed class EnemyAttributeConfigTableEditModeTests
    {
        [Test]
        public void FromJson_ReturnsRowByConfigId()
        {
            string json = "[{\"id\":\"guard_default\",\"maxHealth\":100,\"maxStability\":80,\"attack\":12,\"defense\":3,\"moveSpeedMultiplier\":1.1,\"perceptionMultiplier\":1.2}]";

            EnemyAttributeConfigTable table = EnemyAttributeConfigTable.FromJson(json);
            EnemyAttributeConfigRow row = table.Get("guard_default");

            Assert.AreEqual(100, row.maxHealth);
            Assert.AreEqual(80, row.maxStability);
            Assert.AreEqual(12, row.attack);
            Assert.AreEqual(3, row.defense);
            Assert.AreEqual(1.1f, row.moveSpeedMultiplier);
            Assert.AreEqual(1.2f, row.perceptionMultiplier);
        }
    }
}
```

- [ ] **Step 2: Verify tests fail**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name EnemyDefinitionValidatorEditModeTests --timeout 120000
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name EnemyAttributeConfigTableEditModeTests --timeout 120000
```

Expected: compile failure because config classes do not exist.

- [ ] **Step 3: Implement config classes**

`EnemyDefinition` fields:

```csharp
[SerializeField] private string enemyId;
[SerializeField] private string displayName;
[SerializeField] private BehaviorTreeAsset behaviorTreeAsset;
[SerializeField] private EnemyStateId startState = EnemyStateId.Idle;
[SerializeField] private EnemyStateId[] enabledStates;
[SerializeField] private EnemyMovementConfig movementConfig;
[SerializeField] private EnemyPerceptionConfig perceptionConfig;
[SerializeField] private EnemyAnimationConfig animationConfig;
[SerializeField] private EnemyCombatConfig combatConfig;
[SerializeField] private EnemyLifeConfig lifeConfig;
[SerializeField] private EnemyAttributeConfig attributeConfig;
[SerializeField] private int[] skillSet;
[SerializeField] private Transform[] patrolRoute;
```

Expose read-only properties and editor/test setters. Each setter needs a short Chinese function comment.

Config classes:

```csharp
[System.Serializable]
public sealed class EnemyMovementConfig
{
    public float moveSpeed = 2f;
    public float rotateSpeed = 4f;
    public float stoppingDistance = 1.1f;
    public float navMeshSampleDistance = 2f;
}
```

```csharp
[System.Serializable]
public sealed class EnemyPerceptionConfig
{
    public float range = 8f;
    public float angle = 120f;
    public float closeAwarenessRange = 2.5f;
    public float loseSightGraceTime = 0.5f;
    public float targetMemoryTime = 4f;
    public float searchWaitTime = 5f;
    public float searchRadius = 4f;
    public int searchPointCount = 3;
    public LayerMask targetMask;
    public LayerMask obstacleMask;
}
```

```csharp
[System.Serializable]
public sealed class EnemyCombatConfig
{
    public int firstAttackSkillId = 20001;
    public int[] normalComboSkillIds = new[] { 20001, 20002, 20003 };
    public int[] specialSkillIds = new int[0];
    public float defaultAttackRange = 1.6f;
    public bool canInterruptAttack;
}
```

```csharp
[System.Serializable]
public sealed class EnemyAnimationConfig
{
    public string idleAnimation = "Idle";
    public string moveAnimation = "Move";
    public string getHitAnimation = "GetHit";
    public string unbalanceAnimation = "Unbalance";
    public string deadAnimation = "Dead";
}
```

```csharp
[System.Serializable]
public sealed class EnemyLifeConfig
{
    public bool rememberAttackerOnHit = true;
    public bool allowUnbalanceReaction = true;
    public bool allowDeathReaction = true;
}
```

```csharp
[System.Serializable]
public sealed class EnemyAttributeConfig
{
    public string attributeConfigId = "guard_default";
}
```

- [ ] **Step 4: Implement JSON attribute table**

Create `EnemyAttributeConfigRow`:

```csharp
[System.Serializable]
public sealed class EnemyAttributeConfigRow
{
    public string id;
    public int maxHealth;
    public int maxStability;
    public int attack;
    public int defense;
    public float moveSpeedMultiplier;
    public float perceptionMultiplier;
}
```

Create `EnemyAttributeConfigTable`:

```csharp
using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Game.Character.Enemy.Config
{
    public sealed class EnemyAttributeConfigTable
    {
        private readonly Dictionary<string, EnemyAttributeConfigRow> rows;

        // 保存按配置 Id 建立的属性行索引。
        private EnemyAttributeConfigTable(Dictionary<string, EnemyAttributeConfigRow> rows)
        {
            this.rows = rows;
        }

        // 从 JSON 文本解析敌人属性表。
        public static EnemyAttributeConfigTable FromJson(string json)
        {
            EnemyAttributeConfigRow[] parsedRows = JsonConvert.DeserializeObject<EnemyAttributeConfigRow[]>(json);
            Dictionary<string, EnemyAttributeConfigRow> result = new Dictionary<string, EnemyAttributeConfigRow>();
            foreach (EnemyAttributeConfigRow row in parsedRows)
            {
                result.Add(row.id, row);
            }

            return new EnemyAttributeConfigTable(result);
        }

        // 按配置 Id 取得属性行。
        public EnemyAttributeConfigRow Get(string id)
        {
            if (!rows.TryGetValue(id, out EnemyAttributeConfigRow row))
            {
                throw new Exception("未找到敌人属性配置：" + id);
            }

            return row;
        }
    }
}
```

Create `Assets/Data/EnemyAttributeConfig.json`:

```json
[
  {
    "id": "guard_default",
    "maxHealth": 100,
    "maxStability": 100,
    "attack": 10,
    "defense": 0,
    "moveSpeedMultiplier": 1.0,
    "perceptionMultiplier": 1.0
  }
]
```

- [ ] **Step 5: Implement validator**

Validator result must support exact error lookup:

```csharp
public sealed class EnemyDefinitionValidationResult
{
    private readonly List<string> errors = new List<string>();
    public IReadOnlyList<string> Errors => errors;
    public bool IsValid => errors.Count == 0;

    // 添加校验错误，错误文本必须包含字段名，便于测试和编辑器提示定位。
    public void AddError(string fieldName, string message)
    {
        errors.Add(fieldName + ": " + message);
    }

    // 判断是否存在指定字段的错误。
    public bool HasError(string fieldName)
    {
        return errors.Any(error => error.StartsWith(fieldName + ":", StringComparison.Ordinal));
    }
}
```

`EnemyDefinitionValidator.Validate` must check:
- definition exists
- enemyId is not empty
- behaviorTreeAsset exists
- enabledStates contains startState
- enabledStates contains required reaction states: `GetHit` and `Dead`
- combat config exists and first attack skill id is positive
- animation config exists and key animation names are not empty
- attribute config exists and `attributeConfigId` is not empty

- [ ] **Step 6: Verify tests pass**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name EnemyDefinitionValidatorEditModeTests --timeout 120000
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name EnemyAttributeConfigTableEditModeTests --timeout 120000
```

Expected: command exits `0`.

- [ ] **Step 7: Commit**

```bash
git add Assets/Game/Character/Enemy/Config Assets/Data/EnemyAttributeConfig.json Assets/Game/Editor/EnemyDefinitionValidatorEditModeTests.cs Assets/Game/Editor/EnemyAttributeConfigTableEditModeTests.cs
git commit -m "feat: add enemy definition validation"
```

---

### Task 4: Add Component Shells And State Context

**Files:**
- Create: `Assets/Game/Character/Enemy/Core/EnemyStateContext.cs`
- Create: `Assets/Game/Character/Enemy/Components/EnemyMovementComponent.cs`
- Create: `Assets/Game/Character/Enemy/Components/EnemyPerceptionComponent.cs`
- Create: `Assets/Game/Character/Enemy/Components/EnemyAnimationComponent.cs`
- Create: `Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs`
- Create: `Assets/Game/Character/Enemy/Components/EnemyLifeComponent.cs`
- Create: `Assets/Game/Character/Enemy/Components/EnemyAttributeComponent.cs`
- Create: `Assets/Game/Editor/EnemyComponentEditModeTests.cs`

- [ ] **Step 1: Write failing component tests**

Test only component contracts and cheap behavior:

```csharp
using Game.Character.Enemy.Components;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class EnemyComponentEditModeTests
    {
        [Test]
        public void Movement_Stop_ClearsDestination()
        {
            GameObject owner = new GameObject("Enemy");
            try
            {
                EnemyMovementComponent movement = owner.AddComponent<EnemyMovementComponent>();

                movement.MoveTo(new Vector3(3f, 0f, 0f));
                movement.Stop();

                Assert.IsFalse(movement.HasDestination);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
```

- [ ] **Step 2: Verify tests fail**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name EnemyComponentEditModeTests --timeout 120000
```

Expected: compile failure because component classes do not exist.

- [ ] **Step 3: Implement `EnemyStateContext`**

`EnemyStateContext` stores references only:

```csharp
public sealed class EnemyStateContext
{
    public EnemyAgent Agent { get; }
    public EnemyBlackboard Blackboard { get; }
    public EnemyMovementComponent Movement { get; }
    public EnemyPerceptionComponent Perception { get; }
    public EnemyAnimationComponent Animation { get; }
    public EnemyCombatComponent Combat { get; }
    public EnemyLifeComponent Life { get; }
    public EnemyAttributeComponent Attribute { get; }

    // 收拢状态执行所需组件引用，避免状态直接到处 GetComponent。
    public EnemyStateContext(...)
}
```

- [ ] **Step 4: Implement movement component**

Port behavior from old `EnemyMovement`:
- `MoveTo(Vector3 position)`
- `MoveTo(Transform target)`
- `Stop()`
- `LookAt(Vector3 position)`
- `HasReached(Vector3 position, float distance)`
- `SampleNavMesh(Vector3 source, out Vector3 result)`
- `Tick(float deltaTime)` for gravity and NavMeshAgent to CharacterController sync

The component must keep `HasDestination` for tests and state logic.

- [ ] **Step 5: Implement perception component**

Port behavior from old `EnemyPerception`:
- `ScanTarget()`
- `CanSee(Transform target)`
- `CanSenseNearby(Transform target, float range)`
- `EvaluateTarget(float deltaTime)`
- `ForgetTarget()`
- `GenerateSearchPoints(Vector3 center)`

Perception writes facts into `EnemyBlackboard`; it must not call `AIController.ChangeState`.

- [ ] **Step 6: Implement animation component**

Wrap `Animator` operations currently exposed by `CharacterStateMachine`:
- `Play(string animationName)`
- `TryPlay(string animationName)`
- `IsPlaying(string animationName, out float progress)`
- `HandleAnimationEvent(string eventName)`

Animation events route to `EnemyCombatComponent.EnableWeaponHit` and `EnemyCombatComponent.DisableWeaponHit`.

- [ ] **Step 7: Implement combat, life, and attribute components**

`EnemyCombatComponent`:
- `IsInAttackRange(Transform target)`
- `TryStartAttack(int skillId)`
- `TryStartSkill(int skillId)`
- `EndAction()`
- `EnableWeaponHit()`
- `DisableWeaponHit()`

`EnemyLifeComponent`:
- `HandleHitReaction(string animationName, Transform attacker)`
- `HandleUnbalance(Transform attacker)`
- `HandleDeath()`
- publishes facts into `EnemyBlackboard`

`EnemyAttributeComponent`:
- `LoadFromDefinition(EnemyDefinition definition)`
- read-only runtime values for health, stability, attack, defense, perception, movement
- load `Assets/Data/EnemyAttributeConfig.json` through `EnemyAttributeConfigTable`
- use `definition.AttributeConfig.attributeConfigId` to copy one row into runtime fields

- [ ] **Step 8: Verify tests pass**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name EnemyComponentEditModeTests --timeout 120000
```

Expected: command exits `0`.

- [ ] **Step 9: Commit**

```bash
git add Assets/Game/Character/Enemy/Core/EnemyStateContext.cs Assets/Game/Character/Enemy/Components Assets/Game/Editor/EnemyComponentEditModeTests.cs
git commit -m "feat: add enemy runtime components"
```

---

### Task 5: Add Behavior Tree Facing AIController API And Enemy Nodes

**Files:**
- Create: `Assets/Game/Character/Enemy/AI/AIController.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyBehaviorTreeUtility.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyCanSeeTargetNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyHasTargetMemoryNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyIsInAttackRangeNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyIsDeadNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyHasHitReactionNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyIsUnbalancedNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyIsHealthBelowNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemySetIntentNodeAsset.cs`
- Create: `Assets/Game/Editor/EnemyBehaviorTreeNodeEditModeTests.cs`

- [ ] **Step 1: Write failing node tests**

```csharp
using Game.Character.Enemy.AI;
using Game.Character.Enemy.AI.BehaviorTree;
using Game.Character.Enemy.Core;
using GameMain2.Framework.Core.BehaviorTree;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class EnemyBehaviorTreeNodeEditModeTests
    {
        [Test]
        public void SetIntentNode_WritesIntentToEnemyBlackboard()
        {
            GameObject owner = new GameObject("Enemy");
            try
            {
                AIController controller = owner.AddComponent<AIController>();
                controller.SetBlackboardForTests(new EnemyBlackboard());
                EnemySetIntentNodeAsset node = ScriptableObject.CreateInstance<EnemySetIntentNodeAsset>();
                node.SetIntentForTests(EnemyIntentType.Chase);

                BehaviorTreeStatus status = node.CreateRuntimeNode().Tick(new BehaviorTreeContext(owner, new BehaviorTreeBlackboard()));

                Assert.AreEqual(BehaviorTreeStatus.Success, status);
                Assert.AreEqual(EnemyIntentType.Chase, controller.Blackboard.CurrentIntent.Type);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
```

- [ ] **Step 2: Verify tests fail**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name EnemyBehaviorTreeNodeEditModeTests --timeout 120000
```

Expected: compile failure because `AIController` and enemy behavior tree nodes do not exist.

- [ ] **Step 3: Implement behavior-tree-facing AIController API**

Create `Assets/Game/Character/Enemy/AI/AIController.cs` with the minimal API needed by behavior tree nodes. Task 6 extends this class with FSM and Tick behavior.

```csharp
using Game.Character.Enemy.Core;
using UnityEngine;

namespace Game.Character.Enemy.AI
{
    public sealed class AIController : MonoBehaviour
    {
        public EnemyBlackboard Blackboard { get; private set; } = new EnemyBlackboard();

        // 测试时替换黑板实例，便于验证行为树节点写入意图。
        public void SetBlackboardForTests(EnemyBlackboard blackboard)
        {
            Blackboard = blackboard;
        }
    }
}
```

- [ ] **Step 4: Implement utility**

`EnemyBehaviorTreeUtility` must use the generic behavior tree context owner:

```csharp
// 从行为树上下文的 Owner 上取得 AIController，敌人节点不创建专用 BehaviorTreeContext。
public static bool TryGetController(BehaviorTreeContext context, out AIController controller)
{
    controller = context != null && context.Owner != null
        ? context.Owner.GetComponent<AIController>()
        : null;
    return controller != null;
}
```

- [ ] **Step 5: Implement condition nodes**

Each node directly inherits `ConditionNodeAsset`. Examples:

```csharp
[CreateAssetMenu(fileName = "EnemyCanSeeTargetNode", menuName = "Game/Enemy/Behavior Tree/Can See Target")]
public sealed class EnemyCanSeeTargetNodeAsset : ConditionNodeAsset
{
    // 判断黑板当前目标是否可见。
    protected override bool Evaluate(BehaviorTreeContext context)
    {
        return EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
            && controller.Blackboard.IsTargetVisible;
    }
}
```

Implement the same pattern for:
- target memory
- attack range
- dead
- hit reaction
- unbalance
- health below threshold

- [ ] **Step 6: Implement action node**

`EnemySetIntentNodeAsset` fields:

```csharp
[SerializeField] private EnemyIntentType intentType;
[SerializeField] private int skillId;
```

`Execute` maps:
- `Patrol`, `Idle`, `Dead`, `Unbalance`, `GetHit`, `Flee` -> `EnemyIntent.Simple`
- `Chase` -> `EnemyIntent.Chase(controller.Blackboard.Target)`
- `Search` -> `EnemyIntent.Search(controller.Blackboard.LastKnownPosition)`
- `Attack` and `Skill` -> `EnemyIntent.Skill(intentType, resolvedSkillId, controller.Blackboard.Target)`

- [ ] **Step 7: Verify tests pass**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name EnemyBehaviorTreeNodeEditModeTests --timeout 120000
```

Expected: command exits `0`.

- [ ] **Step 8: Commit**

```bash
git add Assets/Game/Character/Enemy/AI/AIController.cs Assets/Game/Character/Enemy/AI/BehaviorTree Assets/Game/Editor/EnemyBehaviorTreeNodeEditModeTests.cs
git commit -m "feat: add enemy behavior tree nodes"
```

---

### Task 6: Complete AIController And EnemyAgent

**Files:**
- Modify: `Assets/Game/Character/Enemy/AI/AIController.cs`
- Create: `Assets/Game/Character/Enemy/Core/EnemyAgent.cs`
- Create: `Assets/Game/Editor/AIControllerEditModeTests.cs`

- [ ] **Step 1: Write failing AIController tests**

```csharp
using Game.Character.Enemy.AI;
using Game.Character.Enemy.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class AIControllerEditModeTests
    {
        [Test]
        public void RequestState_DeadOverridesAttack()
        {
            GameObject owner = new GameObject("Enemy");
            try
            {
                AIController controller = owner.AddComponent<AIController>();
                controller.SetBlackboardForTests(new EnemyBlackboard());

                Assert.IsTrue(controller.CanChangeTo(EnemyStateId.Attack));
                controller.RequestState(EnemyStateId.Dead);

                Assert.AreEqual(EnemyStateId.Dead, controller.PendingStateForTests);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
```

- [ ] **Step 2: Verify test fails**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name AIControllerEditModeTests --timeout 120000
```

Expected: compile failure because `AIController.RequestState` and `EnemyAgent` do not exist.

- [ ] **Step 3: Implement `AIController`**

Required API:

```csharp
public EnemyBlackboard Blackboard { get; }
public EnemyStateContext Context { get; }
public EnemyStateId CurrentStateId { get; }
public void StartAI(EnemyAgent agent, EnemyDefinition definition)
public void TickAI(float deltaTime)
public void RequestState(EnemyStateId stateId)
public void ChangeState(EnemyStateId stateId)
public bool CanChangeTo(EnemyStateId stateId)
```

Execution order inside `TickAI`:

```csharp
// 按固定顺序推进感知、强制状态、行为树和动作 FSM。
public void TickAI(float deltaTime)
{
    Context.Perception.EvaluateTarget(deltaTime);
    Context.Life.SyncFacts();

    if (Blackboard.IsDead)
    {
        RequestState(EnemyStateId.Dead);
    }
    else if (Blackboard.IsUnbalanced)
    {
        RequestState(EnemyStateId.Unbalance);
    }
    else if (Blackboard.HasHitReaction)
    {
        RequestState(EnemyStateId.GetHit);
    }

    if (!IsInForcedState())
    {
        behaviorTree.Tick(deltaTime);
        ConsumeIntent();
    }

    actionFsm.Update(deltaTime);
}
```

Priority:

```text
Dead > Unbalance > GetHit > Attack/Skill lock > normal intent
```

- [ ] **Step 4: Implement `EnemyAgent`**

`EnemyAgent` owns serialized references:
- `EnemyDefinition definition`
- `BehaviorTreeRunner behaviorTreeRunner`
- `AIController aiController`
- all six enemy components

`Awake` resolves missing references on the same GameObject. `Start` validates `EnemyDefinition` and calls `AIController.StartAI`. `Update` calls component Tick and `AIController.TickAI`.

- [ ] **Step 5: Verify AIController tests pass**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name AIControllerEditModeTests --timeout 120000
```

Expected: command exits `0`.

- [ ] **Step 6: Commit**

```bash
git add Assets/Game/Character/Enemy/AI/AIController.cs Assets/Game/Character/Enemy/Core/EnemyAgent.cs Assets/Game/Editor/AIControllerEditModeTests.cs
git commit -m "feat: add enemy ai controller"
```

---

### Task 7: Add Enemy FSM States

**Files:**
- Create: `Assets/Game/Character/Enemy/AI/States/EnemyStateBase.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/IdleState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/PatrolState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/ChaseState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/SearchState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/EnemyCombatActionState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/AttackState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/SkillState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/GetHitState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/UnbalanceState.cs`
- Create: `Assets/Game/Character/Enemy/AI/States/DeadState.cs`
- Create: `Assets/Game/Editor/EnemyStateEditModeTests.cs`

- [ ] **Step 1: Write failing state tests**

```csharp
using Game.Character.Enemy.AI;
using Game.Character.Enemy.AI.States;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public sealed class EnemyStateEditModeTests
    {
        [Test]
        public void EnemyStateBase_ExposesStateId()
        {
            IdleState state = new IdleState();

            Assert.AreEqual(EnemyStateId.Idle, state.StateId);
        }
    }
}
```

- [ ] **Step 2: Verify tests fail**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name EnemyStateEditModeTests --timeout 120000
```

Expected: compile failure because new state classes do not exist.

- [ ] **Step 3: Implement state base**

```csharp
public abstract class EnemyStateBase : FsmStateBase<AIController>
{
    public abstract EnemyStateId StateId { get; }

    // 进入状态时同步黑板当前状态。
    public override void Enter(FsmBase<AIController> fsm)
    {
        fsm.Owner.Blackboard.SetCurrentState(StateId);
        OnEnter(fsm.Owner);
    }

    // 每帧执行状态行为。
    public override void Update(FsmBase<AIController> fsm, float deltaTime)
    {
        OnUpdate(fsm.Owner, deltaTime);
    }

    // 离开状态时执行清理。
    public override void Exit(FsmBase<AIController> fsm)
    {
        OnExit(fsm.Owner);
    }

    protected abstract void OnEnter(AIController controller);
    protected abstract void OnUpdate(AIController controller, float deltaTime);
    protected virtual void OnExit(AIController controller) { }
}
```

- [ ] **Step 4: Implement locomotion states**

`IdleState`:
- play idle animation
- stop movement

`PatrolState`:
- move along `EnemyDefinition.PatrolRoute`
- wait at point
- request Idle if no route

`ChaseState`:
- move to visible target
- if target lost and last known position exists, request Search
- if in attack range, request Attack

`SearchState`:
- move to last known position
- generate search points through perception
- after search timeout, request Patrol or Idle

- [ ] **Step 5: Implement combat states**

`EnemyCombatActionState` ports old `EnemyCombatActionState` behavior:
- stop movement
- resolve skill id
- call `EnemyCombatComponent.TryStartAttack` or `TryStartSkill`
- play skill animation from `SkillConfig.skillAnimationName`
- wait for animation completion
- keep attack/skill locked until completion unless `AIController.CanChangeTo` receives higher priority state
- continue combo if `comboNextSkillId > 0`
- return to Chase or Search when action ends

`AttackState` resolves `EnemyDefinition.CombatConfig.firstAttackSkillId`.

`SkillState` resolves `EnemyBlackboard.CurrentSkillId`.

- [ ] **Step 6: Implement reaction states**

`GetHitState`:
- consume hit reaction animation from blackboard
- fallback to `EnemyAnimationConfig.getHitAnimation`
- stop movement
- return to Chase or Patrol after animation completion

`UnbalanceState`:
- play unbalance animation
- stop movement
- restore stability to full after animation completion
- clear blackboard unbalance fact

`DeadState`:
- clear target
- stop movement
- cancel combat action
- disable weapon hit
- clear combat state
- play dead animation
- ignore normal intent after entry

- [ ] **Step 7: Verify state tests pass**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name EnemyStateEditModeTests --timeout 120000
```

Expected: command exits `0`.

- [ ] **Step 8: Commit**

```bash
git add Assets/Game/Character/Enemy/AI/States Assets/Game/Editor/EnemyStateEditModeTests.cs
git commit -m "feat: add enemy fsm states"
```

---

### Task 8: Wire CombatReaction To New Life Component

**Files:**
- Modify: `Assets/Game/Battle/Combat/CombatReaction.cs`
- Create: `Assets/Game/Editor/EnemyCombatReactionEditModeTests.cs`

- [ ] **Step 1: Write failing reaction test**

```csharp
using Game.Battle.Combat;
using Game.Character.Enemy.Components;
using Game.Character.Enemy.Core;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class EnemyCombatReactionEditModeTests
    {
        [Test]
        public void Apply_HitReaction_WritesEnemyLifeFacts()
        {
            GameObject enemy = new GameObject("Enemy");
            try
            {
                enemy.AddComponent<EnemyAgent>();
                EnemyLifeComponent life = enemy.AddComponent<EnemyLifeComponent>();
                life.SetBlackboardForTests(new EnemyBlackboard());

                CombatResult result = CombatResult.Create(CombatResultType.Hit);
                result.HitReactionName = "GetHit";

                life.HandleHitReaction(result.HitReactionName, null);

                Assert.IsTrue(life.BlackboardForTests.HasHitReaction);
            }
            finally
            {
                Object.DestroyImmediate(enemy);
            }
        }
    }
}
```

- [ ] **Step 2: Verify test fails**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name EnemyCombatReactionEditModeTests --timeout 120000
```

Expected: compile failure until test helpers and new reaction branch exist.

- [ ] **Step 3: Add new branch to `CombatReaction.Apply`**

Before old enemy branches, resolve:

```csharp
EnemyLifeComponent enemyLife = hit.Target.GetComponentInParent<EnemyLifeComponent>();
if (enemyLife != null)
{
    ApplyEnemyLifeReaction(hit, result, enemyLife);
    return;
}
```

`ApplyEnemyLifeReaction` rules:
- `ShouldDie` -> `enemyLife.HandleDeath()`
- `ShouldEnterUnbalanced` -> `enemyLife.HandleUnbalance(attacker)`
- `ShouldPlayHitReaction` -> `enemyLife.HandleHitReaction(result.HitReactionName, attacker)`

- [ ] **Step 4: Verify reaction tests pass**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name EnemyCombatReactionEditModeTests --timeout 120000
```

Expected: command exits `0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Battle/Combat/CombatReaction.cs Assets/Game/Editor/EnemyCombatReactionEditModeTests.cs
git commit -m "feat: route combat reactions to enemy life component"
```

---

### Task 9: Add Guard Data Assets And Base Prefab

**Files:**
- Create: `Assets/Game/Character/Enemy/Config/Definitions/GuardMeleeEnemyDefinition.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMeleeBehaviorTree.asset`
- Create: `Assets/Game/Character/Enemy/Prefabs/GuardMeleeEnemy.prefab`
- Modify: `Assets/AddressableAssetsData/AssetGroups/EnemyPrefabGroup.asset` if project addressable setup requires explicit prefab entry

- [ ] **Step 1: Create behavior tree asset**

Create `GuardMeleeBehaviorTree.asset` with this structure:

```text
Selector
- Sequence(IsDead, SetIntentDead)
- Sequence(IsUnbalanced, SetIntentUnbalance)
- Sequence(HasHitReaction, SetIntentGetHit)
- Sequence(CanSeeTarget, IsInAttackRange, SetIntentAttack)
- Sequence(CanSeeTarget, SetIntentChase)
- Sequence(HasTargetMemory, SetIntentSearch)
- SetIntentPatrol
```

Use ScriptableObject assets under:

```text
Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/
```

- [ ] **Step 2: Create enemy definition**

Create `GuardMeleeEnemyDefinition.asset`:

```text
enemyId = guard_melee
displayName = Guard Melee
behaviorTreeAsset = GuardMeleeBehaviorTree
startState = Idle
enabledStates = Idle, Patrol, Chase, Search, Attack, Skill, GetHit, Unbalance, Dead
firstAttackSkillId = 20001
normalComboSkillIds = 20001, 20002, 20003
defaultAttackRange = 1.6
attributeConfigId = guard_default
idleAnimation = Idle
moveAnimation = Move
getHitAnimation = GetHit
unbalanceAnimation = Unbalance
deadAnimation = Dead
```

- [ ] **Step 3: Create base prefab**

Use the scene `Boss` hierarchy as the visual reference and create:

```text
Assets/Game/Character/Enemy/Prefabs/GuardMeleeEnemy.prefab
```

Required root components:
- `CharacterController`
- `NavMeshAgent`
- `WeaponHandler`
- `Animator`
- `Combatant`
- `CombatStats`
- `CombatState`
- `SkillRunner`
- `BehaviorTreeRunner`
- `EnemyAgent`
- `AIController`
- `EnemyMovementComponent`
- `EnemyPerceptionComponent`
- `EnemyAnimationComponent`
- `EnemyCombatComponent`
- `EnemyLifeComponent`
- `EnemyAttributeComponent`

- [ ] **Step 4: Validate asset import**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe asset refresh
.\.aibridge\cli\AIBridgeCLI.exe compile unity --timeout 120000
```

Expected: command exits `0`.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Character/Enemy/Config/Definitions Assets/Game/Character/Enemy/Config/BehaviorTrees Assets/Game/Character/Enemy/Prefabs Assets/AddressableAssetsData/AssetGroups/EnemyPrefabGroup.asset
git commit -m "feat: add guard enemy data assets"
```

---

### Task 10: Migrate Scene1 Enemy Instances

**Files:**
- Modify: `Assets/Scenes/Scene1.unity`

- [ ] **Step 1: Capture current scene hierarchy**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe scene load --path "Assets/Scenes/Scene1.unity"
.\.aibridge\cli\AIBridgeCLI.exe scene get_hierarchy --depth 3 --includeInactive true
```

Expected: hierarchy includes `Boss`, `Boss (1)`, and `Boss (2)`.

- [ ] **Step 2: Replace old enemy roots**

For `Boss`, `Boss (1)`, and `Boss (2)`:
- remove old `EnemyMovement`, `EnemyPerception`, `EnemyCombat`, `EnemyController`, `EnemySkillManager`, `EnemyActor`, `GuardStateMachine`, `EnemyBrain`
- add the new components listed in Task 9
- assign `GuardMeleeEnemyDefinition.asset`
- assign `GuardMeleeBehaviorTree.asset` to `BehaviorTreeRunner`
- keep existing transform, model children, animator, combat, skill, weapon, CharacterController, NavMeshAgent references

- [ ] **Step 3: Save scene**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe scene save
.\.aibridge\cli\AIBridgeCLI.exe compile unity --timeout 120000
```

Expected: command exits `0`.

- [ ] **Step 4: Commit**

```bash
git add Assets/Scenes/Scene1.unity
git commit -m "feat: migrate scene enemies to new ai architecture"
```

---

### Task 11: Remove Old Enemy Architecture

**Files:**
- Delete: `Assets/Game/Character/Enemy/Actor/`
- Delete: `Assets/Game/Character/Enemy/EnemyBrain.cs`
- Delete: `Assets/Game/Character/Enemy/EnemyStateMachine.cs`
- Delete: `Assets/Game/Character/Enemy/EnemyFsm/`
- Delete: `Assets/Game/Character/Enemy/EnemyMovement.cs`
- Delete: `Assets/Game/Character/Enemy/EnemyPerception.cs`
- Delete: `Assets/Game/Character/Enemy/EnemyCombat.cs`
- Delete: `Assets/Game/Character/Enemy/EnemySkillManager.cs`
- Delete: `Assets/Game/Character/Enemy/EnemyController.cs`
- Delete old tests listed in the Cleanup section
- Modify: `Assets/Game/Battle/Combat/CombatReaction.cs`

- [ ] **Step 1: Verify no scene or prefab uses deleted classes**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe compile unity --timeout 120000
.\.aibridge\cli\AIBridgeCLI.exe scene get_hierarchy --depth 3 --includeInactive true
```

Expected: hierarchy uses new enemy components only.

- [ ] **Step 2: Delete old files**

Use one shell end-to-end. On Windows, verify paths stay under `D:\MyGameProject\UnityGame\FirstGameDemo` before recursive deletion.

After deletion, remove old branches in `CombatReaction` that reference `EnemyActor` and `EnemyStateMachine`.

- [ ] **Step 3: Verify compile catches stale references**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe compile unity --timeout 120000
```

Expected: command exits `0`.

- [ ] **Step 4: Run enemy test suite**

Run:

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name Enemy --timeout 180000
```

Expected: command exits `0`.

- [ ] **Step 5: Commit**

```bash
git add -A Assets/Game/Character/Enemy Assets/Game/Editor Assets/Game/Battle/Combat/CombatReaction.cs
git commit -m "refactor: remove old enemy ai architecture"
```

---

### Task 12: Final Validation

**Files:**
- No planned code files

- [ ] **Step 1: Run focused behavior tree tests**

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name BehaviorTree --timeout 120000
```

Expected: command exits `0`.

- [ ] **Step 2: Run focused enemy tests**

```bash
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name Enemy --timeout 180000
```

Expected: command exits `0`.

- [ ] **Step 3: Run Unity compile**

```bash
.\.aibridge\cli\AIBridgeCLI.exe compile unity --timeout 120000
```

Expected: command exits `0`.

- [ ] **Step 4: Manual scene acceptance**

Open `Assets/Scenes/Scene1.unity` and verify:
- `Boss` patrols or idles when no target exists
- visible player produces Chase
- attack range produces Attack
- attack animation completion returns to Chase or combo
- lost target produces Search then Patrol or Idle
- hit reaction enters GetHit
- stability break enters Unbalance
- death stops movement, cancels combat, disables weapon hit, plays dead animation

- [ ] **Step 5: Final commit if validation changed assets**

If scene or asset references changed during manual acceptance:

```bash
git add Assets/Scenes/Scene1.unity Assets/Game/Character/Enemy/Config Assets/Game/Character/Enemy/Prefabs
git commit -m "test: validate enemy ai scene migration"
```

If no files changed, do not commit.
