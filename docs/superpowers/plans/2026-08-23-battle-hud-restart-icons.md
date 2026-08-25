# Battle HUD Restart Icons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 让死亡重开后的 Battle HUD 重新绑定新玩家，并正确响应 Tab 武器切换事件。

**Architecture:** 保持现有 SceneFlowManager -> PersistentSceneRoot -> UIManager 刷新链路，仅在 BattleHudPanel 刷新入口重绑当前玩家，并修正 BattleHudSkillSlotsView 的销毁对象解绑状态。Prefab 和装备数据模型不变。

**Tech Stack:** Unity 2022.3.61f1c1、C# 9.0、现有 UI/Equipment 事件系统。

---

### Task 1: 修复 HUD 重绑定和装备事件生命周期

**Files:**
- Modify: `Assets/Game/UI/Panels/BattleHudPanel.cs:71-77`
- Modify: `Assets/Game/UI/BattleHud/Views/BattleHudSkillSlotsView.cs:191-200`

- [ ] **Step 1: 在装备刷新入口重新绑定当前玩家**

在 `RefreshEquipmentSlots()` 中先调用已有的 `BindPlayerAttributes()`，再调用 `playerSkillSlotsView.RefreshCurrentWeapon()`，使快照应用完成后 HUD 指向新玩家。

- [ ] **Step 2: 修正旧装备管理器销毁后的解绑状态**

调整 `UnsubscribeEquipment()`：仅在订阅存在时尝试从仍有效的管理器解绑，并始终将 `m_equipmentSubscribed` 设为 `false`。

- [ ] **Step 3: 使用 Unity 编译验证**

运行 `$CLI compile unity`，确认编译成功且无新增错误。
