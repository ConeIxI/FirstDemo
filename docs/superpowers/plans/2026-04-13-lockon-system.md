# Lock-On System Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 实现魂系风格单目标硬锁定系统——鼠标中键切换锁定/解锁，滚轮切换目标，锁定时玩家始终面朝敌人并可横向平移，头顶显示金黄光圈。

**Architecture:** 新增 `LockOnManager` MonoBehaviour 挂在玩家 GameObject 上，独立处理输入、目标检测、Cinemachine LookAt 切换和光圈指示器。`PlayerStateMachine` 暴露其引用，`WalkState` 和 `PlayerController` 各增加一个锁定分支，FSM 框架和技能系统不变。

**Tech Stack:** Unity 2022.3.61f1c1, Cinemachine 2.10.5, C#

---

## 文件清单

| 操作 | 路径 |
|------|------|
| 修改 | `Assets/Framework/Manager/InputManager.cs` |
| 新建 | `Assets/Game/Character/Player/LockOnManager.cs` |
| 修改 | `Assets/Game/Character/Player/PlayerController.cs` |
| 修改 | `Assets/Game/Character/Player/PlayerStateMachine.cs` |
| 修改 | `Assets/Game/Character/Player/PlayerFsm/WalkState.cs` |

---

## Task 1: InputManager — 新增锁定输入方法

**Files:**
- Modify: `Assets/Framework/Manager/InputManager.cs`

- [ ] **Step 1: 在 InputManager.cs 末尾 `IsWeaponSwitchKeyPressed()` 方法之后添加两个方法**

找到第 62-64 行的 `IsWeaponSwitchKeyPressed()` 方法，在其后、类的结束括号之前插入：

```csharp
/// <summary>
/// 鼠标中键，用于锁定/解锁目标
/// </summary>
public bool IsLockOnPressed()
{
    return Input.GetMouseButtonDown(2);
}

/// <summary>
/// 鼠标滚轮增量，正值向上滚动，负值向下滚动
/// </summary>
public float GetScrollDelta()
{
    return Input.GetAxis("Mouse ScrollWheel");
}
```

修改后文件末尾应如下（从 `IsWeaponSwitchKeyPressed` 开始）：

```csharp
        public bool IsWeaponSwitchKeyPressed()
        {
            return Input.GetKeyDown(KeyCode.Tab);
        }

        /// <summary>
        /// 鼠标中键，用于锁定/解锁目标
        /// </summary>
        public bool IsLockOnPressed()
        {
            return Input.GetMouseButtonDown(2);
        }

        /// <summary>
        /// 鼠标滚轮增量，正值向上滚动，负值向下滚动
        /// </summary>
        public float GetScrollDelta()
        {
            return Input.GetAxis("Mouse ScrollWheel");
        }
    }
}
```

- [ ] **Step 2: Unity 编辑器验证编译**

打开 Unity 编辑器，等待脚本重新编译（右下角进度条消失）。确认 Console 窗口无红色错误。

- [ ] **Step 3: 提交**

```bash
git add Assets/Framework/Manager/InputManager.cs
git commit -m "feat: add lock-on input methods to InputManager (middle mouse + scroll wheel)"
```

---

## Task 2: 新建 LockOnManager.cs

**Files:**
- Create: `Assets/Game/Character/Player/LockOnManager.cs`

- [ ] **Step 1: 创建文件，写入完整实现**

```csharp
using System.Collections.Generic;
using Cinemachine;
using GameMain2.Framework.Manager;
using UnityEngine;

namespace Game.Character.Player
{
    public class LockOnManager : MonoBehaviour
    {
        [Header("检测参数")]
        [SerializeField] private float lockOnRange = 15f;
        [SerializeField] private float autoUnlockRange = 18f;
        [SerializeField] private float lockOnFovDot = 0f;   // 0 = 摄像机前方 90° 内
        [SerializeField] private LayerMask enemyLayer;

        [Header("摄像机")]
        [SerializeField] private CinemachineVirtualCamera virtualCamera;
        [SerializeField] private Transform playerHeadTransform;  // 解锁后 LookAt 恢复的目标

        [Header("光圈指示器")]
        [SerializeField] private GameObject lockOnRingPrefab;
        [SerializeField] private float ringHeadOffset = 2.0f;

        [Header("锁定移动速度")]
        [SerializeField] private float lockOnMoveSpeed = 4f;

        // ── 公开状态 ────────────────────────────────────────────────
        public bool IsLockedOn { get; private set; }
        public Transform CurrentTarget { get; private set; }
        public float LockOnMoveSpeed => lockOnMoveSpeed;

        // ── 私有 ────────────────────────────────────────────────────
        private Transform _lockOnLookTarget;
        private GameObject _ringInstance;

        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            // 创建摄像机 LookAt 用的中间点（跟随玩家移动）
            var go = new GameObject("LockOnLookTarget");
            _lockOnLookTarget = go.transform;
            _lockOnLookTarget.SetParent(transform);
        }

        private void Update()
        {
            HandleInput();

            if (IsLockedOn)
            {
                CheckAutoUnlock();
                UpdateLookTarget();
            }
        }

        private void LateUpdate()
        {
            // 光圈始终朝向摄像机（广告牌效果）
            if (IsLockedOn && _ringInstance != null)
            {
                Vector3 dir = _ringInstance.transform.position - Camera.main.transform.position;
                if (dir != Vector3.zero)
                    _ringInstance.transform.rotation = Quaternion.LookRotation(dir);
            }
        }

        // ── 输入处理 ─────────────────────────────────────────────────

        private void HandleInput()
        {
            if (InputManager.Instance.IsLockOnPressed())
            {
                if (IsLockedOn)
                    Unlock();
                else
                    TryLockOn();
                return;
            }

            if (IsLockedOn)
            {
                float scroll = InputManager.Instance.GetScrollDelta();
                if (scroll > 0f) SwitchTarget(1);
                else if (scroll < 0f) SwitchTarget(-1);
            }
        }

        // ── 公开方法 ─────────────────────────────────────────────────

        /// <summary>检测并锁定最佳目标</summary>
        public void TryLockOn()
        {
            Transform target = FindBestTarget();
            if (target != null)
                LockOnTo(target);
        }

        /// <summary>解除锁定，恢复摄像机和销毁光圈</summary>
        public void Unlock()
        {
            IsLockedOn = false;
            CurrentTarget = null;

            if (virtualCamera != null)
                virtualCamera.LookAt = playerHeadTransform;

            DestroyRing();
        }

        /// <summary>切换到下一个 / 上一个目标。direction: +1 向右，-1 向左</summary>
        public void SwitchTarget(int direction)
        {
            List<Transform> candidates = GetValidTargets();
            if (candidates.Count == 0) { Unlock(); return; }

            // 按屏幕 X 坐标从左到右排序
            candidates.Sort((a, b) =>
            {
                float ax = Camera.main.WorldToScreenPoint(a.position).x;
                float bx = Camera.main.WorldToScreenPoint(b.position).x;
                return ax.CompareTo(bx);
            });

            int currentIndex = candidates.IndexOf(CurrentTarget);
            int nextIndex = (currentIndex + direction + candidates.Count) % candidates.Count;
            LockOnTo(candidates[nextIndex]);
        }

        // ── 私有方法 ─────────────────────────────────────────────────

        private void HandleInput_LockOn() { } // reserved

        private void CheckAutoUnlock()
        {
            // 目标失效：尝试切换，否则解锁
            if (CurrentTarget == null || !CurrentTarget.gameObject.activeInHierarchy)
            {
                TryAutoSwitch();
                return;
            }

            // 超出范围：解锁
            if (Vector3.Distance(transform.position, CurrentTarget.position) > autoUnlockRange)
                Unlock();
        }

        private void TryAutoSwitch()
        {
            List<Transform> candidates = GetValidTargets();
            if (candidates.Count > 0)
                LockOnTo(candidates[0]);   // GetValidTargets 已按屏幕中心距离排序
            else
                Unlock();
        }

        private void LockOnTo(Transform target)
        {
            IsLockedOn = true;
            CurrentTarget = target;

            // 切换摄像机 LookAt
            if (virtualCamera != null)
                virtualCamera.LookAt = _lockOnLookTarget;

            // 更新光圈位置
            if (lockOnRingPrefab != null)
            {
                if (_ringInstance == null)
                    _ringInstance = Instantiate(lockOnRingPrefab);

                _ringInstance.transform.SetParent(CurrentTarget);
                _ringInstance.transform.localPosition = Vector3.up * ringHeadOffset;
                _ringInstance.transform.localRotation = Quaternion.identity;
            }
        }

        private void UpdateLookTarget()
        {
            if (CurrentTarget == null) return;

            // LookAt 目标偏向玩家侧，使玩家和敌人都在画面内
            _lockOnLookTarget.position = Vector3.Lerp(
                transform.position + Vector3.up * 1.0f,
                CurrentTarget.position + Vector3.up * 1.0f,
                0.35f
            );
        }

        private Transform FindBestTarget()
        {
            List<Transform> candidates = GetValidTargets();
            return candidates.Count > 0 ? candidates[0] : null;
        }

        /// <summary>
        /// 返回所有满足条件的目标，按与屏幕中心的距离升序排列。
        /// 条件：范围内 + 摄像机前方 + 无遮挡
        /// </summary>
        private List<Transform> GetValidTargets()
        {
            Collider[] colliders = Physics.OverlapSphere(transform.position, lockOnRange, enemyLayer);
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            var results = new List<(Transform t, float screenDist)>();

            foreach (Collider col in colliders)
            {
                Transform t = col.transform;
                Vector3 dir = (t.position - transform.position).normalized;

                // 视野过滤：必须在摄像机前方（dot > lockOnFovDot）
                if (Vector3.Dot(Camera.main.transform.forward, dir) < lockOnFovDot)
                    continue;

                // 视线遮挡：从玩家胸口到敌人胸口做射线，~enemyLayer 检测障碍物
                // 注意：需要确保玩家自身不在 enemyLayer 上，否则射线会命中自身
                if (Physics.Linecast(
                    transform.position + Vector3.up * 1.5f,
                    t.position + Vector3.up * 1.0f,
                    ~enemyLayer))
                    continue;

                Vector2 screenPos = Camera.main.WorldToScreenPoint(t.position);
                float dist = Vector2.Distance(screenPos, screenCenter);
                results.Add((t, dist));
            }

            results.Sort((a, b) => a.screenDist.CompareTo(b.screenDist));

            var list = new List<Transform>();
            foreach (var r in results) list.Add(r.t);
            return list;
        }

        private void DestroyRing()
        {
            if (_ringInstance != null)
            {
                Destroy(_ringInstance);
                _ringInstance = null;
            }
        }
    }
}
```

- [ ] **Step 2: Unity 编辑器验证编译**

等待 Unity 重新编译，Console 无红色错误。

- [ ] **Step 3: 提交**

```bash
git add Assets/Game/Character/Player/LockOnManager.cs
git commit -m "feat: add LockOnManager with target detection, camera switching, and ring indicator"
```

---

## Task 3: PlayerController — 新增 FaceTarget 方法

**Files:**
- Modify: `Assets/Game/Character/Player/PlayerController.cs`

- [ ] **Step 1: 在 PlayerController.cs 中，`RotateInstantly` 方法之后添加 `FaceTarget`**

在第 53-55 行的 `RotateInstantly` 方法后、类结束括号前插入：

```csharp
        /// <summary>
        /// 锁定模式下每帧调用，将模型旋转朝向目标（忽略Y轴高度差）。
        /// </summary>
        public void FaceTarget(Transform target)
        {
            Vector3 dir = target.position - transform.position;
            dir.y = 0;
            if (dir.sqrMagnitude > 0.001f)
                model.rotation = Quaternion.Slerp(
                    model.rotation,
                    Quaternion.LookRotation(dir),
                    Time.deltaTime * RotateSpeed
                );
        }
```

- [ ] **Step 2: 在 LockOnManager.Update() 中调用 FaceTarget**

在 `LockOnManager.cs` 的 `Update()` 方法里，当 `IsLockedOn` 时加一行 FaceTarget 调用。
修改 `LockOnManager.Update()` 中 `if (IsLockedOn)` 块：

```csharp
        private void Update()
        {
            HandleInput();

            if (IsLockedOn)
            {
                CheckAutoUnlock();
                UpdateLookTarget();
                FacePlayerToTarget();    // ← 新增这一行
            }
        }
```

并在 LockOnManager.cs 中添加 `_playerController` 字段和 `FacePlayerToTarget()` 方法：

在 `LockOnManager` 类的字段区末尾（`_ringInstance` 下方）添加：

```csharp
        private PlayerController _playerController;
```

在 `Awake()` 中初始化（`_lockOnLookTarget` 创建代码之后）：

```csharp
            _playerController = GetComponent<PlayerController>();
```

在类的私有方法区添加：

```csharp
        private void FacePlayerToTarget()
        {
            if (CurrentTarget != null && _playerController != null)
                _playerController.FaceTarget(CurrentTarget);
        }
```

`GetComponent<PlayerController>()` 要求 `LockOnManager` 和 `PlayerController` 在同一个 GameObject 上。

需要在文件顶部添加 using：
```csharp
using GameMain2.Scripts.Character;
```

- [ ] **Step 3: 验证编译**

Unity 编译无错误。

- [ ] **Step 4: 提交**

```bash
git add Assets/Game/Character/Player/PlayerController.cs \
        Assets/Game/Character/Player/LockOnManager.cs
git commit -m "feat: add FaceTarget to PlayerController, wire rotation in LockOnManager"
```

---

## Task 4: PlayerStateMachine — 暴露 LockOnManager 引用

**Files:**
- Modify: `Assets/Game/Character/Player/PlayerStateMachine.cs`

- [ ] **Step 1: 添加字段和属性**

在 `PlayerStateMachine.cs` 中，在 `playerController` 字段声明（第 18 行）之后添加：

```csharp
        [SerializeField]
        private LockOnManager lockOnManager;

        public LockOnManager LockOnManager => lockOnManager;
```

在文件顶部 using 区添加（若尚无）：

```csharp
using Game.Character.Player;
```

完整修改后的字段区（第 15-27 行附近）：

```csharp
        private FsmBase<PlayerStateMachine> m_AniFsm;

        [SerializeField]
        private PlayerController playerController;

        [SerializeField]
        private LockOnManager lockOnManager;

        public LockOnManager LockOnManager => lockOnManager;

        public PlayerController PlayerController
        {
            get => playerController;
        }
```

- [ ] **Step 2: 验证编译**

Unity 编译无错误。

- [ ] **Step 3: 提交**

```bash
git add Assets/Game/Character/Player/PlayerStateMachine.cs
git commit -m "feat: expose LockOnManager reference on PlayerStateMachine"
```

---

## Task 5: WalkState — 锁定时改用横移移动

**Files:**
- Modify: `Assets/Game/Character/Player/PlayerFsm/WalkState.cs`

- [ ] **Step 1: 修改 Update() — 锁定时跳过 Rotate 调用**

将 `WalkState.Update()` 中第 76-83 行的移动/旋转块替换为带锁定分支的版本：

原代码（第 65-83 行）：
```csharp
            Vector2 move = InputManager.Instance.GetMoveDirection();

            Vector2 moveRaw = InputManager.Instance.GetMoveDirectionRaw();
            if (moveRaw.sqrMagnitude == 0)
            {
                if (move.sqrMagnitude == 0)
                {
                    fsm.ChangeState<IdleState>();
                }
            }
            else
            {
                PlayerController playerController = fsm.Owner.PlayerController;

				//更新移动的方向，旋转模型
                float y = Camera.main.transform.eulerAngles.y;
                Vector3 targetDir = Quaternion.Euler(new Vector3(0, y, 0)) * new Vector3(moveRaw.x, 0, moveRaw.y).normalized;
                playerController.Rotate(targetDir.normalized);
            }
```

替换为：
```csharp
            Vector2 move = InputManager.Instance.GetMoveDirection();
            Vector2 moveRaw = InputManager.Instance.GetMoveDirectionRaw();

            if (moveRaw.sqrMagnitude == 0)
            {
                if (move.sqrMagnitude == 0)
                {
                    fsm.ChangeState<IdleState>();
                }
            }
            else
            {
                LockOnManager lockOn = fsm.Owner.LockOnManager;
                bool isLockedOn = lockOn != null && lockOn.IsLockedOn && lockOn.CurrentTarget != null;

                if (!isLockedOn)
                {
                    // 普通模式：相对摄像机方向旋转（原有逻辑）
                    PlayerController playerController = fsm.Owner.PlayerController;
                    float y = Camera.main.transform.eulerAngles.y;
                    Vector3 targetDir = Quaternion.Euler(new Vector3(0, y, 0)) * new Vector3(moveRaw.x, 0, moveRaw.y).normalized;
                    playerController.Rotate(targetDir.normalized);
                }
                // 锁定模式：旋转由 LockOnManager.FacePlayerToTarget() 每帧处理，此处无需调用 Rotate
            }
```

- [ ] **Step 2: 修改 OnAnimtorMove() — 锁定时改用横移移动**

将 `OnAnimtorMove` 方法整体替换：

原代码（第 98-103 行）：
```csharp
        private void OnAnimtorMove(object sender, EventArgsBase e)
        {
            PlayerStateMachine s = (PlayerStateMachine)sender;
            PlayerRootMotionEventArgs eventArgs = (PlayerRootMotionEventArgs)e;
            s.PlayerController.Move(new Vector3(eventArgs.Position.x,-1,eventArgs.Position.z));
        }
```

替换为：
```csharp
        private void OnAnimtorMove(object sender, EventArgsBase e)
        {
            PlayerStateMachine s = (PlayerStateMachine)sender;
            PlayerRootMotionEventArgs eventArgs = (PlayerRootMotionEventArgs)e;

            LockOnManager lockOn = s.LockOnManager;
            bool isLockedOn = lockOn != null && lockOn.IsLockedOn && lockOn.CurrentTarget != null;

            if (isLockedOn)
            {
                // 锁定模式：以玩家→目标为前方，WASD 直接驱动横移
                Vector2 moveRaw = InputManager.Instance.GetMoveDirectionRaw();
                Vector3 forward = lockOn.CurrentTarget.position - s.PlayerController.transform.position;
                forward.y = 0;
                forward.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                Vector3 moveDir = forward * moveRaw.y + right * moveRaw.x;
                float speed = lockOn.LockOnMoveSpeed * Time.deltaTime;
                s.PlayerController.Move(new Vector3(moveDir.x * speed, -1f, moveDir.z * speed));
            }
            else
            {
                // 普通模式：使用根动画运动（原有逻辑）
                s.PlayerController.Move(new Vector3(eventArgs.Position.x, -1f, eventArgs.Position.z));
            }
        }
```

在文件顶部确认已有（或添加）：
```csharp
using Game.Character.Player;
```

- [ ] **Step 3: 验证编译**

Unity 编译无错误。

- [ ] **Step 4: 提交**

```bash
git add Assets/Game/Character/Player/PlayerFsm/WalkState.cs
git commit -m "feat: add lock-on strafe movement branch to WalkState"
```

---

## Task 6: Unity 编辑器配置与整体验证

**Files:**（仅 Unity Inspector 操作，不修改代码文件）

- [ ] **Step 1: 添加 LockOnManager 组件到玩家**

1. 在 Hierarchy 中找到玩家 GameObject（挂有 `PlayerController` 和 `PlayerStateMachine` 的那个）
2. Inspector → Add Component → 搜索 `LockOnManager` → 添加

- [ ] **Step 2: 配置 LockOnManager Inspector 字段**

| 字段 | 值 |
|------|----|
| Lock On Range | 15 |
| Auto Unlock Range | 18 |
| Lock On Fov Dot | 0 |
| Enemy Layer | 选择敌人所在的 Layer（如 Enemy） |
| Virtual Camera | 拖入场景中控制玩家视角的 Cinemachine VirtualCamera |
| Player Head Transform | 拖入玩家头部 Transform（或 VCam 原来的 LookAt 目标） |
| Lock On Ring Prefab | 暂时留空（Step 4 创建后再填入） |
| Ring Head Offset | 2 |
| Lock On Move Speed | 4 |

- [ ] **Step 3: 配置 PlayerStateMachine 的 LockOnManager 字段**

在玩家 Inspector 的 `PlayerStateMachine` 组件中，将新出现的 `Lock On Manager` 字段拖入同一 GameObject 上的 `LockOnManager` 组件。

- [ ] **Step 4: 创建 LockOnRing 预制体**

1. 在 Hierarchy 中创建一个空 GameObject，命名 `LockOnRing`
2. 添加子 GameObject → 添加 `Sprite Renderer` 组件
   - Sprite：选择一个圆环形贴图（或使用 Unity 内置 Circle 图片，Scale 调为扁平）
   - Color：`#FFE066`，Alpha 255
   - Order in Layer：10（确保渲染在角色之上）
3. 给 `LockOnRing` 根 GameObject 添加一个脚本组件 `LockOnRingRotator`，或者直接在 `LockOnRing` 根上添加如下脚本让圆环自转：

```csharp
// 文件: Assets/Game/Character/Player/LockOnRingRotator.cs
using UnityEngine;

namespace Game.Character.Player
{
    public class LockOnRingRotator : MonoBehaviour
    {
        [SerializeField] private float rotateSpeed = 45f;

        private void Update()
        {
            transform.Rotate(Vector3.forward, rotateSpeed * Time.deltaTime, Space.Self);
        }
    }
}
```

4. 将 `LockOnRing` 拖入 Project 窗口（如 `Assets/Res/Prefabs/`），创建预制体
5. 删除 Hierarchy 中的临时 `LockOnRing` 对象
6. 将预制体拖入 `LockOnManager` Inspector 的 `Lock On Ring Prefab` 字段

- [ ] **Step 5: 进入 Play Mode 验证**

按 `Ctrl+P` 进入 Play Mode，逐项验证：

**锁定/解锁：**
- [ ] 视野内有敌人时按鼠标中键 → 敌人头顶出现金黄光圈，摄像机开始朝向中间点
- [ ] 再次按鼠标中键 → 光圈消失，摄像机恢复自由

**朝向：**
- [ ] 锁定后站立不动 → 玩家模型持续面朝目标敌人
- [ ] 锁定后绕目标走动 → 模型始终朝向目标

**移动（WalkState）：**
- [ ] 锁定后按 W → 向敌人方向前进
- [ ] 锁定后按 S → 远离敌人后退
- [ ] 锁定后按 A → 向左横移（不转身）
- [ ] 锁定后按 D → 向右横移（不转身）

**目标切换：**
- [ ] 场景中有 2+ 敌人，锁定后滚轮向上 → 切换到右侧敌人（光圈移动）
- [ ] 滚轮向下 → 切换到左侧敌人

**自动解锁：**
- [ ] 锁定后跑到 18m 外 → 自动解锁
- [ ] 锁定后目标敌人被击败（Disable/Destroy） → 自动切换到下一个敌人或解锁

- [ ] **Step 6: 确认无报错后提交**

```bash
git add Assets/Game/Character/Player/LockOnRingRotator.cs
git commit -m "feat: add LockOnRingRotator for ring self-rotation animation"
```

---

## 常见问题排查

| 现象 | 可能原因 | 处理 |
|------|----------|------|
| 按中键无反应 | `enemyLayer` 未配置 或 附近没有该 Layer 的 Collider | 确认敌人 GameObject 的 Layer 设置和 `enemyLayer` Mask 匹配 |
| 视线检测误判（总是锁不上） | 射线打到玩家自身 | 确认玩家 GameObject 的 Layer **不包含**在 `enemyLayer` 中；或临时注释掉 `Physics.Linecast` 那一行测试 |
| 锁定时摄像机不动 | `virtualCamera` 字段未赋值 | 在 Inspector 中将场景里的 Cinemachine VirtualCamera 拖入 |
| 锁定时移动方向错误 | `fsm.Owner.LockOnManager` 为 null | 确认 `PlayerStateMachine` Inspector 的 `Lock On Manager` 字段已赋值 |
| 光圈不出现 | `lockOnRingPrefab` 未赋值 | 将预制体拖入 `LockOnManager` Inspector |
