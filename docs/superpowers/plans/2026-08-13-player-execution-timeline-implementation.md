# Player Execution Timeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 使用 Unity Timeline 实现玩家对失衡敌人的多武器处决动画、Timeline 中段百分比伤害结算、处决期间无敌和输入锁定。

**Architecture:** 每把武器在 `WeaponData` 上配置专属 `PlayableAsset` 和目标最大生命值百分比，玩家 FSM 新增 `ExecutionState` 统一托管处决生命周期。Timeline 通过运行时控制器动态绑定玩家 Animator、敌人 Animator、镜头和自定义 `ExecutionTransformTrack`，中段 `ExecutionDamageMarker` 只结算一次伤害。

**Tech Stack:** Unity 2022.3.61f1c1, C# 9.0, Unity Timeline/Playables, Cinemachine, 现有 CombatAbilitySystem/EnemyBlackboard/PlayerStateMachine。

---

## Execution Constraints

- 不修改任何 `.controller` 文件。
- 不新增测试文件或测试代码。
- 所有新增或修改函数必须写简体中文用途注释。
- 复杂业务逻辑只写必要简体中文注释，不吞异常，不过度防御，强类型，fast fails。
- 尊重现有用户改动：不要回滚或提交 `Assets/Scenes/Scene1.unity`。
- 验证 Unity 编译只能使用：`$CLI compile unity`。

## File Structure

- Modify: `Assets/Game/Character/Player/Equipment/WeaponData.cs`
  - 保存武器专属处决 Timeline 和目标最大生命值伤害百分比。
- Modify: `Assets/Game/UI/Core/UIManager.cs`
  - 增加外部玩法输入阻断计数，让处决期间除暂停快捷键外全部玩法输入失效。
- Modify: `Assets/Game/Character/Player/LockOnManager.cs`
  - 复用现有锁定范围、前方、无遮挡过滤，提供失衡敌人选择入口。
- Modify: `Assets/Game/Character/Player/PlayerDefine.cs`
  - 在枚举末尾新增 `Execution`，只作为当前状态可读标记。
- Modify: `Assets/Game/Character/Player/PlayerStateMachine.cs`
  - 缓存/暴露 `PlayerExecutionController`，注册 `ExecutionState`，在执行中屏蔽默认攻击事件。
- Modify: `Assets/Game/Character/Player/PlayerFsm/PlayerStateBase.cs`
  - 普攻击键先尝试处决；无有效目标才走普通攻击。
- Create: `Assets/Game/Character/Player/PlayerFsm/ExecutionState.cs`
  - 玩家 FSM 处决状态，等待 Timeline 完成后回到 Locomotion。
- Create: `Assets/Game/Character/Player/Execution/ExecutionStartResult.cs`
  - 区分无目标、已开始、配置失败三种触发结果。
- Create: `Assets/Game/Character/Player/Execution/ExecutionTarget.cs`
  - 强类型保存敌人根节点、Animator、属性、能力系统、AI 控制器等运行时引用。
- Create: `Assets/Game/Character/Player/Execution/PlayerExecutionController.cs`
  - 处决生命周期主控：校验配置、动态绑定 Timeline、无敌、输入锁、敌人锁、伤害 Signal、清理。
- Create: `Assets/Game/Character/Player/Execution/ExecutionTimelineBinder.cs`
  - 按 Timeline Track 名称/类型绑定玩家、敌人、Cinemachine 和 Transform 轨道。
- Create: `Assets/Game/Timeline/Execution/ExecutionDamageMarker.cs`
  - Timeline 中段伤害 Signal 标记，只触发一次处决伤害。
- Create: `Assets/Game/Timeline/Execution/ExecutionTransformTarget.cs`
  - 自定义 Transform 轨道绑定对象，保存玩家根节点和敌人根节点。
- Create: `Assets/Game/Timeline/Execution/ExecutionTransformClip.cs`
  - Clip 可配置玩家相对敌人根节点的位置、旋转、插值曲线。
- Create: `Assets/Game/Timeline/Execution/ExecutionTransformBehaviour.cs`
  - Clip 运行时缓存起始世界姿态，并按标准化进度计算目标姿态。
- Create: `Assets/Game/Timeline/Execution/ExecutionTransformMixer.cs`
  - Mixer 每帧只写一次玩家 Transform，对位 Clip 结束后停止写入。
- Create: `Assets/Game/Timeline/Execution/ExecutionTransformTrack.cs`
  - Timeline Track 声明 Clip 类型和绑定类型。
- Modify: `Assets/Game/Battle/Ability/CombatAbilitySystem.cs`
  - 新增指定数值处决伤害入口，并继续发布 `CombatEvent`。
- Modify: `Assets/Game/Character/Enemy/Components/EnemyLifeComponent.cs`
  - 允许处决伤害事件 `Skill == null` 时仍能走死亡数据流程。
- Modify: `Assets/Game/Character/Enemy/AI/AIController.cs`
  - 增加处决锁；锁定时只允许死亡/失衡分支推进，不刷新感知和普通决策。
- Modify: `Assets/Game/Character/Enemy/Components/EnemyMovementComponent.cs`
  - 增加处决锁，锁定时停止移动并暂停 Tick 位移。
- Modify: `Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs`
  - 增加处决锁，锁定时中断当前动作并拒绝新攻击/防御。
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySetIntentNodeAsset.cs`
  - 失衡 Loop 在处决锁期间冻结剩余时间，恢复后从原剩余时间继续。

---

### Task 1: Weapon Execution Config

**Files:**
- Modify: `Assets/Game/Character/Player/Equipment/WeaponData.cs`

- [ ] **Step 1: Add Timeline namespace**

Add near existing using statements:

```csharp
using UnityEngine.Playables;
```

- [ ] **Step 2: Add serialized execution fields after `defenceCounterSkillId`**

```csharp
        [Header("处决配置")]
        [SerializeField] private PlayableAsset executionTimeline;
        [SerializeField, Range(0f, 1f)] private float executionMaxHealthDamagePercent = 0.35f;
```

- [ ] **Step 3: Add public getters after `GetDefenceCounterSkillId()`**

```csharp
        /// <summary>获取当前武器专属处决 Timeline，缺失时由处决控制器直接报错。</summary>
        public PlayableAsset GetExecutionTimeline()
        {
            return executionTimeline;
        }

        /// <summary>获取处决伤害占目标最大生命值的百分比。</summary>
        public float GetExecutionMaxHealthDamagePercent()
        {
            return executionMaxHealthDamagePercent;
        }
```

- [ ] **Step 4: Do not add fallback Timeline**

Expected behavior: `executionTimeline == null` means this weapon配置错误，处决入口必须报错并消费本次普攻输入，不进入普通攻击。

---

### Task 2: External Gameplay Input Block

**Files:**
- Modify: `Assets/Game/UI/Core/UIManager.cs`

- [ ] **Step 1: Add lock count field near UI runtime state fields**

```csharp
        // 外部系统临时阻断玩法输入；暂停快捷键不走该入口，因此仍可响应。
        private int m_externalGameplayInputBlockCount;
```

- [ ] **Step 2: Add push/pop methods after `IsPanelOpen()`**

```csharp
        /// <summary>增加一次外部玩法输入阻断，处决等强控流程用它屏蔽移动、攻击、锁定和视角输入。</summary>
        public void PushGameplayInputBlock()
        {
            m_externalGameplayInputBlockCount++;
        }

        /// <summary>释放一次外部玩法输入阻断，调用次数必须和 PushGameplayInputBlock 成对。</summary>
        public void PopGameplayInputBlock()
        {
            m_externalGameplayInputBlockCount--;
            if (m_externalGameplayInputBlockCount < 0)
            {
                Debug.LogError("玩法输入阻断释放次数超过获取次数。", this);
                m_externalGameplayInputBlockCount = 0;
            }
        }
```

- [ ] **Step 3: Update `IsGameplayInputBlocked()` first branch**

```csharp
        public bool IsGameplayInputBlocked()
        {
            if (m_externalGameplayInputBlockCount > 0)
            {
                return true;
            }

            EnsurePanelDefinitions();
            ...
        }
```

- [ ] **Step 4: Preserve pause shortcut behavior**

Do not change `InputManager.IsKeyPressed(KeyCode key)` because `UIManager.HandleShortcuts()` uses it for pause and it intentionally bypasses gameplay input blocking.

---

### Task 3: Execution Target Selection

**Files:**
- Create: `Assets/Game/Character/Player/Execution/ExecutionTarget.cs`
- Create: `Assets/Game/Character/Player/Execution/ExecutionStartResult.cs`
- Modify: `Assets/Game/Character/Player/LockOnManager.cs`

- [ ] **Step 1: Create `ExecutionStartResult.cs`**

```csharp
namespace Game.Character.Player.Execution
{
    public enum ExecutionStartResult
    {
        NotFound,
        Started,
        Failed
    }
}
```

- [ ] **Step 2: Create `ExecutionTarget.cs`**

```csharp
using Game.Battle.Ability;
using Game.Character.Enemy.AI;
using Game.Character.Enemy.Components;
using Game.Character.Enemy.Core;
using UnityEngine;

namespace Game.Character.Player.Execution
{
    public readonly struct ExecutionTarget
    {
        public readonly EnemyAgent Agent;
        public readonly Transform Root;
        public readonly Animator Animator;
        public readonly AIController AIController;
        public readonly EnemyMovementComponent Movement;
        public readonly EnemyCombatComponent Combat;
        public readonly EnemyAttributeComponent Attribute;
        public readonly CombatAbilitySystem AbilitySystem;

        /// <summary>保存一次处决目标所需的敌人运行时组件引用。</summary>
        public ExecutionTarget(
            EnemyAgent agent,
            Transform root,
            Animator animator,
            AIController aiController,
            EnemyMovementComponent movement,
            EnemyCombatComponent combat,
            EnemyAttributeComponent attribute,
            CombatAbilitySystem abilitySystem)
        {
            Agent = agent;
            Root = root;
            Animator = animator;
            AIController = aiController;
            Movement = movement;
            Combat = combat;
            Attribute = attribute;
            AbilitySystem = abilitySystem;
        }

        /// <summary>判断目标是否仍处于可处决的失衡且未死亡状态。</summary>
        public bool IsValidUnbalancedTarget()
        {
            return Agent != null
                && Root != null
                && Attribute != null
                && AbilitySystem != null
                && AIController != null
                && AIController.Blackboard != null
                && AIController.Blackboard.IsUnbalanced
                && !Attribute.IsDead;
        }
    }
}
```

- [ ] **Step 3: Add `using Game.Character.Player.Execution;` to `LockOnManager.cs`**

- [ ] **Step 4: Add public target methods after `TurnToCurrentTarget()`**

```csharp
        /// <summary>优先从当前锁定对象解析可处决敌人。</summary>
        public bool TryGetLockedExecutionTarget(float executionRange, out ExecutionTarget target)
        {
            target = default;
            if (!IsLockedOn || CurrentTarget == null)
            {
                return false;
            }

            if (Vector3.Distance(transform.position, CurrentTarget.position) > executionRange)
            {
                return false;
            }

            return TryBuildExecutionTarget(CurrentTarget, out target) && target.IsValidUnbalancedTarget();
        }

        /// <summary>未锁定时查找范围内最近、前方、无遮挡且失衡的敌人。</summary>
        public bool TryFindNearestExecutionTarget(float executionRange, out ExecutionTarget target)
        {
            target = default;
            List<Transform> candidates = GetValidTargets();
            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < candidates.Count; i++)
            {
                Transform candidate = candidates[i];
                float distance = Vector3.Distance(transform.position, candidate.position);
                if (distance > executionRange || distance >= bestDistance)
                {
                    continue;
                }

                if (!TryBuildExecutionTarget(candidate, out ExecutionTarget candidateTarget)
                    || !candidateTarget.IsValidUnbalancedTarget())
                {
                    continue;
                }

                bestDistance = distance;
                target = candidateTarget;
            }

            return target.Agent != null;
        }
```

- [ ] **Step 5: Add private builder near existing enemy helper methods**

```csharp
        /// <summary>把锁定点或敌人子节点解析为处决目标组件集合。</summary>
        private static bool TryBuildExecutionTarget(Transform source, out ExecutionTarget target)
        {
            target = default;
            if (source == null)
            {
                return false;
            }

            EnemyAgent agent = source.GetComponentInParent<EnemyAgent>();
            if (agent == null)
            {
                return false;
            }

            Transform root = agent.transform;
            target = new ExecutionTarget(
                agent,
                root,
                root.GetComponentInChildren<Animator>(),
                root.GetComponent<AIController>(),
                root.GetComponent<EnemyMovementComponent>(),
                root.GetComponent<EnemyCombatComponent>(),
                root.GetComponent<EnemyAttributeComponent>(),
                root.GetComponent<CombatAbilitySystem>());
            return true;
        }
```

---

### Task 4: Timeline Transform Track

**Files:**
- Create: `Assets/Game/Timeline/Execution/ExecutionTransformTarget.cs`
- Create: `Assets/Game/Timeline/Execution/ExecutionTransformBehaviour.cs`
- Create: `Assets/Game/Timeline/Execution/ExecutionTransformClip.cs`
- Create: `Assets/Game/Timeline/Execution/ExecutionTransformMixer.cs`
- Create: `Assets/Game/Timeline/Execution/ExecutionTransformTrack.cs`

- [ ] **Step 1: Create `ExecutionTransformTarget.cs`**

```csharp
using UnityEngine;

namespace Game.Timeline.Execution
{
    public sealed class ExecutionTransformTarget : MonoBehaviour
    {
        public Transform ActorRoot { get; private set; }
        public Transform TargetRoot { get; private set; }

        /// <summary>绑定本次处决对位需要写入的玩家根节点和参考敌人根节点。</summary>
        public void Bind(Transform actorRoot, Transform targetRoot)
        {
            ActorRoot = actorRoot;
            TargetRoot = targetRoot;
        }

        /// <summary>清理对位绑定，Timeline 停止后不再写入 Transform。</summary>
        public void Clear()
        {
            ActorRoot = null;
            TargetRoot = null;
        }

        /// <summary>把敌人局部空间姿态转换到世界空间并写给玩家根节点。</summary>
        public void ApplyWorldPose(Vector3 worldPosition, Quaternion worldRotation)
        {
            ActorRoot.SetPositionAndRotation(worldPosition, worldRotation);
        }
    }
}
```

- [ ] **Step 2: Create `ExecutionTransformBehaviour.cs`**

```csharp
using UnityEngine;
using UnityEngine.Playables;

namespace Game.Timeline.Execution
{
    public sealed class ExecutionTransformBehaviour : PlayableBehaviour
    {
        public Vector3 LocalPosition;
        public Vector3 LocalEulerAngles;
        public AnimationCurve PositionCurve;
        public AnimationCurve RotationCurve;

        private bool m_hasCapturedStartPose;
        private Vector3 m_startPosition;
        private Quaternion m_startRotation;

        /// <summary>开始播放 Clip 时清空起始姿态缓存，下一帧按真实当前位置捕获。</summary>
        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            m_hasCapturedStartPose = false;
        }

        /// <summary>按 Clip 标准化进度计算玩家应该到达的世界姿态。</summary>
        public bool TryEvaluatePose(
            Playable playable,
            ExecutionTransformTarget binding,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (binding == null || binding.ActorRoot == null || binding.TargetRoot == null)
            {
                return false;
            }

            if (!m_hasCapturedStartPose)
            {
                m_startPosition = binding.ActorRoot.position;
                m_startRotation = binding.ActorRoot.rotation;
                m_hasCapturedStartPose = true;
            }

            double duration = playable.GetDuration();
            float normalizedTime = duration <= 0d ? 1f : Mathf.Clamp01((float)(playable.GetTime() / duration));
            float positionT = PositionCurve == null ? normalizedTime : PositionCurve.Evaluate(normalizedTime);
            float rotationT = RotationCurve == null ? normalizedTime : RotationCurve.Evaluate(normalizedTime);
            Vector3 targetPosition = binding.TargetRoot.TransformPoint(LocalPosition);
            Quaternion targetRotation = binding.TargetRoot.rotation * Quaternion.Euler(LocalEulerAngles);

            position = Vector3.LerpUnclamped(m_startPosition, targetPosition, positionT);
            rotation = Quaternion.SlerpUnclamped(m_startRotation, targetRotation, rotationT);
            return true;
        }
    }
}
```

- [ ] **Step 3: Create `ExecutionTransformClip.cs`**

```csharp
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Game.Timeline.Execution
{
    public sealed class ExecutionTransformClip : PlayableAsset, ITimelineClipAsset
    {
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Vector3 localEulerAngles;
        [SerializeField] private AnimationCurve positionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private AnimationCurve rotationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public ClipCaps clipCaps => ClipCaps.Blending;

        /// <summary>创建处决对位 Playable，并把 Clip 配置写入运行时 Behaviour。</summary>
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<ExecutionTransformBehaviour> playable = ScriptPlayable<ExecutionTransformBehaviour>.Create(graph);
            ExecutionTransformBehaviour behaviour = playable.GetBehaviour();
            behaviour.LocalPosition = localPosition;
            behaviour.LocalEulerAngles = localEulerAngles;
            behaviour.PositionCurve = positionCurve;
            behaviour.RotationCurve = rotationCurve;
            return playable;
        }
    }
}
```

- [ ] **Step 4: Create `ExecutionTransformMixer.cs`**

```csharp
using UnityEngine;
using UnityEngine.Playables;

namespace Game.Timeline.Execution
{
    public sealed class ExecutionTransformMixer : PlayableBehaviour
    {
        private ExecutionTransformTarget m_binding;

        /// <summary>保存 Timeline Track 的绑定对象，后续每帧用它写入玩家根节点。</summary>
        public void Bind(ExecutionTransformTarget binding)
        {
            m_binding = binding;
        }

        /// <summary>混合所有处决对位 Clip，并保证每帧最多写一次玩家 Transform。</summary>
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            ExecutionTransformTarget binding = playerData as ExecutionTransformTarget ?? m_binding;
            if (binding == null)
            {
                return;
            }

            float totalWeight = 0f;
            Vector3 blendedPosition = Vector3.zero;
            Quaternion blendedRotation = Quaternion.identity;
            bool hasRotation = false;

            int inputCount = playable.GetInputCount();
            for (int i = 0; i < inputCount; i++)
            {
                float weight = playable.GetInputWeight(i);
                if (weight <= 0f)
                {
                    continue;
                }

                ScriptPlayable<ExecutionTransformBehaviour> input = (ScriptPlayable<ExecutionTransformBehaviour>)playable.GetInput(i);
                ExecutionTransformBehaviour behaviour = input.GetBehaviour();
                if (!behaviour.TryEvaluatePose(input, binding, out Vector3 position, out Quaternion rotation))
                {
                    continue;
                }

                blendedPosition += position * weight;
                blendedRotation = hasRotation ? Quaternion.Slerp(blendedRotation, rotation, weight) : rotation;
                hasRotation = true;
                totalWeight += weight;
            }

            if (totalWeight <= 0f)
            {
                return;
            }

            binding.ApplyWorldPose(blendedPosition / totalWeight, blendedRotation);
        }
    }
}
```

- [ ] **Step 5: Create `ExecutionTransformTrack.cs`**

```csharp
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Game.Timeline.Execution
{
    [TrackColor(0.45f, 0.8f, 1f)]
    [TrackClipType(typeof(ExecutionTransformClip))]
    [TrackBindingType(typeof(ExecutionTransformTarget))]
    public sealed class ExecutionTransformTrack : TrackAsset
    {
        /// <summary>创建处决 Transform Mixer，并把 Track 绑定对象传给运行时混合器。</summary>
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            ScriptPlayable<ExecutionTransformMixer> playable = ScriptPlayable<ExecutionTransformMixer>.Create(graph, inputCount);
            ExecutionTransformTarget binding = go != null ? go.GetComponent<ExecutionTransformTarget>() : null;
            playable.GetBehaviour().Bind(binding);
            return playable;
        }
    }
}
```

---

### Task 5: Timeline Damage Marker

**Files:**
- Create: `Assets/Game/Timeline/Execution/ExecutionDamageMarker.cs`

- [ ] **Step 1: Create notification marker**

```csharp
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Game.Timeline.Execution
{
    public sealed class ExecutionDamageMarker : Marker, INotification, INotificationOptionProvider
    {
        public PropertyName id => new PropertyName(nameof(ExecutionDamageMarker));
        public NotificationFlags flags => NotificationFlags.TriggerOnce;
    }
}
```

Expected Timeline authoring: 在每个武器专属 Timeline 中段添加一次 `ExecutionDamageMarker`；不添加则该 Timeline 不会造成处决伤害，执行时通过日志暴露配置问题。

---

### Task 6: Explicit Execution Damage Settlement

**Files:**
- Modify: `Assets/Game/Battle/Ability/CombatAbilitySystem.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyLifeComponent.cs`

- [ ] **Step 1: Add public method after `ReportHit()`**

```csharp
        /// <summary>执行处决百分比伤害，不走普通攻击倍率和防御抵扣，但继续发布统一战斗事件。</summary>
        public void ReportExecutionDamage(CombatAbilitySystem target, int healthDamage, Vector3 hitPoint)
        {
            if (target == null || target == this || target.m_attributes == null || target.m_attributes.IsDead)
            {
                return;
            }

            if (Faction == target.Faction)
            {
                return;
            }

            int targetHealthDamage = target.m_attributes.ApplyHealthDamage(healthDamage);
            bool targetDead = target.m_attributes.IsDead;
            if (targetDead)
            {
                target.CancelActiveAbility();
            }

            CombatEvent result = CreateCombatEvent(
                CombatEventType.Hit,
                target,
                null,
                hitPoint,
                targetHealthDamage,
                targetStabilityDamage: 0,
                sourceStabilityDamage: 0,
                sourceBattleSpiritGain: 0,
                targetInterrupted: false,
                targetShouldReact: false,
                targetUnbalanced: false,
                sourceUnbalanced: false,
                targetDead: targetDead);

            EventCenter.Instance.Fire(this, result);
            CombatEffectExecutor.Execute(result);
            CombatHitStopController.Play(result);
        }
```

- [ ] **Step 2: Fix enemy death hit weight read**

Change in `EnemyLifeComponent.OnCombatEvent()`:

```csharp
            if (combatEvent.TargetDead)
            {
                SkillHitWeight hitWeight = combatEvent.Skill != null
                    ? combatEvent.Skill.HitWeight
                    : SkillHitWeight.Heavy;
                HandleDeath(hitWeight);
                return;
            }
```

---

### Task 7: Enemy Execution Lock And Unbalance Freeze

**Files:**
- Modify: `Assets/Game/Character/Enemy/AI/AIController.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyMovementComponent.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs`
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySetIntentNodeAsset.cs`

- [ ] **Step 1: Add AI execution lock fields/properties**

In `AIController` add field:

```csharp
        private int m_executionLockCount;
        public bool IsExecutionLocked => m_executionLockCount > 0;
```

- [ ] **Step 2: Add AI lock methods**

```csharp
        /// <summary>进入处决锁定，停止普通感知和战斗决策刷新。</summary>
        public void PushExecutionLock()
        {
            m_executionLockCount++;
        }

        /// <summary>释放处决锁定，允许敌人恢复失衡剩余时间或死亡分支。</summary>
        public void PopExecutionLock()
        {
            m_executionLockCount--;
            if (m_executionLockCount < 0)
            {
                Debug.LogError("敌人处决锁释放次数超过获取次数。", this);
                m_executionLockCount = 0;
            }
        }
```

- [ ] **Step 3: Gate `TickAI(float deltaTime)`**

At the top of `TickAI`:

```csharp
            if (IsExecutionLocked)
            {
                if (behaviorTreeRunner != null)
                {
                    behaviorTreeRunner.Tick(deltaTime);
                }

                return;
            }
```

- [ ] **Step 4: Add movement lock**

In `EnemyMovementComponent` add field and methods:

```csharp
        private int m_executionLockCount;
        public bool IsExecutionLocked => m_executionLockCount > 0;

        /// <summary>进入处决锁定，立即停止当前寻路和位移。</summary>
        public void PushExecutionLock()
        {
            m_executionLockCount++;
            Stop();
        }

        /// <summary>释放处决锁定，后续由 AI 根据黑板状态重新下发移动意图。</summary>
        public void PopExecutionLock()
        {
            m_executionLockCount--;
            if (m_executionLockCount < 0)
            {
                Debug.LogError("敌人移动处决锁释放次数超过获取次数。", this);
                m_executionLockCount = 0;
            }
        }
```

At the top of `Tick(float deltaTime)` after `ResolveMovementComponents();`:

```csharp
            if (IsExecutionLocked)
            {
                return;
            }
```

- [ ] **Step 5: Add combat lock**

In `EnemyCombatComponent` add field and methods:

```csharp
        private int m_executionLockCount;
        public bool IsExecutionLocked => m_executionLockCount > 0;

        /// <summary>进入处决锁定，关闭攻击、防御和武器命中体。</summary>
        public void PushExecutionLock()
        {
            m_executionLockCount++;
            StopDefense();
            InterruptAction();
        }

        /// <summary>释放处决锁定，允许行为树重新请求战斗动作。</summary>
        public void PopExecutionLock()
        {
            m_executionLockCount--;
            if (m_executionLockCount < 0)
            {
                Debug.LogError("敌人战斗处决锁释放次数超过获取次数。", this);
                m_executionLockCount = 0;
            }
        }
```

Add first branches:

```csharp
        public void StartDefense()
        {
            if (IsExecutionLocked)
            {
                return;
            }
            ...
        }

        private bool TryCast(int skillId, out SkillConfig config)
        {
            config = null;
            if (IsExecutionLocked)
            {
                return false;
            }
            ...
        }
```

- [ ] **Step 6: Freeze unbalance loop time**

In `EnemySetIntentNodeAsset.TickUnbalance()` add after dependency checks and before normal loop/end logic:

```csharp
                if (controller.IsExecutionLocked)
                {
                    PauseUnbalanceLoopTimer();
                    return BehaviorTreeStatus.Running;
                }
```

Add helper near `TickUnbalanceLoop()`:

```csharp
            /// <summary>处决期间把失衡 Loop 起始时间向后平移，使剩余失衡时间保持不变。</summary>
            private void PauseUnbalanceLoopTimer()
            {
                if (hasEnteredUnbalanceLoop)
                {
                    unbalanceLoopStartTime += Time.deltaTime;
                }
            }
```

Expected behavior: 处决期间 `IsUnbalanced` 保持为真；敌人存活时恢复 AI 后从原剩余失衡 Loop 时间继续；敌人死亡时黑板死亡清理覆盖失衡事实。

---

### Task 8: Player Execution Controller

**Files:**
- Create: `Assets/Game/Character/Player/Execution/PlayerExecutionController.cs`
- Create: `Assets/Game/Character/Player/Execution/ExecutionTimelineBinder.cs`

- [ ] **Step 1: Create `ExecutionTimelineBinder.cs`**

```csharp
using Cinemachine;
using Game.Timeline.Execution;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Game.Character.Player.Execution
{
    public static class ExecutionTimelineBinder
    {
        private const string PlayerTrackKey = "Player";
        private const string EnemyTrackKey = "Enemy";

        /// <summary>按 Track 类型和名称约定绑定本次处决 Timeline 的运行时对象。</summary>
        public static void Bind(
            PlayableDirector director,
            Animator playerAnimator,
            Animator enemyAnimator,
            ExecutionTransformTarget transformTarget,
            CinemachineBrain cinemachineBrain)
        {
            TimelineAsset timeline = director.playableAsset as TimelineAsset;
            if (timeline == null)
            {
                Debug.LogError("处决 Timeline 资源必须是 TimelineAsset。", director);
                return;
            }

            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track is ExecutionTransformTrack)
                {
                    director.SetGenericBinding(track, transformTarget);
                    continue;
                }

                if (track is CinemachineTrack)
                {
                    director.SetGenericBinding(track, cinemachineBrain);
                    continue;
                }

                if (track is AnimationTrack && track.name.Contains(PlayerTrackKey))
                {
                    director.SetGenericBinding(track, playerAnimator);
                    continue;
                }

                if (track is AnimationTrack && track.name.Contains(EnemyTrackKey))
                {
                    director.SetGenericBinding(track, enemyAnimator);
                }
            }
        }
    }
}
```

Timeline authoring convention: 玩家动画轨道名包含 `Player`，敌人动画轨道名包含 `Enemy`，镜头轨道使用 `CinemachineTrack`，对位轨道使用 `ExecutionTransformTrack`。

- [ ] **Step 2: Create `PlayerExecutionController.cs` skeleton**

```csharp
using Cinemachine;
using Game.Battle.Ability;
using Game.Character.Equipment;
using Game.Timeline.Execution;
using GameMain2.Scripts.Character;
using GameMain2.Scripts.UI;
using UnityEngine;
using UnityEngine.Playables;

namespace Game.Character.Player.Execution
{
    public sealed class PlayerExecutionController : MonoBehaviour, INotificationReceiver
    {
        private const string MissingTimelineError = "当前武器未配置专属处决 Timeline：";

        [SerializeField] private PlayerStateMachine stateMachine;
        [SerializeField] private PlayableDirector director;
        [SerializeField] private ExecutionTransformTarget transformTarget;
        [SerializeField] private CinemachineBrain cinemachineBrain;

        private ExecutionTarget m_target;
        private WeaponData m_weapon;
        private bool m_isPlaying;
        private bool m_damageResolved;
        private bool m_playerInputBlocked;
        private bool m_playerInvincible;
        private bool m_enemyLocked;

        public bool IsPlaying => m_isPlaying;

        /// <summary>初始化处决运行时依赖，缺失的 Director 和 Transform 绑定组件会挂到玩家对象上。</summary>
        private void Awake()
        {
            if (stateMachine == null)
            {
                TryGetComponent(out stateMachine);
            }

            if (director == null)
            {
                director = GetComponent<PlayableDirector>();
                if (director == null)
                {
                    director = gameObject.AddComponent<PlayableDirector>();
                }
            }

            if (transformTarget == null)
            {
                transformTarget = GetComponent<ExecutionTransformTarget>();
                if (transformTarget == null)
                {
                    transformTarget = gameObject.AddComponent<ExecutionTransformTarget>();
                }
            }

            if (cinemachineBrain == null && Camera.main != null)
            {
                cinemachineBrain = Camera.main.GetComponent<CinemachineBrain>();
            }

            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.GameTime;
            director.stopped += OnDirectorStopped;
        }

        /// <summary>销毁时解除 Director 事件并收束可能残留的处决状态。</summary>
        private void OnDestroy()
        {
            if (director != null)
            {
                director.stopped -= OnDirectorStopped;
            }

            CleanupExecution();
        }
    }
}
```

- [ ] **Step 3: Add start method to `PlayerExecutionController`**

```csharp
        /// <summary>尝试按当前武器和目标启动处决，配置缺失时直接报错并消费输入。</summary>
        public ExecutionStartResult TryStartExecution(ExecutionTarget target, WeaponData weapon)
        {
            if (m_isPlaying)
            {
                return ExecutionStartResult.Failed;
            }

            if (!target.IsValidUnbalancedTarget() || weapon == null)
            {
                return ExecutionStartResult.NotFound;
            }

            PlayableAsset timeline = weapon.GetExecutionTimeline();
            if (timeline == null)
            {
                Debug.LogError(MissingTimelineError + weapon.weaponType, weapon);
                return ExecutionStartResult.Failed;
            }

            m_target = target;
            m_weapon = weapon;
            m_damageResolved = false;
            m_isPlaying = true;

            stateMachine.EnterCombatImmediately();
            stateMachine.RefreshCombatActivity();
            LockPlayer();
            LockEnemy();
            BindAndPlayTimeline(timeline);
            return ExecutionStartResult.Started;
        }
```

- [ ] **Step 4: Add lock/play helpers to `PlayerExecutionController`**

```csharp
        /// <summary>给玩家添加无敌标签并阻断除暂停外的玩法输入。</summary>
        private void LockPlayer()
        {
            CombatAbilitySystem abilitySystem = stateMachine.PlayerController.AbilitySystem;
            abilitySystem.AddTag(CombatTag.Invincible);
            m_playerInvincible = true;
            UIManager.Instance.PushGameplayInputBlock();
            m_playerInputBlocked = true;
        }

        /// <summary>锁住敌人 AI、移动和战斗动作，避免处决期间被普通逻辑覆盖。</summary>
        private void LockEnemy()
        {
            m_target.AIController.PushExecutionLock();
            m_target.Movement?.PushExecutionLock();
            m_target.Combat?.PushExecutionLock();
            m_enemyLocked = true;
        }

        /// <summary>绑定 Timeline 所需对象并从头播放处决。</summary>
        private void BindAndPlayTimeline(PlayableAsset timeline)
        {
            director.playableAsset = timeline;
            transformTarget.Bind(transform, m_target.Root);
            ExecutionTimelineBinder.Bind(
                director,
                stateMachine.GetComponentInChildren<Animator>(),
                m_target.Animator,
                transformTarget,
                cinemachineBrain);
            director.time = 0d;
            director.RebuildGraph();
            director.Play();
        }
```

- [ ] **Step 5: Add notification damage method**

```csharp
        /// <summary>接收 Timeline Signal，并在处决中段只结算一次百分比伤害。</summary>
        public void OnNotify(Playable origin, INotification notification, object context)
        {
            if (!(notification is ExecutionDamageMarker) || m_damageResolved || !m_isPlaying)
            {
                return;
            }

            m_damageResolved = true;
            int damage = Mathf.CeilToInt(m_target.Attribute.MaxHealth * m_weapon.GetExecutionMaxHealthDamagePercent());
            stateMachine.PlayerController.AbilitySystem.ReportExecutionDamage(
                m_target.AbilitySystem,
                damage,
                m_target.Root.position);
        }
```

- [ ] **Step 6: Add cleanup methods**

```csharp
        /// <summary>Director 自然停止或异常停止时统一清理处决状态。</summary>
        private void OnDirectorStopped(PlayableDirector stoppedDirector)
        {
            if (stoppedDirector == director)
            {
                CleanupExecution();
            }
        }

        /// <summary>幂等清理玩家无敌、输入锁、敌人锁和 Timeline Transform 绑定。</summary>
        private void CleanupExecution()
        {
            if (!m_isPlaying && !m_playerInvincible && !m_playerInputBlocked && !m_enemyLocked)
            {
                return;
            }

            if (m_playerInvincible && stateMachine != null && stateMachine.PlayerController != null)
            {
                stateMachine.PlayerController.AbilitySystem.RemoveTag(CombatTag.Invincible);
                m_playerInvincible = false;
            }

            if (m_playerInputBlocked)
            {
                UIManager.Instance.PopGameplayInputBlock();
                m_playerInputBlocked = false;
            }

            if (m_enemyLocked)
            {
                if (m_target.AIController != null)
                {
                    m_target.AIController.PopExecutionLock();
                }

                m_target.Movement?.PopExecutionLock();
                m_target.Combat?.PopExecutionLock();
                m_enemyLocked = false;
            }

            if (transformTarget != null)
            {
                transformTarget.Clear();
            }

            m_target = default;
            m_weapon = null;
            m_damageResolved = false;
            m_isPlaying = false;
        }
```

---

### Task 9: Player FSM Integration

**Files:**
- Modify: `Assets/Game/Character/Player/PlayerDefine.cs`
- Modify: `Assets/Game/Character/Player/PlayerStateMachine.cs`
- Modify: `Assets/Game/Character/Player/PlayerFsm/PlayerStateBase.cs`
- Create: `Assets/Game/Character/Player/PlayerFsm/ExecutionState.cs`

- [ ] **Step 1: Add enum value at end of `PlayerState`**

```csharp
        Execution,   //处决
```

- [ ] **Step 2: Add execution controller field/property to `PlayerStateMachine`**

```csharp
        [SerializeField] private PlayerExecutionController executionController;
        public PlayerExecutionController ExecutionController => executionController;
```

Add using:

```csharp
using Game.Character.Player.Execution;
```

- [ ] **Step 3: Resolve controller in `Awake()` after `playerController` resolution**

```csharp
            if (executionController == null)
            {
                executionController = GetComponent<PlayerExecutionController>();
                if (executionController == null)
                {
                    executionController = gameObject.AddComponent<PlayerExecutionController>();
                }
            }
```

- [ ] **Step 4: Skip default attack event while executing**

At the top of `PublishDefaultAttackInputIfPressed()`:

```csharp
            if (executionController != null && executionController.IsPlaying)
            {
                return;
            }
```

- [ ] **Step 5: Register `ExecutionState` in `GetPlayerStates()`**

Add before `DeadState()`:

```csharp
                new ExecutionState(),
```

- [ ] **Step 6: Add execution attempt helper to `PlayerStateBase`**

Add using:

```csharp
using Game.Character.Player.Execution;
```

Add method before `TryStartNormalAttack()`:

```csharp
        /// <summary>普攻击键优先尝试处决失衡目标；无目标时允许后续普通攻击继续处理。</summary>
        protected ExecutionStartResult TryStartExecution(FsmBase<PlayerStateMachine> fsm)
        {
            if (fsm.Owner.ExecutionController == null || fsm.Owner.LockOnManager == null)
            {
                return ExecutionStartResult.NotFound;
            }

            WeaponData activeWeapon = GetActiveWeapon(fsm);
            float executionRange = fsm.Owner.PlayerController.DefaultAttackRange;
            if (fsm.Owner.LockOnManager.TryGetLockedExecutionTarget(executionRange, out ExecutionTarget lockedTarget))
            {
                return fsm.Owner.ExecutionController.TryStartExecution(lockedTarget, activeWeapon);
            }

            if (fsm.Owner.LockOnManager.TryFindNearestExecutionTarget(executionRange, out ExecutionTarget nearestTarget))
            {
                return fsm.Owner.ExecutionController.TryStartExecution(nearestTarget, activeWeapon);
            }

            return ExecutionStartResult.NotFound;
        }
```

- [ ] **Step 7: Update `TryHandleCombatActionInput()` attack branch**

Replace the current attack branch with:

```csharp
            if (!InputManager.Instance.IsAttackKeyPressed())
            {
                return false;
            }

            ExecutionStartResult executionResult = TryStartExecution(fsm);
            if (executionResult == ExecutionStartResult.Started)
            {
                fsm.ChangeState<ExecutionState>();
                return true;
            }

            if (executionResult == ExecutionStartResult.Failed)
            {
                return true;
            }

            TryStartNormalAttack(fsm);
            return true;
```

- [ ] **Step 8: Create `ExecutionState.cs`**

```csharp
using GameMain2.Framework.Core.FSM;
using GameMain2.Scripts.Character;

namespace Game.Character.Player.PlayerFsm
{
    public sealed class ExecutionState : PlayerStateBase
    {
        /// <summary>进入处决状态，玩家保持战斗姿态并等待 Timeline 控制表现。</summary>
        public override void Enter(FsmBase<PlayerStateMachine> fsm)
        {
            fsm.Owner.CurState = PlayerState.Execution;
            fsm.Owner.PlayerController.useGravity = false;
        }

        /// <summary>处决 Timeline 结束后回到 Locomotion，其他玩家操作由输入锁统一屏蔽。</summary>
        public override void Update(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
            if (fsm.Owner.ExecutionController == null || !fsm.Owner.ExecutionController.IsPlaying)
            {
                fsm.ChangeState<LocomotionState>();
            }
        }

        /// <summary>离开处决状态时恢复玩家重力，Timeline 清理由控制器负责幂等处理。</summary>
        public override void Exit(FsmBase<PlayerStateMachine> fsm)
        {
            fsm.Owner.PlayerController.useGravity = true;
        }
    }
}
```

---

### Task 10: Verification And Commit

**Files:**
- Verify all modified code files above.
- Do not stage `Assets/Scenes/Scene1.unity`.

- [ ] **Step 1: Search for forbidden edits**

Run:

```powershell
git status --short
```

Expected: no `.controller` files changed; `Assets/Scenes/Scene1.unity` may remain user-modified but must not be staged.

- [ ] **Step 2: Compile Unity**

Run:

```powershell
$CLI compile unity
```

Expected: Unity compile succeeds with no C# errors.

- [ ] **Step 3: Manual authoring check in Unity**

Open each weapon `WeaponData` and confirm:

- `executionTimeline` is assigned for every weapon type that can be equipped.
- `executionMaxHealthDamagePercent` is set per weapon.
- Each Timeline contains exactly one `ExecutionDamageMarker` at the intended damage frame.
- Timeline animation tracks follow naming convention: `Player...` for player, `Enemy...` for enemy.
- Timeline has one `ExecutionTransformTrack` Clip for initial alignment and no long-running Transform Clip fighting root motion after alignment.

- [ ] **Step 4: Runtime smoke check**

In Play Mode, force or create an enemy失衡 then press普攻 near it:

- Locked失衡目标优先处决。
- Unlocked时选择范围内最近、前方、无遮挡失衡敌人。
- 未拔刀时自动显示当前武器并直接进入处决。
- 处决期间移动、攻击、闪避、防御、切武器、锁定、视角输入无效。
- 暂停键仍能打开暂停菜单，并且 Timeline 随 `GameTime` 暂停。
- Signal 前中断不造成伤害；Signal 后中断不回滚伤害。
- 敌人存活时恢复 AI 并继续剩余失衡时间。
- 敌人死亡时触发现有死亡数据流程，Timeline 继续播完。
- 处决结束后玩家无敌标签和输入锁被释放。

- [ ] **Step 5: Commit only relevant files**

Run staged add with explicit paths only; do not add `Assets/Scenes/Scene1.unity`:

```powershell
git add Assets/Game/Character/Player/Equipment/WeaponData.cs Assets/Game/UI/Core/UIManager.cs Assets/Game/Character/Player/LockOnManager.cs Assets/Game/Character/Player/PlayerDefine.cs Assets/Game/Character/Player/PlayerStateMachine.cs Assets/Game/Character/Player/PlayerFsm/PlayerStateBase.cs Assets/Game/Character/Player/PlayerFsm/ExecutionState.cs Assets/Game/Character/Player/Execution Assets/Game/Timeline/Execution Assets/Game/Battle/Ability/CombatAbilitySystem.cs Assets/Game/Character/Enemy/Components/EnemyLifeComponent.cs Assets/Game/Character/Enemy/AI/AIController.cs Assets/Game/Character/Enemy/Components/EnemyMovementComponent.cs Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySetIntentNodeAsset.cs
git commit -m "实现玩家处决Timeline流程"
```

---

## Self-Review

- Spec coverage: 多武器专属 Timeline、缺失报错、目标最大生命值百分比伤害、Signal 单次结算、敌人可存活、玩家无敌、除暂停外输入锁、自动拔刀、锁定/未锁定目标优先级、敌人失衡剩余时间恢复、自定义 Transform 轨道均有任务覆盖。
- Placeholder scan: 本计划不使用 TBD/TODO/稍后实现；每个新增类型和关键修改都给出目标代码形状。
- Type consistency: 处决相关类型统一放在 `Game.Character.Player.Execution`；Timeline Track 类型统一放在 `Game.Timeline.Execution`；跨文件引用已在任务中列出 using。
- Project constraints: 计划不新增测试、不修改 `.controller`，验证命令使用 `$CLI compile unity`。
