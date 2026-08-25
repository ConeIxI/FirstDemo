# Combat Effect Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a scene-level combat effect management framework for attack animation effects, hit effects, and injury effects while preserving combat settlement as the source of gameplay truth.

**Architecture:** Add a reusable public effect config, skill-level effect bindings, a scene-level `CombatEffectService`, and a private prefab-path object pool. Keep `CombatEffectExecutor` as the `CombatEvent` adapter, and let `CharacterEffectController` only provide character context and animation-event entry points.

**Tech Stack:** Unity 2022.3.61f1c1, C# 9.0, Newtonsoft.Json config loading, Addressables through existing `ResourceManager`, existing `$CLI compile unity` verification.

---

## Constraints

- Do not modify any `.controller` file.
- Do not add test files or test code.
- Do not use Play Mode for acceptance.
- Every new or modified function must include a concise Simplified Chinese comment.
- Keep C# syntax compatible with C# 9.0.
- Use fast-fail configuration errors instead of silent fallback behavior.

## File Structure

- Modify: `Assets/Game/Battle/Skill/Common/SkillConfig.cs`
  - Replace `SkillEffectData` and `SkillEffectTrigger` with `SkillEffectBinding`, `CombatEffectAttachmentOverride`, `CombatEffectTransformOverride`, and the enum types needed by public effect definitions.
  - Replace `onCastEffects` with `attackEffects` and keep `onHitEffects`, `onBlockEffects`, `onParryEffects` as binding arrays.
  - Update `SkillConfigDefaults.EnsureEffectArrays` to normalize the new arrays only.
- Create: `Assets/Game/Battle/Skill/Effects/CombatEffectConfig.cs`
  - Public reusable effect definition implementing `IConfig`.
- Create: `Assets/Game/Battle/Skill/Effects/CombatEffectPlayContext.cs`
  - Strongly typed runtime context used by both animation effects and combat-event effects.
- Create: `Assets/Game/Battle/Skill/Effects/CombatEffectInstanceHandle.cs`
  - Small immutable-ish handle for active effect instances.
- Create: `Assets/Game/Battle/Skill/Effects/CombatEffectPool.cs`
  - Internal prefab-path pool owned only by `CombatEffectService`.
- Create: `Assets/Game/Battle/Skill/Effects/CombatEffectService.cs`
  - Scene-level singleton-like MonoBehaviour that loads public configs, merges overrides, plays effects, updates lifetimes, and recycles instances.
- Create: `Assets/Game/Battle/Skill/Effects/CharacterEffectController.cs`
  - Per-character MonoBehaviour used by animation events to play and stop attack effects.
- Modify: `Assets/Game/Battle/Skill/Effects/CombatEffectExecutor.cs`
  - Remove direct `ResourceManager.Instance.Instantiate` usage and adapt `CombatEvent` to `CombatEffectService` calls.
- Modify: `Assets/Framework/Manager/ConfigManager.cs`
  - Load `Data/CombatEffectConfig.json`, validate duplicate effect IDs, expose `GetCombatEffectConfig` and `GetCombatEffectConfigs`.
- Create: `Assets/Data/CombatEffectConfig.json`
  - Public effect definitions extracted from current skill config effect paths.
- Modify: `Assets/Data/EnemySkillConfig.json`
  - Replace legacy `skillEffectConfig`/old effect object data with effect bindings.
- Modify: `Assets/Data/WeaponConfig/SingleSwordSkillConfig.json`
  - Replace legacy `skillEffectConfig`/old effect object data with effect bindings.
- Modify: `Assets/Data/WeaponConfig/SpearSkillConfig.json`
  - Replace legacy `skillEffectConfig`/old effect object data with effect bindings.

---

### Task 1: Replace Skill Effect Data Model

**Files:**
- Modify: `Assets/Game/Battle/Skill/Common/SkillConfig.cs:26-75`
- Modify: `Assets/Game/Battle/Skill/Common/SkillConfig.cs:155-176`
- Modify: `Assets/Game/Battle/Skill/Common/SkillConfig.cs:231-253`

- [ ] **Step 1: Replace skill effect arrays**

In `SkillConfig`, replace the old arrays:

```csharp
public SkillEffectData[] onCastEffects;
public SkillEffectData[] onHitEffects;
public SkillEffectData[] onBlockEffects;
public SkillEffectData[] onParryEffects;
```

with:

```csharp
public SkillEffectBinding[] attackEffects;
public SkillEffectBinding[] onHitEffects;
public SkillEffectBinding[] onBlockEffects;
public SkillEffectBinding[] onParryEffects;
```

- [ ] **Step 2: Replace old effect DTOs**

Delete `SkillEffectData`, `SkillEffectTrigger`, `SkillEffectConfig`, and `EffectObjectInfo`. Add these serializable types near the bottom of the same file, before `SkillAudioConfig`:

```csharp
[Serializable]
public class SkillEffectBinding
{
    public string triggerKey;
    public string effectId;
    public CombatEffectAttachmentOverride attachmentOverride;
    public CombatEffectTransformOverride transformOverride;
}

[Serializable]
public class CombatEffectAttachmentOverride
{
    public bool overrideAttachment;
    public CombatEffectAttachment attachment;
    public bool overrideSocketName;
    public string socketName;
    public bool overrideFollow;
    public bool follow;
}

[Serializable]
public class CombatEffectTransformOverride
{
    public bool overridePosition;
    public Vec3 position;
    public bool overrideRotation;
    public Vec3 rotation;
    public bool overrideScale;
    public Vec3 scale;
    public bool overrideOrientation;
    public CombatEffectOrientation orientation;
    public bool overrideRecycleMode;
    public CombatEffectRecycleMode recycleMode;
    public bool overrideDuration;
    public float duration;
    public bool overrideConcurrency;
    public CombatEffectConcurrency concurrency;
    public bool overrideChannel;
    public string channel;
}

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
    ManualStop
}

public enum CombatEffectConcurrency
{
    Stack,
    UniqueChannel
}
```

- [ ] **Step 3: Update effect array normalization**

Replace `EnsureEffectArrays` with:

```csharp
/// <summary>把缺失的特效绑定数组统一转换为空数组。</summary>
private static void EnsureEffectArrays(SkillConfig config)
{
    if (config.attackEffects == null)
    {
        config.attackEffects = new SkillEffectBinding[0];
    }

    if (config.onHitEffects == null)
    {
        config.onHitEffects = new SkillEffectBinding[0];
    }

    if (config.onBlockEffects == null)
    {
        config.onBlockEffects = new SkillEffectBinding[0];
    }

    if (config.onParryEffects == null)
    {
        config.onParryEffects = new SkillEffectBinding[0];
    }
}
```

- [ ] **Step 4: Keep `Vec3.ToVector3` formatted**

Change the return line to keep local style readable:

```csharp
/// <summary>把配置向量转换为 Unity 向量。</summary>
public Vector3 ToVector3()
{
    return new Vector3(x, y, z);
}
```

- [ ] **Step 5: Static check old types are gone from this file**

Run:

```powershell
Select-String -Path 'Assets/Game/Battle/Skill/Common/SkillConfig.cs' -Pattern 'SkillEffectData|SkillEffectTrigger|onCastEffects|SkillEffectConfig|EffectObjectInfo'
```

Expected: no matches.

- [ ] **Step 6: Commit task 1**

```powershell
git add Assets/Game/Battle/Skill/Common/SkillConfig.cs
git commit -m "重构技能特效绑定数据结构"
```

---

### Task 2: Add Public Combat Effect Config Loading

**Files:**
- Create: `Assets/Game/Battle/Skill/Effects/CombatEffectConfig.cs`
- Modify: `Assets/Framework/Manager/ConfigManager.cs:1-263`
- Create: `Assets/Data/CombatEffectConfig.json`

- [ ] **Step 1: Create public effect config type**

Create `CombatEffectConfig.cs`:

```csharp
using System;
using Game.Battle.Skill.Common;
using Game.Common;

namespace Game.Battle.Skill.Effects
{
    [Serializable]
    public sealed class CombatEffectConfig : IConfig
    {
        public string effectId;
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
}
```

- [ ] **Step 2: Add config dictionary and load call**

In `ConfigManager`, add:

```csharp
private readonly Dictionary<string, CombatEffectConfig> m_combatEffectConfigs = new Dictionary<string, CombatEffectConfig>();
```

In `Awake`, call `_LoadCombatEffectConfigs();` after `_LoadBuffConfigs();` and before `_LoadItemConfigs();`.

- [ ] **Step 3: Add config loader and validator**

Add to `ConfigManager`:

```csharp
/// <summary>加载并校验全部公共战斗特效配置。</summary>
private void _LoadCombatEffectConfigs()
{
    TextAsset configAsset = ResourceManager.Instance.LoadAsset<TextAsset>("Data/CombatEffectConfig.json");
    if (configAsset == null)
    {
        throw new Exception("未找到战斗特效配置文件：Data/CombatEffectConfig.json");
    }

    CombatEffectConfig[] configs = JsonConvert.DeserializeObject<CombatEffectConfig[]>(configAsset.text);
    if (configs == null)
    {
        throw new Exception("战斗特效配置文件解析失败：Data/CombatEffectConfig.json");
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

/// <summary>校验单个公共战斗特效配置的基础字段。</summary>
private static void ValidateCombatEffectConfig(CombatEffectConfig config)
{
    if (config == null)
    {
        throw new Exception("战斗特效配置存在空配置项");
    }

    if (string.IsNullOrEmpty(config.effectId))
    {
        throw new Exception("战斗特效配置存在空 effectId");
    }

    if (string.IsNullOrEmpty(config.path))
    {
        throw new Exception($"战斗特效{config.effectId}缺少 Prefab 路径");
    }

    if ((config.attachment == CombatEffectAttachment.SourceSocket || config.attachment == CombatEffectAttachment.TargetSocket)
        && string.IsNullOrEmpty(config.socketName))
    {
        throw new Exception($"战斗特效{config.effectId}缺少挂点名称");
    }

    if ((config.recycleMode == CombatEffectRecycleMode.ManualStop || config.concurrency == CombatEffectConcurrency.UniqueChannel)
        && string.IsNullOrEmpty(config.channel))
    {
        throw new Exception($"战斗特效{config.effectId}缺少唯一通道名称");
    }

    if (config.recycleMode == CombatEffectRecycleMode.FixedDuration && config.duration <= 0f)
    {
        throw new Exception($"战斗特效{config.effectId}固定时长必须大于零");
    }
}
```

- [ ] **Step 4: Add config getters**

Add near existing getters:

```csharp
/// <summary>按特效Id查询公共战斗特效配置。</summary>
public CombatEffectConfig GetCombatEffectConfig(string effectId)
{
    if (!m_combatEffectConfigs.ContainsKey(effectId))
    {
        throw new Exception($"未找到战斗特效配置：{effectId}");
    }

    return m_combatEffectConfigs[effectId];
}

/// <summary>返回全部公共战斗特效配置。</summary>
public CombatEffectConfig[] GetCombatEffectConfigs()
{
    return m_combatEffectConfigs.Values.ToArray();
}
```

- [ ] **Step 5: Create initial public config JSON**

Create `Assets/Data/CombatEffectConfig.json`:

```json
[
  {
    "effectId": "sword_slash_1",
    "path": "Fx/Sword_Slash_1.prefab",
    "attachment": "SourceSocket",
    "socketName": "Weapon",
    "follow": false,
    "position": { "x": 0, "y": 0.5, "z": -0.28 },
    "rotation": { "x": 260, "y": 0, "z": 90 },
    "scale": { "x": -0.52, "y": 0.52, "z": 0.52 },
    "orientation": "ConfigRotation",
    "recycleMode": "FixedDuration",
    "duration": 1.5,
    "concurrency": "Stack",
    "channel": ""
  },
  {
    "effectId": "blood_spray_08",
    "path": "Fx/FX_BloodSpray_08.prefab",
    "attachment": "TargetSocket",
    "socketName": "Chest",
    "follow": false,
    "position": { "x": 0, "y": 0, "z": 0 },
    "rotation": { "x": 0, "y": 0, "z": 0 },
    "scale": { "x": 1, "y": 1, "z": 1 },
    "orientation": "HitDirection",
    "recycleMode": "ParticleComplete",
    "duration": 0,
    "concurrency": "Stack",
    "channel": ""
  }
]
```

- [ ] **Step 6: Static check config references compile at source level**

Run:

```powershell
Select-String -Path 'Assets/Framework/Manager/ConfigManager.cs','Assets/Game/Battle/Skill/Effects/CombatEffectConfig.cs' -Pattern 'CombatEffectConfig|GetCombatEffectConfig|GetCombatEffectConfigs'
```

Expected: matches in both files.

- [ ] **Step 7: Commit task 2**

```powershell
git add Assets/Game/Battle/Skill/Effects/CombatEffectConfig.cs Assets/Framework/Manager/ConfigManager.cs Assets/Data/CombatEffectConfig.json
git commit -m "新增公共战斗特效配置"
```

---

### Task 3: Add Runtime Context, Handles, Pool, and Service

**Files:**
- Create: `Assets/Game/Battle/Skill/Effects/CombatEffectPlayContext.cs`
- Create: `Assets/Game/Battle/Skill/Effects/CombatEffectInstanceHandle.cs`
- Create: `Assets/Game/Battle/Skill/Effects/CombatEffectPool.cs`
- Create: `Assets/Game/Battle/Skill/Effects/CombatEffectService.cs`

- [ ] **Step 1: Create play context**

Create `CombatEffectPlayContext.cs`:

```csharp
using Game.Battle.Ability;
using Game.Battle.Skill.Common;
using UnityEngine;

namespace Game.Battle.Skill.Effects
{
    public sealed class CombatEffectPlayContext
    {
        public SkillConfig Skill { get; private set; }
        public SkillEffectBinding Binding { get; private set; }
        public CombatAbilitySystem Source { get; private set; }
        public CombatAbilitySystem Target { get; private set; }
        public Vector3 HitPoint { get; private set; }
        public Vector3 HitDirection { get; private set; }
        public Object Owner { get; private set; }

        /// <summary>创建动画事件触发的攻击动作特效上下文。</summary>
        public static CombatEffectPlayContext ForAttack(SkillConfig skill, SkillEffectBinding binding, CombatAbilitySystem source, Object owner)
        {
            return new CombatEffectPlayContext(skill, binding, source, null, source.transform.position, source.transform.forward, owner);
        }

        /// <summary>创建战斗结算事件触发的命中或受伤特效上下文。</summary>
        public static CombatEffectPlayContext ForCombatEvent(CombatEvent combatEvent, SkillEffectBinding binding)
        {
            return new CombatEffectPlayContext(
                combatEvent.Skill,
                binding,
                combatEvent.Source,
                combatEvent.Target,
                combatEvent.HitPoint,
                combatEvent.HitDirection,
                combatEvent.Source);
        }

        /// <summary>保存一次特效播放所需的完整上下文。</summary>
        private CombatEffectPlayContext(
            SkillConfig skill,
            SkillEffectBinding binding,
            CombatAbilitySystem source,
            CombatAbilitySystem target,
            Vector3 hitPoint,
            Vector3 hitDirection,
            Object owner)
        {
            Skill = skill;
            Binding = binding;
            Source = source;
            Target = target;
            HitPoint = hitPoint;
            HitDirection = hitDirection;
            Owner = owner;
        }
    }
}
```

- [ ] **Step 2: Create instance handle**

Create `CombatEffectInstanceHandle.cs`:

```csharp
using UnityEngine;

namespace Game.Battle.Skill.Effects
{
    public sealed class CombatEffectInstanceHandle
    {
        public string Path { get; private set; }
        public string Channel { get; private set; }
        public Object Owner { get; private set; }
        public GameObject Instance { get; private set; }
        public CombatEffectRecycleMode RecycleMode { get; private set; }
        public float RemainingDuration { get; set; }

        /// <summary>初始化一个活动特效实例句柄。</summary>
        public CombatEffectInstanceHandle(
            string path,
            string channel,
            Object owner,
            GameObject instance,
            CombatEffectRecycleMode recycleMode,
            float remainingDuration)
        {
            Path = path;
            Channel = channel;
            Owner = owner;
            Instance = instance;
            RecycleMode = recycleMode;
            RemainingDuration = remainingDuration;
        }
    }
}
```

- [ ] **Step 3: Create prefab-path pool**

Create `CombatEffectPool.cs`:

```csharp
using System.Collections.Generic;
using GameMain2.Framework.Manager;
using UnityEngine;

namespace Game.Battle.Skill.Effects
{
    internal sealed class CombatEffectPool
    {
        private readonly Transform m_root;
        private readonly Dictionary<string, Stack<GameObject>> m_instances = new Dictionary<string, Stack<GameObject>>();

        /// <summary>创建由战斗特效服务独占的对象池。</summary>
        public CombatEffectPool(Transform root)
        {
            m_root = root;
        }

        /// <summary>按 Prefab 路径取出一个可播放实例。</summary>
        public GameObject Spawn(string path)
        {
            Stack<GameObject> pool;
            if (m_instances.TryGetValue(path, out pool) && pool.Count > 0)
            {
                GameObject pooled = pool.Pop();
                pooled.SetActive(true);
                return pooled;
            }

            return ResourceManager.Instance.Instantiate(path);
        }

        /// <summary>停止粒子并把实例放回对应路径的池中。</summary>
        public void Despawn(CombatEffectInstanceHandle handle)
        {
            GameObject instance = handle.Instance;
            ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            instance.transform.SetParent(m_root, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            instance.SetActive(false);

            Stack<GameObject> pool;
            if (!m_instances.TryGetValue(handle.Path, out pool))
            {
                pool = new Stack<GameObject>();
                m_instances.Add(handle.Path, pool);
            }

            pool.Push(instance);
        }
    }
}
```

- [ ] **Step 4: Create combat effect service skeleton**

Create `CombatEffectService.cs` with the full method set:

```csharp
using System.Collections.Generic;
using Game.Battle.Ability;
using Game.Battle.Skill.Common;
using GameMain2.Framework.Manager;
using UnityEngine;

namespace Game.Battle.Skill.Effects
{
    public sealed class CombatEffectService : MonoBehaviour
    {
        public static CombatEffectService Instance { get; private set; }

        private readonly List<CombatEffectInstanceHandle> m_activeInstances = new List<CombatEffectInstanceHandle>();
        private readonly List<CombatEffectInstanceHandle> m_recycleBuffer = new List<CombatEffectInstanceHandle>();
        private readonly Dictionary<Object, Dictionary<string, CombatEffectInstanceHandle>> m_uniqueChannels =
            new Dictionary<Object, Dictionary<string, CombatEffectInstanceHandle>>();

        private CombatEffectPool m_pool;

        /// <summary>初始化场景级战斗特效服务实例。</summary>
        private void Awake()
        {
            Instance = this;
            m_pool = new CombatEffectPool(transform);
        }

        /// <summary>更新固定时长和粒子完成类型的活动特效。</summary>
        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>播放一条技能特效绑定并返回活动实例句柄。</summary>
        public CombatEffectInstanceHandle Play(CombatEffectPlayContext context)
        {
            CombatEffectConfig config = ConfigManager.Instance.GetCombatEffectConfig(context.Binding.effectId);
            CombatEffectRuntimeData runtimeData = CreateRuntimeData(config, context.Binding);
            ValidateRuntimeData(runtimeData, context);

            if (runtimeData.concurrency == CombatEffectConcurrency.UniqueChannel)
            {
                StopOwnerChannel(context.Owner, runtimeData.channel);
            }

            GameObject instance = m_pool.Spawn(runtimeData.path);
            ApplyTransform(instance, runtimeData, context);
            RestartParticles(instance);

            CombatEffectInstanceHandle handle = new CombatEffectInstanceHandle(
                runtimeData.path,
                runtimeData.channel,
                context.Owner,
                instance,
                runtimeData.recycleMode,
                runtimeData.duration);
            RegisterHandle(handle, runtimeData.concurrency);
            return handle;
        }

        /// <summary>停止指定所有者的唯一通道特效。</summary>
        public void StopOwnerChannel(Object owner, string channel)
        {
            Dictionary<string, CombatEffectInstanceHandle> ownerChannels;
            if (!m_uniqueChannels.TryGetValue(owner, out ownerChannels) || !ownerChannels.ContainsKey(channel))
            {
                return;
            }

            Recycle(ownerChannels[channel]);
        }

        /// <summary>回收指定所有者名下的所有活动特效。</summary>
        public void StopOwner(Object owner)
        {
            m_recycleBuffer.Clear();
            for (int i = 0; i < m_activeInstances.Count; i++)
            {
                if (m_activeInstances[i].Owner == owner)
                {
                    m_recycleBuffer.Add(m_activeInstances[i]);
                }
            }

            for (int i = 0; i < m_recycleBuffer.Count; i++)
            {
                Recycle(m_recycleBuffer[i]);
            }
        }
    }
}
```

- [ ] **Step 5: Add service private helpers**

In the same file, add private helper types and methods before the final class brace:

```csharp
/// <summary>递进活动特效生命周期并回收已结束实例。</summary>
private void Tick(float deltaTime)
{
    m_recycleBuffer.Clear();
    for (int i = 0; i < m_activeInstances.Count; i++)
    {
        CombatEffectInstanceHandle handle = m_activeInstances[i];
        if (handle.RecycleMode == CombatEffectRecycleMode.FixedDuration)
        {
            handle.RemainingDuration -= deltaTime;
            if (handle.RemainingDuration <= 0f)
            {
                m_recycleBuffer.Add(handle);
            }
        }
        else if (handle.RecycleMode == CombatEffectRecycleMode.ParticleComplete && IsParticleComplete(handle.Instance))
        {
            m_recycleBuffer.Add(handle);
        }
    }

    for (int i = 0; i < m_recycleBuffer.Count; i++)
    {
        Recycle(m_recycleBuffer[i]);
    }
}

/// <summary>合并公共定义和技能局部覆盖。</summary>
private static CombatEffectRuntimeData CreateRuntimeData(CombatEffectConfig config, SkillEffectBinding binding)
{
    CombatEffectRuntimeData data = new CombatEffectRuntimeData();
    data.effectId = config.effectId;
    data.path = config.path;
    data.attachment = config.attachment;
    data.socketName = config.socketName;
    data.follow = config.follow;
    data.position = config.position.ToVector3();
    data.rotation = config.rotation.ToVector3();
    data.scale = config.scale.ToVector3();
    data.orientation = config.orientation;
    data.recycleMode = config.recycleMode;
    data.duration = config.duration;
    data.concurrency = config.concurrency;
    data.channel = config.channel;

    CombatEffectAttachmentOverride attachmentOverride = binding.attachmentOverride;
    if (attachmentOverride != null)
    {
        if (attachmentOverride.overrideAttachment)
        {
            data.attachment = attachmentOverride.attachment;
        }

        if (attachmentOverride.overrideSocketName)
        {
            data.socketName = attachmentOverride.socketName;
        }

        if (attachmentOverride.overrideFollow)
        {
            data.follow = attachmentOverride.follow;
        }
    }

    CombatEffectTransformOverride transformOverride = binding.transformOverride;
    if (transformOverride != null)
    {
        if (transformOverride.overridePosition)
        {
            data.position = transformOverride.position.ToVector3();
        }

        if (transformOverride.overrideRotation)
        {
            data.rotation = transformOverride.rotation.ToVector3();
        }

        if (transformOverride.overrideScale)
        {
            data.scale = transformOverride.scale.ToVector3();
        }

        if (transformOverride.overrideOrientation)
        {
            data.orientation = transformOverride.orientation;
        }

        if (transformOverride.overrideRecycleMode)
        {
            data.recycleMode = transformOverride.recycleMode;
        }

        if (transformOverride.overrideDuration)
        {
            data.duration = transformOverride.duration;
        }

        if (transformOverride.overrideConcurrency)
        {
            data.concurrency = transformOverride.concurrency;
        }

        if (transformOverride.overrideChannel)
        {
            data.channel = transformOverride.channel;
        }
    }

    return data;
}

/// <summary>校验运行时播放数据和上下文是否满足配置约束。</summary>
private static void ValidateRuntimeData(CombatEffectRuntimeData data, CombatEffectPlayContext context)
{
    if (string.IsNullOrEmpty(data.path))
    {
        throw new System.Exception($"技能{context.Skill.skillId}特效{data.effectId}缺少 Prefab 路径");
    }

    if ((data.attachment == CombatEffectAttachment.SourceSocket || data.attachment == CombatEffectAttachment.TargetSocket)
        && string.IsNullOrEmpty(data.socketName))
    {
        throw new System.Exception($"技能{context.Skill.skillId}特效{data.effectId}缺少挂点名称");
    }

    if ((data.recycleMode == CombatEffectRecycleMode.ManualStop || data.concurrency == CombatEffectConcurrency.UniqueChannel)
        && string.IsNullOrEmpty(data.channel))
    {
        throw new System.Exception($"技能{context.Skill.skillId}特效{data.effectId}缺少通道名称");
    }

    if (data.recycleMode == CombatEffectRecycleMode.FixedDuration && data.duration <= 0f)
    {
        throw new System.Exception($"技能{context.Skill.skillId}特效{data.effectId}固定时长必须大于零");
    }

    if (data.attachment == CombatEffectAttachment.TargetSocket && context.Target == null)
    {
        throw new System.Exception($"技能{context.Skill.skillId}攻击动作特效不能依赖受击者挂点");
    }
}

/// <summary>把实例放置到目标挂点或世界命中点。</summary>
private static void ApplyTransform(GameObject instance, CombatEffectRuntimeData data, CombatEffectPlayContext context)
{
    Transform parent = ResolveParent(data, context);
    Quaternion rotation = ResolveRotation(data, context);

    if (parent != null && data.follow)
    {
        instance.transform.SetParent(parent, false);
        instance.transform.localPosition = data.position;
        instance.transform.localRotation = Quaternion.Euler(data.rotation);
    }
    else if (parent != null)
    {
        instance.transform.SetParent(null, true);
        instance.transform.position = parent.position + rotation * data.position;
        instance.transform.rotation = rotation * Quaternion.Euler(data.rotation);
    }
    else
    {
        instance.transform.SetParent(null, true);
        instance.transform.position = context.HitPoint + rotation * data.position;
        instance.transform.rotation = rotation * Quaternion.Euler(data.rotation);
    }

    if (data.scale != Vector3.zero)
    {
        instance.transform.localScale = data.scale;
    }
}

/// <summary>解析当前播放数据所需的父挂点。</summary>
private static Transform ResolveParent(CombatEffectRuntimeData data, CombatEffectPlayContext context)
{
    if (data.attachment == CombatEffectAttachment.WorldHitPoint)
    {
        return null;
    }

    CombatAbilitySystem owner = data.attachment == CombatEffectAttachment.SourceSocket ? context.Source : context.Target;
    Transform socket = owner.transform.Find(data.socketName);
    if (socket == null)
    {
        throw new System.Exception($"技能{context.Skill.skillId}特效{data.effectId}找不到挂点：{data.socketName}");
    }

    return socket;
}

/// <summary>根据配置规则解析世界旋转。</summary>
private static Quaternion ResolveRotation(CombatEffectRuntimeData data, CombatEffectPlayContext context)
{
    if (data.orientation == CombatEffectOrientation.SourceForward)
    {
        return Quaternion.LookRotation(context.Source.transform.forward, Vector3.up);
    }

    if (data.orientation == CombatEffectOrientation.HitDirection)
    {
        return Quaternion.LookRotation(context.HitDirection, Vector3.up);
    }

    return Quaternion.identity;
}

/// <summary>重新播放实例下所有粒子系统。</summary>
private static void RestartParticles(GameObject instance)
{
    ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
    for (int i = 0; i < particles.Length; i++)
    {
        particles[i].Clear(true);
        particles[i].Play(true);
    }
}

/// <summary>判断实例下全部粒子系统是否都已结束。</summary>
private static bool IsParticleComplete(GameObject instance)
{
    ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
    if (particles.Length == 0)
    {
        throw new System.Exception($"粒子完成回收要求 Prefab 包含 ParticleSystem：{instance.name}");
    }

    for (int i = 0; i < particles.Length; i++)
    {
        if (particles[i].IsAlive(true))
        {
            return false;
        }
    }

    return true;
}

/// <summary>登记活动实例和唯一通道归属。</summary>
private void RegisterHandle(CombatEffectInstanceHandle handle, CombatEffectConcurrency concurrency)
{
    m_activeInstances.Add(handle);
    if (concurrency != CombatEffectConcurrency.UniqueChannel)
    {
        return;
    }

    Dictionary<string, CombatEffectInstanceHandle> ownerChannels;
    if (!m_uniqueChannels.TryGetValue(handle.Owner, out ownerChannels))
    {
        ownerChannels = new Dictionary<string, CombatEffectInstanceHandle>();
        m_uniqueChannels.Add(handle.Owner, ownerChannels);
    }

    ownerChannels[handle.Channel] = handle;
}

/// <summary>回收活动实例并清理通道记录。</summary>
private void Recycle(CombatEffectInstanceHandle handle)
{
    m_activeInstances.Remove(handle);

    Dictionary<string, CombatEffectInstanceHandle> ownerChannels;
    if (m_uniqueChannels.TryGetValue(handle.Owner, out ownerChannels)
        && ownerChannels.ContainsKey(handle.Channel)
        && ownerChannels[handle.Channel] == handle)
    {
        ownerChannels.Remove(handle.Channel);
    }

    m_pool.Despawn(handle);
}

private sealed class CombatEffectRuntimeData
{
    public string effectId;
    public string path;
    public CombatEffectAttachment attachment;
    public string socketName;
    public bool follow;
    public Vector3 position;
    public Vector3 rotation;
    public Vector3 scale;
    public CombatEffectOrientation orientation;
    public CombatEffectRecycleMode recycleMode;
    public float duration;
    public CombatEffectConcurrency concurrency;
    public string channel;
}
```

- [ ] **Step 6: Commit task 3**

```powershell
git add Assets/Game/Battle/Skill/Effects/CombatEffectPlayContext.cs Assets/Game/Battle/Skill/Effects/CombatEffectInstanceHandle.cs Assets/Game/Battle/Skill/Effects/CombatEffectPool.cs Assets/Game/Battle/Skill/Effects/CombatEffectService.cs
git commit -m "新增战斗特效服务和对象池"
```

---

### Task 4: Add Character Animation Event Bridge

**Files:**
- Create: `Assets/Game/Battle/Skill/Effects/CharacterEffectController.cs`

- [ ] **Step 1: Create character effect controller**

Create `CharacterEffectController.cs`:

```csharp
using Game.Battle.Ability;
using Game.Battle.Skill.Common;
using UnityEngine;

namespace Game.Battle.Skill.Effects
{
    public sealed class CharacterEffectController : MonoBehaviour
    {
        private CombatAbilitySystem m_abilitySystem;

        /// <summary>缓存角色能力系统依赖。</summary>
        private void Awake()
        {
            m_abilitySystem = GetComponent<CombatAbilitySystem>();
        }

        /// <summary>动画事件入口：按触发标识播放当前技能的攻击动作特效。</summary>
        public void PlayAttackEffect(string triggerKey)
        {
            SkillConfig skill = m_abilitySystem.CurrentSkill;
            if (skill == null)
            {
                throw new System.Exception($"角色{name}没有当前技能，无法播放攻击特效：{triggerKey}");
            }

            for (int i = 0; i < skill.attackEffects.Length; i++)
            {
                SkillEffectBinding binding = skill.attackEffects[i];
                if (binding.triggerKey != triggerKey)
                {
                    continue;
                }

                CombatEffectPlayContext context = CombatEffectPlayContext.ForAttack(skill, binding, m_abilitySystem, this);
                CombatEffectService.Instance.Play(context);
            }
        }

        /// <summary>动画事件入口：停止当前角色指定通道的持续攻击特效。</summary>
        public void StopAttackEffect(string channel)
        {
            CombatEffectService.Instance.StopOwnerChannel(this, channel);
        }

        /// <summary>角色禁用时清理当前角色名下的活动特效。</summary>
        private void OnDisable()
        {
            if (CombatEffectService.Instance != null)
            {
                CombatEffectService.Instance.StopOwner(this);
            }
        }
    }
}
```

- [ ] **Step 2: Decide component placement without editing controllers**

Add `CharacterEffectController` to character prefabs or scene objects using normal prefab/scene tooling, but do not modify `.controller` files. If the implementation session cannot safely patch prefabs, record the required manual placement in the final handoff.

- [ ] **Step 3: Commit task 4**

```powershell
git add Assets/Game/Battle/Skill/Effects/CharacterEffectController.cs
git commit -m "新增角色动画特效入口"
```

---

### Task 5: Adapt Combat Events to Effect Service

**Files:**
- Modify: `Assets/Game/Battle/Skill/Effects/CombatEffectExecutor.cs:1-69`

- [ ] **Step 1: Replace direct instantiate executor**

Replace `CombatEffectExecutor.cs` with:

```csharp
using Game.Battle.Ability;
using Game.Battle.Skill.Common;

namespace Game.Battle.Skill.Effects
{
    public static class CombatEffectExecutor
    {
        /// <summary>把战斗事件适配为命中、格挡或招架特效播放请求。</summary>
        public static void Execute(CombatEvent combatEvent)
        {
            if (combatEvent == null || combatEvent.Skill == null)
            {
                return;
            }

            SkillEffectBinding[] effects = ResolveEffects(combatEvent);
            if (effects == null)
            {
                return;
            }

            for (int i = 0; i < effects.Length; i++)
            {
                CombatEffectPlayContext context = CombatEffectPlayContext.ForCombatEvent(combatEvent, effects[i]);
                CombatEffectService.Instance.Play(context);
            }
        }

        /// <summary>根据战斗事件类型选择对应的技能特效绑定数组。</summary>
        private static SkillEffectBinding[] ResolveEffects(CombatEvent combatEvent)
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
                    return null;
            }
        }
    }
}
```

- [ ] **Step 2: Static check executor no longer loads resources directly**

Run:

```powershell
Select-String -Path 'Assets/Game/Battle/Skill/Effects/CombatEffectExecutor.cs' -Pattern 'ResourceManager|Instantiate|SkillEffectData|path'
```

Expected: no matches.

- [ ] **Step 3: Commit task 5**

```powershell
git add Assets/Game/Battle/Skill/Effects/CombatEffectExecutor.cs
git commit -m "改造战斗特效事件适配器"
```

---

### Task 6: Migrate Skill JSON Data

**Files:**
- Modify: `Assets/Data/EnemySkillConfig.json`
- Modify: `Assets/Data/WeaponConfig/SingleSwordSkillConfig.json`
- Modify: `Assets/Data/WeaponConfig/SpearSkillConfig.json`

- [ ] **Step 1: Replace legacy skill effect object shape**

For each skill object, remove `skillEffectConfig`. Add these four arrays at the top level:

```json
"attackEffects": [],
"onHitEffects": [],
"onBlockEffects": [],
"onParryEffects": []
```

- [ ] **Step 2: Migrate cast slash effects to attack effects**

Every legacy `skillEffectConfig.castEffectInfo` item with path `Fx/Sword_Slash_1.prefab` becomes:

```json
{
  "triggerKey": "slash",
  "effectId": "sword_slash_1",
  "attachmentOverride": null,
  "transformOverride": null
}
```

Use the same `triggerKey` for first pass because `.controller` files are out of scope; animation event timing can be wired later without changing this data model.

- [ ] **Step 3: Migrate hit blood effects to on-hit effects**

Every legacy `skillEffectConfig.hitEffectInfo` item with path `Fx/FX_BloodSpray_08.prefab` becomes:

```json
{
  "triggerKey": "",
  "effectId": "blood_spray_08",
  "attachmentOverride": null,
  "transformOverride": null
}
```

- [ ] **Step 4: Keep block and parry arrays explicit**

For skills without block or parry-specific effects, keep:

```json
"onBlockEffects": [],
"onParryEffects": []
```

- [ ] **Step 5: Static check old JSON fields are gone**

Run:

```powershell
Select-String -Path 'Assets/Data/EnemySkillConfig.json','Assets/Data/WeaponConfig/SingleSwordSkillConfig.json','Assets/Data/WeaponConfig/SpearSkillConfig.json' -Pattern 'skillEffectConfig|castEffectInfo|hitEffectInfo|trailEffectInfo|onCastEffects'
```

Expected: no matches.

- [ ] **Step 6: Commit task 6**

```powershell
git add Assets/Data/EnemySkillConfig.json Assets/Data/WeaponConfig/SingleSwordSkillConfig.json Assets/Data/WeaponConfig/SpearSkillConfig.json
git commit -m "迁移技能特效配置数据"
```

---

### Task 7: Wire Scene Service and Verify Compile

**Files:**
- Modify: `Assets/Scenes/Scene1.unity`
- Do not modify any `.controller` file.

- [ ] **Step 1: Place CombatEffectService in runtime scene**

In `Assets/Scenes/Scene1.unity`, create a dedicated root GameObject named `CombatEffectService`, add the `CombatEffectService` component, and keep its Transform at identity so the pool reset rule is stable.

- [ ] **Step 2: Static check no prohibited controller edits exist**

Run:

```powershell
git diff --name-only | Select-String -Pattern '\.controller$'
```

Expected: no matches.

- [ ] **Step 3: Static check old source references are gone**

Run:

```powershell
Select-String -Path 'Assets/Game/**/*.cs' -Pattern 'SkillEffectData|SkillEffectTrigger|onCastEffects|SkillEffectConfig|EffectObjectInfo'
```

Expected: no matches.

- [ ] **Step 4: Unity compile verification**

Run:

```powershell
$CLI compile unity
```

Expected: Unity compilation succeeds. Do not run Play Mode.

- [ ] **Step 5: Commit task 7**

```powershell
git add Assets
git commit -m "接入战斗特效服务并通过编译"
```

---

## Self-Review

- Spec coverage: The plan covers attack animation effects, combat-event hit/block/parry effects, injury effects through skill-controlled `onHitEffects`, public config plus skill overrides, prefab-path pooling, lifecycle modes, attachment modes, follow behavior, unique channels, and non-Play-Mode verification.
- Placeholder scan: No step uses unresolved placeholder language. Scene service placement is explicit: `Assets/Scenes/Scene1.unity` gets one root `CombatEffectService` GameObject.
- Type consistency: `SkillEffectBinding`, `CombatEffectConfig`, `CombatEffectPlayContext`, `CombatEffectInstanceHandle`, `CombatEffectPool`, `CombatEffectService`, and `CharacterEffectController` names are consistent across tasks.
- Constraint check: No test files are planned, no Play Mode acceptance is planned, and `.controller` files are explicitly excluded.
