# 声音管理框架 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建一个配置驱动、支持异步 Addressables 缓存、BGM 交叉淡化、池化 2D/3D SFX 和三路 Mixer 音量的全局声音管理框架。

**Architecture:** `SoundManager` 继承现有 `SingletonManager` 并作为唯一公共入口，内部组合 `AudioClipCache`、`BgmPlayer`、`SfxPool` 和 `AudioVolumeController`。`SoundCatalog` 是声音定义的唯一真相来源，播放句柄隔离异步加载、池实例复用和业务调用方。

**Tech Stack:** Unity 2022.3.61f1c1、C# 9.0、Addressables 1.22.3、Unity AudioMixer、UGUI、TextMeshPro

---

## 实施约束

- 禁止修改任何 `.controller` 文件。
- 禁止新增测试文件或测试代码；本计划用 Unity 编译、静态检查和运行态验收替代 TDD 步骤。
- Unity 编译只能执行 `$CLI compile unity`，不能用 `compile dotnet` 替代。
- 所有新增或修改的函数都必须添加简体中文注释，说明用途或关键行为。
- 兼容 C# 9.0，不使用更高版本语法。
- 不为旧的 `AudioListener.volume` 行为保留兼容分支；设置面板直接迁移到新框架。
- 每次提交前执行 `git diff --name-only -- '*.controller'`，预期无输出。

## 文件结构

| 路径 | 操作 | 单一职责 |
| --- | --- | --- |
| `Assets/Framework/Audio/SoundTypes.cs` | 新建 | 声音 ID、分类、空间模式和播放状态枚举 |
| `Assets/Framework/Audio/SoundDefinition.cs` | 新建 | 单条声音的可序列化配置与约束校验 |
| `Assets/Framework/Audio/SoundCatalog.cs` | 新建 | 全局声音目录、Mixer 引用和 ID 索引 |
| `Assets/Framework/Audio/SoundPlaybackHandle.cs` | 新建 | 播放请求状态、实例序号和版本 |
| `Assets/Framework/Audio/AudioClipCache.cs` | 新建 | Addressables 音频加载、共享和引用释放 |
| `Assets/Framework/Audio/SfxPool.cs` | 新建 | SFX 播放源池、空间更新、淡出和回收 |
| `Assets/Framework/Audio/BgmPlayer.cs` | 新建 | 双播放源 BGM 播放与交叉淡化 |
| `Assets/Framework/Audio/AudioVolumeController.cs` | 新建 | Mixer 三路音量和 PlayerPrefs 持久化 |
| `Assets/Framework/Audio/SoundManager.cs` | 新建 | 自动初始化、公共 API、并发和场景生命周期 |
| `Assets/Res/Audio/SoundMixer.mixer` | 新建 | Master、BGM、SFX Mixer 分组和暴露参数 |
| `Assets/Res/Audio/SoundCatalog.asset` | 新建 | 运行时声音目录资产 |
| `Assets/AddressableAssetsData/AssetGroups/ConfigGroup.asset` | 修改 | 将目录注册为 `Data/SoundCatalog.asset` |
| `Assets/Game/UI/Panels/SettingsPanel.cs` | 修改 | 三路声音滑块接入框架 |
| `Assets/Res/Prefabs/UI/SettingsPanel.prefab` | 修改 | 主音量、音乐、音效三个滑块布局与引用 |

### Task 1: 建立强类型声音配置模型

**Files:**
- Create: `Assets/Framework/Audio/SoundTypes.cs`
- Create: `Assets/Framework/Audio/SoundDefinition.cs`
- Create: `Assets/Framework/Audio/SoundCatalog.cs`

- [ ] **Step 1: 创建声音枚举**

在 `SoundTypes.cs` 定义以下类型。`None` 只表示未配置值，不允许进入目录索引：

```csharp
namespace GameMain2.Framework.Audio
{
    public enum SoundId
    {
        None = 0,
        MainMenuBgm = 1,
        BattleBgm = 2,
        UiClick = 1000
    }

    public enum SoundCategory
    {
        Bgm = 0,
        Sfx = 1
    }

    public enum SoundSpatialMode
    {
        TwoDimensional = 0,
        WorldPosition = 1,
        FollowTarget = 2
    }

    public enum SoundPlaybackState
    {
        Loading = 0,
        Playing = 1,
        FadingOut = 2,
        Completed = 3,
        Canceled = 4,
        Failed = 5
    }
}
```

- [ ] **Step 2: 创建单条声音定义**

`SoundDefinition` 使用私有序列化字段和只读属性，字段固定为：`id`、`category`、`clip`、`baseVolume`、`pitchRange`、`maxConcurrent`、`minDistance`、`maxDistance`、`rolloffMode`。实现 `Validate()`，按以下顺序抛出 `InvalidOperationException`：

1. `id == SoundId.None`。
2. `clip == null` 或 `!clip.RuntimeKeyIsValid()`。
3. `baseVolume` 不在 `[0, 1]`。
4. 音调最小值不大于零，或最大值小于最小值。
5. SFX 的 `maxConcurrent <= 0`。
6. SFX 的最小距离不大于零，或最大距离小于最小距离。

关键接口必须与后续任务保持一致：

```csharp
public SoundId Id { get; }
public SoundCategory Category { get; }
public AssetReferenceT<AudioClip> Clip { get; }
public float BaseVolume { get; }
public Vector2 PitchRange { get; }
public int MaxConcurrent { get; }
public float MinDistance { get; }
public float MaxDistance { get; }
public AudioRolloffMode RolloffMode { get; }
public void Validate();
```

- [ ] **Step 3: 创建声音目录**

`SoundCatalog` 添加 `[CreateAssetMenu(fileName = "SoundCatalog", menuName = "Game/Audio/Sound Catalog")]`，序列化以下字段：

```csharp
[SerializeField] private AudioMixer audioMixer;
[SerializeField] private AudioMixerGroup bgmGroup;
[SerializeField] private AudioMixerGroup sfxGroup;
[SerializeField, Min(0f)] private float defaultBgmFadeSeconds = 1f;
[SerializeField, Min(1)] private int initialSfxPoolSize = 16;
[SerializeField] private List<SoundDefinition> sounds = new List<SoundDefinition>();
```

实现 `BuildLookup()`：先校验 Mixer、两个 Group、淡化时长和池容量，再逐项调用 `SoundDefinition.Validate()`，使用 `Dictionary.Add` 让重复 ID 直接失败，最后返回 `IReadOnlyDictionary<SoundId, SoundDefinition>`。不要缓存第二份可变列表。

- [ ] **Step 4: 执行首次编译检查**

Run: `$CLI compile unity`

Expected: Unity 编译完成，新增三个文件无错误；不要求目录资产已经存在。

- [ ] **Step 5: 提交配置模型**

```powershell
git diff --name-only -- '*.controller'
git add Assets/Framework/Audio/SoundTypes.cs Assets/Framework/Audio/SoundDefinition.cs Assets/Framework/Audio/SoundCatalog.cs Assets/Framework/Audio/*.meta
git commit -m "建立声音配置模型"
```

### Task 2: 实现播放句柄和音频缓存

**Files:**
- Create: `Assets/Framework/Audio/SoundPlaybackHandle.cs`
- Create: `Assets/Framework/Audio/AudioClipCache.cs`

- [ ] **Step 1: 实现强类型播放句柄**

`SoundPlaybackHandle` 使用引用类型，确保异步流程和调用方观察同一个状态。公共面只暴露只读属性，状态迁移方法保持 `internal`：

```csharp
public sealed class SoundPlaybackHandle
{
    public long RequestId { get; }
    public SoundId SoundId { get; }
    public SoundCategory Category { get; }
    public SoundPlaybackState State { get; private set; }
    internal int InstanceId { get; private set; }
    internal uint Version { get; private set; }
    internal bool IsTerminal => State == SoundPlaybackState.Completed
                                || State == SoundPlaybackState.Canceled
                                || State == SoundPlaybackState.Failed;

    internal SoundPlaybackHandle(long requestId, SoundId soundId, SoundCategory category);
    internal void BindInstance(int instanceId, uint version);
    internal void MarkPlaying();
    internal void MarkFadingOut();
    internal void MarkCompleted();
    internal void MarkCanceled();
    internal void MarkFailed();
}
```

每个迁移函数只允许设计文档中的合法迁移；非法迁移抛出 `InvalidOperationException`。`Loading -> Canceled/Failed/Playing`，`Playing -> FadingOut/Completed`，`FadingOut -> Completed`。

- [ ] **Step 2: 实现带引用状态的 AudioClipLease**

在 `AudioClipCache.cs` 内定义 `internal sealed class AudioClipLease : IDisposable`。租约初始计入待播放引用；`PromoteToActive()` 将待播放引用转为活跃引用；`Dispose()` 根据当前阶段只释放一次对应引用。公开给内部调用方的成员固定为：

```csharp
public AudioClip Clip { get; }
public void PromoteToActive();
public void Dispose();
```

- [ ] **Step 3: 实现共享 Addressables 缓存**

`AudioClipCache` 用 `Dictionary<string, CacheEntry>`，键为 `SoundDefinition.Clip.AssetGUID`。实现：

```csharp
public async Task<AudioClipLease> AcquireAsync(SoundDefinition definition);
public void ReleaseUnused();
public void ReleaseAll();
```

`AcquireAsync` 的固定算法：

1. 首次地址创建 `Addressables.LoadAssetAsync<AudioClip>(definition.Clip)` 句柄。
2. 每个调用在 `await` 前增加待播放引用。
3. 共享同一个 `AsyncOperationHandle<AudioClip>.Task`。
4. 失败时减少本次待播放引用，释放没有其他引用的失败句柄，再抛出 `OperationException`。
5. 成功时返回新的 `AudioClipLease`，不复制 `AudioClip` 或加载句柄。

`ReleaseUnused` 只释放已完成且待播放、活跃引用都为零的条目。`ReleaseAll` 仅供 `SoundManager.OnDestroy` 使用，调用前必须先清理全部播放器。

- [ ] **Step 4: 编译并检查 Addressables 泛型句柄类型**

Run: `$CLI compile unity`

Expected: `SoundPlaybackHandle`、`AudioClipLease`、`AudioClipCache` 编译通过，没有未观察的泛型转换错误。

- [ ] **Step 5: 提交句柄和缓存**

```powershell
git diff --name-only -- '*.controller'
git add Assets/Framework/Audio/SoundPlaybackHandle.cs Assets/Framework/Audio/AudioClipCache.cs Assets/Framework/Audio/*.meta
git commit -m "实现声音播放句柄与音频缓存"
```

### Task 3: 实现池化 SFX 播放器

**Files:**
- Create: `Assets/Framework/Audio/SfxPool.cs`

- [ ] **Step 1: 建立池实例状态**

在 `SfxPool` 内定义私有 `SfxVoice`，字段固定为：`Id`、`Version`、`AudioSource`、`Handle`、`Lease`、`Mode`、`FollowTarget`、`FadeStartVolume`、`FadeDuration`、`FadeElapsed`。`Version` 每次取出时递增，回收后不归零。

构造函数接收宿主 `Transform`、SFX `AudioMixerGroup`、初始容量和 `Action<SoundPlaybackHandle>` 回收回调，并预创建禁用的子对象。所有 `AudioSource` 设置 `playOnAwake = false`、`loop = false`、`outputAudioMixerGroup = sfxGroup`。

- [ ] **Step 2: 实现三种播放模式**

实现唯一内部播放入口：

```csharp
public void Play(
    SoundPlaybackHandle handle,
    SoundDefinition definition,
    AudioClipLease lease,
    SoundSpatialMode mode,
    Vector3 position,
    Transform followTarget);
```

固定规则：

- 2D：`spatialBlend = 0f`，位置归零，不保存跟随目标。
- 固定位置 3D：`spatialBlend = 1f`，使用传入世界坐标。
- 跟随对象 3D：`spatialBlend = 1f`，开始前读取目标位置，之后每帧同步。
- 音调使用 `UnityEngine.Random.Range(definition.PitchRange.x, definition.PitchRange.y)`。
- 调用 `lease.PromoteToActive()` 后再 `AudioSource.Play()`，然后绑定实例 ID/版本并把句柄切换为 `Playing`。

- [ ] **Step 3: 实现更新、淡出和回收**

实现以下函数：

```csharp
public void Tick(float unscaledDeltaTime);
public bool Stop(SoundPlaybackHandle handle, float fadeSeconds);
public int StopAll(SoundId id, float fadeSeconds);
public void StopAllImmediate();
```

`Tick` 依次处理跟随目标、淡出和自然结束。跟随目标为 Unity 空引用时立即正常完成。淡出按进入淡出时的实际音量线性插值到零。回收时执行：停止、清空 Clip、释放租约、清空句柄和跟随目标、恢复 Transform/音量/音调/空间参数、禁用对象、句柄进入 `Completed`、触发一次并发回调。

`Stop` 必须同时匹配实例 ID 和版本，旧句柄不能停止复用后的实例。`fadeSeconds == 0` 立即回收；正数进入 `FadingOut`。

- [ ] **Step 4: 编译并提交 SFX 池**

Run: `$CLI compile unity`

Expected: Unity 编译零错误。

```powershell
git diff --name-only -- '*.controller'
git add Assets/Framework/Audio/SfxPool.cs Assets/Framework/Audio/SfxPool.cs.meta
git commit -m "实现池化音效播放器"
```

### Task 4: 实现双播放源 BGM 播放器

**Files:**
- Create: `Assets/Framework/Audio/BgmPlayer.cs`

- [ ] **Step 1: 创建两个 BGM 槽位**

`BgmPlayer` 构造时在宿主对象上创建两个 `AudioSource`，两者设置 `playOnAwake = false`、`loop = true`、`spatialBlend = 0f` 并路由到 BGM Mixer Group。每个槽位保存实例 ID、递增版本、`AudioSource`、句柄、租约、起始音量、目标音量和淡化进度。

- [ ] **Step 2: 实现交叉淡化**

实现：

```csharp
public SoundPlaybackHandle CurrentHandle { get; }
public bool IsCurrent(SoundId id);
public void CrossfadeTo(SoundPlaybackHandle handle, SoundDefinition definition, AudioClipLease lease, float fadeSeconds);
public bool Stop(SoundPlaybackHandle handle, float fadeSeconds);
public int StopAll(SoundId id, float fadeSeconds);
public void Tick(float unscaledDeltaTime);
public void Dispose();
```

`CrossfadeTo` 固定执行以下顺序：

1. 如果已有交叉淡化，保留最新 BGM 槽位作为旧曲，立即释放更老的槽位。
2. 在空闲槽位设置新 Clip，将租约转为活跃引用并从零音量开始播放。
3. 递增新槽位版本并调用 `handle.BindInstance`，后续停止必须同时匹配实例 ID 和版本。
4. 新槽位目标音量为 `definition.BaseVolume`，旧槽位目标音量为零。
5. 淡化时长为零时一次完成，否则使用 `unscaledDeltaTime` 同步更新两个槽位。
6. 旧槽位到零后停止并释放租约，旧句柄进入 `Completed`；新句柄进入 `Playing`。

重复当前 ID 的判断由 `SoundManager` 在加载前完成，`BgmPlayer` 不建立第二套去重规则。

- [ ] **Step 3: 编译并提交 BGM 播放器**

Run: `$CLI compile unity`

Expected: Unity 编译零错误。

```powershell
git diff --name-only -- '*.controller'
git add Assets/Framework/Audio/BgmPlayer.cs Assets/Framework/Audio/BgmPlayer.cs.meta
git commit -m "实现背景音乐交叉淡化"
```

### Task 5: 实现三路 Mixer 音量控制

**Files:**
- Create: `Assets/Framework/Audio/AudioVolumeController.cs`

- [ ] **Step 1: 实现持久化键和值域**

定义唯一键：`Audio_MasterVolume`、`Audio_BgmVolume`、`Audio_SfxVolume`。构造函数从 `PlayerPrefs` 读取三路值，默认值均为 `1f`。公共属性固定为：

```csharp
public float MasterVolume { get; }
public float BgmVolume { get; }
public float SfxVolume { get; }
```

- [ ] **Step 2: 实现 Mixer 绑定和写入**

实现：

```csharp
public void AttachMixer(AudioMixer mixer);
public void SetMasterVolume(float value);
public void SetBgmVolume(float value);
public void SetSfxVolume(float value);
```

写入前要求值位于 `[0, 1]`，越界抛出 `ArgumentOutOfRangeException`。分贝转换固定为：`value == 0f ? -80f : Mathf.Log10(value) * 20f`。`AttachMixer` 立即应用三路已保存值；每个 Set 函数更新字段、Mixer 参数、对应 `PlayerPrefs`，并调用一次 `PlayerPrefs.Save()`。

- [ ] **Step 3: 编译并提交音量控制**

Run: `$CLI compile unity`

Expected: Unity 编译零错误。

```powershell
git diff --name-only -- '*.controller'
git add Assets/Framework/Audio/AudioVolumeController.cs Assets/Framework/Audio/AudioVolumeController.cs.meta
git commit -m "实现声音混音音量控制"
```

### Task 6: 组装 SoundManager 公共入口

**Files:**
- Create: `Assets/Framework/Audio/SoundManager.cs`

- [ ] **Step 1: 实现自动启动和目录初始化**

常量地址固定为：

```csharp
private const string SoundCatalogAddress = "Data/SoundCatalog.asset";
```

添加 `RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)` 静态入口访问 `Instance`。`Awake()` 调用基类后，只由真实单例创建 `AudioVolumeController`、订阅 `SceneManager.activeSceneChanged` 并启动 `InitializeAsync()`。

`InitializeAsync()` 使用 `Addressables.LoadAssetAsync<SoundCatalog>`，保留目录句柄，调用 `BuildLookup()`，绑定 Mixer，创建 `BgmPlayer` 和 `SfxPool`。初始化异常保存到字段并 `Debug.LogException`；所有后续播放请求使用同一个初始化任务并进入 `Failed`。

- [ ] **Step 2: 实现公共 API**

签名必须与设计一致：

```csharp
public SoundPlaybackHandle PlayBgm(SoundId id, float fadeSeconds);
public SoundPlaybackHandle PlaySfx2D(SoundId id);
public SoundPlaybackHandle PlaySfxAt(SoundId id, Vector3 position);
public SoundPlaybackHandle PlaySfxFollow(SoundId id, Transform target);
public void Stop(SoundPlaybackHandle handle, float fadeSeconds = 0f);
public int StopAll(SoundId id, float fadeSeconds = 0f);
public SoundPlaybackState GetState(SoundPlaybackHandle handle);
public void ReleaseUnusedClips();
public float MasterVolume { get; }
public float BgmVolume { get; }
public float SfxVolume { get; }
public void SetMasterVolume(float value);
public void SetBgmVolume(float value);
public void SetSfxVolume(float value);
```

播放函数同步创建 `Loading` 句柄并启动内部异步流程。请求 ID 使用单调递增 `long`。`GetState(null)`、负淡化时间和跟随播放的空目标直接抛出参数异常。

- [ ] **Step 3: 实现 SFX 异步播放和并发计数**

使用 `Dictionary<SoundId, int>` 保存“加载中 + 播放中”总数，使用 `HashSet<SoundPlaybackHandle>` 分别保存加载中请求和已占用并发名额的句柄。内部流程固定为：等待初始化、检查终态、解析并校验 SFX 类型、检查并占用并发名额、获取租约、再次检查终态、交给 `SfxPool.Play`。任何异常都释放已经占用的名额和租约，句柄进入 `Failed` 并记录 ID、资源 GUID 和原始异常。

集中实现 `ReleaseSfxReservation(SoundPlaybackHandle handle)`：只有句柄确实存在于并发名额集合时才按声音 ID 将计数减一，减到零时删除字典项。加载中主动取消、加载失败、池实例回收和场景切换都调用这一入口，保证同一名额最多释放一次。

- [ ] **Step 4: 实现 BGM 最新请求规则**

只保存一个 `pendingBgmHandle`。新请求执行：

1. 当前 BGM ID 相同则返回当前句柄。
2. 取消旧的加载中 BGM 句柄。
3. 等待初始化并验证 BGM 类型。
4. 获取租约；如果期间被替换则释放租约并保持 `Canceled`。
5. 只有最新请求才能调用 `BgmPlayer.CrossfadeTo`。
6. 加载失败时保持当前 BGM 不变，并把新句柄置为 `Failed`。

- [ ] **Step 5: 实现场景和销毁生命周期**

`Update()` 使用 `Time.unscaledDeltaTime` 驱动两个播放器。`OnActiveSceneChanged` 取消全部加载中 SFX、立即释放对应并发名额，再调用 `SfxPool.StopAllImmediate()`；池回调释放活跃实例名额。场景清理不停止 BGM、不释放缓存，结束后并发字典必须为空。

`OnDestroy()` 仅对真实单例执行：退订场景事件、取消待播放请求、释放 BGM/SFX、释放缓存、释放目录 Addressables 句柄，再调用基类。不要修改现有 `SingletonManager`。

- [ ] **Step 6: 编译并提交管理器**

Run: `$CLI compile unity`

Expected: 全部运行时代码编译通过，没有 `async void` 未捕获异常；只有 Unity 生命周期入口允许 `async void`，且其内部显式记录异常。

```powershell
git diff --name-only -- '*.controller'
git add Assets/Framework/Audio/SoundManager.cs Assets/Framework/Audio/SoundManager.cs.meta
git commit -m "组装全局声音管理入口"
```

### Task 7: 创建 Mixer、目录资产和 Addressables 条目

**Files:**
- Create: `Assets/Res/Audio/SoundMixer.mixer`
- Create: `Assets/Res/Audio/SoundCatalog.asset`
- Modify: `Assets/AddressableAssetsData/AssetGroups/ConfigGroup.asset`

- [ ] **Step 1: 创建 SoundMixer**

通过 Unity Editor 创建 `Assets/Res/Audio/SoundMixer.mixer`。根组命名为 `Master`，建立直属子组 `BGM` 和 `SFX`。分别暴露三组 Attenuation Volume，暴露参数精确命名为 `MasterVolume`、`BgmVolume`、`SfxVolume`。不增加 Snapshot 或其他 Effect。

- [ ] **Step 2: 创建空 SoundCatalog**

通过 `Game/Audio/Sound Catalog` 创建 `Assets/Res/Audio/SoundCatalog.asset`，设置：

- `Audio Mixer`：`SoundMixer`
- `Bgm Group`：`Master/BGM`
- `Sfx Group`：`Master/SFX`
- `Default Bgm Fade Seconds`：`1`
- `Initial Sfx Pool Size`：`16`
- `Sounds`：空数组

- [ ] **Step 3: 注册 Addressables 目录**

把 `SoundCatalog.asset` 加入现有 `ConfigGroup`，地址精确设置为 `Data/SoundCatalog.asset`。`SoundMixer.mixer` 作为目录依赖自动打包，不单独建立第二个地址。确认 `ConfigGroup.asset` 只有新增目录条目，没有重写其他地址。

- [ ] **Step 4: 导入、编译并提交资产**

Run: `$CLI compile unity`

Expected: Unity 导入 Mixer 和目录成功；控制台没有重复 Addressables 地址或缺失 Mixer Group 错误。

```powershell
git diff --name-only -- '*.controller'
git add Assets/Res/Audio Assets/AddressableAssetsData/AssetGroups/ConfigGroup.asset
git commit -m "配置声音混音器与声音目录"
```

### Task 8: 将设置面板迁移到三路音量

**Files:**
- Modify: `Assets/Game/UI/Panels/SettingsPanel.cs`
- Modify: `Assets/Res/Prefabs/UI/SettingsPanel.prefab`

- [ ] **Step 1: 替换脚本字段和旧启动逻辑**

删除 `VolumeKey`、`volumeSlider` 和对 `AudioListener.volume` 的全部读写。新增：

```csharp
[SerializeField] private Slider masterVolumeSlider;
[SerializeField] private Slider bgmVolumeSlider;
[SerializeField] private Slider sfxVolumeSlider;
```

将静态启动函数改名为 `ApplySavedDisplaySettingsOnStart`，只保留全屏设置恢复。

- [ ] **Step 2: 更新刷新和事件绑定**

`RefreshValues()` 从 `SoundManager.Instance` 读取三个属性。`BindControls()` 和 `UnbindControls()` 分别绑定或解绑：

```csharp
private void OnMasterVolumeChanged(float value);
private void OnBgmVolumeChanged(float value);
private void OnSfxVolumeChanged(float value);
```

三个回调只调用对应 `SoundManager` Set 函数。全屏和关闭按钮行为保持原样。

- [ ] **Step 3: 更新运行时默认布局**

`EnsureDefaultView()` 将卡片高度调整为 `560`，创建三个标签和滑块：

| 标签 | Slider 名称 | Y 坐标 |
| --- | --- | --- |
| 主音量 | `MasterVolumeSlider` | `-140` |
| 音乐 | `BgmVolumeSlider` | `-205` |
| 音效 | `SfxVolumeSlider` | `-270` |

全屏 Toggle 移到 `-340`，关闭按钮保持距卡片底部 `68`。`CacheControls()` 使用以上三个稳定路径查找 Slider。

- [ ] **Step 4: 同步修改 SettingsPanel 预制体**

在 `SettingsCard` 下把旧 `VolumeSlider` 重命名为 `MasterVolumeSlider`，把旧标签保持为“主音量”；复制两组标签和 Slider，命名及坐标与上表完全一致。卡片、全屏 Toggle 和关闭按钮尺寸位置与运行时默认布局一致。把根 `SettingsPanel` 组件的三个序列化字段分别绑定到对应 Slider。

不得删除后重建整个预制体，不得改变根对象、Overlay、全屏 Toggle、关闭按钮的现有组件引用。

- [ ] **Step 5: 编译并提交设置面板**

Run: `$CLI compile unity`

Expected: Unity 编译零错误，预制体没有 Missing Script 或空 Slider 引用。

```powershell
git diff --name-only -- '*.controller'
git add Assets/Game/UI/Panels/SettingsPanel.cs Assets/Res/Prefabs/UI/SettingsPanel.prefab
git commit -m "接入设置面板三路声音音量"
```

### Task 9: 完成静态和运行态验收

**Files:**
- Verify only; no test files

- [ ] **Step 1: 执行最终 Unity 编译**

Run: `$CLI compile unity`

Expected: Unity 编译零错误。

- [ ] **Step 2: 检查禁止项和工作区范围**

```powershell
git diff a2460ec..HEAD --name-only -- '*.controller'
git status --short
```

Expected: 第一条命令无输出；第二条只显示本计划范围内的预期文件，或工作区干净。

- [ ] **Step 3: 临时配置运行态验收资源**

仅在本地临时把以下资源设为 Addressable 并加入 `SoundCatalog`，验收完成后撤销这些目录条目，不提交临时配置：

- `MainMenuBgm`：`Assets/Res22/Flooded_Grounds/Content/Sounds/Background.mp3`
- `BattleBgm`：`Assets/Res22/Flooded_Grounds/Content/Sounds/Wind.mp3`
- SFX：`Assets/Res22/Audio/FootStep/Light Armor Grass Running 1_01.wav`

SFX 条目使用现有 `UiClick` ID。通过 AIBridge 的运行时代码调用入口驱动公共 API，不创建测试脚本、测试场景或临时 MonoBehaviour。

- [ ] **Step 4: 执行运行态检查清单**

在 Play Mode 逐项确认：

1. 首次请求立即返回 `Loading`，资源就绪后进入 `Playing`。
2. 加载期间停止请求进入 `Canceled`，资源不会开始播放。
3. 同一 SFX 的并发数量达到配置上限后，新请求进入 `Failed`，已有实例不被抢占。
4. 2D、固定位置 3D、跟随对象 3D 均能播放；销毁跟随目标后实例回收。
5. BGM 循环播放，切换时交叉淡化，重复当前 ID 不重播。
6. 连续请求多个 BGM 时只有最新请求生效。
7. 活动场景变化后 BGM 保留，所有 SFX 停止并回收。
8. Master、BGM、SFX 滑块只影响对应 Mixer 路径，重进 Play Mode 后恢复。
9. `ReleaseUnusedClips` 不释放播放中资源，播放结束后能够释放。

- [ ] **Step 5: 清理临时验收数据并复编译**

删除三个临时目录条目，撤销三个临时音频的 Addressables 标记，再执行：

Run: `$CLI compile unity`

Expected: Unity 编译零错误，`SoundCatalog` 恢复为空数组，ConfigGroup 不包含两个临时音频。

- [ ] **Step 6: 提交验收中产生的必要修正**

只有运行态验收确实产生代码或资产修正时执行：

```powershell
git diff --name-only -- '*.controller'
git add Assets/Framework/Audio Assets/Res/Audio Assets/Game/UI/Panels/SettingsPanel.cs Assets/Res/Prefabs/UI/SettingsPanel.prefab Assets/AddressableAssetsData/AssetGroups/ConfigGroup.asset
git commit -m "修正声音框架运行态问题"
```

最终 `git status --short` 应为空，不保留临时音频配置、测试脚本或测试场景。
