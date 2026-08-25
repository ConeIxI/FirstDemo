# 战斗特效管理框架 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 建立由技能配置驱动的攻击动作、命中和受伤特效框架，统一处理动画事件、挂点、对象池、并发和回收。

**Architecture:** 公共 `CombatEffectConfig` 与技能 `SkillEffectBinding` 分离；`CombatEffectService` 是活动实例和生命周期的唯一管理者，内部独占按 Prefab 路径分组的对象池。`CharacterEffectController` 把角色动画事件转换为播放请求，`CombatEffectExecutor` 把 `CombatEvent` 转换为同一种播放请求。

**Tech Stack:** Unity 2022.3.61f1c1、C# 9.0、Newtonsoft.Json、Unity Addressables、ParticleSystem、项目现有 `SingletonManager` / `ConfigManager` / `ResourceManager`

---

## 文件结构

新增文件：

- `Assets/Game/Battle/Skill/Common/CombatEffectConfig.cs`：公共配置、技能绑定、挂载覆盖和枚举。
- `Assets/Game/Battle/Skill/Effects/CombatEffectRequest.cs`：未解析请求与解析后的播放参数。
- `Assets/Game/Battle/Skill/Effects/CombatEffectHandle.cs`：单个活动实例的运行时状态和回收判定。
- `Assets/Game/Battle/Skill/Effects/CombatEffectPool.cs`：单个 Prefab 路径的实例池。
- `Assets/Game/Battle/Skill/Effects/CombatEffectService.cs`：播放、挂点、并发、计时和回收的唯一入口。
- `Assets/Game/Battle/Skill/Effects/CharacterEffectController.cs`：角色当前技能与动画事件适配。
- `Assets/Data/CombatEffectConfig.json`：公共特效定义表；首版为空数组，等待具体特效资源配置。

修改文件：

- `Assets/Game/Battle/Skill/Common/SkillConfig.cs`：用四组 `SkillEffectBinding[]` 替换旧特效字段和类型。
- `Assets/Framework/Manager/ConfigManager.cs`：加载、校验和查询公共特效配置。
- `Assets/Game/Battle/Skill/Effects/CombatEffectExecutor.cs`：从直接实例化改为战斗事件适配。
- `Assets/Game/Battle/Ability/CombatAbilitySystem.cs`：持有角色特效控制器并清理角色所属实例。
- `Assets/Game/Character/CharacterStateMachine.cs`：提供玩家动画事件入口。
- `Assets/Game/Character/Enemy/Components/EnemyAnimationComponent.cs`：提供敌人动画事件入口。
- `Assets/Data/EnemySkillConfig.json`：将 `onCastEffects` 重命名为 `attackEffects`。
- `Assets/Data/WeaponConfig/SingleSwordSkillConfig.json`：将 `onCastEffects` 重命名为 `attackEffects`。
- `Assets/Data/WeaponConfig/SpearSkillConfig.json`：将 `onCastEffects` 重命名为 `attackEffects`。

约束：不创建测试文件，不修改 `.controller`，不运行 Play Mode。所有新增和修改的函数都添加简体中文用途注释。

### Task 1: 建立配置模型与加载入口

**Files:**
- Create: `Assets/Game/Battle/Skill/Common/CombatEffectConfig.cs`
- Create: `Assets/Data/CombatEffectConfig.json`
- Modify: `Assets/Game/Battle/Skill/Common/SkillConfig.cs:9-75`
- Modify: `Assets/Game/Battle/Skill/Common/SkillConfig.cs:155-175`
- Modify: `Assets/Framework/Manager/ConfigManager.cs:15-40`
- Modify: `Assets/Framework/Manager/ConfigManager.cs:87-131`
- Modify: `Assets/Framework/Manager/ConfigManager.cs:234-263`

- [ ] **Step 1: 新增公共配置与技能绑定强类型**

创建 `CombatEffectConfig.cs`，类型和字段固定如下：

```csharp
using System;
using Game.Common;

namespace Game.Battle.Skill.Common
{
    public enum CombatEffectAttachment
    {
        WorldHitPoint,
        SourceSocket,
        TargetSocket
    }

    public enum CombatEffectOrientation
    {
        ConfigRotation,
        SourceForward,
        HitDirection
    }

    public enum CombatEffectRecycleMode
    {
        ParticleComplete,
        FixedDuration,
        Manual
    }

    public enum CombatEffectConcurrency
    {
        AllowMultiple,
        UniqueChannel
    }

    [Serializable]
    public sealed class CombatEffectConfig : IConfig
    {
        public int effectId;
        public string path;
        public CombatEffectAttachment attachment;
        public string socketName;
        public bool follow;
        public Vec3 position;
        public Vec3 rotation;
        public Vec3 scale;
        public CombatEffectOrientation orientation;
        public CombatEffectRecycleMode recycleMode;
        public float duration;
        public CombatEffectConcurrency concurrency;
        public string channel;
    }

    [Serializable]
    public sealed class SkillEffectBinding
    {
        public string triggerKey;
        public int effectId;
        public CombatEffectAttachmentOverride attachmentOverride;
        public CombatEffectTransformOverride transformOverride;
    }

    [Serializable]
    public sealed class CombatEffectAttachmentOverride
    {
        public CombatEffectAttachment attachment;
        public string socketName;
        public bool follow;
        public CombatEffectOrientation orientation;
    }

    [Serializable]
    public sealed class CombatEffectTransformOverride
    {
        public Vec3 position;
        public Vec3 rotation;
        public Vec3 scale;
    }
}
```

- [ ] **Step 2: 将技能配置切换到四组绑定数组**

在 `SkillConfig` 中替换旧四个字段：

```csharp
public SkillEffectBinding[] attackEffects;
public SkillEffectBinding[] onHitEffects;
public SkillEffectBinding[] onBlockEffects;
public SkillEffectBinding[] onParryEffects;
```

删除 `SkillEffectData` 和 `SkillEffectTrigger`。将 `EnsureEffectArrays` 改为初始化上述四组数组，函数保留简体中文注释：

```csharp
/// <summary>确保技能的四类特效绑定数组始终可枚举。</summary>
private static void EnsureEffectArrays(SkillConfig config)
{
    config.attackEffects = config.attackEffects ?? new SkillEffectBinding[0];
    config.onHitEffects = config.onHitEffects ?? new SkillEffectBinding[0];
    config.onBlockEffects = config.onBlockEffects ?? new SkillEffectBinding[0];
    config.onParryEffects = config.onParryEffects ?? new SkillEffectBinding[0];
}
```

- [ ] **Step 3: 在 ConfigManager 中先加载公共特效表**

新增字典和加载调用；公共表必须早于技能表加载，以便技能引用立即校验：

```csharp
private readonly Dictionary<int, CombatEffectConfig> m_combatEffectConfigs =
    new Dictionary<int, CombatEffectConfig>();

protected override void Awake()
{
    base.Awake();
    if (!IsSingletonInstance)
    {
        return;
    }

    _LoadCombatEffectConfigs();
    _LoadPlayerSkillConfigs();
    _LoadSkillConfigs();
    _LoadBuffConfigs();
    _LoadItemConfigs();
}

/// <summary>加载并校验公共战斗特效配置。</summary>
private void _LoadCombatEffectConfigs()
{
    TextAsset asset = ResourceManager.Instance.LoadAsset<TextAsset>("Data/CombatEffectConfig.json");
    if (asset == null)
    {
        throw new Exception("未找到战斗特效配置：Data/CombatEffectConfig.json");
    }

    CombatEffectConfig[] configs = JsonConvert.DeserializeObject<CombatEffectConfig[]>(asset.text);
    if (configs == null)
    {
        throw new Exception("战斗特效配置解析失败：Data/CombatEffectConfig.json");
    }

    m_combatEffectConfigs.Clear();
    for (int i = 0; i < configs.Length; i++)
    {
        ValidateCombatEffectConfig(configs[i]);
        if (m_combatEffectConfigs.ContainsKey(configs[i].effectId))
        {
            throw new Exception($"战斗特效配置存在重复Id：{configs[i].effectId}");
        }

        m_combatEffectConfigs.Add(configs[i].effectId, configs[i]);
    }
}
```

- [ ] **Step 4: 增加公共配置和技能绑定校验**

`ValidateCombatEffectConfig` 明确校验：`effectId > 0`、路径非空、缩放三个分量均大于零、固定时长必须为正数、手动回收和唯一通道必须有 `channel`、角色挂点必须有 `socketName`。

增加以下查询和技能引用校验接口：

```csharp
/// <summary>尝试按 Id 获取公共战斗特效配置。</summary>
public bool TryGetCombatEffectConfig(int effectId, out CombatEffectConfig config)
{
    return m_combatEffectConfigs.TryGetValue(effectId, out config);
}

/// <summary>校验技能的所有特效绑定均引用已加载的公共配置。</summary>
private void ValidateSkillEffectBindings(SkillConfig config)
{
    ValidateSkillEffectBindings(config.skillId, config.attackEffects, true);
    ValidateSkillEffectBindings(config.skillId, config.onHitEffects, false);
    ValidateSkillEffectBindings(config.skillId, config.onBlockEffects, false);
    ValidateSkillEffectBindings(config.skillId, config.onParryEffects, false);
}
```

数组重载逐项检查绑定非空、`effectId` 存在；攻击绑定要求 `triggerKey` 非空，并禁止最终挂载为 `TargetSocket` 或朝向为 `HitDirection`。在 `_LoadSkillConfigs` 和 `_LoadPlayerSkillConfigs` 中，紧接 `ValidateSkillConfig(config)` 调用 `ValidateSkillEffectBindings(config)`。

- [ ] **Step 5: 创建空公共配置文件并验证 Unity 编译**

`Assets/Data/CombatEffectConfig.json` 初始内容固定为：

```json
[]
```

运行：`$CLI compile unity`

预期：Unity 编译成功，无 C# 错误。此阶段不进入 Play Mode。

- [ ] **Step 6: 提交配置模型**

```powershell
git add -- Assets/Game/Battle/Skill/Common/CombatEffectConfig.cs Assets/Game/Battle/Skill/Common/SkillConfig.cs Assets/Framework/Manager/ConfigManager.cs Assets/Data/CombatEffectConfig.json
git commit -m "实现战斗特效配置模型"
```

### Task 2: 建立播放请求、活动句柄与对象池

**Files:**
- Create: `Assets/Game/Battle/Skill/Effects/CombatEffectRequest.cs`
- Create: `Assets/Game/Battle/Skill/Effects/CombatEffectHandle.cs`
- Create: `Assets/Game/Battle/Skill/Effects/CombatEffectPool.cs`

- [ ] **Step 1: 定义未解析请求和解析结果**

`CombatEffectRequest` 使用只读属性，构造参数固定为绑定、技能 ID、攻击者、可空受击者、命中点和命中方向。`ResolvedCombatEffect` 为 `internal sealed`，保存合并后的 path、attachment、socketName、follow、Transform、orientation、recycleMode、duration、concurrency、channel 以及 owner。

```csharp
public sealed class CombatEffectRequest
{
    public SkillEffectBinding Binding { get; }
    public int SkillId { get; }
    public CombatAbilitySystem Source { get; }
    public CombatAbilitySystem Target { get; }
    public Vector3 HitPoint { get; }
    public Vector3 HitDirection { get; }

    /// <summary>创建一次尚未合并公共配置的特效播放请求。</summary>
    public CombatEffectRequest(
        SkillEffectBinding binding,
        int skillId,
        CombatAbilitySystem source,
        CombatAbilitySystem target,
        Vector3 hitPoint,
        Vector3 hitDirection)
    {
        Binding = binding;
        SkillId = skillId;
        Source = source;
        Target = target;
        HitPoint = hitPoint;
        HitDirection = hitDirection;
    }
}
```

- [ ] **Step 2: 定义活动句柄的回收判定**

`CombatEffectHandle` 声明为 `public sealed`，只公开只读的 `Instance`，以便 `CombatEffectService.Play`
公开返回句柄；构造函数、所属池和回收操作保持 `internal`。句柄保存实例、所属池、所有者、是否
跟随所有者、回收策略、剩余时间、通道和所有子粒子系统，只允许服务创建和标记回收。

```csharp
/// <summary>推进活动实例计时，并判断是否已满足自动回收条件。</summary>
internal bool ShouldRecycle(float deltaTime)
{
    if (RecycleMode == CombatEffectRecycleMode.Manual)
    {
        return false;
    }

    if (RecycleMode == CombatEffectRecycleMode.FixedDuration)
    {
        RemainingDuration -= deltaTime;
        return RemainingDuration <= 0f;
    }

    for (int i = 0; i < ParticleSystems.Length; i++)
    {
        if (ParticleSystems[i].IsAlive(false))
        {
            return false;
        }
    }

    return true;
}
```

- [ ] **Step 3: 实现单路径对象池**

`CombatEffectPool` 构造时接收资源路径和池根节点，内部使用 `Stack<GameObject>` 与 `List<GameObject>`。接口固定为：

```csharp
internal sealed class CombatEffectPool
{
    /// <summary>从池中取得实例；池为空时通过 ResourceManager 创建。</summary>
    public GameObject Rent();

    /// <summary>停止粒子、解除父节点、恢复根节点 Transform 并归还实例。</summary>
    public void Return(GameObject instance);

    /// <summary>销毁该路径创建的全部实例。</summary>
    public void Dispose();
}
```

`Rent` 创建新实例后记录到全部实例列表；资源创建失败时返回 null，由服务输出包含 skillId、
effectId 和 path 的错误。`Return` 对收集到的每个子 `ParticleSystem` 执行
`Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear)`，再设置父节点为池根节点、
`localPosition = Vector3.zero`、`localRotation = Quaternion.identity`、
`localScale = Vector3.one`、`SetActive(false)`。`Dispose` 只销毁克隆实例，不调用
`ResourceManager.ReleaseAsset`，资源句柄继续由现有 `ResourceManager` 统一释放。

- [ ] **Step 4: 验证 Unity 编译并提交**

运行：`$CLI compile unity`

预期：Unity 编译成功，无 C# 错误。

```powershell
git add -- Assets/Game/Battle/Skill/Effects/CombatEffectRequest.cs Assets/Game/Battle/Skill/Effects/CombatEffectHandle.cs Assets/Game/Battle/Skill/Effects/CombatEffectPool.cs
git commit -m "实现战斗特效对象池基础"
```

### Task 3: 实现统一播放服务

**Files:**
- Create: `Assets/Game/Battle/Skill/Effects/CombatEffectService.cs`

- [ ] **Step 1: 建立服务的唯一运行时状态**

`CombatEffectService` 继承 `SingletonManager<CombatEffectService>`，字段固定为：

```csharp
private readonly Dictionary<string, CombatEffectPool> m_pools =
    new Dictionary<string, CombatEffectPool>();
private readonly List<CombatEffectHandle> m_activeHandles =
    new List<CombatEffectHandle>();
private readonly Dictionary<CombatEffectChannelKey, CombatEffectHandle> m_channelHandles =
    new Dictionary<CombatEffectChannelKey, CombatEffectHandle>();
private Transform m_poolRoot;
```

`CombatEffectChannelKey` 为本文件内的 `readonly struct`，由 `owner.GetInstanceID()` 和区分大小写的 channel 组成，实现 `IEquatable<CombatEffectChannelKey>`、`Equals` 和 `GetHashCode`。

- [ ] **Step 2: 实现公共定义与技能覆盖合并**

新增私有 `TryResolve`。它必须：

1. 通过 `ConfigManager.Instance.TryGetCombatEffectConfig` 查公共定义；失败时输出 `skillId/effectId`。
2. 复制公共挂载与 Transform。
3. `attachmentOverride != null` 时整体替换挂载、挂点、跟随和朝向。
4. `transformOverride != null` 时整体替换位置、旋转和缩放。
5. `TargetSocket` 缺少目标、`HitDirection` 得到零向量或挂点找不到时输出明确错误并拒绝播放。
6. 所有者规则固定为：`TargetSocket` 使用 Target，其余挂载使用 Source。

粒子回收策略是否用于无粒子 Prefab 只能在对象池取得实例后判断；`Play` 在激活实例前检查全部
子粒子系统，失败时记录相同格式错误并立即把实例归还原池。

错误格式统一包含：`战斗特效播放失败：skillId={skillId}, effectId={effectId}, 原因={reason}`。

- [ ] **Step 3: 实现挂点和坐标解析**

新增以下私有函数并添加简体中文注释：

```csharp
private static Transform FindSocket(CombatAbilitySystem actor, string socketName);
private static Quaternion ResolveWorldRotation(ResolvedCombatEffect effect, CombatEffectRequest request);
private static void ApplyTransform(GameObject instance, ResolvedCombatEffect effect, CombatEffectRequest request);
```

`FindSocket` 遍历 `actor.GetComponentsInChildren<Transform>(true)` 并按名称精确匹配。`ApplyTransform` 严格执行设计坐标规则：

- 世界命中点：`position = hitPoint + worldRotation * offset`。
- 挂点且跟随：父节点为挂点，位置和旋转使用局部空间。
- 挂点不跟随：用挂点世界变换计算一次世界位置和旋转，父节点为空。
- 缩放始终使用解析后的配置值。

- [ ] **Step 4: 实现播放、通道替换和自动回收**

公开接口固定为：

```csharp
/// <summary>解析并播放一次战斗特效，失败时返回 null。</summary>
public CombatEffectHandle Play(CombatEffectRequest request);

/// <summary>停止指定角色唯一通道上的活动特效。</summary>
public void StopChannel(CombatAbilitySystem owner, string channel);

/// <summary>回收指定角色拥有的全部跟随或手动生命周期特效。</summary>
public void StopOwnerEffects(CombatAbilitySystem owner);
```

`Play` 顺序固定为：解析配置、处理唯一通道旧句柄、从 path 对应池取实例、应用 Transform、
收集全部子粒子、激活实例、逐个执行 `Clear(false)` 与 `Play(false)`、创建句柄、写入活动列表
和可选通道表。这样父粒子不会递归启动子粒子后又被循环重复启动。`Update` 倒序调用
`ShouldRecycle(Time.deltaTime)`；满足条件时走唯一 `RecycleHandle`，同时清理活动列表和通道表。

`StopOwnerEffects` 根据句柄的 `Owner`、`FollowsOwner` 和 `RecycleMode`，只回收该所有者的
跟随实例与手动生命周期实例；已经脱离角色的世界瞬时特效继续自然播放。

- [ ] **Step 5: 实现服务销毁清理**

`OnDestroy` 先回收活动句柄、逐池 `Dispose`、清空三个集合，再调用 `base.OnDestroy()`。不得吞没资源或配置异常。

- [ ] **Step 6: 验证 Unity 编译并提交**

运行：`$CLI compile unity`

预期：Unity 编译成功，无 C# 错误。

```powershell
git add -- Assets/Game/Battle/Skill/Effects/CombatEffectService.cs
git commit -m "实现战斗特效播放服务"
```

### Task 4: 接入角色动画事件与死亡清理

**Files:**
- Create: `Assets/Game/Battle/Skill/Effects/CharacterEffectController.cs`
- Modify: `Assets/Game/Battle/Ability/CombatAbilitySystem.cs:25-70`
- Modify: `Assets/Game/Battle/Ability/CombatAbilitySystem.cs:130-177`
- Modify: `Assets/Game/Battle/Ability/CombatAbilitySystem.cs:670-677`
- Modify: `Assets/Game/Character/CharacterStateMachine.cs:1-35`
- Modify: `Assets/Game/Character/CharacterStateMachine.cs:326-380`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyAnimationComponent.cs:1-63`
- Modify: `Assets/Game/Character/Enemy/Components/EnemyAnimationComponent.cs:354-370`

- [ ] **Step 1: 实现角色局部控制器**

`CharacterEffectController` 是普通强类型对象，不是额外 MonoBehaviour，因此不需要修改场景或 Prefab：

```csharp
public sealed class CharacterEffectController
{
    private readonly CombatAbilitySystem m_owner;

    /// <summary>绑定角色战斗能力系统作为特效所有者。</summary>
    public CharacterEffectController(CombatAbilitySystem owner)
    {
        m_owner = owner;
    }

    /// <summary>按当前技能的触发标识播放全部匹配攻击特效。</summary>
    public void PlayAttackEffects(string triggerKey)
    {
        SkillConfig skill = m_owner.CurrentSkill;
        if (skill == null)
        {
            Debug.LogError($"攻击特效触发失败：角色没有当前技能，triggerKey={triggerKey}", m_owner);
            return;
        }

        for (int i = 0; i < skill.attackEffects.Length; i++)
        {
            SkillEffectBinding binding = skill.attackEffects[i];
            if (binding.triggerKey != triggerKey)
            {
                continue;
            }

            CombatEffectService.Instance.Play(new CombatEffectRequest(
                binding,
                skill.skillId,
                m_owner,
                null,
                m_owner.transform.position,
                m_owner.transform.forward));
        }
    }

    /// <summary>停止当前角色指定唯一通道上的特效。</summary>
    public void StopChannel(string channel)
    {
        CombatEffectService.Instance.StopChannel(m_owner, channel);
    }

    /// <summary>清理当前角色拥有的跟随和手动生命周期特效。</summary>
    public void StopOwnedEffects()
    {
        CombatEffectService service;
        if (CombatEffectService.TryGetInstance(out service))
        {
            service.StopOwnerEffects(m_owner);
        }
    }
}
```

- [ ] **Step 2: 让 CombatAbilitySystem 持有控制器**

新增 `CharacterEffectController m_effectController`；`Awake` 完成依赖初始化后创建控制器。新增两个公开动画事件转发接口：

```csharp
/// <summary>转发攻击动画事件并播放匹配特效。</summary>
public void PlayAttackEffect(string triggerKey)
{
    m_effectController.PlayAttackEffects(triggerKey);
}

/// <summary>转发攻击动画事件并停止指定特效通道。</summary>
public void StopAttackEffect(string channel)
{
    m_effectController.StopChannel(channel);
}
```

`OnDisable` 在 `CancelActiveAbility` 前调用 `StopOwnedEffects`。`OnAttributeChanged` 在生命属性变化且 `m_attributes.IsDead` 时调用 `StopOwnedEffects`，然后保留原稳定值恢复逻辑。这样死亡前已有的持续特效会清理，而本次命中结算随后生成的瞬时受伤特效仍可自然结束。

- [ ] **Step 3: 增加玩家动画事件入口**

`CharacterStateMachine` 缓存同一 GameObject 上的 `CombatAbilitySystem`，新增：

```csharp
/// <summary>玩家攻击动画事件：播放当前技能指定触发标识的特效。</summary>
public void PlayAttackEffect(string triggerKey)
{
    GetComponent<CombatAbilitySystem>().PlayAttackEffect(triggerKey);
}

/// <summary>玩家攻击动画事件：停止当前角色指定通道的特效。</summary>
public void StopAttackEffect(string channel)
{
    GetComponent<CombatAbilitySystem>().StopAttackEffect(channel);
}
```

按现有组件依赖风格在 `Start` 缓存引用，缺少时记录明确错误并禁用状态机；不要每次动画事件重复 `GetComponent`。上面的代码块表达公开签名，最终实现使用缓存字段。

- [ ] **Step 4: 增加敌人动画事件入口**

`EnemyAnimationComponent` 在 `Awake` 缓存同一 GameObject 的 `CombatAbilitySystem`，缺失时与现有 combat、weaponHandler 一样报错并禁用。新增与玩家同名同参的 `PlayAttackEffect(string triggerKey)` 和 `StopAttackEffect(string channel)`，直接转发给能力系统。

不修改现有 `HandleAnimationEvent` 的武器命中窗口协议，也不修改任何 `.controller`。

- [ ] **Step 5: 验证 Unity 编译并提交**

运行：`$CLI compile unity`

预期：Unity 编译成功，无 C# 错误。

```powershell
git add -- Assets/Game/Battle/Skill/Effects/CharacterEffectController.cs Assets/Game/Battle/Ability/CombatAbilitySystem.cs Assets/Game/Character/CharacterStateMachine.cs Assets/Game/Character/Enemy/Components/EnemyAnimationComponent.cs
git commit -m "接入角色攻击特效动画事件"
```

### Task 5: 接入战斗事件并迁移技能 JSON

**Files:**
- Modify: `Assets/Game/Battle/Skill/Effects/CombatEffectExecutor.cs:1-69`
- Modify: `Assets/Data/EnemySkillConfig.json`
- Modify: `Assets/Data/WeaponConfig/SingleSwordSkillConfig.json`
- Modify: `Assets/Data/WeaponConfig/SpearSkillConfig.json`

- [ ] **Step 1: 重写 CombatEffectExecutor 为事件适配器**

删除 `ResourceManager` 引用和直接实例化逻辑。最终结构固定为：

```csharp
public static class CombatEffectExecutor
{
    private static readonly SkillEffectBinding[] EmptyBindings = new SkillEffectBinding[0];

    /// <summary>把战斗结算结果转换为统一特效播放请求。</summary>
    public static void Execute(CombatEvent combatEvent)
    {
        if (combatEvent == null || combatEvent.Skill == null)
        {
            return;
        }

        SkillEffectBinding[] bindings = ResolveBindings(combatEvent);
        for (int i = 0; i < bindings.Length; i++)
        {
            CombatEffectService.Instance.Play(new CombatEffectRequest(
                bindings[i],
                combatEvent.Skill.skillId,
                combatEvent.Source,
                combatEvent.Target,
                combatEvent.HitPoint,
                combatEvent.HitDirection));
        }
    }

    /// <summary>按命中结果选择技能对应的特效绑定列表。</summary>
    private static SkillEffectBinding[] ResolveBindings(CombatEvent combatEvent)
    {
        switch (combatEvent.Type)
        {
            case CombatEventType.Hit:
                return combatEvent.Skill.onHitEffects;
            case CombatEventType.Blocked:
                return combatEvent.Skill.onBlockEffects;
            case CombatEventType.Parried:
                return combatEvent.Skill.onParryEffects;
            default:
                return EmptyBindings;
        }
    }
}
```

保持 `CombatAbilitySystem` 现有两处 `CombatEffectExecutor.Execute` 调用位置不变。

- [ ] **Step 2: 机械迁移三份技能配置字段**

当前三份 JSON 的四类特效数组均为空，没有现有资源路径需要抽取。对每个技能对象执行唯一变更：

```json
"attackEffects": [],
"onHitEffects": [],
"onBlockEffects": [],
"onParryEffects": []
```

删除同一对象中的 `onCastEffects`。不要增加示例 Prefab、虚构 effectId 或动画事件；具体资源接入时再向公共表与技能绑定同时添加数据。

- [ ] **Step 3: 静态确认旧模型完全移除**

使用 `codedb_text_search` 批量查询：`SkillEffectData`、`SkillEffectTrigger`、`onCastEffects`。

预期：`Assets/Game/**/*.cs` 和三份技能 JSON 均无旧模型引用。JSON 不在 codedb 索引中，使用结构化 JSON 读取确认每个技能含四组新数组且不含 `onCastEffects`。

- [ ] **Step 4: 验证 Unity 编译并提交**

运行：`$CLI compile unity`

预期：Unity 编译成功，无 C# 错误。

```powershell
git add -- Assets/Game/Battle/Skill/Effects/CombatEffectExecutor.cs Assets/Data/EnemySkillConfig.json Assets/Data/WeaponConfig/SingleSwordSkillConfig.json Assets/Data/WeaponConfig/SpearSkillConfig.json
git commit -m "接入战斗事件特效播放"
```

### Task 6: 最终静态验收

**Files:**
- Verify only; no new files.

- [ ] **Step 1: 执行项目规定的 Unity 编译**

运行：`$CLI compile unity`

预期：Unity 编译成功，控制台无编译错误。不得使用 `compile dotnet` 替代。

- [ ] **Step 2: 检查旧类型和单一真相约束**

使用 codedb 批量检查：

- `SkillEffectData`、`SkillEffectTrigger`、`onCastEffects`：预期零结果。
- `ResourceManager.Instance.Instantiate` 在战斗特效目录中：预期只由 `CombatEffectPool` 调用。
- `CombatEffectService.Instance.Play`：预期由 `CharacterEffectController` 和 `CombatEffectExecutor` 调用。
- `StopOwnerEffects`：预期由 `CharacterEffectController` 和服务内部生命周期使用。

- [ ] **Step 3: 检查变更范围和格式**

```powershell
git diff 2cb2816 --check
git diff 2cb2816 --name-only -- '*.controller'
git status --short
```

预期：`diff --check` 无输出；`.controller` 查询无输出；工作树无未提交文件。允许 Unity 为新增 `.cs` 和 `.json` 生成对应 `.meta`，这些 `.meta` 应随所属任务提交。

- [ ] **Step 4: 记录未进行运行时验收的限制**

最终交付明确说明：根据用户要求未运行 Play Mode，因此动画事件时机、粒子完成检测、唯一通道替换、死亡清理和池复用只完成编译与静态验证，不宣称经过运行时验证。
