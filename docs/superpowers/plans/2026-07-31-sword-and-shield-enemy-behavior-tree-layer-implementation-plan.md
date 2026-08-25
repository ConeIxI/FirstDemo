# 剑盾敌人行为树分层 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将剑盾敌人的根行为树迁移为响应式四层结构，统一处理中断、目标记忆、正常返程、警戒搜索和战斗距离决策，并通过 EditMode、资源结构和 Unity 编译验证。

**Architecture:** 根节点使用 `ReactivePrioritySelectorNodeAsset`，优先级为 `InterruptExecutor > CombatLayer > AlertLayer > NormalLayer`。黑板保存唯一的 `CombatTarget`、警戒记忆、返程请求和中断事实；正常、警戒和中断层各由一个运行时 Routine/Executor 管理阶段，战斗层只包装现有攻击、防御、后撤和战斗待机配置，不在本计划中重写战斗动作内部实现。

**Tech Stack:** Unity 2022.3.61f1c1、C# 9.0、ScriptableObject 行为树、Unity NavMesh、Unity Test Runner EditMode、AIBridge CLI。

## Global Constraints

- Unity 编译只能使用 `$CLI compile unity`；`compile dotnet` 只能作为额外检查。
- C# 代码必须兼容 C# 9.0；新增和修改的函数必须添加简体中文 XML 或行注释。
- 保留现有 `SelectorNodeAsset` 和 `SequenceNodeAsset` 的记忆型语义；响应式节点只新增，不替换旧节点。
- 不引入 GOAP、FSM、第三方依赖、事件队列或多目标仇恨系统。
- 中断新请求只能由 `EnemyLifeComponent` 产生；运行时子树只能消费、清理和更新播放生命周期。
- `CombatTarget` 有效期间读取实时 Transform；警戒层只能读取 `AlertLastKnownPosition`。
- 攻击范围、战斗范围、追击范围、视野范围必须满足 `AttackRange < CombatRange < ChaseRange < VisionRange`。
- 普通受击不能打断后撤且不缓存延迟受击；失衡和死亡可以抢占后撤。
- 死亡保持永久终态；失衡和受击结束后由根节点重新选层，不恢复旧节点索引。
- 工作树中现有用户修改的 `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySetIntentNodeAsset.cs` 不得被回滚或带入无关提交。
- 每个任务完成独立测试和中文 Git 提交；暂存时只加入该任务文件。

## 文件与责任映射

### 行为树框架

- Create: `Assets/Framework/Core/BehaviorTree/Nodes/ReactivePrioritySelectorNodeAsset.cs`：每帧从最高优先级重新评估，并在子树变化时 Reset 旧运行时节点。
- Create: `Assets/Framework/Core/BehaviorTree/Nodes/ReactiveSequenceNodeAsset.cs`：每帧重新检查 Guard，Guard 失败时重置层内 Routine。
- Create: `Assets/Framework/Core/BehaviorTree/Nodes/RepeatForeverNodeAsset.cs`：子节点 Success 后重置并继续 Running，Failure 向上传播。
- Test: `Assets/Game/Editor/BehaviorTreeReactiveNodeEditModeTests.cs`。

### 共享事实、配置与控制器

- Modify: `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs`：唯一保存目标记忆、中断事实、范围事实和 `NeedsReturnHome`。
- Modify: `Assets/Game/Character/Enemy/AI/AIController.cs`：记录启动原点、按顺序推进记忆、刷新距离事实并 Tick 根树。
- Modify: `Assets/Game/Character/Enemy/Core/EnemyAgent.cs`：暴露启动原点和巡逻路线校验所需数据。
- Modify: `Assets/Game/Character/Enemy/Components/EnemyPerceptionComponent.cs`：只负责感知候选和视野检测，不再拥有第二套警戒计时。
- Modify: `Assets/Game/Character/Enemy/Components/EnemyLifeComponent.cs`：分离攻击者记忆更新与受击表现写入，按后撤/失衡规则过滤表现请求。
- Modify: `Assets/Game/Character/Enemy/Components/EnemyMemoryComponent.cs`：改为黑板记忆的兼容只读入口，不维护独立倒计时。
- Modify: `Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs`：新增 `chaseRange`、`combatMemoryDuration`。
- Modify: `Assets/Game/Character/Enemy/Config/EnemyPerceptionConfig.cs`：将 `targetMemoryTime`、`searchWaitTime` 迁移为 `alertMemoryDuration`、`searchObservationDuration`。
- Modify: `Assets/Game/Character/Enemy/Config/EnemyMovementConfig.cs`：新增 `patrolWaitDuration`。
- Modify: `Assets/Game/Character/Enemy/Config/EnemyDefinitionValidator.cs`：校验四种范围、记忆时长、巡逻停留和搜索约束。
- Modify: `Assets/Game/Editor/EnemyDefinitionEditor.cs`：同步新字段中文 Inspector 标签。
- Test: `Assets/Game/Editor/EnemyBlackboardDecisionFactsEditModeTests.cs`、`Assets/Game/Editor/EnemyTargetMemoryEditModeTests.cs`、`Assets/Game/Editor/EnemyDefinitionValidatorEditModeTests.cs`。

### 新增行为节点

- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyInterruptExecutorNodeAsset.cs`：统一调度三个公共中断 Sequence。
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyNormalRoutineNodeAsset.cs`：管理原点、返程、待机和巡逻阶段。
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyAlertRoutineNodeAsset.cs`：管理拔刀、最后已知位置、搜索点、收刀和退出握手。
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyEnsureCombatStanceNodeAsset.cs`：保证直接进入战斗时只拔刀一次。
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyCombatMoveNodeAsset.cs`：按 Chase/Approach 模式移动到实时 CombatTarget。
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyCombatTargetHoldNodeAsset.cs`：范围内决策无结果时停止并保持战斗待机。
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyHasCombatTargetNodeAsset.cs`。
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyShouldRunAlertLayerNodeAsset.cs`。
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyIsBeyondChaseRangeNodeAsset.cs`。
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyIsInChaseRangeNodeAsset.cs`。
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyIsOutsideAttackRangeNodeAsset.cs`。
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySetIntentNodeAsset.cs`：只调整受击/失衡/死亡局部 Reset 和受击不重启语义，保留用户现有无关修改。
- Test: `Assets/Game/Editor/EnemyInterruptExecutorEditModeTests.cs`、`Assets/Game/Editor/EnemyNormalRoutineEditModeTests.cs`、`Assets/Game/Editor/EnemyAlertRoutineEditModeTests.cs`、`Assets/Game/Editor/EnemyCombatLayerEditModeTests.cs`。

### Unity 行为树资源

- Modify: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEnemy/SwordAndShieldEnemyBehaviorTree.asset`：根节点替换为响应式优先级结构。
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEnemy/Interrupt/InterruptExecutor.asset`。
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEnemy/Normal/NormalLayer.asset`、`NormalRoutine.asset`。
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEnemy/Alert/AlertLayer.asset`、`AlertRoutine.asset`。
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEnemy/Combat/CombatLayer.asset`、距离条件、追击/接近 Sequence、范围内决策 Selector 和兜底资产。
- Delete after reference scan: `AlertChaseSequence.asset`、`SetIntentAlertChase.asset`、`LostTargetSearchSequence.asset`、`SearchSequence.asset`、`ShouldSearchLastKnownPosition.asset` 等剑盾敌人旧专用入口；`Common` 目录资产只有在全项目无引用时保留或删除。
- Test: `Assets/Game/Editor/SwordAndShieldBehaviorTreeAssetEditModeTests.cs`。

## Task 1: 建立响应式行为树组合节点

**Files:**
- Create: `Assets/Framework/Core/BehaviorTree/Nodes/ReactivePrioritySelectorNodeAsset.cs`
- Create: `Assets/Framework/Core/BehaviorTree/Nodes/ReactiveSequenceNodeAsset.cs`
- Create: `Assets/Framework/Core/BehaviorTree/Nodes/RepeatForeverNodeAsset.cs`
- Test: `Assets/Game/Editor/BehaviorTreeReactiveNodeEditModeTests.cs`

**Interfaces:**

```csharp
public sealed class ReactivePrioritySelectorNodeAsset : CompositeNodeAsset
{
    public override BehaviorTreeNode CreateRuntimeNode();
}

public sealed class ReactiveSequenceNodeAsset : CompositeNodeAsset
{
    public override BehaviorTreeNode CreateRuntimeNode();
}

public sealed class RepeatForeverNodeAsset : DecoratorNodeAsset
{
    public override BehaviorTreeNode CreateRuntimeNode();
}
```

- [ ] **Step 1: 编写失败测试**：覆盖低优先级 Running 时高优先级下一 Tick 抢占、旧节点只 Reset 一次、ReactiveSequence Guard 失败重置后续、RepeatForever Success 后继续 Running、RepeatForever Failure 向上传播。
- [ ] **Step 2: 运行 EditMode 测试确认失败**：在 Unity Test Runner 中执行 `BehaviorTreeReactiveNodeEditModeTests`；预期新增类型不存在或测试失败。
- [ ] **Step 3: 实现最小运行时节点**：每个节点通过 `CreateRuntimeChildren/CreateRuntimeChild` 创建独立运行时实例；ReactivePrioritySelector 保存 `runningChildIndex`，每帧从 0 开始 Tick，发现新 Running 子节点时先 Reset 旧节点；ReactiveSequence 每帧从 0 开始并在 Guard Failure 时 Reset 后续节点；RepeatForever 在 Success 后 Reset 子节点并返回 Running。
- [ ] **Step 4: 运行测试确认通过**：Unity Test Runner 中该测试组全部通过；预期无旧 Selector/Sequence 回归。
- [ ] **Step 5: 提交**：`git add Assets/Framework/Core/BehaviorTree/Nodes Assets/Game/Editor/BehaviorTreeReactiveNodeEditModeTests.cs && git commit -m "新增响应式行为树组合节点"`。

## Task 2: 收拢目标记忆、范围事实和配置迁移

**Files:**
- Modify: `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs`
- Modify: `Assets/Game/Character/Enemy/AI/AIController.cs`
- Modify: `Assets/Game/Character/Enemy/Core/EnemyAgent.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyPerceptionComponent.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyMemoryComponent.cs`
- Modify: `Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs`
- Modify: `Assets/Game/Character/Enemy/Config/EnemyPerceptionConfig.cs`
- Modify: `Assets/Game/Character/Enemy/Config/EnemyMovementConfig.cs`
- Modify: `Assets/Game/Character/Enemy/Config/EnemyDefinitionValidator.cs`
- Modify: `Assets/Game/Editor/EnemyDefinitionEditor.cs`
- Test: `Assets/Game/Editor/EnemyBlackboardDecisionFactsEditModeTests.cs`
- Create: `Assets/Game/Editor/EnemyTargetMemoryEditModeTests.cs`
- Create: `Assets/Game/Editor/EnemyDefinitionValidatorEditModeTests.cs`

**Interfaces:**

```csharp
public sealed class EnemyBlackboard
{
    public Transform CombatTarget { get; }
    public bool HasCombatTarget { get; }
    public Vector3 AlertLastKnownPosition { get; }
    public bool HasAlertMemory { get; }
    public float CombatMemoryRemaining { get; }
    public float AlertMemoryRemaining { get; }
    public bool IsAlertExitPending { get; }
    public bool NeedsReturnHome { get; }

    public void ObserveTarget(Transform target, bool isInCombatRange, float combatDuration, float alertDuration);
    public void RecordPlayerAttack(Transform attacker, bool wasAlertActive, bool isInCombatRange, float combatDuration, float alertDuration);
    public void TickMemories(float deltaTime);
    public void ClearCombatTarget(Vector3 lastKnownPosition, float alertDuration);
    public void RequestAlertExit();
    public void CompleteAlertExit();
    public void SetNeedsReturnHome(bool value);
    public void ClearHitReactionState();
}
```

- [ ] **Step 1: 编写失败测试**：验证战斗目标只在战斗范围确认时建立；战斗记忆和警戒记忆独立倒计时；战斗记忆到期用最后实时位置建立警戒记忆；警戒退出握手保留到收刀完成；攻击事件先读取 `wasAlertActive`；死亡清理全部事实；范围和时长配置错误被 Validator 拒绝。
- [ ] **Step 2: 运行测试确认失败**：Unity Test Runner 执行 `EnemyTargetMemoryEditModeTests` 和 `EnemyDefinitionValidatorEditModeTests`；预期新属性和方法不存在。
- [ ] **Step 3: 迁移配置字段**：在 `EnemyCombatConfig` 增加 `chaseRange = 6f`、`combatMemoryDuration = 4f`；在 `EnemyPerceptionConfig` 使用 `[FormerlySerializedAs("targetMemoryTime")] alertMemoryDuration = 4f` 和 `[FormerlySerializedAs("searchWaitTime")] searchObservationDuration = 1f`，保留 `searchRadius/searchPointCount`；在 `EnemyMovementConfig` 增加 `patrolWaitDuration = 2f`。更新 `ApplyConfig` 和编辑器标签，确保旧 YAML 值可迁移。
- [ ] **Step 4: 实现黑板单一事实**：把 `Target` 迁移为 `CombatTarget`，把 `LastKnownPosition` 迁移为 `AlertLastKnownPosition`；`TickMemories` 只推进两种倒计时；`ClearCombatTarget` 不清除新建的警戒事实；`SetDead(true)` 继续清空目标、战斗、警戒和中断事实；保留必要只读别名时只能转发到新属性，不创建第二份存储。
- [ ] **Step 5: 拆分感知与记忆**：`EnemyPerceptionComponent` 只提供 `ScanVisibleTarget/CanSee/CanSenseNearby/GenerateSearchPoints`；`AIController.TickAI` 按“感知 -> 事件记忆 -> `TickMemories` -> 距离事实 -> 行为树”顺序执行；`EnemyMemoryComponent` 删除独立记忆计时，只作为统一黑板的兼容入口。
- [ ] **Step 6: 更新范围事实和校验**：将 `IsInAttackRange/IsInCombatRange` 绑定 `CombatTarget`；Validator 校验 `0 < defaultAttackRange < combatEnterRange < chaseRange < perception.range`、记忆时长和巡逻/搜索字段；巡逻路线空数组合法，空元素记录 Error。
- [ ] **Step 7: 运行测试确认通过**：执行上述 EditMode 测试以及现有 `EnemyBlackboardDecisionFactsEditModeTests`；预期全部通过，且现有目标距离事实测试保持有效。
- [ ] **Step 8: 提交**：`git add` 仅加入本任务列出的配置、黑板、控制器、感知和测试文件，提交 `统一敌人目标记忆和范围事实`。

## Task 3: 实现统一中断执行器

**Files:**
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyInterruptExecutorNodeAsset.cs`
- Modify: `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyLifeComponent.cs`
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySetIntentNodeAsset.cs`
- Test: `Assets/Game/Editor/EnemyInterruptExecutorEditModeTests.cs`

**Interfaces:**

```csharp
public sealed class EnemyInterruptExecutorNodeAsset : BehaviorTreeNodeAsset
{
    [SerializeField] private BehaviorTreeNodeAsset deadSequence;
    [SerializeField] private BehaviorTreeNodeAsset unbalanceSequence;
    [SerializeField] private BehaviorTreeNodeAsset getHitSequence;

    public override BehaviorTreeNode CreateRuntimeNode();
}
```

运行时节点必须实现：

```text
Tick(context) -> 按 IsDead / IsUnbalanced / IsHitReactionInProgress|HasHitReaction 选择子树
Reset() -> Reset 三个子树并清除局部 CurrentType，不清除权威黑板事实
```

- [ ] **Step 1: 编写失败测试**：覆盖死亡 > 失衡 > 受击优先级；受击期间新受击不重启当前动画；失衡清空受击；死亡清空全部；后撤期间普通受击不写入；子树 Failure 只退出一次；无中断返回 Failure。
- [ ] **Step 2: 运行测试确认失败**：Unity Test Runner 执行 `EnemyInterruptExecutorEditModeTests`；预期节点资产不存在或行为与旧逻辑不符。
- [ ] **Step 3: 实现统一节点**：为三个 Sequence 创建独立运行时实例；切换类型时先清理事实，再 Reset 旧子树；死亡子树完成表现后永久返回 Running；受击子树 Success 后若有待处理请求立即重启，Failure 时清空本次及待处理请求并退出；失衡 Success/Failure 都清除失衡状态并退出。
- [ ] **Step 4: 修改生命事件入口**：在 `EnemyLifeComponent` 中先处理攻击者记忆，再按当前 `IsDead/IsUnbalanced/CurrentIntent == Retreat` 过滤受击表现；后撤和失衡期间只禁止 `SetHitReaction`，不禁止目标记忆刷新；`HandleDeath` 保持现有碰撞、导航、武器和掉落行为。
- [ ] **Step 5: 修正通用动作**：在 `EnemySetIntentNodeAsset` 的 `TickGetHit` 中移除“`HasHitReaction` 立即重启当前动画”的分支；`Reset` 只针对 GetHit/Unbalance/Dead 局部状态清理，不破坏巡逻和搜索运行进度；每个新增或修改函数补充中文注释。
- [ ] **Step 6: 运行测试确认通过**：执行中断测试和现有生命/黑板测试；预期连续受击、失衡抢占、死亡终态全部通过。
- [ ] **Step 7: 提交**：只提交中断节点、黑板清理入口、生命组件、动作节点和测试文件，提交 `实现剑盾敌人统一中断执行器`；不得提交用户已有的其它改动。

## Task 4: 实现 NormalLayer

**Files:**
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyNormalRoutineNodeAsset.cs`
- Modify: `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs`
- Modify: `Assets/Game/Character/Enemy/AI/AIController.cs`
- Modify: `Assets/Game/Character/Enemy/Core/EnemyAgent.cs`
- Test: `Assets/Game/Editor/EnemyNormalRoutineEditModeTests.cs`

**Interfaces:**

```csharp
public sealed class EnemyNormalRoutineNodeAsset : ActionNodeAsset
{
    public override BehaviorTreeNode CreateRuntimeNode();
}
```

运行时阶段固定为：`Uninitialized/ReturningOrigin/WaitingAtOrigin/MovingToWaypoint/WaitingAtWaypoint/IdleAtOrigin`。

- [ ] **Step 1: 编写失败测试**：验证 0 个巡逻点使用启动位置待机、1 个点到达后待机、2 个以上按数组循环、`NeedsReturnHome` 重置到第一个点、临时受击不触发返程、Guard 失效时 Routine Reset、有效层始终 Running。
- [ ] **Step 2: 运行测试确认失败**：Unity Test Runner 执行 `EnemyNormalRoutineEditModeTests`；预期新 Routine 不存在。
- [ ] **Step 3: 记录启动原点**：在 `AIController.StartAI` 首次绑定 Agent 时记录 `StartupHomePosition/StartupHomeRotation`；按巡逻点数量计算 `NormalOriginPosition/Rotation`；禁止用当前距离推导 `NeedsReturnHome`。
- [ ] **Step 4: 实现 Routine**：0 点进入 `IdleAtOrigin`；1 点把第一个点作为原点并到达后待机；多点先回第一个点、等待后从索引 1 开始循环；返程完成清除 `NeedsReturnHome`，普通巡逻不写入该事实；Movement 到达判定统一使用 `StoppingDistance`。
- [ ] **Step 5: 建立 NormalLayer 资产**：使用 `ReactiveSequence(HasNoCombatTarget, HasNoAlertMemory, NormalRoutine)`，不创建独立 Idle/Patrol Selector；Routine 缺少 Movement 或路点含空引用时记录 Error 并 Failure。
- [ ] **Step 6: 运行测试确认通过**：执行 NormalLayer 测试和现有移动/行为树测试；预期路线和返程语义通过。
- [ ] **Step 7: 提交**：提交 `实现剑盾敌人正常状态层`。

## Task 5: 实现 AlertLayer

**Files:**
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyAlertRoutineNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyShouldRunAlertLayerNodeAsset.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyPerceptionComponent.cs`
- Modify: `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs`
- Modify: `Assets/Game/Character/Enemy/AI/AIController.cs`
- Test: `Assets/Game/Editor/EnemyAlertRoutineEditModeTests.cs`
- Replace: `Assets/Game/Editor/EnemyAlertChaseEditModeTests.cs` 中依赖旧剑盾资产的测试，保留仍适用于通用动作节点的测试。

**Interfaces:**

```csharp
public sealed class EnemyShouldRunAlertLayerNodeAsset : ConditionNodeAsset
{
    protected override bool Evaluate(BehaviorTreeContext context);
}

public sealed class EnemyAlertRoutineNodeAsset : ActionNodeAsset
{
    public override BehaviorTreeNode CreateRuntimeNode();
}
```

阶段固定为：`Uninitialized/EnteringCombatStance/MovingToLastKnownPosition/InspectingLastKnownPosition/MovingToSearchPoint/InspectingSearchPoint/ExitingCombatStance`。

- [ ] **Step 1: 编写失败测试**：覆盖 `HasAlertMemory || IsAlertExitPending` Guard、范围外攻击只警戒、警戒攻击无视战斗范围建立 CombatTarget、拔刀跳过条件、最后位置刷新废弃旧搜索点、NavMesh 失败点舍弃、搜索完成和记忆到期并列退出、收刀后设置返程请求。
- [ ] **Step 2: 运行测试确认失败**：Unity Test Runner 执行 `EnemyAlertRoutineEditModeTests`；预期新 Routine/Guard 不存在。
- [ ] **Step 3: 重构感知入口**：保留 `CanSee`、`CanSenseNearby` 和 NavMesh 候选生成；删除 `targetMemoryTime/searchWaitTime` 的独立倒计时与 `IsSearching` 第二状态机；由 AIController/Blackboard 统一推进记忆。
- [ ] **Step 4: 实现 AlertRoutine**：进入时按 `HasCombatStance` 决定是否拔刀；只移动到 `AlertLastKnownPosition`；到达后观察并一次性生成有限搜索点；只使用有效 NavMesh 点；所有点完成或记忆到期进入收刀；收刀完成调用 `CompleteAlertExit` 并设置 `NeedsReturnHome`。
- [ ] **Step 5: 处理新情报抢占**：最后已知位置变化时清空旧搜索数组并重启调查；战斗目标建立时由根节点抢占；退出收刀期间新情报取消退出握手，但不重复拔刀。
- [ ] **Step 6: 建立 AlertLayer 资产**：使用 `ReactiveSequence(HasNoCombatTarget, ShouldRunAlertLayer, AlertRoutine)`，不创建独立 AlertChase/Search Selector。
- [ ] **Step 7: 运行测试确认通过**：执行 AlertLayer 测试、目标记忆测试和现有 EnemyAlertChase 中仍保留的通用动作测试；预期警戒入口不再依赖旧剑盾平铺资源。
- [ ] **Step 8: 提交**：提交 `实现剑盾敌人警戒状态层`。

## Task 6: 实现 CombatLayer 距离分区

**Files:**
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyEnsureCombatStanceNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyCombatMoveNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyCombatTargetHoldNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyHasCombatTargetNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyIsBeyondChaseRangeNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyIsInChaseRangeNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyIsOutsideAttackRangeNodeAsset.cs`
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySetIntentNodeAsset.cs`：仅接入新 CombatTarget 字段，保留现有攻击、防御、后撤和待机实现。
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyIsInAttackRangeNodeAsset.cs`、`EnemyShouldAttackNodeAsset.cs`、`EnemyShouldCombatIdleNodeAsset.cs`、`EnemyShouldRetreatNodeAsset.cs`：统一读取黑板范围事实。
- Test: `Assets/Game/Editor/EnemyCombatLayerEditModeTests.cs`。

**Interfaces:**

```csharp
public enum EnemyCombatMoveMode
{
    Chase,
    Approach
}

public sealed class EnemyCombatMoveNodeAsset : ActionNodeAsset
{
    [SerializeField] private EnemyCombatMoveMode mode;
    public override BehaviorTreeNode CreateRuntimeNode();
}
```

- [ ] **Step 1: 编写失败测试**：验证 `CombatTarget` 有效时距离分区互斥；大于 ChaseRange 追击、AttackRange 到 ChaseRange 普通接近、进入 AttackRange 复用现有决策；目标离开视野仍可实时追踪；所有范围内决策失败时 Hold 返回 Success；CombatLayer 始终 Running。
- [ ] **Step 2: 运行测试确认失败**：Unity Test Runner 执行 `EnemyCombatLayerEditModeTests`；预期新条件和动作不存在。
- [ ] **Step 3: 实现范围条件和动作**：条件节点只读 AIController 黑板事实；Chase/Approach 每帧读取 `CombatTarget.position`，分别播放 run/move 动画和调用 Movement；目标失效时返回 Failure；Hold 停止移动、朝向目标、播放战斗待机并返回 Success。
- [ ] **Step 4: 实现 EnsureCombatStance**：直接从正常层进入战斗时停止移动、播放拔刀、显示武器并设置 `HasCombatStance`；警戒降级已拔刀时立即 Success；动画完成前 Running。
- [ ] **Step 5: 建立 CombatLayer 资产**：配置 `ReactiveSequence(HasCombatTarget, EnsureCombatStance, RepeatForever(CombatDistanceSelector))`；距离 Selector 结构为 `ChaseSequence -> ApproachSequence -> InAttackRangeSequence`；范围内继续引用现有 Retreat/Defense/Attack/CombatIdle 配置并接入 CombatTargetHold。
- [ ] **Step 6: 保留战斗内部边界**：不在本任务重写技能权重、普通连段、攻击冷却、后撤动画或 `canInterruptAttack`；仅确保普通受击可抢占后撤以外动作，失衡/死亡可抢占所有普通战斗动作。
- [ ] **Step 7: 运行测试确认通过**：执行 CombatLayer 和现有 `EnemyAttackPursuitEditModeTests`、`EnemyCombatIdleEditModeTests`、`EnemyBehaviorDecisionNodeEditModeTests`；预期旧战斗动作仍可被新距离层调用。
- [ ] **Step 8: 提交**：提交 `实现剑盾敌人战斗距离分层`。

## Task 7: 迁移剑盾敌人行为树资源

**Files:**
- Modify: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEnemy/SwordAndShieldEnemyBehaviorTree.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEnemy/Interrupt/InterruptExecutor.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEnemy/Normal/NormalLayer.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEnemy/Normal/NormalRoutine.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEnemy/Alert/AlertLayer.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEnemy/Alert/AlertRoutine.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEnemy/Combat/*`：CombatLayer、距离 Selector、三个距离 Sequence、Guard、移动动作、范围内决策 Selector 和 Hold。
- Delete after AssetDatabase reference scan: 剑盾敌人旧 `AlertChaseSequence`、旧搜索入口及不再引用的平铺分支资源。
- Test: `Assets/Game/Editor/SwordAndShieldBehaviorTreeAssetEditModeTests.cs`。

- [ ] **Step 1: 编写资产结构测试**：从 `AssetDatabase.LoadAssetAtPath<BehaviorTreeAsset>` 读取根资产，断言根为 `ReactivePrioritySelectorNodeAsset`，子节点顺序为 `InterruptExecutor/CombatLayer/AlertLayer/NormalLayer`；断言每层引用完整且旧平铺入口不再被根引用。
- [ ] **Step 2: 运行测试确认失败**：Unity Test Runner 执行 `SwordAndShieldBehaviorTreeAssetEditModeTests`；预期现有旧根树结构失败。
- [ ] **Step 3: 使用 Unity Editor/AIBridge 创建资产**：创建目录和 ScriptableObject 引用，所有运行时节点资产使用独立对象；公共死亡、失衡、受击 Sequence 只被 InterruptExecutor 引用一次。
- [ ] **Step 4: 设置根树引用**：按响应式优先级写入四个层节点；不修改其他敌人的 GuardMelee、TrainingDummy 行为树。
- [ ] **Step 5: 扫描旧资源引用**：用 AssetDatabase 反向引用查询确认旧专用资源没有其他行为树使用；有引用的公共资源保留，无引用的剑盾专用资源删除对应 `.asset/.meta`。
- [ ] **Step 6: 运行资产测试确认通过**：测试必须确认根顺序、层结构、公共 Sequence 引用和删除后的路径不存在。
- [ ] **Step 7: 提交**：提交 `迁移剑盾敌人四层行为树资源`。

## Task 8: 集成验证与行为回归

**Files:**
- Modify: `Assets/Game/Editor/EnemyAlertChaseEditModeTests.cs`：删除旧资源路径断言，改为新 AlertLayer 资产结构断言。
- Modify: `Assets/Game/Editor/EnemyAttackPursuitEditModeTests.cs`：将目标事实设置迁移到 CombatTarget/范围事实。
- Modify: `Assets/Game/Editor/EnemyCombatIdleEditModeTests.cs`：覆盖 CombatTarget 有效期间可战斗待机。
- Create: `Assets/Game/Editor/SwordAndShieldEnemyLayerIntegrationEditModeTests.cs`。
- Create: `Assets/Game/Scenes/Tests/SwordAndShieldEnemyLayerTestScene.unity`。

- [ ] **Step 1: 编写集成测试**：覆盖正常 0/1/多巡逻点、范围外发现进警戒、范围内发现进战斗、警戒受击进战斗、战斗记忆到期进警戒、受击/失衡抢占、后撤普通受击过滤、死亡永久终态。
- [ ] **Step 2: 运行 EditMode 测试**：在 Unity Test Runner 执行 `Game.Character.Enemy.Tests` 程序集；预期所有新增与既有敌人 EditMode 测试通过。
- [ ] **Step 3: 运行 Unity 编译**：执行 `$CLI compile unity`；预期退出码为 0，Unity 无编译错误。
- [ ] **Step 4: 检查 Unity Error 日志**：执行 `$CLI get_logs --logType Error`；预期没有由本次分层资产、节点引用、配置迁移产生的新 Error。
- [ ] **Step 5: 运行场景验收**：在测试场景逐项复现“警戒、战斗、追击、后撤、受击、失衡、死亡、返回原点”流程，记录每次层切换和黑板事实；预期不出现目标实时位置泄漏到警戒层、受击重启、后撤延迟受击或死亡回层。
- [ ] **Step 6: 检查工作树边界**：确认用户原有 `EnemySetIntentNodeAsset.cs` 变更仍保留且未被计划任务提交；确认无 `.meta`、Library、日志或临时输出进入 Git。
- [ ] **Step 7: 提交验证结果**：仅在前述命令和场景验收完成后提交 `完成剑盾敌人行为树分层验证`。

## 执行顺序与交付门槛

```text
Task 1 响应式框架
  -> Task 2 记忆/配置/范围事实
  -> Task 3 统一中断执行器
  -> Task 4 NormalLayer
  -> Task 5 AlertLayer
  -> Task 6 CombatLayer
  -> Task 7 Unity 资源迁移
  -> Task 8 集成验证
```

每个任务必须先写失败测试，再实现，再运行通过测试，最后使用简体中文提交。Task 7 前不得删除旧资源；Task 8 前不得宣称分层迁移完成。所有 Unity 资源引用变更必须经过 AssetDatabase/Inspector 读取确认，不能直接凭路径猜测 GUID。

## 规格覆盖检查

- 总设计：Task 1、2、7、8 覆盖根节点、记忆倒计时、层级迁移和验收。
- 正常层设计：Task 2、4、7、8 覆盖启动原点、0/1/多巡逻点、返程请求和 Routine 生命周期。
- 警戒层设计：Task 2、5、7、8 覆盖独立警戒记忆、最后已知位置、有限搜索、退出握手和范围外受击。
- 战斗层设计：Task 2、6、7、8 覆盖四种范围、追击/接近/攻击范围分区、CombatTarget 实时追踪和现有战斗配置复用。
- 中断层设计：Task 3、6、8 覆盖统一 Executor、死亡/失衡/受击抢占、连续受击缓存、后撤例外和死亡终态。

计划没有包含独立 FSM、伤害系统重写、战斗内部动作重构、声音感知、多目标仇恨或新的外部依赖。
