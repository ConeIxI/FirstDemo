# 敌人纯行为树架构设计

## 目标

敌人 AI 仅使用行为树做决策和执行，移除敌人域内的 FSM、状态标识和意图中转层。保留现有行为树资产及其优先级结构：死亡、失衡、受击、攻击、追击、搜索、巡逻。

项目通用 FSM 框架和玩家 FSM 不在本次范围内。

## 现状

当前 `AIController` 在每帧完成感知后运行行为树，行为树叶子节点向 `EnemyBlackboard` 写入 `EnemyIntent`，控制器再把意图转换为 `EnemyStateId` 并驱动 `FsmBase<AIController>`。巡逻、追击、搜索、攻击、受击、失衡和死亡的实际动作分散在 `AI/States` 目录。

该双层结构让同一个行为同时存在行为树选择、意图映射和 FSM 执行三个入口，优先级与生命周期难以统一。

## 设计

`AIController` 只负责初始化组件上下文、更新感知事实并 Tick 行为树。它不再持有 `FsmBase`、待切换状态或状态优先级逻辑。

`EnemyBlackboard` 仅保存事实数据：目标、最后已知位置、可见性、搜索标记、死亡、失衡与待消费的受击反应。删除当前状态、当前意图和技能编号。

现有敌人行为树叶子资产保留其引用和序列化字段。当前 `EnemySetIntentNodeAsset` 改为直接动作节点：根据配置的行为类型操作移动、动画、战斗和黑板，不再创建或写入意图。保留该脚本资产身份可以避免批量重连现有行为树资产；菜单、注释和测试改为描述“行为动作”。

巡逻、搜索与死亡需要运行时进度，动作节点会在其运行时节点实例中维护：

- 巡逻保存当前路点索引，每次 Tick 维持移动，到达后推进索引。
- 搜索保存搜索点和索引，首次进入搜索时生成点位；重新看见目标时重置搜索进度。
- 死亡首次执行时停止移动和战斗、播放死亡动画，随后返回 `Running`，阻止选择器继续评估低优先级分支。

攻击和技能动作仅在战斗组件未处于动作中时尝试启动；动作结束后，下一帧行为树会依据距离和目标事实重新选择攻击、追击或搜索，不再需要 FSM 回退状态。

受击和失衡动作消费或清理对应事实、停止移动并播放动画。它们返回成功，让根选择器在下一帧重新从最高优先级选择行为。

## 数据流

```text
EnemyAgent.Update
  -> Perception 更新 Blackboard 事实
  -> BehaviorTreeRunner.Tick
  -> 条件节点读取事实
  -> 动作节点直接操作 Movement / Animation / Combat / Blackboard
```

行为树选择器本身承担优先级；不再存在 `RequestState`、`ChangeState`、状态优先级表或意图消费阶段。

## 删除范围

- `Assets/Game/Character/Enemy/AI/States` 下的敌人 FSM 状态类。
- `EnemyIntent.cs`、`EnemyStateId.cs` 及其测试。
- `AIController` 对 `GameMain2.Framework.Core.FSM` 的依赖。
- `EnemyDefinition` 中仅服务敌人 FSM 的起始状态和启用状态配置及其校验。

保留行为树框架、`BehaviorTreeRunner`、敌人组件、`EnemyStateContext`（后续改名为更准确的 AI 上下文）以及现有行为树资产结构。

## 测试与验证

新增或改写 EditMode 测试，覆盖：

- 动作节点直接消费受击和失衡事实。
- 巡逻动作会持续移动并按路点推进。
- 追击动作直接向目标移动，进入攻击范围后尝试启动攻击。
- 死亡动作停止移动与战斗，并保持高优先级终止状态。
- `AIController.TickAI` 不再依赖 FSM、状态 ID 或意图。

完成后执行 Unity 编译、Error 日志检查，并在 `Scene1/Boss` 运行时验证巡逻位置持续变化。

## 非目标

- 不修改玩家 FSM 或通用 FSM 框架。
- 不重建现有敌人行为树的选择器/序列结构。
- 不新增 flee、keep distance、ranged attack、summon 等当前没有实现的敌人行为。
