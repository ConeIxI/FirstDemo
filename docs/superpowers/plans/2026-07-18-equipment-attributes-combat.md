# Equipment Attributes Combat Integration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Equip and unequip operations update player combat Attack and Defense so the existing damage formula automatically uses equipment stats.

**Architecture:** Keep combat stat output inside `CombatAttributeSet`. Add a focused `PlayerEquipmentAttributeSync` component that listens to inventory/equipment changes, summarizes current equipment config values, and writes equipment bonuses to `CombatAttributeSet`. Buffs remain the outer modifier layer: base attributes plus equipment bonuses are passed into `CombatBuffController`.

**Tech Stack:** Unity 2022.3.61f1c1, C# 9.0, NUnit EditMode tests, AIBridge CLI for Unity compile/test.

## Global Constraints

- Use Simplified Chinese comments for new or changed functions.
- Every new function must have a Simplified Chinese comment describing purpose or key behavior.
- Keep code compatible with C# 9.0.
- Validate Unity compile with `./.aibridge/cli/AIBridgeCLI.exe compile unity`.
- Do not stage unrelated existing worktree changes; use explicit `git add <paths>`.
- First version applies only to player equipment.
- Weapons provide attack only; defensive equipment provides defense only.
- Helmet, armor, leggings, and gloves defense values stack.
- Only the current active weapon slot contributes weapon attack.
- If the active weapon slot is empty, weapon attack bonus is 0.
- Equipment stats are applied before Buff modifiers.
- Consumable `buffId` does not participate in equipment Attack/Defense calculation.

---

## File Structure

- Modify `Assets/Game/Battle/Combat/Core/CombatDefinitions.cs`
  - Add `Attack` and `Defense` to `CombatAttributeType` so equipment bonus changes can publish attribute change events.
- Modify `Assets/Game/Battle/Ability/CombatAttributeSet.cs`
  - Store equipment attack/defense bonuses and expose `SetEquipmentAttributeBonus(int attackBonus, int defenseBonus)`.
- Modify `Assets/Game/UI/BagInventoryManager.cs`
  - Expose item stat helpers on `BagItemData` so sync logic does not need to know each config subtype.
- Create `Assets/Game/Character/Player/PlayerEquipmentAttributeSync.cs`
  - Subscribe to `BagInventoryManager.InventoryChanged` and `EquipmentManager.ActiveWeaponChanged`.
  - Summarize current equipped item attack/defense and write to `CombatAttributeSet`.
- Modify `Assets/Game/Editor/PlayerAttributeSetEditModeTests.cs`
  - Cover direct equipment bonus updates on player combat attributes.
- Create `Assets/Game/Editor/PlayerEquipmentAttributeSyncEditModeTests.cs`
  - Cover inventory-driven equipment stat refresh, current weapon selection, unequip behavior, and consumable exclusion.
- Modify `Assets/Game/Editor/CombatBuffControllerEditModeTests.cs`
  - Cover equipment bonuses being applied before Buff modifiers.

---

### Task 1: CombatAttributeSet Equipment Bonus API

**Files:**
- Modify: `Assets/Game/Battle/Combat/Core/CombatDefinitions.cs`
- Modify: `Assets/Game/Battle/Ability/CombatAttributeSet.cs`
- Test: `Assets/Game/Editor/PlayerAttributeSetEditModeTests.cs`
- Test: `Assets/Game/Editor/CombatBuffControllerEditModeTests.cs`

**Interfaces:**
- Produces: `CombatAttributeType.Attack`
- Produces: `CombatAttributeType.Defense`
- Produces: `CombatAttributeSet.EquipmentAttackBonus : int`
- Produces: `CombatAttributeSet.EquipmentDefenseBonus : int`
- Produces: `CombatAttributeSet.SetEquipmentAttributeBonus(int attackBonus, int defenseBonus) : void`
- Existing consumers continue to use `ICombatAttributes.Attack` and `ICombatAttributes.Defense`.

- [ ] **Step 1: Write failing tests for equipment bonus API**

Append these tests to `Assets/Game/Editor/PlayerAttributeSetEditModeTests.cs`:

```csharp
        /// <summary>验证装备加成会叠加到玩家攻击力和防御力。</summary>
        [Test]
        public void SetEquipmentAttributeBonus_AddsEquipmentStatsToCombatAttributes()
        {
            GameObject player = new GameObject("player");
            CombatAttributeSet attributes = player.AddComponent<CombatAttributeSet>();

            attributes.SetEquipmentAttributeBonus(15, 25);

            Assert.AreEqual(25, attributes.Attack);
            Assert.AreEqual(25, attributes.Defense);
            Assert.AreEqual(15, attributes.EquipmentAttackBonus);
            Assert.AreEqual(25, attributes.EquipmentDefenseBonus);
            Object.DestroyImmediate(player);
        }

        /// <summary>验证装备加成清零后玩家攻防回到基础值。</summary>
        [Test]
        public void SetEquipmentAttributeBonus_ClearsEquipmentStatsWhenUnequipped()
        {
            GameObject player = new GameObject("player");
            CombatAttributeSet attributes = player.AddComponent<CombatAttributeSet>();
            attributes.SetEquipmentAttributeBonus(15, 25);

            attributes.SetEquipmentAttributeBonus(0, 0);

            Assert.AreEqual(10, attributes.Attack);
            Assert.AreEqual(0, attributes.Defense);
            Assert.AreEqual(0, attributes.EquipmentAttackBonus);
            Assert.AreEqual(0, attributes.EquipmentDefenseBonus);
            Object.DestroyImmediate(player);
        }

        /// <summary>验证装备攻防变化会发布攻击和防御属性变化事件。</summary>
        [Test]
        public void SetEquipmentAttributeBonus_RaisesAttackAndDefenseChangedEvents()
        {
            GameObject player = new GameObject("player");
            CombatAttributeSet attributes = player.AddComponent<CombatAttributeSet>();
            bool attackChanged = false;
            bool defenseChanged = false;
            attributes.AttributeChanged += change =>
            {
                if (change.Type == CombatAttributeType.Attack && change.Current == 25 && change.Delta == 15)
                {
                    attackChanged = true;
                }

                if (change.Type == CombatAttributeType.Defense && change.Current == 25 && change.Delta == 25)
                {
                    defenseChanged = true;
                }
            };

            attributes.SetEquipmentAttributeBonus(15, 25);

            Assert.IsTrue(attackChanged);
            Assert.IsTrue(defenseChanged);
            Object.DestroyImmediate(player);
        }
```

Append this test to `Assets/Game/Editor/CombatBuffControllerEditModeTests.cs` near the existing player Buff attribute test:

```csharp
        /// <summary>验证装备属性先叠加基础属性，再被 Buff 修正。</summary>
        [Test]
        public void PlayerAttributes_ApplyEquipmentStatsBeforeBuffModifiers()
        {
            GameObject player = new GameObject("player");
            CombatAttributeSet attributes = player.AddComponent<CombatAttributeSet>();
            CombatBuffController controller = player.AddComponent<CombatBuffController>();
            attributes.SetEquipmentAttributeBonus(10, 20);
            controller.InitializeForTests(attributes);
            controller.SetBuffConfigResolverForTests(id =>
            {
                if (id == 1001)
                {
                    return CreateAttackBuff(id, 5f, 10, 0.2f);
                }

                return CreateDefenseBuff(id, 5f, 5, 0.1f);
            });

            controller.AddBuff(1001);
            controller.AddBuff(1002);

            Assert.AreEqual(34, attributes.Attack);
            Assert.AreEqual(27, attributes.Defense);
            UnityEngine.Object.DestroyImmediate(player);
        }
```

Expected calculations:
- Attack: `(base 10 + equipment 10) * 1.2 + flat 10 = 34`
- Defense: `(base 0 + equipment 20) * 1.1 + flat 5 = 27`

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe asset refresh
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name Game.Character.Tests.PlayerAttributeSetEditModeTests --timeout 120000
```

Expected: compile/test fails because `SetEquipmentAttributeBonus`, `EquipmentAttackBonus`, `EquipmentDefenseBonus`, `CombatAttributeType.Attack`, or `CombatAttributeType.Defense` do not exist.

- [ ] **Step 3: Add Attack and Defense attribute event types**

Modify `Assets/Game/Battle/Combat/Core/CombatDefinitions.cs`:

```csharp
    public enum CombatAttributeType
    {
        Health,
        Stability,
        BattleSpirit,
        Attack,
        Defense
    }
```

- [ ] **Step 4: Add equipment bonus storage and final attribute calculation**

Modify `Assets/Game/Battle/Ability/CombatAttributeSet.cs`:

```csharp
[SerializeField] private int equipmentAttackBonus;
[SerializeField] private int equipmentDefenseBonus;

public int EquipmentAttackBonus => equipmentAttackBonus;
public int EquipmentDefenseBonus => equipmentDefenseBonus;
```

Replace the existing attack/defense base calculations with:

```csharp
/// <summary>获取 Buff 修正后的玩家攻击力。</summary>
private int GetModifiedAttack()
{
    int equippedAttack = attack + equipmentAttackBonus;
    CombatBuffController buffController = GetBuffController();
    return buffController != null ? buffController.CalculateAttack(equippedAttack) : equippedAttack;
}

/// <summary>获取 Buff 修正后的玩家防御力。</summary>
private int GetModifiedDefense()
{
    int equippedDefense = defense + equipmentDefenseBonus;
    CombatBuffController buffController = GetBuffController();
    return buffController != null ? buffController.CalculateDefense(equippedDefense) : equippedDefense;
}
```

Add the public setter:

```csharp
/// <summary>写入装备提供的攻击和防御加成，并在最终值变化时发布属性事件。</summary>
public void SetEquipmentAttributeBonus(int attackBonus, int defenseBonus)
{
    int oldAttack = Attack;
    int oldDefense = Defense;
    equipmentAttackBonus = attackBonus;
    equipmentDefenseBonus = defenseBonus;
    PublishAttributeChangeIfNeeded(CombatAttributeType.Attack, oldAttack, Attack);
    PublishAttributeChangeIfNeeded(CombatAttributeType.Defense, oldDefense, Defense);
}

/// <summary>最终属性变化时发布一次属性变化事件。</summary>
private void PublishAttributeChangeIfNeeded(CombatAttributeType type, int oldValue, int newValue)
{
    int delta = newValue - oldValue;
    if (delta == 0)
    {
        return;
    }

    AttributeChanged?.Invoke(new CombatAttributeChanged(type, newValue, 0, delta));
}
```

- [ ] **Step 5: Run GREEN tests and compile**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe asset refresh
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name Game.Character.Tests.PlayerAttributeSetEditModeTests --timeout 120000
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --test-name Game.Battle.Tests.CombatBuffControllerEditModeTests.PlayerAttributes_ApplyEquipmentStatsBeforeBuffModifiers --timeout 120000
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: player attribute tests pass, equipment-before-Buff test passes, Unity compile reports `errorCount:0`.

- [ ] **Step 6: Commit Task 1**

```powershell
git add Assets/Game/Battle/Combat/Core/CombatDefinitions.cs Assets/Game/Battle/Ability/CombatAttributeSet.cs Assets/Game/Editor/PlayerAttributeSetEditModeTests.cs Assets/Game/Editor/CombatBuffControllerEditModeTests.cs
git commit -m "接入玩家装备属性加成"
```

---

### Task 2: Inventory-Driven Equipment Attribute Sync

**Files:**
- Modify: `Assets/Game/UI/BagInventoryManager.cs`
- Create: `Assets/Game/Character/Player/PlayerEquipmentAttributeSync.cs`
- Test: `Assets/Game/Editor/PlayerEquipmentAttributeSyncEditModeTests.cs`

**Interfaces:**
- Consumes: `BagInventoryManager.GetItem(BagSlotType slotType, int index) : BagItemData`
- Consumes: `EquipmentManager.ActiveWeaponIndex : int`
- Consumes: `EquipmentManager.ActiveWeaponChanged`
- Consumes: `CombatAttributeSet.SetEquipmentAttributeBonus(int attackBonus, int defenseBonus) : void`
- Produces: `BagItemData.AttackBonus : int`
- Produces: `BagItemData.DefenseBonus : int`
- Produces: `PlayerEquipmentAttributeSync.RefreshEquipmentAttributes() : void`
- Produces: `PlayerEquipmentAttributeSync.InitializeForTests(BagInventoryManager inventory, CombatAttributeSet attributes, int activeWeaponIndex) : void` inside `#if UNITY_EDITOR`
- Produces: `PlayerEquipmentAttributeSync.SetActiveWeaponIndexForTests(int activeWeaponIndex) : void` inside `#if UNITY_EDITOR`

- [ ] **Step 1: Write failing sync tests**

Create `Assets/Game/Editor/PlayerEquipmentAttributeSyncEditModeTests.cs`:

```csharp
using Game.Battle.Ability;
using GameMain2.Scripts.Character;
using GameMain2.Scripts.UI;
using NUnit.Framework;
using UnityEngine;

namespace Game.Character.Tests
{
    public sealed class PlayerEquipmentAttributeSyncEditModeTests
    {
        /// <summary>验证当前激活武器槽的攻击力会同步到玩家属性。</summary>
        [Test]
        public void RefreshEquipmentAttributes_UsesOnlyActiveWeaponAttack()
        {
            GameObject inventoryObject = new GameObject("inventory");
            BagInventoryManager inventory = inventoryObject.AddComponent<BagInventoryManager>();
            inventory.Initialize(30);
            inventory.MoveBagItemToEquipment(BagItemType.Weapon, 0, BagSlotType.Weapon, 0);
            inventory.MoveBagItemToEquipment(BagItemType.Weapon, 1, BagSlotType.Weapon, 1);
            GameObject player = CreatePlayerWithSync(inventory, 1, out CombatAttributeSet attributes, out PlayerEquipmentAttributeSync sync);

            sync.RefreshEquipmentAttributes();

            Assert.AreEqual(30, attributes.Attack);
            Assert.AreEqual(20, attributes.EquipmentAttackBonus);
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(inventoryObject);
        }

        /// <summary>验证防具槽位的防御力会全部累加到玩家属性。</summary>
        [Test]
        public void RefreshEquipmentAttributes_StacksAllDefensiveEquipment()
        {
            GameObject inventoryObject = new GameObject("inventory");
            BagInventoryManager inventory = inventoryObject.AddComponent<BagInventoryManager>();
            inventory.Initialize(30);
            inventory.MoveBagItemToEquipment(BagItemType.Helmet, 0, BagSlotType.Helmet, 0);
            inventory.MoveBagItemToEquipment(BagItemType.Armor, 0, BagSlotType.Armor, 0);
            inventory.MoveBagItemToEquipment(BagItemType.Leggings, 0, BagSlotType.Leggings, 0);
            inventory.MoveBagItemToEquipment(BagItemType.Gloves, 0, BagSlotType.Gloves, 0);
            GameObject player = CreatePlayerWithSync(inventory, 0, out CombatAttributeSet attributes, out PlayerEquipmentAttributeSync sync);

            sync.RefreshEquipmentAttributes();

            Assert.AreEqual(70, attributes.Defense);
            Assert.AreEqual(70, attributes.EquipmentDefenseBonus);
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(inventoryObject);
        }

        /// <summary>验证卸下装备后同步会移除对应属性加成。</summary>
        [Test]
        public void RefreshEquipmentAttributes_RemovesBonusesAfterUnequip()
        {
            GameObject inventoryObject = new GameObject("inventory");
            BagInventoryManager inventory = inventoryObject.AddComponent<BagInventoryManager>();
            inventory.Initialize(30);
            inventory.MoveBagItemToEquipment(BagItemType.Weapon, 0, BagSlotType.Weapon, 0);
            inventory.MoveBagItemToEquipment(BagItemType.Armor, 0, BagSlotType.Armor, 0);
            GameObject player = CreatePlayerWithSync(inventory, 0, out CombatAttributeSet attributes, out PlayerEquipmentAttributeSync sync);
            sync.RefreshEquipmentAttributes();

            inventory.MoveEquipmentToFirstEmptyBagSlot(BagSlotType.Weapon, 0);
            inventory.MoveEquipmentToFirstEmptyBagSlot(BagSlotType.Armor, 0);
            sync.RefreshEquipmentAttributes();

            Assert.AreEqual(10, attributes.Attack);
            Assert.AreEqual(0, attributes.Defense);
            Assert.AreEqual(0, attributes.EquipmentAttackBonus);
            Assert.AreEqual(0, attributes.EquipmentDefenseBonus);
            Object.DestroyImmediate(player);
            Object.DestroyImmediate(inventoryObject);
        }

        /// <summary>验证消耗品 BuffId 不参与装备攻击和防御计算。</summary>
        [Test]
        public void BagItemData_ConsumableBuffIdDoesNotProvideEquipmentStats()
        {
            BagItemData item = new BagItemData(2, BagItemType.Consumable);

            Assert.AreEqual(0, item.AttackBonus);
            Assert.AreEqual(0, item.DefenseBonus);
        }

        /// <summary>创建测试玩家并注入装备属性同步依赖。</summary>
        private static GameObject CreatePlayerWithSync(
            BagInventoryManager inventory,
            int activeWeaponIndex,
            out CombatAttributeSet attributes,
            out PlayerEquipmentAttributeSync sync)
        {
            GameObject player = new GameObject("player");
            attributes = player.AddComponent<CombatAttributeSet>();
            sync = player.AddComponent<PlayerEquipmentAttributeSync>();
            sync.InitializeForTests(inventory, attributes, activeWeaponIndex);
            return player;
        }
    }
}
```

- [ ] **Step 2: Run tests to verify RED**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe asset refresh
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name Game.Character.Tests.PlayerEquipmentAttributeSyncEditModeTests --timeout 120000
```

Expected: compile/test fails because `PlayerEquipmentAttributeSync`, `BagItemData.AttackBonus`, `BagItemData.DefenseBonus`, or test injection methods do not exist.

- [ ] **Step 3: Add stat helper properties to BagItemData**

Modify `Assets/Game/UI/BagInventoryManager.cs` inside `BagItemData`:

```csharp
public int AttackBonus => GetAttackBonus(Config);
public int DefenseBonus => GetDefenseBonus(Config);
```

Add helper methods inside `BagItemData`:

```csharp
/// <summary>读取装备配置提供的攻击力，非武器固定返回 0。</summary>
private static int GetAttackBonus(ItemConfigBase config)
{
    WeaponItemConfig weaponConfig = config as WeaponItemConfig;
    return weaponConfig == null ? 0 : weaponConfig.attack;
}

/// <summary>读取装备配置提供的防御力，非防具固定返回 0。</summary>
private static int GetDefenseBonus(ItemConfigBase config)
{
    DefenseEquipmentItemConfig defenseConfig = config as DefenseEquipmentItemConfig;
    return defenseConfig == null ? 0 : defenseConfig.defense;
}
```

- [ ] **Step 4: Create PlayerEquipmentAttributeSync**

Create `Assets/Game/Character/Player/PlayerEquipmentAttributeSync.cs`:

```csharp
using Game.Battle.Ability;
using Game.Character.Equipment;
using GameMain2.Scripts.UI;
using UnityEngine;

namespace GameMain2.Scripts.Character
{
    [DisallowMultipleComponent]
    public sealed class PlayerEquipmentAttributeSync : MonoBehaviour
    {
        [SerializeField] private BagInventoryManager inventory;
        [SerializeField] private EquipmentManager equipmentManager;
        [SerializeField] private CombatAttributeSet attributes;
        [SerializeField] private int activeWeaponIndexOverride = -1;

        private bool m_missingAttributesWarned;

        /// <summary>初始化并订阅装备变化事件。</summary>
        private void OnEnable()
        {
            ResolveDependencies();
            SubscribeEvents();
            RefreshEquipmentAttributes();
        }

        /// <summary>取消装备变化事件订阅。</summary>
        private void OnDisable()
        {
            UnsubscribeEvents();
        }

        /// <summary>重新汇总当前装备属性并写入玩家战斗属性。</summary>
        public void RefreshEquipmentAttributes()
        {
            ResolveDependencies();
            if (attributes == null)
            {
                if (!m_missingAttributesWarned)
                {
                    Debug.LogWarning("PlayerEquipmentAttributeSync 未找到 CombatAttributeSet，无法同步装备属性。", this);
                    m_missingAttributesWarned = true;
                }

                return;
            }

            int attackBonus = CalculateWeaponAttackBonus();
            int defenseBonus = CalculateDefenseBonus();
            attributes.SetEquipmentAttributeBonus(attackBonus, defenseBonus);
        }

        /// <summary>解析背包、装备管理器和战斗属性依赖。</summary>
        private void ResolveDependencies()
        {
            if (attributes == null)
            {
                attributes = GetComponent<CombatAttributeSet>();
            }

            if (equipmentManager == null)
            {
                equipmentManager = GetComponent<EquipmentManager>();
            }

            if (inventory == null)
            {
                inventory = FindObjectOfType<BagInventoryManager>(true);
            }
        }

        /// <summary>订阅背包和当前武器变化事件。</summary>
        private void SubscribeEvents()
        {
            if (inventory != null)
            {
                inventory.InventoryChanged -= OnInventoryChanged;
                inventory.InventoryChanged += OnInventoryChanged;
            }

            if (equipmentManager != null)
            {
                equipmentManager.ActiveWeaponChanged -= OnActiveWeaponChanged;
                equipmentManager.ActiveWeaponChanged += OnActiveWeaponChanged;
            }
        }

        /// <summary>取消背包和当前武器变化事件订阅。</summary>
        private void UnsubscribeEvents()
        {
            if (inventory != null)
            {
                inventory.InventoryChanged -= OnInventoryChanged;
            }

            if (equipmentManager != null)
            {
                equipmentManager.ActiveWeaponChanged -= OnActiveWeaponChanged;
            }
        }

        /// <summary>背包装备变化后刷新装备属性。</summary>
        private void OnInventoryChanged()
        {
            RefreshEquipmentAttributes();
        }

        /// <summary>当前激活武器变化后刷新装备攻击力。</summary>
        private void OnActiveWeaponChanged(int slotIndex, WeaponData weapon, GameObject weaponModel)
        {
            RefreshEquipmentAttributes();
        }

        /// <summary>计算当前激活武器槽提供的攻击力。</summary>
        private int CalculateWeaponAttackBonus()
        {
            if (inventory == null)
            {
                return 0;
            }

            int activeWeaponIndex = GetActiveWeaponIndex();
            BagItemData item = inventory.GetItem(BagSlotType.Weapon, activeWeaponIndex);
            return item == null ? 0 : item.AttackBonus;
        }

        /// <summary>累加全部防具槽位提供的防御力。</summary>
        private int CalculateDefenseBonus()
        {
            if (inventory == null)
            {
                return 0;
            }

            return GetDefenseBonus(BagSlotType.Helmet)
                + GetDefenseBonus(BagSlotType.Armor)
                + GetDefenseBonus(BagSlotType.Leggings)
                + GetDefenseBonus(BagSlotType.Gloves);
        }

        /// <summary>读取单个防具槽位的防御力。</summary>
        private int GetDefenseBonus(BagSlotType slotType)
        {
            BagItemData item = inventory.GetItem(slotType, 0);
            return item == null ? 0 : item.DefenseBonus;
        }

        /// <summary>获取当前激活武器槽索引，缺少装备管理器时使用测试覆盖值。</summary>
        private int GetActiveWeaponIndex()
        {
            if (equipmentManager != null)
            {
                return equipmentManager.ActiveWeaponIndex;
            }

            return activeWeaponIndexOverride;
        }

#if UNITY_EDITOR
        /// <summary>为 EditMode 测试注入装备属性同步依赖。</summary>
        public void InitializeForTests(BagInventoryManager testInventory, CombatAttributeSet testAttributes, int activeWeaponIndex)
        {
            inventory = testInventory;
            attributes = testAttributes;
            equipmentManager = null;
            activeWeaponIndexOverride = activeWeaponIndex;
        }

        /// <summary>为 EditMode 测试切换当前激活武器槽。</summary>
        public void SetActiveWeaponIndexForTests(int activeWeaponIndex)
        {
            activeWeaponIndexOverride = activeWeaponIndex;
        }
#endif
    }
}
```

- [ ] **Step 5: Run GREEN tests and compile**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe asset refresh
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name Game.Character.Tests.PlayerEquipmentAttributeSyncEditModeTests --timeout 120000
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: sync tests pass and Unity compile reports `errorCount:0`.

- [ ] **Step 6: Commit Task 2**

```powershell
git add Assets/Game/UI/BagInventoryManager.cs Assets/Game/Character/Player/PlayerEquipmentAttributeSync.cs Assets/Game/Editor/PlayerEquipmentAttributeSyncEditModeTests.cs
git commit -m "同步装备属性到玩家战斗属性"
```

---

### Task 3: Final Integration Verification

**Files:**
- Test-only modifications if a focused regression is missing.
- No production changes expected unless tests reveal a real integration gap.

**Interfaces:**
- Verifies: equipment stats reach `CombatAttributeSet.Attack` and `CombatAttributeSet.Defense`.
- Verifies: existing `CombatAbilitySystem` damage formula needs no direct changes.
- Verifies: consumable `buffId` stays excluded from equipment stat calculation.

- [ ] **Step 1: Run focused equipment and combat tests**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name Game.Character.Tests.PlayerAttributeSetEditModeTests --timeout 120000
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name Game.Character.Tests.PlayerEquipmentAttributeSyncEditModeTests --timeout 120000
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name Game.Battle.Tests.CombatAbilityDamageEditModeTests --timeout 120000
.\.aibridge\cli\AIBridgeCLI.exe test run --mode EditMode --group-name Game.Battle.Tests.CombatBuffControllerEditModeTests --timeout 120000
```

Expected: all focused tests pass with `failed:0`.

- [ ] **Step 2: Run Unity compile**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe compile unity
```

Expected: `success:true`, `errorCount:0`, `warningCount:0`.

- [ ] **Step 3: Check Unity error logs**

Run:

```powershell
.\.aibridge\cli\AIBridgeCLI.exe get_logs --logType Error
```

Expected: `count:0`.

- [ ] **Step 4: Confirm data boundaries**

Run:

```powershell
rg -n '"defense"' Assets/Data/ItemConfig/WeaponItemConfig.json Assets/Data/ItemConfig/ConsumableItemConfig.json
rg -n '"attack"' Assets/Data/ItemConfig/HelmetItemConfig.json Assets/Data/ItemConfig/ArmorItemConfig.json Assets/Data/ItemConfig/LeggingsItemConfig.json Assets/Data/ItemConfig/GlovesItemConfig.json Assets/Data/ItemConfig/ConsumableItemConfig.json
rg -n '"buffId"' Assets/Data/ItemConfig/WeaponItemConfig.json Assets/Data/ItemConfig/HelmetItemConfig.json Assets/Data/ItemConfig/ArmorItemConfig.json Assets/Data/ItemConfig/LeggingsItemConfig.json Assets/Data/ItemConfig/GlovesItemConfig.json
```

Expected:
- first command returns no matches.
- second command returns no matches.
- third command returns no matches.

- [ ] **Step 5: Commit validation fixes only if needed**

If Step 1-4 reveal fixes, stage only touched equipment/combat files:

```powershell
git add Assets/Game/Battle/Combat/Core/CombatDefinitions.cs Assets/Game/Battle/Ability/CombatAttributeSet.cs Assets/Game/UI/BagInventoryManager.cs Assets/Game/Character/Player/PlayerEquipmentAttributeSync.cs Assets/Game/Editor/PlayerAttributeSetEditModeTests.cs Assets/Game/Editor/PlayerEquipmentAttributeSyncEditModeTests.cs Assets/Game/Editor/CombatBuffControllerEditModeTests.cs
git commit -m "完善装备属性战斗计算验证"
```

If no files changed during validation, do not create an empty commit.

---

## Self-Review

- Spec coverage: Task 1 implements `CombatAttributeSet` equipment bonuses and Buff ordering; Task 2 implements inventory/equipment event-driven sync and current-weapon-only attack; Task 3 verifies combat damage uses the existing formula and confirms consumables remain excluded.
- Placeholder scan: This plan contains concrete file paths, method signatures, test code, commands, and expected outcomes. No `TBD`, `TODO`, or unresolved placeholders remain.
- Type consistency: `SetEquipmentAttributeBonus`, `EquipmentAttackBonus`, `EquipmentDefenseBonus`, `AttackBonus`, `DefenseBonus`, and `PlayerEquipmentAttributeSync.RefreshEquipmentAttributes` are introduced before later tasks consume them.