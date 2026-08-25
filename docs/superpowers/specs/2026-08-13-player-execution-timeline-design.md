# 玩家处决 Timeline 设计

**日期**：2026-08-13  
**项目**：FirstGameDemo（Unity 2022.3.61f1c1）

## 1. 目标

使用 Unity Timeline 实现玩家处决系统：敌人失衡后，玩家在有效范围内按下普攻击键，优先触发当前武器的专属处决 Timeline。处决通过 Timeline 中段信号对目标造成一次基于目标最大生命值百分比的高额伤害，敌人可能存活。

系统必须支持多武器、运行时目标绑定、玩家与敌人配对动画、镜头编排、暂停、异常清理及后续新增武器。不得修改任何 `.controller` 文件，不编写测试文件或测试代码。

## 2. 已确认决策

| 项目 | 决策 |
|---|---|
| 总体方案 | 每种武器使用专属 Timeline，运行时控制器统一编排处决 |
| Timeline 复用 | 同一 `WeaponType` 的武器共用对应 Timeline，不按武器实例复制资源 |
| 缺失配置 | 直接报错并拒绝处决，不回退默认 Timeline |
| 伤害 | 每种武器配置目标最大生命值百分比 |
| 伤害时机 | Timeline 中段 Damage Signal 结算一次 |
| 目标选择 | 当前锁定目标优先；否则选最近、前方、无遮挡的失衡敌人 |
| 拔刀 | 未拔刀时直接进入战斗持武器状态，Timeline 负责处决起手表现 |
| 对位 | 自定义 Transform 轨道按敌人根节点和相对偏移对齐玩家 |
| 敌人资源要求 | 可处决敌人必须使用兼容的 Humanoid Avatar |
| 玩家保护 | Timeline 播放期间玩家无敌 |
| 输入 | 进入处决状态后除暂停键外所有玩家操作均不响应 |
| 敌人锁定 | 处决期间暂停 AI、移动、攻击，并拒绝外部战斗结算 |
| 暂停 | Timeline 使用 Game Time；暂停时处决整体停止，暂停菜单使用非缩放时间 |
| 存活结果 | 敌人解除处决锁定并继续剩余失衡窗口 |
| 死亡结果 | 进入现有死亡数据流程，Timeline 继续播放剩余处决表现 |

## 3. 系统架构

```text
PlayerStateMachine
└── ExecutionState
    └── PlayerExecutionController
        ├── ExecutionTargetSelector
        ├── ExecutionTimelineConfig
        ├── PlayableDirector
        └── ExecutionTimelineContext

武器专属 Timeline
├── ExecutionTransformTrack
├── Player Animation Track
├── Enemy Animation Track
├── Cinemachine Track
└── Signal Track
    ├── Damage Signal
    └── Complete Signal
```

### 3.1 ExecutionState

玩家处决流程的唯一 FSM 状态。进入后清除普通战斗输入缓存，并阻断移动、普攻、技能、防御、闪避、锁定切换和武器切换。暂停键仍由全局暂停入口响应。

该状态不直接实现目标搜索、Timeline 绑定或伤害计算，只负责处决生命周期与玩家 FSM 的衔接。

### 3.2 PlayerExecutionController

处决流程的唯一编排者，职责包括：

- 接收候选目标与当前武器类型；
- 校验武器处决配置和目标 Humanoid 兼容性；
- 立即切换为持武器战斗状态；
- 锁定玩家输入和敌人运行时行为；
- 创建本次 `ExecutionTimelineContext`；
- 动态绑定 Timeline 轨道；
- 启动 `PlayableDirector`；
- 响应伤害、完成和停止事件；
- 通过同一个幂等清理入口恢复双方状态。

处决控制器是处决状态的单一真相。自定义轨道、Signal Receiver 和敌人组件不得分别保存另一套独立的处决生命周期状态。

### 3.3 ExecutionTargetSelector

目标选择器只负责查询，不改变双方状态。选择规则按以下顺序执行：

1. 若当前锁定目标存活、失衡、未被其他处决占用且处于处决范围内，则直接选中。
2. 否则在玩家周围的处决半径内查询敌人。
3. 过滤死亡、未失衡、已被处决占用、不在玩家前方或被障碍物遮挡的目标。
4. 按玩家与敌人根节点的水平距离升序选择最近目标。

处决半径使用独立配置，不复用当前 15 米锁定范围。若没有有效目标，本次普攻击继续走现有普通攻击流程。

### 3.4 ExecutionTimelineConfig

配置以 `WeaponType` 为强类型键，每项至少包含：

- 专属 `TimelineAsset`；
- 最大生命值伤害百分比，范围为 `(0, 1]`；
- Timeline 轨道绑定标识；
- 可选的处决范围覆盖值，仅在确有武器距离差异时使用。

所有当前可用于处决的武器类型都必须存在唯一配置。重复键、空 Timeline、非法百分比或必要轨道缺失均视为配置错误，初始化时直接报错。

### 3.5 ExecutionTimelineContext

每次处决创建独立上下文，至少保存：

- 玩家根节点与 Animator；
- 敌人根节点、Animator、生命组件、属性组件和 AI 上下文；
- 当前武器类型与伤害百分比；
- 本次播放令牌；
- 伤害是否已结算；
- 敌人进入处决前的失衡剩余时间；
- 双方需要恢复的运行时状态。

Timeline 资源不保存具体场景玩家或敌人引用。所有对象都在播放前通过 `PlayableDirector.SetGenericBinding` 或上下文轨道绑定动态注入。

## 4. 自定义 Transform 轨道

### 4.1 设计目的

`ExecutionTransformTrack` 保存当前武器处决动作相对敌人根节点的对位数据。新增武器只需创建专属 Timeline 并配置相对姿态，不需要给每个敌人 prefab 添加武器锚点。

轨道由以下类型组成：

```text
ExecutionTransformTrack
└── ExecutionTransformClip
    └── ExecutionTransformBehaviour

ExecutionTransformMixerBehaviour
└── 汇总 Clip 权重并最终写入一次玩家 Transform
```

### 4.2 Clip 数据

每个对位 Clip 保存：

- 玩家相对敌人根节点的位置偏移；
- 玩家相对敌人根节点的欧拉角偏移；
- 位置与旋转过渡曲线；
- 是否保留玩家当前世界高度。

对位持续时间直接使用 Timeline Clip 时长，不额外保存移动速度。不同武器通过调整 Clip 时长和曲线控制对位节奏。

### 4.3 运行时计算

Clip 首次获得有效权重时缓存玩家起始世界姿态。每帧根据敌人当前姿态计算目标世界姿态：

```text
目标位置 = EnemyRoot.TransformPoint(相对位置)
目标旋转 = EnemyRoot.rotation × Quaternion.Euler(相对欧拉角)
```

进度使用 `playable.GetTime() / playable.GetDuration()`，再经过配置曲线求权重。位置使用 `Vector3.Lerp`，旋转使用 `Quaternion.Slerp`。Clip 结束帧精确写入目标姿态，避免残留误差。

不得采用 `deltaTime * moveSpeed` 逐帧逼近，因为该方式无法保证在 Clip 结束时到位，并会使结果受到帧率和 Clip 时长影响。

### 4.4 Mixer 与写入边界

最终 Transform 写入统一放在 Mixer 中。Mixer 读取所有输入 Clip 的权重并只写一次玩家根节点，避免重叠 Clip 互相覆盖。

玩家层级职责如下：

```text
PlayerRoot                 <- Transform 轨道对齐
└── ModelRoot / Animator   <- Animation Track 播放处决动画
```

Transform 轨道只在 Timeline 开头的对位 Clip 有效期间写玩家根节点。Clip 结束后停止写入，由后续动画轨道负责动作表现。不得让 Transform 轨道与 Animator 根运动在同一时间修改同一个节点。

自定义轨道只处理位置与旋转，不修改 Rigidbody、CharacterController、AI、输入、无敌、伤害或处决状态。这些状态全部由 `PlayerExecutionController` 管理和恢复。

## 5. 触发与播放流程

```text
普攻击输入
├── 无有效处决目标 -> 发布现有普攻击事件
└── 有有效处决目标
    -> 校验当前武器配置和双方 Animator
    -> 进入 ExecutionState，锁定非暂停输入
    -> 立即切换为持武器战斗状态
    -> 锁定敌人 AI、移动、攻击和外部战斗结算
    -> 创建上下文并动态绑定轨道
    -> 播放 Timeline，给玩家添加无敌状态
        -> Transform Clip 对位
        -> 双方 Animation Track 播放
        -> Damage Signal 结算一次伤害
        -> Complete Signal 或 Director.stopped 收束
```

普攻击入口必须先尝试处决。只有处决未被接管时，才发布现有 `PlayerAttackInputEventArgs`，避免同一次输入同时触发处决和普通攻击。

进入 `ExecutionState` 后立即锁定非暂停输入。玩家无敌从 `PlayableDirector` 成功开始播放时生效，到统一清理完成后解除。Timeline 启动失败时不进入无敌，并立即恢复输入与敌人状态。

## 6. Timeline 轨道约定

每个武器专属 Timeline 必须包含：

1. 一个 `ExecutionTransformTrack`，负责起始对位；
2. 一个玩家 Animation Track，运行时绑定玩家 Humanoid Animator；
3. 一个敌人 Animation Track，运行时绑定敌人 Humanoid Animator；
4. 一个 Cinemachine Track，负责处决镜头；
5. 一个 Damage Signal Marker；
6. 一个 Complete Signal Marker。

推荐时间结构：

```text
0.00s       约 0.20s                 Timeline 结束
|-- 对位 Clip --|
|------------- 双方处决动画 ----------------|
                         ^ Damage Signal
                                           ^ Complete Signal
```

对位时长和曲线由每种武器 Timeline 自行配置，不要求全部为 0.2 秒。

## 7. 伤害结算

Damage Signal 交给当前 `PlayerExecutionController` 处理，不直接在 Signal Receiver 中查找场景对象。

伤害值计算规则：

```text
处决伤害 = CeilToInt(目标最大生命值 × 当前武器处决伤害百分比)
```

伤害必须通过现有战斗能力与生命结算入口执行，以复用生命变化、死亡标签、死亡事件、碰撞关闭和掉落逻辑。处决上下文携带唯一播放令牌和 `DamageApplied` 标记，同一轮播放最多结算一次。重复 Damage Signal 不重复造成伤害。

处决期间目标拒绝其他来源的战斗事件和属性修改；只有持有当前播放令牌的处决伤害入口可以修改目标生命。

## 8. 输入、无敌与暂停

### 8.1 输入规则

进入 `ExecutionState` 后，以下输入全部不响应：

- 移动；
- 普攻击与连段缓存；
- 武器技能；
- 防御与弹反；
- 闪避；
- 锁定、解锁与切换目标；
- 切换武器。

暂停键继续由全局暂停系统处理。暂停菜单关闭后仍回到同一处决播放时间点，不重新绑定或重播 Timeline。

### 8.2 无敌规则

Timeline 成功开始播放后，玩家获得现有能力系统的无敌标签。处决正常完成、异常停止或对象销毁时，由统一清理入口解除该次处决添加的无敌状态。

### 8.3 时间规则

`PlayableDirector.timeUpdateMode` 使用 `DirectorUpdateMode.GameTime`。游戏暂停将 `Time.timeScale` 设为 0 后，Timeline、Transform 对位、双方动画和失衡窗口全部停止。暂停菜单动画与输入使用非缩放时间。

## 9. 敌人处决承受状态

目标被选中后立即进入独占的处决承受状态：

- 停止行为树决策；
- 停止 NavMesh 或现有移动组件；
- 中断正在执行的普通攻击；
- 阻止普通受击、失衡结束和其他动画覆盖 Timeline；
- 拒绝外部战斗事件与属性修改；
- 保存进入处决前的失衡剩余时间。

现有失衡流程以 `Time.time` 计算 Loop 时长。处决期间不能只停止行为树，否则 Timeline 播放时间仍会消耗失衡窗口。实现时必须显式暂停失衡计时，并在恢复时重新基于保存的剩余时间继续计时。

同一敌人同一时间只能被一个处决上下文占用。已被占用的敌人不再作为其他玩家或系统的处决候选。

## 10. 完成与异常恢复

### 10.1 正常完成且敌人存活

- Timeline 播放到 Complete Signal；
- 解除玩家无敌和输入锁定；
- 解除敌人处决承受状态；
- 恢复 AI 与移动；
- 从保存的剩余失衡时间继续失衡窗口；
- 玩家返回 `LocomotionState`，保留正确的持武器战斗状态。

### 10.2 伤害后敌人死亡

- 立即执行现有死亡数据流程，包括死亡状态、碰撞关闭、死亡事件和掉落；
- 禁止现有死亡动画覆盖仍在播放的处决 Timeline；
- Timeline 继续播放剩余处决表现；
- 完成后只清理绑定、玩家无敌和输入锁定，不恢复敌人 AI、移动、稳定值或碰撞。

### 10.3 异常中断

- Damage Signal 前中断：不补发伤害，恢复双方状态和剩余失衡时间。
- Damage Signal 后中断：保留已造成的伤害，不重复结算；存活敌人恢复失衡，死亡敌人保持死亡。
- 玩家或目标销毁、场景卸载、Director 播放失败、Timeline 被外部停止时均进入同一清理入口。

`Complete Signal` 负责正常完成，`PlayableDirector.stopped` 负责统一兜底。两者必须调用同一个幂等清理方法，保证重复回调不会重复移除标签、恢复组件或结算伤害。

## 11. 配置校验与快速失败

以下情况直接记录明确错误并拒绝开始处决：

- 当前武器没有唯一的专属 Timeline 配置；
- 伤害百分比不在合法范围；
- Timeline 缺少必要的 Transform、Animation、Cinemachine 或 Signal 轨道；
- 缺少 Damage Signal 或 Complete Signal；
- 玩家或敌人 Animator 不是有效 Humanoid；
- 运行时轨道绑定失败；
- 敌人不具备处决所需生命、属性或 AI 上下文。

实现不提供默认 Timeline、默认伤害或静默降级。配置问题必须在进入处决前暴露。

## 12. 兼容性与限制

- Unity 版本为 2022.3.61f1c1。
- C# 代码兼容 C# 9.0，不使用更高版本语法。
- 不修改任何 `.controller` 文件。
- 不编写测试文件和测试代码。
- 第一版只支持兼容 Humanoid Avatar 的敌人。
- 第一版每种武器只配置一套处决 Timeline，不实现同武器多处决动作随机选择。
- 第一版只实现正面候选规则，不实现前后左右多方向处决。
- 不给敌人 prefab 添加按武器划分的处决锚点。

## 13. 验证标准

### 13.1 编译

只能使用以下命令验证 Unity 编译：

```powershell
$CLI compile unity
```

`compile dotnet` 只能作为额外检查，不能替代 Unity 编译。

### 13.2 Play Mode 手动验证

不新增自动化测试。至少手动验证：

1. 锁定目标失衡时，范围内按普攻击优先处决。
2. 未锁定时可选择最近、前方、无遮挡的失衡敌人。
3. 没有有效目标时仍执行普通攻击。
4. 未拔刀时能直接持武器进入处决。
5. 单手剑、巨剑、长枪等不同武器选择各自 Timeline。
6. Transform Clip 能在配置时长内精确对齐玩家且无抖动。
7. 处决期间除暂停外的输入全部失效。
8. 暂停后 Timeline、动画和对位停止，恢复后从原时间继续。
9. Damage Signal 只造成一次最大生命值百分比伤害。
10. 敌人存活时恢复到剩余失衡窗口，随后正常结束失衡。
11. 敌人死亡时正常触发死亡数据、碰撞关闭和掉落，且处决表现不中断。
12. 伤害信号前后分别中断 Timeline，双方状态均按规则恢复。
13. 缺失武器 Timeline、必要轨道或 Humanoid 配置时明确报错并拒绝处决。

## 14. 明确不在本次范围内

- 非 Humanoid 敌人的专用处决系统；
- 同武器多套处决动作随机或条件选择；
- 背刺、侧面处决和空中处决；
- 多人同时处决同一目标；
- 处决提示 UI；
- 修改现有 Animator Controller。
