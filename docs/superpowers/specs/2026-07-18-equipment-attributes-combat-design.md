# 装备属性接入战斗计算设计

## 目标

将装备系统提供的攻击力、防御力接入玩家战斗属性，让现有伤害公式自动使用装备后的攻击、防御数值。第一版只处理玩家装备，不影响敌人属性，不改 Buff 结算规则。

## 当前上下文

- 装备配置已经拆分为武器攻击力和防具防御力：
  - `WeaponItemConfig.attack`
  - `DefenseEquipmentItemConfig.defense`
- 玩家战斗属性出口是 `CombatAttributeSet.Attack` 和 `CombatAttributeSet.Defense`。
- 现有伤害计算已经读取攻击方 `Attack` 和目标 `Defense`，因此只要玩家属性出口返回装备修正后的值，战斗计算就会自然生效。
- 背包装备槽由 `BagInventoryManager` 管理，装备/卸下/交换成功后都会触发 `InventoryChanged`。

## 推荐方案

采用“装备变化时主动刷新玩家属性”的方案。

1. `BagInventoryManager` 继续只负责背包和装备槽数据。
2. 新增一个玩家装备属性同步组件，负责订阅背包变化并汇总当前装备属性。
3. `CombatAttributeSet` 新增装备属性加成字段，不直接依赖背包或 UI。
4. 每次装备、卸下、交换装备成功后，装备属性同步组件重新计算总攻击和总防御，并写回玩家 `CombatAttributeSet`。

这个方案让装备系统是属性来源，`CombatAttributeSet` 是战斗属性出口，Buff 继续作为最终修正层。

## 属性计算规则

玩家最终战斗属性按以下顺序计算：

```text
基础属性 + 装备属性 = 装备后属性
装备后属性经过 Buff 修正 = 最终 Attack / Defense
```

第一版规则：

- 武器只提供攻击力，不提供防御力。
- 防具只提供防御力，不提供攻击力。
- 头盔、胸甲、护腿、臂铠的防御力全部累加。
- 两个武器槽只计算当前激活武器槽的攻击力。
- 如果当前激活武器槽为空，则武器攻击加成为 0。
- 装备变化、卸下装备、交换武器槽、切换当前武器时都必须刷新属性。

## 组件设计

### `CombatAttributeSet`

新增装备加成字段：

- `equipmentAttackBonus`
- `equipmentDefenseBonus`

新增公开方法：

- `SetEquipmentAttributeBonus(int attackBonus, int defenseBonus)`

`Attack` 和 `Defense` 读取逻辑调整为：

- `Attack` 使用基础攻击力加装备攻击力，再交给 `CombatBuffController.CalculateAttack`。
- `Defense` 使用基础防御力加装备防御力，再交给 `CombatBuffController.CalculateDefense`。

属性变化时应触发 `AttributeChanged`，让未来属性面板可以刷新显示。

### 玩家装备属性同步组件

建议新增 `PlayerEquipmentAttributeSync`。

职责：

- 找到玩家的 `CombatAttributeSet`。
- 读取当前装备槽里的物品配置。
- 汇总当前激活武器攻击力和全部防具防御力。
- 调用 `CombatAttributeSet.SetEquipmentAttributeBonus` 写回装备属性加成。
- 在初始化时同步一次。
- 在 `InventoryChanged` 触发后同步一次。
- 在当前激活武器切换时同步一次。

它不负责移动装备，不负责显示 UI，不直接参与伤害计算。

### `BagInventoryManager`

保留现有装备移动职责。

可以补充只读查询接口，供同步组件读取装备槽：

- `GetEquippedItem(BagSlotType slotType, int index)`
- `GetActiveWeaponIndex()` 或通过现有装备管理器读取当前激活武器槽。

如果已有 `GetItem(BagSlotType, int)` 和装备管理器的 `ActiveWeaponIndex` 足够使用，则不新增重复接口。

## 数据流

```text
玩家装备/卸下/交换成功
→ BagInventoryManager.InventoryChanged
→ PlayerEquipmentAttributeSync 重新汇总装备配置
→ CombatAttributeSet.SetEquipmentAttributeBonus
→ CombatAttributeSet.Attack / Defense 返回装备后属性
→ CombatBuffController 修正最终值
→ CombatAbilitySystem 使用最终 Attack / Defense 计算伤害
```

## 错误处理

- 装备槽为空时按 0 加成处理。
- 配置缺失时记录 `Debug.LogError`，该装备按 0 加成处理，不中断战斗。
- 移动装备失败时不刷新属性，因为装备状态没有变化。
- 如果找不到玩家 `CombatAttributeSet`，记录一次警告并跳过同步。

## 测试范围

新增 EditMode 测试覆盖：

- 武器攻击力会增加玩家 `Attack`。
- 胸甲、头盔、护腿、臂铠防御力会累加到玩家 `Defense`。
- 卸下装备后对应加成会移除。
- 两个武器槽只计算当前激活武器。
- 装备属性会先累加基础属性，再被 Buff 修正。
- 消耗品的 `buffId` 不参与装备攻击/防御计算。

## 不做事项

- 第一版不做敌人装备。
- 第一版不做装备百分比属性。
- 第一版不做套装属性。
- 第一版不改变 Buff 配置和 Buff 生命周期。
- 第一版不把背包系统重构成完整存档装备系统。

## 验收标准

- 装备武器后，玩家攻击力提高。
- 装备防具后，玩家防御力提高。
- 卸下装备后，玩家属性回退。
- 当前伤害公式无需改动即可使用装备后的攻击、防御。
- Unity 编译通过，相关 EditMode 测试通过。
