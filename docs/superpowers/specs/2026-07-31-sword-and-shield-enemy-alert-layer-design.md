# 剑盾敌人警戒状态层设计

日期：2026-07-31

状态：已确认

## 与总设计的关系

本文是 `2026-07-31-sword-and-shield-enemy-behavior-tree-layer-design.md` 的警戒状态层详细设计。总设计负责四层优先级、战斗目标和警戒记忆的公共边界；本文负责 AlertLayer 的行为树结构、阶段状态、搜索规则和退出握手。

如果两份文档对警戒层的描述不一致，以本文为准。警戒层只处理“已经知道玩家存在，但当前没有有效战斗目标”的调查流程，不承担战斗决策。

## 已确认决策

- 警戒层使用单一 `AlertRoutine`，不把拔刀、移动、搜索和收刀拆成多个记忆型 Sequence 分支。
- 敌人先移动到 `AlertLastKnownPosition`，再检查周边有限搜索点。
- 全部搜索点检查完成后主动结束警戒，不等待警戒记忆剩余时间归零。
- 警戒记忆到期和有限搜索完成是并列退出条件，任一先发生都请求退出。
- 进入警戒层时拔出剑盾；从战斗层降级到警戒层且武器已拔出时跳过拔刀。
- 退出警戒层前收起剑盾，收武器完成后才进入正常层返程。
- 警戒状态下受到玩家攻击时，不再检查战斗范围，立即建立 `CombatTarget`。
- 正常状态第一次受到战斗范围外的玩家攻击时仍只进入警戒层，不直接进入战斗层。
- 最后已知位置刷新后，旧搜索点立即失效，警戒流程从新的最后已知位置重新开始。
- 追击或搜索结束后设置 `NeedsReturnHome`，由 NormalRoutine 返回正常状态原点。

## 目标

- 用清晰的单一流程表达拔刀、调查最后已知位置、周边搜索、收刀和返程交接。
- 让警戒记忆、搜索进度和战斗目标各自只有一个真相源。
- 保证警戒层可以被战斗层和中断层即时抢占。
- 保证不可见玩家的实时 Transform 位置不会泄漏给移动或搜索行为。
- 保证搜索有明确上限，不会因为随机点、记忆倒计时或节点返回值无限循环。
- 保证警戒层再次受击时能够确认攻击者并转入战斗。

## 非目标

- 不在警戒层执行攻击、防御、后撤或战斗待机决策。
- 不引入声音传播、队友通知、仇恨列表或多目标选择。
- 不设计房间级搜索、掩体检查、门窗交互或跨区域寻路。
- 不为搜索点创建场景对象或持久化 Transform。
- 不保留旧的 `IsSearching`、`HasTargetMemory` 等兼容事实作为第二套状态机。
- 不在 AlertRoutine 内维护第二套警戒总时长倒计时。

## 行为树结构

```text
AlertLayerReactiveSequence
├─ HasNoCombatTarget
├─ ShouldRunAlertLayer
└─ AlertRoutine
```

节点职责：

- `HasNoCombatTarget`：`CombatTarget` 为空时返回 Success，否则返回 Failure。
- `ShouldRunAlertLayer`：存在警戒记忆或正在完成警戒退出动作时返回 Success。
- `AlertRoutine`：负责警戒姿态、移动、观察、有限搜索和退出动作；警戒流程有效期间返回 Running。

`ShouldRunAlertLayer` 的唯一计算规则为：

```text
HasAlertMemory || IsAlertExitPending
```

`IsAlertExitPending` 是警戒退出握手，不是第二份警戒记忆。它只用于在警戒记忆已经清除后，允许 AlertRoutine 完成收武器动作。

## 黑板事实

警戒层读取或写入以下共享事实：

```text
CombatTarget
HasAlertMemory
AlertLastKnownPosition
AlertMemoryRemaining
IsAlertExitPending
HasCombatStance
NeedsReturnHome
```

职责边界：

- 统一目标记忆逻辑负责建立、刷新、到期和清除警戒记忆。
- 感知和受击事件只通过统一入口提交已确认的位置和攻击者。
- AlertRoutine 只读取记忆事实并请求开始或完成警戒退出。
- `NeedsReturnHome` 只由警戒或战斗退出流程设置，由 NormalRoutine 完成返程后清除。
- `HasCombatStance` 只表示剑盾是否已经完成拔出，不等同于是否存在战斗目标。

## 攻击事件规则

处理玩家攻击事件时，必须先读取事件发生前的警戒事实，再写入本次攻击产生的新记忆：

```text
wasAlertActive = HasAlertMemory || IsAlertExitPending

if wasAlertActive
    -> 不检查战斗范围
    -> 建立 CombatTarget
    -> 刷新战斗记忆
else if attacker 在战斗范围内
    -> 建立 CombatTarget
    -> 刷新战斗记忆
else
    -> 只建立警戒记忆

以上分支都同步刷新攻击者的最后已知位置和警戒记忆。
```

必须在写入新的警戒记忆前计算 `wasAlertActive`。否则正常状态第一次受到范围外攻击时，本次攻击刚写入的警戒记忆会被错误地当作“攻击前已经处于警戒”。

警戒状态受击时即使同时产生受击或失衡事实，也在同一帧建立 `CombatTarget`。根节点仍按中断层优先级先执行受击或失衡表现，中断结束后根据已存在的战斗目标进入战斗层。

伤害事件必须提供可识别的玩家攻击者 Transform。攻击者为空属于上游事件契约错误，不能建立匿名战斗目标。

## AlertRoutine 运行时状态

每个敌人的 AlertRoutine 运行时节点独立保存：

```text
phase
activeAlertOrigin
searchPoints
searchIndex
observationRemaining
activeEnterCombatAnimation
activeExitCombatAnimation
```

这些数据不得写入 ScriptableObject 资产，避免多个敌人共享搜索进度。它们也不写入黑板，避免把节点内部步骤提升为跨系统事实。

## 运行阶段

```text
Uninitialized
EnteringCombatStance
MovingToLastKnownPosition
InspectingLastKnownPosition
MovingToSearchPoint
InspectingSearchPoint
ExitingCombatStance
```

### Uninitialized

首次进入或被 Reset 后重新进入警戒层时：

- 清除旧搜索点、搜索索引和观察计时。
- 读取当前 `AlertLastKnownPosition` 作为 `activeAlertOrigin`。
- `HasCombatStance` 为假时进入 `EnteringCombatStance`。
- `HasCombatStance` 为真时直接进入 `MovingToLastKnownPosition`。
- `IsAlertExitPending` 为真时直接进入 `ExitingCombatStance`。

从战斗层降级到警戒层时通常已经拔出剑盾，因此不得重复播放拔刀动画。

### EnteringCombatStance

行为：

- 停止移动。
- 朝向当前最后已知位置。
- 播放拔出剑盾动画。
- 动画完成后显示武器并设置 `HasCombatStance = true`。
- 随后进入 `MovingToLastKnownPosition`。

拔刀期间最后已知位置可以刷新，但不重新播放拔刀动画。拔刀期间建立战斗目标时，由根节点抢占到战斗层；战斗层根据 `HasCombatStance` 决定是否需要补完进入战斗姿态。

### MovingToLastKnownPosition

行为：

- 只读取 `AlertLastKnownPosition`，禁止读取不可见玩家的实时 Transform 位置。
- 播放警戒移动动画并向 `activeAlertOrigin` 移动。
- 使用 `EnemyMovementComponent.StoppingDistance` 判断到达。
- 到达后停止移动并进入 `InspectingLastKnownPosition`。

如果 `AlertLastKnownPosition` 与 `activeAlertOrigin` 不再一致：

- 更新 `activeAlertOrigin`。
- 清除旧搜索点和索引。
- 重新向新的最后已知位置移动。

### InspectingLastKnownPosition

行为：

- 停止移动并保持警戒待机表现。
- 使用 `searchObservationDuration` 等待。
- 感知系统继续在每帧行为树执行前扫描玩家。
- 等待完成后生成一次周边搜索点。

`searchPointCount = 0` 或没有生成有效搜索点时，最后已知位置观察完成即视为有限搜索完成，并请求退出警戒。

### MovingToSearchPoint

行为：

- 按数组顺序移动到当前搜索点。
- 播放警戒移动动画。
- 到达后停止移动并进入 `InspectingSearchPoint`。

搜索过程中只使用 AlertRoutine 已生成的 Vector3 搜索点，不读取玩家 Transform。

### InspectingSearchPoint

行为：

- 停止移动并保持警戒待机表现。
- 使用 `searchObservationDuration` 等待。
- 等待完成后推进 `searchIndex`。
- 仍有搜索点时回到 `MovingToSearchPoint`。
- 全部搜索点完成时请求退出警戒。

### ExitingCombatStance

进入条件：

- 警戒记忆到期；或
- 最后已知位置和所有有效搜索点已经检查完成。

行为：

- 停止移动。
- `HasCombatStance` 为真时播放收起剑盾动画。
- 收武器完成后隐藏武器并设置 `HasCombatStance = false`。
- 通过统一警戒状态入口完成退出握手。
- 设置 `IsAlertExitPending = false`。
- 设置 `NeedsReturnHome = true`。

如果进入退出阶段时 `HasCombatStance` 已经为假，则无需播放收武器动画，直接完成退出握手。

## 搜索点规则

周边搜索点按以下规则生成：

- 以本轮固定的 `activeAlertOrigin` 为圆心。
- 在 `searchRadius` 圆形范围内随机生成最多 `searchPointCount` 个候选点。
- 每个候选点必须成功投射到 NavMesh 才能加入搜索数组。
- NavMesh 投射失败的候选点直接舍弃，禁止使用原始不可达位置兜底。
- 搜索点只在最后已知位置观察完成后生成一次，禁止每帧重新随机。
- 有效搜索点数量允许少于配置数量。
- 一个有效搜索点都没有时，直接视为有限搜索完成。

`searchPointCount` 只表示周边点数量，不包含 `AlertLastKnownPosition` 本身。

最后已知位置刷新时，必须废弃整组搜索点。旧搜索点属于旧情报，禁止在新位置建立后继续执行。

## 警戒退出握手

警戒记忆到期或有限搜索完成时，统一入口执行：

```text
RequestAlertExit
  -> HasAlertMemory = false
  -> AlertMemoryRemaining = 0
  -> 清除 AlertLastKnownPosition
  -> IsAlertExitPending = true
```

AlertLayer 因 `IsAlertExitPending` 保持有效，直到 AlertRoutine 完成收武器动作。完成后执行：

```text
CompleteAlertExit
  -> HasCombatStance = false
  -> IsAlertExitPending = false
  -> NeedsReturnHome = true
```

新的有效情报可以取消退出：

- 看到战斗范围外玩家：重新建立警戒记忆，取消 `IsAlertExitPending`，从新位置开始调查。
- 看到战斗范围内玩家：建立 `CombatTarget`，战斗层抢占。
- 警戒退出期间受到玩家攻击：无视距离建立 `CombatTarget`，取消警戒退出。

收武器动画完成前 `HasCombatStance` 保持为真。被警戒或战斗抢占时中断收武器表现，不重复拔刀。

## 警戒记忆规则

以下事件建立或刷新警戒记忆：

- 敌人看到玩家，无论玩家是否位于战斗范围内。
- 玩家攻击敌人，无论攻击者是否位于战斗范围内。
- 当前战斗目标被重新确认。

刷新时写入当前已确认的位置并重置 `AlertMemoryRemaining`。不可见目标的实时位置不得刷新警戒记忆。

警戒倒计时在以下阶段都继续推进：

- 拔刀。
- 移动到最后已知位置。
- 观察和周边搜索。
- 受击或失衡中断。

`searchObservationDuration` 只是单个观察点停留时间，不是警戒记忆倒计时，也不延长警戒持续时间。

## 层级进入条件

同时满足以下条件时执行警戒层：

- 没有更高优先级的死亡、失衡或受击中断；
- `CombatTarget` 为空；
- `HasAlertMemory` 或 `IsAlertExitPending` 为真。

警戒层可以来自：

- 正常层看到战斗范围外玩家。
- 正常层受到战斗范围外玩家攻击。
- 战斗记忆到期，但警戒记忆仍然有效。
- 非战斗中断结束后，警戒事实仍然有效。

## 层级退出条件

### 进入战斗层

- 看到玩家且玩家位于战斗范围内。
- 警戒状态下受到玩家攻击，不检查战斗范围。

建立 `CombatTarget` 后，`HasNoCombatTarget` 在下一次根节点评估时失败，战斗层按照更高优先级接管。

### 进入正常层

- 警戒记忆到期并完成收武器动作。
- 有限搜索完成并完成收武器动作。

进入正常层前必须设置 `NeedsReturnHome = true`。

### 进入中断层

- 出现死亡、失衡或受击事实。

中断层只临时抢占。中断结束后重新根据 `CombatTarget`、警戒记忆和退出握手选择战斗层、警戒层或正常层。

## Reset 语义

AlertRoutine 被根节点或外层 ReactiveSequence 重置时：

- 停止由警戒层发起的当前移动。
- 清除搜索点、搜索索引和观察计时。
- 清除节点内部的拔刀、收刀动画进度。
- 不清除 `HasAlertMemory`。
- 不清除 `CombatTarget`。
- 不修改 `NeedsReturnHome`。
- 不主动收起武器。
- 不修改已经完成的 `HasCombatStance`。

战斗层抢占警戒层时保留剑盾姿态和警戒记忆。以后战斗记忆到期并降级到警戒层时，从最新 `AlertLastKnownPosition` 重新开始调查。

如果非玩家事件造成临时中断且没有建立战斗目标，中断结束后警戒记忆仍有效，则从最新最后已知位置重新开始，不恢复旧随机搜索点。

## 数据更新顺序

每帧顺序固定为：

```text
AIController.TickAI
  1. 感知组件采集当前可见玩家
  2. 伤害系统按事件顺序提交本帧玩家攻击
  3. 读取攻击发生前的警戒事实并处理目标确认
  4. 统一记忆逻辑刷新或推进战斗记忆、警戒记忆和退出请求
  5. 刷新距离、战斗范围和可见性事实
  6. BehaviorTreeRunner.Tick
  7. 根节点选择中断、战斗、警戒或正常层
  8. AlertRoutine 操作 Movement 和 Animation
```

如果同一帧发生多个攻击事件，按照事件顺序逐个处理。第一次范围外攻击可以把正常状态提升为警戒，后续同帧攻击可以基于已经成立的警戒事实进一步确认战斗目标。

## 配置

新增或明确以下配置：

```text
alertMemoryDuration
searchRadius
searchPointCount
searchObservationDuration
```

约束：

- `alertMemoryDuration > 0`。
- `searchPointCount >= 0`。
- `searchPointCount > 0` 时 `searchRadius > 0`。
- `searchObservationDuration >= 0`。
- 移动停止距离复用 `EnemyMovementComponent.StoppingDistance`。

旧的 `targetMemoryTime` 和 `searchWaitTime` 需要迁移到语义明确的新字段并删除，不能继续作为另一套警戒倒计时。`searchRadius` 和 `searchPointCount` 可以保留字段语义，但搜索点生成实现必须遵守本文规则。

## 资源设计

剑盾敌人的警戒层资源建议为：

```text
SwordAndShieldEnemy/Alert/
├─ AlertLayer.asset
├─ HasNoCombatTarget.asset
├─ ShouldRunAlertLayer.asset
└─ AlertRoutine.asset
```

完成迁移后，剑盾敌人行为树不再引用旧的：

```text
AlertChaseSequence.asset
SetIntentAlertChase.asset
LostTargetSearchSequence.asset
SearchSequence.asset
ShouldSearchLastKnownPosition.asset
```

剑盾敌人目录内的旧专用资源在确认无引用后删除。`Common` 目录下的资源只有在整个项目都无引用时才删除；如果其他敌人仍使用则保留，但不得继续作为剑盾敌人的第二套警戒入口。

## 错误处理

- AlertLayer 缺少必要 Guard 或 AlertRoutine 时，行为树初始化失败并记录 Error。
- 缺少 Movement 或 Animation 属于敌人配置错误，AlertRoutine 快速失败并记录可定位错误。
- 玩家攻击事件缺少攻击者引用属于上游事件契约错误，不创建匿名目标。
- `AlertLastKnownPosition` 只由已确认感知或攻击事件写入，不使用默认零向量冒充有效位置。
- 搜索点 NavMesh 投射失败属于可预期的场景结果，只舍弃该候选点，不使用不可达位置兜底。
- 配置违反数值约束时定义校验失败，不在运行时静默修正。

## 测试方案

### EditMode

- `ShouldRunAlertLayer` 仅在警戒记忆或退出握手成立时成功。
- 正常状态第一次受到战斗范围外攻击时只建立警戒记忆。
- 警戒状态受到玩家攻击时，无视战斗范围建立 `CombatTarget`。
- 攻击事件在写入新警戒记忆前读取 `wasAlertActive`。
- 从正常层进入警戒时播放一次拔刀，从战斗层降级时不重复拔刀。
- 最后已知位置刷新时清除搜索点和索引。
- `searchPointCount = 0` 时检查最后已知位置后请求退出。
- 搜索点只生成一次，且只保留成功投射到 NavMesh 的位置。
- 搜索完成和警戒记忆到期都进入 `ExitingCombatStance`。
- 请求退出后清除警戒记忆并保留 `IsAlertExitPending`。
- 收武器完成后清除退出握手并设置 `NeedsReturnHome`。
- 收武器期间重新发现玩家时取消退出流程。
- AlertRoutine Reset 不清除跨层事实或主动收武器。

### Runtime

- 玩家在战斗范围外暴露后，敌人拔出剑盾并移动到最后已知位置。
- 玩家持续可见但保持在战斗范围外时，敌人持续更新最后已知位置并追踪。
- 玩家进入战斗范围后，战斗层立即接管。
- 正常状态被远程攻击一次时进入警戒，被警戒状态敌人再次远程攻击时进入战斗。
- 警戒受击产生受击或失衡时，先播放中断表现，结束后进入战斗。
- 玩家消失后，敌人检查最后已知位置和有限周边搜索点。
- 搜索完成早于记忆到期时，敌人主动收武器并返程。
- 记忆到期早于搜索完成时，敌人停止搜索、收武器并返程。
- 收武器期间重新发现玩家时，敌人中断退出并恢复警戒或战斗。
- 警戒结束后，0、1、多巡逻点敌人都返回 NormalLayer 定义的正常状态原点。

## 验收标准

- AlertLayer 只包含两个层级 Guard 和一个 AlertRoutine。
- 警戒层不存在独立 Chase/Search Selector 或第二套状态机。
- 警戒记忆和退出握手职责清晰，收武器动画不会被记忆清除直接跳过。
- 警戒状态受玩家攻击时不检查战斗范围，正常状态第一次范围外受击仍只进入警戒。
- 不可见玩家的实时位置不会被警戒移动或搜索行为读取。
- 最后已知位置刷新后不会继续使用旧搜索点。
- 有限搜索完成或警戒记忆到期后都能退出，并设置返程请求。
- 战斗层和中断层可以即时抢占警戒层。
- 剑盾敌人的旧警戒入口完成迁移，不保留重复真相源。
