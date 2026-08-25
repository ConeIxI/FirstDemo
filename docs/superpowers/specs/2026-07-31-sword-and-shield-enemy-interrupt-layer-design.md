# 剑盾敌人中断层设计

日期：2026-07-31

状态：已确认

## 与总设计的关系

本文是 `2026-07-31-sword-and-shield-enemy-behavior-tree-layer-design.md` 的中断层详细设计。总设计负责四层边界、目标记忆和响应式根节点；本文只定义死亡、失衡、受击的统一调度、抢占、缓存、Reset 和异常退出规则。

战斗层内部攻击、防御、后撤和距离决策继续以 `2026-07-31-sword-and-shield-enemy-combat-layer-design.md` 为准。本文新增的唯一战斗动作例外是：普通受击不能打断后撤，失衡和死亡仍可打断后撤。

## 目标

- 使用单个中断执行节点作为根节点的最高优先级入口。
- 固定死亡、失衡和受击之间的抢占规则。
- 复用现有 `DeadSequence`、`UnbalanceSequence` 和 `GetHitSequence` 资产。
- 连续受击时不重启当前动画，只保留最新一条待处理受击。
- 中断结束后重新按事实选择状态层，不恢复旧行为树索引。
- 保证子树失败、被抢占或重置后不残留旧动画进度和黑板事实。

## 非目标

- 不建立无限受击队列或通用战斗事件队列。
- 不把死亡、失衡和受击表现全部重写进一个大型节点。
- 不修改伤害、稳定值、掉落、动画事件或技能命中结算规则。
- 不重构战斗层现有攻击、防御、后撤和待机决策。
- 不为其他敌人自动迁移中断层结构。

## 已确认决策

- 中断层由一个 `InterruptExecutor` 节点统一管理。
- `InterruptExecutor` 只负责优先级、抢占、缓存、子树切换和生命周期。
- 三个现有 Sequence 继续负责一次完整的具体表现。
- 中断优先级固定为 `死亡 > 失衡 > 受击`。
- 受击期间的新受击不重启当前动画，只覆盖一份最新待处理受击。
- 失衡抢占受击并清空全部受击数据；失衡期间普通攻击不生成受击表现。
- 不设计连续失衡和失衡队列；只有死亡可以打断失衡。
- 后撤期间普通攻击不打断、不缓存受击表现；失衡和死亡可以抢占。
- 死亡立即抢占全部状态，完成首次死亡处理后永久保持终态。
- 受击和失衡子树执行失败时立即清理并退出，不逐帧重试。
- 战斗记忆和警戒记忆在非死亡中断期间继续推进。

## 顶层结构

根节点继续使用总设计定义的 `ReactivePrioritySelectorNodeAsset`：

```text
RootReactivePrioritySelector
├─ InterruptExecutor
├─ CombatLayerReactiveSequence
├─ AlertLayerReactiveSequence
└─ NormalLayerReactiveSequence
```

中断层是逻辑层级，不再额外嵌套一个中断 Selector。`InterruptExecutor` 是根节点的第一个子节点，也是死亡、失衡和受击的唯一行为树入口。

`InterruptExecutor` 的运行时结构：

```text
InterruptExecutorRuntime
├─ CurrentType: None / GetHit / Unbalance / Dead
├─ CurrentRuntime
├─ DeadSequenceRuntime
├─ UnbalanceSequenceRuntime
└─ GetHitSequenceRuntime
```

最新待处理受击仍存放在敌人黑板中，不在统一节点内再维护第二份队列或副本。

## 节点职责

### InterruptExecutor

- 每帧按死亡、失衡、受击的顺序读取同一帧黑板事实。
- 根据更高优先级事实抢占当前中断子树。
- 在抢占和切换时清理黑板中断状态并 Reset 旧子树。
- 为三个 Sequence 各创建一个独立运行时实例。
- 没有中断时返回 `Failure`，让根节点继续选择战斗、警戒或正常层。
- 正在执行受击或失衡时返回 `Running`。
- 进入死亡后永久返回 `Running`。

### DeadSequence

继续使用现有结构：

```text
DeadSequence
├─ IsDead
└─ DeadAction
```

它只负责首次死亡表现和永久终态，不负责选择优先级。

### UnbalanceSequence

继续使用现有结构：

```text
UnbalanceSequence
├─ IsUnbalanced
└─ UnbalanceAction
```

它负责一次完整失衡表现、稳定值恢复和失衡事实清理。

### GetHitSequence

继续使用现有结构：

```text
GetHitSequence
├─ HasHitReaction
└─ GetHitAction
```

它每次只消费并播放一条受击请求。当前动画播放期间不消费后来写入的待处理受击。

## 中断事实与单一真相源

继续使用现有黑板事实：

```text
IsDead
IsUnbalanced
HasHitReaction
IsHitReactionInProgress
PendingHitReactionAnimation
CurrentIntent
```

含义：

- `IsDead` 是永久死亡事实。
- `IsUnbalanced` 表示当前存在失衡请求或正在执行失衡。
- `IsHitReactionInProgress` 表示当前受击动画正在执行。
- `HasHitReaction` 表示存在一条尚未消费的最新受击。
- `PendingHitReactionAnimation` 保存该受击需要播放的动画名。
- `CurrentIntent == Retreat` 表示当前后撤动作锁有效。

`EnemyLifeComponent` 是新中断请求的唯一生产入口。`EnemyBlackboard` 只保存和消费事实，不自行推导中断优先级。`InterruptExecutor` 和三个表现子树只更新当前请求的消费、播放中、结束和清理状态，不得绕过 `EnemyLifeComponent` 创建新的中断请求，也不得维护第二套中断状态。

实现时为黑板提供明确的受击清理入口，用于一次性清除：

```text
HasHitReaction
IsHitReactionInProgress
PendingHitReactionAnimation
```

禁止通过调用 `ConsumeHitReaction` 冒充“清空全部受击状态”。消费一条请求和取消整个受击生命周期是两个不同操作。

## 事件写入规则

战斗事件仍按现有顺序处理：

```text
死亡
-> 失衡
-> 普通受击表现
```

### 死亡事件

- 调用现有 `SetDead(true)`。
- 清理目标、搜索、失衡、受击、战斗姿态和战斗意图事实。
- 关闭碰撞、导航和武器命中体。
- 发布死亡事件并处理掉落。

### 失衡事件

- 先按现有规则记录攻击者和刷新目标记忆。
- 设置 `IsUnbalanced = true`。
- 不同时写入普通受击表现。

### 普通受击事件

- 攻击者记忆更新与受击表现过滤必须分开处理。
- 即使当前处于失衡或后撤，攻击者事实和符合条件的战斗记忆仍照常建立或刷新。
- 当前未死亡、未失衡且不处于后撤时，写入最新受击动画。
- 当前失衡或后撤时，只保留伤害与记忆更新，不写入 `HasHitReaction` 和 `PendingHitReactionAnimation`。

这样可以避免为了抑制动画而错误丢失攻击者记忆。

## 统一调度流程

每次 Tick 先检查最高优先级事实：

```text
if IsDead
    切换或保持 Dead
else if IsUnbalanced
    切换或保持 Unbalance
else if IsHitReactionInProgress or HasHitReaction
    切换或保持 GetHit
else
    结束当前非死亡中断并返回 Failure
```

同类型中断继续执行当前运行时子树，不重复创建实例。切换类型时先执行旧类型的退出清理，再 Reset 旧子树，最后从头 Tick 新子树。

## 受击生命周期

首次受击：

```text
1. EnemyLifeComponent 写入最新受击动画
2. InterruptExecutor 选择 GetHitSequence
3. GetHitAction 消费一条请求
4. 设置 IsHitReactionInProgress = true
5. 停止移动并终止当前可中断战斗动作
6. 播放受击动画并持续返回 Running
```

连续受击：

```text
当前受击动画继续播放
-> 新请求只覆盖 PendingHitReactionAnimation
-> 当前动画结束
-> 清除本次 IsHitReactionInProgress
-> 若 HasHitReaction 为真，Reset 并立即开始下一次 GetHitSequence
-> 若无待处理受击，InterruptExecutor 返回 Failure
```

开始下一次受击前不返回低优先级层，避免战斗、警戒或正常行为在两段受击之间执行一帧。

`GetHitAction` 不再因为检测到新的 `HasHitReaction` 而强制重启当前动画。Sequence 负责一次表现，是否再次运行由 `InterruptExecutor` 决定。

## 失衡生命周期

进入失衡时：

```text
1. Reset 当前 GetHitSequence
2. 清除当前和待处理的全部受击数据
3. 停止移动并终止当前战斗动作
4. 从头执行 UnbalanceSequence
```

失衡动画完成后：

- 恢复稳定值。
- 清除 `IsUnbalanced`。
- 清理失衡动作的运行时字段。
- `InterruptExecutor` 返回 `Failure`，由根节点重新选层。

失衡期间普通攻击只结算伤害和更新目标记忆，不写入受击表现。稳定值系统在失衡结束前不再生成新的失衡表现请求，因此不需要重复失衡规则或队列。

## 后撤例外

后撤是战斗层中的不可被普通受击打断动作：

```text
当前为 Retreat
├─ 普通受击：结算伤害并更新目标记忆，不生成受击表现
├─ 失衡：立即抢占后撤
└─ 死亡：立即抢占后撤
```

后撤结束后不补播期间发生的普通受击。该例外只保护后撤免受普通受击表现打断，不阻止伤害、死亡、失衡或记忆更新。

## 死亡生命周期

死亡发生时：

```text
1. 立即 Reset 当前受击或失衡子树
2. 清空全部待处理中断数据
3. 从头执行 DeadSequence
4. 停止移动和战斗动作
5. 尝试播放一次死亡动画
6. 永久保持死亡终态
```

死亡动画无法播放时仍保持死亡终态，不回到其他层，也不逐帧重试动画。敌人死亡后到达的任何普通受击或失衡事件都不得生成新的中断请求。

## 抢占矩阵

| 当前状态 | 新事件 | 处理 |
|---|---|---|
| 正常、警戒或普通战斗动作 | 普通受击 | 进入 `GetHitSequence` |
| 正常、警戒或普通战斗动作 | 失衡 | 进入 `UnbalanceSequence` |
| 任意非死亡状态 | 死亡 | 立即进入 `DeadSequence` |
| 正在受击 | 普通受击 | 当前动画继续，仅覆盖最新待处理受击 |
| 正在受击 | 失衡 | 立即抢占并清空全部受击数据 |
| 正在受击 | 死亡 | 立即抢占并清空全部中断数据 |
| 正在失衡 | 普通受击 | 只结算伤害和更新记忆，不缓存表现 |
| 正在失衡 | 死亡 | 立即抢占 |
| 正在后撤 | 普通受击 | 只结算伤害和更新记忆，不缓存表现 |
| 正在后撤 | 失衡 | 立即抢占 |
| 正在后撤 | 死亡 | 立即抢占 |
| 已死亡 | 任意事件 | 不生成新的中断请求 |

## 中断结束后的层级选择

受击或失衡结束后不保存、恢复此前运行的战斗、警戒或正常节点索引。`InterruptExecutor` 返回 `Failure` 后，响应式根节点在同一套当前事实上重新选择：

```text
CombatTarget 有效 -> CombatLayer
否则警戒记忆或退出握手有效 -> AlertLayer
否则 -> NormalLayer
```

警戒状态受到玩家攻击时，攻击事件先建立 CombatTarget，再执行受击表现；受击结束后根节点因此选择 CombatLayer。

非死亡中断期间，战斗记忆和警戒记忆继续计时。若记忆在中断期间到期，中断结束后必须按到期后的事实选层，而不是恢复中断前状态。

## Reset 语义

`InterruptExecutor` 切换中断类型时：

- 先完成当前类型对应的黑板清理。
- 再调用当前运行时子树的 `Reset`。
- 清空当前类型和当前运行时引用。
- 从头执行新类型子树。

各中断 Action 的 `Reset` 必须真正清理自身局部运行时字段。当前通用动作节点的空 `Reset` 不满足中断子树被抢占后再次进入的要求，需要按动作类型补充定向清理。

外部重置整棵行为树时，统一节点 Reset 三个运行时子树并清理自身局部选择状态，但不得擅自清除 `IsDead`、CombatTarget 或记忆倒计时等权威黑板事实。

## 子树状态与错误处理

### GetHitSequence

- `Running`：保持受击中断。
- `Success`：本次动画完成；存在待处理受击时立即开始下一次，否则退出中断。
- `Failure`：清理当前和待处理受击，记录可定位错误，并退出中断，不重试。

### UnbalanceSequence

- `Running`：保持失衡中断。
- `Success`：清理失衡状态并退出中断。
- `Failure`：清理失衡状态、恢复必要稳定值、记录可定位错误并退出，不重试。

### DeadSequence

- 首次执行后始终由统一节点保持死亡终态。
- 动画播放失败只记录错误，不改变死亡事实，不回退到其他层。

异常不得被静默吞掉，也不得通过每帧重试把行为树锁死。

## 数据更新顺序

```text
AIController.TickAI
  1. 感知系统更新目标和可见性事实
  2. 伤害系统完成伤害、失衡和死亡结算
  3. EnemyLifeComponent 更新攻击者记忆
  4. EnemyLifeComponent 按死亡、失衡、受击顺序写入中断事实
  5. 统一记忆逻辑推进战斗记忆和警戒记忆
  6. BehaviorTreeRunner.Tick
  7. RootReactivePrioritySelector 首先 Tick InterruptExecutor
  8. InterruptExecutor 抢占、保持或退出当前中断
  9. 无中断时根节点继续选择战斗、警戒或正常层
```

行为树执行前必须完成同一帧事实更新，避免中断优先级依赖组件 Tick 顺序碰运气。

## 资产结构

剑盾敌人新增统一入口资产：

```text
SwordAndShieldEnemy/
├─ SwordAndShieldEnemyBehaviorTree.asset
└─ Interrupt/
   └─ InterruptExecutor.asset
```

统一入口继续引用公共资产：

```text
BehaviorTrees/Common/
├─ Sequence/
│  ├─ DeadSequence.asset
│  ├─ UnbalanceSequence.asset
│  └─ GetHitSequence.asset
├─ Condition/
│  ├─ IsDead.asset
│  ├─ IsUnbalanced.asset
│  └─ HasHitReaction.asset
└─ Action/
   ├─ DeadAction.asset
   ├─ UnbalanceAction.asset
   └─ GetHitAction.asset
```

不复制公共 Sequence，不再创建 `InterruptLayer` Selector 资产。根行为树直接把 `InterruptExecutor.asset` 作为最高优先级子节点。

## 实施范围

后续实施需要包含：

- 新增可引用三个 Sequence 的统一中断执行节点资产和运行时节点。
- 在黑板增加语义明确的受击全量清理入口。
- 调整 `EnemyLifeComponent`，分离攻击者记忆更新和受击表现写入。
- 抑制失衡、后撤和死亡期间的普通受击表现写入。
- 调整 `GetHitAction`，禁止新请求立即重启当前动画。
- 为受击和失衡动作补齐可被抢占的 Reset 语义。
- 把剑盾敌人根行为树的最高优先级分支替换为 `InterruptExecutor`。
- 删除剑盾敌人根树中不再使用的旧平铺中断入口，但保留公共 Sequence 资产。

## 测试方案

### EditMode

- 无中断事实时统一节点返回 `Failure`。
- 死亡、失衡和受击优先级固定且可在同一 Tick 抢占。
- GetHit 每次只消费一条请求，新请求不重启当前动作。
- 连续受击只保留最新待处理动画。
- 当前受击完成后直接开始最新待处理受击，中间不执行低优先级层。
- 失衡抢占受击时清空当前和待处理受击。
- 失衡和后撤期间普通受击不写入表现，但攻击者记忆仍更新。
- 死亡清空所有中断事实并永久保持终态。
- 子树切换会 Reset 旧运行时状态，再次进入时从头执行。
- 受击或失衡子树 Failure 后清理并退出，不重复 Tick 失败子树。
- 死亡动画失败时仍保持死亡终态。
- 非死亡中断不暂停战斗记忆和警戒记忆。

### Runtime

- 正常、警戒和战斗行为期间受到普通攻击会立即播放受击。
- 连续受击时当前动画完整播放，随后只播放最后一次待处理受击。
- 受击期间触发失衡会立即切换到失衡，之后不补播旧受击。
- 失衡期间普通攻击只造成伤害，不播放或延迟播放受击动画。
- 后撤期间普通攻击只造成伤害，后撤结束后不补播受击。
- 后撤期间失衡或死亡会立即终止后撤并切换表现。
- 警戒状态受到攻击后，受击结束进入战斗层。
- 中断期间目标记忆到期后，中断结束按最新事实进入警戒或正常层。
- 死亡可从任何状态立即进入，且死亡后不再执行其他行为。

## 验收标准

- 根节点只有一个统一中断入口，内部不使用额外中断 Selector。
- 统一节点复用三个公共 Sequence，不复制表现配置。
- 死亡、失衡和受击的优先级、抢占和退出规则与本文一致。
- 连续受击不会重启当前动画，也不会形成无限队列。
- 失衡和后撤期间不会留下延迟受击表现。
- 普通受击不能打断后撤，失衡和死亡可以。
- 中断结束后按当前事实重新选层，不恢复旧节点索引。
- 子树失败或被抢占后不会残留运行时状态。
- 死亡动画失败也不会让敌人离开死亡终态。
- 中断表现过滤不会阻止攻击者记忆更新。
