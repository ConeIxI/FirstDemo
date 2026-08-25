# 基础战斗与技能框架 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 保留现有 FSM、装备、动画事件和武器碰撞链路，落地第一阶段基础战斗闭环：生命、稳定、战意、防御、格挡、翻滚无敌、打断、霸体、命中特效和 HUD 展示。

**Architecture:** 新增 `Combatant` 作为角色战斗门面，聚合 `CombatStats`、`CombatResource`、`CombatState`、`SkillRunner`、`StatusEffectSystem` 和 `CombatMotionController`。命中统一生成 `CombatHit`，由 `DamageResolver` 和 `InterruptResolver` 得出 `CombatResult`，再由 `CombatReaction` 与 `CombatEffectExecutor` 推动 FSM、资源和表现。

**Tech Stack:** Unity 2022.3.61f1c1 / C# 9.0 / Unity Test Framework EditMode / AIBridge CLI (`$CLI = ./.aibridge/cli/AIBridgeCLI.exe`) / Newtonsoft.Json / 现有 EventCenter 与 FSM 框架。

**Detailed Design:** `docs/superpowers/specs/2026-06-18-combat-skill-framework-design.md`

---

## 文件结构概览

**新建：**

```text
Assets/Game/Battle/Combat/Combatant.cs
Assets/Game/Battle/Combat/CombatStats.cs
Assets/Game/Battle/Combat/CombatResource.cs
Assets/Game/Battle/Combat/CombatState.cs
Assets/Game/Battle/Combat/CombatHit.cs
Assets/Game/Battle/Combat/CombatResult.cs
Assets/Game/Battle/Combat/DamageResolver.cs
Assets/Game/Battle/Combat/CombatReaction.cs
Assets/Game/Battle/Combat/Interrupt/InterruptResolver.cs
Assets/Game/Battle/Skill/SkillRunner.cs
Assets/Game/Battle/Skill/SkillContext.cs
Assets/Game/Battle/Skill/Effects/CombatEffectExecutor.cs
Assets/Game/Battle/Skill/Effects/SkillEffectData.cs
Assets/Game/Battle/StatusEffect/StatusEffectSystem.cs
Assets/Game/Battle/StatusEffect/StatusEffectConfig.cs
Assets/Game/Battle/StatusEffect/StatusEffectInstance.cs
Assets/Game/Battle/Motion/CombatMotionController.cs
Assets/Tests/EditMode/EditModeTests.asmdef
Assets/Tests/EditMode/Combat/CombatStatsTests.cs
Assets/Tests/EditMode/Combat/DamageResolverTests.cs
Assets/Tests/EditMode/Skill/SkillConfigSerializationTests.cs
```

**修改：**

```text
Assets/Game/Battle/Skill/SkillDefine.cs
Assets/Game/Battle/Skill/Common/SkillConfig.cs
Assets/Game/Character/Player/PlayerSkillManager.cs
Assets/Game/Character/Enemy/EnemySkillManager.cs
Assets/Game/Character/Player/PlayerFsm/AttackState.cs
Assets/Game/Character/Enemy/EnemyFsm/Common/AttackState.cs
Assets/Game/Character/Player/PlayerFsm/DefenceState.cs
Assets/Game/Character/Player/PlayerFsm/RollState.cs
Assets/Game/UI/BattleHudPanel.cs
Assets/Framework/Manager/ConfigManager.cs
Assets/Data/WeaponConfig/SingleSwordSkillConfig.json
Assets/Data/WeaponConfig/GreatSwordSkillConfig.json
Assets/Data/EnemySkillConfig.json
```

**保留但不再作为命中结算入口：**

```text
Assets/Game/Battle/Skill/PlayerSkill/PlayerSkillAttack.cs
Assets/Game/Battle/Skill/EnemySkill/EnemySkillAttack.cs
Assets/Game/Battle/Skill/SkillBase.cs
```

---

## Task 1: 建立战斗状态、属性、资源的 EditMode 测试与核心类

**Files:**
- Create: `Assets/Tests/EditMode/EditModeTests.asmdef`
- Create: `Assets/Tests/EditMode/Combat/CombatStatsTests.cs`
- Create: `Assets/Game/Battle/Combat/CombatStats.cs`
- Create: `Assets/Game/Battle/Combat/CombatResource.cs`
- Create: `Assets/Game/Battle/Combat/CombatState.cs`

- [ ] **Step 1: 创建测试程序集**

Create `Assets/Tests/EditMode/EditModeTests.asmdef`:

```json
{
  "name": "FirstGameDemo.EditModeTests",
  "references": [
    "Assembly-CSharp"
  ],
  "optionalUnityReferences": [
    "TestAssemblies"
  ],
  "includePlatforms": [
    "Editor"
  ],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 2: 写失败测试**

Create `Assets/Tests/EditMode/Combat/CombatStatsTests.cs`:

```csharp
using Game.Battle.Combat;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Combat
{
    public class CombatStatsTests
    {
        [Test]
        public void TakeDamage_ClampsHealthAndRaisesDeathFlag()
        {
            GameObject go = new GameObject("target");
            CombatStats stats = go.AddComponent<CombatStats>();
            stats.Initialize(100, 50);

            stats.ApplyHealthDamage(125);

            Assert.AreEqual(0, stats.CurrentHealth);
            Assert.IsTrue(stats.IsDead);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void Stability_RegeneratesOnlyAfterDelay()
        {
            GameObject go = new GameObject("target");
            CombatStats stats = go.AddComponent<CombatStats>();
            stats.Initialize(100, 50);

            stats.ApplyStabilityDamage(30);
            stats.TickStabilityRecovery(0.2f, 1f, 10f);
            Assert.AreEqual(20, stats.CurrentStability);

            stats.TickStabilityRecovery(1.0f, 1f, 10f);
            Assert.AreEqual(30, stats.CurrentStability);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void BattleSpirit_ClampsSpendAndGain()
        {
            GameObject go = new GameObject("player");
            CombatResource resource = go.AddComponent<CombatResource>();
            resource.Initialize(100);

            Assert.IsFalse(resource.TryConsumeBattleSpirit(10));
            resource.AddBattleSpirit(120);
            Assert.AreEqual(100, resource.CurrentBattleSpirit);
            Assert.IsTrue(resource.TryConsumeBattleSpirit(40));
            Assert.AreEqual(60, resource.CurrentBattleSpirit);
            Object.DestroyImmediate(go);
        }

        [Test]
        public void CombatState_TracksDefenceParryInvincibleAndInterruptRules()
        {
            GameObject go = new GameObject("actor");
            CombatState state = go.AddComponent<CombatState>();

            state.BeginDefence(0.25f);
            Assert.IsTrue(state.IsDefending);
            Assert.IsTrue(state.IsParryWindowActive);

            state.Tick(0.3f);
            Assert.IsTrue(state.IsDefending);
            Assert.IsFalse(state.IsParryWindowActive);

            state.SetInvincible(0.2f);
            Assert.IsTrue(state.IsInvincible);
            state.Tick(0.3f);
            Assert.IsFalse(state.IsInvincible);

            state.BeginAction(canBeInterrupted: false, interruptResistLevel: 99);
            Assert.IsFalse(state.CanBeInterrupted);
            Assert.AreEqual(99, state.InterruptResistLevel);
            state.EndAction();
            Assert.IsTrue(state.CanBeInterrupted);
            Assert.AreEqual(0, state.InterruptResistLevel);
            Object.DestroyImmediate(go);
        }
    }
}
```

- [ ] **Step 3: 运行测试确认失败**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --group-name Tests.EditMode.Combat --timeout 120000
```

Expected: FAIL，错误包含 `The type or namespace name 'CombatStats' could not be found` 或同类缺失类型错误。

- [ ] **Step 4: 创建 CombatStats**

Create `Assets/Game/Battle/Combat/CombatStats.cs`:

```csharp
using System;
using UnityEngine;

namespace Game.Battle.Combat
{
    public class CombatStats : MonoBehaviour
    {
        [SerializeField] private int maxHealth = 100;
        [SerializeField] private int maxStability = 100;

        private float m_stabilityRecoverDelayTimer;

        public int MaxHealth => maxHealth;
        public int CurrentHealth { get; private set; }
        public int MaxStability => maxStability;
        public int CurrentStability { get; private set; }
        public bool IsDead => CurrentHealth <= 0;
        public bool IsUnbalanced => CurrentStability <= 0 && !IsDead;

        public event Action<int, int> HealthChanged;
        public event Action<int, int> StabilityChanged;

        private void Awake()
        {
            if (CurrentHealth <= 0)
            {
                Initialize(maxHealth, maxStability);
            }
        }

        public void Initialize(int health, int stability)
        {
            maxHealth = Mathf.Max(1, health);
            maxStability = Mathf.Max(1, stability);
            CurrentHealth = maxHealth;
            CurrentStability = maxStability;
            m_stabilityRecoverDelayTimer = 0f;
            HealthChanged?.Invoke(CurrentHealth, maxHealth);
            StabilityChanged?.Invoke(CurrentStability, maxStability);
        }

        public int ApplyHealthDamage(int amount)
        {
            int damage = Mathf.Max(0, amount);
            int before = CurrentHealth;
            CurrentHealth = Mathf.Max(0, CurrentHealth - damage);
            if (CurrentHealth != before)
            {
                HealthChanged?.Invoke(CurrentHealth, maxHealth);
            }
            return before - CurrentHealth;
        }

        public int ApplyStabilityDamage(int amount)
        {
            int damage = Mathf.Max(0, amount);
            int before = CurrentStability;
            CurrentStability = Mathf.Max(0, CurrentStability - damage);
            if (CurrentStability != before)
            {
                m_stabilityRecoverDelayTimer = 0f;
                StabilityChanged?.Invoke(CurrentStability, maxStability);
            }
            return before - CurrentStability;
        }

        public int RestoreStability(int amount)
        {
            int restore = Mathf.Max(0, amount);
            int before = CurrentStability;
            CurrentStability = Mathf.Min(maxStability, CurrentStability + restore);
            if (CurrentStability != before)
            {
                StabilityChanged?.Invoke(CurrentStability, maxStability);
            }
            return CurrentStability - before;
        }

        public void TickStabilityRecovery(float deltaTime, float delay, float recoverPerSecond)
        {
            if (IsDead || CurrentStability >= maxStability)
            {
                return;
            }

            m_stabilityRecoverDelayTimer += Mathf.Max(0f, deltaTime);
            if (m_stabilityRecoverDelayTimer < delay)
            {
                return;
            }

            int recover = Mathf.FloorToInt(recoverPerSecond * deltaTime);
            if (recover > 0)
            {
                RestoreStability(recover);
            }
        }
    }
}
```

- [ ] **Step 5: 创建 CombatResource**

Create `Assets/Game/Battle/Combat/CombatResource.cs`:

```csharp
using System;
using UnityEngine;

namespace Game.Battle.Combat
{
    public class CombatResource : MonoBehaviour
    {
        [SerializeField] private int maxBattleSpirit = 100;

        public int MaxBattleSpirit => maxBattleSpirit;
        public int CurrentBattleSpirit { get; private set; }

        public event Action<int, int> BattleSpiritChanged;

        public void Initialize(int maxValue)
        {
            maxBattleSpirit = Mathf.Max(1, maxValue);
            CurrentBattleSpirit = 0;
            BattleSpiritChanged?.Invoke(CurrentBattleSpirit, maxBattleSpirit);
        }

        public void AddBattleSpirit(int amount)
        {
            int before = CurrentBattleSpirit;
            CurrentBattleSpirit = Mathf.Clamp(CurrentBattleSpirit + Mathf.Max(0, amount), 0, maxBattleSpirit);
            if (CurrentBattleSpirit != before)
            {
                BattleSpiritChanged?.Invoke(CurrentBattleSpirit, maxBattleSpirit);
            }
        }

        public bool TryConsumeBattleSpirit(int amount)
        {
            int cost = Mathf.Max(0, amount);
            if (CurrentBattleSpirit < cost)
            {
                return false;
            }

            CurrentBattleSpirit -= cost;
            BattleSpiritChanged?.Invoke(CurrentBattleSpirit, maxBattleSpirit);
            return true;
        }
    }
}
```

- [ ] **Step 6: 创建 CombatState**

Create `Assets/Game/Battle/Combat/CombatState.cs`:

```csharp
using UnityEngine;

namespace Game.Battle.Combat
{
    public class CombatState : MonoBehaviour
    {
        private float m_parryWindowTimer;
        private float m_invincibleTimer;

        public bool IsDefending { get; private set; }
        public bool IsParryWindowActive => m_parryWindowTimer > 0f;
        public bool IsInvincible => m_invincibleTimer > 0f;
        public bool CanBeInterrupted { get; private set; } = true;
        public int InterruptResistLevel { get; private set; }

        public void Tick(float deltaTime)
        {
            float dt = Mathf.Max(0f, deltaTime);
            if (m_parryWindowTimer > 0f)
            {
                m_parryWindowTimer = Mathf.Max(0f, m_parryWindowTimer - dt);
            }

            if (m_invincibleTimer > 0f)
            {
                m_invincibleTimer = Mathf.Max(0f, m_invincibleTimer - dt);
            }
        }

        public void BeginDefence(float parryWindowTime)
        {
            IsDefending = true;
            m_parryWindowTimer = Mathf.Max(0f, parryWindowTime);
        }

        public void EndDefence()
        {
            IsDefending = false;
            m_parryWindowTimer = 0f;
        }

        public void SetInvincible(float duration)
        {
            m_invincibleTimer = Mathf.Max(m_invincibleTimer, duration);
        }

        public void ClearInvincible()
        {
            m_invincibleTimer = 0f;
        }

        public void BeginAction(bool canBeInterrupted, int interruptResistLevel)
        {
            CanBeInterrupted = canBeInterrupted;
            InterruptResistLevel = Mathf.Max(0, interruptResistLevel);
        }

        public void EndAction()
        {
            CanBeInterrupted = true;
            InterruptResistLevel = 0;
        }
    }
}
```

- [ ] **Step 7: 运行测试确认通过**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --group-name Tests.EditMode.Combat --timeout 120000
```

Expected: PASS，`CombatStatsTests` 4 个测试全部通过。

- [ ] **Step 8: 编译验证**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
```

Expected: `success:true`，Unity Console 无编译错误。

- [ ] **Step 9: Commit**

```powershell
git add Assets/Game/Battle/Combat Assets/Tests/EditMode
git commit -m "feat: 新增战斗属性状态与战意资源"
```

---

## Task 2: 实现 CombatHit、CombatResult、DamageResolver、InterruptResolver

**Files:**
- Create: `Assets/Tests/EditMode/Combat/DamageResolverTests.cs`
- Create: `Assets/Game/Battle/Combat/CombatHit.cs`
- Create: `Assets/Game/Battle/Combat/CombatResult.cs`
- Create: `Assets/Game/Battle/Combat/DamageResolver.cs`
- Create: `Assets/Game/Battle/Combat/Interrupt/InterruptResolver.cs`
- Modify: `Assets/Game/Battle/Skill/SkillDefine.cs`
- Modify: `Assets/Game/Battle/Skill/Common/SkillConfig.cs`

- [ ] **Step 1: 写失败测试**

Create `Assets/Tests/EditMode/Combat/DamageResolverTests.cs`:

```csharp
using Game.Battle.Combat;
using Game.Battle.Combat.Interrupt;
using Game.Battle.Skill;
using Game.Battle.Skill.Common;
using NUnit.Framework;
using UnityEngine;

namespace Tests.EditMode.Combat
{
    public class DamageResolverTests
    {
        private static Combatant CreateActor(string name, bool withResource)
        {
            GameObject go = new GameObject(name);
            Combatant actor = go.AddComponent<Combatant>();
            actor.EnsureRuntimeComponents(withResource);
            actor.Stats.Initialize(100, 100);
            if (withResource)
            {
                actor.Resource.Initialize(100);
            }
            return actor;
        }

        [Test]
        public void NormalAttackHit_DamagesTargetAndGrantsBattleSpirit()
        {
            Combatant attacker = CreateActor("player", true);
            Combatant target = CreateActor("enemy", false);
            SkillConfig config = TestSkill(SkillType.NormalAttack, 10, 15, 8);

            CombatResult result = DamageResolver.Resolve(new CombatHit(attacker, target, config, Vector3.zero, Vector3.forward));

            Assert.AreEqual(CombatResultType.Hit, result.ResultType);
            Assert.AreEqual(90, target.Stats.CurrentHealth);
            Assert.AreEqual(85, target.Stats.CurrentStability);
            Assert.AreEqual(8, attacker.Resource.CurrentBattleSpirit);
            Object.DestroyImmediate(attacker.gameObject);
            Object.DestroyImmediate(target.gameObject);
        }

        [Test]
        public void WeaponSkillHit_DoesNotGrantBattleSpirit()
        {
            Combatant attacker = CreateActor("player", true);
            Combatant target = CreateActor("enemy", false);
            SkillConfig config = TestSkill(SkillType.WeaponSkill, 10, 15, 8);

            DamageResolver.Resolve(new CombatHit(attacker, target, config, Vector3.zero, Vector3.forward));

            Assert.AreEqual(0, attacker.Resource.CurrentBattleSpirit);
            Object.DestroyImmediate(attacker.gameObject);
            Object.DestroyImmediate(target.gameObject);
        }

        [Test]
        public void Block_PreventsHealthDamageButConsumesStability()
        {
            Combatant attacker = CreateActor("enemy", false);
            Combatant target = CreateActor("player", true);
            target.State.BeginDefence(0f);

            CombatResult result = DamageResolver.Resolve(new CombatHit(attacker, target, TestSkill(SkillType.EnemySkill, 20, 30, 0), Vector3.zero, Vector3.forward));

            Assert.AreEqual(CombatResultType.Block, result.ResultType);
            Assert.AreEqual(100, target.Stats.CurrentHealth);
            Assert.AreEqual(70, target.Stats.CurrentStability);
            Assert.AreEqual(0, target.Resource.CurrentBattleSpirit);
            Object.DestroyImmediate(attacker.gameObject);
            Object.DestroyImmediate(target.gameObject);
        }

        [Test]
        public void Parry_PreventsDamageAndDamagesAttackerStabilityWithoutBattleSpirit()
        {
            Combatant attacker = CreateActor("enemy", false);
            Combatant target = CreateActor("player", true);
            target.State.BeginDefence(0.2f);

            CombatResult result = DamageResolver.Resolve(new CombatHit(attacker, target, TestSkill(SkillType.EnemySkill, 20, 30, 0), Vector3.zero, Vector3.forward));

            Assert.AreEqual(CombatResultType.Parry, result.ResultType);
            Assert.AreEqual(100, target.Stats.CurrentHealth);
            Assert.AreEqual(100, target.Stats.CurrentStability);
            Assert.AreEqual(70, attacker.Stats.CurrentStability);
            Assert.AreEqual(0, target.Resource.CurrentBattleSpirit);
            Object.DestroyImmediate(attacker.gameObject);
            Object.DestroyImmediate(target.gameObject);
        }

        [Test]
        public void Invincible_PreventsAllDamage()
        {
            Combatant attacker = CreateActor("enemy", false);
            Combatant target = CreateActor("player", true);
            target.State.SetInvincible(0.2f);

            CombatResult result = DamageResolver.Resolve(new CombatHit(attacker, target, TestSkill(SkillType.EnemySkill, 20, 30, 0), Vector3.zero, Vector3.forward));

            Assert.AreEqual(CombatResultType.Invincible, result.ResultType);
            Assert.AreEqual(100, target.Stats.CurrentHealth);
            Assert.AreEqual(100, target.Stats.CurrentStability);
            Object.DestroyImmediate(attacker.gameObject);
            Object.DestroyImmediate(target.gameObject);
        }

        [Test]
        public void Interrupt_RespectsDefenceAndResistLevel()
        {
            Combatant attacker = CreateActor("player", true);
            Combatant target = CreateActor("enemy", false);
            SkillConfig config = TestSkill(SkillType.NormalAttack, 5, 5, 1);
            config.interruptConfig.canInterrupt = true;
            config.interruptConfig.interruptLevel = 1;

            target.State.BeginAction(canBeInterrupted: false, interruptResistLevel: 99);
            CombatResult result = DamageResolver.Resolve(new CombatHit(attacker, target, config, Vector3.zero, Vector3.forward));

            Assert.IsFalse(result.IsInterrupted);
            Assert.IsFalse(result.ShouldPlayHitReaction);
            Object.DestroyImmediate(attacker.gameObject);
            Object.DestroyImmediate(target.gameObject);
        }

        private static SkillConfig TestSkill(SkillType type, int healthDamage, int stabilityDamage, int spiritGain)
        {
            return new SkillConfig
            {
                skillId = 1,
                skillType = type,
                battleSpiritGainOnHit = spiritGain,
                hitConfig = new CombatHitConfig
                {
                    healthDamage = healthDamage,
                    stabilityDamage = stabilityDamage,
                    canBeBlocked = true,
                    canBeParried = true,
                    hitReactionName = "GetHit"
                },
                interruptConfig = new InterruptConfig
                {
                    canInterrupt = false,
                    interruptLevel = 0,
                    canBeInterrupted = true,
                    interruptResistLevel = 0,
                    canInterruptDefence = false
                }
            };
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --group-name Tests.EditMode.Combat --timeout 120000
```

Expected: FAIL，错误包含 `Combatant`、`DamageResolver`、`CombatHitConfig`、`InterruptConfig` 缺失。

- [ ] **Step 3: 扩展 SkillDefine**

Modify `Assets/Game/Battle/Skill/SkillDefine.cs`:

```csharp
namespace Game.Battle.Skill
{
    public enum SkillState
    {
        Reday,
        Casting,
        Cooldown
    }

    public enum SkillType
    {
        NormalAttack,
        WeaponSkill,
        EnemySkill
    }

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

    public enum SkillEffectTarget
    {
        Self,
        Target,
        HitPoint,
        ForwardArea
    }
}
```

- [ ] **Step 4: 扩展 SkillConfig**

Modify `Assets/Game/Battle/Skill/Common/SkillConfig.cs` by adding these fields and classes in the same namespace:

```csharp
using System;
using Game.Battle.Skill;
using Game.Common;
using UnityEngine;

namespace Game.Battle.Skill.Common
{
    [Serializable]
    public class SkillConfig : IConfig
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

    [Serializable]
    public class CombatHitConfig
    {
        public int healthDamage;
        public int stabilityDamage;
        public bool canBeBlocked = true;
        public bool canBeParried = true;
        public float hitStopTime;
        public string hitReactionName = "GetHit";
    }

    [Serializable]
    public class InterruptConfig
    {
        public bool canInterrupt;
        public int interruptLevel;
        public bool canBeInterrupted = true;
        public int interruptResistLevel;
        public bool canInterruptDefence;
    }

    [Serializable]
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

    [Serializable]
    public class SkillEffectConfig
    {
        public EffectObjectInfo[] castEffectInfo;
        public EffectObjectInfo[] hitEffectInfo;
        public EffectObjectInfo[] trailEffectInfo;
    }

    [Serializable]
    public class SkillAudioConfig
    {
        public string castSfxPath;
        public string hitSfxPath;
    }

    [Serializable]
    public class EffectObjectInfo
    {
        public string path;
        public Vec3 position;
        public Vec3 rotation;
        public Vec3 scale;
    }

    [Serializable]
    public struct Vec3
    {
        public float x;
        public float y;
        public float z;

        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }
    }
}
```

- [ ] **Step 5: 创建 Combatant、CombatHit、CombatResult、InterruptResolver、DamageResolver**

Create `Assets/Game/Battle/Combat/Combatant.cs`:

```csharp
using Game.Battle.Motion;
using Game.Battle.StatusEffect;
using UnityEngine;

namespace Game.Battle.Combat
{
    public class Combatant : MonoBehaviour
    {
        public CombatStats Stats { get; private set; }
        public CombatResource Resource { get; private set; }
        public CombatState State { get; private set; }
        public StatusEffectSystem StatusEffects { get; private set; }
        public CombatMotionController Motion { get; private set; }

        private void Awake()
        {
            EnsureRuntimeComponents(CompareTag("Player"));
        }

        private void Update()
        {
            State?.Tick(Time.deltaTime);
        }

        public void EnsureRuntimeComponents(bool withResource)
        {
            Stats = EnsureComponent<CombatStats>();
            State = EnsureComponent<CombatState>();
            StatusEffects = EnsureComponent<StatusEffectSystem>();
            Motion = EnsureComponent<CombatMotionController>();
            if (withResource)
            {
                Resource = EnsureComponent<CombatResource>();
            }
            else
            {
                Resource = GetComponent<CombatResource>();
            }
        }

        private T EnsureComponent<T>() where T : Component
        {
            T component = GetComponent<T>();
            return component == null ? gameObject.AddComponent<T>() : component;
        }
    }
}
```

Create `Assets/Game/Battle/Combat/CombatHit.cs`:

```csharp
using Game.Battle.Skill.Common;
using UnityEngine;

namespace Game.Battle.Combat
{
    public readonly struct CombatHit
    {
        public readonly Combatant Attacker;
        public readonly Combatant Target;
        public readonly SkillConfig SkillConfig;
        public readonly Vector3 HitPoint;
        public readonly Vector3 HitDirection;

        public CombatHit(Combatant attacker, Combatant target, SkillConfig skillConfig, Vector3 hitPoint, Vector3 hitDirection)
        {
            Attacker = attacker;
            Target = target;
            SkillConfig = skillConfig;
            HitPoint = hitPoint;
            HitDirection = hitDirection;
        }
    }
}
```

Create `Assets/Game/Battle/Combat/CombatResult.cs`:

```csharp
namespace Game.Battle.Combat
{
    public enum CombatResultType
    {
        None,
        Invincible,
        Block,
        Parry,
        Hit,
        Dead
    }

    public class CombatResult
    {
        public CombatResultType ResultType;
        public int HealthDamageApplied;
        public int StabilityDamageApplied;
        public int BattleSpiritGained;
        public bool IsInterrupted;
        public bool ShouldCancelCurrentSkill;
        public bool ShouldPlayHitReaction;
        public bool ShouldEnterUnbalanced;
        public bool ShouldDie;
    }
}
```

Create `Assets/Game/Battle/Combat/Interrupt/InterruptResolver.cs`:

```csharp
using Game.Battle.Skill.Common;

namespace Game.Battle.Combat.Interrupt
{
    public static class InterruptResolver
    {
        public static bool CanInterrupt(CombatHit hit, CombatResult result)
        {
            InterruptConfig config = hit.SkillConfig.interruptConfig;
            if (config == null || !config.canInterrupt)
            {
                return false;
            }

            if (hit.Target.State.IsDefending && !config.canInterruptDefence)
            {
                return false;
            }

            if (!hit.Target.State.CanBeInterrupted)
            {
                return false;
            }

            return config.interruptLevel >= hit.Target.State.InterruptResistLevel;
        }
    }
}
```

Create `Assets/Game/Battle/Combat/DamageResolver.cs`:

```csharp
using Game.Battle.Combat.Interrupt;
using Game.Battle.Skill;
using Game.Battle.Skill.Common;

namespace Game.Battle.Combat
{
    public static class DamageResolver
    {
        public static CombatResult Resolve(CombatHit hit)
        {
            CombatResult result = new CombatResult { ResultType = CombatResultType.None };
            if (hit.Target == null || hit.Target.Stats == null || hit.Target.Stats.IsDead)
            {
                result.ResultType = CombatResultType.Dead;
                result.ShouldDie = true;
                return result;
            }

            CombatHitConfig hitConfig = hit.SkillConfig.hitConfig;
            if (hit.Target.State.IsInvincible)
            {
                result.ResultType = CombatResultType.Invincible;
                return result;
            }

            if (hit.Target.State.IsParryWindowActive && hitConfig.canBeParried)
            {
                result.ResultType = CombatResultType.Parry;
                result.StabilityDamageApplied = hit.Attacker.Stats.ApplyStabilityDamage(hitConfig.stabilityDamage);
                return result;
            }

            if (hit.Target.State.IsDefending && hitConfig.canBeBlocked)
            {
                result.ResultType = CombatResultType.Block;
                result.StabilityDamageApplied = hit.Target.Stats.ApplyStabilityDamage(hitConfig.stabilityDamage);
                result.ShouldEnterUnbalanced = hit.Target.Stats.IsUnbalanced;
                return result;
            }

            result.ResultType = CombatResultType.Hit;
            result.HealthDamageApplied = hit.Target.Stats.ApplyHealthDamage(hitConfig.healthDamage);
            result.StabilityDamageApplied = hit.Target.Stats.ApplyStabilityDamage(hitConfig.stabilityDamage);
            result.ShouldDie = hit.Target.Stats.IsDead;
            result.ShouldEnterUnbalanced = hit.Target.Stats.IsUnbalanced;

            if (hit.SkillConfig.skillType == SkillType.NormalAttack && hit.Attacker.Resource != null)
            {
                hit.Attacker.Resource.AddBattleSpirit(hit.SkillConfig.battleSpiritGainOnHit);
                result.BattleSpiritGained = hit.SkillConfig.battleSpiritGainOnHit;
            }

            result.IsInterrupted = InterruptResolver.CanInterrupt(hit, result);
            result.ShouldCancelCurrentSkill = result.IsInterrupted;
            result.ShouldPlayHitReaction = result.IsInterrupted || (!hit.Target.State.IsDefending && hit.Target.State.CanBeInterrupted);
            return result;
        }
    }
}
```

- [ ] **Step 6: 创建 StatusEffect 和 Motion 空实现**

Create `Assets/Game/Battle/StatusEffect/StatusEffectSystem.cs`:

```csharp
using Game.Battle.Combat;
using UnityEngine;

namespace Game.Battle.StatusEffect
{
    public class StatusEffectSystem : MonoBehaviour
    {
        public void OnBeforeDealHit(ref CombatHit hit) { }
        public void OnBeforeReceiveHit(ref CombatHit hit) { }
        public void OnAfterDealHit(CombatResult result) { }
        public void OnAfterReceiveHit(CombatResult result) { }
    }
}
```

Create `Assets/Game/Battle/StatusEffect/StatusEffectConfig.cs`:

```csharp
using System;

namespace Game.Battle.StatusEffect
{
    [Serializable]
    public class StatusEffectConfig
    {
        public int statusEffectId;
        public string statusEffectName;
        public float duration;
        public int maxStack;
    }
}
```

Create `Assets/Game/Battle/StatusEffect/StatusEffectInstance.cs`:

```csharp
namespace Game.Battle.StatusEffect
{
    public class StatusEffectInstance
    {
        public StatusEffectConfig Config { get; }
        public float RemainingTime { get; private set; }
        public int Stack { get; private set; }

        public StatusEffectInstance(StatusEffectConfig config)
        {
            Config = config;
            RemainingTime = config.duration;
            Stack = 1;
        }
    }
}
```

Create `Assets/Game/Battle/Motion/CombatMotionController.cs`:

```csharp
using UnityEngine;

namespace Game.Battle.Motion
{
    public class CombatMotionController : MonoBehaviour
    {
        public void Knockback(Vector3 direction, float distance)
        {
            Vector3 flatDirection = new Vector3(direction.x, 0f, direction.z);
            if (flatDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            transform.position += flatDirection.normalized * Mathf.Max(0f, distance);
        }
    }
}
```

- [ ] **Step 7: 运行测试确认通过**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --group-name Tests.EditMode.Combat --timeout 120000
```

Expected: PASS，`CombatStatsTests` 和 `DamageResolverTests` 全部通过。

- [ ] **Step 8: 编译验证**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
```

Expected: `success:true`。

- [ ] **Step 9: Commit**

```powershell
git add Assets/Game/Battle/Combat Assets/Game/Battle/StatusEffect Assets/Game/Battle/Motion Assets/Game/Battle/Skill/SkillDefine.cs Assets/Game/Battle/Skill/Common/SkillConfig.cs Assets/Tests/EditMode/Combat/DamageResolverTests.cs
git commit -m "feat: 新增统一战斗结算与打断解析"
```

---

## Task 3: 实现技能配置默认值与 JSON 兼容

**Files:**
- Create: `Assets/Tests/EditMode/Skill/SkillConfigSerializationTests.cs`
- Modify: `Assets/Framework/Manager/ConfigManager.cs`
- Modify: `Assets/Data/WeaponConfig/SingleSwordSkillConfig.json`
- Modify: `Assets/Data/WeaponConfig/GreatSwordSkillConfig.json`
- Modify: `Assets/Data/EnemySkillConfig.json`

- [ ] **Step 1: 写失败测试**

Create `Assets/Tests/EditMode/Skill/SkillConfigSerializationTests.cs`:

```csharp
using Game.Battle.Skill;
using Game.Battle.Skill.Common;
using Newtonsoft.Json;
using NUnit.Framework;

namespace Tests.EditMode.Skill
{
    public class SkillConfigSerializationTests
    {
        [Test]
        public void MissingNewFields_AreNormalizedForNormalAttack()
        {
            SkillConfig config = JsonConvert.DeserializeObject<SkillConfig>(
                "{\"skillId\":10001,\"skillName\":\"Attack\",\"skillAnimationName\":\"Attack1\",\"comboNextSkillId\":10002}");

            SkillConfigDefaults.ApplyPlayerDefaults(config, SkillType.NormalAttack, isFinalCombo: false);

            Assert.AreEqual(SkillType.NormalAttack, config.skillType);
            Assert.AreEqual(10, config.hitConfig.healthDamage);
            Assert.AreEqual(10, config.hitConfig.stabilityDamage);
            Assert.AreEqual(8, config.battleSpiritGainOnHit);
            Assert.IsFalse(config.interruptConfig.canInterrupt);
        }

        [Test]
        public void FinalNormalAttack_DefaultsToInterrupt()
        {
            SkillConfig config = new SkillConfig { skillId = 10005, comboNextSkillId = 0 };

            SkillConfigDefaults.ApplyPlayerDefaults(config, SkillType.NormalAttack, isFinalCombo: true);

            Assert.IsTrue(config.interruptConfig.canInterrupt);
            Assert.AreEqual(1, config.interruptConfig.interruptLevel);
        }

        [Test]
        public void EnemyBossLikeFinalSkill_DefaultsToSuperArmor()
        {
            SkillConfig config = new SkillConfig { skillId = 20003, comboNextSkillId = 0 };

            SkillConfigDefaults.ApplyEnemyDefaults(config);

            Assert.IsFalse(config.interruptConfig.canBeInterrupted);
            Assert.AreEqual(99, config.interruptConfig.interruptResistLevel);
        }
    }
}
```

- [ ] **Step 2: 运行测试确认失败**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --group-name Tests.EditMode.Skill --timeout 120000
```

Expected: FAIL，错误包含 `SkillConfigDefaults` 不存在。

- [ ] **Step 3: 新增 SkillConfigDefaults 到 SkillConfig.cs**

Append this class inside namespace `Game.Battle.Skill.Common` in `Assets/Game/Battle/Skill/Common/SkillConfig.cs`:

```csharp
    public static class SkillConfigDefaults
    {
        public static void ApplyPlayerDefaults(SkillConfig config, SkillType skillType, bool isFinalCombo)
        {
            if (config == null)
            {
                return;
            }

            config.skillType = skillType;
            EnsureCommonDefaults(config);
            config.hitConfig.healthDamage = config.hitConfig.healthDamage <= 0 ? 10 : config.hitConfig.healthDamage;
            config.hitConfig.stabilityDamage = config.hitConfig.stabilityDamage <= 0 ? 10 : config.hitConfig.stabilityDamage;
            config.battleSpiritGainOnHit = skillType == SkillType.NormalAttack && config.battleSpiritGainOnHit <= 0 ? 8 : config.battleSpiritGainOnHit;
            config.interruptConfig.canInterrupt = config.interruptConfig.canInterrupt || isFinalCombo;
            config.interruptConfig.interruptLevel = config.interruptConfig.canInterrupt && config.interruptConfig.interruptLevel <= 0 ? 1 : config.interruptConfig.interruptLevel;
        }

        public static void ApplyEnemyDefaults(SkillConfig config)
        {
            if (config == null)
            {
                return;
            }

            config.skillType = SkillType.EnemySkill;
            EnsureCommonDefaults(config);
            config.hitConfig.healthDamage = config.hitConfig.healthDamage <= 0 ? 12 : config.hitConfig.healthDamage;
            config.hitConfig.stabilityDamage = config.hitConfig.stabilityDamage <= 0 ? 15 : config.hitConfig.stabilityDamage;
            if (config.comboNextSkillId == 0)
            {
                config.interruptConfig.canBeInterrupted = false;
                config.interruptConfig.interruptResistLevel = 99;
            }
        }

        private static void EnsureCommonDefaults(SkillConfig config)
        {
            if (config.hitConfig == null)
            {
                config.hitConfig = new CombatHitConfig();
            }

            if (config.interruptConfig == null)
            {
                config.interruptConfig = new InterruptConfig();
            }

            if (config.onCastEffects == null)
            {
                config.onCastEffects = new SkillEffectData[0];
            }

            if (config.onHitEffects == null)
            {
                config.onHitEffects = new SkillEffectData[0];
            }

            if (config.onBlockEffects == null)
            {
                config.onBlockEffects = new SkillEffectData[0];
            }

            if (config.onParryEffects == null)
            {
                config.onParryEffects = new SkillEffectData[0];
            }
        }
    }
```

- [ ] **Step 4: 修改 ConfigManager 加载后规范化**

Modify loops in `Assets/Framework/Manager/ConfigManager.cs`:

```csharp
foreach (SkillConfig config in enemyConfigs)
{
    SkillConfigDefaults.ApplyEnemyDefaults(config);
    m_SkillConfigs.Add(config.skillId, config);
}
```

and in `_LoadPlayerSkillConfigs()`:

```csharp
foreach (SkillConfig config in Configs)
{
    bool isFinalCombo = config.comboNextSkillId == 0;
    SkillConfigDefaults.ApplyPlayerDefaults(config, SkillType.NormalAttack, isFinalCombo);
    t.Add(config.skillId, config);
}
```

Add `using Game.Battle.Skill;` at the top of the file.

- [ ] **Step 5: 更新 JSON 显式标注关键技能**

Modify these entries:

`Assets/Data/WeaponConfig/SingleSwordSkillConfig.json` entry `skillId:10005` adds:

```json
"interruptConfig": {
  "canInterrupt": true,
  "interruptLevel": 1,
  "canBeInterrupted": true,
  "interruptResistLevel": 0,
  "canInterruptDefence": false
}
```

`Assets/Data/WeaponConfig/GreatSwordSkillConfig.json` entry `skillId:10003` adds the same `interruptConfig`.

`Assets/Data/EnemySkillConfig.json` entry `skillId:20003` adds:

```json
"interruptConfig": {
  "canInterrupt": true,
  "interruptLevel": 2,
  "canBeInterrupted": false,
  "interruptResistLevel": 99,
  "canInterruptDefence": false
}
```

Keep all existing `skillEffectConfig` and `skillAudioConfig` fields unchanged.

- [ ] **Step 6: 运行 Skill 配置测试**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --group-name Tests.EditMode.Skill --timeout 120000
```

Expected: PASS。

- [ ] **Step 7: 编译验证**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
```

Expected: `success:true`。

- [ ] **Step 8: Commit**

```powershell
git add Assets/Game/Battle/Skill/Common/SkillConfig.cs Assets/Framework/Manager/ConfigManager.cs Assets/Data/WeaponConfig/SingleSwordSkillConfig.json Assets/Data/WeaponConfig/GreatSwordSkillConfig.json Assets/Data/EnemySkillConfig.json Assets/Tests/EditMode/Skill/SkillConfigSerializationTests.cs
git commit -m "feat: 扩展技能配置默认值与打断参数"
```

---

## Task 4: 实现 SkillRunner 和基础效果执行器

**Files:**
- Create: `Assets/Game/Battle/Skill/SkillContext.cs`
- Create: `Assets/Game/Battle/Skill/SkillRunner.cs`
- Create: `Assets/Game/Battle/Skill/Effects/CombatEffectExecutor.cs`
- Modify: `Assets/Game/Character/Player/PlayerSkillManager.cs`
- Modify: `Assets/Game/Character/Enemy/EnemySkillManager.cs`

- [ ] **Step 1: 创建 SkillContext**

Create `Assets/Game/Battle/Skill/SkillContext.cs`:

```csharp
using Game.Battle.Combat;
using Game.Battle.Skill.Common;
using Game.Character.Equipment;
using UnityEngine;

namespace Game.Battle.Skill
{
    public class SkillContext
    {
        public int SkillId { get; }
        public SkillConfig Config { get; }
        public Combatant Caster { get; }
        public WeaponHandler WeaponHandler { get; }

        public SkillContext(int skillId, SkillConfig config, Combatant caster, WeaponHandler weaponHandler)
        {
            SkillId = skillId;
            Config = config;
            Caster = caster;
            WeaponHandler = weaponHandler;
        }
    }
}
```

- [ ] **Step 2: 创建 CombatEffectExecutor**

Create `Assets/Game/Battle/Skill/Effects/CombatEffectExecutor.cs`:

```csharp
using Framework.Utils;
using Game.Battle.Combat;
using Game.Battle.Skill.Common;
using UnityEngine;

namespace Game.Battle.Skill.Effects
{
    public static class CombatEffectExecutor
    {
        public static void ExecuteOnHitEffects(CombatHit hit, CombatResult result)
        {
            ExecuteEffects(hit, result, hit.SkillConfig.onHitEffects);
            SpawnFirstHitEffect(hit);
        }

        public static void ExecuteOnBlockEffects(CombatHit hit, CombatResult result)
        {
            ExecuteEffects(hit, result, hit.SkillConfig.onBlockEffects);
        }

        public static void ExecuteOnParryEffects(CombatHit hit, CombatResult result)
        {
            ExecuteEffects(hit, result, hit.SkillConfig.onParryEffects);
        }

        private static void ExecuteEffects(CombatHit hit, CombatResult result, SkillEffectData[] effects)
        {
            if (effects == null)
            {
                return;
            }

            for (int i = 0; i < effects.Length; i++)
            {
                SkillEffectData effect = effects[i];
                if (effect.effectType == SkillEffectType.Knockback && hit.Target.Motion != null)
                {
                    hit.Target.Motion.Knockback(hit.HitDirection, effect.value);
                }
            }
        }

        private static void SpawnFirstHitEffect(CombatHit hit)
        {
            SkillEffectConfig effectConfig = hit.SkillConfig.skillEffectConfig;
            if (effectConfig == null || effectConfig.hitEffectInfo == null || effectConfig.hitEffectInfo.Length == 0)
            {
                return;
            }

            GameObject fx = SkillUtils.InitSkillEffectObject(effectConfig.hitEffectInfo[0]);
            if (fx != null)
            {
                fx.transform.position = hit.HitPoint;
            }
        }
    }
}
```

- [ ] **Step 3: 创建 SkillRunner**

Create `Assets/Game/Battle/Skill/SkillRunner.cs`:

```csharp
using System.Collections.Generic;
using Game.Battle.Combat;
using Game.Battle.Skill.Common;
using Game.Battle.Skill.Effects;
using Game.Battle.Weapon;
using Game.Character.Equipment;
using GameMain2.Framework.Core;
using GameMain2.Framework.Manager;
using GameMain2.Game.EventArgs;
using UnityEngine;

namespace Game.Battle.Skill
{
    public class SkillRunner : MonoBehaviour
    {
        private readonly HashSet<int> m_availableSkillIds = new HashSet<int>();
        private Combatant m_caster;
        private WeaponHandler m_weaponHandler;
        private SkillContext m_currentContext;

        private void Awake()
        {
            m_caster = GetComponent<Combatant>();
            m_weaponHandler = GetComponent<WeaponHandler>();
        }

        public void LoadSkills(IEnumerable<int> skillIds)
        {
            m_availableSkillIds.Clear();
            if (skillIds == null)
            {
                return;
            }

            foreach (int skillId in skillIds)
            {
                m_availableSkillIds.Add(skillId);
            }
        }

        public bool Cast(int skillId, SkillConfig config)
        {
            if (config == null || m_caster == null)
            {
                return false;
            }

            if (!m_availableSkillIds.Contains(skillId) && config.skillType != SkillType.EnemySkill)
            {
                return false;
            }

            if (config.skillType == SkillType.WeaponSkill && m_caster.Resource != null && !m_caster.Resource.TryConsumeBattleSpirit(config.battleSpiritCost))
            {
                return false;
            }

            m_currentContext = new SkillContext(skillId, config, m_caster, m_weaponHandler);
            RegisterHitHandlers();
            return true;
        }

        public void CancelCurrentSkill()
        {
            UnregisterHitHandlers();
            m_currentContext = null;
        }

        private void RegisterHitHandlers()
        {
            EventCenter.Instance.Subscribe(WeaponHitEventArgs.EventId, OnPlayerWeaponHit);
            EventCenter.Instance.Subscribe(EnemyWeaponHitEventArgs.EventId, OnEnemyWeaponHit);
        }

        private void UnregisterHitHandlers()
        {
            EventCenter.TryUnSubscribe(WeaponHitEventArgs.EventId, OnPlayerWeaponHit);
            EventCenter.TryUnSubscribe(EnemyWeaponHitEventArgs.EventId, OnEnemyWeaponHit);
        }

        private void OnPlayerWeaponHit(object sender, EventArgsBase e)
        {
            if (!(sender is PlayerWeaponHitDetector) || m_currentContext == null || m_currentContext.Config.skillType == SkillType.EnemySkill)
            {
                return;
            }

            WeaponHitEventArgs args = (WeaponHitEventArgs)e;
            ResolveHit(args.Collider, args.HitPoint);
        }

        private void OnEnemyWeaponHit(object sender, EventArgsBase e)
        {
            if (!(sender is EnemyWeaponHitDetector) || m_currentContext == null || m_currentContext.Config.skillType != SkillType.EnemySkill)
            {
                return;
            }

            EnemyWeaponHitEventArgs args = (EnemyWeaponHitEventArgs)e;
            ResolveHit(args.Collider, args.HitPoint);
        }

        private void ResolveHit(Collider collider, Vector3 hitPoint)
        {
            if (collider == null)
            {
                return;
            }

            Combatant target = collider.GetComponentInParent<Combatant>();
            if (target == null || target == m_caster)
            {
                return;
            }

            Vector3 direction = (target.transform.position - m_caster.transform.position).normalized;
            CombatHit hit = new CombatHit(m_caster, target, m_currentContext.Config, hitPoint, direction);
            CombatResult result = DamageResolver.Resolve(hit);
            ApplyResultEffects(hit, result);
            CombatReaction.Apply(hit, result);
        }

        private static void ApplyResultEffects(CombatHit hit, CombatResult result)
        {
            if (result.ResultType == CombatResultType.Hit)
            {
                CombatEffectExecutor.ExecuteOnHitEffects(hit, result);
            }
            else if (result.ResultType == CombatResultType.Block)
            {
                CombatEffectExecutor.ExecuteOnBlockEffects(hit, result);
            }
            else if (result.ResultType == CombatResultType.Parry)
            {
                CombatEffectExecutor.ExecuteOnParryEffects(hit, result);
            }
        }
    }
}
```

- [ ] **Step 4: 给 Combatant 增加 SkillRunner 引用**

Modify `Assets/Game/Battle/Combat/Combatant.cs`:

```csharp
using Game.Battle.Skill;
```

Add property:

```csharp
public SkillRunner SkillRunner { get; private set; }
```

Add this line in `EnsureRuntimeComponents()` after `Motion = EnsureComponent<CombatMotionController>();`:

```csharp
SkillRunner = EnsureComponent<SkillRunner>();
```

- [ ] **Step 5: 创建 CombatReaction**

Create `Assets/Game/Battle/Combat/CombatReaction.cs`:

```csharp
using Game.Character.Enemy;
using GameMain2.Scripts.Character;

namespace Game.Battle.Combat
{
    public static class CombatReaction
    {
        public static void Apply(CombatHit hit, CombatResult result)
        {
            if (!result.ShouldPlayHitReaction && !result.ShouldEnterUnbalanced && !result.ShouldDie)
            {
                return;
            }

            PlayerStateMachine player = hit.Target.GetComponentInChildren<PlayerStateMachine>();
            if (player != null)
            {
                player.ChangeState<Game.Character.Player.PlayerFsm.GetHitState>();
                return;
            }

            EnemyStateMachine enemy = hit.Target.GetComponentInChildren<EnemyStateMachine>();
            if (enemy != null)
            {
                enemy.ChangeState<Game.Character.Enemy.EnemyFsm.Common.GetHitState>();
            }
        }
    }
}
```

- [ ] **Step 6: 修改 PlayerSkillManager**

Replace `PlayerSkillManager` dictionary creation logic with a `SkillRunner` bridge:

```csharp
using System.Collections.Generic;
using Game.Battle.Skill;
using Game.Character.Equipment;
using UnityEngine;

namespace GameMain2.Scripts.Character
{
    public class PlayerSkillManager : MonoBehaviour
    {
        private readonly List<int> m_currentSkillIds = new List<int>();
        private SkillRunner m_skillRunner;

        private void Awake()
        {
            m_skillRunner = GetComponent<SkillRunner>();
        }

        public void LoadSkillsForWeapon(WeaponData weaponData)
        {
            m_currentSkillIds.Clear();
            if (weaponData != null && weaponData.skillIds != null)
            {
                m_currentSkillIds.AddRange(weaponData.skillIds);
            }

            EnsureSkillRunner();
            m_skillRunner.LoadSkills(m_currentSkillIds);
        }

        public void ClearSkills()
        {
            m_currentSkillIds.Clear();
            EnsureSkillRunner();
            m_skillRunner.LoadSkills(m_currentSkillIds);
        }

        public bool HasSkill(int skillId)
        {
            return m_currentSkillIds.Contains(skillId);
        }

        public SkillBase GetSkill(int skillId)
        {
            return null;
        }

        public bool CastSkill(int skillId)
        {
            return false;
        }

        public void CancelSkill(int skillId)
        {
        }

        private void EnsureSkillRunner()
        {
            if (m_skillRunner == null)
            {
                m_skillRunner = GetComponent<SkillRunner>();
            }
        }
    }
}
```

- [ ] **Step 7: 修改 EnemySkillManager**

Replace hard-coded `SkillBase` dictionary with skill id availability:

```csharp
using System.Collections.Generic;
using Game.Battle.Skill;
using GameMain2.Framework.Manager;
using UnityEngine;

namespace GameMain2.Scripts.Character
{
    public class EnemySkillManager : MonoBehaviour
    {
        [SerializeField] private int[] skillIds = { 20001, 20002, 20003 };

        private SkillRunner m_skillRunner;

        private void Awake()
        {
            m_skillRunner = GetComponent<SkillRunner>();
            if (m_skillRunner != null)
            {
                m_skillRunner.LoadSkills(skillIds);
            }
        }

        public bool HasSkill(int skillId)
        {
            for (int i = 0; i < skillIds.Length; i++)
            {
                if (skillIds[i] == skillId)
                {
                    return true;
                }
            }
            return false;
        }

        public SkillBase GetSkill(int skillId)
        {
            return null;
        }

        public bool CastSkill(int skillId)
        {
            return false;
        }

        public void CancelSkill(int skillId)
        {
        }
    }
}
```

- [ ] **Step 8: 编译验证**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
```

Expected: `success:true`。`PlayerSkillManager` 和 `EnemySkillManager` 在本任务保留 `GetSkill()`、`CastSkill()`、`CancelSkill()` 空兼容方法，所以现有 `AttackState` 在 Task 5 改造前仍能编译。

- [ ] **Step 9: Commit**

```powershell
git add Assets/Game/Battle/Skill/SkillContext.cs Assets/Game/Battle/Skill/SkillRunner.cs Assets/Game/Battle/Skill/Effects Assets/Game/Battle/Combat/CombatReaction.cs Assets/Game/Character/Player/PlayerSkillManager.cs Assets/Game/Character/Enemy/EnemySkillManager.cs
git commit -m "feat: 新增 SkillRunner 并接管技能命中处理"
```

---

## Task 5: 改造玩家和敌人 AttackState 使用 SkillRunner

**Files:**
- Modify: `Assets/Game/Character/Player/PlayerFsm/AttackState.cs`
- Modify: `Assets/Game/Character/Enemy/EnemyFsm/Common/AttackState.cs`

- [ ] **Step 1: 修改玩家 AttackState**

In `Assets/Game/Character/Player/PlayerFsm/AttackState.cs`, replace `_skill` with `SkillRunner _skillRunner`, and replace Enter/Exit skill handling:

```csharp
private SkillRunner _skillRunner;
```

Inside `Enter` after `_skillConfig` is loaded:

```csharp
_skillRunner = fsm.Owner.PlayerController.GetComponent<SkillRunner>();
if (_skillRunner == null || !fsm.Owner.PlayerController.SkillManager.HasSkill(_skillConfig.skillId))
{
    fsm.ChangeState<IdleState>();
    return;
}

if (!_skillRunner.Cast(_skillConfig.skillId, _skillConfig))
{
    fsm.ChangeState<IdleState>();
    return;
}

if (fsm.Owner.PlayerController.TryGetComponent(out Combatant combatant))
{
    InterruptConfig interruptConfig = _skillConfig.interruptConfig;
    combatant.State.BeginAction(interruptConfig == null || interruptConfig.canBeInterrupted, interruptConfig == null ? 0 : interruptConfig.interruptResistLevel);
}
```

Inside `Exit`:

```csharp
fsm.Owner.isSkillCanSwitch = false;
if (_skillRunner != null)
{
    _skillRunner.CancelCurrentSkill();
}

if (fsm.Owner.PlayerController.TryGetComponent(out Combatant combatant))
{
    combatant.State.EndAction();
}

EventCenter.TryUnSubscribe(PlayerRootMotionEventArgs.EventId, OnAnimtorMove);
```

Add these usings:

```csharp
using Game.Battle.Combat;
using Game.Battle.Skill;
```

- [ ] **Step 2: 修改敌人 AttackState**

In `Assets/Game/Character/Enemy/EnemyFsm/Common/AttackState.cs`, replace `_skill` with:

```csharp
private SkillRunner _skillRunner;
```

Inside `Enter`, after `_skillConfig` is loaded:

```csharp
_skillRunner = fsm.Owner.GetComponent<SkillRunner>();
if (_skillRunner == null || !fsm.Owner.SkillManager.HasSkill(skillId))
{
    Debug.LogError($"[AttackState] 未找到技能运行器或技能未注册: {skillId}");
    fsm.ChangeState<ChaseState>();
    return;
}

if (!_skillRunner.Cast(skillId, _skillConfig))
{
    fsm.ChangeState<ChaseState>();
    return;
}

if (fsm.Owner.TryGetComponent(out Combatant combatant))
{
    InterruptConfig interruptConfig = _skillConfig.interruptConfig;
    combatant.State.BeginAction(interruptConfig == null || interruptConfig.canBeInterrupted, interruptConfig == null ? 0 : interruptConfig.interruptResistLevel);
}
```

Inside `Exit`:

```csharp
if (_skillRunner != null)
{
    _skillRunner.CancelCurrentSkill();
}

if (fsm.Owner.TryGetComponent(out Combatant combatant))
{
    combatant.State.EndAction();
}
```

Add these usings:

```csharp
using Game.Battle.Combat;
using Game.Battle.Skill;
```

- [ ] **Step 3: 编译验证**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
```

Expected: `success:true`。

- [ ] **Step 4: Commit**

```powershell
git add Assets/Game/Character/Player/PlayerFsm/AttackState.cs Assets/Game/Character/Enemy/EnemyFsm/Common/AttackState.cs
git commit -m "refactor: AttackState 改为通过 SkillRunner 释放技能"
```

---

## Task 6: 接入防御、格挡窗口和翻滚无敌

**Files:**
- Modify: `Assets/Game/Character/Player/PlayerFsm/DefenceState.cs`
- Modify: `Assets/Game/Character/Player/PlayerFsm/RollState.cs`

- [ ] **Step 1: 修改 DefenceState**

Add fields:

```csharp
private const float ParryWindowTime = 0.25f;
```

In `Enter`:

```csharp
fsm.Owner.CurState = PlayerState.Defence;
fsm.Owner.CrossFadeInFixedTime("EnterDefence");
m_SubState = DefenceSubState.EnterDefence;
Combatant combatant = fsm.Owner.PlayerController.GetComponent<Combatant>();
if (combatant != null)
{
    combatant.State.BeginDefence(ParryWindowTime);
}
```

In `Exit`:

```csharp
Combatant combatant = fsm.Owner.PlayerController.GetComponent<Combatant>();
if (combatant != null)
{
    combatant.State.EndDefence();
}
```

Add using:

```csharp
using Game.Battle.Combat;
```

- [ ] **Step 2: 修改 RollState**

Add field:

```csharp
private const float RollInvincibleTime = 0.45f;
```

In `Enter` after setting `CurState`:

```csharp
Combatant combatant = fsm.Owner.PlayerController.GetComponent<Combatant>();
if (combatant != null)
{
    combatant.State.SetInvincible(RollInvincibleTime);
}
```

In `Exit` before unsubscribing:

```csharp
Combatant combatant = fsm.Owner.PlayerController.GetComponent<Combatant>();
if (combatant != null)
{
    combatant.State.ClearInvincible();
}
```

Add using:

```csharp
using Game.Battle.Combat;
```

- [ ] **Step 3: 编译验证**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
```

Expected: `success:true`。

- [ ] **Step 4: Commit**

```powershell
git add Assets/Game/Character/Player/PlayerFsm/DefenceState.cs Assets/Game/Character/Player/PlayerFsm/RollState.cs
git commit -m "feat: 接入防御格挡窗口与翻滚无敌"
```

---

## Task 7: 更新 HUD 显示生命、稳定、战意

**Files:**
- Modify: `Assets/Game/UI/BattleHudPanel.cs`

- [ ] **Step 1: 修改 BattleHudPanel 保存 UI 引用并绑定玩家 Combatant**

Add fields:

```csharp
private Image m_healthFill;
private Image m_stabilityFill;
private Image m_battleSpiritFill;
private TextMeshProUGUI m_healthLabel;
private TextMeshProUGUI m_stabilityLabel;
private TextMeshProUGUI m_battleSpiritLabel;
private Combatant m_playerCombatant;
```

Replace health/stamina bar creation:

```csharp
m_healthFill = UIElementFactory.CreateBar("HealthBar", statusPanel, "生命 100 / 100", new Color(0.78f, 0.16f, 0.16f, 1f));
m_healthLabel = m_healthFill.transform.parent.Find("Label").GetComponent<TextMeshProUGUI>();
SetRect(m_healthFill.transform.parent.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(184f, -78f), new Vector2(320f, 28f));

m_stabilityFill = UIElementFactory.CreateBar("StabilityBar", statusPanel, "稳定 100 / 100", new Color(0.18f, 0.62f, 0.34f, 1f));
m_stabilityLabel = m_stabilityFill.transform.parent.Find("Label").GetComponent<TextMeshProUGUI>();
SetRect(m_stabilityFill.transform.parent.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(184f, -118f), new Vector2(320f, 28f));

m_battleSpiritFill = UIElementFactory.CreateBar("BattleSpiritBar", statusPanel, "战意 0 / 100", new Color(0.32f, 0.52f, 0.90f, 1f));
m_battleSpiritLabel = m_battleSpiritFill.transform.parent.Find("Label").GetComponent<TextMeshProUGUI>();
SetRect(m_battleSpiritFill.transform.parent.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(184f, -158f), new Vector2(320f, 28f));
```

Move weapon info to lower position:

```csharp
SetRect(weapon.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(24f, -190f), new Vector2(-48f, 30f));
```

Add methods:

```csharp
private void Update()
{
    if (m_playerCombatant == null)
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            m_playerCombatant = player.GetComponent<Combatant>();
            RefreshAllBars();
        }
    }
}

private void RefreshAllBars()
{
    if (m_playerCombatant == null)
    {
        return;
    }

    RefreshBar(m_healthFill, m_healthLabel, "生命", m_playerCombatant.Stats.CurrentHealth, m_playerCombatant.Stats.MaxHealth);
    RefreshBar(m_stabilityFill, m_stabilityLabel, "稳定", m_playerCombatant.Stats.CurrentStability, m_playerCombatant.Stats.MaxStability);
    if (m_playerCombatant.Resource != null)
    {
        RefreshBar(m_battleSpiritFill, m_battleSpiritLabel, "战意", m_playerCombatant.Resource.CurrentBattleSpirit, m_playerCombatant.Resource.MaxBattleSpirit);
    }
}

private static void RefreshBar(Image fill, TextMeshProUGUI label, string title, int current, int max)
{
    if (fill != null)
    {
        fill.fillAmount = max <= 0 ? 0f : Mathf.Clamp01((float)current / max);
    }

    if (label != null)
    {
        label.text = $"{title} {current} / {max}";
    }
}
```

Add using:

```csharp
using Game.Battle.Combat;
```

- [ ] **Step 2: 编译验证**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
```

Expected: `success:true`。

- [ ] **Step 3: Commit**

```powershell
git add Assets/Game/UI/BattleHudPanel.cs
git commit -m "feat: HUD 显示生命稳定与战意"
```

---

## Task 8: 端到端测试和基础验收

**Files:**
- No planned file changes. If validation fails, stop this task, diagnose the exact failing file, fix it in a separate small task, then restart Task 8 from Step 1.

- [ ] **Step 1: 跑全部 EditMode 测试**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --timeout 120000
```

Expected: PASS。

- [ ] **Step 2: Unity 编译门禁**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
```

Expected: `success:true`。这是唯一可接受的 Unity 编译验证；`compile dotnet` 不能替代它。

- [ ] **Step 3: 查 Unity 错误日志**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" get_logs --logType Error
```

Expected: 无新增战斗系统相关错误。

- [ ] **Step 4: Play Mode 手动验收**

进入主场景 Play Mode，逐项验证：

```text
玩家普攻敌人：敌人掉血掉稳定，玩家涨战意
玩家技能命中敌人：敌人掉血掉稳定，玩家不涨战意
玩家防御敌人攻击：玩家不掉血，但掉稳定
玩家格挡敌人攻击：玩家不掉血，不涨战意，敌人掉稳定
玩家翻滚中被打：不受伤
普攻最后一段命中正在攻击的敌人：敌人被打断进 GetHit
普攻最后一段命中防御敌人：不打断防御
敌人霸体技能期间被打：掉血、掉稳定，但不进 GetHit
HUD 显示生命、稳定、战意变化
```

- [ ] **Step 5: 最终状态检查**

Run:

```powershell
git status --short
```

Expected: 只出现本功能相关文件，且不包含用户原有无关改动。当前已知无关改动是 `.gitignore` 和 `Assets/Res/Textures/`，执行实现时不要回滚。

- [ ] **Step 6: 最终提交**

If validation passed and only feature files are changed:

```powershell
git add Assets/Game/Battle Assets/Game/Character/Player/PlayerFsm/AttackState.cs Assets/Game/Character/Player/PlayerFsm/DefenceState.cs Assets/Game/Character/Player/PlayerFsm/RollState.cs Assets/Game/Character/Enemy/EnemyFsm/Common/AttackState.cs Assets/Game/Character/Player/PlayerSkillManager.cs Assets/Game/Character/Enemy/EnemySkillManager.cs Assets/Game/UI/BattleHudPanel.cs Assets/Framework/Manager/ConfigManager.cs Assets/Data/WeaponConfig/SingleSwordSkillConfig.json Assets/Data/WeaponConfig/GreatSwordSkillConfig.json Assets/Data/EnemySkillConfig.json Assets/Tests/EditMode
git commit -m "feat: 重构基础战斗与技能结算框架"
```

---

## 完成检查

- [ ] `CombatStatsTests` 通过
- [ ] `DamageResolverTests` 通过
- [ ] `SkillConfigSerializationTests` 通过
- [ ] `$CLI compile unity` 成功
- [ ] `get_logs --logType Error` 无新增战斗系统错误
- [ ] 战意只通过普通攻击命中回复
- [ ] 格挡成功不回复战意
- [ ] 霸体期间仍掉血掉稳定，但不被普通打断
- [ ] 防御状态默认不被普攻最后一段打断
- [ ] HUD 显示生命、稳定、战意
