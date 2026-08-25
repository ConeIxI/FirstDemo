# Enemy AI Behavior Tree Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build an extensible enemy AI decision layer that uses behavior tree nodes plus configurable decision profiles for attack, defense, retreat, keep-distance, and skill selection.

**Architecture:** Keep the current `EnemyAgent` / `AIController` / behavior tree structure. Add `EnemyDecisionProfile` to `EnemyDefinition`, expand `EnemyBlackboard` with decision facts, then introduce reusable behavior tree condition/action nodes that read config and write intent. Execution remains in existing enemy components, with small additions only for defense and retreat support.

**Tech Stack:** Unity 2022.3.61f1c1, C# 9.0, Unity EditMode tests with NUnit, existing `GameMain2.Framework.Core.BehaviorTree` runtime, AIBridge CLI for Unity compile.

## Global Constraints

- 尽量使用简体中文回复，禁止废话，言简意赅。
- 修改复杂业务逻辑时，必须用简体中文添加必要注释。
- 编写代码时，必须给每个函数添加简体中文注释，说明函数用途或关键行为。
- 当前项目 C# 语言版本要求：兼容 C# 9.0，禁止使用更高版本语法。
- Unity 编译只能使用 `$CLI compile unity`。
- `compile dotnet` 只能作为额外检查，不能作为 Unity 编译的替代或 fallback。
- 尊重用户已有改动，不擅自回滚无关文件；当前已知未提交改动为 `Assets/Scenes/Scene1.unity`，本计划实现时不得改动或提交它。
- 行为树只负责决策和写入意图，不直接处理动画帧、霸体帧、伤害结算和稳定值结算。
- 第一版不引入 GOAP，不实现群体协同、绕背、召唤、治疗或阵型。

---

## File Structure

- Create `Assets/Game/Character/Enemy/Config/EnemyWeightedSkill.cs`
  - Serializable skill-weight value object used by decision profile.
- Create `Assets/Game/Character/Enemy/Config/EnemyDecisionProfile.cs`
  - Serializable decision profile containing attack desire, defense rate, retreat tendency, distances, cooldowns, and skill weights.
- Modify `Assets/Game/Character/Enemy/Config/EnemyDefinition.cs`
  - Add `EnemyDecisionProfile decisionProfile`, public getter, and editor test setter.
- Modify `Assets/Game/Character/Enemy/Config/EnemyDefinitionValidator.cs`
  - Validate decision profile probability, distance, cooldown, and skill-weight constraints.
- Create `Assets/Game/Character/Enemy/AI/EnemyCombatIntent.cs`
  - Runtime enum for decision intent written by behavior tree nodes.
- Modify `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs`
  - Add current/last intent, distance facts, selected skill, and decision timestamps.
- Modify `Assets/Game/Character/Enemy/AI/AIController.cs`
  - Expose `DecisionProfile` and refresh blackboard distance facts each tick.
- Create `Assets/Game/Character/Enemy/AI/EnemyDecisionRandom.cs`
  - Deterministic-friendly probability and weighted skill helper.
- Create behavior tree nodes under `Assets/Game/Character/Enemy/AI/BehaviorTree/`
  - `EnemyShouldAttackNodeAsset.cs`
  - `EnemyShouldDefendNodeAsset.cs`
  - `EnemyShouldRetreatNodeAsset.cs`
  - `EnemySelectWeightedSkillNodeAsset.cs`
  - `EnemySetCombatIntentNodeAsset.cs`
- Modify `Assets/Game/Character/Enemy/Config/EnemyAnimationConfig.cs`
  - Add `defenseAnimation` and `retreatAnimation`.
- Modify `Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs`
  - Add minimal timed defense state.
- Modify `Assets/Game/Character/Enemy/Components/EnemyMovementComponent.cs`
  - Add `MoveAwayFrom(Transform target, float distance)`.
- Modify `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemySetIntentNodeAsset.cs`
  - Add `Defense`, `Retreat`, and `KeepDistance` action types that reuse new component support.
- Add EditMode tests under `Assets/Game/Editor/`
  - `EnemyDecisionProfileEditModeTests.cs`
  - `EnemyBlackboardDecisionFactsEditModeTests.cs`
  - `EnemyDecisionRandomEditModeTests.cs`
  - `EnemyBehaviorDecisionNodeEditModeTests.cs`

---

### Task 1: Decision Profile Config

**Files:**
- Create: `Assets/Game/Character/Enemy/Config/EnemyWeightedSkill.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyDecisionProfile.cs`
- Modify: `Assets/Game/Character/Enemy/Config/EnemyDefinition.cs`
- Modify: `Assets/Game/Character/Enemy/Config/EnemyDefinitionValidator.cs`
- Test: `Assets/Game/Editor/EnemyDecisionProfileEditModeTests.cs`

**Interfaces:**
- Consumes: existing `EnemyDefinition`, `EnemyDefinitionValidator`, and `EnemyCombatConfig`.
- Produces:
  - `EnemyDefinition.DecisionProfile : EnemyDecisionProfile`
  - `EnemyDefinition.SetDecisionProfile(EnemyDecisionProfile value)` inside `#if UNITY_EDITOR`
  - `EnemyDecisionProfile` fields used by behavior tree nodes.
  - `EnemyWeightedSkill.skillId`, `EnemyWeightedSkill.weight`

- [ ] **Step 1: Write failing config validation tests**

Create `Assets/Game/Editor/EnemyDecisionProfileEditModeTests.cs`:

```csharp
using Game.Character.Enemy.Config;
using NUnit.Framework;
using UnityEngine;

namespace Game.Character.Enemy.Tests
{
    public sealed class EnemyDecisionProfileEditModeTests
    {
        /// <summary>验证默认决策配置满足基础敌人 AI 的数值约束。</summary>
        [Test]
        public void Validate_DefaultDecisionProfile_IsValid()
        {
            EnemyDefinition definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            definition.SetEnemyId("basic_enemy");
            definition.SetBehaviorTreeAsset(ScriptableObject.CreateInstance<GameMain2.Framework.Core.BehaviorTree.BehaviorTreeAsset>());
            definition.SetAnimationConfig(new EnemyAnimationConfig());
            definition.SetCombatConfig(new EnemyCombatConfig());
            definition.SetAttributeConfig(new EnemyAttributeConfig());

            EnemyDefinitionValidationResult result = EnemyDefinitionValidator.Validate(definition);

            Assert.IsFalse(result.HasError("DecisionProfile"));
            Assert.IsFalse(result.HasError("AttackDesire"));
            Assert.IsFalse(result.HasError("DefenseRate"));
        }

        /// <summary>验证攻击欲望超出概率范围时会报告配置错误。</summary>
        [Test]
        public void Validate_AttackDesireOutsideRange_AddsError()
        {
            EnemyDecisionProfile profile = new EnemyDecisionProfile();
            profile.attackDesire = 1.25f;
            EnemyDefinition definition = CreateDefinition(profile);

            EnemyDefinitionValidationResult result = EnemyDefinitionValidator.Validate(definition);

            Assert.IsTrue(result.HasError("AttackDesire"));
        }

        /// <summary>验证期望距离小于最小安全距离时会报告配置错误。</summary>
        [Test]
        public void Validate_PreferredDistanceBelowMinimum_AddsError()
        {
            EnemyDecisionProfile profile = new EnemyDecisionProfile();
            profile.minSafeDistance = 2f;
            profile.preferredDistance = 1f;
            EnemyDefinition definition = CreateDefinition(profile);

            EnemyDefinitionValidationResult result = EnemyDefinitionValidator.Validate(definition);

            Assert.IsTrue(result.HasError("PreferredDistance"));
        }

        /// <summary>构造带指定决策配置的敌人定义，避免每个测试重复基础字段。</summary>
        private static EnemyDefinition CreateDefinition(EnemyDecisionProfile profile)
        {
            EnemyDefinition definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            definition.SetEnemyId("test_enemy");
            definition.SetBehaviorTreeAsset(ScriptableObject.CreateInstance<GameMain2.Framework.Core.BehaviorTree.BehaviorTreeAsset>());
            definition.SetAnimationConfig(new EnemyAnimationConfig());
            definition.SetCombatConfig(new EnemyCombatConfig());
            definition.SetAttributeConfig(new EnemyAttributeConfig());
            definition.SetDecisionProfile(profile);
            return definition;
        }
    }
}
```

- [ ] **Step 2: Run compile to confirm the tests fail from missing types**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: compile fails because `EnemyDecisionProfile` and `SetDecisionProfile` do not exist.

- [ ] **Step 3: Create decision config value objects**

Create `Assets/Game/Character/Enemy/Config/EnemyWeightedSkill.cs`:

```csharp
using System;

namespace Game.Character.Enemy.Config
{
    [Serializable]
    public sealed class EnemyWeightedSkill
    {
        public int skillId;
        public float weight = 1f;

        /// <summary>创建默认技能权重项，供序列化和测试构造使用。</summary>
        public EnemyWeightedSkill()
        {
        }

        /// <summary>创建指定技能和权重的配置项，便于测试直接构造。</summary>
        public EnemyWeightedSkill(int skillId, float weight)
        {
            this.skillId = skillId;
            this.weight = weight;
        }
    }
}
```

Create `Assets/Game/Character/Enemy/Config/EnemyDecisionProfile.cs`:

```csharp
using System;

namespace Game.Character.Enemy.Config
{
    [Serializable]
    public sealed class EnemyDecisionProfile
    {
        public float attackDesire = 0.45f;
        public float defenseRate = 0.2f;
        public float counterDesire = 0.1f;
        public float retreatDesire = 0.15f;
        public float preferredDistance = 2.2f;
        public float minSafeDistance = 1.0f;
        public float maxChaseDistance = 12f;
        public float attackCooldown = 1.2f;
        public float defenseDuration = 1.0f;
        public float retreatDistance = 2.5f;
        public float lowStabilityThreshold = 0.25f;
        public EnemyWeightedSkill[] skillWeights = new EnemyWeightedSkill[0];

        /// <summary>判断稳定值比例是否低于自保阈值。</summary>
        public bool IsLowStability(float stabilityRatio)
        {
            return stabilityRatio <= lowStabilityThreshold;
        }
    }
}
```

- [ ] **Step 4: Add profile to `EnemyDefinition`**

Modify `Assets/Game/Character/Enemy/Config/EnemyDefinition.cs`:

```csharp
[SerializeField] private EnemyDecisionProfile decisionProfile = new EnemyDecisionProfile();

public EnemyDecisionProfile DecisionProfile => decisionProfile;
```

Inside the existing `#if UNITY_EDITOR` block add:

```csharp
// 设置敌人决策配置，供编辑器工具或测试构造定义。
public void SetDecisionProfile(EnemyDecisionProfile value)
{
    decisionProfile = value;
}
```

- [ ] **Step 5: Validate decision profile**

Add this call inside `EnemyDefinitionValidator.Validate` after combat validation:

```csharp
ValidateDecisionProfile(definition.DecisionProfile, result);
```

Add these methods to `EnemyDefinitionValidator`:

```csharp
// 校验敌人决策配置的概率、距离、冷却和技能权重约束。
private static void ValidateDecisionProfile(EnemyDecisionProfile profile, EnemyDefinitionValidationResult result)
{
    if (profile == null)
    {
        result.AddError("DecisionProfile", "敌人决策配置不能为空");
        return;
    }

    AddErrorIfProbabilityInvalid(profile.attackDesire, "AttackDesire", result);
    AddErrorIfProbabilityInvalid(profile.defenseRate, "DefenseRate", result);
    AddErrorIfProbabilityInvalid(profile.counterDesire, "CounterDesire", result);
    AddErrorIfProbabilityInvalid(profile.retreatDesire, "RetreatDesire", result);
    AddErrorIfProbabilityInvalid(profile.lowStabilityThreshold, "LowStabilityThreshold", result);

    if (profile.minSafeDistance < 0f)
    {
        result.AddError("MinSafeDistance", "最小安全距离不能为负数");
    }

    if (profile.preferredDistance < profile.minSafeDistance)
    {
        result.AddError("PreferredDistance", "期望距离不能小于最小安全距离");
    }

    if (profile.maxChaseDistance <= profile.preferredDistance)
    {
        result.AddError("MaxChaseDistance", "最大追击距离必须大于期望距离");
    }

    if (profile.attackCooldown <= 0f)
    {
        result.AddError("AttackCooldown", "攻击冷却必须为正数");
    }

    if (profile.defenseDuration <= 0f)
    {
        result.AddError("DefenseDuration", "防御时长必须为正数");
    }

    if (profile.retreatDistance <= 0f)
    {
        result.AddError("RetreatDistance", "后撤距离必须为正数");
    }

    ValidateSkillWeights(profile.skillWeights, result);
}

// 校验概率值是否位于 0 到 1 之间。
private static void AddErrorIfProbabilityInvalid(float value, string fieldName, EnemyDefinitionValidationResult result)
{
    if (value < 0f || value > 1f)
    {
        result.AddError(fieldName, "概率值必须位于 0 到 1 之间");
    }
}

// 校验技能权重列表，避免非法技能编号或负权重进入决策。
private static void ValidateSkillWeights(EnemyWeightedSkill[] skillWeights, EnemyDefinitionValidationResult result)
{
    if (skillWeights == null)
    {
        return;
    }

    for (int i = 0; i < skillWeights.Length; i++)
    {
        EnemyWeightedSkill skill = skillWeights[i];
        if (skill == null)
        {
            result.AddError("SkillWeights", "技能权重项不能为空");
            continue;
        }

        if (skill.skillId <= 0)
        {
            result.AddError("SkillWeights", "技能编号必须为正数");
        }

        if (skill.weight < 0f)
        {
            result.AddError("SkillWeights", "技能权重不能为负数");
        }
    }
}
```

- [ ] **Step 6: Run compile**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: Unity compile succeeds, and the new EditMode test code compiles.

- [ ] **Step 7: Commit task 1**

Run:

```powershell
git add Assets/Game/Character/Enemy/Config/EnemyWeightedSkill.cs Assets/Game/Character/Enemy/Config/EnemyDecisionProfile.cs Assets/Game/Character/Enemy/Config/EnemyDefinition.cs Assets/Game/Character/Enemy/Config/EnemyDefinitionValidator.cs Assets/Game/Editor/EnemyDecisionProfileEditModeTests.cs
git commit -m "feat: add enemy decision profile config"
```

---

### Task 2: Blackboard Intent And Decision Facts

**Files:**
- Create: `Assets/Game/Character/Enemy/AI/EnemyCombatIntent.cs`
- Modify: `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs`
- Modify: `Assets/Game/Character/Enemy/AI/AIController.cs`
- Test: `Assets/Game/Editor/EnemyBlackboardDecisionFactsEditModeTests.cs`

**Interfaces:**
- Consumes: `EnemyDecisionProfile`, `EnemyCombatComponent.IsInAttackRange`, existing target facts.
- Produces:
  - `EnemyCombatIntent` enum.
  - `EnemyBlackboard.CurrentIntent`, `LastIntent`, `SelectedSkillId`, `DistanceToTarget`, `IsInAttackRange`, `IsTooCloseToTarget`.
  - `EnemyBlackboard.SetCombatIntent(EnemyCombatIntent intent)`.
  - `EnemyBlackboard.SetSelectedSkillId(int skillId)`.
  - `EnemyBlackboard.SetTargetDistanceFacts(float distance, bool isInAttackRange, bool isTooClose)`.
  - `AIController.DecisionProfile`.

- [ ] **Step 1: Write failing blackboard tests**

Create `Assets/Game/Editor/EnemyBlackboardDecisionFactsEditModeTests.cs`:

```csharp
using Game.Character.Enemy.AI;
using Game.Character.Enemy.Core;
using NUnit.Framework;

namespace Game.Character.Enemy.Tests
{
    public sealed class EnemyBlackboardDecisionFactsEditModeTests
    {
        /// <summary>验证写入新意图时会保留上一帧意图，便于调试状态切换。</summary>
        [Test]
        public void SetCombatIntent_StoresCurrentAndLastIntent()
        {
            EnemyBlackboard blackboard = new EnemyBlackboard();

            blackboard.SetCombatIntent(EnemyCombatIntent.Attack);
            blackboard.SetCombatIntent(EnemyCombatIntent.Defense);

            Assert.AreEqual(EnemyCombatIntent.Defense, blackboard.CurrentIntent);
            Assert.AreEqual(EnemyCombatIntent.Attack, blackboard.LastIntent);
        }

        /// <summary>验证目标距离事实会被集中写入黑板，供行为树条件节点读取。</summary>
        [Test]
        public void SetTargetDistanceFacts_StoresDistanceBooleans()
        {
            EnemyBlackboard blackboard = new EnemyBlackboard();

            blackboard.SetTargetDistanceFacts(1.5f, true, false);

            Assert.AreEqual(1.5f, blackboard.DistanceToTarget);
            Assert.IsTrue(blackboard.IsInAttackRange);
            Assert.IsFalse(blackboard.IsTooCloseToTarget);
        }

        /// <summary>验证清理目标时同步清理距离事实，避免行为树读取旧距离。</summary>
        [Test]
        public void ForgetTarget_ClearsDecisionDistanceFacts()
        {
            EnemyBlackboard blackboard = new EnemyBlackboard();
            blackboard.SetTargetDistanceFacts(1.5f, true, true);

            blackboard.ForgetTarget();

            Assert.AreEqual(0f, blackboard.DistanceToTarget);
            Assert.IsFalse(blackboard.IsInAttackRange);
            Assert.IsFalse(blackboard.IsTooCloseToTarget);
        }
    }
}
```

- [ ] **Step 2: Run compile to confirm missing members**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: compile fails because `EnemyCombatIntent` and new blackboard members do not exist.

- [ ] **Step 3: Create intent enum**

Create `Assets/Game/Character/Enemy/AI/EnemyCombatIntent.cs`:

```csharp
namespace Game.Character.Enemy.AI
{
    public enum EnemyCombatIntent
    {
        None = 0,
        Idle = 1,
        KeepDistance = 2,
        Approach = 3,
        Attack = 4,
        Defense = 5,
        Retreat = 6,
        Hurt = 7,
        Unbalance = 8,
        Dead = 9
    }
}
```

- [ ] **Step 4: Extend `EnemyBlackboard`**

Add `using Game.Character.Enemy.AI;` to `EnemyBlackboard.cs`.

Add these properties:

```csharp
public EnemyCombatIntent CurrentIntent { get; private set; }
public EnemyCombatIntent LastIntent { get; private set; }
public int SelectedSkillId { get; private set; }
public float DistanceToTarget { get; private set; }
public bool IsInAttackRange { get; private set; }
public bool IsTooCloseToTarget { get; private set; }
public float LastAttackDecisionTime { get; private set; }
public float LastDefenseDecisionTime { get; private set; }
public float LastRetreatDecisionTime { get; private set; }
```

Add these methods:

```csharp
// 写入当前战斗意图，并保留上一意图供调试状态切换。
public void SetCombatIntent(EnemyCombatIntent intent)
{
    if (CurrentIntent == intent)
    {
        return;
    }

    LastIntent = CurrentIntent;
    CurrentIntent = intent;
}

// 记录本次决策选择的技能编号，供攻击动作节点消费。
public void SetSelectedSkillId(int skillId)
{
    SelectedSkillId = skillId;
}

// 记录目标距离相关事实，行为树条件节点只读取黑板，不重复计算距离。
public void SetTargetDistanceFacts(float distance, bool isInAttackRange, bool isTooCloseToTarget)
{
    DistanceToTarget = distance;
    IsInAttackRange = isInAttackRange;
    IsTooCloseToTarget = isTooCloseToTarget;
}

// 记录攻击决策时间，供攻击冷却条件判断。
public void MarkAttackDecision(float time)
{
    LastAttackDecisionTime = time;
}

// 记录防御决策时间，供调试和后续冷却规则扩展。
public void MarkDefenseDecision(float time)
{
    LastDefenseDecisionTime = time;
}

// 记录后撤决策时间，供调试和后续冷却规则扩展。
public void MarkRetreatDecision(float time)
{
    LastRetreatDecisionTime = time;
}
```

Inside `ForgetTarget`, add:

```csharp
SetTargetDistanceFacts(0f, false, false);
```

Inside `SetDead(true)` before leaving the `if (isDead)` block, add:

```csharp
SetCombatIntent(EnemyCombatIntent.Dead);
SelectedSkillId = 0;
SetTargetDistanceFacts(0f, false, false);
```

- [ ] **Step 5: Expose profile and refresh facts in `AIController`**

Add property:

```csharp
public EnemyDecisionProfile DecisionProfile => definition != null ? definition.DecisionProfile : null;
```

In `TickAI`, after perception and memory updates and before `behaviorTreeRunner.Tick(deltaTime)`, call:

```csharp
RefreshDecisionFacts();
```

Add method:

```csharp
// 刷新行为树决策所需的距离事实，避免多个节点重复计算目标距离。
private void RefreshDecisionFacts()
{
    if (Blackboard.Target == null || context == null)
    {
        Blackboard.SetTargetDistanceFacts(0f, false, false);
        return;
    }

    float distance = Vector3.Distance(transform.position, Blackboard.Target.position);
    bool isInAttackRange = context.Combat != null && context.Combat.IsInAttackRange(Blackboard.Target);
    EnemyDecisionProfile profile = DecisionProfile;
    bool isTooClose = profile != null && distance < profile.minSafeDistance;
    Blackboard.SetTargetDistanceFacts(distance, isInAttackRange, isTooClose);
}
```

- [ ] **Step 6: Run compile**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: Unity compile succeeds.

- [ ] **Step 7: Commit task 2**

Run:

```powershell
git add Assets/Game/Character/Enemy/AI/EnemyCombatIntent.cs Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs Assets/Game/Character/Enemy/AI/AIController.cs Assets/Game/Editor/EnemyBlackboardDecisionFactsEditModeTests.cs
git commit -m "feat: add enemy combat decision facts"
```

---

### Task 3: Decision Rules And Behavior Tree Nodes

**Files:**
- Create: `Assets/Game/Character/Enemy/AI/EnemyDecisionRandom.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyShouldAttackNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyShouldDefendNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyShouldRetreatNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemySelectWeightedSkillNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemySetCombatIntentNodeAsset.cs`
- Test: `Assets/Game/Editor/EnemyDecisionRandomEditModeTests.cs`
- Test: `Assets/Game/Editor/EnemyBehaviorDecisionNodeEditModeTests.cs`

**Interfaces:**
- Consumes: `EnemyDecisionProfile`, `EnemyBlackboard`, `AIController.DecisionProfile`.
- Produces:
  - `EnemyDecisionRandom.Passes(float chance, float roll)`.
  - `EnemyDecisionRandom.SelectSkill(EnemyWeightedSkill[] skills, float roll, int fallbackSkillId)`.
  - Behavior tree nodes that can be assembled into base enemy, aggressive enemy, shield enemy, and ranged enemy trees.

- [ ] **Step 1: Write deterministic rule tests**

Create `Assets/Game/Editor/EnemyDecisionRandomEditModeTests.cs`:

```csharp
using Game.Character.Enemy.AI;
using Game.Character.Enemy.Config;
using NUnit.Framework;

namespace Game.Character.Enemy.Tests
{
    public sealed class EnemyDecisionRandomEditModeTests
    {
        /// <summary>验证概率判定使用小于等于规则，便于边界值测试。</summary>
        [Test]
        public void Passes_RollEqualChance_ReturnsTrue()
        {
            Assert.IsTrue(EnemyDecisionRandom.Passes(0.5f, 0.5f));
        }

        /// <summary>验证权重选择会跳过零权重技能并返回有效技能。</summary>
        [Test]
        public void SelectSkill_UsesPositiveWeights()
        {
            EnemyWeightedSkill[] skills =
            {
                new EnemyWeightedSkill(20001, 0f),
                new EnemyWeightedSkill(20002, 2f)
            };

            int selected = EnemyDecisionRandom.SelectSkill(skills, 0.25f, 20001);

            Assert.AreEqual(20002, selected);
        }

        /// <summary>验证空技能权重列表会返回攻击配置里的兜底技能。</summary>
        [Test]
        public void SelectSkill_EmptyWeights_ReturnsFallback()
        {
            int selected = EnemyDecisionRandom.SelectSkill(new EnemyWeightedSkill[0], 0.75f, 20001);

            Assert.AreEqual(20001, selected);
        }
    }
}
```

- [ ] **Step 2: Run compile to confirm missing helper**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: compile fails because `EnemyDecisionRandom` does not exist.

- [ ] **Step 3: Create decision random helper**

Create `Assets/Game/Character/Enemy/AI/EnemyDecisionRandom.cs`:

```csharp
using Game.Character.Enemy.Config;

namespace Game.Character.Enemy.AI
{
    public static class EnemyDecisionRandom
    {
        /// <summary>根据概率和随机值判断本次决策是否通过。</summary>
        public static bool Passes(float chance, float roll)
        {
            return roll <= chance;
        }

        /// <summary>按技能权重选择技能，权重为空或总和无效时返回兜底技能。</summary>
        public static int SelectSkill(EnemyWeightedSkill[] skills, float roll, int fallbackSkillId)
        {
            if (skills == null || skills.Length == 0)
            {
                return fallbackSkillId;
            }

            float totalWeight = 0f;
            for (int i = 0; i < skills.Length; i++)
            {
                if (skills[i] != null && skills[i].weight > 0f && skills[i].skillId > 0)
                {
                    totalWeight += skills[i].weight;
                }
            }

            if (totalWeight <= 0f)
            {
                return fallbackSkillId;
            }

            float threshold = roll * totalWeight;
            float accumulated = 0f;
            for (int i = 0; i < skills.Length; i++)
            {
                EnemyWeightedSkill skill = skills[i];
                if (skill == null || skill.weight <= 0f || skill.skillId <= 0)
                {
                    continue;
                }

                accumulated += skill.weight;
                if (threshold <= accumulated)
                {
                    return skill.skillId;
                }
            }

            return fallbackSkillId;
        }
    }
}
```

- [ ] **Step 4: Create attack condition node**

Create `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyShouldAttackNodeAsset.cs`:

```csharp
using Game.Character.Enemy.Config;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Should Attack")]
    public sealed class EnemyShouldAttackNodeAsset : ConditionNodeAsset
    {
        /// <summary>根据攻击冷却、攻击距离和攻击欲望判断是否进入进攻分支。</summary>
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            if (!EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller))
            {
                return false;
            }

            EnemyDecisionProfile profile = controller.DecisionProfile;
            if (profile == null || !controller.Blackboard.IsInAttackRange)
            {
                return false;
            }

            float elapsed = Time.time - controller.Blackboard.LastAttackDecisionTime;
            return elapsed >= profile.attackCooldown
                && EnemyDecisionRandom.Passes(profile.attackDesire, Random.value);
        }
    }
}
```

- [ ] **Step 5: Create defense condition node**

Create `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyShouldDefendNodeAsset.cs`:

```csharp
using Game.Character.Enemy.Config;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Should Defend")]
    public sealed class EnemyShouldDefendNodeAsset : ConditionNodeAsset
    {
        /// <summary>根据防御率判断是否进入防御分支。</summary>
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            if (!EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller))
            {
                return false;
            }

            EnemyDecisionProfile profile = controller.DecisionProfile;
            return profile != null
                && !controller.Blackboard.IsHitReactionInProgress
                && EnemyDecisionRandom.Passes(profile.defenseRate, Random.value);
        }
    }
}
```

- [ ] **Step 6: Create retreat condition node**

Create `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyShouldRetreatNodeAsset.cs`:

```csharp
using Game.Character.Enemy.Components;
using Game.Character.Enemy.Config;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Should Retreat")]
    public sealed class EnemyShouldRetreatNodeAsset : ConditionNodeAsset
    {
        /// <summary>根据近身压力、低稳定值和后撤倾向判断是否进入后撤分支。</summary>
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

            bool lowStability = IsLowStability(controller.Context != null ? controller.Context.Attribute : null, profile);
            bool pressureRetreat = controller.Blackboard.IsTooCloseToTarget || lowStability;
            return pressureRetreat && EnemyDecisionRandom.Passes(profile.retreatDesire, Random.value);
        }

        /// <summary>计算稳定值比例并判断是否低于配置阈值。</summary>
        private static bool IsLowStability(EnemyAttributeComponent attribute, EnemyDecisionProfile profile)
        {
            if (attribute == null || attribute.MaxStability <= 0)
            {
                return false;
            }

            float ratio = (float)attribute.Stability / attribute.MaxStability;
            return profile.IsLowStability(ratio);
        }
    }
}
```

- [ ] **Step 7: Create weighted skill selector node**

Create `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemySelectWeightedSkillNodeAsset.cs`:

```csharp
using Game.Character.Enemy.Config;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Select Weighted Skill")]
    public sealed class EnemySelectWeightedSkillNodeAsset : ActionNodeAsset
    {
        /// <summary>创建用于选择技能的运行时节点。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new EnemySelectWeightedSkillNode(this);
        }

        /// <summary>资产层不直接执行，实际逻辑由运行时节点完成。</summary>
        protected override BehaviorTreeStatus Execute(BehaviorTreeContext context)
        {
            return BehaviorTreeStatus.Failure;
        }

        private sealed class EnemySelectWeightedSkillNode : BehaviorTreeNode
        {
            /// <summary>绑定技能选择资产，满足行为树运行时节点构造约束。</summary>
            public EnemySelectWeightedSkillNode(EnemySelectWeightedSkillNodeAsset asset)
                : base(asset)
            {
            }

            /// <summary>按配置权重选择技能并写入黑板。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (!EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller))
                {
                    return BehaviorTreeStatus.Failure;
                }

                EnemyDecisionProfile profile = controller.DecisionProfile;
                int fallback = controller.Definition != null && controller.Definition.CombatConfig != null
                    ? controller.Definition.CombatConfig.firstAttackSkillId
                    : 0;
                int selected = EnemyDecisionRandom.SelectSkill(
                    profile != null ? profile.skillWeights : null,
                    Random.value,
                    fallback);
                if (selected <= 0)
                {
                    return BehaviorTreeStatus.Failure;
                }

                controller.Blackboard.SetSelectedSkillId(selected);
                return BehaviorTreeStatus.Success;
            }
        }
    }
}
```

- [ ] **Step 8: Create generic intent action node**

Create `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemySetCombatIntentNodeAsset.cs`:

```csharp
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Set Combat Intent")]
    public sealed class EnemySetCombatIntentNodeAsset : ActionNodeAsset
    {
        [SerializeField] private EnemyCombatIntent intent;

        /// <summary>创建写入战斗意图的运行时节点。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new EnemySetCombatIntentNode(this);
        }

        /// <summary>资产层不直接执行，实际逻辑由运行时节点完成。</summary>
        protected override BehaviorTreeStatus Execute(BehaviorTreeContext context)
        {
            return BehaviorTreeStatus.Failure;
        }

        /// <summary>设置测试用战斗意图，避免测试依赖序列化资源。</summary>
        public void SetIntentForTests(EnemyCombatIntent value)
        {
            intent = value;
        }

        private sealed class EnemySetCombatIntentNode : BehaviorTreeNode
        {
            private readonly EnemySetCombatIntentNodeAsset asset;

            /// <summary>绑定意图资产，供运行时读取序列化配置。</summary>
            public EnemySetCombatIntentNode(EnemySetCombatIntentNodeAsset asset)
                : base(asset)
            {
                this.asset = asset;
            }

            /// <summary>写入当前战斗意图，并记录对应决策时间。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (!EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller))
                {
                    return BehaviorTreeStatus.Failure;
                }

                controller.Blackboard.SetCombatIntent(asset.intent);
                if (asset.intent == EnemyCombatIntent.Attack)
                {
                    controller.Blackboard.MarkAttackDecision(Time.time);
                }
                else if (asset.intent == EnemyCombatIntent.Defense)
                {
                    controller.Blackboard.MarkDefenseDecision(Time.time);
                }
                else if (asset.intent == EnemyCombatIntent.Retreat)
                {
                    controller.Blackboard.MarkRetreatDecision(Time.time);
                }

                return BehaviorTreeStatus.Success;
            }
        }
    }
}
```

- [ ] **Step 9: Add node smoke tests**

Create `Assets/Game/Editor/EnemyBehaviorDecisionNodeEditModeTests.cs`:

```csharp
using Game.Character.Enemy.AI;
using Game.Character.Enemy.AI.BehaviorTree;
using NUnit.Framework;

namespace Game.Character.Enemy.Tests
{
    public sealed class EnemyBehaviorDecisionNodeEditModeTests
    {
        /// <summary>验证测试入口能设置意图资产，保证行为树资产可被编辑器测试构造。</summary>
        [Test]
        public void SetCombatIntentNode_SetIntentForTests_DoesNotThrow()
        {
            EnemySetCombatIntentNodeAsset asset = UnityEngine.ScriptableObject.CreateInstance<EnemySetCombatIntentNodeAsset>();

            asset.SetIntentForTests(EnemyCombatIntent.Attack);

            Assert.NotNull(asset);
        }
    }
}
```

- [ ] **Step 10: Run compile**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: Unity compile succeeds.

- [ ] **Step 11: Commit task 3**

Run:

```powershell
git add Assets/Game/Character/Enemy/AI/EnemyDecisionRandom.cs Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyShouldAttackNodeAsset.cs Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyShouldDefendNodeAsset.cs Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyShouldRetreatNodeAsset.cs Assets/Game/Character/Enemy/AI/BehaviorTree/EnemySelectWeightedSkillNodeAsset.cs Assets/Game/Character/Enemy/AI/BehaviorTree/EnemySetCombatIntentNodeAsset.cs Assets/Game/Editor/EnemyDecisionRandomEditModeTests.cs Assets/Game/Editor/EnemyBehaviorDecisionNodeEditModeTests.cs
git commit -m "feat: add enemy decision behavior tree nodes"
```

---

### Task 4: Defense, Retreat, And Keep Distance Execution

**Files:**
- Modify: `Assets/Game/Character/Enemy/Config/EnemyAnimationConfig.cs`
- Modify: `Assets/Game/Character/Enemy/Config/EnemyDefinitionValidator.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyMovementComponent.cs`
- Modify: `Assets/Game/Character/Enemy/Core/EnemyAgent.cs`
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemySetIntentNodeAsset.cs`
- Test: `Assets/Game/Editor/EnemyCombatExecutionEditModeTests.cs`

**Interfaces:**
- Consumes: `EnemyDecisionProfile.defenseDuration`, `retreatDistance`, `preferredDistance`, `minSafeDistance`.
- Produces:
  - `EnemyAnimationConfig.defenseAnimation`
  - `EnemyAnimationConfig.retreatAnimation`
  - `EnemyCombatComponent.Tick(float deltaTime)`
  - `EnemyCombatComponent.StartDefense(float duration)`
  - `EnemyCombatComponent.StopDefense()`
  - `EnemyCombatComponent.IsDefending`
  - `EnemyMovementComponent.MoveAwayFrom(Transform target, float distance)`
  - `EnemyBehaviorActionType.Defense`, `Retreat`, `KeepDistance`

- [ ] **Step 1: Write execution tests**

Create `Assets/Game/Editor/EnemyCombatExecutionEditModeTests.cs`:

```csharp
using Game.Character.Enemy.Components;
using NUnit.Framework;
using UnityEngine;

namespace Game.Character.Enemy.Tests
{
    public sealed class EnemyCombatExecutionEditModeTests
    {
        /// <summary>验证防御状态会按倒计时结束，供行为树防御动作使用。</summary>
        [Test]
        public void DefenseTick_DurationElapsed_StopsDefending()
        {
            GameObject enemy = new GameObject("enemy");
            EnemyCombatComponent combat = enemy.AddComponent<EnemyCombatComponent>();

            combat.StartDefense(0.5f);
            combat.Tick(0.6f);

            Assert.IsFalse(combat.IsDefending);
            Object.DestroyImmediate(enemy);
        }

        /// <summary>验证后撤目标会被设置到远离目标的方向。</summary>
        [Test]
        public void MoveAwayFrom_TargetInFront_SetsDestination()
        {
            GameObject enemy = new GameObject("enemy");
            GameObject target = new GameObject("target");
            enemy.transform.position = Vector3.zero;
            target.transform.position = Vector3.forward;
            EnemyMovementComponent movement = enemy.AddComponent<EnemyMovementComponent>();

            movement.MoveAwayFrom(target.transform, 2f);

            Assert.IsTrue(movement.HasDestination);
            Object.DestroyImmediate(enemy);
            Object.DestroyImmediate(target);
        }
    }
}
```

- [ ] **Step 2: Run compile to confirm missing execution members**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: compile fails because defense and retreat execution APIs do not exist.

- [ ] **Step 3: Extend animation config**

Modify `EnemyAnimationConfig.cs`:

```csharp
public string defenseAnimation = "Defense";
public string retreatAnimation = "Retreat";
```

Modify `EnemyDefinitionValidator.ValidateAnimationConfig`:

```csharp
AddErrorIfEmpty(animationConfig.defenseAnimation, "DefenseAnimation", "防御动画名不能为空", result);
AddErrorIfEmpty(animationConfig.retreatAnimation, "RetreatAnimation", "后撤动画名不能为空", result);
```

- [ ] **Step 4: Add defense execution to combat component**

Modify `EnemyCombatComponent.cs`:

```csharp
private float defenseRemainingTime;

public bool IsDefending { get; private set; }

/// <summary>按帧推进防御倒计时，到期后自动结束防御。</summary>
public void Tick(float deltaTime)
{
    if (!IsDefending)
    {
        return;
    }

    defenseRemainingTime -= deltaTime;
    if (defenseRemainingTime <= 0f)
    {
        StopDefense();
    }
}

/// <summary>进入防御状态，防御期间普通攻击不会启动新的攻击动作。</summary>
public void StartDefense(float duration)
{
    InterruptAction();
    IsDefending = true;
    defenseRemainingTime = Mathf.Max(0.01f, duration);
}

/// <summary>结束防御状态并清理防御倒计时。</summary>
public void StopDefense()
{
    IsDefending = false;
    defenseRemainingTime = 0f;
}
```

At the start of `TryCast`, add:

```csharp
if (IsDefending)
{
    config = null;
    return false;
}
```

- [ ] **Step 5: Tick combat component from agent**

Modify `EnemyAgent.Update` after `movement.Tick(deltaTime)`:

```csharp
if (combat != null)
{
    combat.Tick(deltaTime);
}
```

- [ ] **Step 6: Add retreat movement helper**

Modify `EnemyMovementComponent.cs`:

```csharp
/// <summary>向远离目标的方向移动指定距离，用于后撤行为。</summary>
public void MoveAwayFrom(Transform target, float distance)
{
    if (target == null)
    {
        return;
    }

    Vector3 direction = transform.position - target.position;
    direction.y = 0f;
    if (direction.sqrMagnitude <= 0.0001f)
    {
        direction = -transform.forward;
    }

    Vector3 destination = transform.position + direction.normalized * distance;
    MoveTo(destination);
}
```

- [ ] **Step 7: Extend behavior action enum**

Modify `EnemyBehaviorActionType` in `EnemySetIntentNodeAsset.cs`:

```csharp
Defense = 7,
Retreat = 8,
KeepDistance = 9,
```

Add switch cases in `Tick`:

```csharp
case EnemyBehaviorActionType.Defense:
    return TickDefense(controller);
case EnemyBehaviorActionType.Retreat:
    return TickRetreat(controller);
case EnemyBehaviorActionType.KeepDistance:
    return TickKeepDistance(controller);
```

Add methods:

```csharp
/// <summary>启动防御动作并在防御组件倒计时结束前保持运行。</summary>
private static BehaviorTreeStatus TickDefense(AIController controller)
{
    if (controller.Context == null || controller.Context.Combat == null)
    {
        return BehaviorTreeStatus.Failure;
    }

    EnemyDecisionProfile profile = controller.DecisionProfile;
    float duration = profile != null ? profile.defenseDuration : 1f;
    if (!controller.Context.Combat.IsDefending)
    {
        controller.Context.Movement?.Stop();
        controller.Context.Combat.StartDefense(duration);
        controller.Context.Animation?.TryPlay(controller.Definition != null ? controller.Definition.AnimationConfig.defenseAnimation : null);
        controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Defense);
        controller.Blackboard.MarkDefenseDecision(Time.time);
    }

    return controller.Context.Combat.IsDefending ? BehaviorTreeStatus.Running : BehaviorTreeStatus.Success;
}

/// <summary>执行后撤移动并在达到安全距离前保持运行。</summary>
private static BehaviorTreeStatus TickRetreat(AIController controller)
{
    if (controller.Context == null || controller.Context.Movement == null || controller.Blackboard.Target == null)
    {
        return BehaviorTreeStatus.Failure;
    }

    EnemyDecisionProfile profile = controller.DecisionProfile;
    float retreatDistance = profile != null ? profile.retreatDistance : 2f;
    controller.Context.Animation?.TryPlay(controller.Definition != null ? controller.Definition.AnimationConfig.retreatAnimation : null);
    controller.Context.Movement.MoveAwayFrom(controller.Blackboard.Target, retreatDistance);
    controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Retreat);
    controller.Blackboard.MarkRetreatDecision(Time.time);
    return BehaviorTreeStatus.Success;
}

/// <summary>根据期望距离靠近或停止，维持战斗待机距离。</summary>
private static BehaviorTreeStatus TickKeepDistance(AIController controller)
{
    if (controller.Context == null || controller.Context.Movement == null || controller.Blackboard.Target == null)
    {
        return BehaviorTreeStatus.Failure;
    }

    EnemyDecisionProfile profile = controller.DecisionProfile;
    float preferredDistance = profile != null ? profile.preferredDistance : controller.Context.Movement.StoppingDistance;
    if (controller.Blackboard.DistanceToTarget > preferredDistance)
    {
        controller.Context.Animation?.TryPlay(controller.Definition != null ? controller.Definition.AnimationConfig.moveAnimation : null);
        controller.Context.Movement.MoveTo(controller.Blackboard.Target);
        controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Approach);
        return BehaviorTreeStatus.Success;
    }

    controller.Context.Movement.Stop();
    controller.Context.Movement.LookAt(controller.Blackboard.Target.position);
    controller.Context.Animation?.TryPlay(controller.Definition != null ? controller.Definition.AnimationConfig.idleAnimation : null);
    controller.Blackboard.SetCombatIntent(EnemyCombatIntent.KeepDistance);
    return BehaviorTreeStatus.Success;
}
```

- [ ] **Step 8: Run compile**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: Unity compile succeeds.

- [ ] **Step 9: Commit task 4**

Run:

```powershell
git add Assets/Game/Character/Enemy/Config/EnemyAnimationConfig.cs Assets/Game/Character/Enemy/Config/EnemyDefinitionValidator.cs Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs Assets/Game/Character/Enemy/Components/EnemyMovementComponent.cs Assets/Game/Character/Enemy/Core/EnemyAgent.cs Assets/Game/Character/Enemy/AI/BehaviorTree/EnemySetIntentNodeAsset.cs Assets/Game/Editor/EnemyCombatExecutionEditModeTests.cs
git commit -m "feat: execute enemy defense retreat decisions"
```

---

### Task 5: Final Validation And Handoff

**Files:**
- Verify all files touched in Tasks 1-4.
- Do not stage `Assets/Scenes/Scene1.unity` unless the user explicitly asks.

**Interfaces:**
- Consumes: all previous task commits.
- Produces: compile result and implementation handoff summary.

- [ ] **Step 1: Confirm staged/untracked scope**

Run:

```powershell
git status --short
```

Expected: only intended AI implementation files are modified or staged. If `Assets/Scenes/Scene1.unity` still appears, leave it unstaged.

- [ ] **Step 2: Run Unity compile**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: Unity compile succeeds.

- [ ] **Step 3: Check Unity errors**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe get_logs --logType Error
```

Expected: no new errors from enemy AI decision code.

- [ ] **Step 4: Handle validation failure if needed**

If compile or log checks fail, stop execution and return to the task that introduced the failing file. Fix that task's listed files, rerun `.\.aibridge\cli\AIBridgeCLI.exe compile unity`, then commit the corrected task using that task's commit step.

- [ ] **Step 5: Final handoff**

Report:

```text
已实现敌人行为树决策层基础框架。
已验证：.\.aibridge\cli\AIBridgeCLI.exe compile unity
未提交用户原有场景改动：Assets/Scenes/Scene1.unity
```

---

## Self-Review

- Spec coverage: Tasks 1-4 cover decision profile, blackboard facts, behavior tree decision nodes, five base decision states, extension by config/tree/node, and execution boundary.
- Completion-marker scan: no unfinished-marker text or incomplete implementation steps are present.
- Type consistency: `EnemyDecisionProfile`, `EnemyWeightedSkill`, `EnemyCombatIntent`, `EnemyDecisionRandom`, and blackboard method names are introduced before later tasks consume them.
- Scope check: this remains a single implementation plan for the base decision layer; GOAP and advanced group AI remain outside first version.
