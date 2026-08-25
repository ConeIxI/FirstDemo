# 战斗 Buff 系统设计

## 背景

当前玩家和敌人已经通过 `ICombatAttributes` 统一暴露生命值、稳定值、攻击力和防御力。普通命中伤害读取攻击方 `Attack` 和目标 `Defense`，因此 Buff 系统应优先接入属性读取链路，而不是让技能结算直接管理 Buff。

## 目标

- 玩家和敌人共用同一套 Buff 系统。
- 外部系统只通过 `buffId` 添加、移除和查询 Buff。
- Buff 具体数值、类型、持续时间和 Tick 规则全部来自配置文件。
- 第一版支持攻击力提升、防御力提升、持续回血、持续扣血四种 Buff。
- 重复添加同一个 `buffId` 时只刷新持续时间，不叠加数值。

## 非目标

- 第一版不接技能配置自动施加 Buff。
- 第一版不支持叠层、驱散分类、护盾、移速、眩晕、中毒视觉表现或 UI 展示。
- 第一版不改变现有死亡、失衡、格挡、弹反优先级。

## 配置

新增 `Assets/Data/BuffConfig.json`。每条配置包含：

```json
{
  "buffId": 1001,
  "buffName": "攻击强化",
  "type": "AttackModifier",
  "duration": 5.0,
  "flatValue": 10,
  "percentValue": 0.2,
  "tickInterval": 0.0,
  "tickValue": 0
}
```

字段说明：

- `buffId`：唯一配置 ID。
- `buffName`：调试和未来显示用名称。
- `type`：`AttackModifier`、`DefenseModifier`、`HealthRegen`、`HealthDamage`。
- `duration`：持续时间，单位秒。
- `flatValue`：攻击或防御的固定值加成。
- `percentValue`：攻击或防御的百分比加成，`0.2` 表示 `+20%`。
- `tickInterval`：持续回血或扣血的触发间隔。
- `tickValue`：每次回血或扣血的数值。

配置校验规则：

- `buffId` 必须大于 0，且不能重复。
- `duration` 必须大于 0。
- 攻防 Buff 可同时配置 `flatValue` 和 `percentValue`。
- 持续回血和持续扣血 Buff 的 `tickInterval` 必须大于 0，`tickValue` 必须大于 0。

## 运行时接口

新增 `CombatBuffController`，挂在玩家和敌人对象上。外部只通过 ID 操作：

```csharp
bool AddBuff(int buffId);
bool RemoveBuff(int buffId);
bool HasBuff(int buffId);
void ClearBuffs();
```

行为规则：

- `AddBuff` 通过 `ConfigManager` 查询 `BuffConfig`。
- 找不到 `buffId` 时返回 `false`，并 `Debug.LogError`，不中断战斗流程。
- 已存在同 `buffId` 时，只刷新剩余时间为配置持续时间。
- `RemoveBuff` 找到并移除时返回 `true`，不存在时返回 `false`。
- `ClearBuffs` 清空当前对象全部 Buff。

## 属性修正

新增属性修正查询能力，由 `CombatBuffController` 聚合当前生效 Buff：

```text
最终攻击力 = 基础攻击力 × (1 + 攻击百分比加成总和) + 攻击固定加成总和
最终防御力 = 基础防御力 × (1 + 防御百分比加成总和) + 防御固定加成总和
```

最终值使用 `Mathf.RoundToInt` 取整，并确保不小于 0。

玩家 `CombatAttributeSet` 和敌人 `EnemyAttributeComponent` 保留基础攻击/防御字段，但 `Attack` 和 `Defense` 属性返回 Buff 修正后的最终值。没有 `CombatBuffController` 时返回基础值。

## 持续效果

`CombatBuffController.Update` 每帧推进 Buff 时间：

- Buff 剩余时间归零后移除。
- `HealthRegen` 等待 `tickInterval` 后第一次触发，然后每个间隔调用 `RestoreHealth(tickValue)`。
- `HealthDamage` 等待 `tickInterval` 后第一次触发，然后每个间隔调用 `ApplyHealthDamage(tickValue)`。
- 攻击和防御 Buff 不 Tick，只在属性查询时参与计算。

## 数据流

```text
外部系统 AddBuff(buffId)
  -> CombatBuffController 查询 ConfigManager.GetBuffConfig(buffId)
  -> 创建或刷新运行时 Buff
  -> 属性组件读取 CombatBuffController 的攻防修正
  -> CombatAbilitySystem 继续读取 ICombatAttributes.Attack / Defense
```

持续回血和扣血数据流：

```text
CombatBuffController.Update(deltaTime)
  -> Tick 到点
  -> RestoreHealth 或 ApplyHealthDamage
  -> 属性组件发布 CombatAttributeChanged
  -> 现有 HUD / 战斗逻辑继续响应属性变化
```

## 错误处理

- 缺失 Buff 配置：`AddBuff` 返回 `false`，打印错误日志。
- 非法配置：加载配置时抛出异常，阻止错误数据进入运行时。
- 运行时目标缺少属性组件：`CombatBuffController` 打错误日志并禁用自身。

## 测试计划

- `ConfigManager` 能加载和查询 Buff 配置。
- 重复添加同 `buffId` 只刷新时间，不叠加攻防修正。
- 攻击 Buff 同时支持固定值和百分比。
- 防御 Buff 同时支持固定值和百分比。
- 持续回血 Buff 等待一个 `tickInterval` 后第一次回血。
- 持续扣血 Buff 等待一个 `tickInterval` 后第一次扣血。
- 缺失 `buffId` 时 `AddBuff` 返回 `false` 并记录错误。
- 玩家和敌人都能通过同一套 `CombatBuffController` 获得属性修正。

## 后续扩展

- 技能配置可新增 `onHitBuffIds`、`selfBuffIds` 等字段。
- Buff 可扩展为可叠层、可驱散、死亡清除策略和 UI 图标。
- 事件型 Buff 可在后续接入 `CombatEvent`，支持命中触发、受击触发、击杀触发。
