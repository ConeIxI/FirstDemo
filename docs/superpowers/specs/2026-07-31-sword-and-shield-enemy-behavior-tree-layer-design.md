# 剑盾敌人行为树分层设计

日期：2026-07-31

状态：已确认

## 背景

当前剑盾敌人的行为树以一个记忆型 Selector 平铺死亡、失衡、受击、追击、战斗决策、目标记忆和巡逻分支。子节点返回 `Running` 后，根节点会继续当前索引，不会重新检查更高优先级分支，因此节点排列不能形成真正的中断优先级。同时，目标引用、目标可见性、最后已知位置和目标记忆共用一组事实，正常、警戒和战斗的边界不清晰。

本设计把敌人行为划分为正常、警戒、战斗和中断四个互斥层。层之间没有固定流程顺序，由当前黑板事实决定激活哪一层。

## 与现有设计的关系

本设计延续 `2026-07-11-enemy-behavior-tree-only-design.md` 确定的“敌人只使用行为树、不恢复敌人 FSM”方向。仅在剑盾敌人范围内，本设计取代该旧文档中“不重建现有行为树的选择器/序列结构”这一非目标，允许重建根节点和子树层级。其他敌人的行为树不会因本方案自动迁移。

通用行为树的现有 Selector 和 Sequence 继续遵守 `2026-07-09-behavior-tree-framework-design.md` 定义的记忆型语义。本设计通过新增节点扩展框架，不改变旧节点契约。

## 目标

- 明确定义正常、警戒、战斗和中断四层的进入、循环和退出条件。
- 使用响应式根节点保证死亡和失衡能够抢占其他层，并由统一中断入口按动作锁规则调度普通受击。
- 分离战斗目标和警戒记忆；只有有效 CombatTarget 允许读取目标实时位置，警戒层只使用最后已知位置。
- 让战斗目标记忆和警戒记忆拥有独立倒计时。
- 保留现有 ScriptableObject 行为树架构，不恢复敌人 FSM。
- 不改变现有普通 Selector 和 Sequence 的运行语义。

## 非目标

- 不引入 GOAP 或新的通用 FSM 框架。
- 不设计多目标仇恨、队伍协作、声音感知或阵营系统。
- 不重写伤害、动画、技能和导航系统。
- 不在本次设计中调整攻击欲望、防御率等具体战斗数值。
- 不让行为树直接处理伤害结算、动画帧事件或技能命中。

## 核心事实

第一版继续使用单目标模型，但必须区分以下事实：

```text
VisibleCandidate
    当前感知帧发现的候选玩家，只用于更新记忆，不代表战斗目标。

CombatTarget
    已确认的战斗目标 Transform。仅满足战斗确认规则时建立或刷新。

CombatMemoryRemaining
    战斗目标剩余记忆时间。到期后清空 CombatTarget。

HasAlertMemory
    是否存在有效警戒记忆。

AlertLastKnownPosition
    警戒层可以使用的最后已知位置。

AlertMemoryRemaining
    警戒记忆剩余时间。到期后请求警戒退出。

IsAlertExitPending
    警戒记忆已经清除，但警戒层仍需完成收武器动作。
```

`CombatTarget` 有效期间不自动切换到其他候选目标。只有当前目标死亡、销毁、失活或记忆到期后，才允许建立新的战斗目标。

CombatTarget 有效期间，战斗层允许读取目标实时位置并持续追踪，不受可见性限制。CombatTarget 清除后，警戒层移动和搜索只能读取 `AlertLastKnownPosition`，不得继续读取目标 Transform。

## 记忆更新规则

### 警戒记忆

以下事件建立或刷新警戒记忆：

- 敌人看见玩家，无论玩家是否位于战斗范围内。
- 玩家攻击敌人，无论攻击者是否位于战斗范围内。
- 战斗目标在当前帧被再次确认。

刷新时写入玩家当前已确认的位置，并重置 `AlertMemoryRemaining`。倒计时结束后清除警戒记忆并设置 `IsAlertExitPending`，由警戒层完成收武器动作后退出。

### 战斗记忆

以下事件建立或刷新战斗目标：

- 敌人看见玩家，并且玩家在敌人的战斗范围内。
- 玩家攻击敌人，并且攻击者在敌人的战斗范围内。
- 敌人处于警戒活动期间受到玩家攻击，不检查攻击者是否位于战斗范围内。
- 已有 CombatTarget 再次攻击敌人时刷新当前战斗记忆，不检查距离。

建立或刷新时设置 `CombatTarget`，同步更新警戒记忆，并重置 `CombatMemoryRemaining`。

玩家离开战斗范围或暂时不可见时，不立即退出战斗层。CombatTarget 有效期间敌人继续读取目标实时位置并追踪，但仅实时追踪不会刷新战斗记忆。战斗倒计时结束时先缓存目标当前实时位置，再清空 `CombatTarget`，并以缓存位置建立一段新的完整警戒记忆，随后进入警戒层。

目标死亡、销毁或失活时立即清空 `CombatTarget`，不等待倒计时。清空后仍按照警戒记忆是否有效决定下一层。

处理攻击事件时必须在写入本次警戒记忆前读取攻击发生前的警戒事实。正常状态第一次受到范围外攻击时只进入警戒层；已经处于警戒记忆或警戒退出阶段时再次受玩家攻击，才无视范围建立战斗目标。

记忆倒计时在中断层运行期间继续推进。受击事件若满足战斗确认条件，可以在进入受击中断的同一帧刷新战斗记忆。

## 顶层行为树

根节点使用新增的 `ReactivePrioritySelectorNodeAsset`，不使用现有记忆型 `SelectorNodeAsset`，也不修改现有 Selector 的全局语义。

```text
RootReactivePrioritySelector
├─ InterruptExecutor
├─ CombatLayerReactiveSequence
│  ├─ HasCombatTarget
│  ├─ EnsureCombatStance
│  └─ RepeatForever
│     └─ CombatDistanceSelector
├─ AlertLayerReactiveSequence
│  ├─ HasNoCombatTarget
│  ├─ ShouldRunAlertLayer
│  └─ AlertRoutine
└─ NormalLayerReactiveSequence
   ├─ HasNoCombatTarget
   ├─ HasNoAlertMemory
   └─ NormalRoutine
```

树中的排列表示每帧判定优先级，不表示状态必须按顺序流转：

1. 中断事实成立时，统一中断执行节点按死亡、失衡、受击的优先级抢占当前层。
2. 没有中断且战斗目标有效时，执行战斗层。
3. 没有战斗目标，且警戒记忆或警戒退出握手有效时，执行警戒层。
4. 其他情况执行正常层。

## 响应式组合节点

### ReactivePrioritySelector

`ReactivePrioritySelectorNodeAsset` 每帧从第一个子节点开始检查，不保存“从当前索引继续”的选择器进度。

- 子节点返回 `Failure`：继续检查下一个子节点。
- 子节点返回 `Running`：如果它与上一运行子节点不同，先 `Reset` 旧节点，再记录新节点并返回 `Running`。
- 子节点返回 `Success`：如果它与上一运行子节点不同，先 `Reset` 旧节点；随后清除运行记录并返回 `Success`。
- 上一运行子节点本帧返回 `Failure`：先 `Reset` 该节点，再继续检查低优先级子节点。
- 所有子节点失败：重置旧运行子节点并返回 `Failure`。
- 外部调用 `Reset`：每个子节点只重置一次，并清除当前运行子节点记录。

该节点只新增，不替换现有 `SelectorNodeAsset`，避免破坏其他行为树依赖的记忆型选择语义。

### ReactiveSequence

`ReactiveSequenceNodeAsset` 每帧从第一个子节点重新执行，用于持续检查层级守卫。

- 前置条件返回 `Failure`：立即重置后续正在运行的层内行为并返回 `Failure`。
- 子节点返回 `Running`：返回 `Running`，但下一帧仍从第一个条件重新检查。
- 全部子节点成功：返回 `Success`。

ReactiveSequence 的 Running 子节点之前只允许放无副作用的条件或幂等步骤，避免每帧重复执行一次性动作。普通 Sequence 继续用于一次动作内部的顺序步骤；层级有效性检查必须使用 ReactiveSequence。

### RepeatForever

`RepeatForeverNodeAsset` 用于表达“层级有效期间持续循环”，避免层内攻击、搜索步骤或巡逻动作完成后把整个层误判为退出。

- 子节点返回 `Running`：原样返回 `Running`。
- 子节点返回 `Success`：重置子节点并返回 `Running`，下一帧开始新一轮层内决策。
- 子节点返回 `Failure`：重置子节点并向上返回 `Failure`，不吞掉配置错误或缺失行为。
- 外层 ReactiveSequence 的 Guard 失效：外层负责重置 RepeatForever 及其当前子节点，随后退出该层。

RepeatForever 只负责重复正常完成的层内行为，不决定状态层是否有效。状态退出条件始终由外层 Guard 和黑板事实控制。每个层内 Selector 必须提供合法兜底行为，正常配置下不得以“没有可执行行为”为由返回 Failure。

## 各层状态协议

### 正常层

详细实现以 `2026-07-31-sword-and-shield-enemy-normal-layer-design.md` 为准。

进入条件：

- 没有死亡、失衡或受击中断。
- `CombatTarget` 无效。
- 警戒记忆无效。

循环行为：

- NormalRoutine 根据巡逻点数量统一处理返程、待机和巡逻，不再拆分 Idle/Patrol 状态。
- 0 个巡逻点使用 AI 启动位置作为原点并待机。
- 1 个巡逻点使用该点作为原点并待机。
- 2 个及以上巡逻点使用第一个点作为原点并循环全部路线。
- 追击或搜索结束后先返回正常状态原点，再恢复正常循环。
- NormalRoutine 在正常层有效期间持续返回 `Running`。

退出条件：

- 发现玩家或受到玩家攻击，建立警戒记忆后进入警戒层。
- 同一事件满足战斗确认规则时，可以直接进入战斗层，不强制经过警戒层。
- 出现可执行的受击、失衡或死亡事实时进入中断层。

### 警戒层

详细实现以 `2026-07-31-sword-and-shield-enemy-alert-layer-design.md` 为准。

进入条件：

- 没有有效 `CombatTarget`。
- `HasAlertMemory` 或 `IsAlertExitPending` 为真。

循环行为：

- 进入警戒时拔出剑盾；从战斗层降级且武器已经拔出时跳过拔刀。
- 朝 `AlertLastKnownPosition` 移动。
- 抵达后观察最后已知位置，并依次检查有限周边搜索点。
- 不读取不可见玩家的实时 Transform 位置。
- 最后已知位置刷新时废弃旧搜索点，从新位置重新调查。
- AlertRoutine 在警戒层有效期间持续返回 `Running`。

退出条件：

- 看见战斗范围内玩家并建立 `CombatTarget`，进入战斗层。
- 警戒状态下受到玩家攻击，无视战斗范围建立 `CombatTarget`，进入战斗层。
- 警戒记忆到期或有限搜索完成时清除警戒记忆，设置 `IsAlertExitPending` 并收起剑盾。
- 收武器完成后清除退出握手，设置 `NeedsReturnHome` 并进入正常层。
- 出现可执行的受击、失衡或死亡事实时进入中断层；后撤期间普通受击不生成中断表现。

### 战斗层

详细实现以 `2026-07-31-sword-and-shield-enemy-combat-layer-design.md` 为准。

进入条件：

- 敌人看见玩家且玩家位于战斗范围内。
- 正常状态下受到玩家攻击且攻击者位于战斗范围内。
- 警戒状态下受到玩家攻击，不检查战斗范围。
- 上述确认事件已经建立有效 `CombatTarget`。

循环行为：

```text
CombatDistanceSelector
├─ ChaseSequence
│  ├─ IsBeyondChaseRange
│  └─ ChaseCombatTarget
├─ ApproachSequence
│  ├─ IsInChaseRange
│  ├─ IsOutsideAttackRange
│  └─ ApproachCombatTarget
└─ InAttackRangeSequence
   ├─ IsInAttackRange
   └─ ExistingCombatDecisionSelector
      ├─ RetreatDecisionSequence
      ├─ DefenseDecisionSequence
      ├─ AttackSequence
      ├─ CombatIdleSequence
      └─ CombatTargetHold
```

- CombatTarget 有效时，战斗层始终使用目标实时位置，不受视野范围限制。
- 目标距离大于追击范围时高速追击；位于攻击范围外、追击范围内时普通接近；进入攻击范围后执行现有战斗决策。
- 正常层直接进入战斗时由 EnsureCombatStance 拔出剑盾，警戒层进入时复用已有战斗姿态。
- 玩家离开战斗范围不会直接退出战斗层，也不会仅因实时追踪而刷新战斗记忆。
- 现有范围内决策全部失败时 CombatTargetHold 提供合法兜底。
- CombatDistanceSelector 由 RepeatForever 包裹，单次行为完成后重新进行距离判断，战斗层激活期间持续返回 `Running`。
- 本阶段只完成分层和距离行为，现有攻击、防御、后撤、待机和配置继续复用；内部 CombatRoutine 重构后续单独实施。

退出条件：

- `CombatMemoryRemaining` 到期，缓存目标实时位置、清空 `CombatTarget` 并建立新的完整警戒记忆。
- 目标死亡、销毁或失活，立即清空 `CombatTarget`。
- 目标失效清空后，警戒记忆有效则进入警戒层；否则通过警戒退出握手收武器并进入正常层。
- 出现可执行的受击、失衡或死亡事实时进入中断层；后撤期间普通受击不生成中断表现。

### 中断层

详细实现以 `2026-07-31-sword-and-shield-enemy-interrupt-layer-design.md` 为准。

根节点只保留一个统一中断入口：

```text
InterruptExecutor
├─ DeadSequenceRuntime
├─ UnbalanceSequenceRuntime
└─ GetHitSequenceRuntime
```

中断优先级固定为：

```text
死亡 > 失衡 > 受击 > 当前普通层
```

统一节点负责优先级、抢占、最新受击缓存、子树切换和生命周期；三个现有 Sequence 继续负责一次完整表现。

关键规则：

- 没有中断时 `InterruptExecutor` 返回 `Failure`；受击或失衡执行期间返回 `Running`。
- 连续受击不重启当前动画，只保留最新一条待处理受击；当前动画结束后直接执行最新受击。
- 失衡抢占受击并清空全部受击数据；失衡期间普通攻击只结算伤害和更新目标记忆。
- 后撤期间普通受击不打断、不缓存表现；失衡和死亡仍可抢占后撤。
- 死亡立即抢占全部状态并永久保持死亡终态。
- 受击或失衡结束后不恢复旧行为节点索引，由根节点根据当前事实重新选层。
- 非死亡中断期间战斗记忆和警戒记忆继续推进。

## 状态迁移

```text
正常 --发现/受击且目标在战斗范围外--> 警戒
正常 --发现/受击且目标在战斗范围内--> 战斗
警戒 --确认目标进入战斗范围--> 战斗
警戒 --受到玩家攻击，不检查战斗范围--> 战斗
警戒 --警戒记忆到期或搜索结束--> 收武器并进入正常
战斗 --战斗记忆到期并建立新警戒记忆--> 警戒
战斗 --目标失效且无警戒记忆--> 收武器并进入正常
任意非死亡层 --可执行受击/失衡--> 中断
后撤 --普通受击--> 只结算伤害和更新记忆
后撤 --失衡--> 中断
任意层 --死亡--> 死亡中断
受击/失衡结束 --重新评估事实--> 战斗、警戒或正常
```

## 数据更新顺序

每帧更新顺序固定为：

```text
AIController.TickAI
  1. 感知组件采集当前可见候选
  2. 战斗/生命组件提交本帧受击、失衡和死亡事件
  3. 在写入新警戒记忆前读取攻击发生前的警戒事实
  4. 更新 CombatTarget、战斗记忆、警戒记忆和警戒退出请求
  5. CombatTarget 有效时按实时 Transform 刷新距离、攻击范围、战斗范围和追击范围事实
  6. 战斗记忆到期时缓存实时位置并建立新的警戒记忆
  7. BehaviorTreeRunner.Tick
  8. 响应式根节点选择当前层
  9. 层内行为操作 Movement / Animation / Combat
```

所有节点只读取同一帧已经完成刷新的黑板事实。记忆的建立、刷新和过期只由统一记忆组件或黑板入口负责，行为节点不得自行维护第二套倒计时。

## 配置

敌人定义中的战斗与感知配置新增或明确以下字段：

```text
combatMemoryDuration：战斗目标记忆持续时间
alertMemoryDuration：战斗目标清除后或普通发现后的警戒记忆持续时间
chaseRange：CombatTarget 触发高速追击的距离阈值
searchRadius：最后已知位置周边的搜索半径
searchPointCount：周边搜索点最大数量
searchObservationDuration：最后已知位置和每个搜索点的观察时间
```

约束：

- `combatMemoryDuration` 必须大于等于零，`alertMemoryDuration` 必须大于零。
- 战斗范围由现有战斗配置提供，记忆系统不得另设重复范围。
- 攻击范围、战斗范围、追击范围和视野范围必须严格递增。
- `searchPointCount` 必须大于等于零；大于零时 `searchRadius` 必须大于零。
- `searchObservationDuration` 必须大于等于零。
- 警戒记忆是搜索的最长时间；有限搜索主动结束时允许提前清除警戒记忆。

## 错误处理

- `CombatTarget` 为已销毁、失活或死亡对象时立即清理，不吞没异常或继续执行移动。
- 层级资产缺少必要 Guard、Loop 或 Routine 子节点时，初始化失败并记录 Error。
- 记忆持续时间配置为负数时，定义校验失败，不在运行时静默修正。
- 没有巡逻路线时正常层明确执行 Idle，不创建空移动目标。
- 移动、动画或战斗组件缺失时由对应动作节点快速失败，并记录可定位的配置错误。

## 资产调整建议

剑盾敌人行为树资源按层级拆分：

```text
SwordAndShieldEnemy/
├─ SwordAndShieldEnemyBehaviorTree.asset
├─ Layers/
│  ├─ CombatLayer.asset
│  ├─ AlertLayer.asset
│  └─ NormalLayer.asset
├─ Interrupt/
│  └─ InterruptExecutor.asset
├─ Combat/
│  ├─ CombatLayer.asset
│  ├─ HasCombatTarget.asset
│  ├─ EnsureCombatStance.asset
│  ├─ CombatDistanceSelector.asset
│  ├─ ChaseSequence.asset
│  ├─ ChaseCombatTarget.asset
│  ├─ ApproachSequence.asset
│  ├─ ApproachCombatTarget.asset
│  ├─ InAttackRangeSequence.asset
│  ├─ ExistingCombatDecisionSelector.asset
│  └─ CombatTargetHold.asset
├─ Alert/
│  ├─ AlertLayer.asset
│  ├─ HasNoCombatTarget.asset
│  ├─ ShouldRunAlertLayer.asset
│  └─ AlertRoutine.asset
└─ Normal/
   ├─ NormalLayer.asset
   ├─ HasNoCombatTarget.asset
   ├─ HasNoAlertMemory.asset
   └─ NormalRoutine.asset
```

统一中断入口继续引用 `BehaviorTrees/Common` 中的死亡、失衡和受击 Sequence。迁移完成后，删除剑盾敌人根树中不再使用的旧平铺中断入口，不复制公共表现资产，也不保留两套行为入口。

## 测试方案

### 通用行为树节点测试

- ReactivePrioritySelector 每帧从第一个子节点开始检查。
- 低优先级子节点运行时，高优先级条件成立会在同一 Tick 抢占。
- 抢占时旧运行子节点只被 Reset 一次。
- ReactiveSequence 在 Guard 失效时重置正在运行的后续节点。
- RepeatForever 在子行为成功后重置子行为并保持层节点为 Running，子行为失败时向上传播 Failure。
- 外层 Guard 失效时，RepeatForever 不得继续启动下一轮行为。
- 现有 Selector 和 Sequence 的记忆型行为保持不变。

### 记忆测试

- 看见战斗范围外玩家只刷新警戒记忆。
- 看见战斗范围内玩家同时建立战斗目标并刷新两种记忆。
- 正常状态被范围内攻击时建立战斗目标。
- 正常状态第一次被范围外攻击时只建立警戒记忆。
- 警戒状态被玩家攻击时无视战斗范围建立战斗目标。
- 当前 CombatTarget 再次攻击敌人时无视距离刷新战斗记忆。
- 攻击事件在写入新警戒记忆前读取攻击发生前的警戒事实。
- 目标不可见或离开战斗范围且没有再次攻击敌人时，战斗记忆正常倒计时且不刷新。
- 仅实时追踪目标位置不会刷新战斗记忆。
- 战斗记忆到期后清空目标，并以目标当前实时位置建立新的完整警戒记忆。
- 目标销毁、死亡或失活时立即清空战斗目标。
- 警戒记忆到期或搜索完成后进入退出握手，收武器完成后设置返程请求。

### 层级迁移测试

- 正常层可以直接进入警戒层或战斗层。
- 战斗记忆到期后必定进入警戒层；目标失效时按照警戒记忆或退出握手进入警戒层或正常层。
- 受击和失衡结束后重新评估事实，不恢复旧节点索引。
- 攻击、追击、防御或后撤处于 Running 时，死亡能在下一 Tick 抢占。
- 普通受击不能抢占后撤，且后撤结束后不补播受击；失衡可以抢占后撤。
- 警戒层不会读取不可见目标实时位置；CombatTarget 有效时战斗层允许读取实时位置。
- 行为树资产根节点和四层子树引用完整、顺序正确。

### Runtime 验收

- 玩家在战斗范围外暴露后，敌人进入警戒并走向最后已知位置。
- 玩家在战斗范围内暴露或正常状态下受到范围内攻击后，敌人直接进入战斗。
- 正常状态第一次受到范围外攻击时进入警戒；警戒状态再次受玩家攻击时无视距离进入战斗。
- 玩家离开后，敌人保持战斗直至战斗记忆到期，再进入警戒搜索。
- 玩家离开视野范围但 CombatTarget 有效时，敌人仍追踪实时位置。
- 玩家超过追击范围时高速追击，回到追击范围后转为普通战斗接近。
- 警戒记忆到期或有限搜索完成后，敌人收起剑盾并返回巡逻或待机原点。
- 普通战斗、警戒和正常行为期间，受击、失衡和死亡表现均可正确抢占。
- 后撤期间普通受击只结算伤害，失衡和死亡仍可正确抢占。

## 验证要求

实现完成后必须执行：

```text
$CLI compile unity
$CLI get_logs --logType Error
```

`compile dotnet` 只能作为额外检查，不能替代 Unity 编译。Runtime 验收需要在包含剑盾敌人的测试场景中采集实际行为证据。

## 验收标准

- 根节点使用新增的响应式优先选择器，现有普通 Selector/Sequence 行为不受影响。
- 四个层的进入和退出完全由黑板事实驱动，不依赖节点排列形成隐式状态。
- 战斗目标和警戒记忆相互独立，拥有独立倒计时。
- 战斗记忆到期时以目标实时位置建立新的完整警戒记忆并进入警戒层。
- 警戒记忆和退出握手相互分离，记忆清除后仍能完成收武器动作。
- 警戒状态受玩家攻击时不检查战斗范围，正常状态第一次范围外受击仍只进入警戒。
- 根节点只有一个统一中断入口，死亡、失衡和受击表现复用现有公共 Sequence。
- 死亡和失衡可以抢占所有非死亡行为；普通受击遵守后撤动作锁，不打断也不延迟补播。
- 只有有效 CombatTarget 允许读取目标实时位置，警戒层只使用最后已知位置。
- 旧平铺行为入口完成迁移后被清理，不保留重复真相源。
