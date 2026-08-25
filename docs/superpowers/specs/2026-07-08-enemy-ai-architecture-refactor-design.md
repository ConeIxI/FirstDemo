# 敌人 AI 架构重构设计

## 目标

彻底抛弃现有敌人架构，重建一套行为树 + 状态机的组件化敌人 AI 框架。新架构必须覆盖旧架构已经具备的敌人行为能力，不做功能降级。

本次设计的核心目标：

- 使用行为树负责高层决策，状态机负责动作执行。
- 保留项目现有 `FsmBase<T>` / `FsmStateBase<T>` 框架，不重写基础 FSM。
- 以 `AIController` 作为敌人的 AI 中枢。
- 将敌人拆成决策、移动、感知、动画、战斗、生命、属性等独立组件。
- 使用 `EnemyBlackboard` 作为组件间共享事实存储。
- 使用 ScriptableObject + JSON 混合数据驱动。
- 新敌人优先靠配置创建；新行为通过新增行为树节点、状态或能力组件扩展，不复制一套 AIController。

## 非目标

第一版不做以下内容：

- 行为树可视化编辑器。
- 复杂 Boss 多阶段系统。
- 远程、召唤、飞行等新敌人完整实现。
- 调试 UI 面板。
- 旧敌人架构兼容层。

## 旧能力等价基线

新架构第一版必须覆盖旧架构已有能力：

- 状态：`Idle`、`Patrol`、`Chase`、`Attack`、`Skill`、`GetHit`、`Unbalance`、`Dead`。
- 巡逻：路点巡逻、到点等待、无巡逻路线回到 Idle。
- 感知：视野距离、视野角度、遮挡检测、近距离感知。
- 追踪：可见目标、最后已知位置、搜索等待、目标丢失后回无目标状态。
- 战斗：攻击距离、首段攻击技能、技能释放、连段技能、动画完成后决策。
- 受击：使用受击动画名，播放失败时回退默认 `GetHit`，动画结束后回追击。
- 失衡：播放 `Unbalance`，结束后恢复稳定值并回追击。
- 死亡：清目标、停止移动、终止攻击、关闭武器碰撞、清战斗状态、播放死亡动画。
- 动画事件：武器碰撞开关、命中窗口、攻击决策窗口继续接入。
- 战斗链路：继续复用 `SkillRunner`、`Combatant`、`DamageResolver`。

旧架构里的临时 HP / 稳定值文本显示属于调试逻辑，不进入新敌人核心架构。

## 总体架构

新敌人以 `EnemyAgent` 作为装配根，以 `AIController` 作为 AI 中枢。

```text
EnemyAgent
- EnemyDefinition
- EnemyBlackboard
- AIController
- EnemyMovementComponent
- EnemyPerceptionComponent
- EnemyAnimationComponent
- EnemyCombatComponent
- EnemyLifeComponent
- EnemyAttributeComponent
```

`EnemyAgent` 只负责组件装配、初始化和统一 Tick，不承载具体 AI 业务。

`AIController` 内部包含：

```text
AIController
- BehaviorTreeRunner behaviorTree
- FsmBase<AIController> actionFsm
- EnemyBlackboard blackboard
- EnemyStateContext context
```

`BehaviorTreeRunner`、`BehaviorTreeAsset`、`BehaviorTreeNodeAsset` 和运行时 `BehaviorTreeNode` 来自通用行为树框架。敌人 AI 只新增敌人专用条件节点和动作节点，不复制一套行为树框架。

每帧执行顺序：

```text
1. EnemyPerceptionComponent 更新感知事实
2. EnemyLifeComponent 同步死亡、受击、失衡事实
3. AIController 判断是否进入强制状态
4. BehaviorTreeRunner Tick，返回 BehaviorTreeStatus，并由敌人动作节点写入 EnemyIntent
5. AIController 读取 EnemyIntent 并转成 FSM 状态请求
6. actionFsm.Update(deltaTime) 执行动作状态
```

行为树负责“想做什么”，状态机负责“把动作做完”。

## 组件通信规则

组件通信使用三层规则：黑板、命令、事件。

### 黑板

`EnemyBlackboard` 存储共享事实，不承载业务逻辑。

黑板记录：

- 当前目标。
- 最后已知位置。
- 是否正在搜索。
- 当前意图。
- 当前状态。
- 当前技能 ID。
- 待播放受击动画。
- 是否死亡。
- 是否失衡。
- 当前目标可见性。

### 命令

状态机是主动编排者。执行状态通过 `EnemyStateContext` 调用组件命令：

```text
State
-> Movement.MoveTo / Stop / LookAt
-> Animation.Play / IsPlaying
-> Combat.TryStartAttack / TryStartSkill / EndAction
-> Perception.EvaluateTarget
```

组件之间原则上不互相发命令。感知组件只更新事实，不决定是否追击；是否追击由行为树和状态机共同决定。

### 事件

异步事实通过事件进入系统：

```text
AnimationEvent -> EnemyAnimationComponent -> EnemyCombatComponent
CombatResult -> EnemyLifeComponent / EnemyBlackboard
Life.Died -> AIController 请求 Dead
```

事件只描述发生了什么，不承载复杂决策。

## 行为树设计

行为树 Tick 返回 `BehaviorTreeStatus`。敌人动作节点只负责写入 `EnemyIntent`，不直接播放动画、不直接移动、不直接释放技能。

行为树使用项目通用框架：

```text
BehaviorTreeAsset
-> BehaviorTreeNodeAsset root
-> BehaviorTreeRunner.Tick(deltaTime)
-> BehaviorTreeStatus
```

节点资产继承通用基类：

```text
CompositeNodeAsset
DecoratorNodeAsset
ConditionNodeAsset
ActionNodeAsset
```

敌人专用条件和动作节点继承这些通用资产基类，例如 `EnemyCanSeeTargetNodeAsset` 继承 `ConditionNodeAsset`，`EnemySetIntentNodeAsset` 继承 `ActionNodeAsset`。

`EnemyIntent` 第一版包括：

```text
None
Idle
Patrol
Chase
Search
Attack
Skill
Flee
KeepDistance
Summon
GetHit
Unbalance
Dead
```

第一版行为树节点使用 ScriptableObject 配置，不做可视化编辑器。

节点类型：

```text
CompositeNodeAsset:
- Selector
- Sequence

ConditionNodeAsset:
- IsDead
- HasHitReaction
- IsUnbalanced
- CanSeeTarget
- HasTargetMemory
- IsTargetInAttackRange
- IsHealthBelow

ActionNodeAsset:
- SetIntent(Patrol)
- SetIntent(Chase)
- SetIntent(Attack)
- SetIntent(Skill)
- SetIntent(Flee)
- SetIntent(Search)

DecoratorNodeAsset:
- Inverter
- AlwaysSuccess
- AlwaysFailure
```

追击型敌人行为树示例：

```text
Selector
- Sequence(IsDead, SetIntentDead)
- Sequence(IsUnbalanced, SetIntentUnbalance)
- Sequence(HasHitReaction, SetIntentGetHit)
- Sequence(CanSeeTarget, IsInAttackRange, SetIntentAttack)
- Sequence(CanSeeTarget, SetIntentChase)
- Sequence(HasLastKnownPosition, SetIntentSearch)
- SetIntentPatrol
```

逃跑型敌人行为树示例：

```text
Selector
- Sequence(IsDead, SetIntentDead)
- Sequence(IsUnbalanced, SetIntentUnbalance)
- Sequence(HasHitReaction, SetIntentGetHit)
- Sequence(CanSeeTarget, SetIntentFlee)
- SetIntentPatrol
```

两种敌人共用同一个 `AIController`，差异只来自 `BehaviorTreeAsset`、敌人专用 `BehaviorTreeNodeAsset` 和启用状态集合。

## 状态机设计

状态继续继承项目现有 FSM 框架：

```text
EnemyStateBase : FsmStateBase<AIController>
```

`AIController` 持有：

```text
FsmBase<AIController>
EnemyStateContext
ChangeState(EnemyStateId)
RequestState(EnemyStateId)
```

执行状态第一版包括：

```text
IdleState
PatrolState
ChaseState
SearchState
AttackState
SkillState
GetHitState
UnbalanceState
DeadState
```

可扩展状态包括：

```text
FleeState
KeepDistanceState
RangedAttackState
SummonState
```

强制状态优先级：

```text
Dead > Unbalance > GetHit > Attack/Skill 锁定 > 行为树普通意图
```

如果当前状态是 `AttackState` 或 `SkillState`，行为树下一帧产出普通 Chase 意图也不能立即打断。是否允许打断由技能配置或状态配置决定。

状态行为：

- `IdleState`：播放 Idle，停止移动；等待行为树意图。
- `PatrolState`：按路点巡逻，到点等待；无路点时回 Idle。
- `ChaseState`：追可见目标；目标不可见时交给 Search 意图。
- `SearchState`：移动到最后已知位置并搜索，超时后回无目标意图。
- `AttackState`：停止移动，转向目标，释放攻击技能，等待动画完成并处理连段。
- `SkillState`：释放当前技能 ID，流程类似 Attack。
- `GetHitState`：播放受击动画，结束后回行为树普通决策。
- `UnbalanceState`：播放失衡动画，结束后恢复稳定值。
- `DeadState`：清理战斗和移动状态，播放死亡动画，进入后不再响应普通意图。

## 七大组件职责

### AIController

职责：

- 持有行为树运行器。
- 持有动作状态机。
- 管理意图到状态的转换。
- 管理强制状态优先级。
- 暴露当前状态。

接口：

```text
StartAI()
TickAI(float deltaTime)
RequestState(EnemyStateId stateId)
ChangeState(EnemyStateId stateId)
CanChangeTo(EnemyStateId stateId)
```

### EnemyMovementComponent

职责：

- 移动。
- 停止。
- 转向。
- NavMeshAgent 与 CharacterController 同步。
- NavMesh 采样。

接口：

```text
MoveTo(Vector3 position)
Stop()
LookAt(Vector3 position)
HasReached(Vector3 position, float distance)
SampleNavMesh(Vector3 source, out Vector3 result)
```

### EnemyPerceptionComponent

职责：

- 视觉检测。
- 近距离感知。
- 遮挡判断。
- 最后已知位置。
- 搜索点生成。

接口：

```text
ScanTarget()
CanSee(Transform target)
CanSenseNearby(Transform target, float range)
EvaluateTarget(float deltaTime)
ForgetTarget()
```

### EnemyAnimationComponent

职责：

- 播放动画。
- 查询动画进度。
- 接收 Unity 动画事件。
- 转发动画事件。

接口：

```text
Play(string animationName)
TryPlay(string animationName)
IsPlaying(string animationName, out float progress)
HandleAnimationEvent(string eventName)
```

### EnemyCombatComponent

职责：

- 判断攻击距离。
- 读取技能配置。
- 调用 `SkillRunner.Cast`。
- 管理攻击和技能动作生命周期。
- 管理武器碰撞窗口。

接口：

```text
IsInAttackRange(Transform target)
TryStartAttack(int skillId)
TryStartSkill(int skillId)
EndAction()
EnableWeaponHit()
DisableWeaponHit()
```

战斗结算继续复用现有 `SkillRunner`、`Combatant`、`DamageResolver`。

### EnemyLifeComponent

职责：

- 同步生命状态。
- 处理受击反应请求。
- 处理失衡请求。
- 处理死亡请求。

接口：

```text
IsDead
HandleHitReaction(string animationName)
HandleUnbalance()
HandleDeath()
```

死亡流程由 `DeadState` 执行，生命组件只发布事实和请求。

### EnemyAttributeComponent

职责：

- 从 `EnemyDefinition` 和 JSON 属性表加载运行时属性副本。
- 提供移动、感知、战斗、生存相关只读属性。

接口：

```text
LoadFromDefinition(EnemyDefinition definition)
MoveSpeed
RotateSpeed
AttackPower
Defense
Resistances
PerceptionRange
```

运行时组件读取属性副本，不直接修改 ScriptableObject。

## 数据驱动结构

新敌人配置分三层：

```text
EnemyDefinition：敌人主配置
BehaviorTreeAsset：行为树决策配置
JSON：技能和属性数值
```

`EnemyDefinition` 字段：

```text
enemyId
displayName
behaviorTreeAsset
startState
enabledStates
movementConfig
perceptionConfig
animationConfig
combatConfig
lifeConfig
attributeConfigId
skillSet
patrolRoute
```

`combatConfig` 字段：

```text
firstAttackSkillId
normalComboSkillIds
specialSkillIds
defaultAttackRange
canInterruptAttack
```

`animationConfig` 字段：

```text
idleAnimation
moveAnimation
getHitAnimation
unbalanceAnimation
deadAnimation
```

攻击动画优先读取技能 JSON 的 `skillAnimationName`。

`attributeConfigId` 指向 JSON 敌人属性表，运行时生成属性副本：

```text
maxHealth
maxStability
attack
defense
poise
resistances
moveSpeedMultiplier
perceptionMultiplier
```

## 新敌人创建流程

创建普通近战敌人：

```text
1. 复制基础 Enemy prefab
2. 创建 EnemyDefinition
3. 选择 GuardMeleeBehaviorTree
4. 启用 Idle/Patrol/Chase/Search/Attack/Skill/GetHit/Unbalance/Dead
5. 配置 patrolRoute、perceptionConfig、movementConfig
6. 配置 firstAttackSkillId 和 combo skillIds
7. 配置 attributeConfigId
8. 挂到 prefab 的 EnemyAgent.definition
```

创建看到玩家就逃的敌人：

```text
1. 复制基础 Enemy prefab
2. 创建 EnemyDefinition
3. 选择 CowardFleeBehaviorTree
4. 启用 Idle/Patrol/Flee/GetHit/Dead
5. 配置 fleeDistance、safeDistance、moveSpeed
6. 挂到 prefab
```

创建远程敌人：

```text
1. 新增 RangedAttackState
2. 新增投射物能力组件或扩展 EnemyCombatComponent
3. 创建 ArcherBehaviorTree
4. EnemyDefinition 启用 KeepDistance/RangedAttack
5. 配置远程技能 ID 和投射物参数
```

## 错误处理

初始化校验在 `EnemyAgent.Initialize()` 执行：

```text
缺 EnemyDefinition：报错，禁用 AI
缺 BehaviorTreeAsset：报错，禁用 AI
缺必要组件：报错，禁用 AI
行为树引用未启用状态：报错
启用状态但缺对应状态类：报错
技能 ID 找不到配置：报错
关键动画名为空：报错
属性 ID 找不到：报错
```

运行时规则：

```text
目标丢失：进入 Search 或 Patrol
技能释放失败：记录 Warning，返回 Chase 或默认意图
动画播放失败：受击可回退 GetHit，攻击技能不静默回退
NavMesh 采样失败：记录 Warning，停止移动并返回上层意图
死亡后收到普通意图：忽略
```

## 测试与验收

EditMode 测试：

```text
BehaviorTreeRunner 按节点顺序返回正确 BehaviorTreeStatus，敌人动作节点写入正确 EnemyIntent
AIController 优先级为 Dead > Unbalance > GetHit > Attack锁定 > 普通意图
EnemyBlackboard 正确写入和清理目标、最后已知位置、受击动画
EnemyDefinitionValidator 能发现缺状态、缺技能、缺行为树
```

组件集成测试：

```text
CanSeeTarget -> 行为树 SetIntent(Chase) -> FSM 进入 ChaseState
IsInAttackRange -> SetIntent(Attack) -> AttackState 调用 Combat.TryStartAttack
攻击动画未结束时，普通 Chase 意图不能打断 AttackState
GetHit 事件能抢占普通状态
Dead 事件能抢占所有状态
```

Unity 编译验证必须使用：

```text
$CLI compile unity
```

场景验收：

```text
巡逻到路点并等待
看到玩家后追击
进入攻击距离后攻击
攻击连段能继续
丢失玩家后追最后已知位置并搜索
受击进入 GetHit 后回追击
稳定值破坏进入 Unbalance 并恢复稳定值
死亡后停止移动、关闭攻击、播放死亡动画
```

## 目录建议

```text
Assets/Framework/Core/BehaviorTree/
- BehaviorTreeAsset.cs
- BehaviorTreeRunner.cs
- BehaviorTreeStatus.cs
- BehaviorTreeContext.cs
- BehaviorTreeBlackboard.cs
- Assets/
  - BehaviorTreeNodeAsset.cs
  - CompositeNodeAsset.cs
  - DecoratorNodeAsset.cs
  - ConditionNodeAsset.cs
  - ActionNodeAsset.cs
- Nodes/
  - SelectorNodeAsset.cs
  - SequenceNodeAsset.cs
  - InverterNodeAsset.cs
  - AlwaysSuccessNodeAsset.cs
  - AlwaysFailureNodeAsset.cs
- Runtime/
  - BehaviorTreeNode.cs

Assets/Game/Character/Enemy/
- Core/
  - EnemyAgent.cs
  - EnemyBlackboard.cs
  - EnemyStateContext.cs
- AI/
  - AIController.cs
  - BehaviorTree/
    - EnemyCanSeeTargetNodeAsset.cs
    - EnemyIsInAttackRangeNodeAsset.cs
    - EnemySetIntentNodeAsset.cs
  - States/
- Components/
  - EnemyMovementComponent.cs
  - EnemyPerceptionComponent.cs
  - EnemyAnimationComponent.cs
  - EnemyCombatComponent.cs
  - EnemyLifeComponent.cs
  - EnemyAttributeComponent.cs
- Config/
  - EnemyDefinition.cs
  - EnemyMovementConfig.cs
  - EnemyPerceptionConfig.cs
  - EnemyCombatConfig.cs
  - EnemyAnimationConfig.cs
```

## 迁移策略

迁移采用直接替换，不保留旧兼容层：

```text
1. 新增新架构目录与核心组件
2. 接入通用行为树框架，新增敌人专用行为树节点
3. 实现 AIController + 状态机执行层
4. 实现七大组件
5. 创建等价 Guard 敌人配置
6. 用新 prefab 替换场景旧敌人
7. 删除旧 EnemyStateMachine / EnemyFsm / EnemyActor 相关代码
8. 跑 EditMode 测试和 Unity 编译
```

删除旧架构时只删除敌人旧系统相关文件，不触碰玩家 FSM、通用 FSM、通用战斗结算链路。
