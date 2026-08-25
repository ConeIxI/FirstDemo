# Character 模块重构设计：换装系统

## 概述

重构玩家 Character 模块，引入武器数据驱动 + Animator Override Controller 方案，支持 4-6 种武器类型的换装系统。防具（头盔/护甲/腿甲）通过背包 UI 更换，武器有 2 个槽位可随时切换并播放过渡动画。

## 需求

- **武器类型**: 4-6 种（单手剑、双手剑、双持匕首、弓箭、法杖、长枪等）
- **武器槽位**: 2 个，战斗中可随时通过快捷键切换
- **武器切换动画**: 有收刀/拔刀过渡动画
- **防具**: 头盔/护甲/腿甲，仅在背包 UI 中更换，不影响动画和状态机
- **动画方案**: Animator Override Controller，每种武器一个 Override Controller
- **重构范围**: 玩家模块 + CharacterStateMachine 基类改善，Enemy 模块不动

## 当前架构问题

1. **动画名硬编码** — 每个状态写死动画名（`"Walk"`、`"Idle"` 等），无法根据武器类型切换动画
2. **技能硬编码** — `PlayerSkillManager.InitSkill()` 写死 3 个 `PlayerSkillAttack1`
3. **AttackState 强依赖具体技能类** — 直接强转 `(PlayerSkillAttack1)`
4. **武器检测器单一** — `CharacterStateMachine` 只有一个 `WeaponHitDetector`，无法切换
5. **缺少武器类型概念** — 系统中无区分武器类型的数据结构

## 设计方案

### 1. 数据模型

#### 1.1 WeaponType 枚举

```csharp
public enum WeaponType
{
    None,           // 空手
    SingleSword,    // 单手剑
    GreatSword,     // 双手剑
    DualDagger,     // 双持匕首
    Bow,            // 弓箭
    Staff,          // 法杖
    Spear,          // 长枪
}
```

#### 1.2 EquipmentType 枚举

```csharp
public enum EquipmentType
{
    Helmet,     // 头盔
    Armor,      // 护甲
    Leggings,   // 腿甲
}
```

#### 1.3 WeaponData (ScriptableObject)

每种武器一个配置文件：

| 字段 | 类型 | 说明 |
|------|------|------|
| weaponType | WeaponType | 武器类型 |
| weaponName | string | 武器名称 |
| weaponModelPath | string | Addressable 路径，加载武器模型预制体 |
| animatorOverride | AnimatorOverrideController | 该武器类型的动画覆盖 |
| weaponHoldBone | string | 武器持握骨骼名（如 "Hand_R"） |
| sheathBone | string | 收刀时挂点骨骼名 |
| skillIds | int[] | 该武器的技能组 |
| hitDetectorConfig | HitDetectorConfig | 碰撞检测配置 |

`HitDetectorConfig` 定义：

| 字段 | 类型 | 说明 |
|------|------|------|
| colliderCenter | Vector3 | 碰撞体中心偏移 |
| colliderSize | Vector3 | 碰撞体大小（BoxCollider）|
| detectorPrefabPath | string | 可选，自定义检测器预制体路径（为空时使用默认 BoxCollider 配置）|

#### 1.4 EquipmentData (ScriptableObject)

| 字段 | 类型 | 说明 |
|------|------|------|
| equipmentType | EquipmentType | 装备类型 |
| equipmentName | string | 装备名称 |
| modelPath | string | 模型 Addressable 路径 |
| mountBone | string | 挂载骨骼 |

### 2. 核心组件

#### 2.1 EquipmentManager

管理所有装备槽位，挂载在玩家 GameObject 上。

**职责**:
- 管理 2 个武器槽位和防具槽位
- 提供装备/卸载/切换接口
- 武器切换完成后触发 `OnWeaponChanged` 事件
- 防具更换后触发 `OnEquipmentChanged` 事件

**核心接口**:
- `EquipWeapon(slotIndex, WeaponData)` — 装备武器到指定槽位
- `UnequipWeapon(slotIndex)` — 卸下武器
- `SwitchWeapon()` — 切换激活武器 (0↔1)
- `GetWeapon(slotIndex): WeaponData` — 获取指定槽位的武器数据
- `SetActiveWeaponIndex(int index)` — 设置当前激活武器索引
- `EquipArmor(EquipmentData)` — 装备防具
- `UnequipArmor(EquipmentType)` — 卸下防具
- `ActiveWeapon: WeaponData` — 当前激活武器属性

**事件机制**: `OnWeaponChanged` 和 `OnEquipmentChanged` 使用 C# 原生事件（`System.Action<WeaponData>` / `System.Action<EquipmentData>`），因为这些事件仅在玩家组件间通信，不需要通过全局 `EventCenter` 广播。

#### 2.2 WeaponHandler

负责武器的物理表现，挂载在玩家 GameObject 上。

**职责**:
- 武器模型的加载/卸载/挂点切换
- Animator Override Controller 的运行时切换
- 提供当前武器的 HitDetector 访问

**核心接口**:
- `ApplyWeapon(WeaponData)` — 加载模型、挂载骨骼、切换 Override Controller
- `RemoveWeapon()` — 卸载当前武器模型
- `MoveWeaponToBone(string boneName)` — 将武器移到指定骨骼（收刀/拔刀）
- `GetActiveHitDetector(): WeaponHitDetector` — 获取当前武器碰撞检测器

#### 2.3 PlayerController 改造

新增引用：
- `equipmentManager: EquipmentManager`
- `weaponHandler: WeaponHandler`

保留 `skillManager: PlayerSkillManager`，但技能初始化方式改变。

#### 2.4 PlayerSkillManager 改造

- **删除** `InitSkill()` 中的硬编码
- **新增** `LoadSkillsForWeapon(WeaponData)` — 根据 `weaponData.skillIds` 加载技能组
- **新增** `ClearSkills()` — 清空当前技能

武器切换时由 `EquipmentManager.OnWeaponChanged` 触发 `LoadSkillsForWeapon`。

#### 2.5 CharacterStateMachine 基类改造

- **移除** `weaponDetector` 字段直接引用
- **新增** `WeaponHandler` 属性
- `EnableWeaponCollider()` / `DisableWeaponCollider()` 改为委托给 `WeaponHandler.GetActiveHitDetector()`

#### 2.6 SkillBase 改造

- **新增** 虚方法 `RegisterHandler()` / `UnRegisterHandler()`（默认空实现），使 `AttackState` 不再需要强转具体技能类
- **新增** 虚方法 `SetWeaponReference(WeaponHandler weaponHandler)` — 技能不再在构造时缓存武器引用，而是每次从 WeaponHandler 动态获取

`AttackState` 改造后的伪代码：
```csharp
// 字段类型从 PlayerSkillAttack1 改为 SkillBase
private SkillBase _skill;

public override void Enter(FsmBase<PlayerStateMachine> fsm)
{
    _skillConfig = ConfigManager.Instance.GetSkillConfig((int)fsm.GetData("AttackId"));
    // 不再强转，直接使用 SkillBase
    _skill = fsm.Owner.PlayerController.SkillManager.GetSkill(_skillConfig.skillId);
    _skill.RegisterHandler();  // 虚方法调用
    if (!_skill.Cast()) { fsm.ChangeState<IdleState>(); }
    fsm.Owner.CrossFadeInFixedTime(_skillConfig.skillAnimationName);
    // ... 其余不变
}
```

`PlayerSkillAttack1` 改造要点：
- `_weapon` 字段不再在构造时通过 `GetComponentInChildren` 缓存
- 改为在 `RegisterHandler()` / `OnSkillStart()` 中通过 `WeaponHandler.GetActiveHitDetector()` 动态获取武器引用
- `LoadSkillsForWeapon` 重新创建技能实例时传入 `WeaponHandler` 引用

#### 2.7 IdleState / WalkState 攻击键改造

当前硬编码 `fsm.SetData("AttackId", 10001)` 需要改为从当前武器数据获取：

```csharp
// 改造前
fsm.SetData("AttackId", 10001);

// 改造后
WeaponData activeWeapon = fsm.Owner.PlayerController.EquipmentManager.ActiveWeapon;
if (activeWeapon != null && activeWeapon.skillIds.Length > 0)
{
    fsm.SetData("AttackId", activeWeapon.skillIds[0]);  // 首段攻击
    fsm.ChangeState<AttackState>();
}
```

### 3. 武器切换流程

#### 3.1 WeaponSwitchState（新增状态）

两阶段流程，使用内部枚举管理阶段：

```csharp
private enum SwitchPhase { Sheath, Draw }
private SwitchPhase m_phase;
private WeaponData m_targetWeapon;
private int m_targetIndex;

public override void Enter(FsmBase<PlayerStateMachine> fsm)
{
    fsm.Owner.CurState = PlayerState.WeaponSwitch;
    int targetIndex = (int)fsm.GetData("TargetWeaponIndex");
    m_targetIndex = targetIndex;
    m_targetWeapon = fsm.Owner.PlayerController.EquipmentManager.GetWeapon(targetIndex);
    m_phase = SwitchPhase.Sheath;
    fsm.Owner.CrossFadeInFixedTime("WeaponSheath");
}

public override void Update(FsmBase<PlayerStateMachine> fsm, float deltaTime)
{
    switch (m_phase)
    {
        case SwitchPhase.Sheath:
            // 使用 normalizedTime >= 1 检测（收刀/拔刀为非循环动画）
            if (fsm.Owner.IsPlayingAnimation("WeaponSheath", out float sheathProgress))
            {
                if (sheathProgress >= 1f)
                {
                    // 收刀完成 → 切换武器
                    var weaponHandler = fsm.Owner.PlayerController.WeaponHandler;
                    weaponHandler.RemoveWeapon();
                    weaponHandler.ApplyWeapon(m_targetWeapon);  // 同步加载
                    fsm.Owner.PlayerController.EquipmentManager.SetActiveWeaponIndex(m_targetIndex);
                    m_phase = SwitchPhase.Draw;
                    fsm.Owner.CrossFadeInFixedTime("WeaponDraw");
                }
            }
            break;
        case SwitchPhase.Draw:
            if (fsm.Owner.IsPlayingAnimation("WeaponDraw", out float drawProgress))
            {
                if (drawProgress >= 1f)
                {
                    fsm.ChangeState<IdleState>();
                }
            }
            break;
    }
}

public override void Exit(FsmBase<PlayerStateMachine> fsm)
{
    // 重新加载技能组
    fsm.Owner.PlayerController.SkillManager.LoadSkillsForWeapon(m_targetWeapon);
}
```

**可打断性规则**:
- `WeaponSwitchState` 默认**不可被打断**，Update 中不检测任何输入
- 唯一例外：`GetHitState` 可以通过外部调用 `ChangeState<GetHitState>()` 强制打断
- 如果被 `GetHitState` 打断：`Exit` 中仍会触发 `LoadSkillsForWeapon`，确保技能组与当前武器同步。`WeaponHandler.ApplyWeapon` 在 Sheath 阶段完成前被打断时新武器尚未加载，此时保持旧武器状态不变（因为 RemoveWeapon 和 ApplyWeapon 是连续执行的）

**武器模型加载方式**: `ApplyWeapon` 使用 `ResourceManager.Instantiate`（同步加载），避免异步带来的状态同步问题。

#### 3.2 Animator Controller 层级关系

**基础 Animator Controller**（不被 Override 的部分）:
- `WeaponSheath` — 收刀动画
- `WeaponDraw` — 拔刀动画
- 所有状态节点的名称（`Idle`、`Walk`、`Attack1` 等）和过渡条件

**Override Controller 覆盖的动画 Clip**:
- `Idle` → 各武器类型的待机动画 clip
- `Walk` → 各武器类型的行走/跑步动画 clip
- `JumpStart` / `JumpLoop` / `JumpEnd` → 各武器类型的跳跃动画 clip
- `Roll` → 各武器类型的翻滚动画 clip
- `Attack1` / `Attack2` / `Attack3` → 各武器类型的攻击连招动画 clip
- `GetHit` → 各武器类型的受击动画 clip
- `EnterDefence` / `ExitDefence` → 各武器类型的格挡动画 clip
- `RunStop` → 各武器类型的急停动画 clip

**关键点**: Override Controller 只替换动画 clip，不改变状态机拓扑结构和过渡条件。收刀/拔刀动画对所有武器类型通用，放在基础 Controller 中不被覆盖。

#### 3.3 切换限制

仅在以下状态允许触发武器切换：
- `IdleState`
- `WalkState`

以下状态不允许切换：Attack、Roll、Jump、AirDown、GetHit、Defence、RunStop

#### 3.3 时序

```
玩家按下 Tab
  → IdleState/WalkState 检测到输入
  → fsm.ChangeState<WeaponSwitchState>()
  → 播放收刀动画
  → 收刀完成 → WeaponHandler 卸载旧武器、加载新武器、切换 Override Controller
  → 播放拔刀动画
  → 拔刀完成 → SkillManager 重新加载技能组
  → ChangeState<IdleState>()
  → Idle 动画自动使用新武器的动画
```

#### 3.4 InputManager 新增

```csharp
public bool IsWeaponSwitchKeyPressed()
{
    return Input.GetKeyDown(KeyCode.Tab);
}
```

### 4. 防具换装流程

纯模型替换，不涉及状态机和动画：

```
玩家在背包 UI 中点击装备
  → EquipmentManager.EquipArmor(equipmentData)
  → 根据 mountBone 找到骨骼
  → 卸载旧模型（如果有）
  → ResourceManager 加载新模型挂载到骨骼
  → 触发 OnEquipmentChanged 事件
```

### 5. 文件变更清单

#### 新增文件

| 文件路径 | 说明 |
|----------|------|
| `Assets/Game/Character/Common/WeaponType.cs` | 武器类型枚举 |
| `Assets/Game/Character/Common/EquipmentType.cs` | 装备类型枚举 |
| `Assets/Game/Character/Equipment/EquipmentManager.cs` | 装备管理器 |
| `Assets/Game/Character/Equipment/WeaponHandler.cs` | 武器物理表现处理 |
| `Assets/Game/Character/Equipment/WeaponData.cs` | ScriptableObject |
| `Assets/Game/Character/Equipment/EquipmentData.cs` | ScriptableObject |
| `Assets/Game/Character/Player/PlayerFsm/WeaponSwitchState.cs` | 武器切换状态 |

#### 修改文件

| 文件路径 | 改动说明 |
|----------|----------|
| `Assets/Game/Character/CharacterStateMachine.cs` | 移除 weaponDetector 直接引用，增加 WeaponHandler 属性，EnableWeaponCollider/DisableWeaponCollider 委托给 WeaponHandler |
| `Assets/Game/Character/Player/PlayerController.cs` | 增加 EquipmentManager、WeaponHandler 引用 |
| `Assets/Game/Character/Player/PlayerSkillManager.cs` | 去掉硬编码，增加 LoadSkillsForWeapon/ClearSkills |
| `Assets/Game/Character/Player/PlayerDefine.cs` | PlayerState 枚举增加 WeaponSwitch |
| `Assets/Game/Character/Player/PlayerStateMachine.cs` | _getPlayerStates 增加 WeaponSwitchState |
| `Assets/Game/Character/Player/PlayerFsm/AttackState.cs` | 去掉 PlayerSkillAttack1 强转，使用 SkillBase 虚方法 |
| `Assets/Game/Character/Player/PlayerFsm/IdleState.cs` | 增加武器切换输入检测 |
| `Assets/Game/Character/Player/PlayerFsm/WalkState.cs` | 增加武器切换输入检测 |
| `Assets/Framework/Manager/InputManager.cs` | 增加 IsWeaponSwitchKeyPressed |
| `Assets/Game/Battle/Skill/SkillBase.cs` | 增加虚方法 RegisterHandler/UnRegisterHandler |

#### 不变文件

- `FsmBase<T>` / `FsmStateBase<T>` — 框架层 FSM
- `EventCenter` / `EventArgsBase` — 事件系统
- `ResourceManager` / `ConfigManager` — 管理器
- `CharacterController.cs` — 基类控制器
- Enemy 模块全部
- `JumpState`、`RollState`、`AirDownState`、`RunStopState`、`DefenceState`、`GetHitState`

### 6. 补充说明

#### 6.1 空手状态（WeaponType.None）

`WeaponType.None` 表示无武器装备。此时：
- 不加载 Override Controller，使用基础 Controller 的默认动画
- `ActiveWeapon` 返回 null，`skillIds` 为空
- `IdleState` / `WalkState` 中检测到 `ActiveWeapon == null` 时，不响应攻击键输入
- 不允许触发武器切换（没有武器可切）

#### 6.2 Combo 连招与武器切换

连招关系（第一招接第二招接第三招）仍存储在 `SkillConfig` 的 JSON 中（`comboNextSkillId` 字段）。每种武器的技能配置需要在 `Assets/Data/PlayerSkillConfig.json` 中添加对应的 combo 链数据。`WeaponData.skillIds[0]` 是该武器的首段攻击入口，后续 combo 由 `SkillConfig.comboNextSkillId` 自动串联。
