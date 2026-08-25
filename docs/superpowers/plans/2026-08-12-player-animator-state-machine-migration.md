# Player Animator 与状态机迁移 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将 `Scene1` 中的 Animator 和 PlayerStateMachine 从 `Player/Modular Knight_Prefab` 迁移到 `Player`，并保持现有移动方向、Root Motion、锁定转向与 Cinemachine 跟随行为不变。

**Architecture:** `Player` 继续作为物理位移根、状态机宿主和相机 Follow 目标，`Modular Knight_Prefab` 继续作为独立视觉朝向根。Animator 上移后，`PlayerStateMachine.OnAnimatorMove` 在唯一事件入口把 Player 朝向下的 `deltaPosition` 转换为视觉模型朝向下的世界位移，具体状态不再做额外方向补偿。

**Tech Stack:** Unity 2022.3.61f1c1、C# 9.0、Unity Animator、CharacterController、Cinemachine 2.10.7、AIBridgeCLI

---

## 文件与职责

- Modify: `Assets/Game/Character/Player/PlayerStateMachine.cs:193`
  负责在 Root Motion 事件发布前统一转换位移方向。
- Modify: `Assets/Scenes/Scene1.unity`
  负责 Player 层级上的 Animator、PlayerStateMachine 组件和序列化引用。
- Do not modify: `Assets/Res/AnimatorController/Player/Player.controller`
  本次只复用该控制器引用，禁止修改资源内容。
- Do not create: 测试文件、测试代码或 Player Prefab。

## 执行前置条件

当前 Unity Editor 中 `Scene1` 已处于 Dirty 状态。执行 Task 2 前必须先确认这些未保存内容的归属：

- 若是用户需要保留的改动，先由用户保存，再记录新的 Git 基线。
- 若是不需要的临时改动，只能在用户明确同意后由用户或执行者丢弃。
- 未确认前不得保存 Scene1，不得执行组件迁移，避免把来源不明的场景变化混入本次提交。

## Task 1：统一 Root Motion 方向基准

**Files:**

- Modify: `Assets/Game/Character/Player/PlayerStateMachine.cs:193-204`

- [ ] **Step 1：记录代码和场景基线**

Run:

```powershell
git status --short
git diff -- Assets/Game/Character/Player/PlayerStateMachine.cs Assets/Scenes/Scene1.unity
& './.aibridge/cli/AIBridgeCLI.exe' scene get_active --pretty
```

Expected:

- Git 工作区没有本任务之外的文件改动。
- 当前活动场景为 `Assets/Scenes/Scene1.unity`。
- 若 Unity 仍报告 `isDirty: true`，只执行本 Task 的脚本修改和编译，不保存场景，并在进入 Task 2 前处理前置条件。

- [ ] **Step 2：修改 Root Motion 事件入口**

将 `PlayerStateMachine.OnAnimatorMove` 改为以下实现：

```csharp
/// <summary>按视觉模型朝向换算动画根运动，并发布玩家根运动事件。</summary>
private void OnAnimatorMove()
{
    Quaternion directionOffset =
        playerController.Model.rotation *
        Quaternion.Inverse(animator.transform.rotation);
    Vector3 worldDeltaPosition = directionOffset * animator.deltaPosition;

    if (worldDeltaPosition != Vector3.zero || animator.deltaRotation != Quaternion.identity)
    {
        EventCenter.Instance.Fire(
            this,
            new GameMain2.Game.EventArgs.PlayerRootMotionEventArgs(
                worldDeltaPosition,
                animator.deltaRotation));
    }
}
```

Implementation constraints:

- 不修改 Locomotion、Dodge、Attack 或 Skill 的 Root Motion 消费逻辑。
- 不把 Player 根节点旋转到模型朝向。
- 不对 `animator.deltaRotation` 做方向偏移。
- 不添加空引用防御分支；PlayerStateMachine 的现有初始化约束保证依赖存在并 fast fail。
- 保持 C# 9.0 兼容。

- [ ] **Step 3：检查变更范围**

Run:

```powershell
git diff --check
git diff -- Assets/Game/Character/Player/PlayerStateMachine.cs
```

Expected:

- 只修改 `OnAnimatorMove` 及其中文用途注释。
- 位移只在事件入口转换一次。
- 没有 `.controller` 文件变更。

- [ ] **Step 4：执行 Unity 编译**

Run:

```powershell
& './.aibridge/cli/AIBridgeCLI.exe' compile unity --timeout 120000 --pretty
& './.aibridge/cli/AIBridgeCLI.exe' get_logs get --logType Error --count 50 --pretty
```

Expected:

- Unity compile 成功。
- Error 日志中没有由 `PlayerStateMachine.cs` 引起的编译错误。

- [ ] **Step 5：提交脚本改动**

Run:

```powershell
git add -- Assets/Game/Character/Player/PlayerStateMachine.cs
git diff --cached --check
git commit -m "修复：统一玩家根运动方向基准"
```

Expected:

- 提交只包含 `PlayerStateMachine.cs`。

## Task 2：迁移 Scene1 组件与序列化引用

**Files:**

- Modify: `Assets/Scenes/Scene1.unity`

- [ ] **Step 1：清除场景 Dirty 阻塞并重新确认基线**

在用户已处理执行前置条件后运行：

```powershell
& './.aibridge/cli/AIBridgeCLI.exe' scene get_active --pretty
git status --short
```

Expected:

- `Scene1` 的 `isDirty` 为 `false`。
- Git 状态只允许包含 Task 1 已提交后的既有状态，不存在来源不明的 Scene1 改动。

如果 `isDirty` 仍为 `true`，停止 Task 2，不调用 `scene save`。

- [ ] **Step 2：记录迁移前组件和引用**

Run:

```powershell
$script = @'
inspector get_components --path "Player"
inspector get_components --path "Player/Modular Knight_Prefab"
inspector get_properties --path "Player" --componentName "PlayerController" --includeChildren true
inspector get_properties --path "Player/Modular Knight_Prefab" --componentName "Animator" --includeChildren true
inspector get_properties --path "Player/Modular Knight_Prefab" --componentName "PlayerStateMachine" --includeChildren true
inspector get_properties --path "VirtualCameras/NormalCamera" --componentName "CinemachineVirtualCamera" --includeChildren true
inspector get_properties --path "VirtualCameras/LockCamera" --componentName "CinemachineVirtualCamera" --includeChildren true
inspector get_properties --path "Target Group" --componentName "CinemachineTargetGroup" --includeChildren true
'@
$script | & './.aibridge/cli/AIBridgeCLI.exe' multi --stdin --pretty
```

Expected baseline:

- 旧 Animator：Avatar 为 `ModularKnight_CompleteAvatar`，Controller 为现有 Player Animator Controller，Apply Root Motion 为 true，Update Mode 为 Normal，Culling Mode 为 Cull Update Transforms。
- 旧 PlayerStateMachine：`animator` 指向模型节点 Animator，`playerController`、`weaponHandler`、`LockOnManager` 分别指向 Player 上现有组件。
- `PlayerController.model` 指向 `Modular Knight_Prefab`。
- NormalCamera 和 LockCamera 的 Follow 都指向 Player。
- LockCamera LookAt 指向 Target Group。
- Target Group 玩家目标为 `Player/CameraTarget`。

- [ ] **Step 3：在 Player 上添加 Animator 和 PlayerStateMachine**

Run:

```powershell
& './.aibridge/cli/AIBridgeCLI.exe' inspector add_component --path 'Player' --typeName 'Animator' --pretty
& './.aibridge/cli/AIBridgeCLI.exe' inspector add_component --path 'Player' --typeName 'GameMain2.Scripts.Character.PlayerStateMachine' --pretty
& './.aibridge/cli/AIBridgeCLI.exe' inspector get_components --path 'Player' --pretty
```

Expected:

- Player 上新增一个 Animator 和一个 PlayerStateMachine。
- 旧组件此时仍保留在模型节点，但不要进入 Play Mode，避免两套状态机同时运行。

- [ ] **Step 4：复制 Animator 配置**

使用迁移前已经确认的旧 Animator 属性配置新 Animator：

```powershell
$animatorValues = @{
    'm_Avatar' = 'Assets/_TheTalesFactory/Modular Knights/Models/ModularKnight_Complete.fbx'
    'm_Controller' = 'Assets/Res/AnimatorController/Player/Player.controller'
    'm_CullingMode' = 'Cull Update Transforms'
    'm_UpdateMode' = 'Normal'
    'm_ApplyRootMotion' = $true
    'm_LinearVelocityBlending' = $false
    'm_StabilizeFeet' = $false
    'm_AllowConstantClipSamplingOptimization' = $true
    'm_KeepAnimatorStateOnDisable' = $false
    'm_WriteDefaultValuesOnDisable' = $false
}
$animatorJson = ($animatorValues | ConvertTo-Json -Compress) -replace '"', '\"'
& './.aibridge/cli/AIBridgeCLI.exe' inspector set_properties --path 'Player' --componentName 'Animator' --values $animatorJson --pretty
& './.aibridge/cli/AIBridgeCLI.exe' inspector get_properties --path 'Player' --componentName 'Animator' --includeChildren true --pretty
```

Expected:

- 新旧 Animator 上述属性完全一致。
- 没有修改 `Assets/Res/AnimatorController/Player/Player.controller`。

- [ ] **Step 5：复制 PlayerStateMachine 配置并重绑引用**

自动读取 Player 上各组件的 `instanceId`，写入 PlayerStateMachine：

```powershell
$componentResult = & './.aibridge/cli/AIBridgeCLI.exe' inspector get_components --path 'Player' --raw | ConvertFrom-Json
$components = $componentResult.data.components
$animatorId = ($components | Where-Object fullTypeName -eq 'UnityEngine.Animator').instanceId
$weaponHandlerId = ($components | Where-Object fullTypeName -eq 'Game.Character.Equipment.WeaponHandler').instanceId
$playerControllerId = ($components | Where-Object fullTypeName -eq 'GameMain2.Scripts.Character.PlayerController').instanceId
$lockOnManagerId = ($components | Where-Object fullTypeName -eq 'GameMain2.Scripts.Character.LockOnManager').instanceId

$stateMachineValues = @{
    'moveAnimBlendSpeed' = 2
    'walkSpeed' = 1
    'runSpeed' = 1
    'animator' = $animatorId
    'weaponHandler' = $weaponHandlerId
    'playerController' = $playerControllerId
    'LockOnManager' = $lockOnManagerId
}
$stateMachineJson = ($stateMachineValues | ConvertTo-Json -Compress) -replace '"', '\"'
& './.aibridge/cli/AIBridgeCLI.exe' inspector set_properties --path 'Player' --componentName 'PlayerStateMachine' --values $stateMachineJson --pretty
& './.aibridge/cli/AIBridgeCLI.exe' inspector get_properties --path 'Player' --componentName 'PlayerStateMachine' --includeChildren true --pretty
```

Expected:

- 所有引用都指向 Player 上的组件。
- 数值配置与旧状态机一致。

- [ ] **Step 6：删除模型节点上的旧组件**

先删除旧 PlayerStateMachine，再删除旧 Animator：

```powershell
& './.aibridge/cli/AIBridgeCLI.exe' inspector remove_component --path 'Player/Modular Knight_Prefab' --componentName 'PlayerStateMachine' --pretty
& './.aibridge/cli/AIBridgeCLI.exe' inspector remove_component --path 'Player/Modular Knight_Prefab' --componentName 'Animator' --pretty
```

Expected:

- Player 上各保留一个 Animator 和 PlayerStateMachine。
- `Player/Modular Knight_Prefab` 只保留 Transform 及其模型层级，不再包含 Animator 或 PlayerStateMachine。

- [ ] **Step 7：核对结构、模型引用和相机不变量**

Run:

```powershell
$script = @'
inspector get_components --path "Player"
inspector get_components --path "Player/Modular Knight_Prefab"
inspector get_properties --path "Player" --componentName "PlayerController" --includeChildren true
inspector get_properties --path "Player" --componentName "Animator" --includeChildren true
inspector get_properties --path "Player" --componentName "PlayerStateMachine" --includeChildren true
inspector get_properties --path "Player/CameraTarget" --componentName "Transform" --includeChildren true
inspector get_properties --path "VirtualCameras/NormalCamera" --componentName "CinemachineVirtualCamera" --includeChildren true
inspector get_properties --path "VirtualCameras/LockCamera" --componentName "CinemachineVirtualCamera" --includeChildren true
inspector get_properties --path "Target Group" --componentName "CinemachineTargetGroup" --includeChildren true
'@
$script | & './.aibridge/cli/AIBridgeCLI.exe' multi --stdin --pretty
```

Expected:

- `PlayerController.model` 仍指向 `Modular Knight_Prefab`。
- `rightHandHolder` 等骨骼挂点引用未变化。
- `Player/CameraTarget` 局部位置仍约为 `(0, 0.6, 0)`。
- 两台虚拟相机 Follow、LockCamera LookAt、Target Group 玩家目标均未变化。

- [ ] **Step 8：保存场景并检查磁盘差异**

Run:

```powershell
& './.aibridge/cli/AIBridgeCLI.exe' scene save --pretty
git status --short
git diff --stat -- Assets/Scenes/Scene1.unity
git diff --check -- Assets/Scenes/Scene1.unity
```

Expected:

- 仅 `Assets/Scenes/Scene1.unity` 产生本任务场景改动。
- 没有 `.controller`、模型、Avatar、Prefab 或其他场景资源改动。

- [ ] **Step 9：编译并提交场景迁移**

Run:

```powershell
& './.aibridge/cli/AIBridgeCLI.exe' compile unity --timeout 120000 --pretty
& './.aibridge/cli/AIBridgeCLI.exe' get_logs get --logType Error --count 50 --pretty
git add -- Assets/Scenes/Scene1.unity
git diff --cached --check
git commit -m "重构：迁移玩家动画与状态机组件"
```

Expected:

- Unity compile 成功。
- 没有 Animator、Avatar 或 MissingReference 错误。
- 提交只包含 Scene1。

## Task 3：Play Mode 行为与相机回归验证

**Files:**

- No file changes expected.

- [ ] **Step 1：进入 Play Mode 并确认 Avatar 正常**

Run:

```powershell
& './.aibridge/cli/AIBridgeCLI.exe' editor play --pretty
& './.aibridge/cli/AIBridgeCLI.exe' get_logs get --logType Error --count 50 --pretty
```

Expected:

- Player 模型保持可见，骨骼和蒙皮姿态正常。
- 没有 Avatar 层级不匹配、Animator 未初始化、MissingReference 或空引用异常。

- [ ] **Step 2：验证非锁定移动与相机基准**

手工执行：

1. 分别按前、后、左、右及斜向移动。
2. 用鼠标把普通相机水平旋转约 90 度，再重复移动。
3. 观察模型朝向、Player 实际路径和相机 Follow。

Expected:

- 模型朝向与实际移动方向一致。
- 相机旋转后移动仍相对于相机方向。
- 没有世界轴侧滑、反向移动或模型与碰撞体分离。
- 相机持续跟随 Player，不因模型转向横向绕行或抖动。

- [ ] **Step 3：验证锁定移动和相机切换**

手工执行：

1. 锁定一个有效敌人。
2. 分别进行前后左右移动。
3. 解锁后再次锁定。

Expected:

- 模型持续面向当前目标。
- 四方向移动与 Locomotion BlendTree 方向一致。
- 锁定相机 Target Group 同时包含 `Player/CameraTarget` 和敌人 CameraTarget。
- 锁定与解锁切换无明显位置跳变。

- [ ] **Step 4：验证闪避、攻击和技能 Root Motion**

手工执行：

1. 非锁定状态沿不同输入方向闪避。
2. 锁定状态执行四方向闪避。
3. 执行至少一套带根运动的普通攻击。
4. 执行至少一个同时包含位移和旋转的武器技能。

Expected:

- 非锁定闪避沿相机修正后的输入方向移动。
- 锁定闪避保持面向敌人且方向 BlendTree 正确。
- 普攻水平突进方向与模型朝向一致并保持贴地。
- 技能位移和旋转各执行一次，无重复旋转、反向突进或世界轴偏移。
- 所有动作期间相机连续跟随 Player。

- [ ] **Step 5：验证重力、受击和结束状态**

手工执行：

1. 让玩家经历落地或有高差的移动。
2. 触发普通受击、失衡或倒地。
3. 条件允许时触发死亡状态。

Expected:

- 重力和 CharacterController 碰撞正常。
- 状态切换前后 Player 与模型没有位置跳变。
- 动画状态机没有重复更新或重复事件订阅迹象。

- [ ] **Step 6：退出 Play Mode 并做最终验证**

Run:

```powershell
& './.aibridge/cli/AIBridgeCLI.exe' editor stop --pretty
& './.aibridge/cli/AIBridgeCLI.exe' get_logs get --logType Error --count 100 --pretty
& './.aibridge/cli/AIBridgeCLI.exe' compile unity --timeout 120000 --pretty
git status --short
git log -3 --oneline
```

Expected:

- Error/Exception 日志为空，尤其没有 Animator、Avatar、MissingReference 或事件重复订阅问题。
- Unity compile 再次成功。
- Play Mode 未产生额外文件改动。
- 最近提交包含脚本 Root Motion 修正和 Scene1 组件迁移两个简体中文提交。

## 完成条件

- Player 上各有且只有一个 Animator 和 PlayerStateMachine。
- `Modular Knight_Prefab` 保持视觉模型根职责且不再挂载上述两个组件。
- 所有 Root Motion 动作均沿视觉模型当前朝向移动。
- Player 根、CameraTarget 和 Cinemachine Follow 关系保持稳定。
- Unity 编译与完整 Play Mode 验收均通过。
- 没有修改任何 `.controller` 文件，没有新增测试文件或测试代码。
