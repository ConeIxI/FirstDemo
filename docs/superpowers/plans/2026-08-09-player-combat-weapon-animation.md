# Player Combat Weapon Animation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 为玩家实现双武器收纳显示、敌人锁定驱动的拔刀、3 秒自动收刀、战斗内两段式换武器，以及动画被打断后的确定性结算。

**Architecture:** `PlayerStateMachine` 持有战斗姿态事实，独立的 `PlayerCombatStanceContext` 负责可测试的阶段和计时逻辑；`LocomotionState` 只在 `ArmsLayer` 推进 `EnterCombat`/`ExitCombat`。`EquipmentManager` 把 active 武器数据同步与模型显隐拆开，`PlayerEquipmentAppearance` 按两个槽分别管理手持和收纳对象。

**Tech Stack:** Unity 2022.3.61f1c1、C# 9.0、现有 FSM/EventCenter/AnimatorOverrideController、Unity Test Framework、AIBridge CLI。

---

## 0. 实施前约束

- 设计依据：`docs/superpowers/specs/2026-08-09-player-combat-weapon-animation-design.md`。
- 当前工作区已有用户改动：`CharacterStateMachine.cs`、`LocomotionState.cs`、`RollState.cs`。所有步骤必须在这些改动上合并，不得回滚。
- 当前工程没有玩家 Prefab。玩法玩家与背包预览分别是 `Assets/Scenes/Scene1.unity` 中的 `Player`、`PlayerPreview`。
- 玩家 Animator：`Assets/Res/AnimatorController/Player/Player.controller`。
- 武器 OverrideController：
  - `Assets/Res/AnimatorController/Player/GreatSword.overrideController`
  - `Assets/Res/AnimatorController/Player/SingleSword.overrideController`
- 现有玩家武器对象位于右手骨骼下的 `Weapons/GreatSword` 与 `Weapons/SingleSword`；资源任务需要为两个装备槽建立彼此独立的手持与收纳对象。
- 每个新增或修改函数都添加简体中文注释。
- 每次 Unity 编译只使用 `$CLI compile unity`。EditMode 测试使用 `$CLI test run --mode EditMode`。

## 1. 文件职责映射

**新增：**

- `Assets/Game/Character/Player/Combat/PlayerCombatStanceContext.cs`：纯 C# 战斗姿态、计时、敌人集合和过渡结算。
- `Assets/Game/EventArgs/EnemyCombatTargetChangedEventArgs.cs`：敌人战斗目标变化的强类型全局事件。
- `Assets/Game/Character/Player/Equipment/PlayerWeaponAppearanceSlot.cs`：可序列化的双槽武器表现配置。
- `Assets/Game/Editor/PlayerCombatStanceContextEditModeTests.cs`：战斗姿态纯逻辑测试。
- `Assets/Game/Editor/EnemyCombatTargetChangedEditModeTests.cs`：敌人目标变化测试。
- `Assets/Game/Editor/PlayerEquipmentAppearanceEditModeTests.cs`：双槽显隐测试。
- `Assets/Game/Editor/EquipmentManagerWeaponSwitchEditModeTests.cs`：active 数据与表现解耦测试。
- `Assets/Game/Editor/PlayerCombatStanceIntegrationEditModeTests.cs`：玩家状态机请求、动画事件和中断结算测试。
- `Assets/Game/Editor/Support/PlayerEquipmentTestFixture.cs`：外观、装备和状态机测试共用的完整双槽对象夹具。
- `Assets/Game/Editor/Support/PlayerCombatStateMachineFixture.cs`：组装最小玩家控制器、装备管理和状态机的集成测试夹具。

**修改：**

- `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs`：所有 `CombatTarget` 写入统一经过变化通知。
- `Assets/Game/Character/Enemy/AI/AIController.cs`：转发目标变化，禁用时释放目标。
- `Assets/Game/Character/Player/Equipment/PlayerEquipmentAppearance.cs`：替换“只显示 active”规则。
- `Assets/Game/Character/Player/Equipment/EquipmentManager.cs`：拆分 active 数据同步和姿态显隐。
- `Assets/Game/Character/CharacterStateMachine.cs`：增加 Animator 层和参数查询接口，保留现有平滑 `SetFloat` 重载。
- `Assets/Game/Character/Player/PlayerStateMachine.cs`：拥有姿态上下文、目标集合、Animator 参数和动画事件入口。
- `Assets/Game/Character/Player/PlayerFsm/PlayerStateBase.cs`：攻击/技能直接入战，换武器改为请求流程。
- `Assets/Game/Character/Player/PlayerFsm/LocomotionState.cs`：在 `ArmsLayer` 推进拔刀、收刀和换武器。
- `Assets/Game/Character/Player/PlayerFsm/DefenceState.cs`：换武器请求成功后返回 Locomotion 执行动画。
- `Assets/Game/Character/Player/PlayerDefine.cs`：移除 `WeaponSwitch` 枚举项。
- `Assets/Res/AnimatorController/Player/Player.controller`：添加参数、动画层和两个状态。
- 两个玩家武器 OverrideController：配置各自拔刀、收刀动画。
- `Assets/Scenes/Scene1.unity`：配置 `Player` 与 `PlayerPreview` 的双槽武器表现对象。

**删除：**

- `Assets/Game/Character/Player/PlayerFsm/WeaponSwitchState.cs`
- `Assets/Game/Character/Player/PlayerFsm/WeaponSwitchState.cs.meta`

---

### Task 1: 建立可测试的玩家战斗姿态上下文

**Files:**
- Create: `Assets/Game/Character/Player/Combat/PlayerCombatStanceContext.cs`
- Create: `Assets/Game/Editor/PlayerCombatStanceContextEditModeTests.cs`

- [ ] **Step 1: 写入阶段、计时、敌人集合和中断结算的失败测试**

测试至少覆盖：敌人集合去重、3 秒计时条件、活动刷新、普通拔刀/收刀完成、战斗换武器两阶段，以及四类中断结果。

```csharp
using Game.Character.Player.Combat;
using NUnit.Framework;

public sealed class PlayerCombatStanceContextEditModeTests
{
    /// <summary>只有最后一个敌人释放后，自动收刀计时才会推进。</summary>
    [Test]
    public void TickAutoSheath_MultipleTargetingEnemies_WaitsForLastRelease()
    {
        PlayerCombatStanceContext context = new PlayerCombatStanceContext();
        context.EnterCombatImmediately();
        context.SetEnemyTargeting(10, true);
        context.SetEnemyTargeting(20, true);
        context.SetEnemyTargeting(10, false);

        Assert.IsFalse(context.TickAutoSheath(3f, true));
        context.SetEnemyTargeting(20, false);
        Assert.IsTrue(context.TickAutoSheath(3f, true));
    }

    /// <summary>战斗换武器在旧武器收刀完成后进入新武器拔刀阶段。</summary>
    [Test]
    public void CompleteAnimationPhase_SwitchExit_RequestsDataSwitchAndEnterAnimation()
    {
        PlayerCombatStanceContext context = new PlayerCombatStanceContext();
        context.EnterCombatImmediately();
        Assert.IsTrue(context.RequestWeaponSwitch(0, 1));

        PlayerCombatAnimationCompletion completion = context.CompleteAnimationPhase();

        Assert.AreEqual(PlayerCombatAnimationCompletion.SwitchWeaponAndEnter, completion);
        Assert.AreEqual(PlayerCombatTransitionPhase.SwitchingWeaponEnter, context.Phase);
        Assert.AreEqual(1, context.TargetWeaponIndex);
        Assert.IsTrue(context.IsCombat);
    }

    /// <summary>战斗换武器被打断时直接结算到目标武器已在战斗状态。</summary>
    [Test]
    public void SettleInterruptedTransition_WeaponSwitch_ReturnsTargetWeapon()
    {
        PlayerCombatStanceContext context = new PlayerCombatStanceContext();
        context.EnterCombatImmediately();
        context.RequestWeaponSwitch(0, 1);

        PlayerCombatTransitionOutcome outcome = context.SettleInterruptedTransition();

        Assert.IsTrue(outcome.IsCombat);
        Assert.IsTrue(outcome.ShouldSwitchWeapon);
        Assert.AreEqual(1, outcome.TargetWeaponIndex);
        Assert.AreEqual(PlayerCombatTransitionPhase.None, context.Phase);
    }
}
```

- [ ] **Step 2: 运行测试并确认失败原因是类型尚不存在**

Run:

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --group-name 'PlayerCombatStanceContextEditModeTests' --timeout 120000
```

Expected: FAIL，编译错误指向 `PlayerCombatStanceContext`、阶段枚举或结果类型不存在。

- [ ] **Step 3: 实现战斗姿态上下文**

使用以下公开契约，所有阶段变更只在该类型内部发生：

```csharp
using System.Collections.Generic;

namespace Game.Character.Player.Combat
{
    public enum PlayerCombatTransitionPhase
    {
        None,
        EnteringCombat,
        ExitingCombat,
        SwitchingWeaponExit,
        SwitchingWeaponEnter
    }

    public enum PlayerCombatAnimationCompletion
    {
        None,
        CombatEntered,
        CombatExited,
        SwitchWeaponAndEnter,
        WeaponSwitchCompleted
    }

    public readonly struct PlayerCombatTransitionOutcome
    {
        public bool IsCombat { get; }
        public bool ShouldSwitchWeapon { get; }
        public int TargetWeaponIndex { get; }

        /// <summary>创建一次动画中断后的最终结算结果。</summary>
        public PlayerCombatTransitionOutcome(bool isCombat, bool shouldSwitchWeapon, int targetWeaponIndex)
        {
            IsCombat = isCombat;
            ShouldSwitchWeapon = shouldSwitchWeapon;
            TargetWeaponIndex = targetWeaponIndex;
        }
    }

    public sealed class PlayerCombatStanceContext
    {
        public const float AutoSheathDelay = 3f;

        private readonly HashSet<int> m_targetingEnemyIds = new HashSet<int>();
        private float m_idleElapsed;

        public bool IsCombat { get; private set; }
        public bool HasTargetingEnemy => m_targetingEnemyIds.Count > 0;
        public PlayerCombatTransitionPhase Phase { get; private set; }
        public int SourceWeaponIndex { get; private set; } = -1;
        public int TargetWeaponIndex { get; private set; } = -1;

        /// <summary>写入或移除一个敌人的锁定事实，并在新增锁定时刷新战斗活动。</summary>
        public void SetEnemyTargeting(int enemyId, bool isTargeting)
        {
            if (isTargeting)
            {
                if (m_targetingEnemyIds.Add(enemyId))
                {
                    RefreshCombatActivity();
                }
                return;
            }

            m_targetingEnemyIds.Remove(enemyId);
        }

        /// <summary>请求播放普通拔刀动画；已有战斗姿态或过渡时拒绝重复请求。</summary>
        public bool RequestEnterCombatAnimation()
        {
            if (IsCombat || Phase != PlayerCombatTransitionPhase.None)
            {
                return false;
            }

            Phase = PlayerCombatTransitionPhase.EnteringCombat;
            return true;
        }

        /// <summary>直接进入战斗并清理普通进入或退出过渡。</summary>
        public void EnterCombatImmediately()
        {
            IsCombat = true;
            Phase = PlayerCombatTransitionPhase.None;
            SourceWeaponIndex = -1;
            TargetWeaponIndex = -1;
            RefreshCombatActivity();
        }

        /// <summary>没有可播放武器时直接退出战斗并清理过渡。</summary>
        public void ExitCombatImmediately()
        {
            IsCombat = false;
            Phase = PlayerCombatTransitionPhase.None;
            SourceWeaponIndex = -1;
            TargetWeaponIndex = -1;
            RefreshCombatActivity();
        }

        /// <summary>请求播放普通收刀动画。</summary>
        public bool RequestExitCombatAnimation()
        {
            if (!IsCombat || Phase != PlayerCombatTransitionPhase.None)
            {
                return false;
            }

            Phase = PlayerCombatTransitionPhase.ExitingCombat;
            return true;
        }

        /// <summary>请求战斗中两段式换武器。</summary>
        public bool RequestWeaponSwitch(int sourceWeaponIndex, int targetWeaponIndex)
        {
            if (!IsCombat || Phase != PlayerCombatTransitionPhase.None
                || sourceWeaponIndex < 0 || targetWeaponIndex < 0
                || sourceWeaponIndex == targetWeaponIndex)
            {
                return false;
            }

            SourceWeaponIndex = sourceWeaponIndex;
            TargetWeaponIndex = targetWeaponIndex;
            Phase = PlayerCombatTransitionPhase.SwitchingWeaponExit;
            RefreshCombatActivity();
            return true;
        }

        /// <summary>在满足条件时推进自动收刀计时，并在达到三秒时返回 true。</summary>
        public bool TickAutoSheath(float deltaTime, bool isLocomotion)
        {
            if (!IsCombat || !isLocomotion || HasTargetingEnemy
                || Phase != PlayerCombatTransitionPhase.None)
            {
                return false;
            }

            m_idleElapsed += deltaTime;
            return m_idleElapsed >= AutoSheathDelay;
        }

        /// <summary>刷新战斗活动并把自动收刀计时归零。</summary>
        public void RefreshCombatActivity()
        {
            m_idleElapsed = 0f;
        }

        /// <summary>完成当前动画阶段，并返回状态机需要执行的后续动作。</summary>
        public PlayerCombatAnimationCompletion CompleteAnimationPhase()
        {
            switch (Phase)
            {
                case PlayerCombatTransitionPhase.EnteringCombat:
                    IsCombat = true;
                    ClearTransition();
                    return PlayerCombatAnimationCompletion.CombatEntered;
                case PlayerCombatTransitionPhase.ExitingCombat:
                    IsCombat = false;
                    ClearTransition();
                    return PlayerCombatAnimationCompletion.CombatExited;
                case PlayerCombatTransitionPhase.SwitchingWeaponExit:
                    Phase = PlayerCombatTransitionPhase.SwitchingWeaponEnter;
                    return PlayerCombatAnimationCompletion.SwitchWeaponAndEnter;
                case PlayerCombatTransitionPhase.SwitchingWeaponEnter:
                    ClearTransition();
                    return PlayerCombatAnimationCompletion.WeaponSwitchCompleted;
                default:
                    return PlayerCombatAnimationCompletion.None;
            }
        }

        /// <summary>把任意未完成过渡结算到确定终点，并返回是否要切换目标武器。</summary>
        public PlayerCombatTransitionOutcome SettleInterruptedTransition()
        {
            bool shouldSwitchWeapon = Phase == PlayerCombatTransitionPhase.SwitchingWeaponExit
                || Phase == PlayerCombatTransitionPhase.SwitchingWeaponEnter;
            int targetWeaponIndex = shouldSwitchWeapon ? TargetWeaponIndex : -1;

            if (Phase == PlayerCombatTransitionPhase.EnteringCombat || shouldSwitchWeapon)
            {
                IsCombat = true;
            }
            else if (Phase == PlayerCombatTransitionPhase.ExitingCombat)
            {
                IsCombat = false;
            }

            ClearTransition();
            return new PlayerCombatTransitionOutcome(IsCombat, shouldSwitchWeapon, targetWeaponIndex);
        }

        /// <summary>清理过渡槽位和阶段，不改变稳定战斗姿态。</summary>
        private void ClearTransition()
        {
            Phase = PlayerCombatTransitionPhase.None;
            SourceWeaponIndex = -1;
            TargetWeaponIndex = -1;
        }
    }
}
```

- [ ] **Step 4: 运行姿态测试并确认全部通过**

Run: 与 Step 2 相同。

Expected: `PlayerCombatStanceContextEditModeTests` 全部 PASS，failed 为 0。

- [ ] **Step 5: 提交纯逻辑与测试**

```powershell
git add -- Assets/Game/Character/Player/Combat/PlayerCombatStanceContext.cs Assets/Game/Editor/PlayerCombatStanceContextEditModeTests.cs
git commit -m "新增玩家战斗姿态上下文"
```

---

### Task 2: 发布敌人战斗目标变化事件

**Files:**
- Create: `Assets/Game/EventArgs/EnemyCombatTargetChangedEventArgs.cs`
- Create: `Assets/Game/Editor/EnemyCombatTargetChangedEditModeTests.cs`
- Modify: `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs:12-14,61-97,125-133,166-189,353-389`
- Modify: `Assets/Game/Character/Enemy/AI/AIController.cs:16-90`

- [ ] **Step 1: 写入黑板只在目标真正变化时通知的失败测试**

```csharp
using Game.Character.Enemy.Core;
using NUnit.Framework;
using UnityEngine;

public sealed class EnemyCombatTargetChangedEditModeTests
{
    /// <summary>重复记住同一目标只发布一次变化通知。</summary>
    [Test]
    public void RememberTarget_SameTargetTwice_RaisesOneChange()
    {
        EnemyBlackboard blackboard = new EnemyBlackboard();
        GameObject target = new GameObject("Target");
        int eventCount = 0;
        blackboard.CombatTargetChanged += (oldTarget, newTarget) => eventCount++;

        blackboard.RememberTarget(target.transform);
        blackboard.RememberTarget(target.transform);

        Assert.AreEqual(1, eventCount);
        Object.DestroyImmediate(target);
    }

    /// <summary>敌人死亡清理目标时发布一次释放通知。</summary>
    [Test]
    public void SetDead_WithCombatTarget_RaisesReleaseChange()
    {
        EnemyBlackboard blackboard = new EnemyBlackboard();
        GameObject target = new GameObject("Target");
        Transform releasedTarget = null;
        blackboard.RememberTarget(target.transform);
        blackboard.CombatTargetChanged += (oldTarget, newTarget) => releasedTarget = oldTarget;

        blackboard.SetDead(true);

        Assert.AreSame(target.transform, releasedTarget);
        Assert.IsNull(blackboard.CombatTarget);
        Object.DestroyImmediate(target);
    }
}
```

- [ ] **Step 2: 运行目标变化测试并确认失败**

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --group-name 'EnemyCombatTargetChangedEditModeTests' --timeout 120000
```

Expected: FAIL，`EnemyBlackboard.CombatTargetChanged` 尚不存在。

- [ ] **Step 3: 新增强类型 EventArgs**

```csharp
using GameMain2.Framework.Core;
using UnityEngine;

namespace Game.Character.Enemy.Events
{
    public sealed class EnemyCombatTargetChangedEventArgs : EventArgsBase
    {
        public static readonly int EventId = typeof(EnemyCombatTargetChangedEventArgs).GetHashCode();

        public override int Id => EventId;
        public Transform Enemy { get; }
        public Transform OldTarget { get; }
        public Transform NewTarget { get; }

        /// <summary>创建敌人战斗目标变化事件。</summary>
        public EnemyCombatTargetChangedEventArgs(Transform enemy, Transform oldTarget, Transform newTarget)
        {
            Enemy = enemy;
            OldTarget = oldTarget;
            NewTarget = newTarget;
        }
    }
}
```

- [ ] **Step 4: 让 EnemyBlackboard 的所有目标写入经过单一入口**

添加事件与统一写入函数，并把 `RememberTarget`、`ForgetTarget`、`ClearCombatTarget`、`SetCombatTarget` 中的直接赋值替换为 `ChangeCombatTarget`：

```csharp
public event System.Action<Transform, Transform> CombatTargetChanged;

/// <summary>仅在战斗目标引用真正变化时写入并发布通知。</summary>
private void ChangeCombatTarget(Transform target)
{
    if (CombatTarget == target)
    {
        return;
    }

    Transform oldTarget = CombatTarget;
    CombatTarget = target;
    CombatTargetChanged?.Invoke(oldTarget, target);
}
```

保持记忆时长、警戒记忆、攻击计划等现有清理顺序不变。

- [ ] **Step 5: 让 AIController 转发全局事件并在禁用时释放目标**

记录当前 `EnemyAgent`，在 `OnEnable`/`OnDisable` 订阅和解除黑板事件。禁用时先调用 `Blackboard.ForgetTarget()`，确保仍处于订阅状态时发出释放通知。

```csharp
private EnemyAgent m_agent;

/// <summary>把黑板目标变化转换为全局强类型事件。</summary>
private void OnCombatTargetChanged(Transform oldTarget, Transform newTarget)
{
    if (!Application.isPlaying)
    {
        return;
    }

    Transform enemyTransform = m_agent != null ? m_agent.transform : transform;
    EventCenter.Instance.Fire(
        this,
        new EnemyCombatTargetChangedEventArgs(enemyTransform, oldTarget, newTarget));
}
```

`SetBlackboardForTests` 必须先解除旧黑板事件再绑定新黑板，避免测试和运行时重复订阅。

- [ ] **Step 6: 运行新增测试与敌人记忆回归测试**

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --group-name 'EnemyCombatTargetChangedEditModeTests|EnemyTargetMemoryEditModeTests|EnemyTargetMemoryRuntimeEditModeTests' --timeout 120000
```

Expected: 三组测试全部 PASS，现有战斗记忆倒计时与警戒降级行为不变。

- [ ] **Step 7: 提交敌人目标事件链路**

```powershell
git add -- Assets/Game/EventArgs/EnemyCombatTargetChangedEventArgs.cs Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs Assets/Game/Character/Enemy/AI/AIController.cs Assets/Game/Editor/EnemyCombatTargetChangedEditModeTests.cs
git commit -m "新增敌人战斗目标变化事件"
```

---

### Task 3: 重构双槽武器手持与收纳表现

**Files:**
- Create: `Assets/Game/Character/Player/Equipment/PlayerWeaponAppearanceSlot.cs`
- Create: `Assets/Game/Editor/PlayerEquipmentAppearanceEditModeTests.cs`
- Create: `Assets/Game/Editor/Support/PlayerEquipmentTestFixture.cs`
- Modify: `Assets/Game/Character/Player/Equipment/PlayerEquipmentAppearance.cs:11-35,43-63,124-229,244-303,457-530`

- [ ] **Step 1: 写入双槽显隐和不完整配置拒绝测试**

```csharp
using Game.Character.Equipment;
using NUnit.Framework;
using UnityEngine;

public sealed class PlayerEquipmentAppearanceEditModeTests
{
    /// <summary>非战斗时两个已装备槽都显示收纳对象。</summary>
    [Test]
    public void ApplyCombatAppearance_TwoEquippedOutOfCombat_ShowsBothSheathedObjects()
    {
        PlayerEquipmentTestFixture fixture = PlayerEquipmentTestFixture.Create(false);
        PlayerEquipmentAppearance appearance = fixture.Appearance;
        appearance.SetWeaponObject(0, "GreatSword");
        appearance.SetWeaponObject(1, "SingleSword");

        appearance.ApplyCombatAppearance(0, false);

        Assert.IsTrue(fixture.GreatSwordSheathed.activeSelf);
        Assert.IsTrue(fixture.SingleSwordSheathed.activeSelf);
        Assert.IsFalse(fixture.GreatSwordHand.activeSelf);
        Assert.IsFalse(fixture.SingleSwordHand.activeSelf);
        Object.DestroyImmediate(appearance.gameObject);
    }

    /// <summary>战斗时只有当前槽显示手持对象，另一槽继续收纳。</summary>
    [Test]
    public void ApplyCombatAppearance_ActiveSlotInCombat_ShowsActiveInHand()
    {
        PlayerEquipmentTestFixture fixture = PlayerEquipmentTestFixture.Create(false);
        PlayerEquipmentAppearance appearance = fixture.Appearance;
        appearance.SetWeaponObject(0, "GreatSword");
        appearance.SetWeaponObject(1, "SingleSword");

        appearance.ApplyCombatAppearance(1, true);

        Assert.IsTrue(fixture.GreatSwordSheathed.activeSelf);
        Assert.IsTrue(fixture.SingleSwordHand.activeSelf);
        Assert.IsFalse(fixture.GreatSwordHand.activeSelf);
        Assert.IsFalse(fixture.SingleSwordSheathed.activeSelf);
        Object.DestroyImmediate(appearance.gameObject);
    }
}
```

共享夹具使用完整代码创建两个槽，不依赖 Scene1 序列化对象：

```csharp
using Game.Character.Equipment;
using UnityEngine;

public sealed class PlayerEquipmentTestFixture
{
    public GameObject Root { get; private set; }
    public PlayerEquipmentAppearance Appearance { get; private set; }
    public EquipmentManager Manager { get; private set; }
    public GameObject GreatSwordHand { get; private set; }
    public GameObject GreatSwordSheathed { get; private set; }
    public GameObject SingleSwordHand { get; private set; }
    public GameObject SingleSwordSheathed { get; private set; }

    /// <summary>创建具备两个独立武器槽的 EditMode 测试夹具。</summary>
    public static PlayerEquipmentTestFixture Create(bool includeManager)
    {
        PlayerEquipmentTestFixture fixture = new PlayerEquipmentTestFixture();
        fixture.Root = new GameObject("PlayerEquipmentFixture");
        fixture.Root.SetActive(false);
        fixture.Appearance = fixture.Root.AddComponent<PlayerEquipmentAppearance>();

        PlayerWeaponAppearanceEntry greatSword = fixture.CreateEntry(
            "GreatSword",
            out GameObject greatSwordHand,
            out GameObject greatSwordSheathed);
        PlayerWeaponAppearanceEntry singleSword = fixture.CreateEntry(
            "SingleSword",
            out GameObject singleSwordHand,
            out GameObject singleSwordSheathed);

        fixture.GreatSwordHand = greatSwordHand;
        fixture.GreatSwordSheathed = greatSwordSheathed;
        fixture.SingleSwordHand = singleSwordHand;
        fixture.SingleSwordSheathed = singleSwordSheathed;
        fixture.Appearance.ConfigureWeaponSlotsForTests(new[]
        {
            new PlayerWeaponAppearanceSlot(new[] { greatSword }),
            new PlayerWeaponAppearanceSlot(new[] { singleSword })
        });

        if (includeManager)
        {
            fixture.Manager = fixture.Root.AddComponent<EquipmentManager>();
        }

        return fixture;
    }

    /// <summary>创建一个包含逻辑数据、手持对象和收纳对象的完整表现项。</summary>
    private PlayerWeaponAppearanceEntry CreateEntry(
        string objectName,
        out GameObject handObject,
        out GameObject sheathedObject)
    {
        GameObject dataObject = new GameObject(objectName + "Data");
        dataObject.transform.SetParent(Root.transform);
        WeaponData weaponData = dataObject.AddComponent<WeaponData>();
        handObject = new GameObject(objectName + "Hand");
        handObject.transform.SetParent(dataObject.transform);
        sheathedObject = new GameObject(objectName + "Sheathed");
        sheathedObject.transform.SetParent(dataObject.transform);
        return new PlayerWeaponAppearanceEntry(objectName, weaponData, handObject, sheathedObject);
    }

    /// <summary>销毁测试创建的完整对象层级。</summary>
    public void Dispose()
    {
        Object.DestroyImmediate(Root);
    }
}
```

- [ ] **Step 2: 运行外观测试并确认失败**

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --group-name 'PlayerEquipmentAppearanceEditModeTests' --timeout 120000
```

Expected: FAIL，缺少 `PlayerWeaponAppearanceSlot` 和 `ApplyCombatAppearance`。

- [ ] **Step 3: 新增可序列化武器表现项与槽位**

```csharp
using System;
using UnityEngine;

namespace Game.Character.Equipment
{
    [Serializable]
    public sealed class PlayerWeaponAppearanceEntry
    {
        [SerializeField] private string m_objectName;
        [SerializeField] private WeaponData m_weaponData;
        [SerializeField] private GameObject m_handObject;
        [SerializeField] private GameObject m_sheathedObject;

        public string ObjectName => m_objectName;
        public WeaponData WeaponData => m_weaponData;
        public GameObject HandObject => m_handObject;
        public GameObject SheathedObject => m_sheathedObject;
        public bool IsComplete => !string.IsNullOrWhiteSpace(m_objectName)
            && m_weaponData != null && m_handObject != null && m_sheathedObject != null;

        /// <summary>创建测试或编辑器配置使用的武器表现项。</summary>
        public PlayerWeaponAppearanceEntry(
            string objectName,
            WeaponData weaponData,
            GameObject handObject,
            GameObject sheathedObject)
        {
            m_objectName = objectName;
            m_weaponData = weaponData;
            m_handObject = handObject;
            m_sheathedObject = sheathedObject;
        }

        /// <summary>隐藏该武器的手持和收纳对象。</summary>
        public void Hide()
        {
            m_handObject.SetActive(false);
            m_sheathedObject.SetActive(false);
        }

        /// <summary>把该武器切到手持表现。</summary>
        public void ShowInHand()
        {
            m_sheathedObject.SetActive(false);
            m_handObject.SetActive(true);
        }

        /// <summary>把该武器切到收纳表现。</summary>
        public void ShowSheathed()
        {
            m_handObject.SetActive(false);
            m_sheathedObject.SetActive(true);
        }
    }

    [Serializable]
    public sealed class PlayerWeaponAppearanceSlot
    {
        [SerializeField] private PlayerWeaponAppearanceEntry[] m_entries;

        /// <summary>创建测试或编辑器配置使用的槽位。</summary>
        public PlayerWeaponAppearanceSlot(PlayerWeaponAppearanceEntry[] entries)
        {
            m_entries = entries;
        }

        /// <summary>按装备对象名查找完整表现项。</summary>
        public PlayerWeaponAppearanceEntry Find(string objectName)
        {
            if (m_entries == null)
            {
                return null;
            }

            for (int i = 0; i < m_entries.Length; i++)
            {
                PlayerWeaponAppearanceEntry entry = m_entries[i];
                if (entry != null && entry.IsComplete
                    && string.Equals(entry.ObjectName, objectName, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>隐藏该槽配置的全部武器表现。</summary>
        public void HideAll()
        {
            if (m_entries == null)
            {
                return;
            }

            for (int i = 0; i < m_entries.Length; i++)
            {
                if (m_entries[i] != null && m_entries[i].IsComplete)
                {
                    m_entries[i].Hide();
                }
            }
        }
    }
}
```

- [ ] **Step 4: 用槽位配置重写 PlayerEquipmentAppearance 的武器部分**

保留防具 `EquipmentModelGroup`。删除 `m_WeaponRoot`、`WeaponModelGroup`、`m_activeWeaponIndex` 和 `ApplyWeaponVisibility`，新增：

```csharp
[SerializeField] private PlayerWeaponAppearanceSlot[] m_weaponSlots =
    new PlayerWeaponAppearanceSlot[WeaponSlotCount];

private readonly PlayerWeaponAppearanceEntry[] m_activeEntries =
    new PlayerWeaponAppearanceEntry[WeaponSlotCount];

/// <summary>按当前战斗姿态统一刷新两个武器槽的最终表现。</summary>
public void ApplyCombatAppearance(int activeSlotIndex, bool isCombat)
{
    for (int i = 0; i < WeaponSlotCount; i++)
    {
        PlayerWeaponAppearanceEntry entry = m_activeEntries[i];
        if (entry == null)
        {
            HideWeapon(i);
            continue;
        }

        if (isCombat && i == activeSlotIndex)
        {
            entry.ShowInHand();
        }
        else
        {
            entry.ShowSheathed();
        }
    }
}

/// <summary>把指定槽位当前武器切到手持表现，供动画事件调用。</summary>
public void ShowWeaponInHand(int slotIndex)
{
    if (IsValidWeaponSlot(slotIndex) && m_activeEntries[slotIndex] != null)
    {
        m_activeEntries[slotIndex].ShowInHand();
    }
}

/// <summary>把指定槽位当前武器切到收纳表现，供动画事件调用。</summary>
public void ShowWeaponSheathed(int slotIndex)
{
    if (IsValidWeaponSlot(slotIndex) && m_activeEntries[slotIndex] != null)
    {
        m_activeEntries[slotIndex].ShowSheathed();
    }
}

/// <summary>隐藏指定槽位的所有武器对象。</summary>
public void HideWeapon(int slotIndex)
{
    if (IsValidWeaponSlot(slotIndex) && m_weaponSlots[slotIndex] != null)
    {
        m_weaponSlots[slotIndex].HideAll();
    }
}
```

`SetWeaponObject` 必须先找到完整 entry，再替换槽位；查找失败时清空该槽并返回 `null`。`ClearWeaponObject` 同时调用 `HideWeapon`。`GetWeaponData` 返回 entry 的 `WeaponData`，`GetWeaponGameObject` 返回 entry 的 `HandObject`。

增加仅供 EditMode 测试使用的配置入口：

```csharp
#if UNITY_EDITOR
/// <summary>注入 EditMode 测试使用的武器槽配置并重新初始化。</summary>
public void ConfigureWeaponSlotsForTests(PlayerWeaponAppearanceSlot[] weaponSlots)
{
    m_weaponSlots = weaponSlots;
    m_initialized = false;
    Initialize();
}
#endif
```

- [ ] **Step 5: 运行外观测试并确认通过**

Run: 与 Step 2 相同。

Expected: 两把武器可同时收纳显示，战斗 active 槽切到手持，清槽后两类对象均关闭。

- [ ] **Step 6: 提交双槽表现重构**

```powershell
git add -- Assets/Game/Character/Player/Equipment/PlayerWeaponAppearanceSlot.cs Assets/Game/Character/Player/Equipment/PlayerEquipmentAppearance.cs Assets/Game/Editor/PlayerEquipmentAppearanceEditModeTests.cs Assets/Game/Editor/Support/PlayerEquipmentTestFixture.cs
git commit -m "重构玩家双槽武器表现"
```

---

### Task 4: 解耦当前武器数据与模型姿态

**Files:**
- Create: `Assets/Game/Editor/EquipmentManagerWeaponSwitchEditModeTests.cs`
- Modify: `Assets/Game/Character/Player/Equipment/EquipmentManager.cs:24-45,48-180,217-277,345-363`

- [ ] **Step 1: 写入非战斗切换不改变模型表现的失败测试**

```csharp
using Game.Character.Equipment;
using NUnit.Framework;

public sealed class EquipmentManagerWeaponSwitchEditModeTests
{
    /// <summary>只激活目标武器数据时，两把武器仍保持收纳表现。</summary>
    [Test]
    public void ActivateWeapon_OutOfCombat_DoesNotChangeSheathedAppearance()
    {
        PlayerEquipmentTestFixture fixture = PlayerEquipmentTestFixture.Create(true);
        fixture.Manager.SetWeaponObject(0, "GreatSword", 10);
        fixture.Manager.SetWeaponObject(1, "SingleSword", 20);
        fixture.Manager.ApplyWeaponAppearance(false);

        Assert.IsTrue(fixture.Manager.ActivateWeapon(1));

        Assert.AreEqual(1, fixture.Manager.ActiveWeaponIndex);
        Assert.IsTrue(fixture.GreatSwordSheathed.activeSelf);
        Assert.IsTrue(fixture.SingleSwordSheathed.activeSelf);
        fixture.Dispose();
    }

    /// <summary>显式应用战斗表现后只有当前武器位于手中。</summary>
    [Test]
    public void ApplyWeaponAppearance_InCombat_ShowsOnlyActiveWeaponInHand()
    {
        PlayerEquipmentTestFixture fixture = PlayerEquipmentTestFixture.Create(true);
        fixture.Manager.SetWeaponObject(0, "GreatSword", 10);
        fixture.Manager.SetWeaponObject(1, "SingleSword", 20);
        fixture.Manager.ActivateWeapon(1);

        fixture.Manager.ApplyWeaponAppearance(true);

        Assert.IsTrue(fixture.SingleSwordHand.activeSelf);
        Assert.IsTrue(fixture.GreatSwordSheathed.activeSelf);
        fixture.Dispose();
    }
}
```

- [ ] **Step 2: 运行装备管理测试并确认失败**

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --group-name 'EquipmentManagerWeaponSwitchEditModeTests' --timeout 120000
```

Expected: FAIL，`ActivateWeapon` 或 `ApplyWeaponAppearance` 尚不存在。

- [ ] **Step 3: 拆分 active 数据同步和表现同步**

将 `ApplyActiveWeaponState` 改名为 `ApplyActiveWeaponData`，删除其中的 `m_appearance.SetActiveWeaponIndex` 调用。提供以下公开接口：

```csharp
/// <summary>返回当前槽之后的下一个已装备武器槽，不修改任何状态。</summary>
public int GetNextEquippedWeaponIndex()
{
    return FindNextEquippedWeaponSlot();
}

/// <summary>只切换当前武器数据并同步命中检测器、技能、动画覆盖、事件和属性。</summary>
public bool ActivateWeapon(int targetIndex)
{
    if (!IsValidWeaponSlot(targetIndex) || m_weapons[targetIndex] == null)
    {
        return false;
    }

    m_currentWeaponIndex = targetIndex;
    ApplyActiveWeaponData();
    return true;
}

/// <summary>根据稳定战斗姿态刷新两个槽的手持或收纳表现。</summary>
public void ApplyWeaponAppearance(bool isCombat)
{
    EnsureAppearance();
    if (m_appearance != null)
    {
        m_appearance.ApplyCombatAppearance(m_currentWeaponIndex, isCombat);
    }
}

/// <summary>把指定槽位当前武器显示在手中。</summary>
public void ShowWeaponInHand(int slotIndex)
{
    EnsureAppearance();
    m_appearance?.ShowWeaponInHand(slotIndex);
}

/// <summary>把指定槽位当前武器显示在收纳位置。</summary>
public void ShowWeaponSheathed(int slotIndex)
{
    EnsureAppearance();
    m_appearance?.ShowWeaponSheathed(slotIndex);
}
```

`SetWeaponObject` 与 `ClearWeaponSlot` 在完成数据更新后，读取 `PlayerStateMachine.IsCombat` 并调用 `ApplyWeaponAppearance`。卸下 active 槽时先选备用武器、同步 active 数据，再一次性应用最终表现。卸下最后一把武器时清空技能、命中检测器、AnimatorOverride，并隐藏两个槽。

- [ ] **Step 4: 运行装备管理与外观测试**

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --group-name 'EquipmentManagerWeaponSwitchEditModeTests|PlayerEquipmentAppearanceEditModeTests|PlayerAttributeSetEditModeTests' --timeout 120000
```

Expected: 全部 PASS；现有装备属性快照行为不变。

- [ ] **Step 5: 提交装备数据解耦**

```powershell
git add -- Assets/Game/Character/Player/Equipment/EquipmentManager.cs Assets/Game/Editor/EquipmentManagerWeaponSwitchEditModeTests.cs
git commit -m "解耦当前武器数据与模型姿态"
```

---

### Task 5: 集成 PlayerStateMachine 战斗姿态与动画事件

**Files:**
- Create: `Assets/Game/Editor/PlayerCombatStanceIntegrationEditModeTests.cs`
- Create: `Assets/Game/Editor/Support/PlayerCombatStateMachineFixture.cs`
- Modify: `Assets/Game/Character/CharacterStateMachine.cs:194-230`
- Modify: `Assets/Game/Character/Player/PlayerStateMachine.cs:1-82,102-190,192-238`

- [ ] **Step 1: 写入直接入战、敌人集合和中断结算测试**

```csharp
using Game.Character.Enemy.Events;
using Game.Character.Player.Combat;
using GameMain2.Scripts.Character;
using NUnit.Framework;
using UnityEngine;

public sealed class PlayerCombatStanceIntegrationEditModeTests
{
    /// <summary>攻击入口直接进入战斗并把当前武器显示在手中。</summary>
    [Test]
    public void EnterCombatImmediately_FromNonCombat_SetsAnimatorFactAndAppearance()
    {
        PlayerCombatStateMachineFixture fixture = PlayerCombatStateMachineFixture.Create();

        fixture.StateMachine.EnterCombatImmediately();

        Assert.IsTrue(fixture.StateMachine.IsCombat);
        Assert.IsTrue(fixture.ActiveHandObject.activeSelf);
        fixture.Dispose();
    }

    /// <summary>最后一个敌人释放玩家后才允许自动收刀。</summary>
    [Test]
    public void EnemyTargetChanged_TwoEnemies_TracksBothSources()
    {
        PlayerCombatStateMachineFixture fixture = PlayerCombatStateMachineFixture.Create();
        GameObject enemyA = new GameObject("EnemyA");
        GameObject enemyB = new GameObject("EnemyB");
        Transform player = fixture.StateMachine.PlayerController.transform;

        fixture.StateMachine.HandleEnemyTargetChangedForTests(
            new EnemyCombatTargetChangedEventArgs(enemyA.transform, null, player));
        fixture.StateMachine.HandleEnemyTargetChangedForTests(
            new EnemyCombatTargetChangedEventArgs(enemyB.transform, null, player));
        fixture.StateMachine.HandleEnemyTargetChangedForTests(
            new EnemyCombatTargetChangedEventArgs(enemyA.transform, player, null));

        Assert.IsTrue(fixture.StateMachine.HasTargetingEnemy);
        Object.DestroyImmediate(enemyA);
        Object.DestroyImmediate(enemyB);
        fixture.Dispose();
    }
}
```

集成测试夹具使用以下完整组装方式，整个层级保持 inactive，避免 EditMode 自动执行运行时 `Awake`/`Start`：

```csharp
using Game.Battle.Ability;
using Game.Character.Equipment;
using GameMain2.Scripts.Character;
using UnityEngine;

public sealed class PlayerCombatStateMachineFixture
{
    private PlayerEquipmentTestFixture m_equipmentFixture;

    public PlayerStateMachine StateMachine { get; private set; }
    public EquipmentManager EquipmentManager => m_equipmentFixture.Manager;
    public GameObject ActiveHandObject => m_equipmentFixture.GreatSwordHand;

    /// <summary>创建包含玩家控制器、装备系统和状态机的最小集成测试环境。</summary>
    public static PlayerCombatStateMachineFixture Create(bool equipSecondWeapon = false)
    {
        PlayerCombatStateMachineFixture fixture = new PlayerCombatStateMachineFixture();
        fixture.m_equipmentFixture = PlayerEquipmentTestFixture.Create(true);
        GameObject root = fixture.m_equipmentFixture.Root;
        root.AddComponent<CombatAttributeSet>();
        root.AddComponent<CombatAbilitySystem>();
        PlayerController controller = root.AddComponent<PlayerController>();
        root.AddComponent<PlayerSkillManager>();
        WeaponHandler weaponHandler = root.AddComponent<WeaponHandler>();
        controller.EquipmentManager = fixture.m_equipmentFixture.Manager;
        controller.WeaponHandler = weaponHandler;

        GameObject model = new GameObject("PlayerModel");
        model.transform.SetParent(root.transform);
        Animator animator = model.AddComponent<Animator>();
        fixture.StateMachine = model.AddComponent<PlayerStateMachine>();
        fixture.StateMachine.ConfigureCombatForTests(controller, animator);

        fixture.EquipmentManager.SetWeaponObject(0, "GreatSword", 10);
        if (equipSecondWeapon)
        {
            fixture.EquipmentManager.SetWeaponObject(1, "SingleSword", 20);
        }
        fixture.EquipmentManager.ActivateWeapon(0);
        fixture.EquipmentManager.ApplyWeaponAppearance(false);
        return fixture;
    }

    /// <summary>销毁集成测试创建的玩家层级。</summary>
    public void Dispose()
    {
        m_equipmentFixture.Dispose();
    }
}
```

双武器测试统一使用 `PlayerCombatStateMachineFixture.Create(true)`。

- [ ] **Step 2: 运行集成测试并确认失败**

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --group-name 'PlayerCombatStanceIntegrationEditModeTests' --timeout 120000
```

Expected: FAIL，玩家状态机尚未暴露战斗姿态接口。

- [ ] **Step 3: 为 CharacterStateMachine 增加 Animator 查询接口**

在保留用户新增平滑 `SetFloat` 重载的前提下添加：

```csharp
/// <summary>按名称查找 Animator 层，缺少 Animator 或控制器时返回 -1。</summary>
public int GetAnimatorLayerIndex(string layerName)
{
    EnsureDefaultAnimatorController();
    return animator == null || animator.runtimeAnimatorController == null
        ? -1
        : animator.GetLayerIndex(layerName);
}

/// <summary>检查 Animator 是否包含指定类型的参数。</summary>
public bool HasAnimatorParameter(string parameterName, AnimatorControllerParameterType parameterType)
{
    if (animator == null)
    {
        return false;
    }

    AnimatorControllerParameter[] parameters = animator.parameters;
    for (int i = 0; i < parameters.Length; i++)
    {
        if (parameters[i].name == parameterName && parameters[i].type == parameterType)
        {
            return true;
        }
    }

    return false;
}
```

- [ ] **Step 4: 让 PlayerStateMachine 拥有并编排姿态上下文**

新增常量、字段和属性：

```csharp
private const string ArmsLayerName = "ArmsLayer";
private const string CombatParameterName = "IsCombat";
private const string EnterCombatAnimationName = "EnterCombat";
private const string ExitCombatAnimationName = "ExitCombat";

private readonly PlayerCombatStanceContext m_combatStance = new PlayerCombatStanceContext();
private int m_armsLayerIndex = -1;
private bool m_hasCombatAnimatorConfiguration;

public bool IsCombat => m_combatStance.IsCombat;
public bool HasTargetingEnemy => m_combatStance.HasTargetingEnemy;
public PlayerCombatTransitionPhase CombatTransitionPhase => m_combatStance.Phase;
public int ArmsLayerIndex => m_armsLayerIndex;
```

在 `Start` 中校验 `ArmsLayer` 和 float 参数 `IsCombat`，初始化为 0；缺少配置时记录明确错误。增加以下业务入口：

```csharp
/// <summary>不播放拔刀动画并立即进入战斗，必要时结算正在进行的换武器。</summary>
public void EnterCombatImmediately()
{
    PlayerCombatTransitionOutcome outcome = m_combatStance.SettleInterruptedTransition();
    if (outcome.ShouldSwitchWeapon)
    {
        PlayerController.EquipmentManager.ActivateWeapon(outcome.TargetWeaponIndex);
    }

    m_combatStance.EnterCombatImmediately();
    ApplyCombatAnimatorParameter();
    PlayerController.EquipmentManager.ApplyWeaponAppearance(true);
}

/// <summary>请求按当前姿态执行非战斗直接切换或战斗两段式切换。</summary>
public bool RequestWeaponSwitch()
{
    EquipmentManager equipmentManager = PlayerController.EquipmentManager;
    int targetIndex = equipmentManager.GetNextEquippedWeaponIndex();
    if (targetIndex < 0 || targetIndex == equipmentManager.ActiveWeaponIndex)
    {
        return false;
    }

    if (!IsCombat)
    {
        return equipmentManager.ActivateWeapon(targetIndex);
    }

    return m_combatStance.RequestWeaponSwitch(equipmentManager.ActiveWeaponIndex, targetIndex);
}

/// <summary>完成当前 ArmsLayer 动画阶段并执行数据切换或稳定姿态同步。</summary>
public void CompleteCombatAnimationPhase()
{
    PlayerCombatAnimationCompletion completion = m_combatStance.CompleteAnimationPhase();
    if (completion == PlayerCombatAnimationCompletion.SwitchWeaponAndEnter)
    {
        PlayerController.EquipmentManager.ActivateWeapon(m_combatStance.TargetWeaponIndex);
        return;
    }

    ApplyCombatAnimatorParameter();
    PlayerController.EquipmentManager.ApplyWeaponAppearance(IsCombat);
}

/// <summary>请求普通拔刀；没有当前武器时直接写入战斗姿态。</summary>
public bool RequestEnterCombatAnimation()
{
    if (PlayerController.EquipmentManager.ActiveWeapon == null)
    {
        m_combatStance.EnterCombatImmediately();
        ApplyCombatAnimatorParameter();
        return false;
    }

    return m_combatStance.RequestEnterCombatAnimation();
}

/// <summary>请求普通收刀；没有当前武器时直接退出战斗。</summary>
public bool RequestExitCombatAnimation()
{
    if (PlayerController.EquipmentManager.ActiveWeapon == null)
    {
        m_combatStance.ExitCombatImmediately();
        ApplyCombatAnimatorParameter();
        return false;
    }

    return m_combatStance.RequestExitCombatAnimation();
}

/// <summary>推进 Locomotion 下的自动收刀计时，达到三秒时创建收刀请求。</summary>
public void TickAutoSheath(float deltaTime)
{
    if (m_combatStance.TickAutoSheath(deltaTime, true))
    {
        RequestExitCombatAnimation();
    }
}

/// <summary>刷新攻击、技能或受击产生的战斗活动时间。</summary>
public void RefreshCombatActivity()
{
    m_combatStance.RefreshCombatActivity();
}

/// <summary>Locomotion 被其他 FSM 状态打断时结算到确定终点。</summary>
public void SettleCombatTransitionOnLocomotionExit()
{
    PlayerCombatTransitionOutcome outcome = m_combatStance.SettleInterruptedTransition();
    if (outcome.ShouldSwitchWeapon)
    {
        PlayerController.EquipmentManager.ActivateWeapon(outcome.TargetWeaponIndex);
    }

    ApplyCombatAnimatorParameter();
    PlayerController.EquipmentManager.ApplyWeaponAppearance(outcome.IsCombat);
}

/// <summary>在 Animator 配置有效时同步稳定战斗参数。</summary>
private void ApplyCombatAnimatorParameter()
{
    if (m_hasCombatAnimatorConfiguration)
    {
        SetFloat(CombatParameterName, IsCombat ? 1f : 0f);
    }
}
```

同时提供 `RequestEnterCombatAnimation`、`RequestExitCombatAnimation`、`TickAutoSheath`、`RefreshCombatActivity`、`SettleCombatTransitionOnLocomotionExit`。无 active 武器时，进入或退出请求直接写入稳定 `IsCombat`，不播放 ArmsLayer 动画。

- [ ] **Step 5: 订阅敌人目标变化并连接受击事件**

`OnEnable`/`OnDisable` 同时订阅和解除 `EnemyCombatTargetChangedEventArgs.EventId`。事件处理规则：

- `NewTarget` 是玩家：按敌人 instance ID 加入集合；非战斗时请求动画拔刀。
- `OldTarget` 是玩家且新目标不是玩家：移除该敌人。
- 普通收刀过程中重新被敌人锁定：先结算收刀，再请求动画拔刀。
- `HandleTargetCombatEvent` 入口先 `RefreshCombatActivity()`；收刀过程中受击则 `EnterCombatImmediately()`，然后继续现有死亡/失衡/格挡/受击优先级。

事件处理使用以下单一入口：

```csharp
/// <summary>根据敌人目标变化维护锁定集合，并触发玩家拔刀请求。</summary>
private void HandleEnemyCombatTargetChanged(object sender, EventArgsBase eventArgs)
{
    EnemyCombatTargetChangedEventArgs targetChanged =
        eventArgs as EnemyCombatTargetChangedEventArgs;
    if (targetChanged == null || targetChanged.Enemy == null || playerController == null)
    {
        return;
    }

    Transform playerTransform = playerController.transform;
    int enemyId = targetChanged.Enemy.GetInstanceID();
    bool wasTargetingPlayer = targetChanged.OldTarget == playerTransform;
    bool isTargetingPlayer = targetChanged.NewTarget == playerTransform;

    if (wasTargetingPlayer && !isTargetingPlayer)
    {
        m_combatStance.SetEnemyTargeting(enemyId, false);
    }

    if (!isTargetingPlayer)
    {
        return;
    }

    m_combatStance.SetEnemyTargeting(enemyId, true);
    if (m_combatStance.Phase == PlayerCombatTransitionPhase.ExitingCombat)
    {
        SettleCombatTransitionOnLocomotionExit();
    }
    RequestEnterCombatAnimation();
}
```

为测试增加：

```csharp
#if UNITY_EDITOR
/// <summary>注入 EditMode 集成测试使用的玩家控制器和 Animator。</summary>
public void ConfigureCombatForTests(PlayerController controller, Animator testAnimator)
{
    playerController = controller;
    animator = testAnimator;
    m_armsLayerIndex = 0;
    m_hasCombatAnimatorConfiguration = false;
}

/// <summary>直接处理 EditMode 测试构造的敌人目标变化事件。</summary>
public void HandleEnemyTargetChangedForTests(EnemyCombatTargetChangedEventArgs eventArgs)
{
    HandleEnemyCombatTargetChanged(this, eventArgs);
}
#endif
```

- [ ] **Step 6: 增加玩家动画事件入口**

```csharp
/// <summary>EnterCombat 动画事件：把当前或目标武器切到手持表现。</summary>
public void OnEnterCombatWeaponEvent()
{
    int slotIndex = PlayerController.EquipmentManager.ActiveWeaponIndex;
    PlayerController.EquipmentManager.ShowWeaponInHand(slotIndex);
}

/// <summary>ExitCombat 动画事件：把当前或源武器切到收纳表现。</summary>
public void OnExitCombatWeaponEvent()
{
    int slotIndex = m_combatStance.Phase == PlayerCombatTransitionPhase.SwitchingWeaponExit
        ? m_combatStance.SourceWeaponIndex
        : PlayerController.EquipmentManager.ActiveWeaponIndex;
    PlayerController.EquipmentManager.ShowWeaponSheathed(slotIndex);
}
```

动画结束和中断结算仍会调用 `ApplyWeaponAppearance`，因此事件缺失、重复或动画未播完都能回到最终表现。

- [ ] **Step 7: 运行玩家集成、受击和属性回归测试**

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --group-name 'PlayerCombatStanceIntegrationEditModeTests|CombatAbilityDamageEditModeTests|PlayerAttributeSetEditModeTests' --timeout 120000
```

Expected: 全部 PASS；死亡、失衡、格挡和普通受击优先级不变。

- [ ] **Step 8: 提交玩家状态机集成**

```powershell
git add -- Assets/Game/Character/CharacterStateMachine.cs Assets/Game/Character/Player/PlayerStateMachine.cs Assets/Game/Editor/PlayerCombatStanceIntegrationEditModeTests.cs Assets/Game/Editor/Support/PlayerCombatStateMachineFixture.cs
git commit -m "集成玩家战斗姿态与动画事件"
```

---

### Task 6: 在 LocomotionState 推进动画并移除旧换武器状态

**Files:**
- Modify: `Assets/Game/Character/Player/PlayerFsm/LocomotionState.cs:10-61`
- Modify: `Assets/Game/Character/Player/PlayerFsm/PlayerStateBase.cs:13-109,168-195`
- Modify: `Assets/Game/Character/Player/PlayerFsm/DefenceState.cs:59-86`
- Modify: `Assets/Game/Character/Player/PlayerStateMachine.cs:170-190`
- Modify: `Assets/Game/Character/Player/PlayerDefine.cs:3-15`
- Delete: `Assets/Game/Character/Player/PlayerFsm/WeaponSwitchState.cs`
- Delete: `Assets/Game/Character/Player/PlayerFsm/WeaponSwitchState.cs.meta`
- Test: `Assets/Game/Editor/PlayerCombatStanceIntegrationEditModeTests.cs`

- [ ] **Step 1: 扩充输入与阶段推进测试**

在集成测试中增加以下断言：

- 有效攻击/技能调用 `EnterCombatImmediately` 后才进入动作状态。
- 无效技能不改变 `IsCombat`。
- 非战斗 Tab 只改变 active 槽。
- 战斗 Tab 先进入 `SwitchingWeaponExit`，旧动画完成前 active 槽不变。
- `SwitchingWeaponExit` 完成后 active 槽变化并进入 `SwitchingWeaponEnter`。
- `LocomotionState.Exit` 在四种过渡阶段都调用中断结算。

```csharp
/// <summary>战斗换武器旧收刀完成前不切换 active 数据。</summary>
[Test]
public void RequestWeaponSwitch_InCombat_WaitsForExitAnimationCompletion()
{
    PlayerCombatStateMachineFixture fixture = PlayerCombatStateMachineFixture.Create(true);
    fixture.StateMachine.EnterCombatImmediately();
    int oldIndex = fixture.EquipmentManager.ActiveWeaponIndex;

    Assert.IsTrue(fixture.StateMachine.RequestWeaponSwitch());

    Assert.AreEqual(oldIndex, fixture.EquipmentManager.ActiveWeaponIndex);
    Assert.AreEqual(
        PlayerCombatTransitionPhase.SwitchingWeaponExit,
        fixture.StateMachine.CombatTransitionPhase);
    fixture.Dispose();
}
```

- [ ] **Step 2: 运行扩充测试并确认当前流程失败**

Run:

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --group-name 'PlayerCombatStanceIntegrationEditModeTests' --timeout 120000
```

Expected: 战斗换武器仍依赖 `WeaponSwitchState`，阶段断言失败。

- [ ] **Step 3: 修改 PlayerStateBase 的攻击、技能和换武器入口**

在有效攻击和技能所有预检通过后、写入动作请求前调用 `fsm.Owner.EnterCombatImmediately()`。无效输入保持原状态。

```csharp
/// <summary>写入武器类型和技能 ID，直接进入战斗后切换到普通攻击状态。</summary>
private bool TryEnterAttackState(FsmBase<PlayerStateMachine> fsm, WeaponData activeWeapon, int skillId)
{
    if (activeWeapon == null || skillId <= 0)
    {
        return false;
    }

    fsm.Owner.EnterCombatImmediately();
    fsm.Owner.RefreshCombatActivity();
    fsm.Owner.SetCombatActionRequest(activeWeapon.weaponType, skillId);
    fsm.ChangeState<AttackState>();
    return true;
}

/// <summary>消费换武器输入并把合法请求交给玩家战斗姿态流程。</summary>
protected bool TrySwitchWeapon(FsmBase<PlayerStateMachine> fsm)
{
    if (!InputManager.Instance.IsWeaponSwitchKeyPressed())
    {
        return false;
    }

    fsm.Owner.RequestWeaponSwitch();
    return true;
}
```

战斗换武器阶段继续保持旧 `WeaponSwitchState` 的输入锁定行为：在 `SwitchingWeaponExit` 或 `SwitchingWeaponEnter` 期间不处理攻击、技能、防御、跳跃和翻滚输入，避免动作使用半切换数据。普通 `ExitingCombat` 阶段仍优先处理攻击和技能，以满足收刀中直接入战。

- [ ] **Step 4: 在 LocomotionState 的 ArmsLayer 推进阶段**

增加状态动画名称与当前已启动阶段字段。主循环保持用户已有移动平滑代码，只插入姿态处理：

```csharp
private const string EnterCombatAnimation = "EnterCombat";
private const string ExitCombatAnimation = "ExitCombat";
private PlayerCombatTransitionPhase m_startedPhase;

/// <summary>推进 ArmsLayer 上的拔刀、收刀和战斗换武器动画。</summary>
private void UpdateCombatTransition(FsmBase<PlayerStateMachine> fsm)
{
    PlayerCombatTransitionPhase phase = fsm.Owner.CombatTransitionPhase;
    if (phase == PlayerCombatTransitionPhase.None)
    {
        m_startedPhase = PlayerCombatTransitionPhase.None;
        return;
    }

    string animationName = phase == PlayerCombatTransitionPhase.EnteringCombat
        || phase == PlayerCombatTransitionPhase.SwitchingWeaponEnter
        ? EnterCombatAnimation
        : ExitCombatAnimation;

    if (m_startedPhase != phase)
    {
        if (!fsm.Owner.TryCrossFadeInFixedTime(animationName, 0.1f, 0f, fsm.Owner.ArmsLayerIndex))
        {
            fsm.Owner.CompleteCombatAnimationPhase();
        }
        else
        {
            m_startedPhase = phase;
        }
        return;
    }

    if (!fsm.Owner.IsPlayingAnimation(animationName, out float progress, fsm.Owner.ArmsLayerIndex)
        || progress >= 1f)
    {
        fsm.Owner.CompleteCombatAnimationPhase();
        m_startedPhase = PlayerCombatTransitionPhase.None;
    }
}
```

`Update` 顺序固定为：

1. 若不是战斗换武器阶段，处理高优先级动作输入。
2. 根据敌人锁定请求普通拔刀。
3. 推进 3 秒自动收刀计时并请求普通收刀。
4. 推进 ArmsLayer 动画阶段。
5. 更新原有移动参数、朝向、翻滚和 Root Motion。

`Exit` 在取消 Root Motion 订阅前调用 `fsm.Owner.SettleCombatTransitionOnLocomotionExit()`。

- [ ] **Step 5: 让 DefenceState 的换武器回到 Locomotion**

防御状态检测到 Tab 后调用 `RequestWeaponSwitch`；请求被消费后切回 `LocomotionState`，由 Locomotion 负责动画。不要在 DefenceState 播放 `EnterCombat` 或 `ExitCombat`。

- [ ] **Step 6: 删除旧 WeaponSwitchState 和枚举注册**

从 `GetPlayerStates` 删除 `new WeaponSwitchState()`，从 `PlayerState` 删除 `WeaponSwitch`，删除脚本及 `.meta`。全仓精确搜索必须确认 `WeaponSwitchState`、`PlayerState.WeaponSwitch`、`WeaponSheath` 均无剩余引用。

- [ ] **Step 7: 运行集成测试与 Unity 编译**

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --group-name 'PlayerCombatStanceIntegrationEditModeTests|PlayerAttackInputEventEditModeTests|CombatAbilityDamageEditModeTests' --timeout 120000
& '.\.aibridge\cli\AIBridgeCLI.exe' compile unity
```

Expected: EditMode 测试 failed 为 0；Unity 编译 `success: true` 且 compiler errors 为 0。

- [ ] **Step 8: 提交 Locomotion 流程与旧状态清理**

```powershell
git add -- Assets/Game/Character/Player/PlayerFsm/LocomotionState.cs Assets/Game/Character/Player/PlayerFsm/PlayerStateBase.cs Assets/Game/Character/Player/PlayerFsm/DefenceState.cs Assets/Game/Character/Player/PlayerStateMachine.cs Assets/Game/Character/Player/PlayerDefine.cs Assets/Game/Character/Player/PlayerFsm/WeaponSwitchState.cs Assets/Game/Character/Player/PlayerFsm/WeaponSwitchState.cs.meta Assets/Game/Editor/PlayerCombatStanceIntegrationEditModeTests.cs
git commit -m "在移动状态集成拔刀收刀与换武器"
```

---

### Task 7: 配置 Animator、动画事件和 Scene1 武器对象

**Files:**
- Modify: `Assets/Res/AnimatorController/Player/Player.controller`
- Modify: `Assets/Res/AnimatorController/Player/GreatSword.overrideController`
- Modify: `Assets/Res/AnimatorController/Player/SingleSword.overrideController`
- Modify: `Assets/fighting/Kevin Iglesias/Human Animations/Animations/Male/Misc/Unsheathe/HumanM@UnsheatheBack01_R.fbx.meta`
- Modify: `Assets/fighting/Kevin Iglesias/Human Animations/Animations/Male/Misc/Unsheathe/HumanM@SheatheBack01_R.fbx.meta`
- Modify: `Assets/fighting/Kevin Iglesias/Human Animations/Animations/Male/Misc/Unsheathe/HumanM@UnsheatheHips01_R.fbx.meta`
- Modify: `Assets/fighting/Kevin Iglesias/Human Animations/Animations/Male/Misc/Unsheathe/HumanM@SheatheHips01_R.fbx.meta`
- Modify: `Assets/Scenes/Scene1.unity`

- [ ] **Step 1: 先编译脚本并确认新的序列化类型可用**

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' compile unity
```

Expected: `success: true`，Unity 不处于 compiling 状态，compiler errors 为 0。

- [ ] **Step 2: 配置 Player.controller**

通过 Unity AnimatorController API 或 Editor Inspector 完成以下精确配置：

- 新增 float 参数 `IsCombat`，默认值 `0`。
- 新增层 `ArmsLayer`，权重 `1`，Blending 为 Override。
- AvatarMask 使用 `Assets/Res/AvatarMask/Player/PlayerUpporBodyMask.mask`。
- 层内创建默认空状态、`EnterCombat`、`ExitCombat`。
- `EnterCombat` 的占位 clip 使用 `HumanM@UnsheatheBack01_R`。
- `ExitCombat` 的占位 clip 使用 `HumanM@SheatheBack01_R`。
- 代码通过 CrossFade 直接进入两个状态，不添加依赖 trigger 的业务过渡。

- [ ] **Step 3: 配置两个 AnimatorOverrideController**

GreatSword：

- `EnterCombat` 占位 clip → `HumanM@UnsheatheBack01_R`
- `ExitCombat` 占位 clip → `HumanM@SheatheBack01_R`

SingleSword：

- `EnterCombat` 占位 clip → `HumanM@UnsheatheHips01_R`
- `ExitCombat` 占位 clip → `HumanM@SheatheHips01_R`

保存后重新读取两个 OverrideController，确认两项映射均非空。

- [ ] **Step 4: 添加动画事件**

在四个导入 clip 上配置事件：

- 两个 Unsheathe clip 在归一化进度 `0.55` 调用 `OnEnterCombatWeaponEvent`。
- 两个 Sheathe clip 在归一化进度 `0.45` 调用 `OnExitCombatWeaponEvent`。

使用 clip 实际长度换算事件时间：`event.time = clip.length * normalizedProgress`。事件不传 int 参数，由 `PlayerStateMachine` 根据当前过渡阶段解析槽位。

- [ ] **Step 5: 为 Player 和 PlayerPreview 建立独立双槽表现对象**

在两套角色骨骼中分别完成：

- 右手 `hand_r/Weapons` 下为槽 0、槽 1 各保留独立 GreatSword/SingleSword 手持对象。
- `spine_03` 下建立大剑收纳挂点，为两个槽各放置独立 GreatSword 收纳对象。
- `pelvis` 下建立单手剑收纳挂点，为两个槽各放置独立 SingleSword 收纳对象。
- 每个槽的 GreatSword entry 绑定对应 `WeaponData`、手持 GreatSword、背部 GreatSword。
- 每个槽的 SingleSword entry 绑定对应 `WeaponData`、手持 SingleSword、腰部 SingleSword。
- 初始状态隐藏所有手持对象；已装备槽由运行时 `ApplyCombatAppearance` 决定显示哪一个收纳对象。

挂点姿态以动画接触帧为验收基准：手持对象握把与右手对齐；大剑收纳对象不穿过背部主体；单手剑收纳对象不穿过腿部；PlayerPreview 使用与 Player 一致的局部姿态。

- [ ] **Step 6: 保存 Scene1 并检查缺失引用**

重新读取 `Player` 和 `PlayerPreview` 的 `PlayerEquipmentAppearance` 序列化属性，确认：

- `m_weaponSlots` 长度为 2。
- 每个槽均有 GreatSword 和 SingleSword 两个完整 entry。
- 16 个模型引用（2 个角色 × 2 个槽 × 2 种武器 × 手持/收纳）均非空。
- 场景保存后控制台没有 MissingReferenceException 或序列化错误。

- [ ] **Step 7: 运行 Unity 编译并提交资源**

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' compile unity
git add -- 'Assets/Res/AnimatorController/Player/Player.controller' 'Assets/Res/AnimatorController/Player/GreatSword.overrideController' 'Assets/Res/AnimatorController/Player/SingleSword.overrideController' 'Assets/fighting/Kevin Iglesias/Human Animations/Animations/Male/Misc/Unsheathe/HumanM@UnsheatheBack01_R.fbx.meta' 'Assets/fighting/Kevin Iglesias/Human Animations/Animations/Male/Misc/Unsheathe/HumanM@SheatheBack01_R.fbx.meta' 'Assets/fighting/Kevin Iglesias/Human Animations/Animations/Male/Misc/Unsheathe/HumanM@UnsheatheHips01_R.fbx.meta' 'Assets/fighting/Kevin Iglesias/Human Animations/Animations/Male/Misc/Unsheathe/HumanM@SheatheHips01_R.fbx.meta' 'Assets/Scenes/Scene1.unity'
git commit -m "配置玩家拔刀收刀动画与双槽武器模型"
```

Expected: 编译成功后再提交；提交只包含上述 Animator、动画导入设置和 Scene1。

---

### Task 8: 全量回归与 Play Mode 验收

**Files:**
- Modify only when a failure identifies a concrete defect in Tasks 1-7.

- [ ] **Step 1: 运行本功能全部 EditMode 测试**

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --group-name 'PlayerCombatStanceContextEditModeTests|EnemyCombatTargetChangedEditModeTests|PlayerEquipmentAppearanceEditModeTests|EquipmentManagerWeaponSwitchEditModeTests|PlayerCombatStanceIntegrationEditModeTests' --timeout 120000
```

Expected: total 大于 0，failed 为 0，inconclusive 为 0。

- [ ] **Step 2: 运行相关现有回归测试**

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' test run --mode EditMode --group-name 'EnemyTargetMemoryEditModeTests|EnemyTargetMemoryRuntimeEditModeTests|CombatAbilityDamageEditModeTests|PlayerAttackInputEventEditModeTests|PlayerAttributeSetEditModeTests|LockOnManagerEditModeTests' --timeout 120000
```

Expected: 所有相关既有测试 failed 为 0。

- [ ] **Step 3: 执行最终 Unity 编译**

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' compile unity
```

Expected: `success: true`、compiler errors 为 0。不得用 `compile dotnet` 替代。

- [ ] **Step 4: Play Mode 验证装备与非战斗切换**

在 `Assets/Scenes/Scene1.unity` 进入 Play Mode，依次验证：

1. 装备两把武器后，两把都显示在各自收纳位。
2. 按 Tab，active 槽、技能和 AnimatorOverride 改变，但模型显隐不变。
3. 卸下任一武器，对应手持和收纳模型同时隐藏。
4. 卸下 active 武器，备用武器立即成为 active；非战斗保持收纳。

- [ ] **Step 5: Play Mode 验证战斗姿态和计时**

1. 让一个敌人的 `CombatTarget` 指向玩家，确认播放 `EnterCombat`，事件帧切换模型，结束后 `IsCombat = 1`。
2. 无敌人锁定后保持 Locomotion，确认第 3 秒播放 `ExitCombat`，结束后 `IsCombat = 0`。
3. 计时期间攻击、技能或受击，确认计时归零。
4. 使用两个敌人锁定玩家，依次释放，确认第一个释放后不计时，第二个释放后才开始计时。

- [ ] **Step 6: Play Mode 验证直接入战和中断**

1. 非战斗按攻击和三个技能键，确认不播放 `EnterCombat`，武器直接在手并进入动作状态。
2. 普通收刀过程中按攻击或技能，确认直接恢复 `IsCombat = 1`。
3. 普通拔刀、普通收刀、换武器旧收刀、换武器新拔刀四个阶段分别触发受击。
4. 每次中断后检查 active 槽、AnimatorOverride、技能、命中检测器、`IsCombat` 和模型显隐完全一致。

- [ ] **Step 7: Play Mode 验证战斗中换武器**

确认顺序严格为：

```text
武器1 ExitCombat
→ OnExitCombatWeaponEvent 收起武器1
→ active 数据与 AnimatorOverride 切到武器2
→ 武器2 EnterCombat
→ OnEnterCombatWeaponEvent 拔出武器2
→ IsCombat 全程保持 1
```

换武器两个动画阶段内的攻击、技能、防御、跳跃和翻滚输入应保持旧 `WeaponSwitchState` 的锁定效果，不触发半切换动作。

- [ ] **Step 8: 检查控制台并提交最终修正**

```powershell
& '.\.aibridge\cli\AIBridgeCLI.exe' get_logs --level error --limit 200
git status --short
```

Expected: 没有与本功能相关的 Error、Exception、MissingReference；工作区只保留用户原有改动或本任务已明确提交的内容。若发现失败，回到引入该行为的 Task，先补充失败测试，再修改对应文件并使用该 Task 的中文提交信息提交修正。

---

## 需求覆盖索引

- 装备显示、卸下隐藏：Task 3、Task 4、Task 7、Task 8。
- 非战斗切换只改数据：Task 4、Task 6、Task 8。
- 战斗两段式换武器：Task 1、Task 5、Task 6、Task 8。
- Locomotion + ArmsLayer：Task 5、Task 6、Task 7。
- 攻击/技能直接入战：Task 5、Task 6、Task 8。
- 敌人锁定触发拔刀：Task 2、Task 5、Task 8。
- 3 秒自动收刀与活动刷新：Task 1、Task 5、Task 6、Task 8。
- 动画中断仍成功：Task 1、Task 5、Task 6、Task 8。
- `IsCombat` float 参数：Task 5、Task 7、Task 8。
- 收刀中攻击/技能直接恢复战斗：Task 5、Task 6、Task 8。
- 两个武器动画事件：Task 5、Task 7、Task 8。
