# 基础战斗与技能框架重构设计

## 目标

在保留现有 FSM、装备切换、动画事件、武器碰撞链路的前提下，重构战斗核心和技能运行层。第一阶段先跑通生命、稳定、战意、防御、格挡、翻滚无敌、打断、霸体和基础命中效果，为后续 buff、位移、投射物、范围技留下稳定扩展点。

现有链路继续保留：

```text
InputManager
-> PlayerStateMachine / EnemyStateMachine
-> AttackState / DefenceState / RollState
-> CharacterStateMachine 动画事件
-> WeaponHitDetector
```

重构后的命中链路：

```text
WeaponHitDetector
-> SkillRunner
-> CombatHit
-> DamageResolver
-> InterruptResolver
-> CombatReaction
-> CombatEffectExecutor
```

## 设计原则

- FSM 只管动作状态切换，不直接处理伤害、格挡、打断。
- SkillRunner 只管技能释放、技能上下文、命中事件监听。
- DamageResolver 是唯一伤害、防御、格挡、稳定、死亡结算入口。
- InterruptResolver 独立处理打断和霸体，不把霸体混进无敌。
- EffectExecutor 执行技能效果，技能类不直接写死 buff、位移、特效逻辑。
- 战意是玩家全武器共享资源，只能通过普通攻击命中回复。

## 推荐目录

```text
Assets/Game/Battle/
├─ Combat/
│  ├─ Combatant.cs
│  ├─ CombatStats.cs
│  ├─ CombatResource.cs
│  ├─ CombatState.cs
│  ├─ CombatHit.cs
│  ├─ CombatResult.cs
│  ├─ DamageResolver.cs
│  ├─ CombatReaction.cs
│  └─ Interrupt/
│     ├─ InterruptData.cs
│     ├─ InterruptResolver.cs
│     └─ InterruptResult.cs
│
├─ Skill/
│  ├─ SkillRunner.cs
│  ├─ SkillContext.cs
│  ├─ SkillDefine.cs
│  ├─ Common/SkillConfig.cs
│  └─ Effects/
│     ├─ CombatEffectExecutor.cs
│     ├─ SkillEffectData.cs
│     └─ ICombatEffectHandler.cs
│
├─ StatusEffect/
│  ├─ StatusEffectSystem.cs
│  ├─ StatusEffectConfig.cs
│  └─ StatusEffectInstance.cs
│
└─ Motion/
   └─ CombatMotionController.cs
```

## 核心模块职责

`Combatant` 是战斗门面，聚合 `CombatStats`、`CombatResource`、`CombatState`、`StatusEffectSystem`、`CombatMotionController` 和 `SkillRunner`，给结算系统提供统一入口。

`CombatStats` 管生命值、稳定值、稳定恢复、死亡判断。玩家和敌人都需要。

`CombatResource` 管战意值。第一阶段只挂玩家，敌人不使用战意。

`CombatState` 管防御、格挡窗口、翻滚无敌、当前动作抗打断、失衡、死亡等运行时状态。

`SkillRunner` 替代 `PlayerSkillAttack` 和 `EnemySkillAttack` 的命中处理。它检查战意、扣战意、注册命中事件、根据当前技能生成 `CombatHit`。

`DamageResolver` 统一处理无敌、防御、格挡、生命伤害、稳定伤害和死亡。

`InterruptResolver` 统一处理打断和霸体。霸体不是免伤，只是不被普通受击动画或打断拉走。

`CombatEffectExecutor` 根据结算结果执行技能效果，例如战意增加、击退、位移、添加 buff、生成特效。

## 技能配置

继续沿用当前 JSON 配置，扩展 `SkillConfig`：

```csharp
public class SkillConfig
{
    public int skillId;
    public string skillName;
    public SkillType skillType;
    public string skillAnimationName;
    public int comboNextSkillId;

    public int battleSpiritCost;
    public int battleSpiritGainOnHit;

    public CombatHitConfig hitConfig;
    public InterruptConfig interruptConfig;

    public SkillEffectData[] onCastEffects;
    public SkillEffectData[] onHitEffects;
    public SkillEffectData[] onBlockEffects;
    public SkillEffectData[] onParryEffects;

    public SkillEffectConfig skillEffectConfig;
    public SkillAudioConfig skillAudioConfig;
}
```

技能类型：

```csharp
public enum SkillType
{
    NormalAttack,
    WeaponSkill,
    EnemySkill
}
```

命中配置：

```csharp
public class CombatHitConfig
{
    public int healthDamage;
    public int stabilityDamage;
    public bool canBeBlocked;
    public bool canBeParried;
    public float hitStopTime;
    public string hitReactionName;
}
```

打断配置：

```csharp
public class InterruptConfig
{
    public bool canInterrupt;
    public int interruptLevel;
    public bool canBeInterrupted;
    public int interruptResistLevel;
    public bool canInterruptDefence;
}
```

效果配置：

```csharp
public class SkillEffectData
{
    public SkillEffectType effectType;
    public SkillEffectTarget target;
    public float value;
    public float duration;
    public int statusEffectId;
    public string prefabPath;
    public Vec3 direction;
}
```

```csharp
public enum SkillEffectType
{
    Damage,
    StabilityDamage,
    AddBattleSpirit,
    ApplyStatusEffect,
    Displace,
    Knockback,
    Pull,
    Launch,
    Invincible,
    SpawnFx,
    PlaySfx
}
```

第一阶段实际实现 `Damage`、`StabilityDamage`、`AddBattleSpirit`、`SpawnFx`、`Knockback` 或 `Displace`。`ApplyStatusEffect` 先保留通路，复杂 buff 后续实现。

## 战意规则

战意是玩家全武器共享资源。

```text
普通攻击命中 -> 回复战意
武器技能释放 -> 消耗战意
技能命中 -> 不回复战意
防御成功 -> 不回复战意
格挡成功 -> 不回复战意
敌人 -> 不使用战意
```

`battleSpiritGainOnHit` 只对 `SkillType.NormalAttack` 生效：

```text
if skillType == NormalAttack && result == Hit
    AddBattleSpirit(battleSpiritGainOnHit)
```

## 防御、格挡、翻滚

结算优先级：

```text
死亡
-> 无敌/翻滚
-> 格挡
-> 防御
-> 正常命中
```

`DefenceState.Enter()`：

```text
CombatState.BeginDefence()
CombatState.OpenParryWindow(parryWindowTime)
```

格挡窗口内被命中是 `Parry`，窗口结束后继续按住防御键是 `Block`。

`Block`：

```text
目标不扣生命
目标扣稳定
不回复战意
```

`Parry`：

```text
目标不扣生命
目标不扣稳定
攻击者扣稳定
可触发攻击者硬直或受击反应
不回复战意
```

`RollState.Enter()` 开启短暂无敌，第一阶段可用时间窗口控制。后续如果要精确到动画帧，再改成动画事件开启和关闭无敌。

## 打断和霸体

打断只在正常命中后判断。`Block`、`Parry`、`Invincible` 不触发普通打断。

判断规则：

```text
目标防御中 -> 默认不可打断，除非 canInterruptDefence = true
攻击没有 canInterrupt -> 不打断
目标当前动作 canBeInterrupted = false -> 不打断
interruptLevel < target.interruptResistLevel -> 不打断
否则打断
```

默认配置建议：

```text
普通攻击前几段：
canInterrupt=false
canBeInterrupted=true
interruptResistLevel=0

普通攻击最后一段：
canInterrupt=true
interruptLevel=1
canBeInterrupted=true
interruptResistLevel=0

普通武器技能：
canInterrupt=true
interruptLevel=1 或 2
canBeInterrupted=true 或 false，按技能设计

敌人霸体技能：
canBeInterrupted=false
interruptResistLevel=99

防御状态：
默认不能被打断
```

霸体期间仍然会掉血、掉稳定、吃 buff，只是不进入普通受击动画，也不取消当前技能。

## 一次命中结算流程

```text
1. SkillRunner 收到武器命中事件
2. 生成 CombatHit
3. attacker.StatusEffectSystem.OnBeforeDealHit(hit)
4. target.StatusEffectSystem.OnBeforeReceiveHit(hit)
5. DamageResolver 判断无敌、格挡、防御、正常命中
6. 应用生命、稳定、战意变化
7. InterruptResolver 判断是否打断
8. attacker.StatusEffectSystem.OnAfterDealHit(result)
9. target.StatusEffectSystem.OnAfterReceiveHit(result)
10. CombatReaction 推动 FSM 反应
11. CombatEffectExecutor 执行 onHit/onBlock/onParry 效果
```

`CombatResult` 至少包含：

```text
ResultType: None / Invincible / Block / Parry / Hit / Dead
HealthDamageApplied
StabilityDamageApplied
BattleSpiritGained
IsInterrupted
ShouldCancelCurrentSkill
ShouldPlayHitReaction
ShouldEnterUnbalanced
ShouldDie
```

## 玩家接入

玩家新增组件：

```text
Combatant
CombatStats
CombatResource
CombatState
StatusEffectSystem
CombatMotionController
SkillRunner
```

`EquipmentManager.ApplyActiveWeaponState()` 继续负责切武器模型、`WeaponHandler`、动画覆盖和技能列表。`PlayerSkillManager.LoadSkillsForWeapon()` 不再创建多个 `PlayerSkillAttack`，改为记录当前武器可释放的技能 ID，并把释放请求交给 `SkillRunner`。

`IdleState` 和 `WalkState` 仍然在左键普攻时取当前武器 `skillIds[0]` 进入 `AttackState`。

后续增加三个武器技能键时，按键只需要从当前武器技能列表里取对应 `WeaponSkill`，检查战意后进入 `AttackState` 或单独的 `SkillState`。第一阶段先复用 `AttackState`。

## 敌人接入

敌人新增组件：

```text
Combatant
CombatStats
CombatState
StatusEffectSystem
CombatMotionController
SkillRunner
```

敌人第一阶段不挂 `CombatResource`。

`EnemySkillManager` 不再手写创建 `EnemySkillAttack(20001/20002/20003)`。第一阶段可以继续用 `GuardStateMachine.firstAttackSkillId` 作为首段攻击入口，但技能实例统一交给 `SkillRunner`。

## FSM 接入

`AttackState` 继续负责：

```text
读取 skillId
读取 SkillConfig
播放 skillAnimationName
处理 comboNextSkillId
处理翻滚取消或连段
退出时结束技能
```

进入攻击：

```text
SkillRunner.Cast(skillId)
CombatState.BeginAction(skillConfig.interruptConfig)
```

退出攻击：

```text
SkillRunner.CancelCurrentSkill()
CombatState.EndAction()
```

`CombatReaction` 根据 `CombatResult` 推动状态：

```text
玩家受击 -> PlayerStateMachine.ChangeState<GetHitState>()
敌人受击 -> EnemyStateMachine.ChangeState<GetHitState>()
失衡 -> 第一阶段先复用 GetHitState，后续补 UnbalancedState
死亡 -> 后续补 DeadState
```

## 旧类处理

保留：

```text
PlayerStateMachine / EnemyStateMachine
AttackState / DefenceState / RollState / GetHitState
EquipmentManager / WeaponHandler / WeaponData
CharacterStateMachine.EnableWeaponCollider()
CharacterStateMachine.DisableWeaponCollider()
CharacterStateMachine.SkillStart()
CharacterStateMachine.SkillEnd()
WeaponHitEventArgs / EnemyWeaponHitEventArgs
SkillStartEventArgs / SkillEndEventArgs
```

替换或废弃：

```text
PlayerSkillAttack
EnemySkillAttack
PlayerSkillManager 中按武器创建多个 PlayerSkillAttack 的逻辑
EnemySkillManager 中手写创建 20001/20002/20003 的逻辑
```

`SkillBase` 可以短期保留接口名，最终由 `SkillRunner` 替代它的主要职责。

## 第一阶段范围

第一阶段要实现：

```text
生命值
稳定值
战意值
普通命中
防御
格挡窗口
翻滚无敌
普攻最后一段打断
敌人霸体技能不可打断
命中特效兼容
HUD 显示生命、稳定、战意
```

第一阶段暂不实现：

```text
完整 Buff 系统
投射物技能
范围技能
复杂击飞/拉拽
死亡状态完整表现
失衡专属状态动画
技能键 1/2/3 的完整 UI 展示
```

建议落地顺序：

```text
1. 建 CombatStats / CombatResource / CombatState
2. 建 CombatHit / CombatResult / DamageResolver
3. 接玩家和敌人 Combatant
4. 用 SkillRunner 替换旧命中逻辑
5. 接防御、格挡、翻滚无敌
6. 接打断、霸体
7. 扩展 JSON 配置并给现有技能填默认值
8. 更新 HUD 显示生命、稳定、战意
9. 使用 $CLI compile unity 验证
```

## 验收场景

```text
玩家普攻敌人：敌人掉血掉稳定，玩家涨战意
玩家技能命中敌人：敌人掉血掉稳定，玩家不涨战意
玩家防御敌人攻击：玩家不掉血，但掉稳定
玩家格挡敌人攻击：玩家不掉血，不涨战意，敌人掉稳定
玩家翻滚中被打：不受伤
普攻最后一段命中正在攻击的敌人：敌人被打断进 GetHit
普攻最后一段命中防御敌人：不打断防御
敌人霸体技能期间被打：掉血、掉稳定，但不进 GetHit
```

## 验证要求

实现阶段必须使用：

```text
$CLI compile unity
```

`compile dotnet` 只能作为额外检查，不能替代 Unity 编译。
