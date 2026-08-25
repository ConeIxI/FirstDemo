# Player Animator 与状态机迁移设计

## 目标

将 `Scene1` 中 `Player/Modular Knight_Prefab` 上的 `Animator` 和 `PlayerStateMachine` 迁移到 `Player` 根节点，同时保持当前移动、转向、Root Motion、锁定和相机跟随行为不变。

本次只修改 `Assets/Scenes/Scene1.unity` 中现有 Player，不创建或修改 Player Prefab，不修改任何 `.controller` 文件，不编写测试文件或测试代码。

## 当前结构与行为

当前对象结构：

```text
Player
├─ CharacterController
├─ PlayerController
├─ LockOnManager
├─ CameraTarget
└─ Modular Knight_Prefab
   ├─ Animator
   ├─ PlayerStateMachine
   └─ 模型、骨骼与装备挂点
```

当前职责划分：

- `Player` 是物理位移根。`PlayerController.Move` 通过 Unity `CharacterController` 移动该节点。
- `Modular Knight_Prefab` 是视觉朝向根。`PlayerController.Rotate` 和 `RotateInstantly` 只旋转 `model` 引用指向的该节点。
- `Animator` 位于视觉朝向根，因此当前读取到的 `animator.deltaPosition` 已包含视觉模型朝向。
- `PlayerStateMachine.OnAnimatorMove` 把 Animator 的根运动发布为 `PlayerRootMotionEventArgs`。
- Locomotion、Dodge、Attack 和 Skill 状态分别消费根运动事件，并最终移动 Player 根节点。
- NormalCamera 和 LockCamera 的 Follow 都指向 `Player`。
- `Player/CameraTarget` 是 Player 的直接子节点，也是锁定相机 Target Group 中的玩家目标。

## 目标结构

```text
Player
├─ Animator
├─ PlayerStateMachine
├─ CharacterController
├─ PlayerController
├─ LockOnManager
├─ CameraTarget
└─ Modular Knight_Prefab
   └─ 模型、骨骼与装备挂点
```

迁移后仍保持以下单一职责：

- Player：物理位移根、Animator 宿主、状态机宿主、Cinemachine Follow 目标。
- Modular Knight_Prefab：视觉朝向根及 Avatar 骨骼层级。
- CameraTarget：稳定的玩家锁定取景点，不跟随视觉模型局部转向。

## 方案选择

### 采用方案：保持视觉根独立，并统一换算 Root Motion

Animator 上移后，其宿主 Player 不随视觉转向旋转。状态机在 `OnAnimatorMove` 中使用 Player 根和视觉模型之间的旋转差，把 Animator 根运动换算到视觉模型当前朝向，再发布事件。

优点：

- 保持当前 Player 物理根稳定。
- 不改变两台 Cinemachine 虚拟相机的 Follow 语义。
- 不改变锁定检测、重力、CharacterController 和 CameraTarget 的坐标基准。
- Root Motion 补偿集中在唯一事件入口，所有状态共享同一真相来源。

### 不采用方案：旋转 Player 根节点

让 Player 和模型一起转向虽然能让 Animator 根运动自然沿角色朝向，但会同时旋转 CharacterController、CameraTarget 和 Cinemachine Follow 目标，改变当前相机与锁定系统的既有坐标约束，影响范围更大。

### 不采用方案：各状态分别换算位移

在 Locomotion、Dodge、Attack 和 Skill 中分别补偿会复制坐标变换规则，容易出现某些状态遗漏或重复旋转。根运动应在发布事件前统一转换。

## Root Motion 设计

迁移后的 `Animator.transform.rotation` 来自 Player 根，角色真实视觉朝向来自 `PlayerController.Model.rotation`。统一方向偏移为：

```csharp
Quaternion directionOffset =
    playerController.Model.rotation *
    Quaternion.Inverse(animator.transform.rotation);

Vector3 worldDeltaPosition = directionOffset * animator.deltaPosition;
```

`PlayerStateMachine.OnAnimatorMove` 使用 `worldDeltaPosition` 构造 `PlayerRootMotionEventArgs`。

约束：

- 只转换 `deltaPosition`，不要让 Animator 自动修改 Player Transform。
- `Animator.Apply Root Motion` 保持开启，以确保 Unity 计算 `deltaPosition` 和 `deltaRotation`，实际位移仍由 CharacterController 执行。
- `deltaRotation` 保持动画提供的增量，不在事件入口乘入模型当前朝向。
- `SkillState` 继续以 `Model.rotation * deltaRotation` 更新视觉模型，防止累计旋转被重复计算。
- Locomotion、Dodge、Attack 的水平位移与固定下压规则保持不变。
- 不在任何具体状态中增加第二次方向补偿。

## 组件迁移与引用

迁移时复制旧 Animator 的以下配置到 Player 上的新 Animator：

- Avatar：`ModularKnight_CompleteAvatar`
- Runtime Animator Controller：现有 Player Controller 引用
- Apply Root Motion：开启
- Update Mode：Normal
- Culling Mode：Cull Update Transforms
- 其余 Animator 序列化选项保持现值

迁移时复制旧 PlayerStateMachine 的所有配置，并设置：

- `animator`：Player 上的新 Animator
- `playerController`：Player 上的 PlayerController
- `weaponHandler`：Player 上的 WeaponHandler
- `LockOnManager`：Player 上的 LockOnManager
- `walkSpeed`、`runSpeed`、`moveAnimBlendSpeed`：保持当前值

必须保持：

- `PlayerController.model` 继续指向 `Modular Knight_Prefab`。
- `PlayerController.rightHandHolder` 等骨骼和装备挂点引用不变。
- 新组件启用并验证后，删除 `Modular Knight_Prefab` 上的旧 Animator 和旧 PlayerStateMachine，场景中只保留一套。
- 不允许新旧 PlayerStateMachine 同时运行，避免重复更新 FSM、重复发布 Root Motion 和重复订阅战斗事件。

## Avatar 与层级约束

Humanoid Animator 可以位于骨骼祖先 Player 上，但 Player 必须继续包含完整的 `Modular Knight_Prefab` 骨骼子树。迁移后应在 Inspector 和 Play Mode 中确认 Animator Avatar 有效且没有 Avatar 层级不匹配警告。

不调整以下内容：

- `Modular Knight_Prefab` 的局部位置、旋转和缩放。
- 模型内部骨骼层级。
- SkinnedMeshRenderer 的 Root Bone 和 Bones。
- 装备与武器挂点。
- 任何 Animator Controller 资源。

## 相机与锁定约束

当前相机配置应原样保留：

- NormalCamera Follow：Player
- LockCamera Follow：Player
- LockCamera LookAt：Target Group
- LockOnManager.playerHeadTransform：`Player/CameraTarget`
- Target Group 的玩家目标：`Player/CameraTarget`
- CameraTarget 局部位置约为 `(0, 0.6, 0)`

Player 根节点不参与角色视觉朝向旋转，因此普通移动转向、锁定面向敌人和技能旋转不会把 Follow 根或 CameraTarget 绕角色中心旋转。迁移不应修改 Cinemachine 阻尼、偏移、优先级或 Target Group 权重。

## 实施顺序

1. 记录 Animator 和 PlayerStateMachine 当前序列化属性与对象引用。
2. 修改 `PlayerStateMachine.OnAnimatorMove`，加入统一 Root Motion 方向换算。
3. 在 Player 上添加并配置 Animator。
4. 在 Player 上添加并配置 PlayerStateMachine。
5. 确认 PlayerStateMachine 引用新 Animator，其余引用仍指向 Player 现有组件。
6. 确认 PlayerController.model 仍指向 `Modular Knight_Prefab`。
7. 移除模型节点上的旧 PlayerStateMachine 和 Animator。
8. 确认两台虚拟相机、CameraTarget 和 Target Group 引用未变化。
9. 保存 `Scene1`，使用 `$CLI compile unity` 验证 Unity 编译。
10. 进入 Play Mode 执行手工行为验证并检查 Error/Exception 日志。

## 验收标准

### 结构与引用

- Player 上各有且只有一个 Animator 和 PlayerStateMachine。
- `Modular Knight_Prefab` 上不再存在 Animator 或 PlayerStateMachine。
- Animator 正常驱动现有模型与骨骼。
- PlayerStateMachine 的 Animator、PlayerController、WeaponHandler、LockOnManager 引用均有效。
- PlayerController.model 仍为 `Modular Knight_Prefab`。

### 移动与战斗

- 非锁定状态下，前后左右及斜向移动时，模型朝向和实际位移一致，无侧滑或世界轴偏移。
- 相机绕玩家旋转后再移动，移动方向仍相对于相机正确。
- 锁定状态下，模型始终面向目标，四方向移动与动画方向一致。
- 非锁定闪避沿输入和相机修正后的方向移动。
- 锁定闪避保持面向目标并使用正确的方向 BlendTree。
- 普攻根运动仍只使用水平位移并保持贴地。
- 武器技能的位移和动画旋转都正确，不发生重复转向或反向突进。
- 重力、落地、受击、失衡、倒地和死亡状态没有位置跳变。

### 相机

- 普通相机持续跟随 Player，没有因模型转向产生横向绕行或抖动。
- 锁定和解锁切换时无明显位置跳变。
- 锁定相机仍以 `Player/CameraTarget` 与敌方 CameraTarget 组成 Target Group。
- 闪避、攻击和技能 Root Motion 期间相机连续跟随，无滞留在旧位置的现象。

### 工程约束

- `$CLI compile unity` 成功。
- Play Mode 日志无 Animator、Avatar、MissingReference、事件重复订阅相关 Error 或 Exception。
- 没有修改任何 `.controller` 文件。
- 没有新增测试文件或测试代码。

## 回退边界

实施前只涉及一个脚本与 `Scene1`。若 Avatar 无法在 Player 根正确绑定完整骨骼层级，停止迁移场景组件并保留原结构；不要通过修改 Animator Controller 或骨骼资源绕过 Avatar 绑定问题。
