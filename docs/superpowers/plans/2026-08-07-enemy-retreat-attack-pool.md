# Enemy Retreat Attack Pool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a reusable enemy `retreatAttacks` attack pool and serialize an empty pool on GreatSwordBoss.

**Architecture:** Extend the existing enemy combat config beside `basicAttacks`, `approachAttacks`, and `pursuitAttacks`. Bind the new pool into `EnemyAttackCatalog` so future decision logic can consume runtime attack configs without changing the behavior tree now.

**Tech Stack:** Unity 2022.3.61f1c1, C# 9.0, Unity YAML ScriptableObject assets.

---

## File Structure

- Modify: `Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs` adds the serialized `retreatAttacks` field.
- Modify: `Assets/Game/Character/Enemy/AI/Combat/EnemyAttackCatalog.cs` exposes and binds the runtime retreat attack pool.
- Modify: `Assets/Game/Character/Enemy/Config/Definitions/GreatSwordBossDefinition.asset` writes `retreatAttacks: []` into the Boss combat config.

### Task 1: Add Generic Retreat Attack Pool

**Files:**
- Modify: `Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs`
- Modify: `Assets/Game/Character/Enemy/AI/Combat/EnemyAttackCatalog.cs`

- [x] **Step 1: Add serialized config field**

In `EnemyCombatConfig`, place this field after `pursuitAttacks`:

```csharp
public EnemyAttackConfig[] retreatAttacks = new EnemyAttackConfig[0];
```

- [x] **Step 2: Add runtime catalog property and constructor parameter**

In `EnemyAttackCatalog`, add the property beside other pools:

```csharp
public IReadOnlyList<EnemyAttackRuntimeConfig> RetreatAttacks { get; }
```

Update the private constructor signature and assignment:

```csharp
private EnemyAttackCatalog(
    EnemyAttackRuntimeConfig[] basicAttacks,
    EnemyAttackRuntimeConfig[] approachAttacks,
    EnemyAttackRuntimeConfig[] pursuitAttacks,
    EnemyAttackRuntimeConfig[] retreatAttacks,
    EnemyAttackRuntimeConfig counterAttack)
{
    BasicAttacks = basicAttacks;
    ApproachAttacks = approachAttacks;
    PursuitAttacks = pursuitAttacks;
    RetreatAttacks = retreatAttacks;
    CounterAttack = counterAttack;
    basicAttacksBySkillId = BuildBasicAttackMap(basicAttacks);
    BasicAttackRange = CalculateBasicAttackRange(basicAttacks);
}
```

- [x] **Step 3: Bind the new pool in Create**

Add this line after `pursuitAttacks` is bound:

```csharp
EnemyAttackRuntimeConfig[] retreatAttacks = BindAttackPool(config.retreatAttacks, skillResolver);
```

Return the catalog with the new constructor argument:

```csharp
return new EnemyAttackCatalog(basicAttacks, approachAttacks, pursuitAttacks, retreatAttacks, counterAttack);
```

### Task 2: Serialize GreatSwordBoss Empty Pool

**Files:**
- Modify: `Assets/Game/Character/Enemy/Config/Definitions/GreatSwordBossDefinition.asset`

- [x] **Step 1: Add empty retreat pool to Boss config**

In `combatConfig`, place this YAML field after `pursuitAttacks: []`:

```yaml
    retreatAttacks: []
```

### Task 3: Verify

**Files:**
- Inspect: modified files above

- [x] **Step 1: Run Unity compile**

Run:

```powershell
$CLI compile unity
```

Expected: Unity compile finishes successfully with no C# compile errors.

- [x] **Step 2: Inspect diff**

Run:

```powershell
git diff -- Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs Assets/Game/Character/Enemy/AI/Combat/EnemyAttackCatalog.cs Assets/Game/Character/Enemy/Config/Definitions/GreatSwordBossDefinition.asset
```

Expected: only `retreatAttacks` config/catalog/asset additions are present.
