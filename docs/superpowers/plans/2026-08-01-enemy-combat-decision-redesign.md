# 敌人战斗决策重构 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将全部近战敌人迁移到“行为树调度 + 独立战斗决策器”架构，并为剑盾敌人实现五种基础攻击、分支连招、输入触发防御和低稳定值闪避。

**Architecture:** 行为树只选择闪避、防御、攻击流程和对峙分支；`EnemyCombatDecisionController` 保存攻击意图、攻击阶段、动作和组合路线。玩家按下默认攻击键时发布强类型事件，敌人仅在对峙或攻击结束阶段即时生成反应请求。

**Tech Stack:** Unity 2022.3.61f1c1、C# 9.0、ScriptableObject/Unity YAML、现有 BehaviorTree、CombatAbilitySystem、NUnit EditMode/PlayMode、AIBridgeCLI。

## Global Constraints

- 所有新增或修改函数必须添加简体中文用途注释。
- Unity 编译只能使用 `./.aibridge/cli/AIBridgeCLI.exe compile unity`。
- 禁止保留旧字段兼容分支，配置错误必须快速失败。
- 对峙状态现有 `CombatIdle -> MoveLeft/MoveRight` 表现不得修改。
- 防御伤害结算保持现状；闪避无敌复用 `CombatTag.Invincible`。
- 攻击进行阶段收到的玩家输入必须立即丢弃，不得缓存。

---

## 文件结构

**新增配置与运行时类型**

- `Assets/Game/Character/Enemy/Config/EnemyBasicAttackConfig.cs`：基础动作技能、动画和权重。
- `Assets/Game/Character/Enemy/Config/EnemyComboBranchConfig.cs`：起始技能、后续序列和分支概率。
- `Assets/Game/Character/Enemy/AI/Combat/EnemyCombatDecisionController.cs`：攻击决策、阶段、组合字典和输入反应。
- `Assets/Game/Character/Enemy/AI/Combat/EnemyCombatDecisionState.cs`：战斗状态、攻击阶段和反应枚举。
- `Assets/Game/EventArgs/PlayerAttackInputEventArgs.cs`：玩家按下默认攻击键的强类型事件。
- `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyAttackFlowNodeAsset.cs`：驱动追击和攻击三阶段。
- `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyDefenseNodeAsset.cs`：驱动防御动画生命周期。
- `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyDodgeNodeAsset.cs`：驱动闪避动画和无敌生命周期。
- `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyHasCombatReactionNodeAsset.cs`：按反应类型选择闪避或防御分支。

**主要修改文件**

- `EnemyCombatConfig.cs`、`EnemyDecisionProfile.cs`、`EnemyDefinitionValidator.cs`、`EnemyDefinitionEditor.cs`：新配置与全局迁移。
- `PlayerController.cs`、`PlayerStateMachine.cs`：玩家默认攻击范围和按键事件。
- `AIController.cs`、`EnemyBlackboard.cs`、`EnemyStateContext.cs`：决策器创建、事实刷新和行为树访问。
- `EnemyCombatComponent.cs`、`EnemyLifeComponent.cs`：动画结束防御和无敌中断清理。
- `EnemySetIntentNodeAsset.cs`：删除旧攻击、防御、后撤实现。
- 两套近战行为树资源与定义资产：接入新分支并迁移配置。

---

### Task 1: 替换战斗配置结构并建立严格校验

**Files:**
- Create: `Assets/Game/Character/Enemy/Config/EnemyBasicAttackConfig.cs`
- Create: `Assets/Game/Character/Enemy/Config/EnemyComboBranchConfig.cs`
- Modify: `Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs`
- Modify: `Assets/Game/Character/Enemy/Config/EnemyDecisionProfile.cs`
- Modify: `Assets/Game/Character/Enemy/Config/EnemyDefinitionValidator.cs`
- Modify: `Assets/Game/Editor/EnemyDefinitionEditor.cs`
- Delete: `Assets/Game/Character/Enemy/Config/EnemyWeightedSkill.cs`
- Test: `Assets/Game/Editor/EnemyCombatDecisionConfigEditModeTests.cs`

**Interfaces:**
- Produces: `EnemyBasicAttackConfig`, `EnemyComboBranchConfig`, `EnemyCombatConfig.basicAttacks`, `EnemyCombatConfig.comboBranches`。
- Produces: `attackDecisionCooldown`、`dodgeRate`、`dodgeCooldown`。

- [ ] **Step 1: 写配置失败测试**

覆盖空基础动作、重复技能 ID、空动画、非正总权重、无效组合引用、空组合序列和非正分支概率。

```csharp
[Test]
public void Validate_ComboReferencesUnknownSkill_AddsError()
{
    EnemyDefinition definition = CreateValidDefinition();
    definition.CombatConfig.basicAttacks = new[]
    {
        new EnemyBasicAttackConfig(20001, "Attack1", 1f)
    };
    definition.CombatConfig.comboBranches = new[]
    {
        new EnemyComboBranchConfig(20001, new[] { 20002 }, 1f)
    };

    EnemyDefinitionValidationResult result = EnemyDefinitionValidator.Validate(definition);

    Assert.IsTrue(result.HasError("ComboBranches"));
}
```

- [ ] **Step 2: 运行测试并确认失败**

Run: `./.aibridge/cli/AIBridgeCLI.exe test run --mode EditMode --group-name EnemyCombatDecisionConfigEditModeTests --timeout 240000`

Expected: FAIL，缺少新配置类型或校验规则。

- [ ] **Step 3: 实现配置类型并删除旧字段**

```csharp
[Serializable]
public sealed class EnemyBasicAttackConfig
{
    public int skillId;
    public string animationName;
    public float weight = 1f;

    /// <summary>创建空配置供 Unity 序列化。</summary>
    public EnemyBasicAttackConfig() { }

    /// <summary>创建指定基础攻击配置供测试和编辑器工具使用。</summary>
    public EnemyBasicAttackConfig(int skillId, string animationName, float weight)
    {
        this.skillId = skillId;
        this.animationName = animationName;
        this.weight = weight;
    }
}
```

`EnemyCombatConfig` 删除 `firstAttackSkillId`、`normalComboSkillIds`，新增两个数组；`EnemyDecisionProfile` 删除 `skillWeights`、`retreatDesire`、`retreatCooldown`、`minSafeDistance`、`retreatDistance`、`defenseDuration`，将 `attackCooldown` 改为 `attackDecisionCooldown` 并新增闪避参数。

- [ ] **Step 4: 更新校验器和中文 Inspector 标签**

校验器使用 `HashSet<int>` 验证技能唯一性与组合引用，分支概率只要求大于零，运行时负责归一化。

- [ ] **Step 5: 运行配置测试**

Expected: `EnemyCombatDecisionConfigEditModeTests` 全部 PASS。

- [ ] **Step 6: 提交**

```bash
git add Assets/Game/Character/Enemy/Config Assets/Game/Editor/EnemyCombatDecisionConfigEditModeTests.cs Assets/Game/Editor/EnemyDefinitionEditor.cs
git commit -m "重构敌人战斗决策配置"
```

### Task 2: 实现纯运行时战斗决策器

**Files:**
- Create: `Assets/Game/Character/Enemy/AI/Combat/EnemyCombatDecisionState.cs`
- Create: `Assets/Game/Character/Enemy/AI/Combat/EnemyCombatDecisionController.cs`
- Modify: `Assets/Game/Character/Enemy/AI/EnemyDecisionRandom.cs`
- Test: `Assets/Game/Editor/EnemyCombatDecisionControllerEditModeTests.cs`

**Interfaces:**
- Consumes: `EnemyCombatConfig`、`EnemyDecisionProfile`。
- Produces: `TryCreateAttackIntent`、`SelectInitialAttack`、`TryAdvanceCombo`、`TryHandlePlayerAttackInput`、`ResetAttack`。

- [ ] **Step 1: 写决策器失败测试**

测试稳定值硬门槛、追击范围外强制攻击、欲望冷却、加权选择只执行一次、组合字典分组、概率分支锁定、续段距离/前方失败和闪避优先防御。

```csharp
[Test]
public void TryCreateAttackIntent_LowStability_ReturnsFalseEvenBeyondChaseRange()
{
    EnemyCombatDecisionController controller = CreateController(attackDesire: 1f);

    bool created = controller.TryCreateAttackIntent(
        time: 10f,
        stabilityRatio: 0.24f,
        isInChaseRange: false,
        randomValue: 0f);

    Assert.IsFalse(created);
}
```

- [ ] **Step 2: 运行测试并确认失败**

Expected: FAIL，决策器类型不存在。

- [ ] **Step 3: 实现状态和核心 API**

```csharp
public enum EnemyCombatDecisionState { Confrontation, Attack, Defense, Dodge }
public enum EnemyAttackPhase { None, Pursuit, Start, Active, End }
public enum EnemyCombatReaction { None, Defense, Dodge }
```

决策器构造时建立 `Dictionary<int, EnemyComboBranchConfig[]>`；攻击动作和组合路线一旦选中即保存到字段，直到结束或中断。

- [ ] **Step 4: 实现概率与组合选择**

扩展 `EnemyDecisionRandom` 为基础动作和组合分支提供确定性 `randomValue` 入口，生产路径传 `Random.value`，测试路径传固定值。

- [ ] **Step 5: 运行测试并提交**

```bash
git add Assets/Game/Character/Enemy/AI/Combat Assets/Game/Character/Enemy/AI/EnemyDecisionRandom.cs Assets/Game/Editor/EnemyCombatDecisionControllerEditModeTests.cs
git commit -m "实现敌人战斗决策器"
```

### Task 3: 发布玩家攻击按键事件和默认攻击范围

**Files:**
- Create: `Assets/Game/EventArgs/PlayerAttackInputEventArgs.cs`
- Modify: `Assets/Game/Character/Player/PlayerController.cs`
- Modify: `Assets/Game/Character/Player/PlayerStateMachine.cs`
- Test: `Assets/Game/Editor/PlayerAttackInputEventEditModeTests.cs`

**Interfaces:**
- Produces: `PlayerController.DefaultAttackRange`。
- Produces: `PlayerAttackInputEventArgs.EventId`、`Player`、`DefaultAttackRange`。

- [ ] **Step 1: 写事件测试**

验证一次按键检测只发布一次强类型事件，并携带玩家 Transform 与默认攻击范围。

- [ ] **Step 2: 新增事件与唯一范围数据源**

```csharp
public sealed class PlayerAttackInputEventArgs : EventArgsBase
{
    public static readonly int EventId = typeof(PlayerAttackInputEventArgs).GetHashCode();
    public override int Id => EventId;
    public Transform Player { get; }
    public float DefaultAttackRange { get; }

    /// <summary>创建一次玩家默认攻击按键事件。</summary>
    public PlayerAttackInputEventArgs(Transform player, float defaultAttackRange)
    {
        Player = player;
        DefaultAttackRange = defaultAttackRange;
    }
}
```

`PlayerController` 新增序列化 `defaultAttackRange = 2f` 与只读属性。`PlayerStateMachine.Update` 在 FSM 更新前检测一次按键并发布事件；现有状态仍可在同帧读取 `Input.GetMouseButtonDown(0)`，不改变玩家攻击输入行为。

- [ ] **Step 3: 运行测试并提交**

```bash
git add Assets/Game/EventArgs/PlayerAttackInputEventArgs.cs Assets/Game/Character/Player Assets/Game/Editor/PlayerAttackInputEventEditModeTests.cs
git commit -m "发布玩家攻击输入事件"
```

### Task 4: 将决策器接入 AI、黑板与输入反应

**Files:**
- Modify: `Assets/Game/Character/Enemy/AI/AIController.cs`
- Modify: `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs`
- Modify: `Assets/Game/Character/Enemy/Core/EnemyStateContext.cs`
- Test: `Assets/Game/Editor/EnemyCombatReactionEditModeTests.cs`

**Interfaces:**
- Produces: `AIController.CombatDecision`。
- Produces: 黑板当前战斗状态、攻击阶段和待执行反应事实。

- [ ] **Step 1: 写输入状态门控测试**

覆盖对峙/攻击结束接受输入，追击/攻击起始/进行/防御/闪避丢弃输入；距离使用玩家默认攻击范围；前方使用水平点积 `>= 0`。

- [ ] **Step 2: 在 AI 生命周期订阅事件**

`AIController.OnEnable/OnDisable` 订阅和解除 `PlayerAttackInputEventArgs.EventId`。回调只把当前即时事实交给决策器；不符合状态时不写黑板。

- [ ] **Step 3: 删除过近距离事实**

从 `RefreshDecisionFacts` 和 `EnemyBlackboard` 删除 `IsTooCloseToTarget`，保留攻击、战斗和追击三种范围事实。

- [ ] **Step 4: 运行测试并提交**

```bash
git add Assets/Game/Character/Enemy/AI/AIController.cs Assets/Game/Character/Enemy/Core Assets/Game/Editor/EnemyCombatReactionEditModeTests.cs
git commit -m "接入敌人战斗输入反应"
```

### Task 5: 实现攻击、防御和闪避行为节点

**Files:**
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyAttackFlowNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyDefenseNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyDodgeNodeAsset.cs`
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyHasCombatReactionNodeAsset.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyAnimationComponent.cs`
- Test: `Assets/Game/Editor/EnemyCombatActionFlowEditModeTests.cs`

**Interfaces:**
- Consumes: `AIController.CombatDecision` 和现有 Movement/Animation/Combat。
- Produces: 行为树可持续返回 `Running` 的三个动作节点。

- [ ] **Step 1: 写阶段流转测试**

覆盖追击、进入范围、起始对准、动画进行、结束反应、组合续段、动画结束防御和闪避无敌清理。

- [ ] **Step 2: 实现攻击流节点**

节点在 `Pursuit` 使用现有移动组件和 `Run`；`Start` 调用 `LookAtInstant`、`TryStartAttack(skillId)`、`TryPlay(animationName, false)`；`Active` 用 `IsPlaying(animationName, out normalizedTime)` 等待 `normalizedTime >= 1f`；`End` 先消费反应，再推进组合或重置回对峙。

- [ ] **Step 3: 将防御改为动画生命周期**

删除 `EnemyCombatComponent.Tick` 的防御倒计时和 `StartDefense(float)`，改为无计时 `StartDefense()`；防御节点在动画完成或中断时调用 `StopDefense()`。

- [ ] **Step 4: 实现闪避无敌**

闪避节点进入时调用 `CombatAbilitySystem.AddTag(CombatTag.Invincible)`，退出、重置或中断时调用 `RemoveTag`，确保所有出口清理。

- [ ] **Step 5: 运行测试并提交**

```bash
git add Assets/Game/Character/Enemy/AI/BehaviorTree Assets/Game/Character/Enemy/Components Assets/Game/Editor/EnemyCombatActionFlowEditModeTests.cs
git commit -m "实现敌人攻击防御闪避流程"
```

### Task 6: 删除旧决策节点和旧意图实现

**Files:**
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySetIntentNodeAsset.cs`
- Delete: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySelectWeightedSkillNodeAsset.cs`
- Delete: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyShouldRetreatNodeAsset.cs`
- Delete: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyShouldAttackNodeAsset.cs`
- Delete: `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyShouldDefendNodeAsset.cs`
- Delete: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemySetCombatIntentNodeAsset.cs`
- Modify: existing enemy decision tests.

**Interfaces:**
- Consumes: 新动作节点已经覆盖攻击、防御、闪避。
- Produces: `EnemySetIntentNodeAsset` 只保留巡逻、警戒、搜索、对峙和中断等非新决策职责。

- [ ] **Step 1: 写无旧类型引用测试**

测试近战行为树不再引用旧后撤、加权技能和旧攻击条件节点。

- [ ] **Step 2: 删除旧方法与字段**

从 `EnemySetIntentNodeAsset` 删除 `TickAttack`、攻击接近/组合、`TickDefense`、`TickRetreat` 及对应运行字段；保留 `TickCombatIdle` 原样。

- [ ] **Step 3: 删除旧类型并更新全部资产引用**

删除上述 `.cs` 及对应 `.meta`，并把 GuardMelee、SwordAndShieldEnemy、TrainingDummy 三套行为树中的旧节点引用全部替换为新战斗节点或非战斗保持节点，确保项目没有旧类型资产引用。

- [ ] **Step 4: 编译并提交**

```bash
./.aibridge/cli/AIBridgeCLI.exe compile unity
git add Assets/Game/Character/Enemy/AI/BehaviorTree Assets/Game/Editor
git commit -m "清理旧敌人战斗决策逻辑"
```

### Task 7: 迁移行为树与敌人定义资产

**Files:**
- Modify: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEnemy/Combat/**`
- Modify: `Assets/Game/Character/Enemy/Config/BehaviorTrees/GuardMelee/**`
- Modify: `Assets/Game/Character/Enemy/Config/Definitions/SwordAndShieldEnemyDefinition.asset`
- Modify: `Assets/Game/Character/Enemy/Config/Definitions/GuardMeleeEnemyDefinition.asset`
- Test: `Assets/Game/Editor/SwordAndShieldBehaviorTreeAssetEditModeTests.cs`
- Test: `Assets/Game/Editor/GuardMeleeBehaviorTreeAssetEditModeTests.cs`

**Interfaces:**
- Produces: 两套近战树统一优先级 `Dodge -> Defense -> AttackFlow -> CombatIdle`。

- [ ] **Step 1: 写行为树资产失败测试**

断言战斗选择器顺序、节点类型和旧资源不存在。

- [ ] **Step 2: 创建并接线新节点资产**

通过 Unity AssetDatabase/AIBridge 创建闪避、防御、攻击流程节点和条件节点，更新选择器引用；保留剑盾四层树和 `CombatIdleSequence`。

- [ ] **Step 3: 写入新配置**

剑盾基础动作：`20001/Attack1`、`20002/Attack2`、`20003/Attack3`、`20004/Attack4`、`20005/Attack5`，权重均为 `1`；两个 `20001` 起始组合分支分别为 `[20002,20003]` 与 `[20002,20005]`，概率均为 `0.5`。写入已确认决策参数。

GuardMelee 按其现有可用技能迁移为基础动作配置，若只有 `20001-20003`，三项权重均为 `1`，组合为 `[20002,20003]`。

- [ ] **Step 4: 运行资产测试并提交**

```bash
git add Assets/Game/Character/Enemy/Config Assets/Game/Editor/*BehaviorTreeAssetEditModeTests.cs
git commit -m "迁移近战敌人行为树配置"
```

### Task 8: 增加 Attack4/Attack5 技能与 Animator 状态

**Files:**
- Modify: `Assets/Data/EnemySkillConfig.json`
- Modify: `Assets/Res/AnimatorController/Enemy/SwordAndShieldEnemy.controller`
- Test: `Assets/Game/Editor/SwordAndShieldAttackAssetEditModeTests.cs`

**Interfaces:**
- Produces: 技能 `20004/Attack4`、`20005/Attack5` 和对应 Animator 状态。

- [ ] **Step 1: 写技能与 Animator 失败测试**

测试配置管理器能读取两个技能，动画名匹配；Animator 包含有 Motion 的 `Attack4`、`Attack5` 状态。

- [ ] **Step 2: 添加技能 JSON**

复制 `20003` 的命中、打断、特效和音频参数，只修改 ID、名称、动画名并令 `comboNextSkillId = 0`，避免本次结构重构引入额外数值平衡。

- [ ] **Step 3: 创建 Animator 状态**

使用 Unity Editor API 创建：

```text
Attack4 -> Assets/DoubleL/FBX_Animations/One Hand Base/Fatal/Attack/1Hand_Base_Fatal_Attack_4.fbx
Attack5 -> Assets/DoubleL/FBX_Animations/One Hand Base/Fatal/Attack/1Hand_Base_Fatal_Attack_5.fbx
```

复制现有攻击状态的速度、Foot IK、Write Defaults 和过渡策略，并确认动画事件命中窗口已保留或补齐。

- [ ] **Step 4: 运行测试并提交**

```bash
git add Assets/Data/EnemySkillConfig.json Assets/Res/AnimatorController/Enemy/SwordAndShieldEnemy.controller Assets/Game/Editor/SwordAndShieldAttackAssetEditModeTests.cs
git commit -m "补充剑盾敌人蓄力攻击资源"
```

### Task 9: 全量回归与运行时验收

**Files:**
- Modify as needed: `Assets/Game/Editor/Enemy*EditModeTests.cs`
- Runtime scene: `Assets/Scenes/Scene1.unity`（只在确有必要时修改）。

- [ ] **Step 1: 运行相关 EditMode 测试**

Run: configuration、decision controller、reaction、action flow、behavior tree、attack asset 测试组。

Expected: 全部 PASS。

- [ ] **Step 2: 运行 Unity 编译和 Error 日志检查**

```bash
./.aibridge/cli/AIBridgeCLI.exe compile unity
./.aibridge/cli/AIBridgeCLI.exe get_logs --logType Error
```

Expected: 编译 `0 errors`，Error 日志为空。

- [ ] **Step 3: PlayMode 验收**

验证对峙概率、范围外强制攻击、五种基础动作、两条组合分支、续段中止、输入阶段丢弃、闪避优先、防御 `DefenseHit`、闪避无敌和中断清理。

- [ ] **Step 4: 最终提交**

```bash
git add Assets/Game/Editor
git commit -m "完善敌人战斗决策回归测试"
```

## 完成标准

- 旧字段、旧随机后撤和旧攻击选择逻辑不存在；
- SwordAndShield 与 GuardMelee 均通过新配置校验；
- 所有随机检定只在决策事件发生时执行一次；
- 攻击、防御、闪避生命周期符合设计文档；
- Unity 编译、Error 日志、EditMode 和 PlayMode 验证全部通过。
