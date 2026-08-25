# Combat Buff System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a reusable combat Buff system for both player and enemies, operated externally by `buffId` only.

**Architecture:** Add a focused `CombatBuffController` component that owns runtime Buff lifetime, attribute modifiers, and fixed-interval health ticks. Buff definitions live in `Assets/Data/BuffConfig.json` and are loaded by `ConfigManager`; `CombatAttributeSet` and `EnemyAttributeComponent` keep base values but return Buff-modified attack and defense values.

**Tech Stack:** Unity 2022.3.61f1c1, C# 9.0, Newtonsoft.Json, NUnit EditMode tests, AIBridge CLI for Unity compile/test.

## Global Constraints

- Use Simplified Chinese comments for new or changed functions.
- Every new function must have a Simplified Chinese comment describing purpose or key behavior.
- Keep code compatible with C# 9.0.
- Validate Unity compile with `.\.aibridge\cli\AIBridgeCLI.exe compile unity`.
- Do not stage unrelated existing worktree changes; use explicit `git add <paths>`.
- First version supports only `AttackModifier`, `DefenseModifier`, `HealthRegen`, and `HealthDamage`.
- External callers operate Buffs only by `buffId`; Buff values come from config.
- Re-adding the same `buffId` refreshes duration only and does not stack values.
- Health tick Buffs wait one `tickInterval` before the first tick.

---

## File Structure

- Create `Assets/Game/Battle/Buff/CombatBuffType.cs`
  - Owns the first-version Buff type enum.
- Create `Assets/Game/Battle/Buff/CombatBuffConfig.cs`
  - Serializable config model used by JSON loading and runtime controller.
- Create `Assets/Game/Battle/Buff/CombatBuffController.cs`
  - Runtime component for adding/removing Buffs, ticking durations, applying health ticks, and calculating modified attack/defense.
- Create `Assets/Data/BuffConfig.json`
  - Initial sample Buff definitions for attack, defense, regen, and damage-over-time.
- Modify `Assets/Framework/Manager/ConfigManager.cs`
  - Load, validate, and query Buff configs.
- Modify `Assets/Game/Battle/Ability/CombatAttributeSet.cs`
  - Player attack/defense return Buff-modified final values.
- Modify `Assets/Game/Character/Enemy/Components/EnemyAttributeComponent.cs`
  - Enemy attack/defense return Buff-modified final values.
- Create `Assets/Game/Editor/CombatBuffControllerEditModeTests.cs`
  - TDD coverage for missing config, refresh behavior, attack/defense modifiers, and health ticks.
- Create `Assets/Game/Editor/BuffConfigEditModeTests.cs`
  - TDD coverage for config validation and querying.

---

### Task 1: Buff Config Model And ConfigManager Loading

**Files:**
- Create: `Assets/Game/Battle/Buff/CombatBuffType.cs`
- Create: `Assets/Game/Battle/Buff/CombatBuffConfig.cs`
- Create: `Assets/Data/BuffConfig.json`
- Modify: `Assets/Framework/Manager/ConfigManager.cs`
- Test: `Assets/Game/Editor/BuffConfigEditModeTests.cs`

**Interfaces:**
- Produces: `Game.Battle.Buff.CombatBuffType`
- Produces: `Game.Battle.Buff.CombatBuffConfig`
- Produces: `ConfigManager.GetBuffConfig(int id) : CombatBuffConfig`
- Produces: `ConfigManager.SetBuffConfigsForTests(params CombatBuffConfig[] configs) : void` inside `#if UNITY_EDITOR`

- [ ] **Step 1: Write the failing config query test**

Create `Assets/Game/Editor/BuffConfigEditModeTests.cs`:

```csharp
using Game.Battle.Buff;
using GameMain2.Framework.Manager;
using NUnit.Framework;

namespace Game.Battle.Tests
{
    public sealed class BuffConfigEditModeTests
    {
        /// <summary>验证配置管理器能按 BuffId 返回对应 Buff 配置。</summary>
        [Test]
        public void GetBuffConfig_ExistingId_ReturnsConfig()
        {
            ConfigManager manager = new ConfigManager();
            CombatBuffConfig attackBuff = new CombatBuffConfig
            {
                buffId = 1001,
                buffName = "攻击强化",
                type = CombatBuffType.AttackModifier,
                duration = 5f,
                flatValue = 10,
                percentValue = 0.2f
            };

            manager.SetBuffConfigsForTests(attackBuff);

            CombatBuffConfig result = manager.GetBuffConfig(1001);

            Assert.AreSame(attackBuff, result);
        }

        /// <summary>验证配置管理器找不到 BuffId 时返回空配置。</summary>
        [Test]
        public void GetBuffConfig_MissingId_ReturnsNull()
        {
            ConfigManager manager = new ConfigManager();
            manager.SetBuffConfigsForTests();

            CombatBuffConfig result = manager.GetBuffConfig(9999);

            Assert.IsNull(result);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe asset refresh
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: compile fails because `Game.Battle.Buff`, `CombatBuffConfig`, `CombatBuffType`, `GetBuffConfig`, or `SetBuffConfigsForTests` do not exist.

- [ ] **Step 3: Add Buff enum and config model**

Create `Assets/Game/Battle/Buff/CombatBuffType.cs`:

```csharp
namespace Game.Battle.Buff
{
    public enum CombatBuffType
    {
        AttackModifier,
        DefenseModifier,
        HealthRegen,
        HealthDamage
    }
}
```

Create `Assets/Game/Battle/Buff/CombatBuffConfig.cs`:

```csharp
using System;
using Game.Common;

namespace Game.Battle.Buff
{
    [Serializable]
    public sealed class CombatBuffConfig : IConfig
    {
        public int buffId;
        public string buffName;
        public CombatBuffType type;
        public float duration;
        public int flatValue;
        public float percentValue;
        public float tickInterval;
        public int tickValue;
    }
}
```

- [ ] **Step 4: Add initial Buff JSON**

Create `Assets/Data/BuffConfig.json`:

```json
[
  {
    "buffId": 1001,
    "buffName": "攻击强化",
    "type": "AttackModifier",
    "duration": 5.0,
    "flatValue": 10,
    "percentValue": 0.2,
    "tickInterval": 0.0,
    "tickValue": 0
  },
  {
    "buffId": 1002,
    "buffName": "防御强化",
    "type": "DefenseModifier",
    "duration": 5.0,
    "flatValue": 5,
    "percentValue": 0.1,
    "tickInterval": 0.0,
    "tickValue": 0
  },
  {
    "buffId": 2001,
    "buffName": "持续回血",
    "type": "HealthRegen",
    "duration": 6.0,
    "flatValue": 0,
    "percentValue": 0.0,
    "tickInterval": 1.0,
    "tickValue": 5
  },
  {
    "buffId": 2002,
    "buffName": "持续扣血",
    "type": "HealthDamage",
    "duration": 6.0,
    "flatValue": 0,
    "percentValue": 0.0,
    "tickInterval": 1.0,
    "tickValue": 5
  }
]
```

- [ ] **Step 5: Wire ConfigManager**

Modify `Assets/Framework/Manager/ConfigManager.cs`:

```csharp
using Game.Battle.Buff;
```

Add field near other config dictionaries:

```csharp
private readonly Dictionary<int, CombatBuffConfig> m_buffConfigs = new Dictionary<int, CombatBuffConfig>();
```

Call loader in `Awake` after skill configs:

```csharp
_LoadBuffConfigs();
```

Add methods:

```csharp
/// <summary>加载并校验全部 Buff 配置。</summary>
private void _LoadBuffConfigs()
{
    TextAsset buffConfigAsset = ResourceManager.Instance.LoadAsset<TextAsset>("Data/BuffConfig.json");
    if (buffConfigAsset == null)
    {
        throw new Exception("未找到 Buff 配置文件：Data/BuffConfig.json");
    }

    CombatBuffConfig[] configs = JsonConvert.DeserializeObject<CombatBuffConfig[]>(buffConfigAsset.text);
    if (configs == null)
    {
        throw new Exception("Buff 配置文件解析失败：Data/BuffConfig.json");
    }

    m_buffConfigs.Clear();
    for (int i = 0; i < configs.Length; i++)
    {
        ValidateBuffConfig(configs[i]);
        if (m_buffConfigs.ContainsKey(configs[i].buffId))
        {
            throw new Exception($"Buff 配置存在重复Id：{configs[i].buffId}");
        }

        m_buffConfigs.Add(configs[i].buffId, configs[i]);
    }
}

/// <summary>校验单个 Buff 配置的基础字段。</summary>
private static void ValidateBuffConfig(CombatBuffConfig config)
{
    if (config == null)
    {
        throw new Exception("Buff 配置存在空配置项");
    }

    if (config.buffId <= 0)
    {
        throw new Exception($"Buff 配置存在非法Id：{config.buffId}");
    }

    if (config.duration <= 0f)
    {
        throw new Exception($"Buff{config.buffId}持续时间必须大于零");
    }

    if ((config.type == CombatBuffType.HealthRegen || config.type == CombatBuffType.HealthDamage)
        && (config.tickInterval <= 0f || config.tickValue <= 0))
    {
        throw new Exception($"Buff{config.buffId}持续生命效果必须配置正数 Tick 间隔和数值");
    }
}

/// <summary>按 BuffId 查询 Buff 配置，缺失时返回 null 供调用方软失败。</summary>
public CombatBuffConfig GetBuffConfig(int id)
{
    CombatBuffConfig config;
    m_buffConfigs.TryGetValue(id, out config);
    return config;
}

#if UNITY_EDITOR
/// <summary>为 EditMode 测试直接注入 Buff 配置集合。</summary>
public void SetBuffConfigsForTests(params CombatBuffConfig[] configs)
{
    m_buffConfigs.Clear();
    for (int i = 0; i < configs.Length; i++)
    {
        ValidateBuffConfig(configs[i]);
        m_buffConfigs.Add(configs[i].buffId, configs[i]);
    }
}
#endif
```

- [ ] **Step 6: Run tests and compile**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe asset refresh
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --test-name Game.Battle.Tests.BuffConfigEditModeTests.GetBuffConfig_ExistingId_ReturnsConfig --timeout 120000
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --test-name Game.Battle.Tests.BuffConfigEditModeTests.GetBuffConfig_MissingId_ReturnsNull --timeout 120000
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: both tests pass and Unity compile reports `errorCount:0`.

- [ ] **Step 7: Commit**

Run:

```powershell
git add Assets/Game/Battle/Buff/CombatBuffType.cs Assets/Game/Battle/Buff/CombatBuffConfig.cs Assets/Data/BuffConfig.json Assets/Framework/Manager/ConfigManager.cs Assets/Game/Editor/BuffConfigEditModeTests.cs
git commit -m "新增Buff配置加载"
```

---

### Task 2: CombatBuffController Runtime Behavior

**Files:**
- Create: `Assets/Game/Battle/Buff/CombatBuffController.cs`
- Test: `Assets/Game/Editor/CombatBuffControllerEditModeTests.cs`

**Interfaces:**
- Consumes: `CombatBuffConfig`, `CombatBuffType`
- Produces: `bool AddBuff(int buffId)`
- Produces: `bool RemoveBuff(int buffId)`
- Produces: `bool HasBuff(int buffId)`
- Produces: `void ClearBuffs()`
- Produces: `int CalculateAttack(int baseAttack)`
- Produces: `int CalculateDefense(int baseDefense)`
- Produces: `void Tick(float deltaTime)`
- Produces: `SetBuffConfigResolverForTests(Func<int, CombatBuffConfig> resolver)` inside `#if UNITY_EDITOR`

- [ ] **Step 1: Write failing tests for Add/refresh/modifiers/ticks**

Create `Assets/Game/Editor/CombatBuffControllerEditModeTests.cs`:

```csharp
using System;
using Game.Battle.Ability;
using Game.Battle.Buff;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Game.Battle.Tests
{
    public sealed class CombatBuffControllerEditModeTests
    {
        /// <summary>验证缺失 Buff 配置时添加失败并记录错误。</summary>
        [Test]
        public void AddBuff_MissingConfig_ReturnsFalseAndLogsError()
        {
            GameObject actor = new GameObject("actor");
            TestCombatAttributes attributes = actor.AddComponent<TestCombatAttributes>();
            CombatBuffController controller = actor.AddComponent<CombatBuffController>();
            attributes.Initialize(100, 100, 10, 0);
            controller.InitializeForTests(attributes);
            controller.SetBuffConfigResolverForTests(id => null);
            LogAssert.Expect(LogType.Error, "未找到 Buff 配置：9999");

            bool added = controller.AddBuff(9999);

            Assert.IsFalse(added);
            UnityEngine.Object.DestroyImmediate(actor);
        }

        /// <summary>验证重复添加同 BuffId 只刷新时间，不叠加攻击加成。</summary>
        [Test]
        public void AddBuff_DuplicateId_RefreshesDurationWithoutStacking()
        {
            GameObject actor = new GameObject("actor");
            TestCombatAttributes attributes = actor.AddComponent<TestCombatAttributes>();
            CombatBuffController controller = actor.AddComponent<CombatBuffController>();
            attributes.Initialize(100, 100, 100, 10);
            controller.InitializeForTests(attributes);
            controller.SetBuffConfigResolverForTests(id => CreateAttackBuff(id, 1f, 10, 0f));

            controller.AddBuff(1001);
            controller.Tick(0.75f);
            controller.AddBuff(1001);

            Assert.AreEqual(110, controller.CalculateAttack(attributes.Attack));
            controller.Tick(0.75f);
            Assert.IsTrue(controller.HasBuff(1001));
            Assert.AreEqual(110, controller.CalculateAttack(attributes.Attack));
            controller.Tick(0.26f);
            Assert.IsFalse(controller.HasBuff(1001));
            Assert.AreEqual(100, controller.CalculateAttack(attributes.Attack));
            UnityEngine.Object.DestroyImmediate(actor);
        }

        /// <summary>验证攻击和防御 Buff 同时支持固定值和百分比。</summary>
        [Test]
        public void CalculateAttributes_AppliesFlatAndPercentModifiers()
        {
            GameObject actor = new GameObject("actor");
            TestCombatAttributes attributes = actor.AddComponent<TestCombatAttributes>();
            CombatBuffController controller = actor.AddComponent<CombatBuffController>();
            attributes.Initialize(100, 100, 100, 50);
            controller.InitializeForTests(attributes);
            controller.SetBuffConfigResolverForTests(id =>
            {
                if (id == 1001)
                {
                    return CreateAttackBuff(id, 5f, 10, 0.2f);
                }

                return CreateDefenseBuff(id, 5f, 5, 0.1f);
            });

            controller.AddBuff(1001);
            controller.AddBuff(1002);

            Assert.AreEqual(130, controller.CalculateAttack(attributes.Attack));
            Assert.AreEqual(60, controller.CalculateDefense(attributes.Defense));
            UnityEngine.Object.DestroyImmediate(actor);
        }

        /// <summary>验证持续回血等待一个 Tick 间隔后第一次生效。</summary>
        [Test]
        public void Tick_HealthRegen_WaitsIntervalBeforeFirstRestore()
        {
            GameObject actor = new GameObject("actor");
            TestCombatAttributes attributes = actor.AddComponent<TestCombatAttributes>();
            CombatBuffController controller = actor.AddComponent<CombatBuffController>();
            attributes.Initialize(100, 100, 10, 0);
            attributes.ApplyHealthDamage(50);
            controller.InitializeForTests(attributes);
            controller.SetBuffConfigResolverForTests(id => CreateHealthBuff(id, CombatBuffType.HealthRegen, 3f, 1f, 5));

            controller.AddBuff(2001);
            controller.Tick(0.99f);
            Assert.AreEqual(50, attributes.Health);
            controller.Tick(0.01f);
            Assert.AreEqual(55, attributes.Health);
            controller.Tick(1f);
            Assert.AreEqual(60, attributes.Health);
            UnityEngine.Object.DestroyImmediate(actor);
        }

        /// <summary>验证持续扣血等待一个 Tick 间隔后第一次生效。</summary>
        [Test]
        public void Tick_HealthDamage_WaitsIntervalBeforeFirstDamage()
        {
            GameObject actor = new GameObject("actor");
            TestCombatAttributes attributes = actor.AddComponent<TestCombatAttributes>();
            CombatBuffController controller = actor.AddComponent<CombatBuffController>();
            attributes.Initialize(100, 100, 10, 0);
            controller.InitializeForTests(attributes);
            controller.SetBuffConfigResolverForTests(id => CreateHealthBuff(id, CombatBuffType.HealthDamage, 3f, 1f, 5));

            controller.AddBuff(2002);
            controller.Tick(0.99f);
            Assert.AreEqual(100, attributes.Health);
            controller.Tick(0.01f);
            Assert.AreEqual(95, attributes.Health);
            controller.Tick(1f);
            Assert.AreEqual(90, attributes.Health);
            UnityEngine.Object.DestroyImmediate(actor);
        }

        /// <summary>创建测试用攻击 Buff 配置。</summary>
        private static CombatBuffConfig CreateAttackBuff(int id, float duration, int flatValue, float percentValue)
        {
            return new CombatBuffConfig
            {
                buffId = id,
                buffName = "攻击强化",
                type = CombatBuffType.AttackModifier,
                duration = duration,
                flatValue = flatValue,
                percentValue = percentValue
            };
        }

        /// <summary>创建测试用防御 Buff 配置。</summary>
        private static CombatBuffConfig CreateDefenseBuff(int id, float duration, int flatValue, float percentValue)
        {
            return new CombatBuffConfig
            {
                buffId = id,
                buffName = "防御强化",
                type = CombatBuffType.DefenseModifier,
                duration = duration,
                flatValue = flatValue,
                percentValue = percentValue
            };
        }

        /// <summary>创建测试用持续生命 Buff 配置。</summary>
        private static CombatBuffConfig CreateHealthBuff(int id, CombatBuffType type, float duration, float tickInterval, int tickValue)
        {
            return new CombatBuffConfig
            {
                buffId = id,
                buffName = "生命持续效果",
                type = type,
                duration = duration,
                tickInterval = tickInterval,
                tickValue = tickValue
            };
        }

        private sealed class TestCombatAttributes : MonoBehaviour, ICombatAttributes
        {
            public int Health { get; private set; }
            public int MaxHealth { get; private set; }
            public int Stability { get; private set; }
            public int MaxStability { get; private set; }
            public int Attack { get; private set; }
            public int Defense { get; private set; }
            public bool IsDead => Health <= 0;
            public bool IsUnbalanced => Stability <= 0 && !IsDead;
            public event Action<CombatAttributeChanged> AttributeChanged;

            /// <summary>初始化测试属性。</summary>
            public void Initialize(int maxHealth, int maxStability, int attack, int defense)
            {
                MaxHealth = maxHealth;
                Health = maxHealth;
                MaxStability = maxStability;
                Stability = maxStability;
                Attack = attack;
                Defense = defense;
            }

            /// <summary>扣除生命并返回实际扣除量。</summary>
            public int ApplyHealthDamage(int value)
            {
                int applied = Mathf.Clamp(value, 0, Health);
                Health = Mathf.Clamp(Health - applied, 0, MaxHealth);
                AttributeChanged?.Invoke(new CombatAttributeChanged(CombatAttributeType.Health, Health, MaxHealth, -applied));
                return applied;
            }

            /// <summary>恢复生命并返回实际恢复量。</summary>
            public int RestoreHealth(int value)
            {
                int restored = Mathf.Clamp(value, 0, MaxHealth - Health);
                Health = Mathf.Clamp(Health + restored, 0, MaxHealth);
                AttributeChanged?.Invoke(new CombatAttributeChanged(CombatAttributeType.Health, Health, MaxHealth, restored));
                return restored;
            }

            /// <summary>扣除稳定值并返回实际扣除量。</summary>
            public int ApplyStabilityDamage(int value)
            {
                int applied = Mathf.Clamp(value, 0, Stability);
                Stability = Mathf.Clamp(Stability - applied, 0, MaxStability);
                AttributeChanged?.Invoke(new CombatAttributeChanged(CombatAttributeType.Stability, Stability, MaxStability, -applied));
                return applied;
            }

            /// <summary>恢复稳定值并返回实际恢复量。</summary>
            public int RestoreStability(int value)
            {
                int restored = Mathf.Clamp(value, 0, MaxStability - Stability);
                Stability = Mathf.Clamp(Stability + restored, 0, MaxStability);
                AttributeChanged?.Invoke(new CombatAttributeChanged(CombatAttributeType.Stability, Stability, MaxStability, restored));
                return restored;
            }
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe asset refresh
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: compile fails because `CombatBuffController` does not exist.

- [ ] **Step 3: Implement CombatBuffController**

Create `Assets/Game/Battle/Buff/CombatBuffController.cs`:

```csharp
using System;
using System.Collections.Generic;
using Game.Battle.Ability;
using GameMain2.Framework.Manager;
using UnityEngine;

namespace Game.Battle.Buff
{
    public sealed class CombatBuffController : MonoBehaviour
    {
        private const string MissingAttributesError = "CombatBuffController 缺少 ICombatAttributes，组件已禁用。";
        private readonly Dictionary<int, ActiveCombatBuff> m_activeBuffs = new Dictionary<int, ActiveCombatBuff>();
        private readonly List<int> m_removeBuffer = new List<int>();
        private ICombatAttributes m_attributes;
        private Func<int, CombatBuffConfig> m_configResolver;

        /// <summary>初始化 Buff 控制器依赖。</summary>
        private void Awake()
        {
            InitializeDependencies();
        }

        /// <summary>按帧推进 Buff 生命周期。</summary>
        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>添加 Buff；同 Id 已存在时只刷新持续时间。</summary>
        public bool AddBuff(int buffId)
        {
            CombatBuffConfig config = ResolveConfig(buffId);
            if (config == null)
            {
                Debug.LogError($"未找到 Buff 配置：{buffId}", this);
                return false;
            }

            ActiveCombatBuff activeBuff;
            if (m_activeBuffs.TryGetValue(buffId, out activeBuff))
            {
                activeBuff.Refresh();
                return true;
            }

            m_activeBuffs.Add(buffId, new ActiveCombatBuff(config));
            return true;
        }

        /// <summary>移除指定 Buff，返回是否真的移除。</summary>
        public bool RemoveBuff(int buffId)
        {
            return m_activeBuffs.Remove(buffId);
        }

        /// <summary>检查当前对象是否持有指定 Buff。</summary>
        public bool HasBuff(int buffId)
        {
            return m_activeBuffs.ContainsKey(buffId);
        }

        /// <summary>清空当前对象全部 Buff。</summary>
        public void ClearBuffs()
        {
            m_activeBuffs.Clear();
            m_removeBuffer.Clear();
        }

        /// <summary>计算 Buff 修正后的攻击力。</summary>
        public int CalculateAttack(int baseAttack)
        {
            return CalculateModifiedAttribute(baseAttack, CombatBuffType.AttackModifier);
        }

        /// <summary>计算 Buff 修正后的防御力。</summary>
        public int CalculateDefense(int baseDefense)
        {
            return CalculateModifiedAttribute(baseDefense, CombatBuffType.DefenseModifier);
        }

        /// <summary>按传入时间推进 Buff 计时和持续生命效果。</summary>
        public void Tick(float deltaTime)
        {
            m_removeBuffer.Clear();
            foreach (KeyValuePair<int, ActiveCombatBuff> pair in m_activeBuffs)
            {
                ActiveCombatBuff activeBuff = pair.Value;
                activeBuff.Tick(deltaTime, m_attributes);
                if (activeBuff.IsExpired)
                {
                    m_removeBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < m_removeBuffer.Count; i++)
            {
                m_activeBuffs.Remove(m_removeBuffer[i]);
            }
        }

        /// <summary>解析序列化依赖并缓存配置查询入口。</summary>
        private void InitializeDependencies()
        {
            m_attributes = GetComponent<ICombatAttributes>();
            if (m_attributes == null)
            {
                Debug.LogError(MissingAttributesError, this);
                enabled = false;
                return;
            }

            m_configResolver = ConfigManager.Instance.GetBuffConfig;
        }

        /// <summary>查询 Buff 配置。</summary>
        private CombatBuffConfig ResolveConfig(int buffId)
        {
            if (m_configResolver == null)
            {
                m_configResolver = ConfigManager.Instance.GetBuffConfig;
            }

            return m_configResolver(buffId);
        }

        /// <summary>按 Buff 类型计算固定值和百分比修正后的属性。</summary>
        private int CalculateModifiedAttribute(int baseValue, CombatBuffType type)
        {
            int flatBonus = 0;
            float percentBonus = 0f;
            foreach (ActiveCombatBuff activeBuff in m_activeBuffs.Values)
            {
                if (activeBuff.Config.type != type)
                {
                    continue;
                }

                flatBonus += activeBuff.Config.flatValue;
                percentBonus += activeBuff.Config.percentValue;
            }

            return Mathf.Max(0, Mathf.RoundToInt(baseValue * (1f + percentBonus) + flatBonus));
        }

#if UNITY_EDITOR
        /// <summary>为 EditMode 测试注入属性依赖。</summary>
        public void InitializeForTests(ICombatAttributes attributes)
        {
            m_attributes = attributes;
        }

        /// <summary>为 EditMode 测试注入配置查询入口。</summary>
        public void SetBuffConfigResolverForTests(Func<int, CombatBuffConfig> resolver)
        {
            m_configResolver = resolver;
        }
#endif

        private sealed class ActiveCombatBuff
        {
            public CombatBuffConfig Config { get; }
            public bool IsExpired => RemainingTime <= 0f;
            private float RemainingTime { get; set; }
            private float TickRemainingTime { get; set; }

            /// <summary>创建运行时 Buff，并等待一个 Tick 间隔后首次触发生命效果。</summary>
            public ActiveCombatBuff(CombatBuffConfig config)
            {
                Config = config;
                Refresh();
            }

            /// <summary>刷新持续时间和 Tick 等待时间。</summary>
            public void Refresh()
            {
                RemainingTime = Config.duration;
                TickRemainingTime = Config.tickInterval;
            }

            /// <summary>推进运行时 Buff 时间并触发生命 Tick。</summary>
            public void Tick(float deltaTime, ICombatAttributes attributes)
            {
                RemainingTime -= deltaTime;
                if (Config.type != CombatBuffType.HealthRegen && Config.type != CombatBuffType.HealthDamage)
                {
                    return;
                }

                TickRemainingTime -= deltaTime;
                while (TickRemainingTime <= 0f && !IsExpired)
                {
                    ApplyHealthTick(attributes);
                    TickRemainingTime += Config.tickInterval;
                }
            }

            /// <summary>按 Buff 类型执行一次生命恢复或扣除。</summary>
            private void ApplyHealthTick(ICombatAttributes attributes)
            {
                if (Config.type == CombatBuffType.HealthRegen)
                {
                    attributes.RestoreHealth(Config.tickValue);
                }
                else if (Config.type == CombatBuffType.HealthDamage)
                {
                    attributes.ApplyHealthDamage(Config.tickValue);
                }
            }
        }
    }
}
```

- [ ] **Step 4: Run controller tests**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe asset refresh
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name Game.Battle.Tests.CombatBuffControllerEditModeTests --timeout 120000
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: controller tests pass and Unity compile reports `errorCount:0`.

- [ ] **Step 5: Commit**

Run:

```powershell
git add Assets/Game/Battle/Buff/CombatBuffController.cs Assets/Game/Editor/CombatBuffControllerEditModeTests.cs
git commit -m "新增战斗Buff控制器"
```

---

### Task 3: Attribute Components Consume Buff Modifiers

**Files:**
- Modify: `Assets/Game/Battle/Ability/CombatAttributeSet.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyAttributeComponent.cs`
- Test: extend `Assets/Game/Editor/CombatBuffControllerEditModeTests.cs`

**Interfaces:**
- Consumes: `CombatBuffController.CalculateAttack(int)`
- Consumes: `CombatBuffController.CalculateDefense(int)`
- Produces: player and enemy `Attack` / `Defense` return final Buff-modified values

- [ ] **Step 1: Write failing integration tests for player and enemy attributes**

Append tests to `CombatBuffControllerEditModeTests`:

```csharp
/// <summary>验证玩家属性组件返回 Buff 修正后的攻击力和防御力。</summary>
[Test]
public void PlayerAttributes_ReturnBuffModifiedAttackAndDefense()
{
    GameObject player = new GameObject("player");
    CombatAttributeSet attributes = player.AddComponent<CombatAttributeSet>();
    CombatBuffController controller = player.AddComponent<CombatBuffController>();
    controller.InitializeForTests(attributes);
    controller.SetBuffConfigResolverForTests(id =>
    {
        if (id == 1001)
        {
            return CreateAttackBuff(id, 5f, 10, 0.2f);
        }

        return CreateDefenseBuff(id, 5f, 5, 0.1f);
    });

    controller.AddBuff(1001);
    controller.AddBuff(1002);

    Assert.AreEqual(22, attributes.Attack);
    Assert.AreEqual(5, attributes.Defense);
    UnityEngine.Object.DestroyImmediate(player);
}

/// <summary>验证敌人属性组件返回 Buff 修正后的攻击力和防御力。</summary>
[Test]
public void EnemyAttributes_ReturnBuffModifiedAttackAndDefense()
{
    GameObject enemy = new GameObject("enemy");
    EnemyAttributeComponent attributes = enemy.AddComponent<EnemyAttributeComponent>();
    CombatBuffController controller = enemy.AddComponent<CombatBuffController>();
    EnemyDefinition definition = ScriptableObject.CreateInstance<EnemyDefinition>();
    definition.SetAttributeConfigForTests(new EnemyAttributeConfig
    {
        maxHealth = 100,
        maxStability = 100,
        attack = 20,
        defense = 10
    });
    attributes.LoadFromDefinition(definition);
    controller.InitializeForTests(attributes);
    controller.SetBuffConfigResolverForTests(id =>
    {
        if (id == 1001)
        {
            return CreateAttackBuff(id, 5f, 10, 0.2f);
        }

        return CreateDefenseBuff(id, 5f, 5, 0.1f);
    });

    controller.AddBuff(1001);
    controller.AddBuff(1002);

    Assert.AreEqual(34, attributes.Attack);
    Assert.AreEqual(16, attributes.Defense);
    UnityEngine.Object.DestroyImmediate(enemy);
    UnityEngine.Object.DestroyImmediate(definition);
}
```

If `EnemyDefinition` does not expose a test setter, add one in this task:

```csharp
#if UNITY_EDITOR
/// <summary>为 EditMode 测试注入敌人属性配置。</summary>
public void SetAttributeConfigForTests(EnemyAttributeConfig config)
{
    attributeConfig = config;
}
#endif
```

- [ ] **Step 2: Run tests to verify they fail**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe asset refresh
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name Game.Battle.Tests.CombatBuffControllerEditModeTests --timeout 120000
```

Expected: new player/enemy attribute tests fail because `Attack` and `Defense` still return base values.

- [ ] **Step 3: Modify player attribute component**

In `Assets/Game/Battle/Ability/CombatAttributeSet.cs`, add:

```csharp
using Game.Battle.Buff;
```

Add field:

```csharp
private CombatBuffController m_buffController;
```

Replace attack/defense properties:

```csharp
public int Attack => GetModifiedAttack();
public int Defense => GetModifiedDefense();
```

Add methods:

```csharp
/// <summary>获取 Buff 修正后的玩家攻击力。</summary>
private int GetModifiedAttack()
{
    CombatBuffController buffController = GetBuffController();
    return buffController != null ? buffController.CalculateAttack(attack) : attack;
}

/// <summary>获取 Buff 修正后的玩家防御力。</summary>
private int GetModifiedDefense()
{
    CombatBuffController buffController = GetBuffController();
    return buffController != null ? buffController.CalculateDefense(defense) : defense;
}

/// <summary>懒加载同对象上的 Buff 控制器。</summary>
private CombatBuffController GetBuffController()
{
    if (m_buffController == null)
    {
        TryGetComponent(out m_buffController);
    }

    return m_buffController;
}
```

- [ ] **Step 4: Modify enemy attribute component**

In `Assets/Game/Character/Enemy/Components/EnemyAttributeComponent.cs`, add:

```csharp
using Game.Battle.Buff;
```

Replace existing auto-properties:

```csharp
public int Attack => GetModifiedAttack();
public int Defense => GetModifiedDefense();
private int BaseAttack { get; set; }
private int BaseDefense { get; set; }
private CombatBuffController m_buffController;
```

Update `LoadFromDefinition`:

```csharp
BaseAttack = config.attack;
BaseDefense = config.defense;
```

Add methods:

```csharp
/// <summary>获取 Buff 修正后的敌人攻击力。</summary>
private int GetModifiedAttack()
{
    CombatBuffController buffController = GetBuffController();
    return buffController != null ? buffController.CalculateAttack(BaseAttack) : BaseAttack;
}

/// <summary>获取 Buff 修正后的敌人防御力。</summary>
private int GetModifiedDefense()
{
    CombatBuffController buffController = GetBuffController();
    return buffController != null ? buffController.CalculateDefense(BaseDefense) : BaseDefense;
}

/// <summary>懒加载同对象上的 Buff 控制器。</summary>
private CombatBuffController GetBuffController()
{
    if (m_buffController == null)
    {
        TryGetComponent(out m_buffController);
    }

    return m_buffController;
}
```

- [ ] **Step 5: Run integration tests and compile**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe asset refresh
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name Game.Battle.Tests.CombatBuffControllerEditModeTests --timeout 120000
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: all Buff controller tests pass and Unity compile reports `errorCount:0`.

- [ ] **Step 6: Commit**

Run:

```powershell
git add Assets/Game/Battle/Ability/CombatAttributeSet.cs Assets/Game/Character/Enemy/Components/EnemyAttributeComponent.cs Assets/Game/Character/Enemy/Config/EnemyDefinition.cs Assets/Game/Editor/CombatBuffControllerEditModeTests.cs
git commit -m "接入Buff属性修正"
```

---

### Task 4: Final Validation And Error Log Sweep

**Files:**
- No production file changes expected.
- Test: all Buff-related EditMode tests.

**Interfaces:**
- Verifies: `ConfigManager.GetBuffConfig`
- Verifies: `CombatBuffController`
- Verifies: player and enemy attribute components

- [ ] **Step 1: Run focused Buff tests**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name Game.Battle.Tests.BuffConfigEditModeTests --timeout 120000
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name Game.Battle.Tests.CombatBuffControllerEditModeTests --timeout 120000
```

Expected: all tests pass with `failed:0`.

- [ ] **Step 2: Run Unity compile**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: `success:true`, `errorCount:0`, `warningCount:0`.

- [ ] **Step 3: Check Unity error logs**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe get_logs --logType Error
```

Expected: `count:0`.

- [ ] **Step 4: Confirm old scope boundaries**

Run:

```powershell
rg -n "onHitBuffIds|selfBuffIds|BuffConfig" Assets/Data/WeaponConfig Assets/Data/EnemySkillConfig.json
```

Expected: no skill config Buff fields are present. `BuffConfig` should only appear in Buff implementation or tests, not in skill JSON.

- [ ] **Step 5: Commit validation-only fixes if needed**

If Step 1-4 reveal small fixes, stage only touched Buff files:

```powershell
git add Assets/Game/Battle/Buff Assets/Game/Editor/CombatBuffControllerEditModeTests.cs Assets/Game/Editor/BuffConfigEditModeTests.cs Assets/Framework/Manager/ConfigManager.cs Assets/Game/Battle/Ability/CombatAttributeSet.cs Assets/Game/Character/Enemy/Components/EnemyAttributeComponent.cs
git commit -m "完善Buff系统验证"
```

If no files changed during validation, do not create an empty commit.

---

## Self-Review

- Spec coverage: Buff ID external API, config loading, four Buff types, refresh-only duplicate behavior, fixed Tick delay, missing config soft failure, and player/enemy shared usage are covered by Tasks 1-4.
- Placeholder scan: This plan contains concrete file paths, method names, code snippets, commands, and expected outcomes.
- Type consistency: The plan consistently uses `CombatBuffConfig`, `CombatBuffType`, `CombatBuffController`, `AddBuff(int)`, `RemoveBuff(int)`, `HasBuff(int)`, `ClearBuffs()`, `CalculateAttack(int)`, and `CalculateDefense(int)`.
