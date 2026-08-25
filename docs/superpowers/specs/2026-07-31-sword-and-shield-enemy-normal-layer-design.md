# 剑盾敌人正常状态层设计

日期：2026-07-31

状态：已确认

## 与总设计的关系

本文是 `2026-07-31-sword-and-shield-enemy-behavior-tree-layer-design.md` 的正常状态层详细设计。总设计负责四层边界、记忆和根节点优先级；本文负责 NormalLayer 的节点结构、巡逻路线语义和运行阶段。

如果两份文档对正常层的描述不一致，以本文为准。正常层不再拆分 Idle 和 Patrol 两个状态或两个行为分支，而是由单一 `NormalRoutine` 根据巡逻点数量决定表现。

## 已确认决策

- 正常层使用一个 `NormalRoutine` 行为节点。
- 巡逻点为 0 个时，使用 AI 启动位置作为正常状态原点并持续待机。
- 巡逻点为 1 个时，该点作为正常状态原点，敌人到达后持续待机，不进行巡逻。
- 巡逻点为 2 个及以上时，第一个点作为正常状态原点，并按数组顺序循环巡逻全部点。
- 玩家追击或警戒流程结束后，敌人必须先返回正常状态原点，再恢复待机或巡逻。
- 返回正常状态后，多点路线从第一个巡逻点重新开始。
- 受击或失衡等临时中断结束后，如果没有经历追击或搜索结束，不强制返回原点。

## 目标

- 用一个正常状态行为统一表达待机、返程和巡逻。
- 让场景中的巡逻点数量直接决定正常状态表现，不增加 Idle/Patrol 模式配置。
- 让玩家丢失后的敌人能够返回明确驻点，而不是停留在追击结束位置。
- 保证正常状态可以被警戒、战斗和中断层立即抢占。
- 保证巡逻进度、返程请求和层级状态只有一个真相源。

## 非目标

- 不为正常层增加随机游荡、随机路线或分支路线。
- 不设计多套巡逻路线之间的动态切换。
- 不在行为树中生成或移动场景巡逻点。
- 不让正常层决定警戒记忆或战斗目标是否有效。
- 不在本次设计中处理开门、攀爬、跳跃等特殊导航动作。

## 行为树结构

```text
NormalLayerReactiveSequence
├─ HasNoCombatTarget
├─ HasNoAlertMemory
└─ NormalRoutine
```

节点职责：

- `HasNoCombatTarget`：战斗目标为空时返回 Success，否则返回 Failure。
- `HasNoAlertMemory`：警戒记忆无效时返回 Success，否则返回 Failure。
- `NormalRoutine`：负责返回原点、待机和按路点巡逻；正常层有效期间持续返回 Running。

NormalLayer 不再需要 `NormalBehaviorSelector`、独立 `Idle` 节点或独立 `Patrol` 节点。路线数量属于 NormalRoutine 的输入，不是状态分支。

## 数据定义

### 正常状态原点

运行时必须保存：

```text
StartupHomePosition
StartupHomeRotation
NormalOriginPosition
NormalOriginRotation
```

`StartupHomePosition` 和 `StartupHomeRotation` 在 `AIController.StartAI` 开始时，从敌人当前 Transform 记录一次。

正常状态原点按以下优先级计算：

```text
PatrolRoute.Count >= 1
    -> NormalOrigin = PatrolRoute[0]

PatrolRoute.Count == 0
    -> NormalOrigin = StartupHome
```

巡逻点存在时，第一个点同时承担正常状态原点语义。没有巡逻点时，启动位置是唯一兜底原点。

### 共享事实

黑板新增或明确：

```text
NeedsReturnHome
```

`NeedsReturnHome` 是跨层共享事实，由警戒或战斗记忆退出流程写入，由 NormalRoutine 在完成返程后清除。

禁止用“当前距离原点多远”替代该事实。正常巡逻本身会离开原点，按距离判断会让敌人错误中断巡逻并反复返程。

### NormalRoutine 运行时状态

每个敌人的 NormalRoutine 运行时节点独立保存：

```text
phase
patrolIndex
waitRemaining
isInitialized
```

`patrolIndex`、`waitRemaining` 和阶段状态不写入 ScriptableObject 资产，避免多个敌人共享运行进度。

## 运行阶段

```text
Uninitialized
ReturningOrigin
WaitingAtOrigin
MovingToWaypoint
WaitingAtWaypoint
IdleAtOrigin
```

### Uninitialized

首次进入正常层时读取巡逻路线并确定正常状态原点：

- 0 个点：如果敌人已经位于启动原点，进入 `IdleAtOrigin`；否则进入 `ReturningOrigin`。
- 1 个点：目标设为第一个点，进入 `ReturningOrigin`。
- 2 个及以上：`patrolIndex = 0`，先移动或确认到达第一个点，然后进入等待阶段。

首次启动不会设置 `NeedsReturnHome`。NormalRoutine 自己完成初始对位。

### ReturningOrigin

进入条件：

- `NeedsReturnHome` 为真；或
- NormalRoutine 首次启动但敌人尚未到达正常状态原点。

行为：

- 清理追击或搜索遗留的移动目的地。
- 播放普通移动动画并移动到 `NormalOriginPosition`。
- 到达前返回 Running。
- 到达后停止移动并恢复 `NormalOriginRotation`。
- 重置 `patrolIndex = 0`。
- 进入 `WaitingAtOrigin`，不重复触发 ReturningOrigin。

返程过程中如果出现警戒、战斗或中断事实，外层响应式节点立即重置 NormalLayer 并切换高优先级层。

### WaitingAtOrigin

行为：

- 停止移动并播放普通待机动画。
- 保持 `NormalOriginRotation`。
- 使用统一的 `patrolWaitDuration` 倒计时。
- 等待完成后清除 `NeedsReturnHome`。
- 巡逻点数量小于等于 1 时进入 `IdleAtOrigin`。
- 巡逻点数量大于等于 2 时设置 `patrolIndex = 1` 并进入 `MovingToWaypoint`。

首次启动且已经位于原点时也进入本阶段，但此时 `NeedsReturnHome` 原本就是假；完成等待只负责选择后续待机或巡逻阶段。

### WaitingAtWaypoint

行为：

- 停止移动。
- 播放普通待机动画。
- 朝当前巡逻点配置的旋转方向转向。
- 使用统一的 `patrolWaitDuration` 倒计时。
- 倒计时结束后推进巡逻索引。

普通巡逻循环到第一个点时也使用本阶段，但不得写入 `NeedsReturnHome`。

### MovingToWaypoint

行为：

- 播放普通移动动画。
- 向 `PatrolRoute[patrolIndex]` 移动。
- 到达前返回 Running。
- 到达后切换到 `WaitingAtWaypoint`。

到达判定复用 Movement 的停止距离，不另设一套硬编码距离。

### IdleAtOrigin

只在巡逻点数量小于等于 1 时使用：

- 停止移动。
- 保持正常状态原点旋转。
- 持续播放普通待机动画。
- 持续返回 Running。

## 巡逻点数量语义

### 0 个巡逻点

```text
NormalOrigin = AI 启动位置
返回原点 -> 恢复启动旋转 -> 持续待机
```

### 1 个巡逻点

```text
NormalOrigin = PatrolRoute[0]
移动或返回该点 -> 使用该点旋转 -> 持续待机
```

单点路线不执行“离开后再返回同一点”的伪巡逻。

### 2 个及以上巡逻点

```text
NormalOrigin = PatrolRoute[0]
0 -> 等待 -> 1 -> 等待 -> ... -> 最后一个 -> 等待 -> 0
```

追击或搜索结束后，无论之前巡逻到哪个点，都先返回 `PatrolRoute[0]`，然后从路线开头重新循环。

## 返程请求规则

以下情况设置 `NeedsReturnHome = true`：

- 警戒记忆到期，警戒层退出到正常层。
- 警戒搜索主动结束，警戒层退出到正常层。
- 战斗目标清除且没有有效警戒记忆，战斗层直接退出到正常层。

以下情况不设置返程请求：

- 正常巡逻期间发生受击或失衡，中断结束后仍无警戒或战斗事实。
- 正常层被临时暂停、对象被禁用后又恢复，但没有发生目标记忆退出。
- 初次启动 AI。

中断结束后，响应式根节点重新选择当前层。如果 `NeedsReturnHome` 为假，NormalRoutine 保留当前巡逻点索引并继续正常循环；等待计时可以重新开始，不恢复中断前的剩余等待时间。

## NormalRoutine 返回值

- 正常层 Guard 有效且节点正常运行：始终返回 Running。
- 缺少必需的 Movement 组件：记录 Error 并返回 Failure。
- 巡逻路线包含空引用：配置校验失败，AI 不进入运行；NormalRoutine 不跳过空点。
- 外部 Reset：停止使用当前移动/等待阶段，但不自行写入或清除 `NeedsReturnHome`。
- 下次 Tick：依据 `NeedsReturnHome`、巡逻点数量和保留的 `patrolIndex` 重建当前阶段。

NormalRoutine 不返回 Success，避免根行为树把“完成一次移动或等待”误认为正常状态层已经退出。

## 配置

新增或明确配置：

```text
patrolWaitDuration
```

建议放入 `EnemyMovementConfig`，作为敌人类型的移动节奏参数。约束：

- 必须大于等于 0。
- 为 0 时，到达路点后下一 Tick 直接推进，不执行可见停留。
- 所有巡逻点第一版共用同一停留时间，不增加逐点配置组件。

巡逻路线继续由场景实例 `EnemyAgent.PatrolRoute` 提供，因为路点 Transform 属于场景数据，不应写入 EnemyDefinition 资产。

## 资产设计

剑盾敌人的正常层资源建议为：

```text
SwordAndShieldEnemy/Normal/
├─ NormalLayer.asset
├─ HasNoCombatTarget.asset
├─ HasNoAlertMemory.asset
└─ NormalRoutine.asset
```

不再为剑盾敌人的正常层创建独立 Idle 和 Patrol 分支。现有通用 Idle/Patrol 资产只有在确认没有其他行为树引用后才能删除。

## 数据更新顺序

```text
AIController.StartAI
  -> 记录 StartupHome
  -> 解析 PatrolRoute
  -> 计算 NormalOrigin

AIController.TickAI
  -> 更新中断、战斗和警戒事实
  -> 更新 NeedsReturnHome
  -> Tick 根行为树
  -> NormalLayer Guard
  -> NormalRoutine
```

记忆系统负责提出返程请求，NormalRoutine 只负责执行和完成返程，禁止双方各自维护第二套“是否应该回家”判断。

## 错误处理

- 巡逻路线允许为空，但不允许包含空元素。
- 正常状态原点必须能被导航系统到达；无法寻路时 Movement 返回明确失败，NormalRoutine 记录错误并返回 Failure。
- Movement 缺失属于敌人配置错误，初始化阶段直接失败。
- Animation 缺失属于敌人配置错误，不静默省略待机或移动表现。
- 运行时巡逻点被销毁时立即报告配置错误，不自动缩短或重排路线。

## 测试方案

### EditMode

- 0 个巡逻点时使用启动位置作为 NormalOrigin。
- 1 个巡逻点时移动到该点后持续待机，不重复发起移动。
- 多个巡逻点时按数组顺序循环，并在每个点等待。
- 多点路线的第一个点同时作为返程原点。
- `NeedsReturnHome` 成立时重置索引并先返回第一个点。
- 正常巡逻期间受击后恢复，不会因为远离原点而触发返程。
- CombatTarget 或 AlertMemory 出现时 NormalLayer Guard 立即失败并重置 NormalRoutine。
- NormalRoutine 在有效正常层中持续返回 Running，不返回 Success。
- 路线包含空引用时校验失败。

### Runtime

- 无巡逻路线的敌人离开追击后返回出生位置并待机。
- 单巡逻点敌人离开追击后返回该点并待机。
- 多巡逻点敌人离开追击后返回第一个点，再从头巡逻。
- 敌人在返程或巡逻过程中发现玩家，可以立即切换警戒或战斗层。
- 敌人在巡逻中受击并完成受击反应后，可以继续原路线。

## 验收标准

- NormalLayer 只包含层级 Guard 和一个 NormalRoutine，不存在独立 Idle/Patrol 状态分支。
- 0、1、多个巡逻点的行为符合本文定义。
- 第一个巡逻点优先作为正常状态原点；没有巡逻点时使用启动位置。
- 追击或搜索结束后必须返回正常状态原点。
- 正常巡逻不会因为远离原点而错误触发返程。
- 临时中断不会无条件重置巡逻路线，目标记忆退出会重置到第一个点。
- NormalRoutine 在正常层有效期间持续返回 Running。
