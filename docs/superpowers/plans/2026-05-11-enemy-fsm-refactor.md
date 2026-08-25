# 敌人行为树重构为状态机 - 实现计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 Guard 敌人 AI 从行为树（GuardBT + Blackboard + 5 个 BtNode）迁移为状态机（PatrolState/IdleState/ChaseState/AttackState/GetHitState），搭建可复用的敌人 FSM 基类与状态库，最后清理掉行为树整套框架。

**Architecture:** 顶层 `EnemyStateMachine : CharacterStateMachine` 抽象基类持有 `FsmBase<EnemyStateMachine>` 和组件引用；每种敌人一个子类（本次只做 `GuardStateMachine`）通过 `GetStates()` 装配状态；通用状态位于 `EnemyFsm/Common/`；动画播放统一用 `CrossFadeInFixedTime`（与玩家一致）。Blackboard 完全删除，状态直接调用组件方法。

**Tech Stack:** Unity 2022.3.61f1c1 / C# / `FsmBase<T>` 既有 FSM 框架（`Assets/Framework/Core/FSM/`）

**实施策略：** Unity 项目无单元测试基建，每步用"编译通过 + Unity 播放模式手动验证"代替 TDD。组件改造采用渐进式 —— 先加新方法保留旧字段以维持编译，全部新代码就位后再统一删除旧依赖。

**详细设计：** 见 `docs/superpowers/specs/2026-05-11-enemy-fsm-refactor-design.md`

---

## 文件结构概览

**新建（11 个文件 + 各自 .meta）：**

```
Assets/Game/Character/Enemy/
├── EnemyController.cs                  ← Task 3
├── EnemyStateMachine.cs                ← Task 4
└── EnemyFsm/
    ├── EnemyStateBase.cs               ← Task 4
    ├── Common/
    │   ├── IdleState.cs                ← Task 5 骨架 / Task 6 实现
    │   ├── PatrolState.cs              ← Task 5 骨架 / Task 7 实现
    │   ├── ChaseState.cs               ← Task 5 骨架 / Task 8 实现
    │   ├── AttackState.cs              ← Task 5 骨架 / Task 9 实现
    │   └── GetHitState.cs              ← Task 5 骨架 / Task 10 实现
    └── Guard/
        └── GuardStateMachine.cs        ← Task 11
```

**修改（3 个文件）：**

```
Assets/Game/Character/Enemy/EnemyMovement.cs     ← Task 1 加方法 / Task 14 清理
Assets/Game/Character/Enemy/EnemyCombat.cs       ← Task 2 加方法 / Task 14 清理
Assets/Game/Character/Enemy/EnemyPerception.cs   ← Task 14 清理 blackboard 字段
```

**删除（Task 13）：**

```
Assets/Game/Character/Enemy/EnemyAnimator.cs (+ .meta)
Assets/Game/Character/Enemy/BehaviourTree/  (整目录)
Assets/Framework/Core/BehaviourTree/  (整目录)
```

**Unity 编辑器手动操作（Task 12，由用户在编辑器中执行）：**
- Guard 预制体上把 `GuardBT` 组件换成 `GuardStateMachine`、重拖 `waypoints` 数组、设置 `firstAttackSkillId` 和 `patrolWaitTime`
- 拖入 `controller / movement / combat / perception / skillManager` 引用
- 验证 Animator Controller 中存在 `Idle` / `Move` / `GetHit` 三个 state（且非 Blend Tree 内部子节点）
- 删除组件 Inspector 上残留的 `blackboard` 字段引用

---

## Task 1: 在 EnemyMovement 上添加 FSM 友好的新方法

**Files:**
- Modify: `Assets/Game/Character/Enemy/EnemyMovement.cs`

**目标：** 添加 `MoveTowards(Transform)` / `Stop()` / `LookAt(Transform)` 三个方法供 FSM 状态调用。**暂时保留**原 `Update()` 中读 Blackboard 的逻辑（在 Task 14 才清理），让本任务后既能跑旧 BT 也能给新 FSM 用。

- [ ] **Step 1: 修改 EnemyMovement.cs 增加新方法**

完整文件内容（旧的 Update 逻辑暂时保留，新方法插在 LookAt 之后）：

```csharp
using System;
using Framework.Utils;
using UnityEngine;

namespace Game.Character.Enemy
{
    public class EnemyMovement : MonoBehaviour
    {
        [SerializeField]
        private UnityEngine.CharacterController controller;
        public Blackboard blackboard;

        public bool IsGravity = true;
        public float moveSpeed = 2f;
        public float rotateSpeed = 4f;

        private void Update()
        {
            // 旧逻辑：保留兼容期，让现有 BT 继续工作；Task 14 删除
            if (blackboard == null) { _gravity(); return; }
            bool isMoving = blackboard.Get<bool>("IsMoving");
            bool isAttacking = blackboard.TryGet<bool>("IsAttacking", out bool v) && v;

            if (!isAttacking && blackboard.TryGet<Transform>("Target", out Transform target) && isMoving)
            {
                LookAt(target);
                controller.Move((target.position - transform.position).normalized * moveSpeed * Time.deltaTime);
            }

            _gravity();
        }

        public void LookAt(Transform target)
        {
            Quaternion rot = VectorUtil.FaceTargetY(transform, target.position);
            transform.rotation = Quaternion.Lerp(transform.rotation, rot, rotateSpeed * Time.deltaTime);
        }

        // ===== 供 FSM 状态调用的接口（Task 1 新增） =====

        /// <summary>
        /// 朝目标移动一帧（含转向 + 平移）。重力在 Update 里统一处理。
        /// </summary>
        public void MoveTowards(Transform target)
        {
            if (target == null) return;
            LookAt(target);
            Vector3 dir = (target.position - transform.position).normalized;
            controller.Move(dir * moveSpeed * Time.deltaTime);
        }

        /// <summary>
        /// 停止平移（保留重力）。FSM 状态需要"立定"时调用。
        /// 当前实现只是显式空操作 —— 重力在 Update 里独立处理。
        /// </summary>
        public void Stop()
        {
            // 空操作。明确语义而非空方法 = 让调用方代码可读。
        }

        private void _gravity()
        {
            if (IsGravity)
                controller.Move(new Vector3(0, -9.8f, 0f) * Time.deltaTime);
        }
    }
}
```

- [ ] **Step 2: Unity 编辑器中确认编译通过**

打开 Unity 编辑器，等待 Console 显示 "Reload" 完成，确认无编译错误。
预期：Console 无红色错误。

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Character/Enemy/EnemyMovement.cs
git commit -m "feat(enemy): EnemyMovement 添加 FSM 友好接口（MoveTowards/Stop/LookAt）"
```

---

## Task 2: 在 EnemyCombat 上添加 IsAttacking 属性和 Attack(target) 重置语义

**Files:**
- Modify: `Assets/Game/Character/Enemy/EnemyCombat.cs`

**目标：** 把"是否攻击中"从 Blackboard 标志改为 `EnemyCombat` 内部的 `IsAttacking` 属性，由 `Attack()` 置 true、由动画事件 `EndAttack()` 置 false。**暂时保留 Blackboard 字段**给旧 BT 用。

- [ ] **Step 1: 修改 EnemyCombat.cs**

完整文件内容：

```csharp
using System;
using UnityEngine;

namespace Game.Character.Enemy
{
    public class EnemyCombat : MonoBehaviour
    {
        public Blackboard blackboard;
        /// <summary>
        /// 攻击距离
        /// </summary>
        public float attackRange;
        /// <summary>
        /// 攻击间隔
        /// </summary>
        public float attackTime;

        private float _attackCounter = 0f;

        /// <summary>
        /// 当前是否处于攻击动画期间。由 Attack() 置 true，由动画事件 EndAttack() 置 false。
        /// </summary>
        public bool IsAttacking { get; private set; }

        public bool IsAttackRange(Transform target)
        {
            float dist = Vector3.Distance(target.position, transform.position);
            return dist <= attackRange;
        }

        public bool IsAttackTime()
        {
            return _attackCounter >= attackTime;
        }

        private void Update()
        {
            _attackCounter += Time.deltaTime;
        }

        public void Attack(Transform target)
        {
            _attackCounter = 0;
            IsAttacking = true;
            // 兼容旧 BT 期间也回写黑板，Task 14 移除
            if (blackboard != null) blackboard.Set<bool>("IsAttacking", true);
        }

        // 由攻击动画最后一帧的 Animation Event 调用
        public void EndAttack()
        {
            IsAttacking = false;
            // 兼容旧 BT 期间也回写黑板，Task 14 移除
            if (blackboard != null) blackboard.Set<bool>("IsAttacking", false);
        }
    }
}
```

- [ ] **Step 2: Unity 编辑器中确认编译通过**

预期：Console 无红色错误。

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Character/Enemy/EnemyCombat.cs
git commit -m "feat(enemy): EnemyCombat 添加 IsAttacking 属性（保留 Blackboard 兼容）"
```

---

## Task 3: 新增 EnemyController

**Files:**
- Create: `Assets/Game/Character/Enemy/EnemyController.cs`

**目标：** 创建 `EnemyController : CharacterController`，参考 commit `2c9c090` 删除前的版本，提供 `Rotate` 重写和持有 `EnemySkillManager` 引用的能力。

- [ ] **Step 1: 创建 EnemyController.cs**

完整内容：

```csharp
using GameMain2.Scripts.Character;
using UnityEngine;

namespace Game.Character.Enemy
{
    public class EnemyController : CharacterController
    {
        public EnemySkillManager SkillManager;

        private void Awake()
        {
            controller.detectCollisions = false;
        }

        public override void Rotate(Vector3 targetDir)
        {
            transform.rotation = Quaternion.Lerp(
                transform.rotation,
                Quaternion.LookRotation(targetDir),
                Time.deltaTime * RotateSpeed);
        }
    }
}
```

- [ ] **Step 2: Unity 编辑器中确认编译通过**

预期：Console 无红色错误。Unity 会自动生成 `EnemyController.cs.meta`。

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Character/Enemy/EnemyController.cs Assets/Game/Character/Enemy/EnemyController.cs.meta
git commit -m "feat(enemy): 新增 EnemyController（CharacterController 子类）"
```

---

## Task 4: 新增 EnemyStateBase 和 EnemyStateMachine 基类

**Files:**
- Create: `Assets/Game/Character/Enemy/EnemyFsm/EnemyStateBase.cs`
- Create: `Assets/Game/Character/Enemy/EnemyStateMachine.cs`

**目标：** 搭好 FSM 基础设施。注意：`EnemyStateMachine` 中暂未引用具体状态类型（5 个 Common State 在 Task 5 才建），所以 `GetStartStateType()` 是抽象方法、`OnHit` 中也用 typeof 字符串延迟绑定 —— 实际上 `OnHit` 必须引用 `GetHitState` 类型，**因此 Task 4 与 Task 5 必须连续提交**才能编译通过。本任务先创建文件但不在 Unity 重载之间停留；Task 5 一起完成后再编译。

⚠️ **本任务结束时项目编译会失败**（因为 OnHit 引用未创建的 GetHitState）—— 这是预期，Task 5 后会恢复。

- [ ] **Step 1: 创建目录与 EnemyStateBase.cs**

文件路径：`Assets/Game/Character/Enemy/EnemyFsm/EnemyStateBase.cs`

```csharp
using GameMain2.Framework.Core.FSM;
using UnityEngine;

namespace Game.Character.Enemy.EnemyFsm
{
    public abstract class EnemyStateBase : FsmStateBase<EnemyStateMachine>
    {
        /// <summary>
        /// 通用感知 helper：检测视野范围内是否有目标，发现则写入 fsm.Owner.Target。
        /// </summary>
        protected bool TryDetectTarget(FsmBase<EnemyStateMachine> fsm)
        {
            Transform t = fsm.Owner.Perception.Eyesight();
            if (t != null)
            {
                fsm.Owner.Target = t;
                return true;
            }
            return false;
        }
    }
}
```

- [ ] **Step 2: 创建 EnemyStateMachine.cs**

文件路径：`Assets/Game/Character/Enemy/EnemyStateMachine.cs`

```csharp
using System;
using Game.Character.Enemy.EnemyFsm;
using GameMain2.Framework.Core.FSM;
using GameMain2.Scripts.Character;
using UnityEngine;

namespace Game.Character.Enemy
{
    public abstract class EnemyStateMachine : Game.Character.CharacterStateMachine
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

        /// <summary>当前锁定目标。由感知/受击写入，由状态读取。</summary>
        public Transform Target { get; set; }

        /// <summary>当前要施放的攻击技能 ID。ChaseState 写入首段，AttackState 写入连段下一个 ID。</summary>
        public int CurrentAttackSkillId { get; set; }

        /// <summary>首段攻击技能 ID。每种敌人子类提供具体值。</summary>
        public abstract int FirstAttackSkillId { get; }

        private FsmBase<EnemyStateMachine> m_Fsm;

        private void Awake()
        {
            m_Fsm = new FsmBase<EnemyStateMachine>(this, GetStates());
        }

        private void Start()
        {
            m_Fsm.SetStartState(GetStartStateType());
        }

        private void Update()
        {
            m_Fsm.Update(Time.deltaTime);
        }

        public void ChangeState<T>() where T : EnemyStateBase
        {
            m_Fsm.ChangeState<T>();
        }

        /// <summary>子类装配：返回此种敌人需要的状态实例数组。</summary>
        protected abstract EnemyStateBase[] GetStates();

        /// <summary>子类装配：起始状态类型。</summary>
        protected abstract Type GetStartStateType();

        /// <summary>
        /// 外部受击入口。锁定攻击者为目标并切到 GetHit。
        /// 本次重构不接入调用方（设计文档已说明）。
        /// </summary>
        public void OnHit(Transform attacker)
        {
            Target = attacker;
            ChangeState<Game.Character.Enemy.EnemyFsm.Common.GetHitState>();
        }
    }
}
```

⚠️ 编译错误（找不到 `Game.Character.Enemy.EnemyFsm.Common.GetHitState`、不存在 `Common` 命名空间）是预期，Task 5 修复。

- [ ] **Step 3: 跳过编译验证，直接进入 Task 5**

不要在 Unity 中等待重载（会满屏报错）；继续执行 Task 5，把状态骨架创建完毕后再统一回到编辑器验证。

- [ ] **Step 4: Commit**

```bash
git add Assets/Game/Character/Enemy/EnemyStateMachine.cs Assets/Game/Character/Enemy/EnemyStateMachine.cs.meta Assets/Game/Character/Enemy/EnemyFsm Assets/Game/Character/Enemy/EnemyFsm.meta
git commit -m "feat(enemy): 新增 EnemyStateMachine 基类和 EnemyStateBase（暂未编译通过，待 Task 5）"
```

（如果 `.meta` 文件还没生成，Unity 会在下次重载时生成；先提交 .cs。把 `EnemyFsm` 目录也加入，Unity 自动会建 `EnemyFsm.meta`。）

---

## Task 5: 创建 5 个 Common State 的空骨架（解锁编译）

**Files:**
- Create: `Assets/Game/Character/Enemy/EnemyFsm/Common/IdleState.cs`
- Create: `Assets/Game/Character/Enemy/EnemyFsm/Common/PatrolState.cs`
- Create: `Assets/Game/Character/Enemy/EnemyFsm/Common/ChaseState.cs`
- Create: `Assets/Game/Character/Enemy/EnemyFsm/Common/AttackState.cs`
- Create: `Assets/Game/Character/Enemy/EnemyFsm/Common/GetHitState.cs`

**目标：** 一次性建出所有状态文件骨架（Enter/Update/Exit 均空实现），让 `EnemyStateMachine.OnHit` 中的 `GetHitState` 引用能编译通过。后续 Task 6-10 分别填充各状态的具体逻辑。

- [ ] **Step 1: 创建 IdleState 骨架**

文件 `Assets/Game/Character/Enemy/EnemyFsm/Common/IdleState.cs`：

```csharp
using GameMain2.Framework.Core.FSM;

namespace Game.Character.Enemy.EnemyFsm.Common
{
    public class IdleState : EnemyStateBase
    {
        public override void Enter(FsmBase<EnemyStateMachine> fsm) { }
        public override void Update(FsmBase<EnemyStateMachine> fsm, float deltaTime) { }
        public override void Exit(FsmBase<EnemyStateMachine> fsm) { }
    }
}
```

- [ ] **Step 2: 创建 PatrolState 骨架**

文件 `Assets/Game/Character/Enemy/EnemyFsm/Common/PatrolState.cs`：

```csharp
using GameMain2.Framework.Core.FSM;

namespace Game.Character.Enemy.EnemyFsm.Common
{
    public class PatrolState : EnemyStateBase
    {
        public override void Enter(FsmBase<EnemyStateMachine> fsm) { }
        public override void Update(FsmBase<EnemyStateMachine> fsm, float deltaTime) { }
        public override void Exit(FsmBase<EnemyStateMachine> fsm) { }
    }
}
```

- [ ] **Step 3: 创建 ChaseState 骨架**

文件 `Assets/Game/Character/Enemy/EnemyFsm/Common/ChaseState.cs`：

```csharp
using GameMain2.Framework.Core.FSM;

namespace Game.Character.Enemy.EnemyFsm.Common
{
    public class ChaseState : EnemyStateBase
    {
        public override void Enter(FsmBase<EnemyStateMachine> fsm) { }
        public override void Update(FsmBase<EnemyStateMachine> fsm, float deltaTime) { }
        public override void Exit(FsmBase<EnemyStateMachine> fsm) { }
    }
}
```

- [ ] **Step 4: 创建 AttackState 骨架**

文件 `Assets/Game/Character/Enemy/EnemyFsm/Common/AttackState.cs`：

```csharp
using GameMain2.Framework.Core.FSM;

namespace Game.Character.Enemy.EnemyFsm.Common
{
    public class AttackState : EnemyStateBase
    {
        public override void Enter(FsmBase<EnemyStateMachine> fsm) { }
        public override void Update(FsmBase<EnemyStateMachine> fsm, float deltaTime) { }
        public override void Exit(FsmBase<EnemyStateMachine> fsm) { }
    }
}
```

- [ ] **Step 5: 创建 GetHitState 骨架**

文件 `Assets/Game/Character/Enemy/EnemyFsm/Common/GetHitState.cs`：

```csharp
using GameMain2.Framework.Core.FSM;

namespace Game.Character.Enemy.EnemyFsm.Common
{
    public class GetHitState : EnemyStateBase
    {
        public override void Enter(FsmBase<EnemyStateMachine> fsm) { }
        public override void Update(FsmBase<EnemyStateMachine> fsm, float deltaTime) { }
        public override void Exit(FsmBase<EnemyStateMachine> fsm) { }
    }
}
```

- [ ] **Step 6: Unity 编辑器中确认编译通过**

切到 Unity 等待重载，预期 Console 无红色错误（Task 4 中的 GetHitState 引用现在可解析）。

- [ ] **Step 7: Commit**

```bash
git add Assets/Game/Character/Enemy/EnemyFsm/Common
git commit -m "feat(enemy): 新增 5 个 Common State 空骨架（解锁编译）"
```

---

## Task 6: 实现 IdleState

**Files:**
- Modify: `Assets/Game/Character/Enemy/EnemyFsm/Common/IdleState.cs`

**目标：** 无 waypoints 时的纯待机状态：进入时播 Idle 动画并停止移动；Update 时检测视野，发现目标切 Chase。

- [ ] **Step 1: 填充 IdleState 实现**

完整内容替换：

```csharp
using GameMain2.Framework.Core.FSM;

namespace Game.Character.Enemy.EnemyFsm.Common
{
    public class IdleState : EnemyStateBase
    {
        public override void Enter(FsmBase<EnemyStateMachine> fsm)
        {
            fsm.Owner.CrossFadeInFixedTime("Idle");
            fsm.Owner.Movement.Stop();
        }

        public override void Update(FsmBase<EnemyStateMachine> fsm, float deltaTime)
        {
            if (TryDetectTarget(fsm))
            {
                fsm.ChangeState<ChaseState>();
            }
        }

        public override void Exit(FsmBase<EnemyStateMachine> fsm) { }
    }
}
```

- [ ] **Step 2: Unity 编辑器中确认编译通过**

预期：Console 无错误。

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Character/Enemy/EnemyFsm/Common/IdleState.cs
git commit -m "feat(enemy): 实现 IdleState（无路点时纯待机 + 视野检测）"
```

---

## Task 7: 实现 PatrolState

**Files:**
- Modify: `Assets/Game/Character/Enemy/EnemyFsm/Common/PatrolState.cs`

**目标：** 巡逻状态内部管理"走→到达→等→下一个"的循环。`waypoints` 数组从 `GuardStateMachine` 取（通过强类型转换 —— 暂时引入对 Guard 的依赖，后续如需多种敌人 patrol 可抽到基类接口）。

⚠️ **关于 waypoints 来源**：通用状态库不应依赖具体子类。本任务里 `PatrolState` 通过 `fsm.Owner as GuardStateMachine` 取 `waypoints`/`patrolWaitTime`，若不是 GuardStateMachine 则回退 `IdleState`。这是设计文档允许的"无 waypoints 时使用 IdleState"路径的具体实现。若后续其它敌人也需 patrol，应在 `EnemyStateMachine` 基类抽象 `Waypoints` / `PatrolWaitTime` 属性 —— 本次不做。

- [ ] **Step 1: 填充 PatrolState 实现**

完整内容替换：

```csharp
using Game.Character.Enemy.Guard;
using GameMain2.Framework.Core.FSM;
using UnityEngine;

namespace Game.Character.Enemy.EnemyFsm.Common
{
    public class PatrolState : EnemyStateBase
    {
        private int _waypointIndex;
        private bool _waiting;
        private float _waitCounter;

        public override void Enter(FsmBase<EnemyStateMachine> fsm)
        {
            fsm.Owner.CrossFadeInFixedTime("Move");
            _waiting = false;
            _waitCounter = 0f;
            // _waypointIndex 不重置：Patrol 被中断后回到 Patrol 应从上次的路点继续
        }

        public override void Update(FsmBase<EnemyStateMachine> fsm, float deltaTime)
        {
            GuardStateMachine guard = fsm.Owner as GuardStateMachine;
            Transform[] waypoints = guard != null ? guard.waypoints : null;

            if (waypoints == null || waypoints.Length == 0)
            {
                fsm.ChangeState<IdleState>();
                return;
            }

            if (TryDetectTarget(fsm))
            {
                fsm.ChangeState<ChaseState>();
                return;
            }

            float patrolWaitTime = guard.patrolWaitTime;

            if (_waiting)
            {
                _waitCounter += deltaTime;
                fsm.Owner.Movement.Stop();
                if (_waitCounter >= patrolWaitTime)
                {
                    _waiting = false;
                    _waypointIndex = (_waypointIndex + 1) % waypoints.Length;
                    fsm.Owner.CrossFadeInFixedTime("Move");
                }
                return;
            }

            Transform wp = waypoints[_waypointIndex];
            if (Vector3.Distance(fsm.Owner.transform.position, wp.position) <= 1.1f)
            {
                _waiting = true;
                _waitCounter = 0f;
                fsm.Owner.CrossFadeInFixedTime("Idle");
            }
            else
            {
                fsm.Owner.Movement.MoveTowards(wp);
            }
        }

        public override void Exit(FsmBase<EnemyStateMachine> fsm) { }
    }
}
```

⚠️ 注意：`using Game.Character.Enemy.Guard;` 指向 Task 11 才会创建的命名空间。本步骤后**编译会暂时失败**，直到 Task 11。

- [ ] **Step 2: 跳过中间编译验证**

继续 Task 8-11，全部完成后再统一回 Unity 编译。

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Character/Enemy/EnemyFsm/Common/PatrolState.cs
git commit -m "feat(enemy): 实现 PatrolState（路点巡逻 + 等待节拍 + 视野检测）"
```

---

## Task 8: 实现 ChaseState

**Files:**
- Modify: `Assets/Game/Character/Enemy/EnemyFsm/Common/ChaseState.cs`

**目标：** 追击：朝 Target 持续移动，进入攻击距离切 Attack，目标丢失/失活回 Patrol。

- [ ] **Step 1: 填充 ChaseState 实现**

完整内容替换：

```csharp
using GameMain2.Framework.Core.FSM;
using UnityEngine;

namespace Game.Character.Enemy.EnemyFsm.Common
{
    public class ChaseState : EnemyStateBase
    {
        public override void Enter(FsmBase<EnemyStateMachine> fsm)
        {
            fsm.Owner.CrossFadeInFixedTime("Move");
        }

        public override void Update(FsmBase<EnemyStateMachine> fsm, float deltaTime)
        {
            Transform target = fsm.Owner.Target;
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                fsm.Owner.Target = null;
                fsm.ChangeState<PatrolState>();
                return;
            }

            if (fsm.Owner.Combat.IsAttackRange(target))
            {
                fsm.Owner.CurrentAttackSkillId = fsm.Owner.FirstAttackSkillId;
                fsm.ChangeState<AttackState>();
                return;
            }

            fsm.Owner.Movement.MoveTowards(target);
        }

        public override void Exit(FsmBase<EnemyStateMachine> fsm) { }
    }
}
```

- [ ] **Step 2: 跳过中间编译验证**（Task 7 引入的依赖仍待 Task 11 解决）

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Character/Enemy/EnemyFsm/Common/ChaseState.cs
git commit -m "feat(enemy): 实现 ChaseState（追击 + 进入攻击距离切换 + 目标丢失回 Patrol）"
```

---

## Task 9: 实现 AttackState

**Files:**
- Modify: `Assets/Game/Character/Enemy/EnemyFsm/Common/AttackState.cs`

**目标：** 攻击状态：进入时施放技能并播放攻击动画；动画期间仅缓慢转向；动画结束按"目标丢失/出距离/有无连段"分支。

- [ ] **Step 1: 填充 AttackState 实现**

完整内容替换：

```csharp
using Game.Battle.Skill;
using Game.Battle.Skill.Common;
using GameMain2.Framework.Core.FSM;
using GameMain2.Framework.Manager;
using UnityEngine;

namespace Game.Character.Enemy.EnemyFsm.Common
{
    public class AttackState : EnemyStateBase
    {
        private SkillConfig _skillConfig;
        private SkillBase _skill;

        public override void Enter(FsmBase<EnemyStateMachine> fsm)
        {
            fsm.Owner.Movement.Stop();

            int skillId = fsm.Owner.CurrentAttackSkillId;
            _skillConfig = null;
            _skill = null;

            try
            {
                _skillConfig = ConfigManager.Instance.GetSkillConfig(skillId);
            }
            catch (System.Exception)
            {
                Debug.LogError($"[AttackState] 未找到技能配置: {skillId}");
                fsm.ChangeState<ChaseState>();
                return;
            }

            _skill = fsm.Owner.SkillManager.GetSkill(skillId);
            if (_skill == null)
            {
                Debug.LogError($"[AttackState] 未找到技能实例: {skillId}");
                fsm.ChangeState<ChaseState>();
                return;
            }

            _skill.RegisterHandler();
            if (!_skill.Cast())
            {
                fsm.ChangeState<ChaseState>();
                return;
            }

            fsm.Owner.Combat.Attack(fsm.Owner.Target);
            fsm.Owner.CrossFadeInFixedTime(_skillConfig.skillAnimationName);
        }

        public override void Update(FsmBase<EnemyStateMachine> fsm, float deltaTime)
        {
            if (_skillConfig == null) return; // Enter 已切走

            Transform target = fsm.Owner.Target;
            if (target != null)
            {
                fsm.Owner.Movement.LookAt(target);
            }

            if (!fsm.Owner.IsPlayingAnimation(_skillConfig.skillAnimationName, out float animProgress))
                return;

            if (animProgress < 1f) return;

            // 动画播完：分支出口
            if (target == null || !fsm.Owner.Combat.IsAttackRange(target))
            {
                if (TryDetectTarget(fsm))
                    fsm.ChangeState<ChaseState>();
                else
                    fsm.ChangeState<PatrolState>();
                return;
            }

            if (_skillConfig.comboNextSkillId == 0)
            {
                fsm.ChangeState<ChaseState>();
                return;
            }

            fsm.Owner.CurrentAttackSkillId = _skillConfig.comboNextSkillId;
            fsm.ChangeState<AttackState>();
        }

        public override void Exit(FsmBase<EnemyStateMachine> fsm)
        {
            if (_skill != null)
                _skill.UnRegisterHandler();
        }
    }
}
```

- [ ] **Step 2: 跳过中间编译验证**（依赖仍待 Task 11）

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Character/Enemy/EnemyFsm/Common/AttackState.cs
git commit -m "feat(enemy): 实现 AttackState（施放技能 + 连段 + 出口分支）"
```

---

## Task 10: 实现 GetHitState

**Files:**
- Modify: `Assets/Game/Character/Enemy/EnemyFsm/Common/GetHitState.cs`

**目标：** 受击：播 GetHit 动画，播完切 Chase（Target 已由 OnHit 锁定）。

- [ ] **Step 1: 填充 GetHitState 实现**

完整内容替换：

```csharp
using GameMain2.Framework.Core.FSM;

namespace Game.Character.Enemy.EnemyFsm.Common
{
    public class GetHitState : EnemyStateBase
    {
        public override void Enter(FsmBase<EnemyStateMachine> fsm)
        {
            fsm.Owner.CrossFadeInFixedTime("GetHit");
            fsm.Owner.Movement.Stop();
        }

        public override void Update(FsmBase<EnemyStateMachine> fsm, float deltaTime)
        {
            if (fsm.Owner.IsPlayingAnimation("GetHit", out float animProgress) && animProgress >= 1f)
            {
                fsm.ChangeState<ChaseState>();
            }
        }

        public override void Exit(FsmBase<EnemyStateMachine> fsm) { }
    }
}
```

- [ ] **Step 2: 跳过中间编译验证**（依赖仍待 Task 11）

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Character/Enemy/EnemyFsm/Common/GetHitState.cs
git commit -m "feat(enemy): 实现 GetHitState（受击动画 + 播完切 Chase）"
```

---

## Task 11: 新增 GuardStateMachine 并解锁编译

**Files:**
- Create: `Assets/Game/Character/Enemy/EnemyFsm/Guard/GuardStateMachine.cs`

**目标：** Guard 专属装配类，提供 waypoints、firstAttackSkillId、patrolWaitTime 配置，装配 5 个状态。完成后整个项目应能编译通过。

- [ ] **Step 1: 创建 GuardStateMachine.cs**

文件 `Assets/Game/Character/Enemy/EnemyFsm/Guard/GuardStateMachine.cs`：

```csharp
using System;
using Game.Character.Enemy.EnemyFsm.Common;
using UnityEngine;

namespace Game.Character.Enemy.Guard
{
    public class GuardStateMachine : EnemyStateMachine
    {
        public Transform[] waypoints;
        [SerializeField] private int firstAttackSkillId = 20001;
        public float patrolWaitTime = 1f;

        public override int FirstAttackSkillId => firstAttackSkillId;

        protected override EnemyFsm.EnemyStateBase[] GetStates() => new EnemyFsm.EnemyStateBase[]
        {
            new IdleState(),
            new PatrolState(),
            new ChaseState(),
            new AttackState(),
            new GetHitState(),
        };

        protected override Type GetStartStateType() => typeof(PatrolState);
    }
}
```

- [ ] **Step 2: Unity 编辑器中确认整个项目编译通过**

切到 Unity 等待重载完成。预期：Console 无红色错误。如有错误，逐个排查（常见：缺命名空间 using、文件名与类名不一致）。

- [ ] **Step 3: Commit**

```bash
git add Assets/Game/Character/Enemy/EnemyFsm/Guard
git commit -m "feat(enemy): 新增 GuardStateMachine 装配（项目恢复编译）"
```

---

## Task 12: Unity 编辑器手动操作（指南，由用户在编辑器中执行）

**目标：** 把 Guard 预制体上挂载的 `GuardBT` 组件替换为 `GuardStateMachine`，重新拖入引用，验证 Animator Controller。

⚠️ 此任务**不是代码任务**，需要用户在 Unity 编辑器内手动操作。AI 无法替代。请按以下清单逐项检查后再继续 Task 13。

- [ ] **Step 1: 找到 Guard 预制体**

在 Unity Project 视图中定位 Guard 预制体（很可能在 `Assets/Res/` 或 `Assets/Game/Character/Enemy/` 下；用 Hierarchy 中场景里 Guard 实例右键 → "Select Prefab Asset" 也可定位）。

- [ ] **Step 2: 移除 GuardBT 组件并添加 GuardStateMachine**

在 Inspector 中：
1. 右键 `Guard BT (Script)` 组件 → Remove Component
2. Add Component → 搜 `Guard State Machine` → 添加
3. **不要**保存场景前先完成 Step 3 配置

- [ ] **Step 3: 配置 GuardStateMachine 字段**

在新加的 `Guard State Machine` 组件 Inspector 上：
- `Animator`：拖入 Guard 上的 Animator 组件（或与之前 BT 装配一致的引用）
- `Weapon Handler`：保持与之前一致
- `Walk Speed` / `Run Speed`：保持与之前一致
- `Controller`：拖入 EnemyController 组件
- `Movement`：拖入 EnemyMovement 组件
- `Combat`：拖入 EnemyCombat 组件
- `Perception`：拖入 EnemyPerception 组件
- `Skill Manager`：拖入 EnemySkillManager 组件
- `Waypoints`：把之前 `GuardBT.waypoints` 数组里的 Transform 重新拖入
- `First Attack Skill Id`：填 20001（或场景需要的首段技能 ID）
- `Patrol Wait Time`：填 1（或希望的等待秒数）

- [ ] **Step 4: 删除残留的 Blackboard 字段引用（如有）**

检查同一 GameObject 上：
- `EnemyMovement` Inspector 里的 `Blackboard` 字段 —— 当前仍是公开字段，先不动（Task 14 才删除字段定义）
- `EnemyCombat` 同上
- `EnemyPerception` 同上

留到 Task 14 一起处理。

- [ ] **Step 5: 验证 Animator Controller**

打开 Guard 当前使用的 Animator Controller 资源，确认存在以下 state（**不是 Blend Tree 内部子节点**，而是顶层 state）：
- `Idle`
- `Move`
- `GetHit`
- 攻击技能动画 state（与 SkillConfig 里 `skillAnimationName` 字段一致，例如 `EnemyAttack01` 等）

如果当前 Controller 是用 Bool `Move` 在 Blend Tree 间过渡，需要改造：
1. 把 Blend Tree 拆为独立的 `Idle` 和 `Move` 两个 state
2. 删除 `Move` Bool 参数（FSM 用 CrossFade 直接切，不需要 Bool）

如不确定，先保留原 Controller 进入 Task 13；如果运行后表现异常再回来调整。

- [ ] **Step 6: 保存场景和预制体**

`File → Save`（保存场景），预制体上的修改也会一并保存。

- [ ] **Step 7: 不需要 commit**

预制体和 Animator 资源的变更会在下次代码 commit 时作为 .prefab / .controller 文件改动一并提交（或单独提交）。建议：

```bash
git add -A
git status  # 检查只有预期的 .prefab / .controller / .unity 改动
git commit -m "chore(enemy): Guard 预制体切换 GuardBT → GuardStateMachine（Unity 编辑器侧）"
```

---

## Task 13: 删除行为树框架、节点和 EnemyAnimator

**Files:**
- Delete: `Assets/Game/Character/Enemy/BehaviourTree/` (整目录)
- Delete: `Assets/Framework/Core/BehaviourTree/` (整目录)
- Delete: `Assets/Game/Character/Enemy/EnemyAnimator.cs` (+ `.meta`)

**目标：** 删除所有不再使用的行为树代码和 EnemyAnimator。

⚠️ **前置确认：** 必须先完成 Task 12（预制体已不再挂载 GuardBT、EnemyAnimator），否则会导致预制体引用断链。

- [ ] **Step 1: 删除 BehaviourTree 节点目录**

```bash
git rm -r Assets/Game/Character/Enemy/BehaviourTree
git rm -f Assets/Game/Character/Enemy/BehaviourTree.meta 2>/dev/null || true
```

- [ ] **Step 2: 删除 BehaviourTree 框架目录**

```bash
git rm -r Assets/Framework/Core/BehaviourTree
git rm -f Assets/Framework/Core/BehaviourTree.meta 2>/dev/null || true
```

- [ ] **Step 3: 删除 EnemyAnimator**

```bash
git rm Assets/Game/Character/Enemy/EnemyAnimator.cs
git rm -f Assets/Game/Character/Enemy/EnemyAnimator.cs.meta 2>/dev/null || true
```

- [ ] **Step 4: Unity 编辑器中确认编译通过**

切到 Unity 等待重载完成。预期：Console 无红色错误。

如有错误，最常见两种：
1. `Blackboard` 类型找不到 —— 因为 `EnemyMovement` / `EnemyCombat` / `EnemyPerception` 仍引用 `public Blackboard blackboard;` 字段，但 `Blackboard.cs` 已删除。这个会在 Task 14 修复，**但**为了 Task 13 单独编译通过，需要先把这三个文件里的 `public Blackboard blackboard;` 字段改成注释或临时声明 `public object blackboard;`。**建议把 Task 14 紧跟 Task 13 一起执行，跳过中间编译验证。**
2. 预制体仍引用已删 script —— 在场景/预制体中显式删掉残余引用。

**采用紧跟策略**：跳过本步骤的 Unity 编译验证，直接进入 Task 14。

- [ ] **Step 5: Commit**

```bash
git commit -m "chore(enemy): 删除行为树框架、节点和 EnemyAnimator"
```

---

## Task 14: 清理 EnemyMovement / EnemyCombat / EnemyPerception 的 Blackboard 依赖

**Files:**
- Modify: `Assets/Game/Character/Enemy/EnemyMovement.cs`
- Modify: `Assets/Game/Character/Enemy/EnemyCombat.cs`
- Modify: `Assets/Game/Character/Enemy/EnemyPerception.cs`

**目标：** 删除三个组件中所有 Blackboard 字段和兼容代码，恢复项目编译。

- [ ] **Step 1: 简化 EnemyMovement.cs**

完整内容替换（删除 `blackboard` 字段和旧 Update 逻辑）：

```csharp
using Framework.Utils;
using UnityEngine;

namespace Game.Character.Enemy
{
    public class EnemyMovement : MonoBehaviour
    {
        [SerializeField]
        private UnityEngine.CharacterController controller;

        public bool IsGravity = true;
        public float moveSpeed = 2f;
        public float rotateSpeed = 4f;

        private void Update()
        {
            _gravity();
        }

        public void LookAt(Transform target)
        {
            Quaternion rot = VectorUtil.FaceTargetY(transform, target.position);
            transform.rotation = Quaternion.Lerp(transform.rotation, rot, rotateSpeed * Time.deltaTime);
        }

        /// <summary>
        /// 朝目标移动一帧（含转向 + 平移）。
        /// </summary>
        public void MoveTowards(Transform target)
        {
            if (target == null) return;
            LookAt(target);
            Vector3 dir = (target.position - transform.position).normalized;
            controller.Move(dir * moveSpeed * Time.deltaTime);
        }

        /// <summary>
        /// 停止平移（保留重力）。FSM 状态需要"立定"时调用。
        /// </summary>
        public void Stop()
        {
            // 显式空操作 = 让调用方代码可读。
        }

        private void _gravity()
        {
            if (IsGravity)
                controller.Move(new Vector3(0, -9.8f, 0f) * Time.deltaTime);
        }
    }
}
```

- [ ] **Step 2: 简化 EnemyCombat.cs**

完整内容替换：

```csharp
using UnityEngine;

namespace Game.Character.Enemy
{
    public class EnemyCombat : MonoBehaviour
    {
        /// <summary>攻击距离</summary>
        public float attackRange;
        /// <summary>攻击间隔</summary>
        public float attackTime;

        private float _attackCounter = 0f;

        /// <summary>
        /// 当前是否处于攻击动画期间。由 Attack() 置 true，由动画事件 EndAttack() 置 false。
        /// </summary>
        public bool IsAttacking { get; private set; }

        public bool IsAttackRange(Transform target)
        {
            float dist = Vector3.Distance(target.position, transform.position);
            return dist <= attackRange;
        }

        public bool IsAttackTime()
        {
            return _attackCounter >= attackTime;
        }

        private void Update()
        {
            _attackCounter += Time.deltaTime;
        }

        public void Attack(Transform target)
        {
            _attackCounter = 0;
            IsAttacking = true;
        }

        // 由攻击动画最后一帧的 Animation Event 调用
        public void EndAttack()
        {
            IsAttacking = false;
        }
    }
}
```

- [ ] **Step 3: 简化 EnemyPerception.cs**

完整内容替换：

```csharp
using UnityEngine;

namespace Game.Character.Enemy
{
    public class EnemyPerception : MonoBehaviour
    {
        public float range;
        public LayerMask mask;
        public float angle;

        public Transform Eyesight()
        {
            Collider[] colliders = Physics.OverlapSphere(transform.position, range, mask);
            Transform target = null;
            if (colliders.Length != 0)
            {
                // 检查是否在前方且指定角度内
                target = colliders[0].transform;
                Vector3 dir = (target.position - transform.position).normalized;
                if (!(Vector3.Angle(dir, transform.forward) <= angle / 2.0f))
                {
                    target = null;
                }
            }
            return target;
        }
    }
}
```

- [ ] **Step 4: Unity 编辑器中确认编译通过**

切到 Unity 等待重载完成。预期：Console 无红色错误。

如果 Console 提示 "missing script reference" 警告（来自预制体上的 `blackboard` 字段引用），属于序列化遗留，**不影响编译**。Unity 重新保存预制体时会清掉。

- [ ] **Step 5: Commit**

```bash
git add Assets/Game/Character/Enemy/EnemyMovement.cs Assets/Game/Character/Enemy/EnemyCombat.cs Assets/Game/Character/Enemy/EnemyPerception.cs
git commit -m "refactor(enemy): 删除 Blackboard 字段和兼容代码，组件接口简化"
```

---

## Task 15: 播放模式验证

**目标：** 在 Unity 播放模式手动验证 Guard 行为符合设计：巡逻 → 发现 → 追击 → 攻击 → 连段 → 失去目标回巡逻。

- [ ] **Step 1: 打开主场景**

Project 视图打开 `Assets/FirstGameLauncher.unity` 或 Guard 所在的测试场景。

- [ ] **Step 2: 点击 Play，观察 Guard 初始状态**

进入 Play 模式。预期：
- Guard 出生后立即开始在 waypoints 之间移动（播 Move 动画）
- 到达路点后停下 1 秒（播 Idle 动画），然后走向下一个路点

如果 Guard 不动：检查 Animator Controller 是否有 `Move` state；检查 `controller.detectCollisions=false` 是否阻塞了 CharacterController 移动。

- [ ] **Step 3: 操控玩家进入 Guard 视野范围**

走到 Guard 视野内（注意 EnemyPerception 的 range 和 angle 限制）。预期：
- Guard 切换到追击：朝玩家移动，方向跟随玩家
- 玩家移动时 Guard 持续追

- [ ] **Step 4: 让玩家进入 Guard 攻击距离**

靠近 Guard 直到进入 EnemyCombat.attackRange。预期：
- Guard 停下并播放首段攻击动画（skillId=20001 对应的 animation）
- 攻击命中后玩家正常接受伤害（如已有命中链路）
- 如果 SkillConfig 配置了 comboNextSkillId，攻击动画播完后会自动进入下一段

如果 Guard 不攻击：
- 检查 `EnemySkillManager` 中是否注册了 `firstAttackSkillId`（默认 20001/20002/20003 已在 `EnemySkillManager.InitSkill()` 注册）
- Console 检查是否有 `[AttackState] 未找到技能...` 错误

- [ ] **Step 5: 玩家逃离 Guard 视野**

跑出 EnemyPerception 范围。预期：
- Guard 攻击动画播完后切到 Chase；若再无法看到玩家则切回 Patrol，回到 waypoints 巡逻

- [ ] **Step 6: 退出 Play 模式，记录任何异常**

如果观察到异常（动画卡死、状态切换错误、Console 报错），定位到具体 State 文件后修复 + commit。

- [ ] **Step 7: 验证通过后无需额外 commit**

播放模式验证不修改文件，无需 commit。

---

## 完成检查

- [ ] 所有 14 个代码任务已 commit
- [ ] Task 12（Unity 编辑器侧配置）已完成
- [ ] Task 15 播放模式验证通过
- [ ] `git status` 工作区干净
- [ ] `Assets/Game/Character/Enemy/BehaviourTree/` 不存在
- [ ] `Assets/Framework/Core/BehaviourTree/` 不存在
- [ ] `Assets/Game/Character/Enemy/EnemyAnimator.cs` 不存在
- [ ] Console 无编译错误
- [ ] Guard 实例运行正常（巡逻/追击/攻击/连段）
