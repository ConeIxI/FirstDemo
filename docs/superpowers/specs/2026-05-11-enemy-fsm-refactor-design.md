# 敌人行为树重构为状态机 - 设计文档

**日期**：2026-05-11
**范围**：将 Guard 敌人的行为树（BehaviourTree）实现迁移到状态机（FSM），并搭好支持多种敌人差异化扩展的通用基类与状态库。其他敌人类型本次不动。

---

## 1. 背景与动机

当前 Guard 敌人 AI 使用行为树实现（`Assets/Game/Character/Enemy/BehaviourTree/GuardBT.cs`），通过 `Blackboard` 在 BT 节点与 `EnemyMovement` / `EnemyAnimator` / `EnemyCombat` 之间传递 `IsMoving` / `IsAttacking` / `Target` / `Intention` 等共享数据。

重构动机：
- **统一架构**：玩家已使用 `FsmBase<T>` 状态机，敌人改用相同模式可降低心智负担。
- **黑板/Intention 太绕**：当前节点之间通过黑板键隐式耦合，状态边界不清晰、调试困难，希望用 FSM 的显式状态切换替代。
- **行为表达力**：Selector 每帧重评估抢占的方式导致一些细节问题（攻击中被巡逻分支拉走需靠 `IsAttacking` 锁定），FSM 的显式入/出口更适合。

---

## 2. 目标与范围

**本次范围**
- 只迁移 Guard 敌人。
- 搭建可复用的敌人 FSM 通用基类与状态库（供后续其它敌人类型按需装配）。
- 完整删除行为树相关代码（节点 + 框架层 + Blackboard）。
- 删除 `EnemyAnimator`，动画播放统一通过 `CharacterStateMachine.CrossFadeInFixedTime()` 显式驱动，与玩家一致。

**不在本次范围**
- 受击事件链路（`OnHit` API 暴露但暂不接入调用方，后续再接）。
- 其他敌人类型的 FSM 装配。
- `Dead` 死亡状态。

---

## 3. 架构总览

### 3.1 文件结构变化

```
Assets/Game/Character/Enemy/
├── EnemyController.cs              (新建)
├── EnemyStateMachine.cs            (新建，抽象基类，继承 CharacterStateMachine)
├── EnemyMovement.cs                (改造：移除 Blackboard，暴露方法)
├── EnemyCombat.cs                  (改造：移除 Blackboard，暴露方法)
├── EnemyPerception.cs              (基本不动)
├── EnemySkillManager.cs            (不动)
├── EnemyAnimator.cs                ❌ 删除
├── EnemyFsm/
│   ├── EnemyStateBase.cs           (新建，所有敌人状态基类)
│   ├── Common/                     (通用可复用状态)
│   │   ├── IdleState.cs
│   │   ├── PatrolState.cs
│   │   ├── ChaseState.cs
│   │   ├── AttackState.cs
│   │   └── GetHitState.cs
│   └── Guard/
│       └── GuardStateMachine.cs    (具体装配)
└── BehaviourTree/                  ❌ 整目录删除

Assets/Framework/Core/BehaviourTree/ ❌ 整目录删除
```

### 3.2 分层职责

- **框架通用层**：`EnemyStateMachine`（基类） + `EnemyStateBase`（状态基类，含 `TryDetectTarget` helper）
- **状态库（按需复用）**：`EnemyFsm/Common/*` 中的状态，可被多种敌人 StateMachine 子类组合
- **敌人专属装配层**：每种敌人一个 `*StateMachine` 子类，在 `GetStates()` 里挑选/扩展状态，在 `GetStartStateType()` 里指定起始状态。本次只做 `GuardStateMachine`。

---

## 4. 核心类接口

### 4.1 `EnemyStateMachine : CharacterStateMachine`

```csharp
public abstract class EnemyStateMachine : CharacterStateMachine
{
    [SerializeField] private EnemyController controller;
    [SerializeField] private EnemyMovement movement;
    [SerializeField] private EnemyCombat combat;
    [SerializeField] private EnemyPerception perception;
    [SerializeField] private EnemySkillManager skillManager;

    public EnemyController Controller => controller;
    public EnemyMovement Movement => movement;
    public EnemyCombat Combat => combat;
    public EnemyPerception Perception => perception;
    public EnemySkillManager SkillManager => skillManager;

    // 当前锁定目标（感知/受击写入，状态读取）
    public Transform Target { get; set; }

    // 当前要施放的攻击技能 ID（Attack 状态使用，连段写入下一个 ID）
    public int CurrentAttackSkillId { get; set; }

    // 首段攻击技能 ID（子类 override 提供具体值，Chase → Attack 切换时使用）
    public abstract int FirstAttackSkillId { get; }

    private FsmBase<EnemyStateMachine> m_Fsm;

    private void Awake()  { m_Fsm = new FsmBase<EnemyStateMachine>(this, GetStates()); }
    private void Start()  { m_Fsm.SetStartState(GetStartStateType()); }
    private void Update() { m_Fsm.Update(Time.deltaTime); }

    public void ChangeState<T>() where T : EnemyStateBase
        => m_Fsm.ChangeState<T>();

    // 子类装配
    protected abstract EnemyStateBase[] GetStates();
    protected abstract System.Type GetStartStateType();

    // 受击外部调用入口（API 先开放，调用方后续接入）
    public void OnHit(Transform attacker)
    {
        Target = attacker;
        ChangeState<GetHitState>();
    }
}
```

### 4.2 `EnemyStateBase : FsmStateBase<EnemyStateMachine>`

```csharp
public abstract class EnemyStateBase : FsmStateBase<EnemyStateMachine>
{
    protected bool TryDetectTarget(FsmBase<EnemyStateMachine> fsm)
    {
        Transform t = fsm.Owner.Perception.Eyesight();
        if (t != null) { fsm.Owner.Target = t; return true; }
        return false;
    }
}
```

### 4.3 `GuardStateMachine : EnemyStateMachine`

```csharp
public class GuardStateMachine : EnemyStateMachine
{
    public Transform[] waypoints;
    [SerializeField] private int firstAttackSkillId = 20001;
    public float patrolWaitTime = 1f;

    public override int FirstAttackSkillId => firstAttackSkillId;

    protected override EnemyStateBase[] GetStates() => new EnemyStateBase[]
    {
        new IdleState(),
        new PatrolState(),
        new ChaseState(),
        new AttackState(),
        new GetHitState(),
    };

    protected override System.Type GetStartStateType() => typeof(PatrolState);
}
```

### 4.4 组件改造（去 Blackboard）

```csharp
// EnemyMovement
public void MoveTowards(Transform target);  // 朝目标移动一帧（含 LookAt + 重力）
public void Stop();                         // 仅施重力，不平移
public void LookAt(Transform target);

// EnemyCombat
public bool IsAttackRange(Transform target);
public bool IsAttackTime();
public void Attack(Transform target);       // 重置 cd, IsAttacking=true
public bool IsAttacking { get; }            // 动画事件 EndAttack() 置 false

// EnemyPerception
public Transform Eyesight();                // 维持不变
```

---

## 5. 状态转换图

```
                  ┌─────────────┐
                  │   Idle      │ ← 仅在无 waypoints 配置时启用
                  └─────────────┘
                           ↑ 发现目标
                  ┌─────────────┐
                  │   Patrol    │ ← 起始状态
                  │ (内部管理   │
                  │  走→等→下) │
                  └──┬──────────┘
                     │ 发现目标
                     ▼
                  ┌─────────────┐
                  │   Chase     │
                  │  追击目标   │
                  └──┬──────┬───┘
   进入攻击距离     │      │ 丢失目标
                     ▼      ▼
                  ┌────────┐  Patrol
                  │ Attack │
                  │ 出招连段│
                  └──┬─────┘
   动画播完(>=1f) │
                     ├── 仍可攻击 → Attack 自切（连段）
                     ├── 出攻击距离 → Chase
                     └── 看不见 → Patrol

GetHit ← OnHit(attacker) 外部触发（本次不接入调用方）
GetHit 动画结束 → Chase（Target 已由 OnHit 锁定）
```

---

## 6. 各状态行为细节

### 6.1 `PatrolState`（起始状态）

```
Enter:
  - CrossFadeInFixedTime("Move")
  - 首次进入：_waypointIndex = 0
  - _waiting = false; _waitCounter = 0

Update:
  - 若 waypoints == null 或 waypoints.Length == 0: ChangeState<IdleState>(); return;
  - 若 TryDetectTarget → ChangeState<ChaseState>(); return;
  - 若 _waiting:
      _waitCounter += dt
      Movement.Stop()
      若 _waitCounter >= patrolWaitTime:
          _waiting = false
          _waypointIndex = (_waypointIndex + 1) % waypoints.Length
          CrossFadeInFixedTime("Move")
  - 否则:
      Transform wp = waypoints[_waypointIndex]
      若 距离 <= 1.1f:
          _waiting = true
          _waitCounter = 0
          CrossFadeInFixedTime("Idle")
      否则: Movement.MoveTowards(wp)

Exit: 无
```

### 6.2 `IdleState`（无 waypoints 时使用 / fallback）

```
Enter: CrossFadeInFixedTime("Idle"); Movement.Stop()
Update: 若 TryDetectTarget → ChangeState<ChaseState>()
Exit: 无
```

### 6.3 `ChaseState`

```
Enter: CrossFadeInFixedTime("Move")

Update:
  - 若 Target == null 或 !Target.gameObject.activeInHierarchy:
      fsm.Owner.Target = null
      ChangeState<PatrolState>(); return;
  - 若 Combat.IsAttackRange(Target):
      fsm.Owner.CurrentAttackSkillId = fsm.Owner.FirstAttackSkillId
      ChangeState<AttackState>(); return;
  - Movement.MoveTowards(Target)

Exit: 无
```

### 6.4 `AttackState`

```
Enter:
  - Movement.Stop()
  - skillConfig = ConfigManager.GetSkillConfig(CurrentAttackSkillId)
  - skill = SkillManager.GetSkill(CurrentAttackSkillId)
  - 若 skill == null 或 skillConfig == null:
      Debug.LogError(...) + ChangeState<ChaseState>(); return;
  - skill.RegisterHandler()
  - 若 !skill.Cast(): ChangeState<ChaseState>(); return;
  - Combat.Attack(Target)  // 重置 cd，置 IsAttacking=true
  - CrossFadeInFixedTime(skillConfig.skillAnimationName)

Update:
  - Movement.LookAt(Target)  // 攻击期间慢慢转向，不平移
  - 若 IsPlayingAnimation(skillConfig.skillAnimationName, out animProgress) && animProgress >= 1f:
      若 Target == null 或 !Combat.IsAttackRange(Target):
          若 TryDetectTarget: ChangeState<ChaseState>()
          否则: ChangeState<PatrolState>()
          return;
      若 skillConfig.comboNextSkillId == 0:
          ChangeState<ChaseState>()  // 由 Chase 重新判定是否再次进入 Attack
          return;
      CurrentAttackSkillId = skillConfig.comboNextSkillId
      ChangeState<AttackState>()  // 自切，触发 Enter 重播动画

Exit:
  - skill.UnRegisterHandler()
```

**说明**：首次从 Chase 进入 Attack 时，`ChaseState` 在切换前将 `fsm.Owner.CurrentAttackSkillId` 设为 `fsm.Owner.FirstAttackSkillId`（通用状态库不强依赖具体敌人子类）。连段过程中由 `AttackState` 自己写入 `comboNextSkillId`。

### 6.5 `GetHitState`

```
Enter: CrossFadeInFixedTime("GetHit"); Movement.Stop()

Update:
  - 若 IsPlayingAnimation("GetHit", out animProgress) && animProgress >= 1f:
      ChangeState<ChaseState>()  // Target 已由 OnHit 锁定

Exit: 无
```

---

## 7. 数据流与生命周期

### 7.1 初始化时序

```
1. Inspector 配置（手动操作）：
   - GuardStateMachine 上拖入 animator/controller/movement/combat/perception/skillManager
   - 配置 waypoints[] / firstAttackSkillId / patrolWaitTime
2. Awake(): m_Fsm = new FsmBase(this, GetStates())
3. Start():
   - animator.speed = walkSpeed  (CharacterStateMachine.Start 中执行)
   - m_Fsm.SetStartState(typeof(PatrolState))
     → PatrolState.Enter() → CrossFade("Move")
4. Update(): m_Fsm.Update(dt) → 当前状态 Update(dt)
```

### 7.2 核心数据归属

| 数据 | 持有者 | 写入方 | 读取方 |
|---|---|---|---|
| `Target` | `EnemyStateMachine` | `EnemyStateBase.TryDetectTarget`, `OnHit` | 所有状态、`Movement.MoveTowards` 参数 |
| `FirstAttackSkillId` | `EnemyStateMachine`（抽象，子类 override） | 子类 Inspector 配置 | `ChaseState` |
| `CurrentAttackSkillId` | `EnemyStateMachine` | `ChaseState`（首段）/ `AttackState`（连段） | `AttackState` |
| `IsAttacking` | `EnemyCombat` 内部 bool | `Combat.Attack()` / `EndAttack()` 动画事件 | （`AttackState` 不直接读，通过动画进度判定） |
| `_waypointIndex` / `_waiting` / `_waitCounter` | `PatrolState` 实例字段 | PatrolState | PatrolState |

### 7.3 典型完整事件流（Guard 一生）

```
出生 → PatrolState
  ├─ 走到 wp0 → 等 1s → 走到 wp1 → ...
  └─ TryDetectTarget=true → ChaseState
     └─ MoveTowards(Target) 持续追击
        └─ IsAttackRange(Target) → AttackState
           ├─ CastSkill(currentAttackSkillId) + CrossFade(skillAnim) + Combat.Attack
           ├─ 动画期间 LookAt(Target)
           ├─ 动画事件 EndAttack → Combat.IsAttacking = false
           └─ animProgress >= 1f:
              ├─ 连段(comboNextSkillId != 0): AttackState 自切
              ├─ 仍在距离: Chase（由 Chase 判定再次进入 Attack）
              ├─ 出了攻击距离能看见: Chase
              └─ 看不见: Patrol

任意状态被打:
  外部调用 stateMachine.OnHit(attacker)
    → Target = attacker
    → ChangeState<GetHitState>()
    → 动画播完 → ChaseState
```

---

## 8. 错误处理与边界情况

| 情景 | 处理 |
|---|---|
| `waypoints` 为空 / null | `PatrolState.Enter`(或 Update) 检测到后立即 `ChangeState<IdleState>()` |
| `Target` 在 `ChaseState` 期间被销毁/失活 | `ChaseState.Update` 首行检查，清空 Target → `PatrolState` |
| `Target` 在 `AttackState` 期间被销毁 | 不中断动画，等 `animProgress >= 1f` 由出口分支处理 |
| `SkillManager.GetSkill(id)` 找不到 | `AttackState.Enter` 输出 Debug.LogError + 立即 `ChangeState<ChaseState>()` |
| `skill.Cast()` 失败 | 同上 |
| `comboNextSkillId == 0` | 退出 Attack 时按"无连段"分支处理 → ChaseState |
| 动画事件 `EndAttack` 未触发 | `IsAttacking` 标志可能卡住；本次重构不在此处加保护，运行期观察 |
| 受击事件接入前 | `OnHit` API 已暴露但无调用方，行为表现 = 永远不进 GetHit（确认为预期） |

---

## 9. 前置条件（Unity 编辑器侧）

代码改完后还需要在 Unity 编辑器中完成以下手动操作，否则运行会失败：

1. **Guard 预制体上**：把 `GuardBT` 组件替换为 `GuardStateMachine`，将原 `waypoints` 重新拖入。
2. **删除 Blackboard 引用**：`EnemyMovement` / `EnemyCombat` / `EnemyPerception` 上的 `blackboard` Inspector 字段不再存在，需检查序列化数据不会卡住。
3. **Animator Controller 验证**：`GetHitState` / `PatrolState` / `IdleState` / `ChaseState` / `AttackState` 中使用了 `CrossFadeInFixedTime("Idle")` / `CrossFadeInFixedTime("Move")` / `CrossFadeInFixedTime("GetHit")` / `CrossFadeInFixedTime(skillAnim)`，需要 Animator Controller 中存在对应 state 名（非 Blend Tree 内部子节点）。原 `EnemyAnimator` 使用 `SetBool("Move")` 触发，若 Animator 现为 Bool 驱动 Blend Tree，需要调整为独立 Idle/Move state。
4. **动画事件 `EndAttack`**：保持原行为不变（攻击动画末尾事件调 `EnemyCombat.EndAttack`），无需调整。

---

## 10. 删除清单

```
❌ Assets/Game/Character/Enemy/BehaviourTree/  (整目录)
   ├── GuardBT.cs (+.meta)
   └── Node/
       ├── AttackTaskNode.cs (+.meta)
       ├── EnemyAttackCheckNode.cs (+.meta)
       ├── EnemyFOVCheckNode.cs (+.meta)
       ├── GoToTargetTaskNode.cs (+.meta)
       ├── IntentionType.cs (+.meta)
       └── PatrolTaskNode.cs (+.meta)
❌ Assets/Framework/Core/BehaviourTree/  (整目录，无其它使用者)
   ├── BTree.cs (+.meta)
   ├── BtNode.cs (+.meta)
   ├── SelectorNode.cs (+.meta)
   ├── SequenceNode.cs (+.meta)
   ├── NodeState.cs (+.meta)
   └── Blackboard.cs (+.meta)
❌ Assets/Game/Character/Enemy/EnemyAnimator.cs (+.meta)
```

---

## 11. 验证方式

Unity 项目无单元测试基建，验证靠播放模式手动：

1. 编译通过（Console 无报错）
2. 进入主场景 `Assets/FirstGameLauncher.unity`
3. Guard 出生 → 在 waypoints 间巡逻（走→停→下一个）
4. 玩家进入视野范围 → Guard 切 Chase 追击
5. 进入攻击距离 → 出招，连段（按 SkillConfig 配置）
6. 玩家离开视野 → 回 Patrol
7. （受击分支本次不接，跳过）

---

## 12. 变更摘要

| 类别 | 项 |
|---|---|
| 新增 | `EnemyController.cs`、`EnemyStateMachine.cs`、`EnemyStateBase.cs`、`EnemyFsm/Common/{Idle,Patrol,Chase,Attack,GetHit}State.cs`、`EnemyFsm/Guard/GuardStateMachine.cs` |
| 修改 | `EnemyMovement.cs`（去黑板、暴露方法）、`EnemyCombat.cs`（去黑板、暴露 `IsAttacking`） |
| 删除 | `EnemyAnimator.cs`、整个 `Assets/Game/Character/Enemy/BehaviourTree/`、整个 `Assets/Framework/Core/BehaviourTree/` |
| Unity 编辑器侧 | Guard 预制体组件替换、waypoints 重拖、Animator Controller 验证 |
