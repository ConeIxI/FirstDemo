# Enemy Behavior Tree Only Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the enemy-domain FSM and intent relay so existing behavior trees directly select and execute all enemy behavior.

**Architecture:** `AIController` updates perception facts then ticks `BehaviorTreeRunner`. The existing `EnemySetIntentNodeAsset` retains its ScriptableObject identity and serialized `intentType` field, but its runtime node becomes a direct action executor with local patrol, search, and death progress. `EnemyBlackboard` retains world facts only; no current state, pending intent, or skill relay remains.

**Tech Stack:** Unity 2022.3.61f1c1, C# 9.0, NUnit EditMode tests, AIBridge CLI.

---

## File Structure

- Modify: `Assets/Game/Character/Enemy/AI/AIController.cs` - perception and behavior-tree orchestration only.
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemySetIntentNodeAsset.cs` - direct behavior-tree action executor while preserving existing asset references.
- Modify: `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs` - retain facts and remove FSM/intent data.
- Modify: `Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs` - stop publishing skill intent.
- Modify: `Assets/Game/Character/Enemy/Config/EnemyDefinition.cs` and `EnemyDefinitionValidator.cs` - remove FSM-only definition data and validation.
- Delete: `Assets/Game/Character/Enemy/AI/EnemyIntent.cs`, `EnemyStateId.cs`, and `AI/States/` - obsolete enemy FSM layer.
- Modify/Delete tests in `Assets/Game/Editor/` - replace FSM and intent assertions with direct action assertions.

### Task 1: Establish Direct-Action Regression Tests

**Files:**
- Modify: `Assets/Game/Editor/EnemyBehaviorTreeNodeEditModeTests.cs`
- Modify: `Assets/Game/Editor/AIControllerEditModeTests.cs`
- Delete: `Assets/Game/Editor/EnemyStateEditModeTests.cs`

- [ ] **Step 1: Replace the intent-writing test with a direct hit-action test**

Replace `SetIntentNode_WritesIntentToEnemyBlackboard` with a test that configures the existing node for `GetHit`, gives the controller a blackboard hit reaction, ticks the runtime node, and asserts the reaction was consumed:

```csharp
[Test]
public void ActionNode_GetHit_ConsumesBlackboardReaction()
{
    GameObject owner = new GameObject("EnemyBehaviorTreeOwner");
    EnemySetIntentNodeAsset node = ScriptableObject.CreateInstance<EnemySetIntentNodeAsset>();
    try
    {
        AIController controller = owner.AddComponent<AIController>();
        EnemyBlackboard blackboard = new EnemyBlackboard();
        controller.SetBlackboardForTests(blackboard);
        blackboard.SetHitReaction("Hit");
        node.SetIntentForTests(EnemyIntentType.GetHit);

        BehaviorTreeContext context = new BehaviorTreeContext(owner, new BehaviorTreeBlackboard());
        BehaviorTreeStatus status = node.CreateRuntimeNode().Tick(context);

        Assert.AreEqual(BehaviorTreeStatus.Success, status);
        Assert.IsFalse(blackboard.HasHitReaction);
    }
    finally
    {
        Object.DestroyImmediate(node);
        Object.DestroyImmediate(owner);
    }
}
```

- [ ] **Step 2: Add a patrol action progression test**

Use two scene `GameObject` route points, an `EnemyAgent` with its serialized `patrolRoute` set through `SerializedObject`, and a test-only `EnemyMovementComponent` destination assertion. Expose no production test-only API; assert that a patrol action node returns `Success` on consecutive ticks and that `movement.HasDestination` becomes true after the first tick.

```csharp
[Test]
public void ActionNode_Patrol_SetsRouteDestination()
{
    // Create enemy, two route points, agent, controller, movement, and action node.
    // Assign patrolRoute with SerializedObject and configure node as Patrol.
    // Tick the runtime node once.
    // Assert Success and movement.HasDestination.
}
```

- [ ] **Step 3: Replace AIController FSM tests with behavior-tree-only assertions**

Delete `RequestState_DeadOverridesAttack` and `TickAI_HitReaction_EntersFsmStateAndConsumesReaction`. Keep the existing perception scan test. Add an assertion that `TickAI` delegates behavior after perception without requiring any `EnemyStateId`:

```csharp
[Test]
public void TickAI_NoBehaviorTree_DoesNotRequireStateMachine()
{
    GameObject owner = new GameObject("Enemy");
    EnemyDefinition definition = ScriptableObject.CreateInstance<EnemyDefinition>();
    try
    {
        EnemyAgent agent = owner.AddComponent<EnemyAgent>();
        AIController controller = owner.AddComponent<AIController>();

        controller.StartAI(agent, definition);
        controller.TickAI(0.016f);

        Assert.IsNotNull(controller.Context);
    }
    finally
    {
        Object.DestroyImmediate(definition);
        Object.DestroyImmediate(owner);
    }
}
```

- [ ] **Step 4: Run the focused tests and verify they fail for the expected missing behavior**

Run:

```powershell
& .\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --test-name Game.Tests.EditMode.EnemyBehaviorTreeNodeEditModeTests.ActionNode_GetHit_ConsumesBlackboardReaction
```

Expected: FAIL because the current action node writes an intent and does not consume the hit reaction.

- [ ] **Step 5: Do not commit yet**

Keep tests uncommitted until the direct action implementation and deletion sweep compile together.

### Task 2: Simplify Blackboard, Combat, Definition, and Controller Boundaries

**Files:**
- Modify: `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs`
- Modify: `Assets/Game/Character/Enemy/Config/EnemyDefinition.cs`
- Modify: `Assets/Game/Character/Enemy/Config/EnemyDefinitionValidator.cs`
- Modify: `Assets/Game/Character/Enemy/AI/AIController.cs`

- [ ] **Step 1: Remove FSM and intent fields from EnemyBlackboard**

Delete `CurrentIntent`, `CurrentState`, `CurrentSkillId`, `SetIntent`, `ClearIntent`, and `SetCurrentState`. Keep target memory, visibility, searching, hit reaction, unbalance, and death APIs. `SetDead(true)` must still clear target, searching, unbalance, and pending hit reaction.

The resulting public fact surface is:

```csharp
public Transform Target { get; private set; }
public Vector3 LastKnownPosition { get; private set; }
public bool HasLastKnownPosition { get; private set; }
public bool IsTargetVisible { get; private set; }
public bool IsSearching { get; private set; }
public bool IsDead { get; private set; }
public bool IsUnbalanced { get; private set; }
public bool HasHitReaction { get; private set; }
```

- [ ] **Step 2: Remove combat-to-intent feedback**

In `EnemyCombatComponent.TryCast`, delete the `blackboard.SetIntent(...)` block. A successful cast only sets `IsActing = true`; behavior-tree actions own subsequent selection.

```csharp
if (config == null || !skillRunner.Cast(skillId, config))
{
    return false;
}

IsActing = true;
return true;
```

- [ ] **Step 3: Remove FSM-only EnemyDefinition data**

Delete serialized `startState` and `enabledStates` fields, their public properties, and editor-only setters. Retain `behaviorTreeAsset` as the sole behavior-selection configuration. Delete `ValidateEnabledStates` and its invocation from `EnemyDefinitionValidator.Validate`.

- [ ] **Step 4: Reduce AIController to context initialization, perception, and tree ticking**

Remove `System`, `AI.States`, and `Framework.Core.FSM` imports; `stateMachine`, `pendingState`, `CurrentStateId`, `PendingStateForTests`, all state request/mapping/priority methods, `InitializeStateMachine`, `GetStateType`, and FSM shutdown.

Implement `TickAI` as:

```csharp
public void TickAI(float deltaTime)
{
    if (context != null && context.Perception != null)
    {
        if (Blackboard.Target == null)
        {
            context.Perception.ScanTarget();
        }
        else
        {
            bool reachedLastKnownPosition = context.Movement != null
                && Blackboard.HasLastKnownPosition
                && context.Movement.HasReached(Blackboard.LastKnownPosition, 1.1f);
            context.Perception.EvaluateTarget(deltaTime, reachedLastKnownPosition);
        }
    }

    if (behaviorTreeRunner != null)
    {
        behaviorTreeRunner.Tick(deltaTime);
    }
}
```

Keep `PatrolRoute`, `HasPatrolRoute`, `Context`, `Definition`, and `SetBlackboardForTests` because behavior-tree condition and action nodes use them.

- [ ] **Step 5: Run Unity compilation and inspect references**

Run:

```powershell
& .\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: compilation fails only at the still-unconverted `EnemySetIntentNodeAsset`, state classes, and tests that reference removed intent/state types.

### Task 3: Convert Existing Intent Leaf Assets into Stateful Direct Actions

**Files:**
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemySetIntentNodeAsset.cs`
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyBehaviorTreeUtility.cs`
- Modify: `Assets/Game/Editor/EnemyBehaviorTreeNodeEditModeTests.cs`

- [ ] **Step 1: Keep the existing ScriptableObject type and serialized action integer**

Do not move or rename `EnemySetIntentNodeAsset.cs`; existing behavior-tree `.asset` files reference its script GUID. Keep the serialized field name `intentType` so stored integer values remain compatible. Replace `EnemyIntentType` with a local public `EnemyBehaviorActionType` enum using the same numeric values:

```csharp
public enum EnemyBehaviorActionType
{
    Idle = 1,
    Patrol = 2,
    Chase = 3,
    Search = 4,
    Attack = 5,
    Skill = 6,
    GetHit = 50,
    Unbalance = 60,
    Dead = 100
}
```

- [ ] **Step 2: Replace ActionNodeAsset's stateless default runtime node with a custom runtime node**

Override `CreateRuntimeNode` and return a private `EnemyActionNode` that stores `patrolIndex`, `searchPoints`, `searchIndex`, `hasStartedSearch`, and `hasHandledDeath`. Its `Tick` must obtain the controller with `EnemyBehaviorTreeUtility.TryGetController` and dispatch by `asset.intentType`; its `Reset` clears all leaf-local progress.

```csharp
public override BehaviorTreeNode CreateRuntimeNode()
{
    return new EnemyActionNode(this);
}

private sealed class EnemyActionNode : BehaviorTreeNode
{
    public override void Reset()
    {
        patrolIndex = 0;
        searchPoints = new Vector3[0];
        searchIndex = 0;
        hasStartedSearch = false;
        hasHandledDeath = false;
    }
}
```

- [ ] **Step 3: Implement direct idle, patrol, chase, attack, and skill actions**

Use the controller context directly. All nonterminal actions return `Success` so the root selector reevaluates priorities on the next frame.

```csharp
private BehaviorTreeStatus TickChase(AIController controller)
{
    if (controller.Blackboard.Target != null && controller.Blackboard.IsTargetVisible)
    {
        controller.Context?.Animation?.TryPlay(controller.Definition?.AnimationConfig?.moveAnimation);
        controller.Context?.Movement?.MoveTo(controller.Blackboard.Target);
    }

    return BehaviorTreeStatus.Success;
}

private BehaviorTreeStatus TickAttack(AIController controller, int skillId)
{
    if (controller.Context?.Combat != null && !controller.Context.Combat.IsActing)
    {
        controller.Context.Movement?.Stop();
        controller.Context.Combat.TryStartAttack(skillId);
    }

    return BehaviorTreeStatus.Success;
}
```

`Patrol` uses `controller.PatrolRoute`; if the route is empty, stop movement and play idle. When the current point is reached, advance `patrolIndex = (patrolIndex + 1) % route.Length` before requesting the next destination. `Skill` uses the node's serialized `skillId`, not blackboard state.

- [ ] **Step 4: Implement search, hit, unbalance, and death actions**

`Search` generates points once while its branch remains selected, moves to the last known position then each search point, and resets when the target becomes visible or no target memory remains. `GetHit` stops movement, calls `ConsumeHitReaction`, and plays either the consumed or configured animation. `Unbalance` stops movement, plays unbalance animation, then calls `SetUnbalanced(false)`. `Dead` executes its stop/end-action/animation block once and returns `Running` thereafter.

```csharp
private BehaviorTreeStatus TickDead(AIController controller)
{
    if (!hasHandledDeath)
    {
        controller.Blackboard.SetDead(true);
        controller.Context?.Movement?.Stop();
        controller.Context?.Combat?.EndAction();
        controller.Context?.Combat?.DisableWeaponHit();
        controller.Context?.Animation?.TryPlay(controller.Definition?.AnimationConfig?.deadAnimation);
        hasHandledDeath = true;
    }

    return BehaviorTreeStatus.Running;
}
```

- [ ] **Step 5: Update node test configuration helpers**

Retain `SetIntentForTests` temporarily to avoid rewriting serialized test setup, but change its parameter type to `EnemyBehaviorActionType`. Add `SetSkillIdForTests` coverage that asserts the configured skill action calls the direct combat path through a real `EnemyCombatComponent` when a valid test skill setup exists.

- [ ] **Step 6: Run focused direct-action tests**

Run:

```powershell
& .\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name EnemyBehaviorTreeNodeEditModeTests
```

Expected: PASS for direct hit consumption and patrol destination tests; no test references `EnemyIntent` or `EnemyStateId`.

### Task 4: Delete Enemy FSM Artifacts and Complete Test Migration

**Files:**
- Delete: `Assets/Game/Character/Enemy/AI/EnemyIntent.cs`
- Delete: `Assets/Game/Character/Enemy/AI/EnemyStateId.cs`
- Delete: `Assets/Game/Character/Enemy/AI/States/AttackState.cs`
- Delete: `Assets/Game/Character/Enemy/AI/States/ChaseState.cs`
- Delete: `Assets/Game/Character/Enemy/AI/States/DeadState.cs`
- Delete: `Assets/Game/Character/Enemy/AI/States/EnemyCombatActionState.cs`
- Delete: `Assets/Game/Character/Enemy/AI/States/EnemyStateBase.cs`
- Delete: `Assets/Game/Character/Enemy/AI/States/GetHitState.cs`
- Delete: `Assets/Game/Character/Enemy/AI/States/IdleState.cs`
- Delete: `Assets/Game/Character/Enemy/AI/States/PatrolState.cs`
- Delete: `Assets/Game/Character/Enemy/AI/States/SearchState.cs`
- Delete: `Assets/Game/Character/Enemy/AI/States/SkillState.cs`
- Delete: `Assets/Game/Character/Enemy/AI/States/UnbalanceState.cs`
- Delete matching `.meta` files and `Assets/Game/Editor/EnemyStateEditModeTests.cs`.
- Modify: `Assets/Game/Editor/EnemyBlackboardEditModeTests.cs`
- Modify: `Assets/Game/Editor/EnemyDefinitionValidatorEditModeTests.cs`

- [ ] **Step 1: Replace blackboard intent tests with fact tests**

Delete assertions of `SetIntent`, `CurrentIntent`, and skill IDs. Keep tests for target memory, hit reaction consumption, unbalance, and death cleanup. Add:

```csharp
[Test]
public void SetDead_ClearsTargetAndTransientReactions()
{
    EnemyBlackboard blackboard = new EnemyBlackboard();
    GameObject target = new GameObject("Target");
    try
    {
        blackboard.RememberTarget(target.transform);
        blackboard.SetHitReaction("Hit");
        blackboard.SetUnbalanced(true);
        blackboard.SetDead(true);

        Assert.IsTrue(blackboard.IsDead);
        Assert.IsNull(blackboard.Target);
        Assert.IsFalse(blackboard.HasHitReaction);
        Assert.IsFalse(blackboard.IsUnbalanced);
    }
    finally
    {
        Object.DestroyImmediate(target);
    }
}
```

- [ ] **Step 2: Update definition validator tests**

Remove all `SetStartState`, `SetEnabledStates`, and enabled-state validation cases. Keep behavior tree required, combat skill ID, animation names, and attribute configuration validation cases.

- [ ] **Step 3: Delete FSM files and their metadata with version-control-aware deletes**

Use `git rm` for files tracked by Git. Do not delete `Assets/Framework/Core/FSM` or any player FSM file. Verify no remaining enemy source imports `GameMain2.Framework.Core.FSM`.

Run:

```powershell
@'
{"command":"rg","queries":["EnemyIntent","EnemyStateId","EnemyStateBase","FsmBase<AIController>","Framework.Core.FSM"],"globs":["*.cs"],"paths":["Assets/Game/Character/Enemy","Assets/Game/Editor"]}
'@ | & .\.aibridge\cli\AIBridgeCLI.exe exec run --stdin
```

Expected: no matches in enemy AI or enemy tests; player and framework FSM references remain outside the searched scope.

- [ ] **Step 4: Run all enemy EditMode tests**

Run:

```powershell
& .\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name "Enemy.*EditModeTests"
```

Expected: PASS with no missing-script or obsolete type errors.

### Task 5: Unity and Scene1 Runtime Validation

**Files:**
- Verify only: `Assets/Scenes/Scene1.unity`

- [ ] **Step 1: Compile Unity and inspect Error logs**

Run:

```powershell
& .\.aibridge\cli\AIBridgeCLI.exe compile unity
& .\.aibridge\cli\AIBridgeCLI.exe get_logs --logType Error
```

Expected: compilation succeeds with `errorCount: 0`; Error logs are empty.

- [ ] **Step 2: Start Scene1 Play Mode and collect Boss transform evidence**

Use the already opened `Scene1`; do not save the scene. Run:

```powershell
& .\.aibridge\cli\AIBridgeCLI.exe editor play
& .\.aibridge\cli\AIBridgeCLI.exe runtime list_targets --probe true
& .\.aibridge\cli\AIBridgeCLI.exe inspector get_properties --path Boss --componentName Transform --includeChildren true
& .\.aibridge\cli\AIBridgeCLI.exe runtime perf --target latest --duration 2s --interval 250ms
& .\.aibridge\cli\AIBridgeCLI.exe inspector get_properties --path Boss --componentName Transform --includeChildren true
& .\.aibridge\cli\AIBridgeCLI.exe editor stop
```

Expected: the two Boss position samples differ while Play Mode is active, demonstrating that the preserved patrol branch still drives movement without enemy FSM code.

- [ ] **Step 3: Review the final diff and commit only owned refactor files**

Run `git diff --check` and `git status --short`. Stage only files created or changed by this refactor; do not stage unrelated existing scene, prefab, or component changes. Commit using:

```powershell
git add Assets/Game/Character/Enemy Assets/Game/Editor
git commit -m "refactor: run enemy ai with behavior tree only"
```

Do not commit `Scene1.unity` unless the refactor itself changed it.

## Plan Review

- Spec coverage: Tasks 2-4 remove every enemy FSM and intent boundary; Task 3 makes behavior-tree leaves the direct executor; Task 5 verifies Scene1 patrol.
- Serialization safety: Task 3 preserves `EnemySetIntentNodeAsset` script identity and `intentType` field so current behavior-tree assets retain references and values.
- Scope safety: Task 4 scopes all deletions to enemy AI and tests; framework/player FSM remain untouched.
- Placeholder scan: no deferred implementation or unspecified validation steps remain.
