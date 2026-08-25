# 剑盾精英敌人行为树实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立按技能范围驱动的敌人攻击计划框架，并让剑盾精英通过独立行为树执行基础攻击、五段组合、突刺、快速飞跃和防御反击。

**Architecture:** 全局 `SkillConfig` 保存技能唯一攻击范围，敌人定义保存技能编号、动画名和权重。AI 启动时构建只读攻击目录，`EnemyCombatDecisionController` 生成并锁定攻击计划，行为树只负责准备、移动、播放动画和衔接连招。

**Tech Stack:** Unity 2022.3.61f1c1、C# 9.0、ScriptableObject 行为树、NUnit EditMode 测试、Newtonsoft.Json、AIBridge CLI PlayMode 验证。

---

## 执行约束

- 保留当前未提交的 `EnemyAgent.cs` 和剑盾精英资产改动，在其基础上迁移。
- 所有新增或修改的 C# 函数都添加简体中文用途注释。
- 使用强类型和快速失败，不吞没配置异常。
- Unity 编译只运行 `& "./.aibridge/cli/AIBridgeCLI.exe" compile unity`。
- `compile dotnet` 不作为 Unity 编译替代或回退。
- 复杂 ScriptableObject 行为树资产通过 Unity Editor API 生成，不直接拼接 GUID。
- 每项任务只暂存其列出的文件，提交信息使用简体中文。

## 文件职责

### 新建文件

- `Assets/Game/Character/Enemy/Config/EnemyAttackConfig.cs`：敌人侧技能编号、动画名和权重。
- `Assets/Game/Character/Enemy/AI/Combat/EnemyAttackRuntimeConfig.cs`：敌人攻击条目与全局技能配置的运行时绑定。
- `Assets/Game/Character/Enemy/AI/Combat/EnemyAttackCatalog.cs`：基础、进身、追击和反击攻击目录。
- `Assets/Game/Character/Enemy/AI/Combat/EnemyAttackPlan.cs`：攻击类型、准备方式、当前技能与锁定连招状态。
- `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyPrepareAttackPlanNodeAsset.cs`：按计划距离接近目标或允许直接释放。
- `Assets/Game/Editor/SkillAttackRangeConfigEditModeTests.cs`：技能 JSON 合法性和攻击范围数据测试。
- `Assets/Game/Editor/EnemyAttackCatalogEditModeTests.cs`：运行时攻击目录测试。
- `Assets/Game/Editor/EnemyAttackPlanDecisionEditModeTests.cs`：基础、进身和追击计划选择测试。
- `Assets/Game/Editor/EnemyCounterAttackEditModeTests.cs`：格挡计数与反击计划测试。
- `Assets/Game/Editor/SwordAndShieldEliteBehaviorTreeAssetEditModeTests.cs`：精英行为树独立性和配置测试。
- `Assets/Game/Editor/EnemyBehaviorTreeAssetBuilder.cs`：通过 Unity API 重建通用战斗树和精英独立战斗树。

### 重命名文件

- `Assets/Game/Character/Enemy/Config/EnemyBasicAttackConfig.cs` → `Assets/Game/Character/Enemy/Config/EnemyAttackConfig.cs`，保留原 `.meta` GUID。

### 主要修改文件

- `Assets/Game/Battle/Skill/Common/SkillConfig.cs`
- `Assets/Framework/Manager/ConfigManager.cs`
- `Assets/Data/EnemySkillConfig.json`
- `Assets/Data/WeaponConfig/SingleSwordSkillConfig.json`
- `Assets/Data/WeaponConfig/GreatSwordSkillConfig.json`
- `Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs`
- `Assets/Game/Character/Enemy/Config/EnemyDecisionProfile.cs`
- `Assets/Game/Character/Enemy/Config/EnemyDefinitionValidator.cs`
- `Assets/Game/Editor/EnemyDefinitionEditor.cs`
- `Assets/Game/Character/Enemy/AI/EnemyDecisionRandom.cs`
- `Assets/Game/Character/Enemy/AI/Combat/EnemyCombatDecisionController.cs`
- `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs`
- `Assets/Game/Character/Enemy/AI/AIController.cs`
- `Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs`
- `Assets/Game/Character/Enemy/AI/BehaviorTree/Utilities/EnemyBehaviorTreeUtility.cs`
- `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyGenerateAttackIntentNodeAsset.cs`
- `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyAttackFlowNodeAsset.cs`
- `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyDefenseNodeAsset.cs`
- `Assets/Game/Character/Enemy/Config/Definitions/GreatSwordEnemyDefinition.asset`
- `Assets/Game/Character/Enemy/Config/Definitions/SpearEenemyDefinition.asset`
- `Assets/Game/Character/Enemy/Config/Definitions/SwordAndShieldEnemyDefinition.asset`
- `Assets/Game/Character/Enemy/Config/Definitions/SwordAndShieldEliteEnemyDefinition.asset`
- `Assets/Game/Character/Enemy/Config/BehaviorTrees/Common/`
- `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEliteEnemy/`

### 删除文件

- `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyIsInAttackRangeNodeAsset.cs`
- `Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions/EnemyIsOutsideAttackRangeNodeAsset.cs`
- 两个脚本对应的 `.meta`。
- 通用行为树中迁移后无引用的 `IsInAttackRange.asset`、`IsOutsideAttackRange.asset`、`ChaseSequence.asset` 和 `InAttackRangeSequence.asset`。

---

### Task 1: 增加全局技能攻击范围并修复技能数据

**Files:**
- Create: `Assets/Game/Editor/SkillAttackRangeConfigEditModeTests.cs`
- Modify: `Assets/Game/Battle/Skill/Common/SkillConfig.cs`
- Modify: `Assets/Framework/Manager/ConfigManager.cs`
- Modify: `Assets/Data/EnemySkillConfig.json`
- Modify: `Assets/Data/WeaponConfig/SingleSwordSkillConfig.json`
- Modify: `Assets/Data/WeaponConfig/GreatSwordSkillConfig.json`

- [ ] **Step 1: 编写技能配置解析与范围测试**

```csharp
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Game.Character.Enemy.Tests
{
    public sealed class SkillAttackRangeConfigEditModeTests
    {
        private static readonly string[] ConfigPaths =
        {
            "Assets/Data/EnemySkillConfig.json",
            "Assets/Data/WeaponConfig/SingleSwordSkillConfig.json",
            "Assets/Data/WeaponConfig/GreatSwordSkillConfig.json"
        };

        /// <summary>验证所有技能配置文件均为合法 JSON，且每个技能都有正数攻击范围。</summary>
        [Test]
        public void SkillConfigFiles_AllSkillsHavePositiveAttackRange()
        {
            for (int pathIndex = 0; pathIndex < ConfigPaths.Length; pathIndex++)
            {
                JArray skills = JArray.Parse(File.ReadAllText(ConfigPaths[pathIndex]));
                for (int skillIndex = 0; skillIndex < skills.Count; skillIndex++)
                {
                    int skillId = skills[skillIndex].Value<int>("skillId");
                    float attackRange = skills[skillIndex].Value<float>("attackRange");
                    Assert.Greater(attackRange, 0f, ConfigPaths[pathIndex] + " 技能 " + skillId);
                }
            }
        }

        /// <summary>验证剑盾精英九个技能编号连续且范围符合已确认配置。</summary>
        [Test]
        public void EnemySkillConfig_EliteSkillRangesMatchDesign()
        {
            JArray skills = JArray.Parse(File.ReadAllText(ConfigPaths[0]));
            for (int skillId = 20301; skillId <= 20309; skillId++)
            {
                JObject skill = FindSkill(skills, skillId);
                float expectedRange = skillId == 20307 ? 6f : skillId == 20308 ? 10f : 4f;
                Assert.AreEqual(expectedRange, skill.Value<float>("attackRange"));
            }
        }

        /// <summary>按技能编号查找唯一配置，缺失或重复时立即让测试失败。</summary>
        private static JObject FindSkill(JArray skills, int skillId)
        {
            JObject result = null;
            for (int index = 0; index < skills.Count; index++)
            {
                JObject candidate = (JObject)skills[index];
                if (candidate.Value<int>("skillId") != skillId)
                {
                    continue;
                }

                Assert.IsNull(result, "技能编号重复：" + skillId);
                result = candidate;
            }

            Assert.NotNull(result, "缺少技能：" + skillId);
            return result;
        }
    }
}
```

- [ ] **Step 2: 运行测试并确认当前技能 JSON 失败**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.SkillAttackRangeConfigEditModeTests"
```

Expected: FAIL。`EnemySkillConfig.json` 在 `20205`、`20206` 附近解析失败，其他技能也缺少 `attackRange`。

- [ ] **Step 3: 增加模型字段与加载校验**

在 `SkillConfig` 的 `skillAnimationName` 后加入：

```csharp
public float attackRange;
```

在 `ConfigManager.ValidateSkillConfig` 的技能编号校验后加入：

```csharp
if (config.attackRange <= 0f)
{
    throw new Exception($"技能{config.skillId}攻击范围必须大于零");
}
```

- [ ] **Step 4: 使用结构化 JSON 修改补齐范围和精英技能**

修复 `20205`、`20206` 的非法字符串引号，并按下表写入数据：

| 技能集合 | 编号 | `attackRange` |
|---|---:|---:|
| 剑盾小兵 | `20001`～`20005` | `2` |
| 大剑敌人 | `20101`～`20105` | `2` |
| 长枪敌人 | `20201`～`20209` | `3` |
| 剑盾精英基础/组合 | `20301`～`20306` | `4` |
| 剑盾精英突刺 | `20307` | `6` |
| 剑盾精英快速飞跃 | `20308` | `10` |
| 剑盾精英反击 | `20309` | `4` |
| 玩家单手剑与大剑 | 所有现有编号 | `2` |

新增精英技能时复制下列现有战斗数值模板，所有 `comboNextSkillId` 设为 `0`，敌人动画不写入全局技能配置：

| 新编号 | 名称 | 数值模板 |
|---:|---|---:|
| `20301` | 二连斩 | `20001` |
| `20302` | 上挑 | `20004` |
| `20303` | 左劈 | `20001` |
| `20304` | 盾击 | `20003` |
| `20305` | 跃击 | `20004` |
| `20306` | 后撤 | `20002` |
| `20307` | 突刺 | `20005` |
| `20308` | 快速飞跃 | `20005` |
| `20309` | 反击 | `20003` |

`20309.interruptConfig.canInterrupt` 保持 `true`，确保反击命中走统一打断流程。

- [ ] **Step 5: 编译并运行技能配置测试**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.SkillAttackRangeConfigEditModeTests"
```

Expected: 编译 `errorCount: 0`，测试全部 PASS。

- [ ] **Step 6: 提交全局技能范围**

```powershell
git add Assets/Game/Battle/Skill/Common/SkillConfig.cs Assets/Framework/Manager/ConfigManager.cs Assets/Data/EnemySkillConfig.json Assets/Data/WeaponConfig/SingleSwordSkillConfig.json Assets/Data/WeaponConfig/GreatSwordSkillConfig.json Assets/Game/Editor/SkillAttackRangeConfigEditModeTests.cs
git commit -m "功能：为技能增加统一攻击范围"
```

---

### Task 2: 重构敌人攻击配置与校验

**Files:**
- Rename: `Assets/Game/Character/Enemy/Config/EnemyBasicAttackConfig.cs` → `Assets/Game/Character/Enemy/Config/EnemyAttackConfig.cs`
- Modify: `Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs`
- Modify: `Assets/Game/Character/Enemy/Config/EnemyDecisionProfile.cs`
- Modify: `Assets/Game/Character/Enemy/Config/EnemyDefinitionValidator.cs`
- Modify: `Assets/Game/Editor/EnemyDefinitionEditor.cs`
- Modify: `Assets/Game/Editor/EnemyCombatDecisionConfigEditModeTests.cs`
- Modify: all tests that construct `EnemyBasicAttackConfig`

- [ ] **Step 1: 把配置测试改为新结构并增加失败用例**

在 `EnemyCombatDecisionConfigEditModeTests` 增加：

```csharp
/// <summary>验证反击技能不能重复出现在普通候选池。</summary>
[Test]
public void Validate_CounterAttackDuplicatedInBasicPool_AddsError()
{
    EnemyDefinition definition = CreateValidDefinition();
    definition.CombatConfig.basicAttacks = new[]
    {
        new EnemyAttackConfig(20001, "Attack1", 1f)
    };
    definition.CombatConfig.counterAttack = new EnemyAttackConfig(20001, "Counter", 1f);
    definition.CombatConfig.counterBlockThreshold = 2;

    EnemyDefinitionValidationResult result = EnemyDefinitionValidator.Validate(definition);

    Assert.IsTrue(result.HasError("CounterAttack"));
    DestroyDefinition(definition);
}

/// <summary>验证配置反击技能时格挡阈值必须为正数。</summary>
[Test]
public void Validate_CounterAttackWithZeroThreshold_AddsError()
{
    EnemyDefinition definition = CreateValidDefinition();
    definition.CombatConfig.counterAttack = new EnemyAttackConfig(20309, "Attack3", 1f);
    definition.CombatConfig.counterBlockThreshold = 0;

    EnemyDefinitionValidationResult result = EnemyDefinitionValidator.Validate(definition);

    Assert.IsTrue(result.HasError("CounterBlockThreshold"));
    DestroyDefinition(definition);
}
```

同时把测试中的 `EnemyBasicAttackConfig` 全部替换为 `EnemyAttackConfig`，并增加进身池、追击池内部重复编号的测试。

- [ ] **Step 2: 运行配置测试并确认编译失败**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyCombatDecisionConfigEditModeTests"
```

Expected: FAIL，缺少 `EnemyAttackConfig` 和新战斗配置字段。

- [ ] **Step 3: 重命名攻击条目并实现通用类型**

保留原 `.meta` 文件，类实现为：

```csharp
using System;

namespace Game.Character.Enemy.Config
{
    [Serializable]
    public sealed class EnemyAttackConfig
    {
        public int skillId;
        public string animationName;
        public float weight = 1f;

        /// <summary>创建供 Unity 序列化使用的空攻击配置。</summary>
        public EnemyAttackConfig()
        {
        }

        /// <summary>创建指定技能、动画和选择权重的敌人攻击配置。</summary>
        public EnemyAttackConfig(int skillId, string animationName, float weight)
        {
            this.skillId = skillId;
            this.animationName = animationName;
            this.weight = weight;
        }
    }
}
```

- [ ] **Step 4: 替换敌人战斗配置字段**

`EnemyCombatConfig` 完整字段顺序固定为：

```csharp
using System;

namespace Game.Character.Enemy.Config
{
    [Serializable]
    public sealed class EnemyCombatConfig
    {
        public EnemyAttackConfig[] basicAttacks =
        {
            new EnemyAttackConfig(20001, "Attack1", 1f)
        };
        public EnemyAttackConfig[] approachAttacks = new EnemyAttackConfig[0];
        public EnemyAttackConfig[] pursuitAttacks = new EnemyAttackConfig[0];
        public EnemyAttackConfig counterAttack;
        public EnemyComboBranchConfig[] comboBranches = new EnemyComboBranchConfig[0];
        public int counterBlockThreshold = 2;
        public float combatEnterRange = 4f;
        public float chaseRange = 6f;
        public float combatMemoryDuration = 4f;
        public bool canInterruptAttack;
    }
}
```

从 `EnemyDecisionProfile` 删除 `counterDesire`。

- [ ] **Step 5: 实现攻击池、连招和反击结构校验**

在 `EnemyDefinitionValidator` 中使用统一方法校验三个候选池：

```csharp
/// <summary>校验敌人攻击池中的必填字段和重复技能编号。</summary>
private static void ValidateAttackPool(
    string fieldName,
    EnemyAttackConfig[] attacks,
    bool requireNonEmpty,
    EnemyDefinitionValidationResult result)
{
    if (attacks == null || attacks.Length == 0)
    {
        if (requireNonEmpty)
        {
            result.AddError(fieldName, "攻击池不能为空");
        }
        return;
    }

    HashSet<int> skillIds = new HashSet<int>();
    for (int index = 0; index < attacks.Length; index++)
    {
        EnemyAttackConfig attack = attacks[index];
        if (attack == null || attack.skillId <= 0 || string.IsNullOrWhiteSpace(attack.animationName) || attack.weight <= 0f)
        {
            result.AddError(fieldName, "攻击条目必须配置正数技能编号、动画名和权重");
            continue;
        }

        if (!skillIds.Add(attack.skillId))
        {
            result.AddError(fieldName, "存在重复技能编号：" + attack.skillId);
        }
    }
}
```

连招起手和每个后续编号都必须存在于 `basicAttacks`。反击技能不得存在于三个候选池，配置反击时阈值必须大于零。

- [ ] **Step 6: 更新自定义 Inspector 与所有构造调用**

`EnemyDefinitionEditor` 按“基础攻击、进身攻击、追击攻击、反击、反击格挡次数、组合分支”的顺序绘制字段。全项目测试构造函数统一改为 `EnemyAttackConfig`。

- [ ] **Step 7: 编译并运行配置回归测试**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyCombatDecisionConfigEditModeTests"
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyDefinitionValidatorEditModeTests"
```

Expected: 编译 `errorCount: 0`，两组测试全部 PASS。

- [ ] **Step 8: 提交敌人配置重构**

```powershell
git add -A -- Assets/Game/Character/Enemy/Config/EnemyBasicAttackConfig.cs Assets/Game/Character/Enemy/Config/EnemyBasicAttackConfig.cs.meta Assets/Game/Character/Enemy/Config/EnemyAttackConfig.cs Assets/Game/Character/Enemy/Config/EnemyAttackConfig.cs.meta
git add Assets/Game/Character/Enemy/Config/EnemyCombatConfig.cs Assets/Game/Character/Enemy/Config/EnemyDecisionProfile.cs Assets/Game/Character/Enemy/Config/EnemyDefinitionValidator.cs Assets/Game/Editor/EnemyDefinitionEditor.cs
git add Assets/Game/Editor/EnemyCombatDecisionConfigEditModeTests.cs Assets/Game/Editor/EnemyDefinitionValidatorEditModeTests.cs Assets/Game/Editor/EnemyDecisionProfileEditModeTests.cs Assets/Game/Editor/EnemyDecisionRandomEditModeTests.cs Assets/Game/Editor/EnemyAttackIntentNodeEditModeTests.cs Assets/Game/Editor/EnemyCombatDecisionControllerEditModeTests.cs Assets/Game/Editor/EnemyCombatReactionEditModeTests.cs Assets/Game/Editor/EnemyCombatActionFlowEditModeTests.cs Assets/Game/Editor/GuardMeleeBehaviorTreeAssetEditModeTests.cs Assets/Game/Editor/SwordAndShieldBehaviorTreeAssetEditModeTests.cs
git commit -m "重构：统一敌人攻击配置结构"
```

提交前使用 `git diff --cached --name-status` 排除精英定义、行为树资产和 `EnemyAgent.cs`。

---

### Task 3: 构建只读运行时攻击目录

**Files:**
- Create: `Assets/Game/Character/Enemy/AI/Combat/EnemyAttackRuntimeConfig.cs`
- Create: `Assets/Game/Character/Enemy/AI/Combat/EnemyAttackCatalog.cs`
- Create: `Assets/Game/Editor/EnemyAttackCatalogEditModeTests.cs`

- [ ] **Step 1: 编写目录构建、范围和缺失技能测试**

```csharp
using System;
using System.Collections.Generic;
using Game.Battle.Skill.Common;
using Game.Character.Enemy.AI.Combat;
using Game.Character.Enemy.Config;
using NUnit.Framework;

namespace Game.Character.Enemy.Tests
{
    public sealed class EnemyAttackCatalogEditModeTests
    {
        /// <summary>验证目录绑定全局范围并计算基础攻击最大范围。</summary>
        [Test]
        public void Create_ValidConfig_BuildsPoolsAndBasicAttackRange()
        {
            EnemyCombatConfig config = new EnemyCombatConfig
            {
                basicAttacks = new[]
                {
                    new EnemyAttackConfig(1, "Attack1", 1f),
                    new EnemyAttackConfig(2, "Attack2", 1f)
                },
                approachAttacks = new[] { new EnemyAttackConfig(3, "Thrust", 1f) },
                pursuitAttacks = new[] { new EnemyAttackConfig(4, "Leap", 1f) },
                counterAttack = new EnemyAttackConfig(5, "Counter", 1f)
            };
            Dictionary<int, SkillConfig> skills = new Dictionary<int, SkillConfig>
            {
                { 1, CreateSkill(1, 2f) },
                { 2, CreateSkill(2, 4f) },
                { 3, CreateSkill(3, 6f) },
                { 4, CreateSkill(4, 10f) },
                { 5, CreateSkill(5, 4f) }
            };

            EnemyAttackCatalog catalog = EnemyAttackCatalog.Create(config, id => skills[id]);

            Assert.AreEqual(4f, catalog.BasicAttackRange);
            Assert.AreEqual(6f, catalog.ApproachAttacks[0].AttackRange);
            Assert.AreEqual(10f, catalog.PursuitAttacks[0].AttackRange);
            Assert.AreEqual(5, catalog.CounterAttack.SkillId);
        }

        /// <summary>验证引用不存在的全局技能时立即失败。</summary>
        [Test]
        public void Create_MissingSkill_Throws()
        {
            EnemyCombatConfig config = new EnemyCombatConfig
            {
                basicAttacks = new[] { new EnemyAttackConfig(99, "Missing", 1f) }
            };

            Assert.Throws<InvalidOperationException>(() =>
                EnemyAttackCatalog.Create(config, id => throw new KeyNotFoundException(id.ToString())));
        }

        /// <summary>创建指定编号和范围的测试技能。</summary>
        private static SkillConfig CreateSkill(int skillId, float attackRange)
        {
            return new SkillConfig { skillId = skillId, attackRange = attackRange };
        }
    }
}
```

- [ ] **Step 2: 运行测试并确认缺少目录类型**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyAttackCatalogEditModeTests"
```

Expected: FAIL，缺少 `EnemyAttackCatalog` 和 `EnemyAttackRuntimeConfig`。

- [ ] **Step 3: 实现运行时攻击条目**

```csharp
using Game.Battle.Skill.Common;
using Game.Character.Enemy.Config;

namespace Game.Character.Enemy.AI.Combat
{
    public sealed class EnemyAttackRuntimeConfig
    {
        public EnemyAttackConfig EnemyConfig { get; }
        public SkillConfig SkillConfig { get; }
        public int SkillId => EnemyConfig.skillId;
        public string AnimationName => EnemyConfig.animationName;
        public float Weight => EnemyConfig.weight;
        public float AttackRange => SkillConfig.attackRange;

        /// <summary>绑定敌人攻击表现配置和全局技能战斗配置。</summary>
        public EnemyAttackRuntimeConfig(EnemyAttackConfig enemyConfig, SkillConfig skillConfig)
        {
            EnemyConfig = enemyConfig;
            SkillConfig = skillConfig;
        }
    }
}
```

- [ ] **Step 4: 实现攻击目录**

`EnemyAttackCatalog.Create` 接收 `Func<int, SkillConfig>`，构建三个只读数组、可空反击条目和基础技能编号字典。解析异常统一包装为包含技能编号的 `InvalidOperationException`。`GetRequiredBasicAttack(int skillId)` 只允许连招引用基础池。

关键公开接口固定为：

```csharp
public IReadOnlyList<EnemyAttackRuntimeConfig> BasicAttacks { get; }
public IReadOnlyList<EnemyAttackRuntimeConfig> ApproachAttacks { get; }
public IReadOnlyList<EnemyAttackRuntimeConfig> PursuitAttacks { get; }
public EnemyAttackRuntimeConfig CounterAttack { get; }
public float BasicAttackRange { get; }
public EnemyAttackRuntimeConfig GetRequiredBasicAttack(int skillId);
public static EnemyAttackCatalog Create(EnemyCombatConfig config, Func<int, SkillConfig> skillResolver);
```

- [ ] **Step 5: 编译并运行目录测试**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyAttackCatalogEditModeTests"
```

Expected: 编译 `errorCount: 0`，测试全部 PASS。

- [ ] **Step 6: 提交攻击目录**

```powershell
git add Assets/Game/Character/Enemy/AI/Combat/EnemyAttackRuntimeConfig.cs Assets/Game/Character/Enemy/AI/Combat/EnemyAttackRuntimeConfig.cs.meta Assets/Game/Character/Enemy/AI/Combat/EnemyAttackCatalog.cs Assets/Game/Character/Enemy/AI/Combat/EnemyAttackCatalog.cs.meta Assets/Game/Editor/EnemyAttackCatalogEditModeTests.cs Assets/Game/Editor/EnemyAttackCatalogEditModeTests.cs.meta
git commit -m "功能：构建敌人运行时攻击目录"
```

---

### Task 4: 实现按距离生成和锁定攻击计划

**Files:**
- Create: `Assets/Game/Character/Enemy/AI/Combat/EnemyAttackPlan.cs`
- Create: `Assets/Game/Editor/EnemyAttackPlanDecisionEditModeTests.cs`
- Modify: `Assets/Game/Character/Enemy/AI/EnemyDecisionRandom.cs`
- Modify: `Assets/Game/Character/Enemy/AI/Combat/EnemyCombatDecisionController.cs`
- Modify: `Assets/Game/Editor/EnemyCombatDecisionControllerEditModeTests.cs`
- Modify: `Assets/Game/Editor/EnemyDecisionRandomEditModeTests.cs`

- [ ] **Step 1: 编写基础、进身、追击和权重过滤测试**

测试至少覆盖以下输入和结果：

| 当前距离 | 追击范围 | 基础最大范围 | 可用技能 | 结果 |
|---:|---:|---:|---|---|
| `3` | `8` | `4` | 基础 `4` | 直接基础计划 |
| `5` | `8` | `4` | 突刺 `6` | 直接进身计划 |
| `7` | `8` | `4` | 突刺 `6` | 普通基础接近计划 |
| `9` | `8` | `4` | 飞跃 `10` | 锁定追击计划，释放距离 `8` |
| `9` | `8` | `4` | 无追击技能 | 锁定基础接近计划 |

代表性测试：

```csharp
/// <summary>验证追击范围外锁定技能且释放距离为技能范围的百分之八十。</summary>
[Test]
public void TryCreateAttackPlan_OutsideChaseRange_LocksPursuitSkill()
{
    EnemyCombatDecisionController controller = CreateController();

    bool created = controller.TryCreateAttackPlan(
        time: 10f,
        stabilityRatio: 1f,
        distanceToTarget: 9f,
        chaseRange: 8f,
        randomValue: 0f);

    Assert.IsTrue(created);
    Assert.AreEqual(EnemyAttackPlanType.Pursuit, controller.CurrentPlan.Type);
    Assert.AreEqual(20308, controller.CurrentPlan.CurrentAttack.SkillId);
    Assert.AreEqual(8f, controller.CurrentPlan.ReleaseDistance);
}
```

- [ ] **Step 2: 运行计划决策测试并确认缺少计划 API**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyAttackPlanDecisionEditModeTests"
```

Expected: FAIL，缺少攻击计划类型和 `TryCreateAttackPlan`。

- [ ] **Step 3: 实现攻击计划类型**

```csharp
namespace Game.Character.Enemy.AI.Combat
{
    public enum EnemyAttackPlanType
    {
        Basic,
        Approach,
        Pursuit,
        Counter
    }

    public enum EnemyAttackPreparationMode
    {
        Direct,
        Approach,
        Pursuit
    }

    public sealed class EnemyAttackPlan
    {
        public EnemyAttackPlanType Type { get; }
        public EnemyAttackPreparationMode PreparationMode { get; }
        public EnemyAttackRuntimeConfig CurrentAttack { get; private set; }
        public float ReleaseDistance { get; private set; }

        /// <summary>创建已选定技能和释放距离的攻击计划。</summary>
        public EnemyAttackPlan(
            EnemyAttackPlanType type,
            EnemyAttackPreparationMode preparationMode,
            EnemyAttackRuntimeConfig attack,
            float releaseDistance)
        {
            Type = type;
            PreparationMode = preparationMode;
            CurrentAttack = attack;
            ReleaseDistance = releaseDistance;
        }

        /// <summary>切换到锁定连招的下一段技能。</summary>
        public void SetCurrentAttack(EnemyAttackRuntimeConfig attack)
        {
            CurrentAttack = attack;
            ReleaseDistance = attack.AttackRange;
        }
    }
}
```

- [ ] **Step 4: 将随机选择器泛化为运行时攻击条目**

`EnemyDecisionRandom` 提供：

```csharp
/// <summary>从已过滤的攻击候选中按正数权重选择一个条目。</summary>
public static EnemyAttackRuntimeConfig SelectAttack(
    IReadOnlyList<EnemyAttackRuntimeConfig> attacks,
    float roll)
```

删除只服务于 `EnemyBasicAttackConfig` 的旧重载，测试改为运行时攻击条目。

- [ ] **Step 5: 重构战斗决策器创建完整计划**

构造函数改为：

```csharp
/// <summary>使用战斗配置、决策配置和已解析攻击目录创建决策器。</summary>
public EnemyCombatDecisionController(
    EnemyCombatConfig combatConfig,
    EnemyDecisionProfile profile,
    EnemyAttackCatalog attackCatalog)
```

`TryCreateAttackPlan` 严格按以下顺序处理：

1. 低稳定值拒绝。
2. 已有计划直接返回成功，不重新抽取。
3. 追击范围外优先选择追击池，池为空时选择基础技能并普通接近。
4. 追击范围内执行全局冷却和 `attackDesire`。
5. 基础边界内从覆盖当前距离的基础技能中选取。
6. 基础边界外优先从覆盖当前距离的进身池选取，否则选择基础技能普通接近。

追击计划使用 `attackRange * 0.8f`，进身和当前距离内基础计划使用完整范围并直接释放。

- [ ] **Step 6: 编译并运行决策测试**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyAttackPlanDecisionEditModeTests"
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyCombatDecisionControllerEditModeTests"
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyDecisionRandomEditModeTests"
```

Expected: 编译 `errorCount: 0`，三组测试全部 PASS。

- [ ] **Step 7: 提交攻击计划决策**

```powershell
git add Assets/Game/Character/Enemy/AI/Combat Assets/Game/Character/Enemy/AI/EnemyDecisionRandom.cs Assets/Game/Editor/EnemyAttackPlanDecisionEditModeTests.cs Assets/Game/Editor/EnemyCombatDecisionControllerEditModeTests.cs Assets/Game/Editor/EnemyDecisionRandomEditModeTests.cs
git commit -m "功能：实现按距离选择攻击计划"
```

---

### Task 5: 将黑板、AI 控制器和移动准备切换到攻击计划

**Files:**
- Create: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyPrepareAttackPlanNodeAsset.cs`
- Modify: `Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs`
- Modify: `Assets/Game/Character/Enemy/AI/AIController.cs`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs`
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Utilities/EnemyBehaviorTreeUtility.cs`
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyGenerateAttackIntentNodeAsset.cs`
- Modify: `Assets/Game/Editor/EnemyAttackIntentNodeEditModeTests.cs`
- Modify: `Assets/Game/Editor/EnemyCombatLayerEditModeTests.cs`
- Modify: tests that call `AIController.StartAI`

- [ ] **Step 1: 改写黑板和生成节点测试**

测试断言：

- 黑板只保存 `DistanceToTarget`、`IsInCombatRange`、`IsInChaseRange` 和攻击计划镜像。
- 生成节点成功后存在完整计划和攻击意图。
- 再次 Tick 不重新选择已锁定计划。
- 追击准备在距离大于 `8` 时保持 `Running`，等于 `8` 时返回 `Success`。
- 直接计划在目标离开技能范围后取消并返回 `Failure`。

- [ ] **Step 2: 运行节点测试并确认旧距离事实不符合新设计**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyAttackIntentNodeEditModeTests"
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyCombatLayerEditModeTests"
```

Expected: FAIL，黑板仍依赖统一攻击距离且没有准备节点。

- [ ] **Step 3: 移除统一攻击范围运行时状态**

从 `EnemyCombatComponent` 删除 `attackRange`、`IsInAttackRange` 和 `ApplyConfig` 中的统一范围赋值。从 `EnemyBlackboard` 删除 `IsInAttackRange`，把距离事实入口改为：

```csharp
/// <summary>更新目标距离、战斗范围和追击范围事实。</summary>
public void SetTargetDistanceFacts(
    float distance,
    bool isInCombatRange,
    bool isInChaseRange)
{
    DistanceToTarget = distance;
    IsInCombatRange = isInCombatRange;
    IsInChaseRange = isInChaseRange;
}
```

- [ ] **Step 4: 在 AI 启动时构建攻击目录**

`AIController` 增加仅供 EditMode 测试注入的强类型解析器，并在 `StartAI` 中构建目录：

```csharp
#if UNITY_EDITOR
private System.Func<int, SkillConfig> skillResolverForTests;

/// <summary>设置 EditMode 测试使用的技能解析器。</summary>
public void SetSkillResolverForTests(System.Func<int, SkillConfig> resolver)
{
    skillResolverForTests = resolver;
}
#endif

/// <summary>从测试解析器或全局配置管理器读取敌人技能。</summary>
private SkillConfig ResolveEnemySkill(int skillId)
{
#if UNITY_EDITOR
    if (skillResolverForTests != null)
    {
        return skillResolverForTests(skillId);
    }
#endif
    return ConfigManager.Instance.GetSkillConfig(skillId);
}
```

所有调用 `StartAI` 的 EditMode 测试先注入与测试定义一致的技能配置，禁止在目录构建失败时生成默认范围。

- [ ] **Step 5: 实现攻击计划准备节点**

节点规则：

- 没有计划或目标时返回 `Failure`。
- 当前距离小于等于 `ReleaseDistance` 且目标在前方时停止移动、把阶段设为 `Start` 并返回 `Success`。
- `Direct` 计划不满足条件时清理计划并返回 `Failure`。
- `Approach` 播放普通移动动画并朝目标转向。
- `Pursuit` 播放跑步动画并朝目标转向。

公开资产类保持无序列化参数，移动模式完全来自当前计划。

- [ ] **Step 6: 改造攻击计划生成节点和同步工具**

`EnemyGenerateAttackIntentNodeAsset` 调用 `TryCreateAttackPlan`，传入黑板实时距离和 `Combat.ChaseRange`。`EnemyBehaviorTreeUtility.SyncCombatDecisionFacts` 同步当前技能编号、攻击计划类型和阶段；黑板不持有第二份可变计划对象。

同时在 `EnemyBehaviorTreeUtility` 增加 `IsTargetInFront(AIController controller)`，统一供准备节点、连招和反击检查目标朝向，删除 `EnemyAttackFlowNodeAsset` 内部的重复实现。

```csharp
/// <summary>判断当前战斗目标是否位于敌人前方半区。</summary>
public static bool IsTargetInFront(AIController controller)
{
    Vector3 direction = controller.Blackboard.CombatTarget.position - controller.transform.position;
    direction.y = 0f;
    if (direction.sqrMagnitude <= 0.0001f)
    {
        return true;
    }

    Vector3 forward = controller.transform.forward;
    forward.y = 0f;
    return Vector3.Dot(forward.normalized, direction.normalized) >= 0f;
}
```

- [ ] **Step 7: 编译并运行 AI/节点测试**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyAttackIntentNodeEditModeTests"
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyCombatLayerEditModeTests"
```

Expected: 编译 `errorCount: 0`，测试全部 PASS。

- [ ] **Step 8: 提交攻击计划准备流程**

```powershell
git add Assets/Game/Character/Enemy/Core/EnemyBlackboard.cs Assets/Game/Character/Enemy/AI/AIController.cs Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs Assets/Game/Character/Enemy/AI/BehaviorTree/Utilities/EnemyBehaviorTreeUtility.cs Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyGenerateAttackIntentNodeAsset.cs Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyPrepareAttackPlanNodeAsset.cs Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyPrepareAttackPlanNodeAsset.cs.meta
git add Assets/Game/Editor/EnemyAttackIntentNodeEditModeTests.cs Assets/Game/Editor/EnemyCombatLayerEditModeTests.cs Assets/Game/Editor/EnemyAlertChaseEditModeTests.cs Assets/Game/Editor/EnemyCombatReactionEditModeTests.cs Assets/Game/Editor/EnemyTargetMemoryRuntimeEditModeTests.cs Assets/Game/Editor/EnemyAlertRoutineEditModeTests.cs Assets/Game/Editor/EnemyCombatIdleEditModeTests.cs Assets/Game/Editor/EnemyNormalRoutineEditModeTests.cs Assets/Game/Editor/EnemyCombatActionFlowEditModeTests.cs
git commit -m "重构：使用攻击计划驱动敌人接近"
```

提交前排除尚未迁移的行为树 `.asset` 和用户的 `EnemyAgent.cs`。

---

### Task 6: 让攻击执行与连招使用逐技能范围

**Files:**
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyAttackFlowNodeAsset.cs`
- Modify: `Assets/Game/Character/Enemy/AI/Combat/EnemyCombatDecisionController.cs`
- Modify: `Assets/Game/Character/Enemy/AI/Combat/EnemyAttackPlan.cs`
- Modify: `Assets/Game/Editor/EnemyCombatActionFlowEditModeTests.cs`
- Modify: `Assets/Game/Editor/EnemyCombatDecisionControllerEditModeTests.cs`

- [ ] **Step 1: 编写逐段范围和朝向失败测试**

```csharp
/// <summary>验证下一段技能越界时结束整套连招。</summary>
[Test]
public void TryAdvanceCombo_NextSkillOutOfRange_EndsCombo()
{
    EnemyCombatDecisionController controller = CreateComboController();
    CreateInitialPlan(controller, startSkillId: 20302);

    bool advanced = controller.TryAdvanceCombo(
        randomValue: 0f,
        distanceToTarget: 4.1f,
        isTargetInFront: true);

    Assert.IsFalse(advanced);
    Assert.IsNull(controller.CurrentPlan);
}
```

增加距离 `4f` 成功、目标不在前方失败、路线首次选中后不重新抽取三项测试。

- [ ] **Step 2: 运行战斗流程测试并确认仍使用统一布尔范围**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyCombatActionFlowEditModeTests"
```

Expected: FAIL，旧 `TryAdvanceCombo` 只接收 `isInAttackRange`。

- [ ] **Step 3: 把连招路线和索引移入攻击计划**

`EnemyAttackPlan` 保存锁定后的 `EnemyAttackRuntimeConfig[]` 和下一段索引。决策器首次衔接时根据 `comboBranches` 选择一条路线并通过 `EnemyAttackCatalog.GetRequiredBasicAttack` 解析全部技能；之后只沿已锁定数组前进。

- [ ] **Step 4: 精简攻击执行节点**

`EnemyAttackFlowNodeAsset` 的 `TickStart` 只读取 `CurrentPlan.CurrentAttack`：

```csharp
EnemyAttackRuntimeConfig attack = decision.CurrentPlan.CurrentAttack;
if (!controller.Context.Combat.TryStartAttack(attack.SkillId)
    || controller.Context.Animation == null
    || !controller.Context.Animation.TryPlay(attack.AnimationName, interruptCurrentAction: false))
{
    controller.Context.Combat.InterruptAction();
    decision.ResetAttack();
    EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(controller);
    return BehaviorTreeStatus.Failure;
}
```

动画结束后调用的新签名为：

```csharp
decision.TryAdvanceCombo(
    Random.value,
    controller.Blackboard.DistanceToTarget,
    EnemyBehaviorTreeUtility.IsTargetInFront(controller));
```

失败时完成并清理整套计划，不执行追近续接。

- [ ] **Step 5: 编译并运行连招测试**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyCombatActionFlowEditModeTests"
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyCombatDecisionControllerEditModeTests"
```

Expected: 编译 `errorCount: 0`，两组测试全部 PASS。

- [ ] **Step 6: 提交逐技能连招执行**

```powershell
git add Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyAttackFlowNodeAsset.cs Assets/Game/Character/Enemy/AI/Combat/EnemyCombatDecisionController.cs Assets/Game/Character/Enemy/AI/Combat/EnemyAttackPlan.cs Assets/Game/Editor/EnemyCombatActionFlowEditModeTests.cs Assets/Game/Editor/EnemyCombatDecisionControllerEditModeTests.cs
git commit -m "功能：按技能范围衔接敌人连招"
```

---

### Task 7: 实现实际格挡计数与防御反击

**Files:**
- Modify: `Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs`
- Modify: `Assets/Game/Character/Enemy/AI/Combat/EnemyCombatDecisionController.cs`
- Modify: `Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyDefenseNodeAsset.cs`
- Create: `Assets/Game/Editor/EnemyCounterAttackEditModeTests.cs`
- Modify: `Assets/Game/Editor/EnemyCombatReactionEditModeTests.cs`
- Modify: `Assets/Game/Editor/EnemyCombatActionFlowEditModeTests.cs`

- [ ] **Step 1: 编写格挡次数、动画等待和取消测试**

覆盖以下状态序列：

```text
StartDefense
第一次 RequestDefenseHitReaction -> 消费 1 次 -> 无待反击
第二次 RequestDefenseHitReaction -> 消费 1 次 -> 有待反击
防御受击动画仍在播放 -> 不生成攻击计划
动画结束且距离 4、目标在前 -> 生成 20309 反击计划
动画结束但距离 4.1 -> 清除待反击并结束防御
```

代表性计数测试：

```csharp
/// <summary>验证多个实际格挡不会被布尔标记合并丢失。</summary>
[Test]
public void DefenseHits_TwoRequests_AreConsumedSeparately()
{
    EnemyCombatComponent combat = CreateCombatComponent();
    combat.StartDefense();
    combat.RequestDefenseHitReaction();
    combat.RequestDefenseHitReaction();

    Assert.IsTrue(combat.ConsumeDefenseHitReaction());
    Assert.IsTrue(combat.ConsumeDefenseHitReaction());
    Assert.IsFalse(combat.ConsumeDefenseHitReaction());
}
```

- [ ] **Step 2: 运行反击测试并确认第二次格挡被合并**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyCounterAttackEditModeTests"
```

Expected: FAIL，当前组件只保存一个布尔防御受击标记。

- [ ] **Step 3: 把防御受击标记改为计数**

`EnemyCombatComponent` 使用 `int pendingDefenseHitCount`。`RequestDefenseHitReaction` 在防御状态下递增，`ConsumeDefenseHitReaction` 每次递减一个。`StartDefense` 和 `StopDefense` 都清零，避免跨防御周期残留。

- [ ] **Step 4: 在决策器维护本次防御的反击状态**

增加以下接口：

```csharp
public bool HasPendingCounter { get; private set; }

/// <summary>记录一次实际格挡，并在达到配置阈值时标记待反击。</summary>
public void RecordDefenseBlock()

/// <summary>在距离和朝向有效时把待反击转换为固定攻击计划。</summary>
public bool TryCreateCounterPlan(float distanceToTarget, bool isTargetInFront)

/// <summary>结束本次防御并清空格挡次数和待反击标记。</summary>
public void ResetDefense()
```

`TryCreateCounterPlan` 先检查反击技能、`distance <= AttackRange` 和朝向，再创建 `Direct` 类型反击计划。它不进行随机判定。

- [ ] **Step 5: 调整防御节点状态转换**

`EnemyDefenseNodeAsset` 每消费一次格挡先调用 `RecordDefenseBlock`，再播放或重启防御受击动画。动画结束后先检查 `HasPendingCounter`：

1. 读取实时距离和朝向。
2. 调用 `Combat.StopDefense()`。
3. 成功生成反击计划时返回 `Success`，让下一帧攻击计划分支接管。
4. 条件失败时调用 `ResetDefense()` 并正常结束。

死亡、失衡、层退出和节点重置统一调用 `StopDefense` 与 `ResetDefense`。

- [ ] **Step 6: 编译并运行反击与旧反应测试**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyCounterAttackEditModeTests"
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyCombatReactionEditModeTests"
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.EnemyCombatActionFlowEditModeTests"
```

Expected: 编译 `errorCount: 0`，三组测试全部 PASS。

- [ ] **Step 7: 提交防御反击**

```powershell
git add Assets/Game/Character/Enemy/Components/EnemyCombatComponent.cs Assets/Game/Character/Enemy/AI/Combat/EnemyCombatDecisionController.cs Assets/Game/Character/Enemy/AI/BehaviorTree/Actions/EnemyDefenseNodeAsset.cs Assets/Game/Editor/EnemyCounterAttackEditModeTests.cs Assets/Game/Editor/EnemyCombatReactionEditModeTests.cs Assets/Game/Editor/EnemyCombatActionFlowEditModeTests.cs
git commit -m "功能：实现防御格挡计数与反击计划"
```

---

### Task 8: 迁移普通敌人和通用行为树

**Files:**
- Modify: `Assets/Game/Character/Enemy/Config/Definitions/GreatSwordEnemyDefinition.asset`
- Modify: `Assets/Game/Character/Enemy/Config/Definitions/SpearEenemyDefinition.asset`
- Modify: `Assets/Game/Character/Enemy/Config/Definitions/SwordAndShieldEnemyDefinition.asset`
- Modify: `Assets/Game/Character/Enemy/Config/BehaviorTrees/Common/`
- Modify: `Assets/Game/Editor/GuardMeleeBehaviorTreeAssetEditModeTests.cs`
- Modify: `Assets/Game/Editor/SwordAndShieldBehaviorTreeAssetEditModeTests.cs`
- Modify: `Assets/Game/Editor/EnemyCombatLayerEditModeTests.cs`
- Delete: old unified attack-range condition scripts and assets listed above

- [ ] **Step 1: 把资产结构测试改为攻击计划树**

通用战斗选择器最终顺序固定为：

```text
TurnToTargetSequence
DodgeDecisionSequence
DefenseDecisionSequence
AttackSequence
CombatHoldSequence
```

`AttackSequence` 固定为：

```text
HasAttackIntent
PrepareAttackPlan
AttackFlow
```

测试同时断言 `ChaseSequence`、`InAttackRangeSequence`、`IsInAttackRange` 和 `IsOutsideAttackRange` 资产不存在。

- [ ] **Step 2: 运行行为树资产测试并确认旧结构失败**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.GuardMeleeBehaviorTreeAssetEditModeTests"
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.SwordAndShieldBehaviorTreeAssetEditModeTests"
```

Expected: FAIL，通用树仍包含统一距离分支和旧移动分支。

- [ ] **Step 3: 迁移三个普通敌人定义**

对每个定义执行：

- 将 `basicAttacks` 的序列化类型迁移为 `EnemyAttackConfig`，保留技能编号、动画名和权重。
- 写入空 `approachAttacks`、空 `pursuitAttacks`、空 `counterAttack`。
- 写入 `counterBlockThreshold: 2`，但无反击技能时不参与决策。
- 删除 `specialSkillIds` 和 `defaultAttackRange`。
- 删除 `decisionProfile.counterDesire`。

普通敌人范围已在 Task 1 按旧值写入全局技能配置，因此基础近战边界保持不变。

- [ ] **Step 4: 使用 Unity Editor API 重建通用战斗树**

在 `EnemyBehaviorTreeAssetBuilder` 中实现 `RebuildCommonCombatTree()`，通过 `AssetDatabase.LoadAssetAtPath` 读取通用叶节点，通过 `SetChildren`、`SetChild` 和 `BehaviorTreeAsset.SetRoot` 更新组合节点。创建 `PrepareAttackPlan.asset`，删除四个旧距离资产后运行 `AssetDatabase.SaveAssets()` 与 `AssetDatabase.Refresh()`。

调用命令：

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" menu_item --menuPath "Tools/Enemy AI/Rebuild Common Combat Tree"
```

Expected: 命令成功，通用战斗树变为五分支攻击计划结构。

- [ ] **Step 5: 删除旧距离脚本并确认无引用**

删除两个统一攻击范围条件脚本和 `.meta`。使用 codedb 引用查询确认 C# 无调用方，再运行 Unity 编译确认资产脚本引用已全部迁移。

- [ ] **Step 6: 编译并运行普通敌人回归测试**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode
```

Expected: 编译 `errorCount: 0`，全部 EditMode 测试 PASS。

- [ ] **Step 7: 提交通用行为树迁移**

```powershell
git add Assets/Game/Character/Enemy/Config/Definitions/GreatSwordEnemyDefinition.asset Assets/Game/Character/Enemy/Config/Definitions/SpearEenemyDefinition.asset Assets/Game/Character/Enemy/Config/Definitions/SwordAndShieldEnemyDefinition.asset Assets/Game/Character/Enemy/Config/BehaviorTrees/Common Assets/Game/Character/Enemy/AI/BehaviorTree/Conditions Assets/Game/Editor/EnemyBehaviorTreeAssetBuilder.cs Assets/Game/Editor/GuardMeleeBehaviorTreeAssetEditModeTests.cs Assets/Game/Editor/SwordAndShieldBehaviorTreeAssetEditModeTests.cs Assets/Game/Editor/EnemyCombatLayerEditModeTests.cs
git commit -m "重构：迁移普通敌人到攻击计划行为树"
```

---

### Task 9: 配置剑盾精英并构建独立行为树

**Files:**
- Modify: `Assets/Game/Character/Enemy/Config/Definitions/SwordAndShieldEliteEnemyDefinition.asset`
- Modify: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEliteEnemy/SwordAndShieldEliteEnemyBehaviorTree.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEliteEnemy/RootLayerSelector.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEliteEnemy/Combat/CombatLayer.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEliteEnemy/Combat/RepeatCombatPlan.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEliteEnemy/Combat/CombatPlanSelector.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEliteEnemy/Combat/TurnToTargetSequence.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEliteEnemy/Combat/DodgeDecisionSequence.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEliteEnemy/Combat/DefenseDecisionSequence.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEliteEnemy/Combat/AttackSequence.asset`
- Create: `Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEliteEnemy/Combat/CombatHoldSequence.asset`
- Create: `Assets/Game/Editor/SwordAndShieldEliteBehaviorTreeAssetEditModeTests.cs`
- Modify: `Assets/Game/Editor/EnemyBehaviorTreeAssetBuilder.cs`

- [ ] **Step 1: 编写精英配置和资产独立性测试**

测试断言：

- `enemyId` 为 `SwordAndShieldElite`，不再与小兵重复。
- 基础池包含 `20301`～`20306`。
- 进身池只有 `20307`，追击池只有 `20308`，反击为 `20309`，格挡阈值为 `2`。
- 连招为 `20302 -> 20303 -> 20304 -> 20305 -> 20306`，概率为 `1`。
- 精英 `BehaviorTreeAsset.Root` 位于精英目录，不是 `Common/Root/RootLayerSelector.asset`。
- 精英根节点引用独立 `CombatLayer`，警戒层和普通层允许引用通用只读子树。
- 精英战斗组合节点全部位于精英目录，叶节点可以引用通用动作和条件资产。

- [ ] **Step 2: 运行精英资产测试并确认仍复用通用根节点**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.SwordAndShieldEliteBehaviorTreeAssetEditModeTests"
```

Expected: FAIL，当前精英树的根 GUID 指向 `Common/Root/RootLayerSelector.asset`。

- [ ] **Step 3: 写入精英攻击配置**

精英定义使用以下确定配置：

| 池 | 技能 | 临时动画 | 权重 |
|---|---:|---|---:|
| 基础 | `20301` 二连斩 | `Attack1` | `1` |
| 基础/组合起手 | `20302` 上挑 | `Attack2` | `0.7` |
| 基础/组合段 | `20303` 左劈 | `Attack3` | `0.8` |
| 基础/组合段 | `20304` 盾击 | `Attack4` | `0.7` |
| 基础/组合段 | `20305` 跃击 | `Attack5` | `0.5` |
| 基础/组合段 | `20306` 后撤 | `Retreat` | `0.4` |
| 进身 | `20307` 突刺 | `Attack5` | `1` |
| 追击 | `20308` 快速飞跃 | `Attack4` | `1` |
| 反击 | `20309` 反击 | `Attack3` | `1` |

这些动画名明确是当前可运行映射。最终动画到位后只修改本定义中的 `animationName` 和 Animator 状态，不改变技能编号、攻击计划代码或行为树结构。

同时把 `enemyId`、`displayName` 改为 `SwordAndShieldElite`，并通过 `SerializedObject` 将 `behaviorTreeAsset` 重新指向当前精英目录下的 `SwordAndShieldEliteEnemyBehaviorTree.asset`，修复现有定义与行为树 `.meta` GUID 不一致的问题。

- [ ] **Step 4: 用资产构建器创建精英独立组合节点**

`RebuildSwordAndShieldEliteTree()` 创建精英根节点和九个独立组合节点，结构为：

```text
RootLayerSelector
  InterruptExecutor                         Common leaf
  CombatLayer                              Elite composite
    HasCombatTarget                        Common leaf
    EnsureCombatStance                     Common leaf
    RepeatCombatPlan                       Elite decorator
      CombatPlanSelector                   Elite composite
        TurnToTargetSequence               Elite composite
        DodgeDecisionSequence              Elite composite
        DefenseDecisionSequence            Elite composite
        AttackSequence                     Elite composite
        CombatHoldSequence                 Elite composite
  AlertLayer                               Common subtree
  NormalLayer                              Common subtree
```

`AttackSequence` 使用通用 `HasAttackIntent`、`PrepareAttackPlan`、`AttackFlow` 叶节点。构建器更新现有 `SwordAndShieldEliteEnemyBehaviorTree.asset` 的根引用，不删除用户已创建的行为树主资产和 `.meta`。

调用命令：

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" menu_item --menuPath "Tools/Enemy AI/Rebuild Sword And Shield Elite Tree"
```

Expected: 命令成功，精英目录新增独立根节点与战斗组合节点。

- [ ] **Step 5: 编译并运行精英资产测试**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.SwordAndShieldEliteBehaviorTreeAssetEditModeTests"
```

Expected: 编译 `errorCount: 0`，测试全部 PASS。

- [ ] **Step 6: 提交精英定义和独立行为树**

```powershell
git add Assets/Game/Character/Enemy/Config/Definitions/SwordAndShieldEliteEnemyDefinition.asset Assets/Game/Character/Enemy/Config/Definitions/SwordAndShieldEliteEnemyDefinition.asset.meta Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEliteEnemy Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEliteEnemy.meta Assets/Game/Editor/EnemyBehaviorTreeAssetBuilder.cs Assets/Game/Editor/SwordAndShieldEliteBehaviorTreeAssetEditModeTests.cs Assets/Game/Editor/SwordAndShieldEliteBehaviorTreeAssetEditModeTests.cs.meta
git commit -m "功能：构建剑盾精英独立行为树"
```

---

### Task 10: 验证二连斩命中窗口与完整战斗流程

**Files:**
- Modify: `Assets/Game/Editor/SwordAndShieldAttackAssetEditModeTests.cs`
- Create: `Assets/Game/Editor/SwordAndShieldEliteCombatIntegrationEditModeTests.cs`
- Modify: `Assets/DoubleL/FBX_Animations/One Hand Base/Fatal/Attack/1Hand_Base_Fatal_Attack_1.fbx.meta` only when the imported `Attack1` clip lacks two complete hit windows

- [ ] **Step 1: 增加二连斩动画事件测试**

测试加载精英定义中 `20301` 对应的 `Attack1` 状态，获取实际 AnimationClip，并断言事件顺序为：

```text
EnableWeaponHit
DisableWeaponHit
EnableWeaponHit
DisableWeaponHit
```

允许同一动画包含其他事件，但四个武器碰撞事件必须保持该相对顺序。

- [ ] **Step 2: 运行动画事件测试确认资产现状**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.SwordAndShieldAttackAssetEditModeTests"
```

Expected: 当前 `Attack1` 有两组窗口时 PASS；缺少第二组时 FAIL，并显示实际事件序列。

- [ ] **Step 3: 缺少第二组时通过 Unity AnimationUtility 写入确定事件**

读取 `Attack1` 的 `clip.length`，保留已有第一组窗口，并写入以下归一化时间：

| 事件 | 归一化时间 |
|---|---:|
| 第一次 `EnableWeaponHit` | `0.22` |
| 第一次 `DisableWeaponHit` | `0.38` |
| 第二次 `EnableWeaponHit` | `0.58` |
| 第二次 `DisableWeaponHit` | `0.76` |

实际事件时间使用 `normalizedTime * clip.length`。通过 Unity Editor API 修改导入器事件并重新导入，不直接手工改写 FBX `.meta` 文本。

- [ ] **Step 4: 增加 EditMode 集成流程测试**

`SwordAndShieldEliteCombatIntegrationEditModeTests` 使用内存攻击目录、固定随机值、真实决策器和行为树运行时节点，验证：

- 距离 `5` 时选择 `20307` 突刺。
- 距离 `9` 时锁定 `20308`，距离大于 `8` 时准备节点保持 `Running`，距离等于 `8` 时允许攻击。
- 第二次实际格挡后等待防御受击动画结束，再生成 `20309`。
- 连招在距离 `4` 内按五段推进，在距离 `4.1` 时终止。
- 二连斩连续两次打开和关闭命中窗口时，`CombatAbilitySystem.CurrentSkill` 保持为 `20301`，直至动画流程结束。

- [ ] **Step 5: 运行集成测试**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.SwordAndShieldEliteCombatIntegrationEditModeTests"
```

Expected: 编译 `errorCount: 0`，测试全部 PASS。

- [ ] **Step 6: 在 Scene1 执行非持久化 PlayMode 验证**

1. 使用 `scene load --scenePath "Assets/Scenes/Scene1.unity"` 加载场景。
2. 在未保存场景的前提下复制一个现有剑盾敌人实例，并把 `EnemyAgent.definition` 指向 `SwordAndShieldEliteEnemyDefinition.asset`。
3. 进入 Play Mode，分别把玩家放在距离 `3`、`5`、`9`，观察基础、突刺和追击计划。
4. 连续攻击正在防御的精英两次，确认第二次防御受击动画结束后才反击。
5. 退出 Play Mode 后重新加载 `Scene1.unity`，不保存临时对象变更。
6. 运行 `get_logs --count 200 --logType Error`，确认没有行为树丢失引用、技能解析或动画状态错误。

- [ ] **Step 7: 提交动画事件和集成覆盖**

```powershell
git add Assets/Game/Editor/SwordAndShieldAttackAssetEditModeTests.cs Assets/Game/Editor/SwordAndShieldEliteCombatIntegrationEditModeTests.cs Assets/Game/Editor/SwordAndShieldEliteCombatIntegrationEditModeTests.cs.meta
git add "Assets/DoubleL/FBX_Animations/One Hand Base/Fatal/Attack/1Hand_Base_Fatal_Attack_1.fbx.meta"
git commit -m "测试：覆盖剑盾精英完整攻击流程"
```

若 FBX 导入设置未发生变化，则不执行第二条 `git add`。

---

### Task 11: 全量验证与清理

**Files:**
- Verify: all files changed by Tasks 1-10

- [ ] **Step 1: 检查废弃符号和旧字段无引用**

使用 codedb 精确查询确认以下名称无生产代码引用：

```text
EnemyBasicAttackConfig
defaultAttackRange
specialSkillIds
counterDesire
EnemyIsInAttackRangeNodeAsset
EnemyIsOutsideAttackRangeNodeAsset
SelectInitialAttack
```

玩家 `PlayerController.defaultAttackRange` 不属于敌人统一范围字段，必须保留。

- [ ] **Step 2: 运行 Unity 编译**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" compile unity
```

Expected: `success: true`、`status: success`、`errorCount: 0`。

- [ ] **Step 3: 运行全部 EditMode 测试**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --mode EditMode
```

Expected: 全部 PASS，失败数为 `0`。

- [ ] **Step 4: 运行剑盾精英集成测试**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" test run --test-name "Game.Character.Enemy.Tests.SwordAndShieldEliteCombatIntegrationEditModeTests"
```

Expected: 全部 PASS。

- [ ] **Step 5: 检查 Unity Console**

Run:

```powershell
& "./.aibridge/cli/AIBridgeCLI.exe" get_logs --count 200 --logType Error
```

Expected: 没有由敌人配置、行为树丢失引用、动画状态或技能解析产生的新错误。

- [ ] **Step 6: 核对工作区与提交历史**

```powershell
git status --short
git log --oneline -12
```

Expected: 用户原有改动得到保留；本计划产生的文件均已按任务提交；没有生成文件、临时脚本或无引用资产遗留。

---

## 完成判定

- 精英技能编号为 `20301`～`20309`。
- `20301`～`20306`、`20309` 范围为 `4`，`20307` 范围为 `6`，`20308` 范围为 `10`。
- 追击范围为 `8` 时，`20308` 在距离 `8` 释放。
- 突刺只在基础边界 `4` 外、距离 `6` 内进入候选。
- 第 2 次实际格挡对应的防御受击动画结束后才尝试反击。
- 剑盾精英使用独立根节点和战斗组合节点。
- 普通敌人不再依赖敌人统一 `defaultAttackRange`，原基础攻击距离保持不变。
- 所有 Unity 编译、EditMode 测试和 Scene1 定向 PlayMode 验证通过。
