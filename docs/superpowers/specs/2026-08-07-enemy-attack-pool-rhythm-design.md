# 敌人攻击池节奏策略设计

## 背景

Boss 需要在进身攻击池、近距离攻击池、远离攻击池之间做节奏选择：优先判断是否需要进身；如果不需要进身，则在近距离攻击和远离攻击之间按权重选择；近距离攻击完成后，下一次选择远离攻击池的权重提升。

该能力应做成敌人通用配置和通用决策逻辑，不为 Boss 单独创建 `EnemyCombatDecisionController`。普通小怪可以只配置近距离攻击池，进身池和远离池为空时正常跳过。

## 设计结论

- 保留 `EnemyCombatDecisionController` 作为统一入口。
- 新增通用“攻击池节奏策略”，负责先选择攻击池，再从选中池里选择技能。
- `basicAttacks` 作为近距离攻击池，仍是战斗敌人的必填主攻击池。
- `approachAttacks` 作为进身攻击池，可为空。
- `retreatAttacks` 作为远离攻击池，可为空。
- 不创建 Boss 专属决策器，Boss 只通过配置启用更复杂的池节奏。

## 池选择规则

1. 如果 `approachAttacks` 非空，并且存在任意满足当前距离或接近条件的技能，则优先选择进身攻击池。
2. 如果进身池不可用，则进入近距离节奏选择。
3. 近距离节奏选择只在可用池之间进行：`basicAttacks` 可用则加入候选，`retreatAttacks` 非空且满足触发条件则加入候选。
4. 如果只有 `basicAttacks` 可用，则直接选择近距离攻击池。
5. 如果 `retreatAttacks` 为空，则远离池不参与权重抽选。
6. 如果所有战斗技能池都不可用，则视为配置错误，保持 fast fail。

## 权重节奏

Boss 的初始状态下，近距离攻击池权重大于远离攻击池。每次完整执行近距离攻击池技能后，远离攻击池的有效权重提升一次。远离攻击池被选中后，远离权重加成重置或衰减，避免 Boss 连续高频后撤。

建议新增配置项：

- `closeAttackPoolWeight`：近距离攻击池基础权重。
- `retreatAttackPoolWeight`：远离攻击池基础权重。
- `retreatWeightBonusAfterCloseAttack`：近距离攻击完成后给远离池增加的权重。
- `retreatWeightBonusLimit`：远离池额外权重上限。
- `resetRetreatBonusAfterRetreat`：远离池被选中后是否重置加成。

## 攻击计划类型

新增 `EnemyAttackPlanType.Retreat`，用于标记本次计划来自远离攻击池。攻击流结束时，决策器需要知道刚完成的是近距离攻击还是远离攻击，从而更新远离池权重加成。

攻击结束清理前应通知决策器，例如新增 `CompleteCurrentPlan()` 或 `RecordCompletedAttackPlan(plan.Type)`。该方法只记录节奏状态，不负责播放动画或移动。

## 空池处理

空池是正常配置状态，不代表错误：

- 小怪没有进身技能：跳过 `approachAttacks`，继续使用近距离池。
- 小怪没有远离技能：跳过 `retreatAttacks`，不会抽到不存在的技能。
- Boss 三个池都配置时，按完整节奏运行。

唯一强约束是：可战斗敌人必须至少有一个近距离主攻击技能，即 `basicAttacks` 不应为空。

## 行为树影响

本设计不要求新增 Boss 专属行为树节点。现有行为树继续调用 `TryCreateAttackPlan`，池选择细节留在战斗决策器内部或其策略对象中。

## 非目标

- 不接入处决。
- 不实现纯后撤行为。
- 不把受击闪避、防御反应用作主动远离动作。
- 不为 Boss 单独创建 `EnemyCombatDecisionController`。

## 验收标准

- 普通小怪只配置 `basicAttacks` 时，战斗行为保持可用。
- `approachAttacks` 为空时，决策器不会报错，也不会阻断近距离攻击。
- `retreatAttacks` 为空时，远离池不参与抽选。
- Boss 三个攻击池都有配置时，优先进身池；不需要进身时，在近距离池和远离池之间按节奏权重选择。
- 近距离攻击完成后，远离攻击池下一次被选中的概率提升。
