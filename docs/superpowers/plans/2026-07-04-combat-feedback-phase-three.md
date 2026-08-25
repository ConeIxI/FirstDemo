# Combat Feedback Phase Three Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 完成第三阶段战斗表现反馈补强的代码侧闭环，让命中、格挡、弹反、无敌、失衡、死亡在不接入音效、特效、敌人 HUD 的前提下有清晰可验证的反馈分流。

**Architecture:** 保持现有链路 `SkillRunner -> DamageResolver -> CombatReaction`。`DamageResolver` 只产出结算和反馈元数据，新增命中停顿控制器消费 `CombatResult`，`CombatReaction` 继续负责 FSM 状态反应并把 `hitReactionName` 传给受击状态。玩家 HUD 只增强已有生命、稳定值、战意条的变化反馈，不新增敌人血条、锁定条或伤害数字。

**Tech Stack:** Unity 2022.3.61f1c1, C# 9.0, NUnit EditMode tests, AIBridge CLI (`./.aibridge/cli/AIBridgeCLI.exe`).

---

## Scope Guard

本计划只实现以下内容：

- 命中停顿：读取 `hitConfig.hitStopTime`，按结果类型决定是否播放和最大时长。
- 结果区分补强：为 `Hit`、`Block`、`Parry`、`Invincible`、`Dead` 建立明确反馈类型。
- 受击表现选择：让 `hitConfig.hitReactionName` 能驱动普通受击状态播放的动画名。
- 失衡/死亡优先级：保持死亡、失衡和普通受击分离。
- 玩家 HUD 资源变化反馈：仅增强已有玩家生命、稳定值、战意条。

本计划明确不实现以下内容：

- 不接入音效。
- 不接入特效。
- 不新增敌人 HUD、敌人血条、锁定目标条或伤害数字。
- 不迁移 `skillEffectConfig`，不清理旧技能接口。

## File Structure

- Modify: `Assets/Game/Battle/Combat/Core/CombatResult.cs`
  - 增加 `CombatFeedbackKind` 和反馈元数据字段。
- Modify: `Assets/Game/Battle/Combat/Core/DamageResolver.cs`
  - 为每种结算结果写入反馈类型、停顿时间和受击动画名。
- Create: `Assets/Game/Battle/Combat/Feedback/CombatHitStopController.cs`
  - 统一执行短暂停顿，避免把 `Time.timeScale` 操作散落到 `SkillRunner`。
- Modify: `Assets/Game/Battle/Skill/SkillRunner.cs`
  - 在结算后触发命中停顿反馈。
- Modify: `Assets/Game/Character/CharacterStateMachine.cs`
  - 增加待播放受击动画名的设置和消费入口，玩家和敌人共用。
- Modify: `Assets/Game/Battle/Combat/CombatReaction.cs`
  - 普通受击前写入 `HitReactionName`；死亡和失衡不消费该字段。
- Modify: `Assets/Game/Character/Player/PlayerFsm/GetHitState.cs`
  - 消费待播放受击动画名。
- Modify: `Assets/Game/Character/Enemy/EnemyFsm/Common/GetHitState.cs`
  - 消费待播放受击动画名。
- Modify: `Assets/Game/UI/BattleHudPanel.cs`
  - 对已有玩家资源条增加短暂颜色反馈。
- Modify: `Assets/Tests/EditMode/Combat/DamageResolverTests.cs`
  - 覆盖反馈元数据和结果区分。
- Modify: `Assets/Tests/EditMode/Combat/CombatHitTests.cs`
  - 覆盖命中停顿控制器、状态机受击动画名缓存、死亡/失衡优先级。
- Modify: `Assets/Tests/EditMode/UI/BattleHudPanelTests.cs`
  - 覆盖 HUD 反馈工具方法和旧 prefab 兼容。
- Modify: `Assets/Data/WeaponConfig/SingleSwordSkillConfig.json`
  - 给单手剑已有技能配置轻/重命中停顿和受击动画名。
- Modify: `Assets/Data/WeaponConfig/GreatSwordSkillConfig.json`
  - 给巨剑至少一条更重的命中停顿配置。
- Modify: `Assets/Data/EnemySkillConfig.json`
  - 给敌人攻击配置格挡/弹反可区分的停顿输入。

---

### Task 1: CombatResult 反馈元数据和 DamageResolver 结果区分

**Files:**
- Modify: `Assets/Game/Battle/Combat/Core/CombatResult.cs`
- Modify: `Assets/Game/Battle/Combat/Core/DamageResolver.cs`
- Test: `Assets/Tests/EditMode/Combat/DamageResolverTests.cs`

- [ ] **Step 1: Write failing tests for result feedback metadata**

Add these tests to `Assets/Tests/EditMode/Combat/DamageResolverTests.cs` inside `DamageResolverTests`:

```csharp
[Test]
public void Resolve_NormalHit_CarriesFeedbackDataFromHitConfig()
{
    Combatant attacker = CreateCombatant("attacker", withResource: true);
    Combatant target = CreateCombatant("target", withResource: false);
    SkillConfig skill = CreateSkill(SkillType.NormalAttack, healthDamage: 20, stabilityDamage: 10, battleSpiritGain: 8);
    skill.hitConfig.hitStopTime = 0.05f;
    skill.hitConfig.hitReactionName = "LightHit";

    CombatResult result = DamageResolver.Resolve(new CombatHit(attacker, target, skill));

    Assert.AreEqual(CombatResultType.Hit, result.ResultType);
    Assert.AreEqual(CombatFeedbackKind.NormalHit, result.FeedbackKind);
    Assert.AreEqual(0.05f, result.HitStopTime, 0.0001f);
    Assert.AreEqual("LightHit", result.HitReactionName);
    DestroyCombatants(attacker, target);
}

[Test]
public void Resolve_WeaponSkillHit_UsesHeavyFeedbackAndClampsHitStop()
{
    Combatant attacker = CreateCombatant("attacker", withResource: true);
    Combatant target = CreateCombatant("target", withResource: false);
    SkillConfig skill = CreateSkill(SkillType.WeaponSkill, healthDamage: 35, stabilityDamage: 25, battleSpiritGain: 0);
    skill.hitConfig.hitStopTime = 0.3f;
    skill.hitConfig.hitReactionName = "HeavyHit";

    CombatResult result = DamageResolver.Resolve(new CombatHit(attacker, target, skill));

    Assert.AreEqual(CombatFeedbackKind.HeavyHit, result.FeedbackKind);
    Assert.AreEqual(0.12f, result.HitStopTime, 0.0001f);
    Assert.AreEqual("HeavyHit", result.HitReactionName);
    DestroyCombatants(attacker, target);
}

[Test]
public void Resolve_BlockParryInvincibleAndDead_CarryDistinctFeedbackKinds()
{
    Combatant attacker = CreateCombatant("attacker", withResource: true);
    Combatant blockingTarget = CreateCombatant("blocking-target", withResource: false);
    blockingTarget.State.BeginDefence(0f);
    SkillConfig blockSkill = CreateSkill(SkillType.NormalAttack, healthDamage: 30, stabilityDamage: 12, battleSpiritGain: 8);
    blockSkill.hitConfig.hitStopTime = 0.2f;

    CombatResult blockResult = DamageResolver.Resolve(new CombatHit(attacker, blockingTarget, blockSkill));

    Assert.AreEqual(CombatResultType.Block, blockResult.ResultType);
    Assert.AreEqual(CombatFeedbackKind.Block, blockResult.FeedbackKind);
    Assert.AreEqual(0.06f, blockResult.HitStopTime, 0.0001f);
    Assert.IsNull(blockResult.HitReactionName);
    DestroyCombatants(blockingTarget);

    Combatant parryTarget = CreateCombatant("parry-target", withResource: false);
    parryTarget.State.BeginDefence(0.2f);
    SkillConfig parrySkill = CreateSkill(SkillType.NormalAttack, healthDamage: 30, stabilityDamage: 12, battleSpiritGain: 8);
    parrySkill.hitConfig.hitStopTime = 0.2f;

    CombatResult parryResult = DamageResolver.Resolve(new CombatHit(attacker, parryTarget, parrySkill));

    Assert.AreEqual(CombatResultType.Parry, parryResult.ResultType);
    Assert.AreEqual(CombatFeedbackKind.Parry, parryResult.FeedbackKind);
    Assert.AreEqual(0.06f, parryResult.HitStopTime, 0.0001f);
    Assert.IsNull(parryResult.HitReactionName);
    DestroyCombatants(parryTarget);

    Combatant invincibleTarget = CreateCombatant("invincible-target", withResource: false);
    invincibleTarget.State.SetInvincible(0.5f);
    CombatResult invincibleResult = DamageResolver.Resolve(new CombatHit(attacker, invincibleTarget, blockSkill));

    Assert.AreEqual(CombatResultType.Invincible, invincibleResult.ResultType);
    Assert.AreEqual(CombatFeedbackKind.Invincible, invincibleResult.FeedbackKind);
    Assert.AreEqual(0f, invincibleResult.HitStopTime, 0.0001f);
    Assert.IsNull(invincibleResult.HitReactionName);
    DestroyCombatants(invincibleTarget);

    Combatant deadTarget = CreateCombatant("dead-target", withResource: false);
    deadTarget.Stats.ApplyHealthDamage(100);
    CombatResult deadResult = DamageResolver.Resolve(new CombatHit(attacker, deadTarget, blockSkill));

    Assert.AreEqual(CombatResultType.Dead, deadResult.ResultType);
    Assert.AreEqual(CombatFeedbackKind.None, deadResult.FeedbackKind);
    Assert.AreEqual(0f, deadResult.HitStopTime, 0.0001f);
    Assert.IsNull(deadResult.HitReactionName);
    DestroyCombatants(attacker, deadTarget);
}
```

- [ ] **Step 2: Run failing test**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --filter "Tests.EditMode.Combat.DamageResolverTests" --timeout 120000
```

Expected: FAIL because `CombatFeedbackKind`, `FeedbackKind`, `HitStopTime`, and `HitReactionName` do not exist yet.

- [ ] **Step 3: Add feedback fields to CombatResult**

In `Assets/Game/Battle/Combat/Core/CombatResult.cs`, add this enum after `CombatResultType`:

```csharp
public enum CombatFeedbackKind
{
    /// <summary>本次结算不播放额外反馈。</summary>
    None,
    /// <summary>普通命中反馈。</summary>
    NormalHit,
    /// <summary>强命中或武器技命中反馈。</summary>
    HeavyHit,
    /// <summary>格挡碰撞反馈。</summary>
    Block,
    /// <summary>弹反碰撞反馈。</summary>
    Parry,
    /// <summary>无敌闪避反馈。</summary>
    Invincible
}
```

Add these properties to `CombatResult`:

```csharp
/// <summary>本次结算对应的反馈类型，由表现层消费。</summary>
public CombatFeedbackKind FeedbackKind { get; set; }

/// <summary>本次命中建议播放的停顿时长，单位为秒。</summary>
public float HitStopTime { get; set; }

/// <summary>本次普通受击建议播放的动画名；格挡、弹反、无敌和死亡不使用该字段。</summary>
public string HitReactionName { get; set; }
```

- [ ] **Step 4: Write DamageResolver feedback mapping**

In `Assets/Game/Battle/Combat/Core/DamageResolver.cs`, add `using UnityEngine;`, then add constants and helpers inside `DamageResolver`:

```csharp
private const float MaxNormalHitStopTime = 0.08f;
private const float MaxHeavyHitStopTime = 0.12f;
private const float MaxCollisionHitStopTime = 0.06f;
private const int HeavyHitStabilityDamageThreshold = 20;
private const string DefaultHitReactionName = "GetHit";

/// <summary>把结算结果映射到轻量反馈数据，后续表现层只读取 CombatResult。</summary>
private static void ApplyFeedback(CombatResult result, CombatFeedbackKind feedbackKind, CombatHitConfig hitConfig, string hitReactionName)
{
    if (result == null)
    {
        return;
    }

    result.FeedbackKind = feedbackKind;
    result.HitStopTime = ResolveHitStopTime(hitConfig, feedbackKind);
    result.HitReactionName = hitReactionName;
}

/// <summary>根据反馈类型限制停顿上限，避免配置过大破坏动作流畅性。</summary>
private static float ResolveHitStopTime(CombatHitConfig hitConfig, CombatFeedbackKind feedbackKind)
{
    if (hitConfig == null || hitConfig.hitStopTime <= 0f)
    {
        return 0f;
    }

    float maxDuration;
    switch (feedbackKind)
    {
        case CombatFeedbackKind.HeavyHit:
            maxDuration = MaxHeavyHitStopTime;
            break;
        case CombatFeedbackKind.Block:
        case CombatFeedbackKind.Parry:
            maxDuration = MaxCollisionHitStopTime;
            break;
        case CombatFeedbackKind.NormalHit:
            maxDuration = MaxNormalHitStopTime;
            break;
        default:
            return 0f;
    }

    return Mathf.Clamp(hitConfig.hitStopTime, 0f, maxDuration);
}

/// <summary>普通命中根据技能类型、稳定值伤害和失衡结果区分轻重反馈。</summary>
private static CombatFeedbackKind ResolveNormalHitFeedbackKind(ICombatSkillConfig skillConfig, int stabilityDamage, bool shouldEnterUnbalanced)
{
    if (shouldEnterUnbalanced || stabilityDamage >= HeavyHitStabilityDamageThreshold)
    {
        return CombatFeedbackKind.HeavyHit;
    }

    if (skillConfig != null && skillConfig.SkillType == SkillType.WeaponSkill)
    {
        return CombatFeedbackKind.HeavyHit;
    }

    return CombatFeedbackKind.NormalHit;
}

/// <summary>读取配置的受击动画名，空配置回退到默认 GetHit。</summary>
private static string ResolveHitReactionName(CombatHitConfig hitConfig)
{
    if (hitConfig == null || string.IsNullOrWhiteSpace(hitConfig.hitReactionName))
    {
        return DefaultHitReactionName;
    }

    return hitConfig.hitReactionName;
}
```

Update these existing branches:

```csharp
if (target.State != null && target.State.IsInvincible)
{
    CombatResult result = CombatResult.Create(CombatResultType.Invincible);
    ApplyFeedback(result, CombatFeedbackKind.Invincible, hitConfig, null);
    return result;
}

if (target.State != null && target.State.IsParryWindowActive && CanBeParried(hitConfig))
{
    return ResolveParry(target, attacker, stabilityDamage, parryStabilityRestore, hitConfig);
}

if (target.State != null && target.State.IsDefending && CanBeBlocked(hitConfig))
{
    return ResolveBlock(target, stabilityDamage, hitConfig);
}
```

Change method signatures and bodies:

```csharp
/// <summary>结算弹反效果：目标恢复稳定值，攻击者承受稳定值伤害。</summary>
private static CombatResult ResolveParry(Combatant target, Combatant attacker, int stabilityDamage, int parryStabilityRestore, CombatHitConfig hitConfig)
{
    CombatResult result = CombatResult.Create(CombatResultType.Parry);
    if (target != null && target.Stats != null)
    {
        result.StabilityRestored = target.Stats.RestoreStability(parryStabilityRestore);
    }

    if (attacker != null && attacker.Stats != null)
    {
        result.StabilityDamageApplied = attacker.Stats.ApplyStabilityDamage(stabilityDamage);
        result.ShouldEnterAttackerUnbalanced = attacker.Stats.IsUnbalanced;
        // CombatResult 的状态反应由受击目标消费，不能把攻击者失衡写进目标反应标记。
    }

    ApplyFeedback(result, CombatFeedbackKind.Parry, hitConfig, null);
    return result;
}

/// <summary>结算格挡效果：目标不受生命伤害，但承受稳定值压力。</summary>
private static CombatResult ResolveBlock(Combatant target, int stabilityDamage, CombatHitConfig hitConfig)
{
    CombatResult result = CombatResult.Create(CombatResultType.Block);
    result.StabilityDamageApplied = target.Stats.ApplyStabilityDamage(stabilityDamage);
    result.ShouldEnterUnbalanced = target.Stats.IsUnbalanced;
    result.ShouldDie = target.Stats.IsDead;
    ApplyFeedback(result, CombatFeedbackKind.Block, hitConfig, null);
    return result;
}
```

At the end of `ResolveNormalHit`, before `return result;`, add:

```csharp
CombatFeedbackKind feedbackKind = ResolveNormalHitFeedbackKind(hit.SkillConfig, stabilityDamage, result.ShouldEnterUnbalanced);
string hitReactionName = result.ShouldPlayHitReaction ? ResolveHitReactionName(hitConfig) : null;
ApplyFeedback(result, feedbackKind, hitConfig, hitReactionName);
```

- [ ] **Step 5: Run test**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --filter "Tests.EditMode.Combat.DamageResolverTests" --timeout 120000
```

Expected: PASS for `DamageResolverTests`.

- [ ] **Step 6: Commit**

```powershell
git add Assets/Game/Battle/Combat/Core/CombatResult.cs Assets/Game/Battle/Combat/Core/DamageResolver.cs Assets/Tests/EditMode/Combat/DamageResolverTests.cs
git commit -m "添加战斗结果反馈元数据"
```

---

### Task 2: 命中停顿控制器和 SkillRunner 接入

**Files:**
- Create: `Assets/Game/Battle/Combat/Feedback/CombatHitStopController.cs`
- Modify: `Assets/Game/Battle/Skill/SkillRunner.cs`
- Test: `Assets/Tests/EditMode/Combat/CombatHitTests.cs`

- [ ] **Step 1: Write failing test for hit stop controller**

Add this test to `Assets/Tests/EditMode/Combat/CombatHitTests.cs`:

```csharp
[Test]
public void CombatHitStopController_OnlyStopsForPlayableFeedbackKinds()
{
    Type controllerType = GetAssemblyCSharpType("Game.Battle.Combat.Feedback.CombatHitStopController");
    MethodInfo shouldPlayMethod = controllerType.GetMethod("ShouldPlayHitStop", BindingFlags.Static | BindingFlags.Public);
    MethodInfo resolveMethod = controllerType.GetMethod("ResolveDuration", BindingFlags.Static | BindingFlags.Public);
    Assert.IsNotNull(shouldPlayMethod);
    Assert.IsNotNull(resolveMethod);

    CombatResult normalHit = CombatResult.Create(CombatResultType.Hit);
    normalHit.FeedbackKind = CombatFeedbackKind.NormalHit;
    normalHit.HitStopTime = 0.05f;

    Assert.IsTrue((bool)shouldPlayMethod.Invoke(null, new object[] { normalHit }));
    Assert.AreEqual(0.05f, (float)resolveMethod.Invoke(null, new object[] { normalHit }), 0.0001f);

    CombatResult block = CombatResult.Create(CombatResultType.Block);
    block.FeedbackKind = CombatFeedbackKind.Block;
    block.HitStopTime = 0.04f;

    Assert.IsTrue((bool)shouldPlayMethod.Invoke(null, new object[] { block }));
    Assert.AreEqual(0.04f, (float)resolveMethod.Invoke(null, new object[] { block }), 0.0001f);

    CombatResult invincible = CombatResult.Create(CombatResultType.Invincible);
    invincible.FeedbackKind = CombatFeedbackKind.Invincible;
    invincible.HitStopTime = 0.05f;

    Assert.IsFalse((bool)shouldPlayMethod.Invoke(null, new object[] { invincible }));
    Assert.AreEqual(0f, (float)resolveMethod.Invoke(null, new object[] { invincible }), 0.0001f);

    CombatResult dead = CombatResult.Create(CombatResultType.Dead);
    dead.FeedbackKind = CombatFeedbackKind.None;
    dead.HitStopTime = 0.05f;

    Assert.IsFalse((bool)shouldPlayMethod.Invoke(null, new object[] { dead }));
    Assert.AreEqual(0f, (float)resolveMethod.Invoke(null, new object[] { dead }), 0.0001f);
}
```

- [ ] **Step 2: Run failing test**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --filter "Tests.EditMode.Combat.CombatHitTests.CombatHitStopController_OnlyStopsForPlayableFeedbackKinds" --timeout 120000
```

Expected: FAIL because `Game.Battle.Combat.Feedback.CombatHitStopController` does not exist.

- [ ] **Step 3: Create hit stop controller**

Create `Assets/Game/Battle/Combat/Feedback/CombatHitStopController.cs`:

```csharp
using System.Collections;
using UnityEngine;

namespace Game.Battle.Combat.Feedback
{
    public sealed class CombatHitStopController : MonoBehaviour
    {
        private const string RuntimeObjectName = "[CombatHitStopController]";
        private static CombatHitStopController s_instance;

        private Coroutine m_runningRoutine;
        private float m_originalTimeScale = 1f;
        private float m_originalFixedDeltaTime = 0.02f;

        /// <summary>判断结算结果是否应该播放命中停顿。</summary>
        public static bool ShouldPlayHitStop(CombatResult result)
        {
            if (result == null || result.HitStopTime <= 0f)
            {
                return false;
            }

            return result.FeedbackKind == CombatFeedbackKind.NormalHit
                || result.FeedbackKind == CombatFeedbackKind.HeavyHit
                || result.FeedbackKind == CombatFeedbackKind.Block
                || result.FeedbackKind == CombatFeedbackKind.Parry;
        }

        /// <summary>解析可执行的停顿时长，无效结果返回 0。</summary>
        public static float ResolveDuration(CombatResult result)
        {
            return ShouldPlayHitStop(result) ? result.HitStopTime : 0f;
        }

        /// <summary>请求播放命中停顿；无效结果会被直接忽略。</summary>
        public static void Play(CombatResult result)
        {
            float duration = ResolveDuration(result);
            if (duration <= 0f)
            {
                return;
            }

            EnsureInstance().StartHitStop(duration);
        }

        /// <summary>创建或复用运行时控制器，集中管理全局时间缩放。</summary>
        private static CombatHitStopController EnsureInstance()
        {
            if (s_instance != null)
            {
                return s_instance;
            }

            GameObject go = new GameObject(RuntimeObjectName);
            DontDestroyOnLoad(go);
            s_instance = go.AddComponent<CombatHitStopController>();
            return s_instance;
        }

        /// <summary>启动新的停顿，若已有停顿正在执行则先恢复时间再重启。</summary>
        private void StartHitStop(float duration)
        {
            if (m_runningRoutine != null)
            {
                StopCoroutine(m_runningRoutine);
                RestoreTimeScale();
            }

            m_runningRoutine = StartCoroutine(PlayRoutine(duration));
        }

        /// <summary>使用真实时间等待，避免 Time.timeScale 为 0 后无法恢复。</summary>
        private IEnumerator PlayRoutine(float duration)
        {
            m_originalTimeScale = Time.timeScale <= 0f ? 1f : Time.timeScale;
            m_originalFixedDeltaTime = Time.fixedDeltaTime > 0f ? Time.fixedDeltaTime : 0.02f;

            Time.timeScale = 0f;
            Time.fixedDeltaTime = 0f;

            yield return new WaitForSecondsRealtime(duration);

            RestoreTimeScale();
            m_runningRoutine = null;
        }

        /// <summary>恢复停顿前的时间参数。</summary>
        private void RestoreTimeScale()
        {
            Time.timeScale = m_originalTimeScale <= 0f ? 1f : m_originalTimeScale;
            Time.fixedDeltaTime = m_originalFixedDeltaTime <= 0f ? 0.02f : m_originalFixedDeltaTime;
        }
    }
}
```

If Unity generates a `.meta`, keep it with the new file.

- [ ] **Step 4: Integrate SkillRunner**

In `Assets/Game/Battle/Skill/SkillRunner.cs`, add:

```csharp
using Game.Battle.Combat.Feedback;
```

In `ResolveHit`, change the block after `DamageResolver.Resolve(hit)` to:

```csharp
CombatResult result = DamageResolver.Resolve(hit);
CombatHitStopController.Play(result);
ExecuteEffects(hit, result, m_currentContext.Config);
CombatReaction.Apply(hit, result);
```

- [ ] **Step 5: Run test**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --filter "Tests.EditMode.Combat.CombatHitTests.CombatHitStopController_OnlyStopsForPlayableFeedbackKinds" --timeout 120000
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add -f Assets/Game/Battle/Combat/Feedback/CombatHitStopController.cs Assets/Game/Battle/Combat/Feedback/CombatHitStopController.cs.meta
git add Assets/Game/Battle/Skill/SkillRunner.cs Assets/Tests/EditMode/Combat/CombatHitTests.cs
git commit -m "接入战斗命中停顿反馈"
```

---

### Task 3: hitReactionName 驱动普通受击表现

**Files:**
- Modify: `Assets/Game/Character/CharacterStateMachine.cs`
- Modify: `Assets/Game/Battle/Combat/CombatReaction.cs`
- Modify: `Assets/Game/Character/Player/PlayerFsm/GetHitState.cs`
- Modify: `Assets/Game/Character/Enemy/EnemyFsm/Common/GetHitState.cs`
- Test: `Assets/Tests/EditMode/Combat/CombatHitTests.cs`

- [ ] **Step 1: Write failing tests for pending hit reaction animation**

Add these tests to `Assets/Tests/EditMode/Combat/CombatHitTests.cs`:

```csharp
[Test]
public void CharacterStateMachine_PendingHitReactionAnimation_ConsumesOnceAndFallsBackToGetHit()
{
    Type stateMachineType = GetAssemblyCSharpType("Game.Character.CharacterStateMachine");
    MethodInfo setMethod = stateMachineType.GetMethod("SetPendingHitReactionAnimation", BindingFlags.Instance | BindingFlags.Public);
    MethodInfo consumeMethod = stateMachineType.GetMethod("ConsumePendingHitReactionAnimation", BindingFlags.Instance | BindingFlags.Public);
    Assert.IsNotNull(setMethod);
    Assert.IsNotNull(consumeMethod);

    GameObject owner = new GameObject("player-state-machine");
    try
    {
        Component stateMachine = owner.AddComponent(GetAssemblyCSharpType("GameMain2.Scripts.Character.PlayerStateMachine"));

        Assert.AreEqual("GetHit", consumeMethod.Invoke(stateMachine, null));

        setMethod.Invoke(stateMachine, new object[] { "HeavyHit" });
        Assert.AreEqual("HeavyHit", consumeMethod.Invoke(stateMachine, null));
        Assert.AreEqual("GetHit", consumeMethod.Invoke(stateMachine, null));

        setMethod.Invoke(stateMachine, new object[] { "" });
        Assert.AreEqual("GetHit", consumeMethod.Invoke(stateMachine, null));
    }
    finally
    {
        UnityEngine.Object.DestroyImmediate(owner);
    }
}

[Test]
public void CombatReaction_ShouldApplyReaction_KeepsDeathAndUnbalanceBeforeNormalHitReaction()
{
    Type reactionType = GetAssemblyCSharpType("Game.Battle.Combat.CombatReaction");
    MethodInfo method = reactionType.GetMethod("ShouldApplyReaction", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    Assert.IsNotNull(method);

    CombatResult deadResult = CombatResult.Create(CombatResultType.Hit);
    deadResult.ShouldDie = true;
    deadResult.ShouldEnterUnbalanced = true;
    deadResult.ShouldPlayHitReaction = true;
    deadResult.HitReactionName = "HeavyHit";

    Assert.IsTrue((bool)method.Invoke(null, new object[] { deadResult }));

    CombatResult alreadyDeadResult = CombatResult.Create(CombatResultType.Dead);
    alreadyDeadResult.ShouldDie = true;

    Assert.IsFalse((bool)method.Invoke(null, new object[] { alreadyDeadResult }));

    CombatResult superArmorResult = CombatResult.Create(CombatResultType.Hit);
    superArmorResult.ShouldPlayHitReaction = false;
    superArmorResult.HitReactionName = "HeavyHit";

    Assert.IsFalse((bool)method.Invoke(null, new object[] { superArmorResult }));
}
```

- [ ] **Step 2: Run failing tests**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --filter "Tests.EditMode.Combat.CombatHitTests" --timeout 120000
```

Expected: FAIL for the new pending animation API.

- [ ] **Step 3: Add pending reaction API to CharacterStateMachine**

In `Assets/Game/Character/CharacterStateMachine.cs`, add fields near the existing private fields:

```csharp
private const string DefaultHitReactionAnimation = "GetHit";
private string m_pendingHitReactionAnimation = DefaultHitReactionAnimation;
```

Add methods:

```csharp
/// <summary>缓存下一次普通受击要播放的动画名，空值会回退到默认 GetHit。</summary>
public void SetPendingHitReactionAnimation(string animationName)
{
    m_pendingHitReactionAnimation = string.IsNullOrWhiteSpace(animationName)
        ? DefaultHitReactionAnimation
        : animationName;
}

/// <summary>消费下一次普通受击动画名，消费后恢复默认 GetHit。</summary>
public string ConsumePendingHitReactionAnimation()
{
    string animationName = string.IsNullOrWhiteSpace(m_pendingHitReactionAnimation)
        ? DefaultHitReactionAnimation
        : m_pendingHitReactionAnimation;
    m_pendingHitReactionAnimation = DefaultHitReactionAnimation;
    return animationName;
}
```

- [ ] **Step 4: Pass hit reaction name from CombatReaction**

In `Assets/Game/Battle/Combat/CombatReaction.cs`, before player normal hit state change:

```csharp
if (result.ShouldPlayHitReaction)
{
    player.SetPendingHitReactionAnimation(result.HitReactionName);
    player.ChangeState<PlayerGetHitState>();
}
```

Before enemy normal hit:

```csharp
if (result.ShouldPlayHitReaction)
{
    enemy.SetPendingHitReactionAnimation(result.HitReactionName);
    enemy.OnHit(hit.Attacker != null ? hit.Attacker.transform : null);
}
```

Do not set pending hit reaction for `ShouldDie`, `ShouldEnterUnbalanced`, or `ShouldEnterAttackerUnbalanced`.

- [ ] **Step 5: Consume animation name in player GetHitState**

Replace `Assets/Game/Character/Player/PlayerFsm/GetHitState.cs` with:

```csharp
using GameMain2.Framework.Core.FSM;
using GameMain2.Scripts.Character;

namespace Game.Character.Player.PlayerFsm
{
    public class GetHitState : PlayerStateBase
    {
        private string m_animationName = "GetHit";

        /// <summary>进入玩家受击状态，并播放本次战斗结算指定的受击动画。</summary>
        public override void Enter(FsmBase<PlayerStateMachine> fsm)
        {
            m_animationName = fsm.Owner.ConsumePendingHitReactionAnimation();
            fsm.Owner.CrossFadeInFixedTime(m_animationName);
        }

        /// <summary>受击动画播放结束后回到待机状态。</summary>
        public override void Update(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
            if (fsm.Owner.IsPlayingAnimation(m_animationName, out float time))
            {
                if (time >= 1f)
                {
                    fsm.ChangeState<IdleState>();
                }
            }
        }

        /// <summary>退出玩家受击状态时恢复默认动画名。</summary>
        public override void Exit(FsmBase<PlayerStateMachine> fsm)
        {
            m_animationName = "GetHit";
        }
    }
}
```

- [ ] **Step 6: Consume animation name in enemy GetHitState**

Replace `Assets/Game/Character/Enemy/EnemyFsm/Common/GetHitState.cs` with:

```csharp
using GameMain2.Framework.Core.FSM;

namespace Game.Character.Enemy.EnemyFsm.Common
{
    public class GetHitState : EnemyStateBase
    {
        private string m_animationName = "GetHit";

        /// <summary>进入敌人受击状态，停止移动并播放本次结算指定的受击动画。</summary>
        public override void Enter(FsmBase<EnemyStateMachine> fsm)
        {
            m_animationName = fsm.Owner.ConsumePendingHitReactionAnimation();
            fsm.Owner.CrossFadeInFixedTime(m_animationName);
            fsm.Owner.Movement.Stop();
        }

        /// <summary>受击动画播放结束后回到追击状态。</summary>
        public override void Update(FsmBase<EnemyStateMachine> fsm, float deltaTime)
        {
            if (fsm.Owner.IsPlayingAnimation(m_animationName, out float animProgress) && animProgress >= 1f)
            {
                fsm.ChangeState<ChaseState>();
            }
        }

        /// <summary>退出敌人受击状态时恢复默认动画名。</summary>
        public override void Exit(FsmBase<EnemyStateMachine> fsm)
        {
            m_animationName = "GetHit";
        }
    }
}
```

- [ ] **Step 7: Run tests**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --filter "Tests.EditMode.Combat.CombatHitTests" --timeout 120000
```

Expected: PASS for `CombatHitTests`.

- [ ] **Step 8: Commit**

```powershell
git add Assets/Game/Character/CharacterStateMachine.cs Assets/Game/Battle/Combat/CombatReaction.cs Assets/Game/Character/Player/PlayerFsm/GetHitState.cs Assets/Game/Character/Enemy/EnemyFsm/Common/GetHitState.cs Assets/Tests/EditMode/Combat/CombatHitTests.cs
git commit -m "让命中配置驱动受击动画"
```

---

### Task 4: 玩家 HUD 资源变化轻反馈

**Files:**
- Modify: `Assets/Game/UI/BattleHudPanel.cs`
- Test: `Assets/Tests/EditMode/UI/BattleHudPanelTests.cs`

- [ ] **Step 1: Write failing HUD feedback tests**

Add this test to `Assets/Tests/EditMode/UI/BattleHudPanelTests.cs`:

```csharp
[Test]
public void ResolveFeedbackColor_UsesFlashColorThenReturnsBaseColor()
{
    Type panelType = FindRequiredType("GameMain2.Scripts.UI.BattleHudPanel");
    MethodInfo method = panelType.GetMethod("ResolveFeedbackColor", BindingFlags.Static | BindingFlags.NonPublic);
    Assert.IsNotNull(method);

    Color baseColor = new Color(0.1f, 0.2f, 0.3f, 1f);
    Color flashColor = new Color(0.9f, 0.8f, 0.2f, 1f);

    Color active = (Color)method.Invoke(null, new object[] { baseColor, flashColor, 0.22f });
    Assert.AreEqual(flashColor.r, active.r, 0.0001f);
    Assert.AreEqual(flashColor.g, active.g, 0.0001f);
    Assert.AreEqual(flashColor.b, active.b, 0.0001f);

    Color expired = (Color)method.Invoke(null, new object[] { baseColor, flashColor, 0f });
    Assert.AreEqual(baseColor.r, expired.r, 0.0001f);
    Assert.AreEqual(baseColor.g, expired.g, 0.0001f);
    Assert.AreEqual(baseColor.b, expired.b, 0.0001f);
}
```

- [ ] **Step 2: Run failing HUD test**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --filter "Tests.EditMode.UI.BattleHudPanelTests" --timeout 120000
```

Expected: FAIL because `ResolveFeedbackColor` does not exist.

- [ ] **Step 3: Add player bar feedback fields**

In `Assets/Game/UI/BattleHudPanel.cs`, add these fields inside `BattleHudPanel`:

```csharp
private const float ResourceFlashDuration = 0.22f;

private static readonly Color HealthBaseColor = new Color(0.78f, 0.16f, 0.16f, 1f);
private static readonly Color StabilityBaseColor = new Color(0.18f, 0.62f, 0.34f, 1f);
private static readonly Color BattleSpiritBaseColor = new Color(0.84f, 0.52f, 0.18f, 1f);
private static readonly Color ResourceGainFlashColor = new Color(1f, 0.92f, 0.35f, 1f);
private static readonly Color ResourceLossFlashColor = new Color(1f, 0.35f, 0.28f, 1f);

private int m_lastHealth = -1;
private int m_lastStability = -1;
private int m_lastBattleSpirit = -1;
private float m_healthFlashTimer;
private float m_stabilityFlashTimer;
private float m_battleSpiritFlashTimer;
private Color m_healthFlashColor = HealthBaseColor;
private Color m_stabilityFlashColor = StabilityBaseColor;
private Color m_battleSpiritFlashColor = BattleSpiritBaseColor;
```

Change `EnsureCombatBars` to use the base color constants:

```csharp
m_healthFill = EnsureBar(statusPanel, "HealthBar", "生命 100 / 100", HealthBaseColor);
m_stabilityFill = EnsureBar(statusPanel, "StabilityBar", "稳定 100 / 100", StabilityBaseColor);
m_battleSpiritFill = EnsureBar(statusPanel, "BattleSpiritBar", "战意 0 / 100", BattleSpiritBaseColor);
```

- [ ] **Step 4: Replace RefreshAllBars bar updates**

In `RefreshAllBars`, replace direct `RefreshBar` calls for valid player stats with:

```csharp
CombatStats stats = m_playerCombatant.Stats;
RefreshTrackedBar(
    m_healthFill,
    m_healthLabel,
    "生命",
    stats.CurrentHealth,
    stats.MaxHealth,
    ref m_lastHealth,
    ref m_healthFlashTimer,
    ref m_healthFlashColor,
    HealthBaseColor);
RefreshTrackedBar(
    m_stabilityFill,
    m_stabilityLabel,
    "稳定",
    stats.CurrentStability,
    stats.MaxStability,
    ref m_lastStability,
    ref m_stabilityFlashTimer,
    ref m_stabilityFlashColor,
    StabilityBaseColor);

CombatResource resource = m_playerCombatant.Resource;
if (resource != null)
{
    RefreshTrackedBar(
        m_battleSpiritFill,
        m_battleSpiritLabel,
        "战意",
        resource.CurrentBattleSpirit,
        resource.MaxBattleSpirit,
        ref m_lastBattleSpirit,
        ref m_battleSpiritFlashTimer,
        ref m_battleSpiritFlashColor,
        BattleSpiritBaseColor);
}
else
{
    RefreshBar(m_battleSpiritFill, m_battleSpiritLabel, "战意", 0, 0);
}
```

Add methods:

```csharp
/// <summary>刷新带变化反馈的玩家资源条。</summary>
private static void RefreshTrackedBar(
    Image fill,
    TextMeshProUGUI label,
    string title,
    int current,
    int max,
    ref int lastCurrent,
    ref float flashTimer,
    ref Color flashColor,
    Color baseColor)
{
    if (lastCurrent >= 0 && current != lastCurrent)
    {
        flashTimer = ResourceFlashDuration;
        flashColor = current > lastCurrent ? ResourceGainFlashColor : ResourceLossFlashColor;
    }

    lastCurrent = current;
    RefreshBar(fill, label, title, current, max);

    if (fill != null)
    {
        fill.color = ResolveFeedbackColor(baseColor, flashColor, flashTimer);
    }

    flashTimer = Mathf.Max(0f, flashTimer - Time.unscaledDeltaTime);
}

/// <summary>根据反馈剩余时间计算资源条颜色，时间结束后恢复基础色。</summary>
private static Color ResolveFeedbackColor(Color baseColor, Color flashColor, float flashTimer)
{
    if (flashTimer <= 0f)
    {
        return baseColor;
    }

    float t = Mathf.Clamp01(flashTimer / ResourceFlashDuration);
    return Color.Lerp(baseColor, flashColor, t);
}
```

- [ ] **Step 5: Run HUD tests**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --filter "Tests.EditMode.UI.BattleHudPanelTests" --timeout 120000
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add Assets/Game/UI/BattleHudPanel.cs Assets/Tests/EditMode/UI/BattleHudPanelTests.cs
git commit -m "增强玩家战斗资源变化反馈"
```

---

### Task 5: 第三阶段配置和验收清单

**Files:**
- Modify: `Assets/Data/WeaponConfig/SingleSwordSkillConfig.json`
- Modify: `Assets/Data/WeaponConfig/GreatSwordSkillConfig.json`
- Modify: `Assets/Data/EnemySkillConfig.json`
- Modify: `docs/superpowers/plans/2026-07-04-combat-feedback-phase-three.md`

- [ ] **Step 1: Configure single sword feedback data**

In `Assets/Data/WeaponConfig/SingleSwordSkillConfig.json`, set existing single sword normal attack hit configs to light/medium/finisher timings:

```json
"hitStopTime": 0.05,
"hitReactionName": "GetHit"
```

```json
"hitStopTime": 0.07,
"hitReactionName": "GetHit"
```

```json
"hitStopTime": 0.1,
"hitReactionName": "GetHit"
```

Keep existing damage, stability, battle-spirit, block, parry, and interrupt values unless Task 1 tests show a direct conflict.

- [ ] **Step 2: Configure great sword feedback data**

In `Assets/Data/WeaponConfig/GreatSwordSkillConfig.json`, make at least one existing great sword normal attack or weapon skill heavier than single sword:

```json
"hitStopTime": 0.12,
"hitReactionName": "GetHit"
```

This creates a clear weight difference without adding new animation, sound, or effects.

- [ ] **Step 3: Configure enemy attack feedback input**

In `Assets/Data/EnemySkillConfig.json`, ensure `hitConfig.hitStopTime` exists on the `BlazeBandit` attack entries used in `Scene1`:

```json
"hitStopTime": 0.06,
"hitReactionName": "GetHit"
```

Enemy attacks still use the same result mapping: player block becomes `CombatFeedbackKind.Block`, player parry becomes `CombatFeedbackKind.Parry`, player roll invincibility becomes `CombatFeedbackKind.Invincible`.

- [ ] **Step 4: Add manual verification checklist to this plan**

Ensure the Scene1 manual checklist remains under the final verification section of this file.

- [ ] **Step 5: Run JSON serialization tests**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --filter "Tests.EditMode.Skill.SkillConfigSerializationTests" --timeout 120000
```

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add Assets/Data/WeaponConfig/SingleSwordSkillConfig.json Assets/Data/WeaponConfig/GreatSwordSkillConfig.json Assets/Data/EnemySkillConfig.json docs/superpowers/plans/2026-07-04-combat-feedback-phase-three.md
git commit -m "补充第三阶段战斗反馈配置"
```

---

### Task 6: Full Verification

**Files:**
- No code changes.

Scene1 手动验收清单：
1. 普通命中：敌人扣生命/稳定值，玩家涨战意，有短暂停顿。
2. 强命中或巨剑命中：停顿强于单手剑轻命中。
3. 格挡：玩家不掉生命，稳定值变化可见，停顿短且硬，不进入普通受击。
4. 弹反：攻击者稳定值受惩罚，停顿不同于格挡；若攻击者失衡，进入失衡而不是普通受击。
5. 翻滚无敌：玩家不掉血，不触发普通命中停顿和受击反应。
6. 霸体受击：目标扣血/扣稳定值，但不播放普通受击。
7. 死亡：死亡优先，不重复普通受击。
8. 玩家 HUD：生命、稳定值、战意变化时已有条目短暂变色。

- [ ] **Step 1: Run combat EditMode tests**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --filter "Tests.EditMode.Combat" --timeout 120000
```

Expected: PASS. The output should report all combat tests passing, including `DamageResolverTests` and `CombatHitTests`.

- [ ] **Step 2: Run UI EditMode tests**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --filter "Tests.EditMode.UI.BattleHudPanelTests" --timeout 120000
```

Expected: PASS.

- [ ] **Step 3: Run full EditMode tests**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode --timeout 120000
```

Expected: PASS for all discovered EditMode tests.

- [ ] **Step 4: Run Unity compile**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
```

Expected: compile succeeds. Unity compile must use this command; `compile dotnet` is not a substitute.

- [ ] **Step 5: Check Unity error logs**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" get_logs --logType Error
```

Expected: no new errors caused by the feedback changes.

- [ ] **Step 6: Review git status**

Run:

```powershell
git status --short
```

Expected: only intentional files from this plan are modified or added. Include generated `.meta` files for new Unity assets.

- [ ] **Step 7: Final commit**

If any verification-only fixes were needed after Task 5, commit them:

```powershell
git add Assets/Game/Battle/Combat/Core/CombatResult.cs Assets/Game/Battle/Combat/Core/DamageResolver.cs Assets/Game/Battle/Combat/Feedback/CombatHitStopController.cs Assets/Game/Battle/Skill/SkillRunner.cs Assets/Game/Character/CharacterStateMachine.cs Assets/Game/Battle/Combat/CombatReaction.cs Assets/Game/Character/Player/PlayerFsm/GetHitState.cs Assets/Game/Character/Enemy/EnemyFsm/Common/GetHitState.cs Assets/Game/UI/BattleHudPanel.cs Assets/Tests/EditMode/Combat/DamageResolverTests.cs Assets/Tests/EditMode/Combat/CombatHitTests.cs Assets/Tests/EditMode/UI/BattleHudPanelTests.cs Assets/Data/WeaponConfig/SingleSwordSkillConfig.json Assets/Data/WeaponConfig/GreatSwordSkillConfig.json Assets/Data/EnemySkillConfig.json
git commit -m "完成第三阶段战斗反馈补强"
```

If Task 1-5 commits already contain all changes and `git status --short` is clean, no final commit is needed.

---

## Self-Review

Spec coverage:

- 命中触感补强：Task 1 writes `HitStopTime`; Task 2 executes hit stop.
- 结果区分补强：Task 1 adds `CombatFeedbackKind` and maps `Hit`、`Block`、`Parry`、`Invincible`、`Dead`.
- 受击、失衡、死亡表现补强：Task 3 wires `hitReactionName` and keeps death/unbalance priority.
- 资源信息补强：Task 4 enhances existing player HUD bars only.
- 技能表现配置补强：Task 5 updates only `hitStopTime` and `hitReactionName`, with no audio/effect fields.
- 可验证反馈清单：Task 5 appends the Scene1 manual checklist; Task 6 runs automated verification.

Placeholder scan:

- No placeholder markers are present.
- No task depends on audio, visual effects, enemy HUD, enemy health bars, lock-on bars, or damage numbers.

Type consistency:

- `CombatFeedbackKind` lives in `Game.Battle.Combat`, matching `CombatResult`.
- `CombatHitStopController` lives in `Game.Battle.Combat.Feedback`, and `SkillRunner` imports that namespace.
- `SetPendingHitReactionAnimation` and `ConsumePendingHitReactionAnimation` live on `CharacterStateMachine`, so both `PlayerStateMachine` and `EnemyStateMachine` can use them.

Plan complete and saved to `docs/superpowers/plans/2026-07-04-combat-feedback-phase-three.md`. Two execution options:

1. Subagent-Driven (recommended) - dispatch a fresh subagent per task, review between tasks, fast iteration.
2. Inline Execution - execute tasks in this session using executing-plans, batch execution with checkpoints.
