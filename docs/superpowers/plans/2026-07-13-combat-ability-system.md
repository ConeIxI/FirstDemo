# 轻量战斗能力系统 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 用一个玩家与敌人共用的 `CombatAbilitySystem` 替换旧战斗核心，同时保留玩家 FSM、敌人行为树、JSON 技能配置、装备系统和现有表现链。

**Architecture:** `CombatAbilitySystem` 负责技能激活、标签、命中窗口、稳定恢复和命中结算；玩家使用 `CombatAttributeSet`，敌人使用实现同一接口的 `EnemyAttributeComponent`。武器检测器统一上报目标，结算结果通过 `CombatEvent` 连接 FSM、行为树和表现系统，HUD 通过属性变化事件更新。

**Tech Stack:** Unity 2022.3.61f1c1、C# 9.0、NUnit/Unity Test Framework、AIBridge CLI、Newtonsoft.Json。

## Global Constraints

- Unity 编译只能使用 `$CLI compile unity`；`compile dotnet` 只能作为额外检查。
- 所有新增或修改的函数必须添加简体中文注释，说明用途或关键行为。
- 兼容 C# 9.0，禁止使用更高版本语法。
- 不引入新的第三方依赖。
- 不自动创建缺失的战斗组件；配置错误必须明确报错并禁用对应组件。
- 保留用户对敌人定义和 `Assets/Scenes/Scene1.unity` 的现有改动，只修改本计划要求的序列化字段与组件。
- 每个任务先写失败测试，再写最小实现，再运行测试和 Unity 编译。
- 设计依据：`docs/superpowers/specs/2026-07-13-combat-ability-system-design.md`。

---

## File Map

### 新增运行时代码

- `Assets/Game/Battle/Ability/CombatDefinitions.cs`：阵营、标签、激活结果和属性类型枚举。
- `Assets/Game/Battle/Ability/CombatAttributeContracts.cs`：属性变化数据、`ICombatAttributes`、`ICombatResource`、`ICombatMotion`。
- `Assets/Game/Battle/Ability/CombatAttributeSet.cs`：玩家生命、稳定值和战意。
- `Assets/Game/Battle/Ability/CombatEvent.cs`：一次命中完成后的只读事件数据。
- `Assets/Game/Battle/Ability/CombatAbilitySystem.cs`：技能激活、标签、稳定恢复、命中窗口和结算主流程。

### 新增测试

- `Assets/Game/Editor/CombatAttributesEditModeTests.cs`
- `Assets/Game/Editor/CombatAbilityActivationEditModeTests.cs`
- `Assets/Game/Editor/CombatHitResolutionEditModeTests.cs`
- `Assets/Game/Editor/WeaponHitDetectorEditModeTests.cs`
- `Assets/Game/Editor/PlayerCombatAbilityIntegrationEditModeTests.cs`
- `Assets/Game/Editor/EnemyCombatAbilityIntegrationEditModeTests.cs`
- `Assets/Game/Editor/BattleHudAttributeBindingEditModeTests.cs`

### 主要修改文件

- `Assets/Game/Battle/Skill/Common/SkillConfig.cs`
- `Assets/Framework/Manager/ConfigManager.cs`
- `Assets/Game/Character/Enemy/Components/EnemyAttributeComponent.cs`
- `Assets/Game/Character/Equipment/WeaponHandler.cs`
- `Assets/Game/Battle/Weapon/WeaponHitDetector.cs`
- `Assets/Game/Character/Player/PlayerSkillManager.cs`
- `Assets/Game/Character/Player/PlayerController.cs`
- `Assets/Game/Character/Player/PlayerStateMachine.cs`
- `Assets/Game/Character/Player/PlayerFsm/PlayerStateBase.cs`
- `Assets/Game/Character/Player/PlayerFsm/PlayerCombatActionState.cs`
- `Assets/Game/Character/Player/PlayerFsm/DefenceState.cs`
- `Assets/Game/Character/Player/PlayerFsm/RollState.cs`
- `Assets/Game/Character/Player/PlayerFsm/UnbalanceState.cs`
- `Assets/Game/Character/Player/PlayerFsm/DeadState.cs`
- `Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs`
- `Assets/Game/Character/Enemy/Components/EnemyLifeComponent.cs`
- `Assets/Game/Character/Enemy/Components/EnemyAnimationComponent.cs`
- `Assets/Game/UI/BattleHudPanel.cs`
- `Assets/Game/Battle/Skill/Effects/CombatEffectExecutor.cs`
- `Assets/Game/Battle/Combat/Feedback/CombatHitStopController.cs`

### Unity 资源迁移

- `Assets/Scenes/Scene1.unity`
- `Assets/Game/Character/Enemy/Prefabs/GuardMeleeEnemy.prefab`
- `Assets/Res/Prefabs/Weapon/SingleSword.prefab`
- `Assets/Res/Prefabs/Weapon/GreatSword.prefab`

---

### Task 1: 建立属性契约并迁移玩家、敌人属性

**Files:**
- Create: `Assets/Game/Battle/Ability/CombatDefinitions.cs`
- Create: `Assets/Game/Battle/Ability/CombatAttributeContracts.cs`
- Create: `Assets/Game/Battle/Ability/CombatAttributeSet.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyAttributeComponent.cs`
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyIsHealthBelowNodeAsset.cs`
- Create: `Assets/Game/Editor/CombatAttributesEditModeTests.cs`
- Modify: `Assets/Game/Editor/EnemyComponentEditModeTests.cs`

**Interfaces:**
- Produces: `ICombatAttributes`、`ICombatResource`、`ICombatMotion`、`CombatAttributeChanged`。
- Produces: 玩家 `CombatAttributeSet` 和敌人 `EnemyAttributeComponent` 的统一生命/稳定值 API。

- [ ] **Step 1: 写属性失败测试**

在 `CombatAttributesEditModeTests.cs` 中创建以下测试：

```csharp
using Game.Battle.Ability;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class CombatAttributesEditModeTests
    {
        /// <summary>验证玩家属性扣血会限制到零并发布实际变化量。</summary>
        [Test]
        public void ApplyHealthDamage_ClampsToZeroAndPublishesChange()
        {
            GameObject owner = new GameObject("PlayerAttributes");
            try
            {
                CombatAttributeSet attributes = owner.AddComponent<CombatAttributeSet>();
                attributes.Initialize(100, 80, 60);
                CombatAttributeChanged received = default;
                attributes.AttributeChanged += value => received = value;

                int applied = attributes.ApplyHealthDamage(120);

                Assert.AreEqual(100, applied);
                Assert.AreEqual(0, attributes.Health);
                Assert.AreEqual(CombatAttributeType.Health, received.Type);
                Assert.AreEqual(-100, received.Delta);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }

        /// <summary>验证战意不足时不会发生部分扣除。</summary>
        [Test]
        public void TryConsumeBattleSpirit_WhenInsufficient_DoesNotChangeValue()
        {
            GameObject owner = new GameObject("PlayerAttributes");
            try
            {
                CombatAttributeSet attributes = owner.AddComponent<CombatAttributeSet>();
                attributes.Initialize(100, 100, 10);

                bool consumed = attributes.TryConsumeBattleSpirit(11);

                Assert.IsFalse(consumed);
                Assert.AreEqual(10, attributes.BattleSpirit);
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
```

扩展 `EnemyComponentEditModeTests.LoadFromDefinition_UsesSerializedAttributeConfig`，增加：

```csharp
Assert.AreEqual(120, attribute.MaxHealth);
Assert.AreEqual(90, attribute.MaxStability);
Assert.AreEqual(20, attribute.ApplyHealthDamage(20));
Assert.AreEqual(100, attribute.Health);
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
$CLI test run --mode EditMode --group-name "Game.Tests.EditMode.CombatAttributesEditModeTests|Game.Tests.EditMode.EnemyComponentEditModeTests"
```

Expected: FAIL，提示 `Game.Battle.Ability`、`CombatAttributeSet` 或敌人运行时伤害 API 尚不存在。

- [ ] **Step 3: 创建共享定义与属性契约**

`CombatDefinitions.cs` 必须包含：

```csharp
namespace Game.Battle.Ability
{
    public enum CombatFaction { Player, Enemy }
    public enum CombatTag { Dead, Unbalanced, Defending, ParryWindow, Invincible }
    public enum AbilityActivationResult { Success, Dead, Unbalanced, AlreadyActive, BlockedByTag, InsufficientResource }
    public enum CombatAttributeType { Health, Stability, BattleSpirit }
}
```

`CombatAttributeContracts.cs` 必须提供以下签名：

```csharp
using System;
using UnityEngine;

namespace Game.Battle.Ability
{
    public readonly struct CombatAttributeChanged
    {
        public CombatAttributeChanged(CombatAttributeType type, int current, int max, int delta)
        {
            Type = type;
            Current = current;
            Max = max;
            Delta = delta;
        }

        public CombatAttributeType Type { get; }
        public int Current { get; }
        public int Max { get; }
        public int Delta { get; }
    }

    public interface ICombatAttributes
    {
        int Health { get; }
        int MaxHealth { get; }
        int Stability { get; }
        int MaxStability { get; }
        bool IsDead { get; }
        bool IsUnbalanced { get; }
        event Action<CombatAttributeChanged> AttributeChanged;
        int ApplyHealthDamage(int value);
        int RestoreHealth(int value);
        int ApplyStabilityDamage(int value);
        int RestoreStability(int value);
    }

    public interface ICombatResource
    {
        int BattleSpirit { get; }
        int MaxBattleSpirit { get; }
        bool TryConsumeBattleSpirit(int value);
        int AddBattleSpirit(int value);
    }

    public interface ICombatMotion
    {
        void ApplyExternalDisplacement(Vector3 offset);
    }
}
```

- [ ] **Step 4: 实现玩家 CombatAttributeSet**

实现要求：

```csharp
public sealed class CombatAttributeSet : MonoBehaviour, ICombatAttributes, ICombatResource
{
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private int maxStability = 100;
    [SerializeField] private int maxBattleSpirit = 100;

    public int Health { get; private set; }
    public int MaxHealth => maxHealth;
    public int Stability { get; private set; }
    public int MaxStability => maxStability;
    public int BattleSpirit { get; private set; }
    public int MaxBattleSpirit => maxBattleSpirit;
    public bool IsDead => Health <= 0;
    public bool IsUnbalanced => Stability <= 0 && !IsDead;
    public event Action<CombatAttributeChanged> AttributeChanged;
}
```

`Initialize`、四个生命/稳定操作和两个战意操作必须使用 `Mathf.Clamp`，返回实际变化量，并且只在值改变时发布事件。

- [ ] **Step 5: 扩展 EnemyAttributeComponent**

保留 `Attack`、`Defense`、`Perception`、`Movement`，并实现 `ICombatAttributes`。`LoadFromDefinition` 必须同时设置最大值和当前值：

```csharp
MaxHealth = config.maxHealth;
Health = MaxHealth;
MaxStability = config.maxStability;
Stability = MaxStability;
```

`EnemyIsHealthBelowNodeAsset` 继续读取 `attribute.Health`，不再引入第二个属性源。

- [ ] **Step 6: 运行属性测试和 Unity 编译**

Run:

```powershell
$CLI test run --mode EditMode --group-name "Game.Tests.EditMode.CombatAttributesEditModeTests|Game.Tests.EditMode.EnemyComponentEditModeTests"
$CLI compile unity
$CLI get_logs --logType Error
```

Expected: 测试 PASS；Unity 编译成功；Error 日志为空。

- [ ] **Step 7: 提交属性层**

```bash
git add Assets/Game/Battle/Ability Assets/Game/Character/Enemy/Components/EnemyAttributeComponent.cs Assets/Game/Character/Enemy/AI/BehaviorTree/EnemyIsHealthBelowNodeAsset.cs Assets/Game/Editor/CombatAttributesEditModeTests.cs Assets/Game/Editor/EnemyComponentEditModeTests.cs
git commit -m "feat: add shared combat attribute contracts"
```

---

### Task 2: 实现标签、技能激活与稳定值恢复

**Files:**
- Create: `Assets/Game/Battle/Ability/CombatAbilitySystem.cs`
- Modify: `Assets/Game/Battle/Skill/Common/SkillConfig.cs`
- Modify: `Assets/Framework/Manager/ConfigManager.cs`
- Create: `Assets/Game/Editor/CombatAbilityActivationEditModeTests.cs`

**Interfaces:**
- Consumes: `ICombatAttributes`、`ICombatResource`、`CombatTag`。
- Produces: `CanActivate(SkillConfig)`、`TryActivate(SkillConfig)`、`CancelActiveAbility()`、标签 API、命中窗口 API。

- [ ] **Step 1: 写激活、标签和恢复失败测试**

测试至少包含：

```csharp
[Test]
public void TryActivate_WhenDead_ReturnsDead()
{
    TestContext context = CreateContext(health: 0, stability: 100, battleSpirit: 100);
    Assert.AreEqual(AbilityActivationResult.Dead, context.System.TryActivate(CreateSkill()));
}

[Test]
public void TryActivate_WhenResourceInsufficient_DoesNotActivate()
{
    TestContext context = CreateContext(health: 100, stability: 100, battleSpirit: 5);
    SkillConfig config = CreateSkill();
    config.battleSpiritCost = 10;
    Assert.AreEqual(AbilityActivationResult.InsufficientResource, context.System.TryActivate(config));
    Assert.IsFalse(context.System.IsAbilityActive);
}

[Test]
public void Tick_WhenRecoveryDelayEnds_RestoresStability()
{
    TestContext context = CreateContext(health: 100, stability: 50, battleSpirit: 0);
    context.System.ResetStabilityRecovery();
    context.System.Tick(6f);
    Assert.Greater(context.Attributes.Stability, 50);
}
```

同一测试类必须定义并复用以下辅助代码，避免引用未声明的测试类型：

```csharp
private readonly List<GameObject> m_createdObjects = new List<GameObject>();

[TearDown]
public void TearDown()
{
    for (int i = 0; i < m_createdObjects.Count; i++)
        Object.DestroyImmediate(m_createdObjects[i]);
    m_createdObjects.Clear();
}

private TestContext CreateContext(int health, int stability, int battleSpirit)
{
    GameObject owner = new GameObject("AbilitySystemTest");
    m_createdObjects.Add(owner);
    CombatAttributeSet attributes = owner.AddComponent<CombatAttributeSet>();
    attributes.Initialize(100, 100, 100);
    attributes.ApplyHealthDamage(100 - health);
    attributes.ApplyStabilityDamage(100 - stability);
    attributes.TryConsumeBattleSpirit(100 - battleSpirit);
    CombatAbilitySystem system = owner.AddComponent<CombatAbilitySystem>();
    system.SetDependenciesForTests(CombatFaction.Player, attributes, null);
    system.SetStabilityRecoveryForTests(5f, 20f);
    return new TestContext(system, attributes);
}

private static SkillConfig CreateSkill()
{
    return new SkillConfig
    {
        skillId = 1,
        hitConfig = new CombatHitConfig(),
        interruptConfig = new InterruptConfig(),
        requiredTags = new CombatTag[0],
        blockedTags = new CombatTag[0],
        activeTags = new CombatTag[0]
    };
}

private readonly struct TestContext
{
    public TestContext(CombatAbilitySystem system, CombatAttributeSet attributes)
    {
        System = system;
        Attributes = attributes;
    }

    public CombatAbilitySystem System { get; }
    public CombatAttributeSet Attributes { get; }
}
```

- [ ] **Step 2: 运行测试确认失败**

```powershell
$CLI test run --mode EditMode --group-name "Game.Tests.EditMode.CombatAbilityActivationEditModeTests"
```

Expected: FAIL，提示 `CombatAbilitySystem` 尚不存在。

- [ ] **Step 3: 扩展 SkillConfig 和默认值归一化**

在 `SkillConfig` 中增加：

```csharp
public CombatTag[] requiredTags;
public CombatTag[] blockedTags;
public CombatTag[] activeTags;
```

在 `SkillConfigDefaults` 中增加一个统一方法，将三个数组的空值转换为空数组。玩家和敌人默认值入口都必须调用该方法。

- [ ] **Step 4: 增加 ConfigManager 配置校验**

在配置加入字典前调用 `ValidateSkillConfig`。校验逻辑必须明确抛出异常：

```csharp
private static void ValidateSkillConfig(SkillConfig config)
{
    if (config.skillId <= 0) throw new Exception("技能Id必须大于零");
    if (config.battleSpiritCost < 0) throw new Exception($"技能{config.skillId}战意消耗不能为负数");
    if (config.hitConfig.healthDamage < 0 || config.hitConfig.stabilityDamage < 0)
        throw new Exception($"技能{config.skillId}伤害不能为负数");
    if (config.activeTags.Contains(CombatTag.Dead))
        throw new Exception($"技能{config.skillId}不能激活死亡标签");
}
```

同时检查 `requiredTags` 与 `blockedTags` 的交集，以及全部配置加载完成后的 `comboNextSkillId` 是否存在。

- [ ] **Step 5: 实现 CombatAbilitySystem 激活与标签核心**

关键公开 API 固定为：

```csharp
public CombatFaction Faction { get; }
public ICombatAttributes Attributes { get; }
public SkillConfig CurrentSkill { get; }
public bool IsAbilityActive { get; }
public bool IsHitWindowOpen { get; }
public bool HasTag(CombatTag tag);
public void AddTag(CombatTag tag);
public void RemoveTag(CombatTag tag);
public void AddTimedTag(CombatTag tag, float duration);
public AbilityActivationResult CanActivate(SkillConfig config);
public AbilityActivationResult TryActivate(SkillConfig config);
public void CancelActiveAbility();
public void BeginHitWindow();
public void EndHitWindow();
public void Tick(float deltaTime);
public void ResetStabilityRecovery();
```

实现细节：

- 使用序列化 `MonoBehaviour attributesProvider`，在 `Awake` 转换为 `ICombatAttributes`。
- `TryActivate` 先调用 `CanActivate`，成功后再消耗玩家战意和添加 `activeTags`。
- 限时标签存储剩余秒数，`Tick(Time.deltaTime)` 递减并移除到期标签。
- 稳定值恢复参数保存在能力系统中；再次受到稳定伤害时调用 `ResetStabilityRecovery`。
- `OnDisable` 调用 `CancelActiveAbility` 并清空限时标签。
- `#if UNITY_EDITOR` 下提供测试依赖注入方法，测试不得依赖反射修改私有接口字段。

- [ ] **Step 6: 运行激活测试和 Unity 编译**

```powershell
$CLI test run --mode EditMode --group-name "Game.Tests.EditMode.CombatAbilityActivationEditModeTests"
$CLI compile unity
$CLI get_logs --logType Error
```

Expected: 全部 PASS，编译成功，Error 日志为空。

- [ ] **Step 7: 提交能力激活核心**

```bash
git add Assets/Game/Battle/Ability/CombatAbilitySystem.cs Assets/Game/Battle/Skill/Common/SkillConfig.cs Assets/Framework/Manager/ConfigManager.cs Assets/Game/Editor/CombatAbilityActivationEditModeTests.cs
git commit -m "feat: add combat ability activation and tags"
```

---

### Task 3: 实现命中结算与 CombatEvent

**Files:**
- Create: `Assets/Game/Battle/Ability/CombatEvent.cs`
- Modify: `Assets/Game/Battle/Ability/CombatAbilitySystem.cs`
- Create: `Assets/Game/Editor/CombatHitResolutionEditModeTests.cs`

**Interfaces:**
- Produces: `ReportHit(CombatAbilitySystem target, Vector3 hitPoint)`。
- Produces: `CombatEvent.EventId` 和只读结算属性。

- [ ] **Step 1: 写结算顺序失败测试**

为以下场景分别写测试：无敌、弹反、格挡、普通命中、打断、死亡、来源失衡、同阵营、重复目标。

普通命中测试的核心断言：

```csharp
[Test]
public void ReportHit_NormalHit_AppliesDamageAndPublishesEvent()
{
    TestPair pair = CreatePair();
    SkillConfig skill = CreateSkill(healthDamage: 20, stabilityDamage: 15);
    pair.Source.TryActivate(skill);
    pair.Source.BeginHitWindow();
    CombatEvent received = null;
    Action<object, EventArgsBase> handler = (_, args) => received = (CombatEvent)args;
    EventCenter.Instance.Subscribe(CombatEvent.EventId, handler);
    try
    {
        pair.Source.ReportHit(pair.Target, Vector3.one);

        Assert.AreEqual(80, pair.TargetAttributes.Health);
        Assert.AreEqual(85, pair.TargetAttributes.Stability);
        Assert.AreEqual(CombatEventType.Hit, received.Type);
        Assert.AreEqual(20, received.TargetHealthDamage);
    }
    finally
    {
        EventCenter.TryUnSubscribe(CombatEvent.EventId, handler);
    }
}
```

测试类内定义以下辅助类型和构造方法：

```csharp
private readonly List<GameObject> m_createdObjects = new List<GameObject>();

private TestPair CreatePair()
{
    CombatAbilitySystem source = CreateSystem("Source", CombatFaction.Player, out CombatAttributeSet sourceAttributes);
    CombatAbilitySystem target = CreateSystem("Target", CombatFaction.Enemy, out CombatAttributeSet targetAttributes);
    return new TestPair(source, target, sourceAttributes, targetAttributes);
}

private CombatAbilitySystem CreateSystem(string name, CombatFaction faction, out CombatAttributeSet attributes)
{
    GameObject owner = new GameObject(name);
    m_createdObjects.Add(owner);
    attributes = owner.AddComponent<CombatAttributeSet>();
    attributes.Initialize(100, 100, 100);
    CombatAbilitySystem system = owner.AddComponent<CombatAbilitySystem>();
    system.SetDependenciesForTests(faction, attributes, null);
    return system;
}

private static SkillConfig CreateSkill(int healthDamage, int stabilityDamage)
{
    return new SkillConfig
    {
        skillId = 1,
        skillType = SkillType.NormalAttack,
        battleSpiritGainOnHit = 8,
        hitConfig = new CombatHitConfig
        {
            healthDamage = healthDamage,
            stabilityDamage = stabilityDamage,
            canBeBlocked = true,
            canBeParried = true
        },
        interruptConfig = new InterruptConfig(),
        requiredTags = new CombatTag[0],
        blockedTags = new CombatTag[0],
        activeTags = new CombatTag[0]
    };
}

private readonly struct TestPair
{
    public TestPair(CombatAbilitySystem source, CombatAbilitySystem target,
        CombatAttributeSet sourceAttributes, CombatAttributeSet targetAttributes)
    {
        Source = source;
        Target = target;
        SourceAttributes = sourceAttributes;
        TargetAttributes = targetAttributes;
    }

    public CombatAbilitySystem Source { get; }
    public CombatAbilitySystem Target { get; }
    public CombatAttributeSet SourceAttributes { get; }
    public CombatAttributeSet TargetAttributes { get; }
}
```

使用 `[TearDown]` 销毁 `m_createdObjects` 中的全部对象。

- [ ] **Step 2: 运行测试确认失败**

```powershell
$CLI test run --mode EditMode --group-name "Game.Tests.EditMode.CombatHitResolutionEditModeTests"
```

Expected: FAIL，提示 `CombatEvent` 或 `ReportHit` 尚不存在。

- [ ] **Step 3: 创建只读 CombatEvent**

`CombatEvent` 继承 `EventArgsBase`，包含：

```csharp
public enum CombatEventType { Invincible, Parried, Blocked, Hit }

public sealed class CombatEvent : EventArgsBase
{
    public static readonly int EventId = typeof(CombatEvent).GetHashCode();
    public override int Id => EventId;
    public CombatEventType Type { get; }
    public CombatAbilitySystem Source { get; }
    public CombatAbilitySystem Target { get; }
    public SkillConfig Skill { get; }
    public int TargetHealthDamage { get; }
    public int TargetStabilityDamage { get; }
    public int SourceStabilityDamage { get; }
    public int SourceBattleSpiritGain { get; }
    public bool TargetInterrupted { get; }
    public bool TargetShouldReact { get; }
    public bool TargetUnbalanced { get; }
    public bool SourceUnbalanced { get; }
    public bool TargetDead { get; }
    public Vector3 HitPoint { get; }
    public Vector3 HitDirection { get; }
}
```

所有值只允许通过构造函数设置。

- [ ] **Step 4: 在 CombatAbilitySystem 中实现固定结算顺序**

`ReportHit` 必须按以下结构实现，禁止由外部消费者补算伤害：

```csharp
public void ReportHit(CombatAbilitySystem target, Vector3 hitPoint)
{
    if (!CanResolveTarget(target)) return;
    m_resolvedTargets.Add(target);

    CombatEvent result;
    if (target.HasTag(CombatTag.Invincible)) result = ResolveInvincible(target, hitPoint);
    else if (CanParry(target)) result = ResolveParry(target, hitPoint);
    else if (CanBlock(target)) result = ResolveBlock(target, hitPoint);
    else result = ResolveNormalHit(target, hitPoint);

    EventCenter.Instance.Fire(this, result);
}
```

状态优先级固定为 `Dead > Unbalanced > Interrupted > 普通受击`。死亡和失衡必须取消当前能力并关闭命中窗口。普通受击事件必须明确计算 `TargetShouldReact`，避免霸体技能错误进入受击状态。

- [ ] **Step 5: 运行全部新核心测试**

```powershell
$CLI test run --mode EditMode --group-name "Game.Tests.EditMode.CombatAttributesEditModeTests|Game.Tests.EditMode.CombatAbilityActivationEditModeTests|Game.Tests.EditMode.CombatHitResolutionEditModeTests"
$CLI compile unity
$CLI get_logs --logType Error
```

Expected: 全部 PASS，编译成功，Error 日志为空。

- [ ] **Step 6: 提交结算核心**

```bash
git add Assets/Game/Battle/Ability/CombatEvent.cs Assets/Game/Battle/Ability/CombatAbilitySystem.cs Assets/Game/Editor/CombatHitResolutionEditModeTests.cs
git commit -m "feat: add combat hit resolution events"
```

---

### Task 4: 统一 WeaponHandler 与 WeaponHitDetector

**Files:**
- Modify: `Assets/Game/Character/Equipment/WeaponHandler.cs`
- Modify: `Assets/Game/Battle/Weapon/WeaponHitDetector.cs`
- Modify: `Assets/Game/Battle/Weapon/PlayerWeaponHitDetector.cs`
- Modify: `Assets/Game/Battle/Weapon/EnemyWeaponHitDetector.cs`
- Create: `Assets/Game/Editor/WeaponHitDetectorEditModeTests.cs`

**Interfaces:**
- Consumes: `CombatAbilitySystem.BeginHitWindow`、`EndHitWindow`、`ReportHit`。
- Produces: `WeaponHandler.OpenHitWindow()`、`CloseHitWindow()`。

- [ ] **Step 1: 写武器窗口与多 Collider 去重失败测试**

测试必须验证：

- `OpenHitWindow` 清空记录并启用 Collider。
- `CloseHitWindow` 禁用 Collider。
- 检测器把子 Collider 命中解析为父级 `CombatAbilitySystem`。
- 同一目标多个 Collider 在一个窗口只结算一次。

至少包含以下可直接调用受保护上报方法的测试替身：

```csharp
private sealed class TestWeaponHitDetector : WeaponHitDetector
{
    /// <summary>向测试公开碰撞上报入口。</summary>
    public void ReportForTest(Collider other)
    {
        ReportCollision(other);
    }
}

[Test]
public void ReportCollision_WhenTargetHasMultipleColliders_ResolvesOnce()
{
    TestCombatPair pair = CreateCombatPairWithTwoTargetColliders();
    pair.Source.TryActivate(CreateSkill());
    pair.Source.BeginHitWindow();
    pair.Detector.BindSource(pair.Source);

    pair.Detector.ReportForTest(pair.FirstCollider);
    pair.Detector.ReportForTest(pair.SecondCollider);

    Assert.AreEqual(90, pair.TargetAttributes.Health);
}

private TestCombatPair CreateCombatPairWithTwoTargetColliders()
{
    GameObject sourceObject = new GameObject("Source");
    GameObject targetObject = new GameObject("Target");
    GameObject firstColliderObject = new GameObject("TargetColliderA");
    GameObject secondColliderObject = new GameObject("TargetColliderB");
    firstColliderObject.transform.SetParent(targetObject.transform);
    secondColliderObject.transform.SetParent(targetObject.transform);

    CombatAttributeSet sourceAttributes = sourceObject.AddComponent<CombatAttributeSet>();
    CombatAttributeSet targetAttributes = targetObject.AddComponent<CombatAttributeSet>();
    sourceAttributes.Initialize(100, 100, 100);
    targetAttributes.Initialize(100, 100, 100);
    CombatAbilitySystem source = sourceObject.AddComponent<CombatAbilitySystem>();
    CombatAbilitySystem target = targetObject.AddComponent<CombatAbilitySystem>();
    source.SetDependenciesForTests(CombatFaction.Player, sourceAttributes, null);
    target.SetDependenciesForTests(CombatFaction.Enemy, targetAttributes, null);

    TestWeaponHitDetector detector = sourceObject.AddComponent<TestWeaponHitDetector>();
    Collider first = firstColliderObject.AddComponent<BoxCollider>();
    Collider second = secondColliderObject.AddComponent<BoxCollider>();
    m_createdObjects.Add(sourceObject);
    m_createdObjects.Add(targetObject);
    return new TestCombatPair(source, targetAttributes, detector, first, second);
}

private readonly struct TestCombatPair
{
    public TestCombatPair(CombatAbilitySystem source, CombatAttributeSet targetAttributes,
        TestWeaponHitDetector detector, Collider firstCollider, Collider secondCollider)
    {
        Source = source;
        TargetAttributes = targetAttributes;
        Detector = detector;
        FirstCollider = firstCollider;
        SecondCollider = secondCollider;
    }

    public CombatAbilitySystem Source { get; }
    public CombatAttributeSet TargetAttributes { get; }
    public TestWeaponHitDetector Detector { get; }
    public Collider FirstCollider { get; }
    public Collider SecondCollider { get; }
}
```

测试类定义 `m_createdObjects` 和 `[TearDown]`，销毁创建的来源与目标对象。`CreateSkill()` 使用 Task 3 已定义的完整 `SkillConfig` 字段集合。

- [ ] **Step 2: 运行测试确认失败**

```powershell
$CLI test run --mode EditMode --group-name "Game.Tests.EditMode.WeaponHitDetectorEditModeTests"
```

Expected: FAIL，提示通用命中上报和窗口 API 尚不存在。

- [ ] **Step 3: 扩展 WeaponHandler**

实现固定入口：

```csharp
public void OpenHitWindow()
{
    m_abilitySystem.BeginHitWindow();
    m_weaponHitDetector.ClearHitList();
    m_weaponHitDetector.EnableCollider(true);
}

public void CloseHitWindow()
{
    if (m_weaponHitDetector != null) m_weaponHitDetector.EnableCollider(false);
    if (m_abilitySystem != null) m_abilitySystem.EndHitWindow();
}
```

`SetActiveHitDetector` 必须调用 `hitDetector.BindSource(m_abilitySystem)`。

- [ ] **Step 4: 将 WeaponHitDetector 改为通用实现**

新增：

```csharp
public void BindSource(CombatAbilitySystem source);
protected void ReportCollision(Collider other);
```

`ReportCollision` 查找 `other.GetComponentInParent<CombatAbilitySystem>()`，计算 `ClosestPoint`，然后调用来源系统 `ReportHit`。当前阶段让玩家和敌人子类只保留三行 `OnTriggerStay` 转发，直到 Unity 资源完成脚本替换。

- [ ] **Step 5: 运行武器测试与编译**

```powershell
$CLI test run --mode EditMode --group-name "Game.Tests.EditMode.WeaponHitDetectorEditModeTests|Game.Tests.EditMode.CombatHitResolutionEditModeTests"
$CLI compile unity
$CLI get_logs --logType Error
```

Expected: PASS，编译成功，Error 日志为空。

- [ ] **Step 6: 提交武器管线**

```bash
git add Assets/Game/Character/Equipment/WeaponHandler.cs Assets/Game/Battle/Weapon Assets/Game/Editor/WeaponHitDetectorEditModeTests.cs
git commit -m "refactor: unify weapon hit detection"
```

---

### Task 5: 迁移玩家 FSM、技能管理与玩家位移

**Files:**
- Modify: `Assets/Game/Character/Player/PlayerSkillManager.cs`
- Modify: `Assets/Game/Character/Player/PlayerController.cs`
- Modify: `Assets/Game/Character/CharacterStateMachine.cs`
- Modify: `Assets/Game/Character/Player/PlayerStateMachine.cs`
- Modify: `Assets/Game/Character/Player/PlayerFsm/PlayerStateBase.cs`
- Modify: `Assets/Game/Character/Player/PlayerFsm/PlayerCombatActionState.cs`
- Modify: `Assets/Game/Character/Player/PlayerFsm/DefenceState.cs`
- Modify: `Assets/Game/Character/Player/PlayerFsm/RollState.cs`
- Modify: `Assets/Game/Character/Player/PlayerFsm/UnbalanceState.cs`
- Modify: `Assets/Game/Character/Player/PlayerFsm/DeadState.cs`
- Create: `Assets/Game/Editor/PlayerCombatAbilityIntegrationEditModeTests.cs`

**Interfaces:**
- Consumes: `CombatAbilitySystem`、`CombatEvent`、`WeaponHandler`。
- Preserves: 玩家 FSM 状态类型、输入缓冲、连段、动画和装备技能 ID。

- [ ] **Step 1: 写玩家接入失败测试**

覆盖：

- `PlayerSkillManager.LoadSkillsForWeapon` 只更新技能 ID，不创建旧核心。
- 防御状态添加/移除 `Defending` 和 `ParryWindow`。
- 翻滚添加限时 `Invincible`。
- `CombatEvent.TargetDead` 进入 `DeadState`。
- `CombatEvent.TargetUnbalanced` 进入 `UnbalanceState`。
- 普通受击只在 `TargetShouldReact` 为真时进入 `GetHitState`。

在现有 `PlayerDefenceStateEditModeTests` 的状态机 fixture 中增加以下断言，不新建另一套玩家测试搭建代码：

```csharp
[Test]
public void DefenceState_EnterAndExit_UpdatesDefendingTag()
{
    m_fsm.ChangeState<DefenceState>();
    Assert.IsTrue(m_playerController.AbilitySystem.HasTag(CombatTag.Defending));

    m_fsm.ChangeState<IdleState>();
    Assert.IsFalse(m_playerController.AbilitySystem.HasTag(CombatTag.Defending));
}
```

如果现有 fixture 的字段名称不同，沿用该文件已有字段；不要创建未定义的 `PlayerFixture`。事件测试通过实际 `ReportHit` 产生 `CombatEvent`，并断言 `PlayerStateMachine.CurState`，测试结束时销毁已有 fixture 创建的 GameObject。

- [ ] **Step 2: 运行测试确认失败**

```powershell
$CLI test run --mode EditMode --group-name "Game.Tests.EditMode.PlayerCombatAbilityIntegrationEditModeTests"
```

Expected: FAIL，现有玩家代码仍引用 `SkillRunner`、`Combatant` 和 `CombatState`。

- [ ] **Step 3: 简化 PlayerSkillManager**

保留：

```csharp
public void LoadSkillsForWeapon(WeaponData weaponData);
public void ClearSkills();
public bool HasSkill(int skillId);
```

删除 `SkillRunner`、`Combatant`、`SkillBase`、`Runner`、空实现的 `CastSkill/GetSkill/AddSkill`。`ClearSkills` 调用玩家 `CombatAbilitySystem.CancelActiveAbility()`。

- [ ] **Step 4: 给 PlayerController 暴露能力系统并实现 ICombatMotion**

新增序列化引用和只读属性：

```csharp
[SerializeField] private CombatAbilitySystem abilitySystem;
public CombatAbilitySystem AbilitySystem => abilitySystem;

/// <summary>应用战斗产生的外部位移。</summary>
public void ApplyExternalDisplacement(Vector3 offset)
{
    Move(offset);
}
```

- [ ] **Step 5: 迁移攻击、防御、翻滚、失衡和死亡状态**

- `PlayerCombatActionState.Enter` 调用 `TryActivate`，只有 `Success` 才播放动画。
- `Exit` 调用 `CancelActiveAbility` 和 `WeaponHandler.CloseHitWindow`。
- 删除 `BeginCombatAction(Combatant, bool)`。
- `DefenceState` 使用标签 API。
- `RollState` 使用 `AddTimedTag(Invincible, RollInvincibleTime)`。
- `UnbalanceState` 通过能力系统恢复稳定值并移除标签。
- `DeadState` 取消能力并清理临时战斗标签。
- `CharacterStateMachine.EnableWeaponCollider/DisableWeaponCollider` 改为调用 `WeaponHandler.OpenHitWindow/CloseHitWindow`。

- [ ] **Step 6: 在 PlayerStateMachine 消费 CombatEvent**

在 `OnEnable`/`OnDisable` 订阅和取消订阅。处理顺序：

```csharp
if (combatEvent.TargetDead) ChangeState<DeadState>();
else if (combatEvent.TargetUnbalanced) ChangeState<UnbalanceState>();
else if (combatEvent.Type == CombatEventType.Blocked) ChangeState<PlayerBlockHitState>();
else if (combatEvent.TargetShouldReact) ChangeState<GetHitState>();
```

若玩家是事件来源且 `SourceUnbalanced` 为真，进入 `UnbalanceState`。格挡动画根据 `TargetStabilityDamage` 映射现有轻、中、重动画名称。

- [ ] **Step 7: 运行玩家测试、旧玩家状态测试和编译**

```powershell
$CLI test run --mode EditMode --group-name "Game.Tests.EditMode.PlayerCombatAbilityIntegrationEditModeTests|Game.Tests.EditMode.PlayerDefenceStateEditModeTests|Game.Tests.EditMode.PlayerRollStateEditModeTests|Game.Tests.EditMode.PlayerBlockHitStateEditModeTests"
$CLI compile unity
$CLI get_logs --logType Error
```

Expected: PASS，玩家源代码不再引用 `SkillRunner`、`Combatant`、`CombatStats`、`CombatResource` 或 `CombatState`。

- [ ] **Step 8: 提交玩家迁移**

```bash
git add Assets/Game/Character/Player/PlayerSkillManager.cs Assets/Game/Character/Player/PlayerController.cs Assets/Game/Character/Player/PlayerStateMachine.cs Assets/Game/Character/Player/PlayerFsm Assets/Game/Character/CharacterStateMachine.cs Assets/Game/Editor/PlayerCombatAbilityIntegrationEditModeTests.cs
git commit -m "refactor: migrate player combat to ability system"
```

---

### Task 6: 迁移敌人战斗、属性和行为树反应

**Files:**
- Modify: `Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyLifeComponent.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyAnimationComponent.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyMovementComponent.cs`
- Modify: `Assets/Game/Character/Enemy/AI/AIController.cs`
- Create: `Assets/Game/Editor/EnemyCombatAbilityIntegrationEditModeTests.cs`
- Modify: `Assets/Game/Editor/EnemyCombatReactionEditModeTests.cs`

**Interfaces:**
- Consumes: `CombatAbilitySystem`、`CombatEvent`、`ICombatMotion`。
- Preserves: `EnemyCombatComponent` 现有公开 API 和行为树资源。

- [ ] **Step 1: 写敌人接入失败测试**

覆盖：

- `TryStartAttack` 通过 `CombatAbilitySystem` 激活敌人技能。
- `EndAction` 和 `InterruptAction` 取消当前能力。
- `CombatEvent.TargetShouldReact` 写入受击事实。
- 失衡和死亡写入敌人黑板。
- `EnemyMovementComponent.ApplyExternalDisplacement` 停止移动后应用位移。

将现有 `EnemyCombatReactionEditModeTests` 改写为通过真实结算事件验证黑板：

```csharp
[Test]
public void CombatEvent_WhenTargetDies_WritesEnemyBlackboard()
{
    GameObject sourceObject = new GameObject("Source");
    GameObject enemyObject = new GameObject("Enemy");
    try
    {
        CombatAttributeSet sourceAttributes = sourceObject.AddComponent<CombatAttributeSet>();
        CombatAttributeSet targetAttributes = enemyObject.AddComponent<CombatAttributeSet>();
        sourceAttributes.Initialize(100, 100, 100);
        targetAttributes.Initialize(10, 100, 0);
        CombatAbilitySystem source = sourceObject.AddComponent<CombatAbilitySystem>();
        CombatAbilitySystem target = enemyObject.AddComponent<CombatAbilitySystem>();
        source.SetDependenciesForTests(CombatFaction.Player, sourceAttributes, null);
        target.SetDependenciesForTests(CombatFaction.Enemy, targetAttributes, null);
        EnemyLifeComponent life = enemyObject.AddComponent<EnemyLifeComponent>();
        life.SetBlackboardForTests(new EnemyBlackboard());
        life.SetAbilitySystemForTests(target);

        source.TryActivate(CreateLethalSkill());
        source.BeginHitWindow();
        source.ReportHit(target, Vector3.zero);

        Assert.IsTrue(life.BlackboardForTests.IsDead);
    }
    finally
    {
        Object.DestroyImmediate(sourceObject);
        Object.DestroyImmediate(enemyObject);
    }
}
```

`CreateLethalSkill()` 在同一文件中返回生命伤害为 20、数组字段均为空数组的完整 `SkillConfig`。`EnemyLifeComponent` 在 `#if UNITY_EDITOR` 下提供 `SetAbilitySystemForTests`，设置引用后完成事件订阅。

- [ ] **Step 2: 运行测试确认失败**

```powershell
$CLI test run --mode EditMode --group-name "Game.Tests.EditMode.EnemyCombatAbilityIntegrationEditModeTests|Game.Tests.EditMode.EnemyCombatReactionEditModeTests"
```

Expected: FAIL，敌人组件仍依赖旧核心。

- [ ] **Step 3: 迁移 EnemyCombatComponent**

保留所有行为树调用的公开方法，将内部字段替换为：

```csharp
[SerializeField] private CombatAbilitySystem abilitySystem;
[SerializeField] private WeaponHandler weaponHandler;
```

`TryCast` 从 `ConfigManager` 取配置并要求 `TryActivate(config) == Success`。命中窗口调用 `WeaponHandler`，不再直接操作 Collider 或 `SkillRunner`。

- [ ] **Step 4: 迁移 EnemyLifeComponent 和动画事件**

`EnemyLifeComponent` 在 `OnEnable`/`OnDisable` 订阅 `CombatEvent`，只处理 `combatEvent.Target == abilitySystem`：

- `TargetDead` 调用 `HandleDeath`。
- `TargetUnbalanced` 调用 `HandleUnbalance`。
- `TargetShouldReact` 调用 `HandleHitReaction`。

`EnemyAnimationComponent.HandleAnimationEvent` 改为调用 `weaponHandler.OpenHitWindow/CloseHitWindow`。

- [ ] **Step 5: 让 EnemyMovementComponent 实现 ICombatMotion**

现有 `ApplyExternalDisplacement` 直接满足接口；修改类声明，并确保调用前停止 NavMesh/直接移动状态。

- [ ] **Step 6: 运行敌人行为树与组件回归测试**

```powershell
$CLI test run --mode EditMode --group-name "Game.Tests.EditMode.EnemyCombatAbilityIntegrationEditModeTests|Game.Tests.EditMode.EnemyCombatReactionEditModeTests|Game.Tests.EditMode.EnemyBehaviorTreeNodeEditModeTests|Game.Tests.EditMode.EnemyComponentEditModeTests"
$CLI compile unity
$CLI get_logs --logType Error
```

Expected: PASS，行为树资源结构无需修改，敌人运行时代码不再引用旧战斗核心。

- [ ] **Step 7: 提交敌人迁移**

```bash
git add Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs Assets/Game/Character/Enemy/Components/EnemyLifeComponent.cs Assets/Game/Character/Enemy/Components/EnemyAnimationComponent.cs Assets/Game/Character/Enemy/Components/EnemyMovementComponent.cs Assets/Game/Character/Enemy/AI/AIController.cs Assets/Game/Editor/EnemyCombatAbilityIntegrationEditModeTests.cs Assets/Game/Editor/EnemyCombatReactionEditModeTests.cs
git commit -m "refactor: migrate enemy combat to ability system"
```

---

### Task 7: 迁移 HUD、特效、命中停顿和击退

**Files:**
- Modify: `Assets/Game/UI/BattleHudPanel.cs`
- Modify: `Assets/Game/Battle/Skill/Effects/CombatEffectExecutor.cs`
- Modify: `Assets/Game/Battle/Combat/Feedback/CombatHitStopController.cs`
- Modify: `Assets/Game/Battle/Ability/CombatAbilitySystem.cs`
- Create: `Assets/Game/Editor/BattleHudAttributeBindingEditModeTests.cs`
- Modify: `Assets/Game/Editor/CombatStabilityEditModeTests.cs`

**Interfaces:**
- Consumes: `CombatAttributeChanged`、`CombatEvent`、`ICombatMotion`。
- Produces: 事件驱动 HUD 和不依赖 `CombatResult/CombatHit` 的表现入口。

- [ ] **Step 1: 写 HUD 与表现失败测试**

覆盖：

- HUD 绑定玩家 `CombatAttributeSet` 后只更新变化的资源条。
- `OnClose` 取消属性事件订阅。
- 死亡事件不播放命中停顿。
- `Blocked/Parried/Hit` 使用 `SkillConfig` 对应效果数组。
- 击退只在 Hit/Blocked 且距离大于零时调用 `ICombatMotion`。

- [ ] **Step 2: 运行测试确认失败**

```powershell
$CLI test run --mode EditMode --group-name "Game.Tests.EditMode.BattleHudAttributeBindingEditModeTests|Game.Tests.EditMode.CombatStabilityEditModeTests"
```

Expected: FAIL，HUD 和表现仍依赖旧类型。

- [ ] **Step 3: 将 BattleHudPanel 改为属性事件驱动**

替换 `Combatant` 字段为：

```csharp
private CombatAbilitySystem m_playerAbilitySystem;
private CombatAttributeSet m_playerAttributes;
```

`OnOpen` 绑定并主动刷新一次；`OnClose` 取消订阅；`Update` 只更新闪烁计时。事件处理使用：

```csharp
private void OnAttributeChanged(CombatAttributeChanged change)
{
    switch (change.Type)
    {
        case CombatAttributeType.Health: RefreshHealth(change); break;
        case CombatAttributeType.Stability: RefreshStability(change); break;
        case CombatAttributeType.BattleSpirit: RefreshBattleSpirit(change); break;
    }
}
```

- [ ] **Step 4: 改造 CombatEffectExecutor 与 CombatHitStopController**

- `CombatEffectExecutor` 接收 `CombatEvent`，通过 `combatEvent.Skill` 和 `HitPoint` 选择 OnHit/OnBlock/OnParry 效果。
- `CombatHitStopController.Play` 和 `ShouldPlayHitStop` 接收 `CombatEvent`。
- 命中停顿时长继续限制为现有最大值；`TargetDead` 时返回零。

- [ ] **Step 5: 在能力系统结算完成后执行通用表现**

发布 `CombatEvent` 后依次：

```text
CombatEffectExecutor.Execute(combatEvent)
CombatHitStopController.Play(combatEvent)
ApplyMoveBack(combatEvent)
```

`ApplyMoveBack` 只依赖目标 `ICombatMotion`，不查找玩家 FSM 或敌人移动具体类型。不得创建新的 `CombatReaction` 替代品。

- [ ] **Step 6: 运行 HUD、表现和核心回归测试**

```powershell
$CLI test run --mode EditMode --group-name "Game.Tests.EditMode.BattleHudAttributeBindingEditModeTests|Game.Tests.EditMode.CombatHitResolutionEditModeTests|Game.Tests.EditMode.CombatStabilityEditModeTests"
$CLI compile unity
$CLI get_logs --logType Error
```

Expected: PASS，表现代码不再引用 `CombatHit` 或 `CombatResult`。

- [ ] **Step 7: 提交 UI 与表现迁移**

```bash
git add Assets/Game/UI/BattleHudPanel.cs Assets/Game/Battle/Skill/Effects/CombatEffectExecutor.cs Assets/Game/Battle/Combat/Feedback/CombatHitStopController.cs Assets/Game/Battle/Ability/CombatAbilitySystem.cs Assets/Game/Editor/BattleHudAttributeBindingEditModeTests.cs Assets/Game/Editor/CombatStabilityEditModeTests.cs
git commit -m "refactor: connect combat events to hud and feedback"
```

---

### Task 8: 迁移 Unity 资源并删除旧核心

**Files:**
- Modify: `Assets/Scenes/Scene1.unity`
- Modify: `Assets/Game/Character/Enemy/Prefabs/GuardMeleeEnemy.prefab`
- Modify: `Assets/Res/Prefabs/Weapon/SingleSword.prefab`
- Modify: `Assets/Res/Prefabs/Weapon/GreatSword.prefab`
- Delete: `Assets/Game/Battle/Skill/SkillRunner.cs`
- Delete: `Assets/Game/Battle/Skill/SkillContext.cs`
- Delete: `Assets/Game/Battle/Skill/SkillBase.cs`
- Delete: `Assets/Game/Battle/Skill/SkillDefine.cs`
- Delete: `Assets/Framework/Interface/ISkill.cs`
- Delete: `Assets/Game/Battle/Combat/Core/Combatant.cs`
- Delete: `Assets/Game/Battle/Combat/Core/CombatStats.cs`
- Delete: `Assets/Game/Battle/Combat/Core/CombatResource.cs`
- Delete: `Assets/Game/Battle/Combat/Core/CombatState.cs`
- Delete: `Assets/Game/Battle/Combat/Core/CombatHit.cs`
- Delete: `Assets/Game/Battle/Combat/Core/CombatResult.cs`
- Delete: `Assets/Game/Battle/Combat/Core/DamageResolver.cs`
- Delete: `Assets/Game/Battle/Combat/Core/InterruptResolver.cs`
- Delete: `Assets/Game/Battle/Combat/CombatReaction.cs`
- Delete: `Assets/Game/Battle/Weapon/PlayerWeaponHitDetector.cs`
- Delete: `Assets/Game/Battle/Weapon/EnemyWeaponHitDetector.cs`
- Delete: `Assets/Game/EventArgs/WeaponHitEventArgs.cs`
- Delete: `Assets/Game/EventArgs/EnemyWeaponHitEventArgs.cs`
- Delete: `Assets/Tests/EditMode/Combat/CombatStatsTests.cs`

**Interfaces:**
- Finalizes: 场景、Prefab 和脚本全部切换到新系统。
- Removes: 所有旧核心和临时检测器子类。

- [ ] **Step 1: 在删除脚本前迁移序列化组件**

使用 AIBridge Inspector/Prefab Patch 完成：

- `Scene1` 玩家对象添加 `CombatAbilitySystem` 与 `CombatAttributeSet`，配置 Player 阵营、属性提供者和 `WeaponHandler`。
- `Scene1` 敌人对象添加 `CombatAbilitySystem`，配置 Enemy 阵营、`EnemyAttributeComponent` 和 `WeaponHandler`。
- `GuardMeleeEnemy.prefab` 完成同样敌人配置。
- `SingleSword.prefab`、`GreatSword.prefab` 将脚本从 `PlayerWeaponHitDetector` 替换为通用 `WeaponHitDetector`。
- `GuardMeleeEnemy.prefab` 和 `Scene1` 的敌人武器检测器替换为通用类型。
- 删除上述对象上的 `SkillRunner`、`Combatant`、`CombatStats`、`CombatResource`、`CombatState` 组件。

先对 Prefab 运行 dry-run，再应用；场景改动必须保留用户现有的敌人定义引用和场景调整。

- [ ] **Step 2: 编译确认新资源绑定有效**

```powershell
$CLI compile unity
$CLI get_logs --logType Error
```

Expected: 编译成功，没有序列化引用或空依赖错误。

- [ ] **Step 3: 删除旧脚本与对应 meta**

使用 `apply_patch` 删除清单中的脚本；每个 Unity 脚本同时删除对应 `.meta`。删除后执行精确文本检索，结果必须为空：

```powershell
$request = @{
    command = "rg"
    queries = @("SkillRunner|DamageResolver|CombatStats|CombatResource|CombatState|CombatResult|CombatReaction|Combatant|CombatHit|InterruptResolver")
    paths = @("Assets/Game", "Assets/Framework")
    allowedExitCodes = @(0, 1)
} | ConvertTo-Json -Depth 5 -Compress
$request | & $CLI exec run --stdin
```

Expected: 不存在运行时代码引用；测试中也不允许残留旧类型。

- [ ] **Step 4: 运行全部 EditMode 测试和编译**

```powershell
$CLI test run --mode EditMode --group-name "Game.Tests.EditMode"
$CLI compile unity
$CLI get_logs --logType Error
```

Expected: 全部 PASS，编译成功，Error 日志为空。

- [ ] **Step 5: 检查 Missing Script**

使用 AIBridge 检查 `Scene1`、`GuardMeleeEnemy.prefab`、`SingleSword.prefab` 和 `GreatSword.prefab` 的组件列表；任何 Missing Script 都视为失败。

- [ ] **Step 6: 提交资源迁移与旧系统清理**

```bash
git add Assets/Scenes/Scene1.unity Assets/Game/Character/Enemy/Prefabs/GuardMeleeEnemy.prefab Assets/Res/Prefabs/Weapon/SingleSword.prefab Assets/Res/Prefabs/Weapon/GreatSword.prefab Assets/Game/Battle/Skill/SkillRunner.cs Assets/Game/Battle/Skill/SkillContext.cs Assets/Game/Battle/Skill/SkillBase.cs Assets/Game/Battle/Skill/SkillDefine.cs Assets/Framework/Interface/ISkill.cs Assets/Game/Battle/Combat/Core/Combatant.cs Assets/Game/Battle/Combat/Core/CombatStats.cs Assets/Game/Battle/Combat/Core/CombatResource.cs Assets/Game/Battle/Combat/Core/CombatState.cs Assets/Game/Battle/Combat/Core/CombatHit.cs Assets/Game/Battle/Combat/Core/CombatResult.cs Assets/Game/Battle/Combat/Core/DamageResolver.cs Assets/Game/Battle/Combat/Core/InterruptResolver.cs Assets/Game/Battle/Combat/CombatReaction.cs Assets/Game/Battle/Weapon/PlayerWeaponHitDetector.cs Assets/Game/Battle/Weapon/EnemyWeaponHitDetector.cs Assets/Game/EventArgs/WeaponHitEventArgs.cs Assets/Game/EventArgs/EnemyWeaponHitEventArgs.cs Assets/Tests/EditMode/Combat/CombatStatsTests.cs
git commit -m "refactor: remove legacy combat framework"
```

---

### Task 9: Runtime 验收与最终回归

**Files:**
- Modify only if validation exposes a confirmed defect in the new framework.

**Interfaces:**
- Verifies: 设计文档第 15 节全部验收标准。

- [ ] **Step 1: 最终静态检查**

```powershell
$CLI compile unity
$CLI get_logs --logType Error
```

Expected: 编译成功，Error 日志为空。

- [ ] **Step 2: 运行全部 EditMode 测试**

```powershell
$CLI test run --mode EditMode --group-name "Game.Tests.EditMode"
```

Expected: 全部 PASS。

- [ ] **Step 3: Play Mode 验证玩家对敌人**

进入 `Scene1` Play Mode，验证：

1. 单手剑和巨剑普通攻击正常。
2. 同一命中窗口同一敌人只受伤一次。
3. 敌人生命和稳定值来自 `EnemyAttributeComponent`。
4. 敌人受击、失衡和死亡行为树分支正常。
5. 命中特效、击退和停顿正常。

- [ ] **Step 4: Play Mode 验证敌人对玩家**

验证：

1. 普通受击扣除玩家属性。
2. 防御只扣稳定值。
3. 弹反扣除攻击者稳定值。
4. 翻滚无敌不受伤。
5. 玩家失衡和死亡进入正确 FSM 状态。

- [ ] **Step 5: 验证 HUD、装备和暂停**

验证：

1. 生命、稳定值和战意变化立即更新 HUD。
2. HUD 关闭后不再接收属性事件。
3. 武器切换后技能 ID 与动画控制器正常。
4. 暂停时无敌、弹反窗口和命中停顿时间不被错误推进。

- [ ] **Step 6: 最终工作树与提交检查**

```bash
git status --short
git log --oneline -9
```

Expected: 只保留用户原有且未纳入本重构的改动；本计划产生的提交按任务边界存在。若验证未产生修复，不创建空提交。

---

## Final Review Checklist

- [ ] 所有设计需求都映射到具体任务。
- [ ] 没有 TODO、TBD 或“以后实现”占位符。
- [ ] 玩家与敌人的属性类型、方法签名一致。
- [ ] `CombatEvent` 字段在生产代码和测试中命名一致。
- [ ] 最终资源不引用已删除脚本 GUID。
- [ ] 所有函数都有简体中文注释。
- [ ] 所有 Unity 编译均使用 `$CLI compile unity`。
- [ ] 用户原有场景和敌人定义改动没有被回滚。
