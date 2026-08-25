# 通用行为树框架设计

日期：2026-07-09

## 目标

为项目新增一套通用行为树框架。第一版只提供框架能力，不接入敌人业务，不创建敌人专用节点。

核心目标：

- 使用 ScriptableObject 资产配置行为树和节点。
- 支持 `Success`、`Failure`、`Running` 三态。
- 通过运行时节点实例保存执行进度，避免多个对象共享同一资产时串状态。
- 提供通用 `BehaviorTreeContext` 和 `BehaviorTreeBlackboard`，便于后续业务节点扩展。
- 保持框架独立，不依赖敌人、战斗或角色系统。

## 非目标

第一版不做以下内容：

- 行为树可视化编辑器。
- 敌人专用条件节点或动作节点。
- 行为树调试面板。
- JSON 导入导出。
- 随机、冷却、循环等高级节点。
- 接入 `EnemyDecisionAsset`、`EnemyActor` 或新 `AIController`。

## 目录建议

新增目录：

```text
Assets/Framework/Core/BehaviorTree/
- BehaviorTreeAsset.cs
- BehaviorTreeRunner.cs
- BehaviorTreeContext.cs
- BehaviorTreeBlackboard.cs
- BehaviorTreeStatus.cs
- Assets/
  - BehaviorTreeNodeAsset.cs
  - CompositeNodeAsset.cs
  - DecoratorNodeAsset.cs
  - ConditionNodeAsset.cs
  - ActionNodeAsset.cs
- Runtime/
  - BehaviorTreeNode.cs
  - CompositeNode.cs
  - DecoratorNode.cs
- Nodes/
  - SelectorNodeAsset.cs
  - SequenceNodeAsset.cs
  - InverterNodeAsset.cs
  - AlwaysSuccessNodeAsset.cs
  - AlwaysFailureNodeAsset.cs
```

命名使用 `BehaviorTree`，不复用旧文档里的 `BehaviourTree` 拼写，避免和已删除旧目录混淆。

## 总体架构

框架分为资产层、运行层和上下文层。

### 资产层

`BehaviorTreeAsset` 是行为树入口资产，保存根节点引用。

`BehaviorTreeNodeAsset` 是所有节点资产基类，负责创建对应运行时节点实例。它只保存配置，不保存每帧运行状态。

节点资产分为：

- `CompositeNodeAsset`：保存多个子节点。
- `DecoratorNodeAsset`：保存一个子节点。
- `ConditionNodeAsset`：业务条件节点基类。
- `ActionNodeAsset`：业务动作节点基类。

### 运行层

`BehaviorTreeRunner` 持有一棵 `BehaviorTreeAsset` 和一个 `BehaviorTreeContext`。

Runner 初始化时从根节点资产递归创建运行时节点实例。运行时节点实例保存当前执行索引、Running 状态等临时数据。这样同一个行为树资产可以被多个对象共享，每个对象的执行状态互不影响。

### 上下文层

`BehaviorTreeContext` 提供每帧执行所需的通用信息：

- `GameObject Owner`
- `Transform Transform`
- `float DeltaTime`
- `BehaviorTreeBlackboard Blackboard`

`BehaviorTreeBlackboard` 是通用键值表，用于业务节点之间共享简单事实。第一版支持对象、布尔、整数、浮点和向量等常用值的读写、覆盖和清除。

## 节点状态

行为树节点统一返回：

```text
Success
Failure
Running
```

含义：

- `Success`：节点执行成功，本次分支可以继续。
- `Failure`：节点执行失败，本次分支终止或尝试其它分支。
- `Running`：节点尚未完成，下一帧继续从当前节点或当前子节点执行。

## Tick 流程

初始化流程：

```text
BehaviorTreeRunner.Start()
- 校验 BehaviorTreeAsset 和 Root
- 从 Root 节点资产递归创建运行时节点实例
- 每个运行时节点保存自己的 Running 进度
```

每帧流程：

```text
BehaviorTreeRunner.Tick(deltaTime)
- 写入 context.DeltaTime
- 调用 root.Tick(context)
- 返回 Success / Failure / Running
```

Runner 还提供 `Reset()`，用于外部切换树、禁用对象或强制中断行为时，递归清掉所有运行态。

## 第一版节点

### Selector

按顺序执行子节点。

- 子节点返回 `Success`：Selector 返回 `Success`，并重置当前索引。
- 子节点返回 `Running`：Selector 返回 `Running`，下一帧继续当前子节点。
- 子节点返回 `Failure`：继续尝试下一个子节点。
- 全部子节点失败：Selector 返回 `Failure`，并重置当前索引。

空 Selector 返回 `Failure`。

### Sequence

按顺序执行子节点。

- 子节点返回 `Failure`：Sequence 返回 `Failure`，并重置当前索引。
- 子节点返回 `Running`：Sequence 返回 `Running`，下一帧继续当前子节点。
- 子节点返回 `Success`：继续执行下一个子节点。
- 全部子节点成功：Sequence 返回 `Success`，并重置当前索引。

空 Sequence 返回 `Success`。

### Inverter

装饰一个子节点。

- 子节点返回 `Success`：返回 `Failure`。
- 子节点返回 `Failure`：返回 `Success`。
- 子节点返回 `Running`：返回 `Running`。

没有子节点时返回 `Failure` 并记录 Warning。

### AlwaysSuccess

装饰一个子节点。

- 子节点返回 `Running`：返回 `Running`。
- 子节点返回其它终态：返回 `Success`。

没有子节点时返回 `Failure` 并记录 Warning。

### AlwaysFailure

装饰一个子节点。

- 子节点返回 `Running`：返回 `Running`。
- 子节点返回其它终态：返回 `Failure`。

没有子节点时返回 `Failure` 并记录 Warning。

### ConditionNodeAsset

业务条件节点基类。

条件节点只做判断，返回 `Success` 或 `Failure`，不返回 `Running`。例如后续敌人业务可以实现 `CanSeeTarget`、`IsInAttackRange`。

### ActionNodeAsset

业务动作节点基类。

动作节点允许返回 `Success`、`Failure`、`Running`。例如后续敌人业务可以实现 `MoveToTarget`、`PlayAnimation`、`CastSkill`。

## 错误处理

第一版错误处理保持直接：

- `BehaviorTreeAsset.Root` 为空：Runner 初始化失败，记录 Error，Tick 返回 `Failure`。
- `BehaviorTreeContext.Owner` 为空：Runner 初始化失败，Tick 返回 `Failure`。
- 组合节点子节点列表为空：按节点语义返回空结果。
- 装饰器没有子节点：返回 `Failure` 并记录 Warning。
- 子节点资产为空：跳过该子节点并记录 Warning。
- 运行时切换树：先 `Reset()` 旧树，再重新构建运行时实例。

不为缺失配置做复杂兜底。配置错误应尽快暴露，避免行为树静默执行出错。

## 测试计划

新增 EditMode 测试覆盖通用行为：

```text
Selector：第一个 Success 立即成功；Running 时下一帧继续当前子节点
Sequence：第一个 Failure 立即失败；Running 时下一帧继续当前子节点
Inverter：Success/Failure 互换，Running 不变
AlwaysSuccess/AlwaysFailure：Running 保留，终态强制改写
Runner：共享同一个 BehaviorTreeAsset 的两个 Runner 不串 Running 状态
Blackboard：能写入、读取、覆盖、清除常用对象/数值
```

测试只使用框架节点和测试专用节点，不依赖敌人、战斗或场景资源。

## 验证方式

实现完成后必须使用 Unity 编译验证：

```text
$CLI compile unity
```

`compile dotnet` 只能作为额外检查，不能替代 Unity 编译。

## 后续扩展

框架稳定后，可以在独立任务中继续扩展：

- 敌人专用行为树节点。
- `BehaviorTreeDecisionAsset`，把行为树输出转换为 `EnemyIntent`。
- 行为树调试面板。
- 冷却、循环、随机选择、权重选择等高级节点。
- 行为树资产创建辅助菜单或简单 Inspector 优化。
