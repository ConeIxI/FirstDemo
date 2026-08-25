# 锁定系统设计文档

**日期**：2026-04-13
**项目**：FirstGameDemo（Unity 2022.3.61f1c1）

---

## 概述

为玩家添加魂系风格的单目标硬锁定系统。锁定时摄像机持续朝向目标，玩家始终面朝目标，WASD 转变为相对目标方向的横移操作。

---

## 核心设计决策

| 决策项 | 选择 |
|--------|------|
| 锁定风格 | 魂系硬锁定（单目标） |
| 移动模式 | 战斗横移（W/S 靠近/后退，A/D 横向平移） |
| 触发按键 | 鼠标中键（切换锁定/解锁） |
| 目标切换 | 鼠标滚轮（上/下循环切换） |
| 视觉标记 | 目标头顶金黄色旋转光圈环 |
| 自动解锁 | 目标死亡、超出距离（18m）、手动按中键 |

---

## 架构

### 新增组件

**`LockOnManager`**（MonoBehaviour，挂在玩家 GameObject 上）

唯一职责：管理锁定状态、目标检测与切换、**自身处理锁定相关输入**（中键、滚轮）、摄像机 LookAt 切换、光圈指示器生命周期。输入检测在 `LockOnManager.Update()` 中进行，不依赖任何 FSM 状态，确保玩家在行走、攻击等任意状态下均可切换锁定。

```
LockOnManager
├── CurrentTarget : Transform      （只读，外部查询用）
├── IsLockedOn : bool              （只读）
├── TryLockOn()                    （按中键时调用）
├── Unlock()                       （解锁，内/外部均可调用）
├── SwitchTarget(int direction)    （+1 向右，-1 向左）
└── Update() / LateUpdate()        （每帧检测有效性、更新光圈朝向）
```

### 修改的现有文件

| 文件 | 修改内容 |
|------|----------|
| `InputManager.cs` | 新增 `IsLockOnPressed()`（中键）、`GetScrollDelta()`（滚轮） |
| `PlayerController.cs` | `Rotate()` 锁定时改为朝向目标 |
| `WalkState.cs` | 移动方向计算增加锁定分支 |
| `IdleState.cs` | 无需修改（输入由 LockOnManager 自身处理） |
| `CharacterStateMachine.cs` | 暴露 Cinemachine VCam 引用，供 LockOnManager 切换 LookAt |

### 不改动

FSM 框架、技能系统、事件系统、现有状态数量。

---

## 目标检测算法

### 初次锁定

```
1. Physics.OverlapSphere(玩家位置, lockOnRange=15m, enemyLayer)
2. 过滤：dot(camera.forward, 玩家→目标方向) > 0（在摄像机前方）
3. 过滤：Physics.Linecast 无障碍物遮挡
4. 按与屏幕中心的距离升序排序
5. 选取距屏幕中心最近的目标
```

### 滚轮切换

```
1. 同上步骤 1-3 获取所有当前有效候选目标
2. 按屏幕空间 X 坐标从左到右排序
3. 滚轮向上 → 下一个，滚轮向下 → 上一个（循环）
```

### 关键参数

| 参数 | 值 | 说明 |
|------|----|------|
| `lockOnRange` | 15m | 最大初始锁定距离 |
| `autoUnlockRange` | 18m | 超出后自动解锁（略大于锁定距离） |
| `lockOnFovDot` | 0.0 | 视野过滤阈值（90° 范围内） |

---

## 自动解锁逻辑（LockOnManager.Update）

```
if (目标为 null 或目标已死亡):
    candidates = GetValidTargets()
    if (candidates 非空): SwitchTarget(最近候选)
    else: Unlock()

if (distance(玩家, 目标) > autoUnlockRange):
    Unlock()
```

---

## 摄像机行为

在玩家身上挂一个 `_lockOnLookTarget` 空 Transform，每帧更新位置：

```csharp
_lockOnLookTarget.position = Vector3.Lerp(
    player.position + Vector3.up * 1.0f,
    target.position + Vector3.up * 1.0f,
    0.35f  // 偏向玩家侧，让双方都在画面中
);
```

锁定/解锁时切换 Cinemachine VCam 的 `LookAt`：

```
锁定：vcam.LookAt = _lockOnLookTarget
解锁：vcam.LookAt = _playerHeadTransform（原始值）
```

摄像机位置控制不变，仍由 Cinemachine 的 Follow + Damping 处理，过渡自然无需额外代码。

---

## 移动与旋转

### 玩家朝向（PlayerController.Rotate）

```csharp
if (lockOnManager.IsLockedOn) {
    Vector3 dir = target.position - transform.position;
    dir.y = 0;
    transform.rotation = Quaternion.Slerp(
        transform.rotation,
        Quaternion.LookRotation(dir),
        rotationSpeed * Time.deltaTime
    );
    return; // 跳过原有逻辑
}
// 原有逻辑不变
```

### WalkState 移动计算

```csharp
if (lockOnManager.IsLockedOn) {
    Vector3 forward = (target.position - player.position);
    forward.y = 0;
    forward.Normalize();
    Vector3 right = Vector3.Cross(Vector3.up, forward);

    Vector3 moveDir = forward * moveRaw.y + right * moveRaw.x;
    playerController.Move(moveDir); // 直接驱动移动，不调用 Rotate()
} else {
    // 现有相机相对移动逻辑不变
}
```

动画参数（`Speed`、`MoveX`、`MoveY`）无需修改，沿用现有 Animator 配置。

---

## UI 光圈指示器

**预制体**：`LockOnRing`，世界空间圆环 Sprite 或 LineRenderer。

**生命周期**：

```
锁定时：Instantiate(lockOnRingPrefab)
        ring.parent = currentTarget
        ring.localPosition = Vector3.up * headOffset

切换目标：ring.parent = newTarget（重设 parent 即可）

解锁时：Destroy(ringInstance)
```

**LateUpdate 始终朝向摄像机**：

```csharp
_ringInstance.transform.rotation =
    Quaternion.LookRotation(
        _ringInstance.transform.position - Camera.main.transform.position
    );
```

**视觉参数**：

| 参数 | 值 |
|------|----|
| 圆环直径 | 1.2f |
| 颜色 | 金黄色 `#FFE066`，带 Bloom |
| 自转速度 | 45°/秒 |
| 头部偏移 | 2.0f（可按敌人类型配置） |

---

## 文件清单

| 文件路径 | 操作 |
|----------|------|
| `Assets/Game/Character/Player/LockOnManager.cs` | 新建 |
| `Assets/Framework/Manager/InputManager.cs` | 修改：新增 2 个方法 |
| `Assets/Game/Character/Player/PlayerController.cs` | 修改：Rotate() |
| `Assets/Game/Character/Player/PlayerFsm/WalkState.cs` | 修改：移动计算 |
| `Assets/Game/Character/Player/PlayerFsm/IdleState.cs` | 无需修改 |
| `Assets/Game/Character/CharacterStateMachine.cs` | 修改：暴露 VCam 引用 |
| `Assets/Res/Prefabs/LockOnRing.prefab` | 新建（美术资产） |
