# 战斗系统框架文档

本文档基于当前项目代码整理，目标是说明战斗系统的职责边界、核心数据、调用链、表现入口和后续扩展位置。当前战斗系统主要围绕 `Combatant`、`SkillRunner`、`DamageResolver`、`CombatReaction` 四层展开：角色状态负责发起动作，武器碰撞负责发现命中，结算层负责产出结果，表现层负责播放反馈和切换受击状态。

## 1. 总体架构

战斗系统可以拆成六层：

1. 输入与 AI 决策层
   - 玩家由 `IdleState`、`WalkState`、`DefenceState` 等 FSM 状态读取输入。
   - 敌人由 `EnemyStateMachine`、`EnemyCombatActionState` 及追击/攻击状态根据目标与技能 ID 决策。

2. 动作状态层
   - 玩家普通攻击和武器技能共用 `PlayerCombatActionState`。
   - 敌人攻击和敌人技能共用 `EnemyCombatActionState`。
   - 动作状态负责读取技能配置、校验能否释放、播放动画、订阅根运动、退出时清理当前技能。

3. 技能运行层
   - `SkillRunner` 是一次技能释放的运行时上下文管理器。
   - 它保存当前 `SkillContext`，注册武器命中事件，过滤重复目标，并把命中交给结算层。

4. 命中检测层
   - `WeaponHandler` 记录当前装备武器的 `WeaponHitDetector`。
   - `CharacterStateMachine.EnableWeaponCollider()` 在动画事件打开命中窗口时启用武器碰撞。
   - `PlayerWeaponHitDetector` 只检测 `Enemy` 标签目标。
   - `EnemyWeaponHitDetector` 只检测 `Player` 标签目标。

5. 战斗结算层
   - `DamageResolver.Resolve()` 根据目标状态和技能配置产出 `CombatResult`。
   - 结算顺序是：已死亡、无敌、弹反、格挡、普通命中。
   - `CombatStats` 实际修改生命值和稳定值。
   - `CombatResource` 处理战意获取和消耗。

6. 表现与状态反应层
   - `CombatHitStopController` 根据 `CombatResult` 播放命中停顿。
   - `CombatEffectExecutor` 根据命中结果播放命中、格挡、弹反特效。
   - `CombatReaction.Apply()` 根据结果驱动玩家或敌人进入死亡、失衡、受击等状态。

## 2. 核心文件与职责

### 2.1 战斗单位

- `Assets/Game/Battle/Combat/Core/Combatant.cs`
  - 战斗单位入口组件。
  - 持有 `CombatStats`、`CombatResource`、`CombatState`。
  - `EnsureRuntimeComponents(bool withResource)` 确保运行时组件存在。
  - 每帧调用 `Stats.TickStabilityRecovery()` 推进稳定值恢复。

- `Assets/Game/Battle/Combat/Core/CombatStats.cs`
  - 管理生命值和稳定值。
  - `ApplyHealthDamage()` 扣生命。
  - `ApplyStabilityDamage()` 扣稳定值，并刷新稳定值恢复等待。
  - `RestoreStability()` 恢复稳定值。
  - 暴露 `HealthChanged`、`StabilityChanged` 事件，UI 可监听。

- `Assets/Game/Battle/Combat/Core/CombatResource.cs`
  - 管理战意。
  - 普通攻击命中可增加战意。
  - 武器技能释放前会尝试消耗战意。

- `Assets/Game/Battle/Combat/Core/CombatState.cs`
  - 管理临时战斗状态。
  - `IsDefending` 表示正在防御。
  - `IsParryWindowActive` 表示弹反窗口仍有效。
  - `IsInvincible` 表示当前无敌。
  - `CanBeInterrupted` 和 `InterruptResistLevel` 用于打断判断。

### 2.2 技能与配置

- `Assets/Game/Battle/Skill/Common/SkillConfig.cs`
  - 技能配置数据。
  - 关键字段包括：
    - `skillId`
    - `skillName`
    - `skillAnimationName`
    - `comboNextSkillId`
    - `skillType`
    - `battleSpiritCost`
    - `battleSpiritGainOnHit`
    - `hitConfig`
    - `interruptConfig`
    - `onCastEffects`
    - `onHitEffects`
    - `onBlockEffects`
    - `onParryEffects`
    - `skillEffectConfig`
    - `skillAudioConfig`

- `Assets/Game/Battle/Combat/Core/SkillConfig.cs`
  - 定义 `SkillType`、`CombatHitConfig`、`InterruptConfig` 等战斗配置结构。
  - `CombatHitConfig` 决定生命伤害、稳定值伤害、命中停顿、是否可格挡、是否可弹反、受击动画名等。
  - `InterruptConfig` 决定能否打断、打断等级、抗打断等级等。

- `Assets/Framework/Manager/ConfigManager.cs`
  - 加载玩家技能配置和敌人技能配置。
  - 玩家技能按 `WeaponType` 分类，通过 `GetPlayerSkillConfig(WeaponType type, int id)` 读取。
  - 敌人技能通过 `GetSkillConfig(int id)` 读取。
  - 加载时会调用 `SkillConfigDefaults.ApplyPlayerDefaults()` 或 `ApplyEnemyDefaults()` 补默认值。

### 2.3 技能运行

- `Assets/Game/Battle/Skill/SkillRunner.cs`
  - 当前技能运行核心。
  - `Initialize(Combatant caster, WeaponHandler weaponHandler)` 绑定释放者和武器。
  - `LoadSkills(IEnumerable<int> skillIds)` 同步可释放技能列表。
  - `CanCast(int skillId, SkillConfig config)` 非消耗式检查技能能否释放。
  - `Cast(int skillId, SkillConfig config)` 真正释放技能，武器技能会消耗战意，并创建 `SkillContext`。
  - `CancelCurrentSkill()` 取消当前技能并取消事件订阅。
  - `BeginHitWindow()` 清空已命中目标集合，允许多段攻击在新窗口再次命中同一目标。
  - `OnPlayerWeaponHit()` 和 `OnEnemyWeaponHit()` 接收武器命中事件。
  - `ResolveHit()` 构造 `CombatHit`，调用结算、停顿、特效和状态反应。

- `Assets/Game/Battle/Skill/SkillContext.cs`
  - 一次技能释放上下文。
  - 保存技能 ID、技能配置、释放者、当前武器处理器。

### 2.4 武器与命中

- `Assets/Game/Character/Equipment/WeaponData.cs`
  - 武器数据。
  - `skillIds` 是旧版技能组。
  - `normalAttackSkillIds` 是普通攻击连段。
  - `weaponSkillIds` 是 1/2/3 键对应的武器技能槽。
  - `EnumerateAllSkillIds()` 给技能管理器同步全部可用技能。

- `Assets/Game/Character/Equipment/WeaponHandler.cs`
  - 当前武器命中检测器管理器。
  - `ApplyWeapon()` 从当前武器模型中查找 `WeaponHitDetector`。
  - 切换武器时会关闭旧碰撞，并清空命中列表。

- `Assets/Game/Battle/Weapon/WeaponHitDetector.cs`
  - 武器命中检测基类。
  - 保存本轮命中过的物体列表，避免同一窗口重复触发。
  - 提供 `EnableCollider()` 和 `ClearHitList()`。

- `Assets/Game/Battle/Weapon/PlayerWeaponHitDetector.cs`
  - 玩家武器触发器。
  - `OnTriggerStay()` 命中 `Enemy` 标签后发出 `WeaponHitEventArgs`。

- `Assets/Game/Battle/Weapon/EnemyWeaponHitDetector.cs`
  - 敌人武器触发器。
  - `OnTriggerStay()` 命中 `Player` 标签后发出 `EnemyWeaponHitEventArgs`。

### 2.5 玩家战斗状态

- `Assets/Game/Character/Player/PlayerFsm/PlayerStateBase.cs`
  - 玩家状态公共基类。
  - `TryStartNormalAttack()` 从当前武器读取首段普攻 ID。
  - `TryStartWeaponSkill()` 根据技能槽读取武器技能 ID。
  - 进入攻击或技能前写入 FSM 数据：`WeaponType`、`AttackId`。

- `Assets/Game/Character/Player/PlayerFsm/PlayerCombatActionState.cs`
  - 玩家普通攻击和武器技能的公共动作状态。
  - `Enter()` 读取技能配置、校验类型、调用 `SkillRunner.Cast()`、标记战斗动作并播放动画。
  - `Update()` 记录预输入，在后摇决策窗口中处理技能、翻滚、防御、连段。
  - `Exit()` 取消当前技能、结束战斗动作、关闭武器碰撞、取消根运动订阅。
  - `ResolvePlayerSkillConfig()` 根据 FSM 中的武器类型和技能 ID 读取玩家技能配置。
  - `BeginCombatAction()` 根据技能的打断配置写入 `CombatState`。

- `Assets/Game/Character/Player/PlayerFsm/AttackState.cs`
  - 普通攻击状态。
  - 只允许 `SkillType.NormalAttack`。
  - 在后摇决策窗口中消费普攻预输入，若存在 `comboNextSkillId` 则进入下一段连段。

- `Assets/Game/Character/Player/PlayerFsm/SkillState.cs`
  - 武器技能状态。
  - 只允许 `SkillType.WeaponSkill`。
  - 不响应普攻连段，动画结束后回到待机。

- `Assets/Game/Character/Player/PlayerFsm/DefenceState.cs`
  - 玩家防御状态。
  - 进入时播放 `Defence` 动画，调用 `CombatState.BeginDefence(ParryWindowTime)`。
  - 防御开始后的短时间内可触发弹反。
  - 退出时调用 `CombatState.EndDefence()`。

- `Assets/Game/Character/CharacterStateMachine.cs`
  - 玩家和敌人的角色状态机基类。
  - `EnableWeaponCollider()` 在动画事件打开命中窗口时启用当前武器碰撞并清空命中列表。
  - `DisableWeaponCollider()` 关闭武器碰撞。
  - `AttackDecisionWindowOpen()` 打开攻击后摇决策窗口。
  - `SkillCanSwitch(int canSwitch)` 兼容旧动画事件，打开技能切换/决策窗口。
  - `ResetAttackDecisionWindow()` 清理窗口状态。

### 2.6 敌人战斗状态

- `Assets/Game/Character/Enemy/EnemyStateMachine.cs`
  - 敌人 FSM 总入口。
  - 记录 `Target`、`CurrentAttackSkillId`、`CurrentSkillId`。
  - `EnableWeaponCollider()` 继承基类逻辑，并调用 `SkillRunner.BeginHitWindow()`。
  - `OnHit()`、`OnUnbalanced()`、`OnDeath()` 是战斗结算驱动敌人反应的外部入口。

- `Assets/Game/Character/Enemy/EnemyFsm/Common/EnemyCombatActionState.cs`
  - 敌人攻击和技能公共动作状态。
  - 进入时停止移动、读取技能配置、校验技能、调用 `SkillRunner.Cast()`、标记战斗动作、播放动画。
  - 动画结束后根据目标可见性和距离决定继续追击、巡逻或进入下一段。
  - 退出时取消技能、结束攻击、结束战斗动作并关闭武器碰撞。

- `Assets/Game/Character/Enemy/EnemySkillManager.cs`
  - 敌人技能管理器。
  - 初始化敌人可释放技能并同步给 `SkillRunner`。

## 3. 关键数据结构

### 3.1 CombatHit

`CombatHit` 是一次命中的输入数据，包含：

- `Attacker`：攻击者 `Combatant`。
- `Target`：目标 `Combatant`。
- `SkillConfig`：技能配置接口。
- `HitPoint`：命中点。
- `HitDirection`：从攻击者指向目标的方向。
- `HitConfig`：从技能配置中取出的命中配置。
- `InterruptConfig`：从技能配置中取出的打断配置。

### 3.2 CombatResult

`CombatResult` 是一次命中的输出数据，包含：

- `ResultType`
  - `Dead`：目标命中前已死亡。
  - `Invincible`：目标无敌，本次命中无效。
  - `Parry`：目标弹反成功。
  - `Block`：目标格挡成功。
  - `Hit`：普通命中。

- `FeedbackKind`
  - `None`
  - `NormalHit`
  - `HeavyHit`
  - `Block`
  - `Parry`
  - `Invincible`

- 数值结果
  - `HealthDamageApplied`
  - `StabilityDamageApplied`
  - `StabilityRestored`
  - `BattleSpiritGained`

- 状态反应
  - `IsInterrupted`
  - `ShouldCancelCurrentSkill`
  - `ShouldPlayHitReaction`
  - `ShouldEnterUnbalanced`
  - `ShouldEnterAttackerUnbalanced`
  - `ShouldDie`

- 表现字段
  - `HitStopTime`
  - `HitReactionName`

## 4. 玩家攻击敌人主流程

1. 玩家在 `IdleState` 或 `WalkState` 中按攻击键。
2. `PlayerStateBase.TryStartNormalAttack()` 读取当前武器的首段普通攻击技能 ID。
3. `TryEnterAttackState()` 写入 `WeaponType` 和 `AttackId`，切换到 `AttackState`。
4. `AttackState` 进入 `PlayerCombatActionState.Enter()`。
5. `ResolvePlayerSkillConfig()` 读取玩家技能配置。
6. `PlayerSkillManager` 和 `SkillRunner` 校验当前技能是否可用。
7. `SkillRunner.Cast()` 创建当前 `SkillContext` 并注册武器命中事件。
8. `BeginCombatAction()` 写入玩家 `CombatState`，用于打断抗性等判定。
9. 玩家播放技能动画。
10. 攻击动画事件调用 `EnableWeaponCollider()`。
11. 当前武器碰撞体启用，命中列表清空，`SkillRunner.BeginHitWindow()` 清空本技能窗口内已结算目标。
12. `PlayerWeaponHitDetector.OnTriggerStay()` 检测到 `Enemy` 标签目标，发出 `WeaponHitEventArgs`。
13. `SkillRunner.OnPlayerWeaponHit()` 验证事件来源属于当前技能上下文。
14. `SkillRunner.ResolveHit()` 从命中的 Collider 上找目标 `Combatant`。
15. `ResolveHit()` 构造 `CombatHit`。
16. `DamageResolver.Resolve()` 进行结算。
17. `CombatHitStopController.Play()` 播放命中停顿。
18. `CombatEffectExecutor` 播放命中、格挡或弹反特效。
19. `CombatReaction.Apply()` 驱动目标进入受击、失衡或死亡状态。
20. 攻击状态退出时 `PlayerCombatActionState.Exit()` 取消技能并关闭武器碰撞。

## 5. 敌人攻击玩家主流程

1. 敌人 AI 或状态机设置 `CurrentAttackSkillId` 或 `CurrentSkillId`。
2. 敌人进入 `EnemyCombatActionState` 的子类。
3. `EnemyCombatActionState.Enter()` 从 `ConfigManager.GetSkillConfig()` 读取敌人技能配置。
4. 敌人 `SkillRunner.Cast()` 创建技能上下文。
5. `BeginCombatAction()` 写入敌人 `CombatState`。
6. `EnemyCombat.Attack()` 面向目标并进入攻击逻辑。
7. 敌人播放攻击动画。
8. 动画事件调用 `EnemyStateMachine.EnableWeaponCollider()`。
9. `EnemyWeaponHitDetector.OnTriggerStay()` 检测到 `Player` 标签目标，发出 `EnemyWeaponHitEventArgs`。
10. `SkillRunner.OnEnemyWeaponHit()` 验证事件来源和技能类型。
11. 后续同玩家攻击流程：构造 `CombatHit`，调用 `DamageResolver.Resolve()`，播放反馈，调用 `CombatReaction.Apply()`。

## 6. 伤害、格挡、弹反、打断

### 6.1 结算顺序

`DamageResolver.Resolve()` 的顺序固定：

1. 目标为空、没有 Stats、或目标已死亡：返回 `Dead`。
2. 目标处于无敌：返回 `Invincible`。
3. 目标处于弹反窗口，且技能允许被弹反：进入 `ResolveParry()`。
4. 目标正在防御，且技能允许被格挡：进入 `ResolveBlock()`.
5. 其他情况：进入 `ResolveNormalHit()`。

这个顺序很重要。例如目标同时处于防御和弹反窗口时，优先弹反。

### 6.2 普通命中

普通命中会：

- 扣生命值。
- 扣稳定值。
- 普通攻击命中时给攻击者增加战意。
- 调用 `InterruptResolver.CanInterrupt()` 判断是否打断目标。
- 根据技能类型、稳定值伤害、是否失衡判断反馈是 `NormalHit` 还是 `HeavyHit`。
- 根据配置写入受击动画名。

### 6.3 格挡

格挡命中会：

- 不扣生命值。
- 扣稳定值。
- 如果稳定值归零，目标进入失衡。
- 反馈类型设置为 `Block`。
- 当前已有 `onBlockEffects` 特效入口。

当前格挡结果不会默认切换玩家到格挡受击状态；如需“格挡被打后后退、播放格挡受击动画”，应扩展 `CombatResult` 和 `CombatReaction`。

### 6.4 弹反

弹反命中会：

- 目标恢复稳定值。
- 攻击者承受稳定值伤害。
- 如果攻击者稳定值归零，攻击者进入失衡。
- 反馈类型设置为 `Parry`。
- 当前已有 `onParryEffects` 特效入口。

### 6.5 打断

打断由 `InterruptResolver` 判断。核心依据：

- 命中配置是否允许打断。
- 攻击的打断等级。
- 目标当前动作的抗打断等级。
- 目标是否正在防御。

普通命中中，如果结果被判定为打断，则：

- `result.IsInterrupted = true`
- `result.ShouldCancelCurrentSkill = true`
- `result.ShouldPlayHitReaction = true`

## 7. 表现系统

### 7.1 命中停顿

`CombatHitStopController.Play(result)` 根据 `CombatResult` 播放全局停顿。

当前支持：

- 普通命中：`NormalHit`
- 重命中：`HeavyHit`
- 格挡：`Block`
- 弹反：`Parry`

死亡结果不会叠加命中停顿。

### 7.2 特效

`SkillRunner.ExecuteEffects()` 根据 `CombatResultType` 分发：

- `Hit` -> `CombatEffectExecutor.ExecuteOnHitEffects()`
- `Block` -> `CombatEffectExecutor.ExecuteOnBlockEffects()`
- `Parry` -> `CombatEffectExecutor.ExecuteOnParryEffects()`

`CombatEffectExecutor` 目前支持：

- 旧版命中特效 `skillEffectConfig.hitEffectInfo`
- 新版 `SkillEffectData[]`，包括 `onHitEffects`、`onBlockEffects`、`onParryEffects`

特效生成位置通常使用 `CombatHit.HitPoint`。

### 7.3 音效

`SkillConfig` 中已有 `skillAudioConfig` 字段，但当前证据中没有看到统一消费逻辑。建议后续新增 `CombatAudioExecutor`，由 `SkillRunner.ResolveHit()` 在结算后按 `CombatResult.FeedbackKind` 播放：

- `NormalHit`：普通命中音效。
- `HeavyHit`：重击音效。
- `Block`：格挡音效。
- `Parry`：弹反音效。
- `Invincible`：无敌闪避音效。

音效不建议放在 `DamageResolver` 中，因为 `DamageResolver` 应保持纯结算职责。

## 8. 状态反应系统

`CombatReaction.Apply()` 是命中结果驱动角色状态变化的统一入口。

### 8.1 玩家目标

当目标是玩家时：

1. `ShouldDie`：切换到玩家死亡状态。
2. `ShouldEnterUnbalanced`：切换到玩家失衡状态。
3. `ShouldPlayHitReaction`：缓存受击动画名，切换到玩家受击状态。

### 8.2 敌人目标

当目标是敌人时：

1. `ShouldDie`：调用 `enemy.OnDeath()`。
2. `ShouldEnterUnbalanced`：调用 `enemy.OnUnbalanced(attacker)`。
3. `ShouldPlayHitReaction`：缓存受击动画名，调用 `enemy.OnHit(attacker)`。

### 8.3 攻击者反应

弹反可能让攻击者进入失衡：

- 攻击者是玩家：切换到玩家 `UnbalanceState`。
- 攻击者是敌人：调用敌人 `OnUnbalanced()`。

## 9. 动画事件约定

当前战斗高度依赖动画事件：

- `EnableWeaponCollider()`
  - 打开武器碰撞。
  - 清空武器命中列表。
  - 玩家和敌人的状态机还会通知 `SkillRunner.BeginHitWindow()`。

- `DisableWeaponCollider()`
  - 关闭武器碰撞。

- `AttackDecisionWindowOpen()`
  - 打开玩家后摇决策窗口。
  - 允许玩家在窗口中衔接连段、武器技能、翻滚、防御等。

- `SkillCanSwitch(int canSwitch)`
  - 兼容旧动画事件。
  - 当 `canSwitch != 0` 时也会打开攻击决策窗口。

动画事件是命中窗口和连段窗口的关键边界。后续新增动作时，应先确认动画事件是否正确挂载。

## 10. 配置驱动关系

### 10.1 玩家技能配置

玩家技能按武器类型加载：

- 单手剑：`Data/WeaponConfig/SingleSwordSkillConfig.json`
- 大剑：`Data/WeaponConfig/GreatSwordSkillConfig.json`

玩家攻击入口会同时写入 `WeaponType` 和 `AttackId`，因此同一个技能 ID 在不同武器类型下可以有不同配置。

### 10.2 敌人技能配置

敌人技能走通用技能配置表，由 `ConfigManager.GetSkillConfig(int id)` 读取。

### 10.3 武器数据

武器 prefab 或武器对象上的 `WeaponData` 决定：

- 当前武器类型。
- 使用哪个 Animator Override。
- 普通攻击连段 ID。
- 1/2/3 键武器技能 ID。
- 当前武器模型中的命中检测器。

## 11. 扩展建议

### 11.1 加格挡特效

推荐位置：

- 配置侧：在技能 `onBlockEffects` 中配置资源路径和偏移。
- 运行侧：继续走 `CombatEffectExecutor.ExecuteOnBlockEffects()`。

如果需要所有技能都有默认格挡火花，可以增加一个全局反馈配置，例如 `CombatFeedbackEffectConfig`，不要把默认路径硬编码在 `DamageResolver` 中。

### 11.2 加格挡受击反应

推荐改法：

1. 在 `CombatResult` 增加格挡反应字段，例如：
   - `ShouldPlayBlockReaction`
   - `BlockReactionName`
2. 在 `DamageResolver.ResolveBlock()` 中填充这些字段。
3. 在 `CombatReaction.Apply()` 玩家分支中识别 `Block` 结果。
4. 新增 `PlayerBlockHitState`，负责播放格挡受击动画；后退表现放进对应动画或 Root Motion，不在状态代码里额外位移。

不建议把复杂格挡受击逻辑塞进 `DefenceState`，因为 `DefenceState` 当前职责是持续防御和移动输入，格挡硬直是一次性反应。

### 11.3 加弹反特效与音效

推荐位置：

- 弹反特效：`CombatEffectExecutor.ExecuteOnParryEffects()`。
- 弹反音效：新增 `CombatAudioExecutor.Play(result, hit, config)`。
- 调用点：`SkillRunner.ResolveHit()` 中 `CombatHitStopController.Play()` 和 `ExecuteEffects()` 附近。

音效字段可以优先复用 `SkillConfig.skillAudioConfig`，如果配置结构不够用，再扩展为命中、格挡、弹反、重击分开的音效字段。

### 11.4 加玩家被重攻击反应

当前 `DamageResolver.ResolveNormalHitFeedbackKind()` 已经能区分 `HeavyHit`：

- 目标失衡。
- 稳定值伤害达到阈值。
- 技能类型是 `WeaponSkill`。

推荐改法：

1. 在 `ResolveNormalHit()` 中，当 `FeedbackKind == HeavyHit` 时设置重受击动画名。
2. 若只需要不同动画，可继续复用 `GetHitState` 和 `HitReactionName`。
3. 若需要后退、击飞、硬直时间等动作控制，建议新增字段：
   - `ReactionMotionKind`
   - `KnockbackDistance`
   - `KnockbackDuration`
4. `CombatReaction.Apply()` 将这些字段交给 `GetHitState` 或新的 `HeavyGetHitState`。

### 11.5 加音频系统

建议新增：

- `CombatAudioExecutor`
  - 输入：`CombatHit`、`CombatResult`、`SkillConfig`。
  - 根据 `FeedbackKind` 选择音效。
  - 负责空间位置，通常用 `hit.HitPoint`。

- `CombatAudioConfig`
  - 普通命中音效。
  - 重击音效。
  - 格挡音效。
  - 弹反音效。
  - 无敌音效。

不要让 `DamageResolver` 直接播放音效。结算层只产出结果，表现层消费结果。

## 12. 当前风险与注意点

1. `MonsterDeadEventArgs` 目前只看到订阅，没有看到死亡时发出事件。敌人死亡后刷怪计数可能没有被正确回收。
2. 格挡目前主要影响结算和特效，不会默认驱动格挡受击动画。
3. `skillAudioConfig` 已存在，但缺少统一播放入口。
4. 命中依赖标签：玩家武器检测 `Enemy`，敌人武器检测 `Player`。Prefab 标签错误会导致无法命中。
5. 命中窗口依赖动画事件。动画事件漏配会导致攻击没有判定或判定时间错误。
6. `SkillRunner` 会过滤同一技能上下文中的重复目标；多段攻击需要每个命中窗口调用 `BeginHitWindow()`。
7. `DamageResolver` 的结算顺序不要轻易调整，否则会影响防御、弹反、无敌优先级。

## 13. 推荐开发规范

1. 新增战斗数值时，优先放到配置结构中。
2. 新增结算语义时，优先扩展 `CombatResult`，由表现层消费。
3. 新增特效和音效时，不要放进 `DamageResolver`。
4. 新增玩家或敌人反应时，优先从 `CombatReaction.Apply()` 分发到 FSM 状态。
5. 新增命中类型时，先确认它属于 `ResultType` 还是 `FeedbackKind`：
   - `ResultType` 表示结算分支，例如命中、格挡、弹反。
   - `FeedbackKind` 表示表现强度或反馈类型，例如普通命中、重命中。
6. 修改战斗系统后必须用 `$CLI compile unity` 做 Unity 编译验证。

## 14. 一句话总结

当前战斗系统的核心设计是：角色状态负责释放技能，武器碰撞负责发现命中，`SkillRunner` 负责把命中转成 `CombatHit`，`DamageResolver` 负责产出 `CombatResult`，最后由停顿、特效和 `CombatReaction` 负责表现与状态切换。后续扩展格挡、弹反、重击、音效时，应尽量沿着这条链路增加字段和表现消费点，避免把结算、表现和状态反应混在同一个类里。

## 15. 防御受击表现约定

玩家防御成功后，`DamageResolver.ResolveBlock()` 会根据稳定值伤害产出 `BlockReactionType`：

- `Light`：播放 `DefenceHit_Light`，短促轻微后退。
- `Medium`：播放 `DefenceHit_Medium`，常规后退。
- `Heavy`：播放 `DefenceHit_Heavy`，明显后退。
- 稳定值被打空时优先进入 `Unbalance`，不播放普通格挡受击。

代码层只负责选择和播放动画，不再根据命中方向主动移动玩家。轻、中、重三档的后退幅度应直接做进 `DefenceHit_Light`、`DefenceHit_Medium`、`DefenceHit_Heavy` 动画，或由这些动画的 Root Motion 承担。

Animator 需要提供以下状态或可被 `TryCrossFadeInFixedTime` 播放的动画名：

- `DefenceHit_Light`
- `DefenceHit_Medium`
- `DefenceHit_Heavy`

格挡火花仍通过技能配置的 `onBlockEffects` 播放；格挡音效后续应通过统一的 `CombatAudioExecutor` 接入，不放进 `DamageResolver`。
