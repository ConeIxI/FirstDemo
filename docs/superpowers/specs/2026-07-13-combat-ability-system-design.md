# 轻量战斗能力系统重构设计

- 日期：2026-07-13
- 状态：已批准，进入实施计划
- 适用项目：FirstGameDemo
- Unity：2022.3.61f1c1
- C#：兼容 C# 9.0

## 1. 目标

重写当前单次技能与命中结算框架，借鉴 Gameplay Ability System 的能力、属性、标签和事件思想，同时保持适合当前单机项目的规模。

重构必须满足：

- 玩家继续使用 FSM。
- 敌人继续使用行为树。
- 继续使用 JSON 技能配置、技能 ID、装备配置和 `ConfigManager`。
- 玩家与敌人共用一套技能激活、命中检测和结算规则。
- 玩家使用新的 `CombatAttributeSet`。
- 敌人继续使用并扩展 `EnemyAttributeComponent`。
- HUD、动画、敌人黑板、命中特效和命中停顿能够消费新系统输出。
- 最终删除旧战斗核心，不保留长期兼容层。

## 2. 非目标

本次不实现：

- 联机同步、客户端预测和服务端授权。
- 完整 Gameplay Ability System。
- 持续 Buff、周期伤害、效果叠层和驱散。
- 整场战斗的胜负、奖励和结算界面。
- 由战斗系统控制玩家 FSM、敌人行为树或动画状态机。
- 由战斗系统负责销毁、回收或复活角色。

## 3. 需要删除的旧核心

完成迁移后删除：

- `SkillRunner`
- `DamageResolver`
- `CombatStats`
- `CombatResource`
- `CombatState`
- `CombatResult`
- `CombatReaction`
- `Combatant`
- `CombatHit`
- `InterruptResolver`
- `SkillBase`
- `SkillContext`
- `SkillDefine`
- `ISkill`
- `PlayerWeaponHitDetector`
- `EnemyWeaponHitDetector`
- `WeaponHitEventArgs`
- `EnemyWeaponHitEventArgs`

旧类型对应的专用测试同步删除或改写为新框架测试。

## 4. 总体架构

```text
玩家 FSM / 敌人行为树
          │
          │ SkillConfig
          ▼
 CombatAbilitySystem
   ├── 战斗标签
   ├── 当前技能
   ├── 命中窗口
   ├── 目标去重
   └── 命中结算
          │
          ▼
  ICombatAttributes
   ├── 玩家：CombatAttributeSet
   └── 敌人：EnemyAttributeComponent
          │
          ▼
      CombatEvent
   ├── 玩家 FSM
   ├── EnemyLifeComponent
   ├── 命中特效
   ├── 命中停顿
   └── 击退表现
```

职责边界：

- FSM 和行为树决定做什么动作。
- `CombatAbilitySystem` 决定技能能否激活以及命中如何结算。
- `WeaponHandler` 管理武器和命中窗口。
- `WeaponHitDetector` 只采集命中目标。
- 属性组件保存并修改数值。
- `CombatEvent` 对外描述已经发生的战斗事实。
- HUD 通过属性变化事件更新，不依赖命中事件。

## 5. 公共组件和接口

### 5.1 CombatAbilitySystem

玩家和敌人都挂载同一个 `CombatAbilitySystem`。它是战斗规则的唯一入口，负责：

- 接收并激活 `SkillConfig`。
- 检查死亡、失衡、标签和玩家战意。
- 保存当前技能与打断参数。
- 管理常驻标签和限时标签。
- 推进稳定值延迟恢复，并在受到稳定伤害时重置恢复计时。
- 打开、关闭命中窗口并记录已命中目标。
- 接收 `WeaponHitDetector` 上报的目标。
- 按固定优先级执行无敌、弹反、格挡和普通命中结算。
- 修改来源与目标属性。
- 取消被打断、失衡或死亡单位的当前技能。
- 通过现有 `EventCenter` 发布 `CombatEvent`。

组件必须使用 `[DisallowMultipleComponent]`，且不允许运行时自动补挂依赖组件。

阵营使用强类型 `CombatFaction`，当前只包含 `Player` 和 `Enemy`。没有能力系统的场景物体不参与阵营判断。

### 5.2 WeaponHandler

继续作为玩家和敌人的公共武器组件，并增加：

- `OpenHitWindow()`
- `CloseHitWindow()`

打开命中窗口时：

1. 通知 `CombatAbilitySystem` 清空本窗口已命中目标。
2. 清空 `WeaponHitDetector` 的碰撞记录。
3. 启用武器 Collider。

关闭命中窗口只禁用 Collider，不结束当前技能。一个技能允许包含多个命中窗口。

### 5.3 WeaponHitDetector

玩家和敌人共用一个具体实现。它负责：

1. 从碰撞对象父级查找目标 `CombatAbilitySystem`。
2. 排除没有能力系统的场景物体。
3. 将目标与命中位置上报给来源 `CombatAbilitySystem`。

阵营检查和最终目标去重由 `CombatAbilitySystem` 完成。去重键是目标能力系统，而不是具体 Collider 或 GameObject，避免多碰撞体重复伤害。

### 5.4 ICombatAttributes

玩家和敌人的公共属性接口包含：

- 当前生命与最大生命。
- 当前稳定值与最大稳定值。
- 死亡和失衡判断。
- 生命、稳定值的伤害与恢复操作。
- 属性变化事件。

`CombatAbilitySystem` 只依赖该接口，不依赖玩家或敌人具体类型。

### 5.5 ICombatResource

仅玩家实现，用于战意：

- 当前战意与最大战意。
- 消耗战意。
- 增加战意。

敌人不实现该接口，也不包含无用的战意字段。

### 5.6 ICombatMotion

统一击退入口：

- 玩家由 `PlayerController` 实现。
- 敌人由 `EnemyMovementComponent` 实现。

敌人执行外部位移前停止自身寻路移动。结算层不再判断目标是玩家还是敌人。

## 6. 属性设计

### 6.1 玩家 CombatAttributeSet

玩家 `CombatAttributeSet` 同时实现 `ICombatAttributes` 和 `ICombatResource`，管理：

- Health / MaxHealth
- Stability / MaxStability
- BattleSpirit / MaxBattleSpirit

所有数值修改必须经过公开属性操作，不允许 FSM、UI 或其他外围系统直接写字段。

### 6.2 敌人 EnemyAttributeComponent

保留现有组件和敌人定义加载流程，扩展为真正的运行时属性组件：

- 使用 `EnemyDefinition.AttributeConfig` 初始化最大值和当前值。
- 实现 `ICombatAttributes`。
- `Health` 和 `Stability` 表示当前值。
- 增加 `MaxHealth`、`MaxStability`、伤害、恢复、死亡和失衡能力。
- 行为树血量判断继续读取该组件，但读取的是会随战斗变化的当前生命。

敌人不再挂载或使用 `CombatStats` 和 `CombatResource`，消除双属性源。

### 6.3 属性变化事件

属性组件发布本地 C# 事件，数据包含：

- 属性类型：Health、Stability 或 BattleSpirit。
- 当前值。
- 最大值。
- 本次变化量。

事件只在实际数值发生变化时发布。

属性组件负责初始化自身数值；进入战斗运行期后，生命、稳定值和战意变化必须由 `CombatAbilitySystem` 调用属性接口完成。

`CombatAbilitySystem` 保留当前战斗单位的稳定值恢复参数和计时状态：受到稳定伤害后重新计时，等待恢复延迟结束后按每秒恢复量调用属性接口。死亡、稳定值已满或游戏暂停时不推进恢复。

## 7. 战斗标签

不实现字符串层级标签，使用强类型 `CombatTag` 枚举：

- `Dead`
- `Unbalanced`
- `Defending`
- `ParryWindow`
- `Invincible`

`CombatAbilitySystem` 内部维护：

- 常驻标签集合。
- 限时标签及其到期时间。

公开操作只包含：

- 查询标签。
- 添加标签。
- 移除标签。
- 添加限时标签。

限时标签使用受 `Time.timeScale` 影响的游戏时间，暂停时无敌和弹反窗口同步暂停。

打断能力和抗打断等级继续使用 `InterruptConfig`，不编码为标签。

## 8. 技能配置与激活

### 8.1 配置来源

继续使用现有 JSON `SkillConfig`、技能 ID 和 `ConfigManager`，不创建 ScriptableObject Ability。

给 `SkillConfig` 增加可选字段：

- `requiredTags`
- `blockedTags`
- `activeTags`

JSON 使用 `CombatTag` 枚举名称保存标签。旧 JSON 缺少这些字段时按空数组处理。

### 8.2 激活流程

```text
FSM / EnemyCombatComponent
→ ConfigManager 获取 SkillConfig
→ CombatAbilitySystem.TryActivate(config)
```

激活顺序：

1. 拒绝空配置。
2. 拒绝拥有 `Dead` 或 `Unbalanced` 的单位。
3. 拒绝当前已有激活技能的单位。
4. 检查 `requiredTags` 与 `blockedTags`。
5. 玩家检查并消耗战意。
6. 保存当前技能和打断参数。
7. 添加 `activeTags`。
8. 清空命中目标集合。

激活结果使用枚举表达：

- Success
- Dead
- Unbalanced
- AlreadyActive
- BlockedByTag
- InsufficientResource

### 8.3 取消流程

取消技能时：

1. 关闭命中窗口。
2. 移除当前技能的 `activeTags`。
3. 清空当前技能与打断参数。
4. 清空命中目标集合。

FSM 和行为树继续负责动画、连段、输入缓冲和动作结束时机。

## 9. 单次命中结算

固定顺序：

```text
目标校验 → 无敌 → 弹反 → 格挡 → 普通命中 → 死亡/失衡 → CombatEvent
```

### 9.1 目标校验

以下情况直接忽略且不发布事件：

- 来源没有激活技能。
- 目标没有 `CombatAbilitySystem`。
- 命中自己。
- 命中同阵营。
- 当前窗口已经命中过目标。
- 目标已经死亡。
- 命中窗口已经关闭。

### 9.2 无敌

目标拥有 `Invincible` 时：

- 不修改属性。
- 不打断技能。
- 发布 `Invincible` 结果。

### 9.3 弹反

目标拥有 `ParryWindow` 且技能允许被弹反时：

- 不扣目标生命与稳定值。
- 按配置恢复目标稳定值。
- 扣除攻击者稳定值。
- 攻击者稳定值归零时进入失衡并取消当前技能。
- 发布 `Parried` 结果。

### 9.4 格挡

目标拥有 `Defending` 且技能允许格挡时：

- 不扣生命。
- 扣除目标稳定值。
- 稳定值归零时进入失衡。
- 本次破防不追加生命伤害。
- 发布 `Blocked` 结果。

### 9.5 普通命中

- 扣除目标生命与稳定值。
- 普攻命中时给攻击者增加战意。
- 根据 `InterruptConfig` 判断是否取消目标当前技能。
- 发布 `Hit` 结果。

### 9.6 状态优先级

```text
Dead > Unbalanced > Interrupted > 普通受击
```

死亡时：

- 添加 `Dead`。
- 移除 `Defending`、`ParryWindow`、`Invincible` 和 `Unbalanced`。
- 取消当前技能并关闭命中窗口。

失衡时：

- 添加 `Unbalanced`。
- 移除 `Defending` 和 `ParryWindow`。
- 取消当前技能。

## 10. CombatEvent

`CombatEvent` 继承现有 `EventArgsBase`，通过 `EventCenter` 发布，不新增事件总线。

事件类型：

- Invincible
- Parried
- Blocked
- Hit

事件数据包含：

- 来源与目标 `CombatAbilitySystem`。
- 当前 `SkillConfig`。
- 目标生命伤害。
- 目标稳定伤害。
- 来源稳定伤害。
- 来源战意获得。
- 目标是否被打断。
- 目标是否需要播放普通受击反应。
- 目标是否进入失衡。
- 来源是否进入失衡。
- 目标是否死亡。
- 命中位置与命中方向。

事件是只读的已发生事实，消费者不得修改结算结果。

## 11. 外围系统接入

### 11.1 玩家 FSM

- `PlayerSkillManager` 只维护装备授予的技能 ID，不再创建 `SkillRunner`。
- 玩家攻击状态从 `ConfigManager` 获取配置并调用 `TryActivate`。
- 攻击状态退出时取消能力并关闭命中窗口。
- 防御状态添加和移除 `Defending`、`ParryWindow`。
- 翻滚状态添加限时 `Invincible`。
- 失衡状态结束时恢复稳定值并移除 `Unbalanced`。
- 死亡状态取消能力并清理临时标签。
- `PlayerStateMachine` 消费以自己为来源或目标的 `CombatEvent`，按状态优先级切换 FSM。

### 11.2 敌人行为树

- 保留 `EnemyCombatComponent` 的公开攻击、结束、中断和命中窗口接口。
- 内部使用 `CombatAbilitySystem` 代替 `SkillRunner` 和 `Combatant`。
- `EnemyLifeComponent` 消费 `CombatEvent`，写入受击、失衡、死亡和攻击者黑板事实。
- 行为树结构和行为树资源保持不变。
- `EnemyAnimationComponent` 的动画事件转发到 `WeaponHandler` 的通用命中窗口接口。

### 11.3 HUD

HUD 不依赖 `CombatEvent`，而是订阅玩家 `CombatAttributeSet` 的属性变化事件。

打开 HUD 时：

1. 绑定玩家能力系统与属性组件。
2. 订阅属性变化事件。
3. 主动刷新一次全部资源条。

属性变化后只刷新对应的生命、稳定值或战意条。关闭 HUD 时取消订阅。

`Update` 只保留颜色闪烁、渐变等表现计时，不再每帧查询全部属性。

### 11.4 表现系统

- 命中特效根据 `CombatEvent` 类型和 `SkillConfig` 播放。
- 命中停顿根据 `CombatEvent` 与技能配置执行。
- 击退通过目标 `ICombatMotion` 执行。
- 战斗系统不直接播放角色动画。

## 12. 错误处理与生命周期

### 12.1 配置错误

`CombatAbilitySystem` 必须显式配置阵营、属性提供者和武器处理器。属性提供者必须实现 `ICombatAttributes`。配置错误时输出明确错误并禁用组件，不自动创建缺失依赖。

`ConfigManager` 加载技能时校验：

- 技能 ID 合法且不重复。
- 伤害、稳定伤害和战意消耗不为负数。
- `requiredTags` 与 `blockedTags` 不冲突。
- 死亡标签不能作为技能激活标签。
- 连段下一技能 ID 存在。

### 12.2 正常运行时忽略

自身、同阵营、重复命中、已死亡目标、没有能力系统的场景物体和关闭窗口后的碰撞均属于正常情况，不输出错误日志。

### 12.3 禁用清理

`CombatAbilitySystem.OnDisable`：

- 取消当前技能。
- 关闭命中窗口。
- 清空命中目标。
- 清空限时标签和技能激活标签。

`WeaponHandler.OnDisable`：

- 禁用武器 Collider。
- 清空命中记录。

事件消费者统一在 `OnEnable` 订阅，在 `OnDisable` 取消订阅。

## 13. 迁移顺序

1. 新增核心类型、接口、标签和事件。
2. 迁移玩家与敌人属性源。
3. 扩展 `WeaponHandler` 并合并武器检测器。
4. 迁移玩家 FSM 与 `PlayerSkillManager`。
5. 迁移 `EnemyCombatComponent`、`EnemyLifeComponent` 和敌人行为树接入点。
6. 迁移 HUD、特效、命中停顿和击退。
7. 更新 `Scene1`、玩家对象和 `GuardMeleeEnemy.prefab` 的组件绑定。
8. 删除旧核心、旧武器事件和旧专用测试。
9. 完成编译、日志和 Runtime 验收。

迁移阶段允许新旧代码短暂共存，但最终代码不得保留旧核心适配层。

## 14. 测试设计

### 14.1 EditMode

覆盖：

- 玩家与敌人属性初始化、上下限、伤害和恢复。
- 稳定值受到伤害后延迟恢复，并在再次受击时重置延迟。
- 战意消耗、增加与属性事件。
- 常驻和限时标签。
- 暂停时限时标签停止计时。
- 死亡和失衡后的标签清理。
- 技能激活条件与失败结果。
- 自身、同阵营、重复和无效目标过滤。
- 无敌、弹反、格挡、普通命中和打断。
- 死亡优先于失衡。
- `CombatEvent` 字段与实际属性变化一致。

### 14.2 PlayMode / Runtime

覆盖：

1. 玩家普通攻击敌人。
2. 敌人攻击玩家。
3. 玩家格挡与破防。
4. 玩家弹反。
5. 玩家翻滚无敌。
6. 玩家和敌人进入失衡。
7. 玩家死亡进入 FSM 死亡状态。
8. 敌人死亡进入行为树死亡分支。
9. 武器切换后技能 ID 正确。
10. HUD 更新生命、稳定值和战意。
11. 命中特效、击退和命中停顿正常。
12. 暂停期间无敌和弹反窗口不提前结束。

## 15. 验收标准

- `$CLI compile unity` 成功。
- Unity Error 日志为空。
- 项目中不存在旧核心类型引用。
- 场景和 Prefab 没有 Missing Script。
- 玩家 FSM 和敌人行为树结构保留。
- JSON 技能 ID、装备配置和敌人技能配置继续可用。
- 玩家与敌人使用同一个 `CombatAbilitySystem`。
- 玩家只有 `CombatAttributeSet` 一个运行时战斗属性源。
- 敌人只有 `EnemyAttributeComponent` 一个运行时战斗属性源。
- 所有属性修改经过能力系统和属性接口。
- 所有命中结果通过 `CombatEvent` 对外发布。
- HUD 通过属性变化事件更新，不轮询旧属性组件。
