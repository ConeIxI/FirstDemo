# 玩家战斗姿态与武器拔刀收刀设计

## 1. 目标

为玩家增加由 `ArmsLayer` 播放的拔刀、收刀和战斗中换武器表现，同时保持现有玩家 FSM 的动作状态结构。拔刀和收刀不是独立 FSM 状态，真实战斗姿态由 `PlayerStateMachine` 持有，`LocomotionState` 只负责动画表现和流程推进。

本设计覆盖以下行为：

- 玩家装备武器时显示对应模型，卸下时完整隐藏对应模型。
- 非战斗状态下，两把已装备武器同时显示在各自收纳位置。
- 非战斗切武器只改变内部当前武器数据，表现层不变化。
- 战斗切武器依次播放旧武器收刀和新武器拔刀。
- 攻击或技能可直接进入战斗，不播放拔刀动画。
- 被敌人设为战斗目标时，玩家播放拔刀动画进入战斗。
- 无敌人锁定且连续 3 秒无战斗行为时，玩家播放收刀动画退出战斗。
- 动画被其他玩家状态打断时，流程仍结算到明确的最终结果。

## 2. 已确认的业务口径

### 2.1 武器显示

- 玩家有两个武器槽。
- 非战斗状态下，两个已装备槽都显示各自的收纳模型。
- 战斗状态下，当前武器显示手持模型，另一把已装备武器继续显示收纳模型。
- 未装备槽的手持模型和收纳模型都必须隐藏。
- 装备栏操作不播放拔刀或收刀动画，直接同步最终表现。
- 卸下当前武器后若仍有另一把武器，立即将其设为当前武器，并按当前战斗姿态显示。

### 2.2 战斗状态

- Animator 参数名为 `IsCombat`，类型为 `float`。
- `IsCombat = 0` 表示非战斗状态，`IsCombat = 1` 表示战斗状态。
- 普通拔刀动画结束时写入 `IsCombat = 1`。
- 普通收刀动画结束时写入 `IsCombat = 0`。
- 战斗中换武器期间始终保持 `IsCombat = 1`。
- 攻击、技能和受击会刷新战斗活跃时间。
- 只有玩家处于 `LocomotionState`、没有敌人锁定且连续 3 秒没有战斗行为时，自动收刀计时才会完成。
- 没有装备武器时若被敌人锁定，玩家直接设置 `IsCombat = 1`，不播放无武器可表现的拔刀动画；锁定解除并满足退出条件后直接设置 `IsCombat = 0`。战斗中首次装备武器时按装备栏规则直接显示在手。

### 2.3 敌人锁定

“被敌人锁定”严格定义为：至少一个存活敌人的 `EnemyBlackboard.CombatTarget` 指向玩家。

以下情况不算敌人锁定玩家：

- 敌人只有警戒记忆或正在搜索玩家最后位置。
- 玩家通过 `LockOnManager` 主动锁定敌人。
- 敌人的战斗目标已经释放、死亡或禁用。

玩家使用敌人集合记录锁定来源。只有最后一个锁定玩家的敌人释放目标后，玩家才允许开始自动收刀计时。

## 3. 总体架构

### 3.1 PlayerStateMachine：唯一战斗姿态数据源

`PlayerStateMachine` 持有战斗姿态上下文，包括：

- 当前稳定姿态：非战斗或战斗。
- 当前过渡阶段。
- 自动收刀计时。
- 正在锁定玩家的敌人集合。
- 战斗换武器的源槽位和目标槽位。

过渡阶段只用于表达内部流程，不注册为玩家 FSM 状态。建议阶段如下：

- `None`
- `EnteringCombat`
- `ExitingCombat`
- `SwitchingWeaponExit`
- `SwitchingWeaponEnter`

`PlayerStateMachine` 提供明确的业务入口：

- 请求播放拔刀进入战斗。
- 不播放动画并立即进入战斗。
- 请求自动收刀。
- 请求切换武器。
- 刷新战斗活跃时间。
- 推进或完成当前动画过渡。
- 在离开 `LocomotionState` 时结算被打断的过渡。

### 3.2 LocomotionState：动画流程执行者

`LocomotionState` 负责：

- 通过层名查找并缓存 `ArmsLayer` 索引。
- 在该层播放 `EnterCombat` 和 `ExitCombat`。
- 检查动画归一化进度并通知战斗姿态上下文完成当前阶段。
- 在允许条件下推进 3 秒自动收刀计时。
- 在退出状态时结算未完成的姿态过渡。

`LocomotionState` 不拥有真实战斗状态。攻击、技能、受击、翻滚等状态打断它后，战斗姿态上下文仍然有效。

### 3.3 EquipmentManager：数据与表现解耦

现有 `ApplyActiveWeaponState` 同时修改当前武器数据和武器显隐，需要拆分为两类职责：

- 当前武器数据同步：更新 `ActiveWeaponIndex`、命中检测器、技能、AnimatorOverride、装备属性及相关事件。
- 武器姿态同步：根据已装备槽、当前武器和战斗姿态刷新手持或收纳对象。

非战斗切武器只执行第一类同步。战斗切武器在旧武器收刀结束后执行第一类同步，再开始新武器拔刀。

### 3.4 PlayerEquipmentAppearance：双槽手持与收纳表现

移除当前 `HideAll` 后只显示 active 武器的规则。每个武器槽、每种可装备武器都配置独立表现项：

- 逻辑武器数据 `WeaponData`。
- 手持对象。
- 收纳对象。

表现接口按槽位工作：

- `ShowSheathed(slotIndex)`：隐藏该槽手持对象，显示收纳对象。
- `ShowInHand(slotIndex)`：隐藏该槽收纳对象，显示手持对象。
- `Hide(slotIndex)`：同时隐藏该槽两类对象。
- `ApplyCombatAppearance(activeSlotIndex, isCombat)`：统一校正两个槽的最终表现。

两个槽必须使用彼此独立的表现对象，避免相同武器配置被两个槽引用时共享同一 GameObject。

### 3.5 敌人目标变化通知

`EnemyBlackboard` 在 `CombatTarget` 真正发生变化时发出通知。`AIController` 将其转换为全局强类型事件，事件至少携带：

- 敌人身份。
- 旧目标。
- 新目标。

敌人禁用、死亡或释放目标时必须发出对应移除通知。`PlayerStateMachine` 订阅事件，并以敌人身份维护集合，避免重复通知破坏锁定计数。

## 4. Animator 与资源约定

- 动画层名称：`ArmsLayer`。
- 拔刀动画名称：`EnterCombat`。
- 收刀动画名称：`ExitCombat`。
- 战斗参数名称：`IsCombat`，类型为 `float`。
- 所有玩家武器 AnimatorOverrideController 都必须提供可播放的 `EnterCombat` 和 `ExitCombat`。
- `EnterCombat` 在武器离开收纳位置的关键帧调用 `OnEnterCombatWeaponEvent`。
- `ExitCombat` 在武器回到收纳位置的关键帧调用 `OnExitCombatWeaponEvent`。

两个动画事件均设计为幂等调用。事件负责关键帧表现，动画结束和中断结算负责最终状态校正，因此动画事件缺失、重复或未执行完都不会留下半完成显隐状态。

## 5. 详细流程

### 5.1 敌人锁定触发普通拔刀

1. 玩家收到第一个敌人将 `CombatTarget` 设置为玩家的事件。
2. 若玩家已在战斗状态，只刷新战斗活跃时间。
3. 若玩家没有装备武器，直接设置 `IsCombat = 1` 并结束流程。
4. 若玩家处于非战斗且当前不在 `LocomotionState`，记录待执行的拔刀请求。
5. 玩家进入 `LocomotionState` 后，在 `ArmsLayer` 播放 `EnterCombat`。
6. 动画事件将当前武器切换为手持表现。
7. 动画完成后设置 `IsCombat = 1`，阶段恢复为 `None`，并校正最终模型显隐。

### 5.2 攻击或技能直接进入战斗

1. 输入处理先验证当前武器和技能是否有效。
2. 在进入攻击或技能 FSM 状态前，立即取消或结算当前收刀流程。
3. 设置 `IsCombat = 1`。
4. 直接将当前武器显示为手持状态，不播放 `EnterCombat`。
5. 刷新战斗活跃时间。
6. 进入攻击或技能状态。

无效攻击或技能输入不会错误地将玩家切入战斗。

### 5.3 自动收刀

1. 玩家处于完整战斗姿态并进入 `LocomotionState`。
2. 当前没有任何敌人以玩家为 `CombatTarget`。
3. 连续 3 秒内没有攻击、技能或受击行为。
4. 若玩家没有装备武器，直接设置 `IsCombat = 0` 并结束流程。
5. 在 `ArmsLayer` 播放 `ExitCombat`。
6. 动画事件将当前武器切回收纳表现。
7. 动画完成后设置 `IsCombat = 0`，阶段恢复为 `None`，并校正两个槽的最终表现。

移动、转向和锁定移动本身不刷新战斗活跃时间。

### 5.4 非战斗切武器

1. 校验至少装备两把武器。
2. 计算下一个已装备槽位。
3. 立即切换 `ActiveWeaponIndex`。
4. 同步命中检测器、技能、AnimatorOverride、属性和事件。
5. 不调用武器显隐切换；两个槽继续显示收纳对象。

### 5.5 战斗中切武器

1. 记录旧槽位和目标槽位，进入 `SwitchingWeaponExit`。
2. 保持 `IsCombat = 1`，使用旧武器 AnimatorOverride 在 `ArmsLayer` 播放 `ExitCombat`。
3. `OnExitCombatWeaponEvent` 将旧武器显示为收纳状态。
4. 旧武器收刀动画结束后，切换 `ActiveWeaponIndex` 并同步新武器数据和 AnimatorOverride。
5. 进入 `SwitchingWeaponEnter`，使用新武器 AnimatorOverride 播放 `EnterCombat`。
6. `OnEnterCombatWeaponEvent` 将新武器显示为手持状态。
7. 新武器拔刀动画结束后恢复阶段 `None`，保持 `IsCombat = 1`，校正最终表现。

切武器请求在已有切换流程进行时不重复入队，也不允许覆盖当前目标槽位。

### 5.6 动画中断

离开 `LocomotionState` 时，根据当前阶段一次性结算：

- `EnteringCombat`：当前武器直接显示在手，设置 `IsCombat = 1`。
- `ExitingCombat`：当前武器直接显示在收纳位，设置 `IsCombat = 0`。
- `SwitchingWeaponExit` 或 `SwitchingWeaponEnter`：切换到目标武器，目标武器直接显示在手，保持 `IsCombat = 1`。

自动收刀过程中受到攻击时，先结算收刀，再由受击行为立即进入战斗，最终结果为当前武器在手且 `IsCombat = 1`，不播放新的拔刀动画。

敌人锁定发生在普通收刀过程中时，先结算收刀，再请求播放一次 `EnterCombat`。

## 6. 装备栏操作

- 装备到空槽：记录逻辑武器；非战斗显示收纳对象，战斗且成为当前武器时显示手持对象。
- 替换非当前槽：旧表现完全隐藏，新武器显示收纳对象。
- 卸下非当前槽：该槽手持和收纳对象都隐藏，不影响当前武器。
- 卸下当前槽且存在备用武器：立即选择备用槽并同步全部当前武器数据；战斗时备用武器直接显示在手，非战斗时保持收纳。
- 卸下最后一把武器：清空当前武器、技能、命中检测器和武器 AnimatorOverride，并将两个槽全部隐藏。

装备栏操作不进入拔刀、收刀或战斗换武器过渡。

## 7. 配置错误与失败行为

- 缺少 `ArmsLayer`、`EnterCombat`、`ExitCombat` 或 `IsCombat` 时输出包含对象和缺失项的明确错误，本次动画过渡立即结算到目标姿态，避免阻塞战斗逻辑。
- 某武器槽缺少逻辑数据、手持对象或收纳对象时，拒绝该次装备操作，不写入半完成槽位数据。
- 动画事件重复触发时保持幂等，不反复翻转模型。
- 敌人目标事件重复时通过集合去重；敌人释放不存在的锁定记录时不改变其他敌人的锁定状态。

## 8. 代码改造范围

主要修改：

- `Assets/Game/Character/Player/PlayerStateMachine.cs`
- `Assets/Game/Character/Player/PlayerFsm/LocomotionState.cs`
- `Assets/Game/Character/Player/PlayerFsm/PlayerStateBase.cs`
- `Assets/Game/Character/Player/Equipment/EquipmentManager.cs`
- `Assets/Game/Character/Player/Equipment/PlayerEquipmentAppearance.cs`
- `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs`
- `Assets/Game/Character/Enemy/AI/AIController.cs`

清理：

- 删除 `Assets/Game/Character/Player/PlayerFsm/WeaponSwitchState.cs`。
- 删除玩家 FSM 对 `WeaponSwitchState` 的注册和跳转。
- 清除玩家旧 `WeaponSheath` 动画名称依赖。

资源修改：

- 玩家 AnimatorController 的 `ArmsLayer`、`IsCombat` 和两个动画。
- 所有武器 AnimatorOverrideController 的拔刀、收刀覆盖。
- `EnterCombat`、`ExitCombat` 动画事件。
- 玩家 Prefab 的双槽手持与收纳表现配置。

实施时必须保留当前工作区中 `CharacterStateMachine.cs`、`LocomotionState.cs` 和 `RollState.cs` 的已有用户改动，并在其基础上合并。

## 9. 测试与验收

### 9.1 编辑模式测试

- 两个槽装备后，非战斗状态同时显示两把收纳武器。
- 卸下任意槽后，该槽手持和收纳对象均隐藏。
- 非战斗切换只改变当前武器数据，不改变模型显隐。
- 战斗切换在旧武器收刀完成前不修改当前武器数据。
- 战斗切换按旧武器收刀、新数据切换、新武器拔刀顺序完成。
- 攻击和技能在非战斗、普通收刀过程中均直接进入战斗。
- 无效攻击或技能输入不触发战斗姿态变化。
- 单个和多个敌人锁定、释放玩家时，敌人集合和自动收刀资格正确。
- 攻击、技能、受击刷新计时；计时只在 `LocomotionState` 且无敌人锁定时推进。
- 每种过渡阶段被中断后都结算到设计规定的最终状态。
- 动画事件重复触发保持幂等。

### 9.2 Unity 验证

1. 使用 `$CLI compile unity` 完成 Unity 编译。
2. 运行相关 EditMode 测试。
3. 在 Play Mode 验证 `ArmsLayer` 不影响下半身移动。
4. 验证普通拔刀、自动收刀、非战斗切武器、战斗切武器和装备栏操作。
5. 在拔刀、收刀和战斗切换的每个阶段触发受击，检查最终状态。
6. 使用两个敌人依次锁定和释放玩家，检查只有最后一个敌人释放后才自动收刀。
7. 检查 Animator 的 `IsCombat` 值、当前 AnimatorOverride、技能集、命中检测器与模型表现保持一致。

## 10. 完成标准

所有已确认流程均通过自动测试和 Play Mode 验证；任何时刻都不存在内部当前武器、`IsCombat`、技能、命中检测器和模型显隐互相矛盾的状态；现有玩家移动、攻击、技能、受击、翻滚和防御流程无回归。
