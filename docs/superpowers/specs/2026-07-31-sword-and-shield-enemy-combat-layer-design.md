# 剑盾敌人战斗状态层设计

日期：2026-07-31

状态：已确认

## 与总设计的关系

本文是 `2026-07-31-sword-and-shield-enemy-behavior-tree-layer-design.md` 的战斗状态层详细设计。总设计负责四层优先级、战斗目标和警戒记忆的公共边界；本文负责 CombatLayer 的范围分区、行为树结构、战斗记忆交接和本阶段实施边界。

如果两份文档对战斗层的描述不一致，以本文为准。

## 本阶段范围

本阶段只完成战斗层分层和距离行为重组，不重构现有攻击、防御、后撤、连段和技能选择内部实现。

本阶段实施：

- 建立独立 CombatLayer 入口和循环。
- 增加战斗姿态保证节点。
- 按攻击范围和追击范围拆分追击、接近和范围内决策。
- 允许有效 CombatTarget 使用目标实时位置。
- 增加追击范围配置和黑板事实。
- 明确战斗记忆刷新、到期和警戒层交接规则。
- 为现有战斗决策增加合法兜底，避免 CombatTarget 有效时错误退出战斗层。

本阶段不实施：

- 不拆分当前大型通用动作节点。
- 不实现 `CombatRoutine + 独立动作执行器` 重构。
- 不重构随机决策、技能权重、特殊技能或普通连段。
- 不改变 `canInterruptAttack` 的当前运行行为。
- 不删除或改名现有战斗配置字段。
- 不为后撤增加代码位移。

## 已确认决策

- 敌人使用攻击范围、战斗范围、追击范围和视野范围四种严格递增的范围。
- `CombatTarget` 有效期间，敌人始终可以读取目标实时 Transform 位置，不受视野范围限制。
- 战斗范围只用于从其他层确认并建立战斗目标。
- 追击范围是高速追击的触发阈值，目标距离大于追击范围时进入追击。
- 攻击范围外、追击范围内时执行普通战斗接近。
- 只有进入攻击范围后才允许执行攻击、防御、后撤和战斗待机决策。
- 正常层直接进入战斗时，由战斗层负责拔出剑盾。
- 从警戒层进入战斗且剑盾已经拔出时，不重复播放拔刀动画。
- 当前战斗目标再次攻击敌人时，无视距离刷新战斗记忆。
- 战斗记忆到期时，以目标最后一个实时位置建立一段新的完整警戒记忆。
- 后撤只播放动画，不执行行为代码位移。
- 后撤不能被普通战斗动作或普通受击打断；失衡和死亡仍可抢占。
- 现有攻击、防御、后撤、待机和全部配置在本阶段保留。

## 目标

- 让 CombatTarget 成为战斗层唯一进入和退出事实。
- 让四种范围各自只有一个明确职责。
- 保证目标跨越距离边界时，战斗层能够在追击、接近和范围内决策之间循环切换。
- 保证 CombatTarget 有效期间战斗层始终返回 Running，不因现有概率分支全部失败而退出。
- 保证战斗记忆到期后必然进入警戒层调查，而不是直接回到正常层。
- 在不提前重构战斗内部逻辑的前提下，为后续重构建立稳定层级边界。

## 非目标

- 不在本阶段解决现有随机 Condition 逐轮重投问题。
- 不在本阶段实现玩家攻击前摇感知或事件驱动防御。
- 不调整攻击欲望、防御率、后撤欲望等具体数值。
- 不新增仇恨列表、多目标切换、队友协作或战斗脱离半径。
- 不让视野范围承担战斗追踪或战斗退出职责。
- 不在行为树内维护第二套战斗记忆倒计时。

## 四种范围

四种范围必须满足：

```text
AttackRange < CombatRange < ChaseRange < VisionRange
```

### 攻击范围

`AttackRange` 表示敌人能够执行攻击动作的距离。

- 目标进入攻击范围后才允许范围内战斗决策。
- 目标离开攻击范围后不再启动新的攻击动作。
- 已经启动的动作继续遵守现有动作运行规则，本阶段不重构动作中断。

### 战斗范围

`CombatRange` 表示其他层通过普通发现或普通受击建立 CombatTarget 的距离。

- 正常层看见玩家且玩家进入战斗范围时建立 CombatTarget。
- 正常层受到战斗范围内玩家攻击时建立 CombatTarget。
- 警戒层受到玩家攻击时不检查战斗范围，直接建立 CombatTarget。
- CombatTarget 建立后，战斗范围不再决定战斗层是否退出。

### 追击范围

`ChaseRange` 是高速追击触发阈值。

- 目标距离大于追击范围时执行高速追击。
- 目标重新进入追击范围后结束高速追击，转为普通战斗接近。
- 追击范围不是最大追击边界，越过该范围不会立即清除 CombatTarget。

### 视野范围

`VisionRange` 只表示敌人能够通过视野发现玩家的距离。

- 视野范围由感知配置提供。
- 玩家位于视野范围内但战斗范围外时，只建立或刷新警戒记忆。
- CombatTarget 有效后，即使目标离开视野范围，战斗层仍能读取目标实时位置并继续追击。

## 配置归属

```text
EnemyCombatConfig
├─ defaultAttackRange
├─ combatEnterRange
├─ chaseRange
└─ canInterruptAttack

EnemyPerceptionConfig
└─ range
```

数值约束：

```text
0 < defaultAttackRange
defaultAttackRange < combatEnterRange
combatEnterRange < chaseRange
chaseRange < perception.range
```

本阶段只新增 `chaseRange`。`normalComboSkillIds`、`specialSkillIds`、`skillWeights`、`retreatDistance` 和其他现有字段全部保留。

配置违反范围顺序时定义校验失败，不在运行时静默交换或修正数值。

## 黑板事实

战斗层读取以下共享事实：

```text
CombatTarget
CombatMemoryRemaining
DistanceToTarget
IsInAttackRange
IsInCombatRange
IsInChaseRange
HasCombatStance
```

计算属性：

```text
IsOutsideAttackRange =
    CombatTarget != null && !IsInAttackRange

IsBeyondChaseRange =
    CombatTarget != null && !IsInChaseRange
```

范围事实由 AIController 在行为树执行前统一刷新。Condition 节点只读取黑板，不再分别调用距离计算。

没有 CombatTarget 时，距离和全部范围事实清零。CombatTarget 有效时，无论目标是否可见，都使用目标实时 Transform 刷新距离事实。

## 行为树结构

```text
CombatLayerReactiveSequence
├─ HasCombatTarget
├─ EnsureCombatStance
└─ RepeatForever
   └─ CombatDistanceSelector
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

节点职责：

- `HasCombatTarget`：CombatTarget 非空且有效时返回 Success。
- `EnsureCombatStance`：保证直接进入战斗层的敌人已经完成拔刀。
- `CombatDistanceSelector`：按距离选择高速追击、普通接近或范围内决策。
- `ExistingCombatDecisionSelector`：复用现有后撤、防御、攻击和战斗待机分支。
- `CombatTargetHold`：现有范围内决策全部不成立时的合法兜底。
- `RepeatForever`：单次行为正常完成后重新开始距离选择，并使战斗层保持 Running。

`CombatDistanceSelector` 和 `ExistingCombatDecisionSelector` 本阶段继续使用现有记忆型 Selector 语义。动作返回 Running 后保持当前分支，动作完成后由 RepeatForever 开始下一轮距离判断。

## EnsureCombatStance

`HasCombatStance = true` 时立即返回 Success，不播放重复拔刀动画。

`HasCombatStance = false` 时：

- 停止移动。
- 朝向 CombatTarget 实时位置。
- 播放进入战斗动画。
- 动画完成后显示剑盾。
- 设置 `HasCombatStance = true`。
- 返回 Success，允许后续战斗循环运行。

动画未完成前返回 Running。死亡、失衡或受击可以通过根节点中断层抢占。

CombatTarget 在拔刀期间失效时，外层 ReactiveSequence 立即 Reset 本节点并退出战斗层。

## 高速追击

### ChaseSequence

进入条件：

```text
CombatTarget != null
&& DistanceToTarget > chaseRange
```

`ChaseCombatTarget` 行为：

- 每帧读取 CombatTarget 实时位置。
- 播放 `runAnimation`。
- 持续向目标移动。
- 距离仍大于追击范围时返回 Running。
- 目标进入追击范围后停止本次高速追击并返回 Success。

目标离开视野范围不影响本分支。CombatTarget 到期或被清除时由外层 Guard 退出战斗层。

## 普通战斗接近

### ApproachSequence

进入条件：

```text
CombatTarget != null
&& DistanceToTarget <= chaseRange
&& DistanceToTarget > attackRange
```

`ApproachCombatTarget` 行为：

- 每帧读取 CombatTarget 实时位置。
- 播放 `moveAnimation`。
- 使用普通战斗移动接近目标。
- 距离仍在攻击范围外时返回 Running。
- 进入攻击范围后停止接近并返回 Success。

目标在接近期间再次超过追击范围时，本动作结束并让下一轮距离选择进入高速追击。

## 范围内战斗决策

### InAttackRangeSequence

只有 `IsInAttackRange` 为真时执行现有战斗决策。

本阶段决策优先级保持：

```text
Retreat
-> Defense
-> Attack
-> CombatIdle
-> CombatTargetHold
```

### 后撤

- 继续复用现有后撤判断和后撤动画。
- 后撤行为只停止导航并播放 `retreatAnimation`。
- 不调用 `MoveAwayFrom`、`MoveTo` 或外部位移。
- 动画资源自身的 RootMotion 由动画和 Animator 配置决定，行为树不施加位移。
- 后撤开始后保持 Running，动画结束后返回 Success。
- 攻击、防御、追击、接近和再次后撤都不能打断当前后撤。
- 普通受击只结算伤害和更新目标记忆，不打断后撤，也不在后撤结束后补播。
- 死亡和失衡仍可通过中断层抢占后撤。
- `retreatDistance` 本阶段保留，即使当前行为不消费该字段。

### 防御

- 继续使用 `defenseDuration` 启动限时防御。
- 防御期间停止移动并保持防御动作锁。
- 防御结束以防御计时为准，不要求动画长度完全一致。
- 防御结束后返回 Success，下一轮重新进行距离判断。
- 死亡、失衡和实际受击仍可由中断层抢占。

### 攻击

- 继续复用现有攻击冷却、普通连段和技能配置。
- 只有进入攻击范围后才允许现有 AttackSequence 被选择。
- 攻击动作开始后继续遵守现有 Running 和连段规则。
- 本阶段不重构 `normalComboSkillIds`、`specialSkillIds` 或 `skillWeights`。
- 本阶段不实现新的技能权重选择分支。

### 战斗待机

- 继续复用现有 CombatIdleSequence。
- 停止移动并朝向 CombatTarget。
- 播放现有战斗待机或左右移动表现。

### CombatTargetHold

当后撤、防御、攻击和现有战斗待机条件全部失败时：

- 停止移动。
- 朝向 CombatTarget 实时位置。
- 播放战斗待机表现。
- 返回 Success。

该节点是正常决策兜底，不负责吞掉配置错误。必要 Movement、Animation 或 Combat 组件缺失必须在初始化或节点执行时报告 Error。

## 动作锁与阶段性技术债

当前记忆型 Selector 会在动作返回 Running 后保持该动作，因此本阶段继续获得基础动作锁。

已经确认的后续目标规则为：

- 攻击、防御和后撤默认执行到结束。
- `canInterruptAttack = true` 时，明确成立的防御或后撤可以打断攻击。
- 防御和后撤不能被普通战斗决策打断。
- 死亡和失衡始终可以抢占普通战斗动作。
- 普通受击可以抢占后撤以外的普通战斗动作，后撤期间不生成受击表现。

上述完整配置式动作中断不在本阶段实现。本阶段不声称 `canInterruptAttack` 已经满足后续目标语义。

以下问题明确留到后续 `CombatRoutine + 独立动作执行器` 重构：

- 现有随机 Condition 可能按循环频繁重投。
- 攻击、防御和后撤的运行状态分散在大型通用动作节点中。
- `canInterruptAttack` 没有统一的行为树级调度入口。
- 技能权重和特殊技能配置尚未形成完整执行链。
- 现有攻击接近逻辑与新的距离分区仍存在职责重叠。
- `retreatDistance` 当前保留但不被动画型后撤消费。

## 战斗目标建立规则

以下事件建立 CombatTarget：

- 敌人看见玩家，并且玩家位于战斗范围内。
- 正常层受到玩家攻击，并且攻击者位于战斗范围内。
- 警戒层受到玩家攻击，不检查攻击者距离。

建立时：

- 写入 CombatTarget。
- 重置 CombatMemoryRemaining。
- 更新警戒记忆和已确认位置。
- 取消正在进行的警戒退出握手。

第一版继续使用单 CombatTarget。已有目标有效期间不自动切换到其他玩家。

## 战斗记忆刷新规则

以下事件刷新当前 CombatTarget 的战斗记忆：

- 看见当前 CombatTarget，并且目标位于战斗范围内。
- 当前 CombatTarget 再次攻击敌人，不检查距离。

以下情况不刷新战斗记忆：

- 只因为 CombatTarget 仍然存在。
- 只因为战斗层正在读取目标实时位置。
- 目标只位于追击范围或视野范围内，但没有发生有效刷新事件。

因此，敌人可以在 CombatTarget 记忆有效期间持续实时追踪目标，但目标长期不回到战斗范围且不再攻击敌人时，战斗记忆仍会正常到期。

## 战斗记忆到期交接

CombatMemoryRemaining 到期时，统一记忆逻辑执行：

```text
1. 缓存 CombatTarget 当前实时位置
2. 清除 CombatTarget
3. 清除战斗距离事实
4. 以缓存位置建立一段完整的警戒记忆
5. 重置 AlertMemoryRemaining = alertMemoryDuration
6. 保留 HasCombatStance = true
```

根节点随后选择 AlertLayer。警戒层跳过拔刀，从该实时位置开始调查。

该交接不依赖旧警戒记忆是否仍然有效，保证战斗记忆到期后始终保留一段独立警戒记忆。

## 目标失效交接

目标死亡、销毁或失活时立即清除 CombatTarget，不等待倒计时。

- 仍有有效警戒记忆时进入 AlertLayer。
- 没有警戒记忆时设置 `IsAlertExitPending`，由 AlertLayer 只执行收武器动作。
- 收武器完成后设置 `NeedsReturnHome` 并进入 NormalLayer。

已确认目标死亡时不强制创建新的完整警戒记忆。

## Reset 语义

CombatLayer 或当前战斗行为被外层响应式节点重置时：

- 停止追击和接近移动。
- 攻击中关闭命中窗口并取消当前技能。
- 防御中调用 `StopDefense`。
- 清除当前连段和动作运行进度。
- 清除节点内部的拔刀运行进度。
- 不自行清除 CombatTarget 或记忆倒计时。
- 不收起剑盾。
- 不设置 NeedsReturnHome。

受击或失衡中断结束后，只要 CombatTarget 仍有效，就重新按当前距离选择战斗分支，不恢复旧动作进度。

CombatTarget 到期或清除后的收武器和返程由警戒层退出握手处理。

## 数据更新顺序

```text
AIController.TickAI
  1. 感知系统更新可见性
  2. 伤害系统提交攻击、受击、失衡和死亡事件
  3. 统一目标记忆逻辑建立、刷新或推进 CombatTarget
  4. CombatTarget 有效时按实时 Transform 刷新距离和三种战斗范围事实
  5. CombatTarget 到期时缓存实时位置并建立新的警戒记忆
  6. BehaviorTreeRunner.Tick
  7. 根节点选择中断、战斗、警戒或正常层
  8. CombatLayer 操作 Movement、Animation 和 Combat
```

行为树节点不得自行倒计时或清除 CombatTarget。

## 资源设计

```text
SwordAndShieldEnemy/Combat/
├─ CombatLayer.asset
├─ HasCombatTarget.asset
├─ EnsureCombatStance.asset
├─ CombatDistanceSelector.asset
├─ ChaseSequence.asset
├─ IsBeyondChaseRange.asset
├─ ChaseCombatTarget.asset
├─ ApproachSequence.asset
├─ IsInChaseRange.asset
├─ IsOutsideAttackRange.asset
├─ ApproachCombatTarget.asset
├─ InAttackRangeSequence.asset
├─ ExistingCombatDecisionSelector.asset
└─ CombatTargetHold.asset
```

现有 `GuardMelee` 目录中的：

```text
RetreatDecisionSequence.asset
DefenseDecisionSequence.asset
AttackSequence.asset
CombatIdleSequence.asset
```

继续复用，不复制、不删除。剑盾敌人旧根节点中的平铺战斗引用迁移到 CombatLayer 后移除，但底层共享资产继续保留。

## 错误处理

- CombatLayer 缺少 HasCombatTarget、EnsureCombatStance、RepeatForever 或距离分支时，行为树初始化失败并记录 Error。
- 缺少 Movement、Animation 或 Combat 组件属于敌人配置错误，初始化或对应动作快速失败并记录 Error。
- CombatTarget 为死亡、销毁或失活对象时，由统一记忆逻辑立即清理。
- CombatTarget 有效但所有正常范围内决策不成立时，CombatTargetHold 负责合法兜底。
- 范围配置不满足严格递增关系时定义校验失败。
- 行为节点不得因为目标不可见而读取 AlertLastKnownPosition 代替 CombatTarget 实时位置。

## 测试方案

### EditMode

- 范围配置验证 `AttackRange < CombatRange < ChaseRange < VisionRange`。
- CombatTarget 有效时统一刷新距离、攻击范围、战斗范围和追击范围事实。
- CombatTarget 为空时所有距离事实清零。
- 正常层直接建立 CombatTarget 后，EnsureCombatStance 播放一次拔刀。
- 从警戒层进入战斗且 HasCombatStance 为真时跳过拔刀。
- 距离大于 chaseRange 时选择 ChaseSequence。
- 距离位于 attackRange 和 chaseRange 之间时选择 ApproachSequence。
- 距离进入 attackRange 后选择 ExistingCombatDecisionSelector。
- 现有范围内决策全部失败时 CombatTargetHold 返回 Success。
- RepeatForever 在单次分支成功后保持 CombatLayer 为 Running。
- 当前 CombatTarget 从任意距离攻击敌人时刷新战斗记忆。
- 仅实时追踪目标位置不会刷新战斗记忆。
- 战斗记忆到期时以目标实时位置建立完整警戒记忆。
- CombatLayer Reset 关闭命中窗口、停止防御和移动，但不清除跨层事实。
- 后撤动作不调用任何代码位移接口。

### Runtime

- 玩家从正常层进入战斗时，敌人先拔出剑盾再行动。
- 玩家从警戒层进入战斗时不重复拔刀。
- 玩家超过追击范围后，敌人使用高速追击。
- 玩家回到追击范围后，敌人切换为普通战斗接近。
- 玩家进入攻击范围后，现有攻击、防御、后撤和待机决策接管。
- 玩家离开视野范围但 CombatTarget 有效时，敌人仍追踪实时位置。
- 玩家长期未满足刷新条件时，敌人追击直到战斗记忆到期，再进入警戒层。
- 当前目标在任意距离再次攻击敌人时，战斗记忆被刷新。
- 受击、失衡和死亡可以抢占追击、接近、攻击、防御和战斗待机。
- 后撤只播放动画，普通战斗行为和普通受击不能打断；失衡和死亡可以打断。
- CombatTarget 有效期间不会因为概率分支全部失败而进入警戒或正常层。

## 验收标准

- CombatLayer 由 HasCombatTarget、EnsureCombatStance 和持续循环组成。
- 四种范围职责明确并满足严格递增约束。
- CombatTarget 有效期间允许读取目标实时位置，不依赖目标可见性。
- 高速追击、普通接近和范围内决策互斥且可以循环切换。
- 范围内决策继续复用现有战斗资产，不提前实施内部重构。
- CombatTargetHold 保证正常配置下战斗层不会错误 Failure。
- 当前目标再次攻击敌人时无视距离刷新战斗记忆。
- 战斗记忆到期后必定建立一段新的警戒记忆并进入警戒层。
- 后撤不施加代码位移，也不会被普通战斗行为打断。
- 后撤不会被普通受击打断或在结束后补播受击，失衡和死亡仍可抢占。
- 现有配置字段完整保留，只新增 chaseRange。
- 后续重构项在本文中明确记录，但不混入本阶段实施。
