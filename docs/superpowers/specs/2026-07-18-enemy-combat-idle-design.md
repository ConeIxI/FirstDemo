# 敌人战斗待机状态设计

## 目标

为近战敌人增加独立的战斗待机状态。敌人已进入战斗、没有更高优先级操作且位于期望战斗距离内时，应停止寻路移动、保持面向玩家并播放待机动画，避免移动动画持续输出 RootMotion。

## 战斗判定

满足以下任一事实时视为仍处于战斗：

- 当前目标可见。
- 当前仍保留攻击者记忆。

目标丢失并进入搜索流程后，不进入战斗待机，由现有 Search 分支处理。

## 行为树结构

新增独立 `CombatIdle` 分支，插入 GuardMelee 根 Selector 的 Attack 之后、Chase 之前。相关优先级保持为：

1. 死亡、失衡、受击。
2. 后撤、防御、攻击。
3. 战斗待机。
4. 追击、搜索、巡逻。

战斗待机分支由条件节点和动作节点组成：

- 条件节点：目标存在，目标可见或仍有攻击者记忆，并且目标距离不大于 `preferredDistance`。
- 动作节点：停止当前寻路目的地、面向目标、播放 `idleAnimation`，并把黑板战斗意图写为 `EnemyCombatIntent.Idle`。

当目标距离大于 `preferredDistance` 时，条件节点返回失败，Selector 在同一帧继续执行 Chase。

## 代码与资源改动

- 在 `EnemyBehaviorActionType` 中增加 `CombatIdle` 类型。
- 在 `EnemySetIntentNodeAsset` 的运行时动作中增加战斗待机处理。
- 新增战斗待机条件节点资产类型。
- 新增 GuardMelee 的战斗待机条件、动作和 Sequence 资产。
- 更新 `GuardMeleeBehaviorTree.asset`，将新 Sequence 放在 Attack 与 Chase 之间。
- 复用现有 `EnemyCombatIntent.Idle`，不增加重复意图类型。

## RootMotion 与转向

`EnemyMovementComponent.Stop()` 继续只负责清理目的地和停止 NavMeshAgent。战斗待机动作必须同时播放无水平位移的 `idleAnimation`，从动画状态上终止移动 RootMotion。

转向继续由 `EnemyMovementComponent.LookAt()` 负责。战斗待机动作每帧执行，因此玩家移动时敌人会持续朝向玩家，但不会重新设置移动目的地。

## 异常处理

- 缺少上下文、移动组件或目标时，战斗待机分支返回失败，让行为树继续选择其他有效分支。
- 缺少动画组件时仍执行停止和转向，避免因为表现组件缺失而继续移动。
- 缺少决策配置时使用现有 `StoppingDistance` 作为距离阈值，与项目当前配置回退方式保持一致。

## 验证

新增 EditMode 测试覆盖：

- 目标可见且处于 `preferredDistance` 内时，战斗待机条件通过。
- 仅保留攻击者记忆且处于距离内时，战斗待机条件通过。
- 目标超出 `preferredDistance` 时条件失败，允许 Chase 执行。
- 战斗待机动作清除移动目的地并写入 Idle 战斗意图。
- 战斗待机不会启动新的移动目的地。

实现完成后执行：

- `$CLI compile unity`
- `$CLI get_logs --logType Error`
- Play Mode 验证敌人在战斗距离内停止 RootMotion、持续面向玩家，玩家离开期望距离后恢复追击。

## 非目标

- 不修改攻击、防御、后撤、受击、失衡和死亡行为。
- 不恢复已删除的 KeepDistance 类型和资源。
- 不让 `EnemyMovementComponent.Stop()` 直接依赖动画组件。
- 不调整巡逻停留规则或攻击欲望计算。
