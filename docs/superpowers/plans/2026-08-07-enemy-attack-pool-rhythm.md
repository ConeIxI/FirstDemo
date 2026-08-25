# Enemy Attack Pool Rhythm Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a generic enemy attack-pool rhythm selector so Boss can choose between approach, close, and retreat attack pools while ordinary enemies safely skip empty optional pools.

**Architecture:** Keep `EnemyCombatDecisionController` as the single behavior-tree-facing entry point. Add serializable rhythm weights to `EnemyCombatConfig`, expose `retreatAttacks` through `EnemyAttackCatalog`, extend `EnemyAttackPlanType` with `Retreat`, and update `TryCreateAttackPlan` so it first evaluates approach opportunities and then weights close versus retreat pools. Attack flow will notify the controller when a plan completes so close attacks can increase the next retreat-pool weight.

**Tech Stack:** Unity 2022.3.61f1c1, C# 9.0, NUnit EditMode tests, AIBridge Unity compile/test runner.

---

## Current Workspace Note

The workspace already contains uncommitted changes from the prior `retreatAttacks` work:

- `Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs`
- `Assets/Game/Character/Enemy/AI/Combat/EnemyAttackCatalog.cs`
- `Assets/Game/Character/Enemy/Config/Definitions/GreatSwordBossDefinition.asset`
- `docs/superpowers/plans/2026-08-07-enemy-retreat-attack-pool.md`

Treat those as in-scope prerequisites for this plan. Do not stage or modify `Assets/Data/EnemySkillConfig.json`; it is an unrelated existing workspace change.

## File Structure

- Modify: `Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs` adds optional rhythm weights and keeps `retreatAttacks` as an optional attack pool.
- Modify: `Assets/Game/Character/Enemy/AI/Combat/EnemyAttackCatalog.cs` ensures `RetreatAttacks` is bound and exposed.
- Modify: `Assets/Game/Character/Enemy/AI/Combat/EnemyAttackPlan.cs` adds `EnemyAttackPlanType.Retreat`.
- Modify: `Assets/Game/Character/Enemy/AI/Combat/EnemyCombatDecisionController.cs` implements approach-first pool selection, close-versus-retreat rhythm weighting, and plan completion rhythm updates.
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyAttackFlowNodeAsset.cs` calls the decision controller before clearing a completed attack plan.
- Modify: `Assets/Game/Character/Enemy/Config/Definitions/GreatSwordBossDefinition.asset` serializes Boss rhythm weights and keeps an empty retreat pool until skills are assigned.
- Modify: `Assets/Game/Editor/EnemyAttackCatalogEditModeTests.cs` verifies the retreat pool is bound.
- Modify: `Assets/Game/Editor/EnemyAttackPlanDecisionEditModeTests.cs` verifies empty optional pools, approach priority, retreat selection, and retreat bonus reset.

### Task 1: Lock In Retreat Pool Config Support

**Files:**
- Modify: `Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs`
- Modify: `Assets/Game/Character/Enemy/AI/Combat/EnemyAttackCatalog.cs`
- Modify: `Assets/Game/Character/Enemy/Config/Definitions/GreatSwordBossDefinition.asset`
- Modify: `Assets/Game/Editor/EnemyAttackCatalogEditModeTests.cs`

- [x] **Step 1: Confirm serialized config field exists**

In `Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs`, keep `retreatAttacks` directly after `pursuitAttacks`:

```csharp
public EnemyAttackConfig[] approachAttacks = new EnemyAttackConfig[0];
public EnemyAttackConfig[] pursuitAttacks = new EnemyAttackConfig[0];
public EnemyAttackConfig[] retreatAttacks = new EnemyAttackConfig[0];
```

- [x] **Step 2: Confirm runtime catalog binds retreat attacks**

In `Assets/Game/Character/Enemy/AI/Combat/EnemyAttackCatalog.cs`, keep the public property and constructor assignment:

```csharp
public IReadOnlyList<EnemyAttackRuntimeConfig> RetreatAttacks { get; }
```

```csharp
EnemyAttackRuntimeConfig[] retreatAttacks = BindAttackPool(config.retreatAttacks, skillResolver);
return new EnemyAttackCatalog(basicAttacks, approachAttacks, pursuitAttacks, retreatAttacks, counterAttack);
```

- [x] **Step 3: Add catalog regression test for retreat pool**

In `Assets/Game/Editor/EnemyAttackCatalogEditModeTests.cs`, update `Create_ValidConfig_BuildsPoolsAndBasicAttackRange` so the config and skill map include retreat skill `6`:

```csharp
EnemyCombatConfig config = new EnemyCombatConfig
{
    basicAttacks = new[]
    {
        new EnemyAttackConfig(1, "Attack1", 1f),
        new EnemyAttackConfig(2, "Attack2", 1f)
    },
    approachAttacks = new[] { new EnemyAttackConfig(3, "Thrust", 1f) },
    pursuitAttacks = new[] { new EnemyAttackConfig(4, "Leap", 1f) },
    retreatAttacks = new[] { new EnemyAttackConfig(6, "BackSlash", 1f) },
    counterAttack = new EnemyAttackConfig(5, "Counter", 1f)
};
Dictionary<int, SkillConfig> skills = new Dictionary<int, SkillConfig>
{
    { 1, CreateSkill(1, 2f) },
    { 2, CreateSkill(2, 4f) },
    { 3, CreateSkill(3, 6f) },
    { 4, CreateSkill(4, 10f) },
    { 5, CreateSkill(5, 4f) },
    { 6, CreateSkill(6, 3f) }
};
```

Add this assertion with the existing catalog assertions:

```csharp
Assert.AreEqual(6, catalog.RetreatAttacks[0].SkillId);
Assert.AreEqual(3f, catalog.RetreatAttacks[0].AttackRange);
```

- [x] **Step 4: Run catalog regression test**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --test-name "Game.Character.Enemy.Tests.EnemyAttackCatalogEditModeTests.Create_ValidConfig_BuildsPoolsAndBasicAttackRange"
```

Expected: PASS after `retreatAttacks` binding is present.

- [x] **Step 5: Commit retreat pool support**

Run:

```powershell
git add -- "Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs" "Assets/Game/Character/Enemy/AI/Combat/EnemyAttackCatalog.cs" "Assets/Game/Character/Enemy/Config/Definitions/GreatSwordBossDefinition.asset" "Assets/Game/Editor/EnemyAttackCatalogEditModeTests.cs" "docs/superpowers/plans/2026-08-07-enemy-retreat-attack-pool.md"
git commit -m "添加敌人远离攻击池配置"
```

Expected: commit includes only retreat-pool config/catalog/resource/test/plan files, not `Assets/Data/EnemySkillConfig.json`.

### Task 2: Add Rhythm Configuration And Plan Type

**Files:**
- Modify: `Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs`
- Modify: `Assets/Game/Character/Enemy/AI/Combat/EnemyAttackPlan.cs`
- Modify: `Assets/Game/Character/Enemy/Config/Definitions/GreatSwordBossDefinition.asset`

- [x] **Step 1: Add rhythm config fields**

In `Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs`, place these fields after `retreatAttacks`:

```csharp
public float closeAttackPoolWeight = 1f;
public float retreatAttackPoolWeight;
public float retreatWeightBonusAfterCloseAttack;
public float retreatWeightBonusLimit;
public bool resetRetreatBonusAfterRetreat = true;
```

These defaults preserve current enemy behavior: close pool is enabled, retreat pool is disabled unless configured.

- [x] **Step 2: Add retreat plan type**

In `Assets/Game/Character/Enemy/AI/Combat/EnemyAttackPlan.cs`, update the enum:

```csharp
public enum EnemyAttackPlanType
{
    Basic,
    Approach,
    Retreat,
    Pursuit,
    Counter
}
```

- [x] **Step 3: Serialize Boss rhythm weights**

In `Assets/Game/Character/Enemy/Config/Definitions/GreatSwordBossDefinition.asset`, place these fields after `retreatAttacks: []`:

```yaml
    closeAttackPoolWeight: 1
    retreatAttackPoolWeight: 0.35
    retreatWeightBonusAfterCloseAttack: 0.5
    retreatWeightBonusLimit: 1.5
    resetRetreatBonusAfterRetreat: 1
```

- [x] **Step 4: Run Unity compile**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
```

Expected: `success: true`, `errorCount: 0`.

- [x] **Step 5: Commit rhythm data surface**

Run:

```powershell
git add -- "Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs" "Assets/Game/Character/Enemy/AI/Combat/EnemyAttackPlan.cs" "Assets/Game/Character/Enemy/Config/Definitions/GreatSwordBossDefinition.asset"
git commit -m "添加敌人攻击池节奏配置"
```

Expected: commit contains only config fields, plan type, and Boss serialized values.

### Task 3: Add Pool Rhythm Decision Tests

**Files:**
- Modify: `Assets/Game/Editor/EnemyAttackPlanDecisionEditModeTests.cs`

- [x] **Step 1: Add empty optional pools test**

Add this test to `EnemyAttackPlanDecisionEditModeTests`:

```csharp
/// <summary>验证进身池和远离池为空时，敌人仍会使用近距离主攻击池。</summary>
[Test]
public void TryCreateAttackPlan_EmptyOptionalPools_CreatesBasicPlan()
{
    EnemyCombatDecisionController controller = CreateRhythmController(
        includeApproach: false,
        includeRetreat: false,
        closeWeight: 1f,
        retreatWeight: 0f,
        retreatBonus: 0f,
        retreatBonusLimit: 0f);

    bool created = controller.TryCreateAttackPlan(10f, 1f, 1f, 8f, 0f);

    Assert.IsTrue(created);
    Assert.AreEqual(EnemyAttackPlanType.Basic, controller.CurrentPlan.Type);
    Assert.AreEqual(20301, controller.CurrentPlan.CurrentAttack.SkillId);
}
```

- [x] **Step 2: Add approach priority test**

Add this test to the same file:

```csharp
/// <summary>验证目标超出近距离池覆盖范围但进身池可覆盖时，优先创建进身攻击计划。</summary>
[Test]
public void TryCreateAttackPlan_ApproachCoversDistanceBeforeCloseRhythm_CreatesApproachPlan()
{
    EnemyCombatDecisionController controller = CreateRhythmController(
        includeApproach: true,
        includeRetreat: true,
        closeWeight: 1f,
        retreatWeight: 1f,
        retreatBonus: 0f,
        retreatBonusLimit: 0f);

    bool created = controller.TryCreateAttackPlan(10f, 1f, 5f, 8f, 0.99f);

    Assert.IsTrue(created);
    Assert.AreEqual(EnemyAttackPlanType.Approach, controller.CurrentPlan.Type);
    Assert.AreEqual(20307, controller.CurrentPlan.CurrentAttack.SkillId);
}
```

- [x] **Step 3: Add retreat weighted selection test**

Add this test to the same file:

```csharp
/// <summary>验证近距离节奏抽选命中远离池时，会创建远离攻击计划。</summary>
[Test]
public void TryCreateAttackPlan_RetreatPoolSelectedByRhythmWeight_CreatesRetreatPlan()
{
    EnemyCombatDecisionController controller = CreateRhythmController(
        includeApproach: false,
        includeRetreat: true,
        closeWeight: 1f,
        retreatWeight: 1f,
        retreatBonus: 0f,
        retreatBonusLimit: 0f);

    bool created = controller.TryCreateAttackPlan(10f, 1f, 1f, 8f, 0.75f);

    Assert.IsTrue(created);
    Assert.AreEqual(EnemyAttackPlanType.Retreat, controller.CurrentPlan.Type);
    Assert.AreEqual(20309, controller.CurrentPlan.CurrentAttack.SkillId);
}
```

- [x] **Step 4: Add close-completion bonus test**

Add this test to the same file:

```csharp
/// <summary>验证近距离攻击完成后，远离池权重加成会让下一次近距离节奏选择命中远离池。</summary>
[Test]
public void CompleteCurrentPlan_BasicDirectPlan_IncreasesRetreatWeight()
{
    EnemyCombatDecisionController controller = CreateRhythmController(
        includeApproach: false,
        includeRetreat: true,
        closeWeight: 1f,
        retreatWeight: 0f,
        retreatBonus: 2f,
        retreatBonusLimit: 2f);

    Assert.IsTrue(controller.TryCreateAttackPlan(10f, 1f, 1f, 8f, 0.99f));
    Assert.AreEqual(EnemyAttackPlanType.Basic, controller.CurrentPlan.Type);

    controller.CompleteCurrentPlan();
    controller.ResetAttack();

    Assert.IsTrue(controller.TryCreateAttackPlan(11f, 1f, 1f, 8f, 0.99f));
    Assert.AreEqual(EnemyAttackPlanType.Retreat, controller.CurrentPlan.Type);
    Assert.AreEqual(20309, controller.CurrentPlan.CurrentAttack.SkillId);
}
```

- [x] **Step 5: Add retreat reset test**

Add this test to the same file:

```csharp
/// <summary>验证远离攻击完成后，远离池权重加成会按配置重置。</summary>
[Test]
public void CompleteCurrentPlan_RetreatPlan_ResetsRetreatBonus()
{
    EnemyCombatDecisionController controller = CreateRhythmController(
        includeApproach: false,
        includeRetreat: true,
        closeWeight: 1f,
        retreatWeight: 0f,
        retreatBonus: 2f,
        retreatBonusLimit: 2f);

    Assert.IsTrue(controller.TryCreateAttackPlan(10f, 1f, 1f, 8f, 0.99f));
    Assert.AreEqual(EnemyAttackPlanType.Basic, controller.CurrentPlan.Type);
    controller.CompleteCurrentPlan();
    controller.ResetAttack();

    Assert.IsTrue(controller.TryCreateAttackPlan(11f, 1f, 1f, 8f, 0.99f));
    Assert.AreEqual(EnemyAttackPlanType.Retreat, controller.CurrentPlan.Type);
    controller.CompleteCurrentPlan();
    controller.ResetAttack();

    Assert.IsTrue(controller.TryCreateAttackPlan(12f, 1f, 1f, 8f, 0.99f));
    Assert.AreEqual(EnemyAttackPlanType.Basic, controller.CurrentPlan.Type);
}
```

- [x] **Step 6: Add rhythm controller test helper**

Add this helper near the existing helpers in the same file:

```csharp
/// <summary>创建带近距离、进身和远离攻击池的节奏决策器。</summary>
private static EnemyCombatDecisionController CreateRhythmController(
    bool includeApproach,
    bool includeRetreat,
    float closeWeight,
    float retreatWeight,
    float retreatBonus,
    float retreatBonusLimit)
{
    EnemyCombatConfig config = new EnemyCombatConfig
    {
        basicAttacks = new[] { new EnemyAttackConfig(20301, "Attack1", 1f) },
        approachAttacks = includeApproach
            ? new[] { new EnemyAttackConfig(20307, "Thrust", 1f) }
            : new EnemyAttackConfig[0],
        pursuitAttacks = new EnemyAttackConfig[0],
        retreatAttacks = includeRetreat
            ? new[] { new EnemyAttackConfig(20309, "BackSlash", 1f) }
            : new EnemyAttackConfig[0],
        closeAttackPoolWeight = closeWeight,
        retreatAttackPoolWeight = retreatWeight,
        retreatWeightBonusAfterCloseAttack = retreatBonus,
        retreatWeightBonusLimit = retreatBonusLimit,
        resetRetreatBonusAfterRetreat = true,
        chaseRange = 8f
    };
    Dictionary<int, SkillConfig> skills = new Dictionary<int, SkillConfig>
    {
        { 20301, CreateSkill(20301, 4f) },
        { 20307, CreateSkill(20307, 6f) },
        { 20309, CreateSkill(20309, 3f) }
    };
    EnemyAttackCatalog catalog = EnemyAttackCatalog.Create(config, id => skills[id]);
    EnemyDecisionProfile profile = new EnemyDecisionProfile
    {
        attackDesire = 1f,
        attackDecisionCooldown = 1f,
        lowStabilityThreshold = 0.25f
    };
    return new EnemyCombatDecisionController(config, profile, catalog);
}
```

- [x] **Step 7: Run new decision tests and confirm they fail before implementation**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --test-name "Game.Character.Enemy.Tests.EnemyAttackPlanDecisionEditModeTests.TryCreateAttackPlan_RetreatPoolSelectedByRhythmWeight_CreatesRetreatPlan"
```

Expected before Task 4: FAIL because `EnemyAttackPlanType.Retreat`, rhythm selection, and `CompleteCurrentPlan` are not implemented.

### Task 4: Implement Rhythm Pool Selection In Decision Controller

**Files:**
- Modify: `Assets/Game/Character/Enemy/AI/Combat/EnemyCombatDecisionController.cs`

- [x] **Step 1: Add retreat bonus state**

Add this field near the existing decision timers:

```csharp
private float retreatWeightBonus;
```

- [x] **Step 2: Replace in-range plan selection with approach-first rhythm logic**

Inside `TryCreateAttackPlan`, after attack desire passes, replace the existing basic-then-approach section with:

```csharp
if (TryCreateApproachPlan(distanceToTarget, randomValue))
{
    return true;
}

return TryCreateCloseRangeRhythmPlan(distanceToTarget, randomValue);
```

- [x] **Step 3: Add approach-first helper**

Add this method to `EnemyCombatDecisionController` below `CreateAttackPlan`:

```csharp
/// <summary>当前距离超出近距离池覆盖时，优先尝试创建进身攻击计划。</summary>
private bool TryCreateApproachPlan(float distanceToTarget, float randomValue)
{
    if (HasAttackCoveringDistance(attackCatalog.BasicAttacks, distanceToTarget))
    {
        return false;
    }

    EnemyAttackRuntimeConfig approachAttack = SelectAttackCoveringDistance(
        attackCatalog.ApproachAttacks,
        distanceToTarget,
        randomValue);
    if (approachAttack == null)
    {
        return false;
    }

    return CreateAttackPlan(
        EnemyAttackPlanType.Approach,
        EnemyAttackPreparationMode.Direct,
        approachAttack,
        approachAttack.AttackRange);
}
```

- [x] **Step 4: Add close-versus-retreat rhythm helper**

Add this method below `TryCreateApproachPlan`:

```csharp
/// <summary>在近距离节奏中按池权重选择近距离攻击或远离攻击。</summary>
private bool TryCreateCloseRangeRhythmPlan(float distanceToTarget, float randomValue)
{
    bool hasClosePool = HasSelectableAttack(attackCatalog.BasicAttacks);
    bool hasRetreatPool = HasSelectableAttack(attackCatalog.RetreatAttacks);
    if (!hasClosePool && !hasRetreatPool)
    {
        return false;
    }

    if (ShouldSelectRetreatPool(hasClosePool, hasRetreatPool, randomValue))
    {
        EnemyAttackRuntimeConfig retreatAttack = SelectAttackWithCompensation(
            attackCatalog.RetreatAttacks,
            randomValue);
        if (retreatAttack != null)
        {
            return CreateAttackPlan(
                EnemyAttackPlanType.Retreat,
                EnemyAttackPreparationMode.Direct,
                retreatAttack,
                retreatAttack.AttackRange);
        }
    }

    EnemyAttackRuntimeConfig basicAttack = SelectAttackCoveringDistance(
        attackCatalog.BasicAttacks,
        distanceToTarget,
        randomValue);
    if (basicAttack != null)
    {
        return CreateAttackPlan(
            EnemyAttackPlanType.Basic,
            EnemyAttackPreparationMode.Direct,
            basicAttack,
            basicAttack.AttackRange);
    }

    return CreateFallbackBasicApproachPlan(randomValue);
}
```

- [x] **Step 5: Add pool utility helpers**

Add these methods below `TryCreateCloseRangeRhythmPlan`:

```csharp
/// <summary>判断指定攻击池是否至少有一个可被权重选择的技能。</summary>
private static bool HasSelectableAttack(IReadOnlyList<EnemyAttackRuntimeConfig> attacks)
{
    for (int i = 0; i < attacks.Count; i++)
    {
        EnemyAttackRuntimeConfig attack = attacks[i];
        if (attack != null && attack.SkillId > 0 && attack.Weight > 0f)
        {
            return true;
        }
    }

    return false;
}

/// <summary>判断指定攻击池是否存在能覆盖当前距离或关闭距离检测的技能。</summary>
private static bool HasAttackCoveringDistance(
    IReadOnlyList<EnemyAttackRuntimeConfig> attacks,
    float distanceToTarget)
{
    for (int i = 0; i < attacks.Count; i++)
    {
        EnemyAttackRuntimeConfig attack = attacks[i];
        if (attack != null
            && attack.SkillId > 0
            && attack.Weight > 0f
            && (!attack.EnableAttackDistanceCheck || attack.AttackRange >= distanceToTarget))
        {
            return true;
        }
    }

    return false;
}

/// <summary>根据近距离池和远离池的当前有效权重判断是否选择远离池。</summary>
private bool ShouldSelectRetreatPool(bool hasClosePool, bool hasRetreatPool, float randomValue)
{
    if (!hasRetreatPool)
    {
        return false;
    }

    float retreatWeight = combatConfig.retreatAttackPoolWeight + retreatWeightBonus;
    if (retreatWeight <= 0f)
    {
        return false;
    }

    if (!hasClosePool || combatConfig.closeAttackPoolWeight <= 0f)
    {
        return true;
    }

    float totalWeight = combatConfig.closeAttackPoolWeight + retreatWeight;
    return randomValue * totalWeight > combatConfig.closeAttackPoolWeight;
}
```

- [x] **Step 6: Run decision tests**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --test-name "Game.Character.Enemy.Tests.EnemyAttackPlanDecisionEditModeTests.TryCreateAttackPlan_EmptyOptionalPools_CreatesBasicPlan"
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --test-name "Game.Character.Enemy.Tests.EnemyAttackPlanDecisionEditModeTests.TryCreateAttackPlan_ApproachCoversDistanceBeforeCloseRhythm_CreatesApproachPlan"
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --test-name "Game.Character.Enemy.Tests.EnemyAttackPlanDecisionEditModeTests.TryCreateAttackPlan_RetreatPoolSelectedByRhythmWeight_CreatesRetreatPlan"
```

Expected: the first three new tests PASS; completion-bonus tests still fail until Task 5 adds `CompleteCurrentPlan`.

### Task 5: Record Attack Completion Rhythm State

**Files:**
- Modify: `Assets/Game/Character/Enemy/AI/Combat/EnemyCombatDecisionController.cs`
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyAttackFlowNodeAsset.cs`

- [x] **Step 1: Add public completion method**

Add this method to `EnemyCombatDecisionController` near `ResetAttack`:

```csharp
/// <summary>记录当前攻击计划已经完整执行，用于推进攻击池节奏状态。</summary>
public void CompleteCurrentPlan()
{
    EnemyAttackPlan plan = CurrentPlan;
    if (plan == null)
    {
        return;
    }

    RecordCompletedAttackPlan(plan.Type, plan.PreparationMode);
}
```

- [x] **Step 2: Add rhythm recording helper**

Add this private method below `CompleteCurrentPlan`:

```csharp
/// <summary>根据已完成计划类型更新远离池权重加成。</summary>
private void RecordCompletedAttackPlan(
    EnemyAttackPlanType type,
    EnemyAttackPreparationMode preparationMode)
{
    if (type == EnemyAttackPlanType.Basic && preparationMode == EnemyAttackPreparationMode.Direct)
    {
        retreatWeightBonus = Math.Min(
            retreatWeightBonus + combatConfig.retreatWeightBonusAfterCloseAttack,
            combatConfig.retreatWeightBonusLimit);
        return;
    }

    if (type == EnemyAttackPlanType.Retreat && combatConfig.resetRetreatBonusAfterRetreat)
    {
        retreatWeightBonus = 0f;
    }
}
```

- [x] **Step 3: Reset rhythm history when target-selection history resets**

Update `ResetAttackSelectionHistory`:

```csharp
/// <summary>清理当前敌人的攻击动作频率和池节奏记录，进入下一次战斗时重新计算补偿。</summary>
public void ResetAttackSelectionHistory()
{
    attackMissCounts.Clear();
    retreatWeightBonus = 0f;
}
```

- [x] **Step 4: Notify completion before normal attack-layer clear**

In `EnemyAttackFlowNodeAsset.TickEnd`, replace the final clear block:

```csharp
ClearAttackLayerState(controller);
controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Idle);
EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
return BehaviorTreeStatus.Success;
```

with:

```csharp
decision.CompleteCurrentPlan();
ClearAttackLayerState(controller);
controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Idle);
EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
return BehaviorTreeStatus.Success;
```

- [x] **Step 5: Notify completion before follow-up movement clear**

In `EnemyAttackFlowNodeAsset.ConsumeCurrentAttackAndPlayFollowUpMovement`, add completion before clearing:

```csharp
controller.CombatDecision.CompleteCurrentPlan();
ClearAttackLayerState(controller);
activeController = null;
PlayAttackFollowUpMovement(controller);
EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
```

- [x] **Step 6: Run completion-bonus tests**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --test-name "Game.Character.Enemy.Tests.EnemyAttackPlanDecisionEditModeTests.CompleteCurrentPlan_BasicDirectPlan_IncreasesRetreatWeight"
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --test-name "Game.Character.Enemy.Tests.EnemyAttackPlanDecisionEditModeTests.CompleteCurrentPlan_RetreatPlan_ResetsRetreatBonus"
```

Expected: both tests PASS.

- [x] **Step 7: Commit rhythm implementation**

Run:

```powershell
git add -- "Assets/Game/Character/Enemy/AI/Combat/EnemyCombatDecisionController.cs" "Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyAttackFlowNodeAsset.cs" "Assets/Game/Editor/EnemyAttackPlanDecisionEditModeTests.cs"
git commit -m "实现敌人攻击池节奏选择"
```

Expected: commit contains decision logic, attack-flow completion hook, and rhythm decision tests.

### Task 6: Full Verification

**Files:**
- Inspect: all files changed by Tasks 1-5

- [ ] **Step 1: Run targeted EditMode suites**

Result: `EnemyAttackCatalogEditModeTests`, `EnemyAttackPlanDecisionEditModeTests`, and `EnemyCombatDecisionControllerEditModeTests` passed. `EnemyCombatActionFlowEditModeTests.DefenseNode_AnimationComplete_StopsDefense` failed because the current defense node exits by `defenseDuration` countdown while this older test only advances animation progress; this file was not modified by the attack-pool rhythm implementation.

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --test-name "Game.Character.Enemy.Tests.EnemyAttackCatalogEditModeTests"
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --test-name "Game.Character.Enemy.Tests.EnemyAttackPlanDecisionEditModeTests"
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --test-name "Game.Character.Enemy.Tests.EnemyCombatDecisionControllerEditModeTests"
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --test-name "Game.Character.Enemy.Tests.EnemyCombatActionFlowEditModeTests"
```

Expected: all four suites PASS.

- [x] **Step 2: Run Unity compile**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
```

Expected: `success: true`, `errorCount: 0`.

- [x] **Step 3: Inspect final diff for scope**

Run:

```powershell
git diff --stat
git status --short
```

Expected: only planned enemy config, combat decision, behavior-tree action, tests, and plan files are changed; `Assets/Data/EnemySkillConfig.json` remains unstaged unless the user explicitly asks to include it.

- [x] **Step 4: Commit this implementation plan**

Run:

```powershell
git add -- "docs/superpowers/plans/2026-08-07-enemy-attack-pool-rhythm.md"
git commit -m "添加敌人攻击池节奏实现计划"
```

Expected: plan document is committed separately after implementation commits, keeping docs history clear.
