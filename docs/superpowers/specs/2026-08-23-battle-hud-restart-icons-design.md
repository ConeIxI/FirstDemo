# 战斗 HUD 死亡重开图标同步设计

## 目标

修复玩家装备两把武器后死亡重开，按 Tab 切换武器时 Battle HUD 技能图标和相关图标不刷新的问题。

## 根因

死亡重开会销毁旧的 Gobal/Player，但持久化的 BattleHudPanel 不会重新执行完整打开流程，仍持有旧玩家的 EquipmentManager。旧管理器销毁后，BattleHudSkillSlotsView 的解绑逻辑因 Unity 对象引用为空提前返回，订阅标记残留，因此新玩家的 ActiveWeaponChanged 事件无法被 HUD 接收。

## 设计

在 UIManager 现有的 RefreshBattleHudEquipmentSlots 调用链中修复生命周期：BattleHudPanel.RefreshEquipmentSlots 先重新查找并绑定当前玩家的 CombatAbilitySystem、属性组件和 EquipmentManager，再刷新当前武器槽。BattleHudSkillSlotsView.UnsubscribeEquipment 无论旧管理器是否已销毁都清理订阅标记，仅在旧对象仍有效时执行事件解绑。

## 约束

- 不修改 BattleHudPanel.prefab。
- 不修改 Unity Animator Controller 文件。
- 不新增测试代码，使用 Unity 编译验证。
