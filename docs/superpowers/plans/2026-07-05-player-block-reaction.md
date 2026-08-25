# Player Block Reaction Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让防御中的玩家被敌人命中时，根据攻击力道播放不同格挡受击动画，并产生对应后退表现。

**2026-07-05 调整:** 格挡后退不再由代码位移实现；后退表现放进 `DefenceHit_Light/Medium/Heavy` 动画或 Root Motion。下方旧任务中涉及 `BlockPushBackDistance`、`BlockPushBackDuration`、`ResolvePushBack*` 的内容仅作历史记录，不再作为当前执行方案。

**Architecture:** 结算层只判断格挡反应等级并写入 `CombatResult`；表现层由 `CombatReaction` 把玩家切到新的 `PlayerBlockHitState`；状态层消费缓存的格挡反应数据，只播放 `DefenceHit_Light/Medium/Heavy`，后退幅度由动画资产承担。现有 `onBlockEffects` 继续负责格挡特效，不把特效、音效或动画播放塞进 `DamageResolver`。

**Tech Stack:** Unity 2022.3.61f1c1, C# 9.0, NUnit EditMode Tests, 项目 FSM、AIBridge `$CLI compile unity`。

---

## File Structure

- Modify: `Assets/Game/Battle/Combat/Core/CombatResult.cs`
  - 新增 `BlockReactionType` 枚举。
  - 新增格挡反应字段：动画名、是否播放格挡受击反应。

- Modify: `Assets/Game/Battle/Combat/Core/DamageResolver.cs`
  - 在 `ResolveBlock()` 中根据稳定值伤害和技能类型判定格挡力道。
  - 写入 `CombatResult` 的格挡反应数据。
  - 保持生命伤害、稳定值、格挡反馈原语义不变。

- Modify: `Assets/Game/Battle/Combat/CombatReaction.cs`
  - 玩家目标分支新增格挡反应分发。
  - 死亡和失衡仍优先于格挡受击。

- Modify: `Assets/Game/Character/CharacterStateMachine.cs`
  - 新增一次性缓存 `PendingBlockReaction`，供 `PlayerBlockHitState` 消费。
  - 缓存放在角色状态机基类，和现有普通受击动画缓存保持一致。

- Create: `Assets/Game/Character/Player/PlayerFsm/PlayerBlockHitState.cs`
  - 播放格挡受击动画。
  - 不执行代码位移，动画或 Root Motion 自身负责后退表现。
  - 动画或兜底时间结束后，如果还按防御键回到 `DefenceState`，否则回到 `IdleState`。

- Modify: `Assets/Game/Character/Player/PlayerStateMachine.cs`
  - 在 `_getPlayerStates()` 中注册 `PlayerBlockHitState`。

- Modify: `Assets/Game/Editor/CombatStabilityEditModeTests.cs`
  - 增加 `ResolveBlock` 对轻/中/重格挡反应等级的测试。
  - 增加破防时不播放普通格挡受击的测试。

- Create: `Assets/Game/Editor/PlayerBlockHitStateEditModeTests.cs`
  - 测试动画名解析和后退曲线计算这种纯逻辑。

---

### Task 1: Add CombatResult Block Reaction Data

**Files:**
- Modify: `Assets/Game/Battle/Combat/Core/CombatResult.cs`
- Test: `Assets/Game/Editor/CombatStabilityEditModeTests.cs`

- [ ] **Step 1: Write failing tests for block reaction fields**

Add the following tests inside `CombatStabilityEditModeTests` before the private `TestSkillConfig` class:

```csharp
/// <summary>验证轻力道格挡会产出轻格挡受击反应。</summary>
[Test]
public void Resolve_WhenBlockingLightAttack_ReturnsLightBlockReaction()
{
    GameObject attackerObject = new GameObject("Attacker");
    GameObject targetObject = new GameObject("Target");
    try
    {
        Combatant attacker = attackerObject.AddComponent<Combatant>();
        Combatant target = targetObject.AddComponent<Combatant>();
        attacker.EnsureRuntimeComponents(false);
        target.EnsureRuntimeComponents(false);
        target.Stats.Initialize(100, 30);
        target.State.BeginDefence(0f);

        TestSkillConfig config = new TestSkillConfig
        {
            SkillTypeValue = SkillType.EnemySkill,
            HitConfigValue = new CombatHitConfig
            {
                stabilityDamage = 5,
                canBeBlocked = true
            }
        };

        CombatResult result = DamageResolver.Resolve(new CombatHit(attacker, target, config));

        Assert.AreEqual(CombatResultType.Block, result.ResultType);
        Assert.IsTrue(result.ShouldPlayBlockReaction);
        Assert.AreEqual(BlockReactionType.Light, result.BlockReactionType);
        Assert.AreEqual("DefenceHit_Light", result.BlockReactionAnimation);
    }
    finally
    {
        Object.DestroyImmediate(attackerObject);
        Object.DestroyImmediate(targetObject);
    }
}

/// <summary>验证高稳定值伤害格挡会产出重格挡受击反应。</summary>
[Test]
public void Resolve_WhenBlockingHeavyAttack_ReturnsHeavyBlockReaction()
{
    GameObject attackerObject = new GameObject("Attacker");
    GameObject targetObject = new GameObject("Target");
    try
    {
        Combatant attacker = attackerObject.AddComponent<Combatant>();
        Combatant target = targetObject.AddComponent<Combatant>();
        attacker.EnsureRuntimeComponents(false);
        target.EnsureRuntimeComponents(false);
        target.Stats.Initialize(100, 80);
        target.State.BeginDefence(0f);

        TestSkillConfig config = new TestSkillConfig
        {
            SkillTypeValue = SkillType.EnemySkill,
            HitConfigValue = new CombatHitConfig
            {
                stabilityDamage = 30,
                canBeBlocked = true
            }
        };

        CombatResult result = DamageResolver.Resolve(new CombatHit(attacker, target, config));

        Assert.AreEqual(CombatResultType.Block, result.ResultType);
        Assert.IsTrue(result.ShouldPlayBlockReaction);
        Assert.AreEqual(BlockReactionType.Heavy, result.BlockReactionType);
        Assert.AreEqual("DefenceHit_Heavy", result.BlockReactionAnimation);
        Assert.Greater(result.BlockPushBackDistance, 0.5f);
    }
    finally
    {
        Object.DestroyImmediate(attackerObject);
        Object.DestroyImmediate(targetObject);
    }
}
```

Update the existing `TestSkillConfig` in `CombatStabilityEditModeTests` so tests can set skill type:

```csharp
private sealed class TestSkillConfig : ICombatSkillConfig
{
    public SkillType SkillTypeValue { get; set; } = SkillType.EnemySkill;
    public SkillType SkillType => SkillTypeValue;
    public int BattleSpiritGainOnHit => 0;
    public CombatHitConfig HitConfigValue { get; set; }
    public CombatHitConfig HitConfig => HitConfigValue;
    public InterruptConfig InterruptConfig => null;
}
```

- [ ] **Step 2: Run Unity compile to confirm new tests do not compile yet**

Run:

```bash
./.aibridge/cli/AIBridgeCLI.exe compile unity
```

Expected: compile fails because `BlockReactionType`, `ShouldPlayBlockReaction`, `BlockReactionAnimation`, and `BlockPushBackDistance` do not exist yet.

- [ ] **Step 3: Add block reaction fields**

Modify `Assets/Game/Battle/Combat/Core/CombatResult.cs`:

```csharp
namespace Game.Battle.Combat
{
    public enum CombatResultType
    {
        /// <summary>目标在本次结算前已经死亡。</summary>
        Dead,
        /// <summary>目标处于无敌状态，本次命中不生效。</summary>
        Invincible,
        /// <summary>目标成功弹反本次命中。</summary>
        Parry,
        /// <summary>目标成功格挡本次命中。</summary>
        Block,
        /// <summary>本次命中按普通受击结算。</summary>
        Hit
    }

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

    public enum BlockReactionType
    {
        /// <summary>不播放格挡受击反应。</summary>
        None,
        /// <summary>轻格挡受击，适合小硬直和轻微后退。</summary>
        Light,
        /// <summary>中格挡受击，适合常规攻击命中防御。</summary>
        Medium,
        /// <summary>重格挡受击，适合重攻击、武器技或高稳定值伤害。</summary>
        Heavy
    }

    public sealed class CombatResult
    {
        /// <summary>本次战斗结算的结果类型。</summary>
        public CombatResultType ResultType { get; set; }

        /// <summary>本次结算对应的反馈类型，由表现层消费。</summary>
        public CombatFeedbackKind FeedbackKind { get; set; }

        /// <summary>本次命中建议播放的停顿时长，单位为秒。</summary>
        public float HitStopTime { get; set; }

        /// <summary>本次普通受击建议播放的动画名；格挡、弹反、无敌和死亡不使用该字段。</summary>
        public string HitReactionName { get; set; }

        /// <summary>格挡成功后是否播放防御受击反应。</summary>
        public bool ShouldPlayBlockReaction { get; set; }

        /// <summary>格挡受击力道等级，由玩家表现层选择动画和位移强度。</summary>
        public BlockReactionType BlockReactionType { get; set; }

        /// <summary>格挡受击动画名，空值表示由表现层回退到默认轻格挡动画。</summary>
        public string BlockReactionAnimation { get; set; }

        /// <summary>格挡受击时沿命中方向后退的距离。</summary>
        public float BlockPushBackDistance { get; set; }

        /// <summary>格挡受击后退位移持续时间。</summary>
        public float BlockPushBackDuration { get; set; }

        /// <summary>本次实际扣除的生命值。</summary>
        public int HealthDamageApplied { get; set; }

        /// <summary>本次实际扣除的稳定值。</summary>
        public int StabilityDamageApplied { get; set; }

        /// <summary>本次实际恢复的稳定值。</summary>
        public int StabilityRestored { get; set; }

        /// <summary>本次命中实际获得的战意值。</summary>
        public int BattleSpiritGained { get; set; }

        /// <summary>本次命中是否打断了目标当前动作。</summary>
        public bool IsInterrupted { get; set; }

        /// <summary>是否需要取消目标当前正在释放的技能。</summary>
        public bool ShouldCancelCurrentSkill { get; set; }

        /// <summary>是否需要播放普通受击反应。</summary>
        public bool ShouldPlayHitReaction { get; set; }

        /// <summary>是否需要进入失衡状态。</summary>
        public bool ShouldEnterUnbalanced { get; set; }

        /// <summary>是否需要让本次攻击发起者进入失衡状态，主要用于弹反反制。</summary>
        public bool ShouldEnterAttackerUnbalanced { get; set; }

        /// <summary>是否需要进入死亡状态。</summary>
        public bool ShouldDie { get; set; }

        /// <summary>创建指定结果类型的战斗结算结果。</summary>
        public static CombatResult Create(CombatResultType resultType)
        {
            return new CombatResult
            {
                ResultType = resultType
            };
        }
    }
}
```

- [ ] **Step 4: Run Unity compile**

Run:

```bash
./.aibridge/cli/AIBridgeCLI.exe compile unity
```

Expected: compile still fails because `DamageResolver.ResolveBlock()` has not populated the new assertions yet, or tests fail if compile includes tests.

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Battle/Combat/Core/CombatResult.cs Assets/Game/Editor/CombatStabilityEditModeTests.cs
git commit -m "test: cover block reaction result data"
```

---

### Task 2: Resolve Block Reaction Strength

**Files:**
- Modify: `Assets/Game/Battle/Combat/Core/DamageResolver.cs`
- Test: `Assets/Game/Editor/CombatStabilityEditModeTests.cs`

- [ ] **Step 1: Add guard-break test**

Add this test inside `CombatStabilityEditModeTests`:

```csharp
/// <summary>验证格挡被打空稳定值时进入失衡，不播放普通格挡受击反应。</summary>
[Test]
public void Resolve_WhenBlockBreaksStability_DoesNotPlayBlockReaction()
{
    GameObject attackerObject = new GameObject("Attacker");
    GameObject targetObject = new GameObject("Target");
    try
    {
        Combatant attacker = attackerObject.AddComponent<Combatant>();
        Combatant target = targetObject.AddComponent<Combatant>();
        attacker.EnsureRuntimeComponents(false);
        target.EnsureRuntimeComponents(false);
        target.Stats.Initialize(100, 10);
        target.State.BeginDefence(0f);

        TestSkillConfig config = new TestSkillConfig
        {
            SkillTypeValue = SkillType.EnemySkill,
            HitConfigValue = new CombatHitConfig
            {
                stabilityDamage = 10,
                canBeBlocked = true
            }
        };

        CombatResult result = DamageResolver.Resolve(new CombatHit(attacker, target, config));

        Assert.AreEqual(CombatResultType.Block, result.ResultType);
        Assert.IsTrue(result.ShouldEnterUnbalanced);
        Assert.IsFalse(result.ShouldPlayBlockReaction);
        Assert.AreEqual(BlockReactionType.None, result.BlockReactionType);
    }
    finally
    {
        Object.DestroyImmediate(attackerObject);
        Object.DestroyImmediate(targetObject);
    }
}
```

- [ ] **Step 2: Implement block reaction strength**

Modify `Assets/Game/Battle/Combat/Core/DamageResolver.cs` by adding constants near the existing constants:

```csharp
private const int MediumBlockStabilityDamageThreshold = 10;
private const int HeavyBlockStabilityDamageThreshold = 25;
private const float LightBlockPushBackDistance = 0.18f;
private const float MediumBlockPushBackDistance = 0.35f;
private const float HeavyBlockPushBackDistance = 0.7f;
private const float LightBlockPushBackDuration = 0.12f;
private const float MediumBlockPushBackDuration = 0.16f;
private const float HeavyBlockPushBackDuration = 0.22f;
private const string LightBlockReactionName = "DefenceHit_Light";
private const string MediumBlockReactionName = "DefenceHit_Medium";
private const string HeavyBlockReactionName = "DefenceHit_Heavy";
```

Replace `ResolveBlock()` with:

```csharp
/// <summary>结算格挡效果：目标不受生命伤害，但承受稳定值压力。</summary>
private static CombatResult ResolveBlock(Combatant target, int stabilityDamage, CombatHitConfig hitConfig)
{
    CombatResult result = CombatResult.Create(CombatResultType.Block);
    result.StabilityDamageApplied = target.Stats.ApplyStabilityDamage(stabilityDamage);
    result.ShouldEnterUnbalanced = target.Stats.IsUnbalanced;
    result.ShouldDie = target.Stats.IsDead;
    ApplyBlockReaction(result, hitConfig, stabilityDamage);
    ApplyFeedback(result, CombatFeedbackKind.Block, hitConfig, null);
    return result;
}
```

Add these helper methods in `DamageResolver`:

```csharp
/// <summary>根据格挡承受的稳定值伤害写入防御受击表现数据。</summary>
private static void ApplyBlockReaction(CombatResult result, CombatHitConfig hitConfig, int stabilityDamage)
{
    if (result == null || result.ShouldEnterUnbalanced || result.ShouldDie)
    {
        return;
    }

    BlockReactionType reactionType = ResolveBlockReactionType(hitConfig, stabilityDamage);
    result.BlockReactionType = reactionType;
    result.ShouldPlayBlockReaction = reactionType != BlockReactionType.None;
    result.BlockReactionAnimation = ResolveBlockReactionAnimation(reactionType);
    result.BlockPushBackDistance = ResolveBlockPushBackDistance(reactionType);
    result.BlockPushBackDuration = ResolveBlockPushBackDuration(reactionType);
}

/// <summary>把稳定值伤害映射为轻、中、重三档格挡受击。</summary>
private static BlockReactionType ResolveBlockReactionType(CombatHitConfig hitConfig, int stabilityDamage)
{
    if (stabilityDamage >= HeavyBlockStabilityDamageThreshold)
    {
        return BlockReactionType.Heavy;
    }

    if (stabilityDamage >= MediumBlockStabilityDamageThreshold)
    {
        return BlockReactionType.Medium;
    }

    return BlockReactionType.Light;
}

/// <summary>根据格挡受击档位返回动画名。</summary>
private static string ResolveBlockReactionAnimation(BlockReactionType reactionType)
{
    switch (reactionType)
    {
        case BlockReactionType.Heavy:
            return HeavyBlockReactionName;
        case BlockReactionType.Medium:
            return MediumBlockReactionName;
        case BlockReactionType.Light:
            return LightBlockReactionName;
        default:
            return null;
    }
}

/// <summary>根据格挡受击档位返回后退距离。</summary>
private static float ResolveBlockPushBackDistance(BlockReactionType reactionType)
{
    switch (reactionType)
    {
        case BlockReactionType.Heavy:
            return HeavyBlockPushBackDistance;
        case BlockReactionType.Medium:
            return MediumBlockPushBackDistance;
        case BlockReactionType.Light:
            return LightBlockPushBackDistance;
        default:
            return 0f;
    }
}

/// <summary>根据格挡受击档位返回后退持续时间。</summary>
private static float ResolveBlockPushBackDuration(BlockReactionType reactionType)
{
    switch (reactionType)
    {
        case BlockReactionType.Heavy:
            return HeavyBlockPushBackDuration;
        case BlockReactionType.Medium:
            return MediumBlockPushBackDuration;
        case BlockReactionType.Light:
            return LightBlockPushBackDuration;
        default:
            return 0f;
    }
}
```

- [ ] **Step 3: Run Unity compile**

Run:

```bash
./.aibridge/cli/AIBridgeCLI.exe compile unity
```

Expected: compile succeeds for production code. If Unity also runs EditMode tests, the three new block reaction tests pass.

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Battle/Combat/Core/DamageResolver.cs Assets/Game/Editor/CombatStabilityEditModeTests.cs
git commit -m "feat: resolve block reaction strength"
```

---

### Task 3: Cache Block Reaction on CharacterStateMachine

**Files:**
- Modify: `Assets/Game/Character/CharacterStateMachine.cs`

- [ ] **Step 1: Add block reaction cache struct and methods**

Modify `Assets/Game/Character/CharacterStateMachine.cs`. Add this nested struct inside `CharacterStateMachine`:

```csharp
public struct PendingBlockReaction
{
    public string AnimationName;
    public Vector3 HitDirection;
    public float PushBackDistance;
    public float PushBackDuration;
}
```

Add this field near `m_pendingHitReactionAnimation`:

```csharp
private PendingBlockReaction m_pendingBlockReaction;
```

Add these methods near the existing pending hit reaction methods:

```csharp
/// <summary>缓存下一次格挡受击表现数据，供 PlayerBlockHitState 消费。</summary>
public void SetPendingBlockReaction(string animationName, Vector3 hitDirection, float pushBackDistance, float pushBackDuration)
{
    m_pendingBlockReaction = new PendingBlockReaction
    {
        AnimationName = ResolveBlockReactionAnimationName(animationName),
        HitDirection = hitDirection,
        PushBackDistance = Mathf.Max(0f, pushBackDistance),
        PushBackDuration = Mathf.Max(0f, pushBackDuration)
    };
}

/// <summary>消费下一次格挡受击表现数据，消费后恢复默认轻格挡反应。</summary>
public PendingBlockReaction ConsumePendingBlockReaction()
{
    PendingBlockReaction reaction = m_pendingBlockReaction;
    if (string.IsNullOrWhiteSpace(reaction.AnimationName))
    {
        reaction.AnimationName = ResolveBlockReactionAnimationName(null);
    }

    m_pendingBlockReaction = default;
    return reaction;
}

/// <summary>解析格挡受击动画名，空值统一回退到轻格挡动画。</summary>
public string ResolveBlockReactionAnimationName(string animationName)
{
    return string.IsNullOrWhiteSpace(animationName)
        ? "DefenceHit_Light"
        : animationName;
}
```

- [ ] **Step 2: Run Unity compile**

Run:

```bash
./.aibridge/cli/AIBridgeCLI.exe compile unity
```

Expected: compile succeeds.

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Character/CharacterStateMachine.cs
git commit -m "feat: cache pending block reaction"
```

---

### Task 4: Route Block Reaction Through CombatReaction

**Files:**
- Modify: `Assets/Game/Battle/Combat/CombatReaction.cs`

- [ ] **Step 1: Update reaction routing**

In `CombatReaction.Apply()`, inside the player target branch after unbalance handling and before normal hit handling, add:

```csharp
if (result.ShouldPlayBlockReaction)
{
    player.SetPendingBlockReaction(
        result.BlockReactionAnimation,
        hit.HitDirection,
        result.BlockPushBackDistance,
        result.BlockPushBackDuration);
    player.ChangeState<PlayerBlockHitState>();
    return;
}
```

Update using aliases at the top:

```csharp
using PlayerBlockHitState = Game.Character.Player.PlayerFsm.PlayerBlockHitState;
```

Update `ShouldApplyReaction()`:

```csharp
return result.ShouldDie
    || result.ShouldPlayHitReaction
    || result.ShouldPlayBlockReaction
    || result.ShouldEnterUnbalanced
    || result.ShouldEnterAttackerUnbalanced;
```

- [ ] **Step 2: Run Unity compile to confirm missing state**

Run:

```bash
./.aibridge/cli/AIBridgeCLI.exe compile unity
```

Expected: compile fails because `PlayerBlockHitState` does not exist yet. This confirms the routing code references the intended state.

- [ ] **Step 3: Commit after Task 5 instead of now**

Do not commit this task alone if compile fails. Commit together with Task 5 when the new state exists.

---

### Task 5: Add PlayerBlockHitState

**Files:**
- Create: `Assets/Game/Character/Player/PlayerFsm/PlayerBlockHitState.cs`
- Modify: `Assets/Game/Character/Player/PlayerStateMachine.cs`
- Test: `Assets/Game/Editor/PlayerBlockHitStateEditModeTests.cs`

- [ ] **Step 1: Add pure logic tests**

Create `Assets/Game/Editor/PlayerBlockHitStateEditModeTests.cs`:

```csharp
using System.Reflection;
using Game.Character.Player.PlayerFsm;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class PlayerBlockHitStateEditModeTests
    {
        /// <summary>验证格挡受击后退方向会忽略纵向分量，避免击退导致角色上下抖动。</summary>
        [Test]
        public void ResolvePushBackDirection_WhenHitDirectionHasVerticalComponent_ReturnsHorizontalDirection()
        {
            MethodInfo resolver = typeof(PlayerBlockHitState).GetMethod("ResolvePushBackDirection", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(resolver, "PlayerBlockHitState 需要提供可测试的后退方向解析。");

            Vector3 direction = (Vector3)resolver.Invoke(null, new object[] { new Vector3(0f, 3f, 1f), Vector3.forward });

            Assert.AreEqual(0f, direction.y);
            Assert.That(direction.z, Is.LessThan(0f));
        }

        /// <summary>验证无命中方向时，格挡受击会沿角色背向后退。</summary>
        [Test]
        public void ResolvePushBackDirection_WhenHitDirectionIsZero_UsesOwnerBackDirection()
        {
            MethodInfo resolver = typeof(PlayerBlockHitState).GetMethod("ResolvePushBackDirection", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(resolver, "PlayerBlockHitState 需要提供可测试的后退方向解析。");

            Vector3 direction = (Vector3)resolver.Invoke(null, new object[] { Vector3.zero, Vector3.forward });

            Assert.AreEqual(Vector3.back, direction);
        }

        /// <summary>验证后退曲线按持续时间归一化，并且不会超过总距离。</summary>
        [Test]
        public void ResolvePushBackStep_WhenElapsedWithinDuration_ReturnsPartialStep()
        {
            MethodInfo resolver = typeof(PlayerBlockHitState).GetMethod("ResolvePushBackStep", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(resolver, "PlayerBlockHitState 需要提供可测试的后退步长解析。");

            float step = (float)resolver.Invoke(null, new object[] { 0.4f, 0.2f, 0.1f, 0f });

            Assert.Greater(step, 0f);
            Assert.LessOrEqual(step, 0.4f);
        }
    }
}
```

- [ ] **Step 2: Create PlayerBlockHitState**

Create `Assets/Game/Character/Player/PlayerFsm/PlayerBlockHitState.cs`:

```csharp
using GameMain2.Framework.Core.FSM;
using GameMain2.Framework.Manager;
using GameMain2.Scripts.Character;
using UnityEngine;

namespace Game.Character.Player.PlayerFsm
{
    public class PlayerBlockHitState : PlayerStateBase
    {
        private const string DefaultAnimationName = "DefenceHit_Light";
        private const float DefaultFallbackExitDuration = 0.35f;
        private const float GroundStickVelocity = -1f;

        private string m_animationName = DefaultAnimationName;
        private Vector3 m_pushBackDirection;
        private float m_pushBackDistance;
        private float m_pushBackDuration;
        private float m_elapsedTime;
        private float m_appliedDistance;

        /// <summary>进入格挡受击状态，播放本次格挡力道对应动画并初始化后退参数。</summary>
        public override void Enter(FsmBase<PlayerStateMachine> fsm)
        {
            fsm.Owner.CurState = PlayerState.Defence;
            CharacterStateMachine.PendingBlockReaction reaction = fsm.Owner.ConsumePendingBlockReaction();
            m_animationName = fsm.Owner.ResolveBlockReactionAnimationName(reaction.AnimationName);
            m_pushBackDirection = ResolvePushBackDirection(reaction.HitDirection, fsm.Owner.transform.forward);
            m_pushBackDistance = Mathf.Max(0f, reaction.PushBackDistance);
            m_pushBackDuration = Mathf.Max(0f, reaction.PushBackDuration);
            m_elapsedTime = 0f;
            m_appliedDistance = 0f;
            fsm.Owner.TryCrossFadeInFixedTime(m_animationName);
        }

        /// <summary>更新格挡受击后退和退出逻辑，动画结束后根据防御键状态回到防御或待机。</summary>
        public override void Update(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
            m_elapsedTime += Mathf.Max(0f, deltaTime);
            ApplyPushBack(fsm, deltaTime);

            if (fsm.Owner.IsPlayingAnimation(m_animationName, out float time))
            {
                if (time < 1f)
                {
                    return;
                }
            }
            else if (m_elapsedTime < DefaultFallbackExitDuration)
            {
                return;
            }

            if (InputManager.Instance.IsDefenseKeyPressed())
            {
                fsm.ChangeState<DefenceState>();
                return;
            }

            fsm.ChangeState<IdleState>();
        }

        /// <summary>退出格挡受击状态时清理一次性位移和动画缓存。</summary>
        public override void Exit(FsmBase<PlayerStateMachine> fsm)
        {
            m_animationName = DefaultAnimationName;
            m_pushBackDirection = Vector3.zero;
            m_pushBackDistance = 0f;
            m_pushBackDuration = 0f;
            m_elapsedTime = 0f;
            m_appliedDistance = 0f;
        }

        /// <summary>按配置距离和时长执行后退，保持轻微下压以贴地。</summary>
        private void ApplyPushBack(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
            if (fsm.Owner.PlayerController == null || m_pushBackDistance <= 0f || m_pushBackDuration <= 0f)
            {
                return;
            }

            float step = ResolvePushBackStep(m_pushBackDistance, m_pushBackDuration, Mathf.Max(0f, deltaTime), m_appliedDistance);
            if (step <= 0f)
            {
                return;
            }

            m_appliedDistance += step;
            Vector3 motion = m_pushBackDirection * step;
            motion.y = GroundStickVelocity * Mathf.Max(0f, deltaTime);
            fsm.Owner.PlayerController.Move(motion);
        }

        /// <summary>把命中方向转换为玩家后退方向；没有命中方向时使用角色背向。</summary>
        private static Vector3 ResolvePushBackDirection(Vector3 hitDirection, Vector3 ownerForward)
        {
            Vector3 direction = -hitDirection;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                return direction.normalized;
            }

            Vector3 fallback = -ownerForward;
            fallback.y = 0f;
            if (fallback.sqrMagnitude > 0.0001f)
            {
                return fallback.normalized;
            }

            return Vector3.back;
        }

        /// <summary>计算当前帧应后退的距离，确保累计位移不会超过目标距离。</summary>
        private static float ResolvePushBackStep(float distance, float duration, float deltaTime, float appliedDistance)
        {
            if (distance <= 0f || duration <= 0f || deltaTime <= 0f)
            {
                return 0f;
            }

            float remainingDistance = Mathf.Max(0f, distance - Mathf.Max(0f, appliedDistance));
            float step = distance / duration * deltaTime;
            return Mathf.Min(remainingDistance, step);
        }
    }
}
```

- [ ] **Step 3: Register PlayerBlockHitState**

Modify `_getPlayerStates()` in `Assets/Game/Character/Player/PlayerStateMachine.cs`:

```csharp
stateList.Add(new GetHitState());
stateList.Add(new PlayerBlockHitState());
stateList.Add(new DefenceState());
```

- [ ] **Step 4: Run Unity compile**

Run:

```bash
./.aibridge/cli/AIBridgeCLI.exe compile unity
```

Expected: compile succeeds. If compile fails because `CharacterStateMachine.PendingBlockReaction` needs full namespace resolution, add `using Game.Character;` to `PlayerBlockHitState.cs`.

- [ ] **Step 5: Commit Tasks 4 and 5**

```bash
git add Assets/Game/Battle/Combat/CombatReaction.cs Assets/Game/Character/Player/PlayerFsm/PlayerBlockHitState.cs Assets/Game/Character/Player/PlayerStateMachine.cs Assets/Game/Editor/PlayerBlockHitStateEditModeTests.cs
git commit -m "feat: play player block hit reactions"
```

---

### Task 6: Tune Visual Data and Document Animator Requirements

**Files:**
- Modify: `Docs/CombatSystemFramework.md`

- [ ] **Step 1: Update combat system document**

Append this section to `Docs/CombatSystemFramework.md`:

```markdown
## 15. 防御受击表现约定

玩家防御成功后，`DamageResolver.ResolveBlock()` 会根据稳定值伤害产出 `BlockReactionType`：

- `Light`：播放 `DefenceHit_Light`，短促轻微后退。
- `Medium`：播放 `DefenceHit_Medium`，常规后退。
- `Heavy`：播放 `DefenceHit_Heavy`，明显后退。
- 稳定值被打空时优先进入 `Unbalance`，不播放普通格挡受击。

Animator 需要提供以下状态或可被 `TryCrossFadeInFixedTime` 播放的动画名：

- `DefenceHit_Light`
- `DefenceHit_Medium`
- `DefenceHit_Heavy`

格挡火花仍通过技能配置的 `onBlockEffects` 播放；格挡音效后续应通过统一的 `CombatAudioExecutor` 接入，不放进 `DamageResolver`。
```

- [ ] **Step 2: Run Unity compile**

Run:

```bash
./.aibridge/cli/AIBridgeCLI.exe compile unity
```

Expected: compile succeeds.

- [ ] **Step 3: Commit**

```bash
git add Docs/CombatSystemFramework.md
git commit -m "docs: document player block reactions"
```

---

### Task 7: Final Validation

**Files:**
- No code changes expected.

- [ ] **Step 1: Run required Unity compile**

Run:

```bash
./.aibridge/cli/AIBridgeCLI.exe compile unity
```

Expected: Unity compile succeeds with exit code 0.

- [ ] **Step 2: Check Unity error log**

Run:

```bash
./.aibridge/cli/AIBridgeCLI.exe get_logs --logType Error
```

Expected: no new compile/runtime errors related to `BlockReactionType`, `PlayerBlockHitState`, `CombatReaction`, or `DamageResolver`.

- [ ] **Step 3: Manual Play Mode smoke test**

In the Unity scene:

1. Give player an Animator state or override clip named `DefenceHit_Light`.
2. Give player an Animator state or override clip named `DefenceHit_Medium`.
3. Give player an Animator state or override clip named `DefenceHit_Heavy`.
4. Enter Play Mode.
5. Hold defence.
6. Let a light enemy attack hit the player.
7. Observe `DefenceHit_Light` and slight back movement.
8. Let a heavier enemy attack hit the player.
9. Observe `DefenceHit_Heavy` and stronger back movement.
10. Reduce player stability so the next blocked hit empties stability.
11. Observe player enters `Unbalance` instead of a normal block hit animation.

- [ ] **Step 4: Final commit if validation-only changes occurred**

If validation required small tuning changes:

```bash
git add Assets/Game/Battle/Combat/Core/DamageResolver.cs Assets/Game/Character/Player/PlayerFsm/PlayerBlockHitState.cs Docs/CombatSystemFramework.md
git commit -m "fix: tune player block reaction validation"
```

If no changes occurred, do not create an empty commit.

---

## Self-Review

- Spec coverage:
  - 不同攻击力道对应不同防御受击动画：Tasks 1, 2, 5。
  - 防御玩家被敌人攻击后有反应：Tasks 4, 5。
  - 后退表现：Tasks 1, 2, 5。
  - 破防优先失衡：Task 2。
  - 文档同步：Task 6。
  - Unity 编译验证：Tasks 2, 3, 5, 6, 7。

- Placeholder scan:
  - 本计划不包含 `TBD`、`TODO`、`implement later`。
  - 每个代码修改步骤都给出具体代码或具体插入内容。

- Type consistency:
  - `BlockReactionType` 定义在 `CombatResult.cs` 的 `Game.Battle.Combat` 命名空间下。
  - `CombatResult.BlockReactionAnimation` 被 `CombatReaction` 写入 `CharacterStateMachine.SetPendingBlockReaction()`。
  - `PlayerBlockHitState` 从 `ConsumePendingBlockReaction()` 消费同一数据结构。
