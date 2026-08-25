# 声音管理框架设计

## 1. 背景

项目当前没有统一的声音管理模块，业务代码中也未形成稳定的 `AudioSource` 播放入口。现有设置面板只通过 `AudioListener.volume` 保存一个总音量，无法分别控制背景音乐和音效。

项目已有以下可复用基础设施：

- `SingletonManager<T>` 负责跨场景单例生命周期。
- Addressables 负责资源加载。
- `PlayerPrefs` 负责当前设置持久化。
- 设置面板已经具备运行时创建和绑定控件的结构。

本设计建立一个全局声音管理框架，首版只覆盖背景音乐（BGM）和音效（SFX）。

## 2. 目标

- 提供统一且强类型的 BGM、2D SFX、固定位置 3D SFX、跟随对象 3D SFX 播放入口。
- BGM 支持循环播放和双播放源交叉淡入淡出。
- SFX 使用 `AudioSource` 对象池，并按声音 ID 限制并发数量。
- 使用配置资产集中维护声音 ID、Addressables 引用和播放参数。
- 首次播放时异步加载音频并缓存；调用方立即获得可取消的播放句柄。
- 使用 `AudioMixer` 分别控制 Master、BGM、SFX 音量并持久化。
- 场景切换时保留 BGM，停止并回收全部 SFX。
- 设置面板接入三路音量，不再直接操作 `AudioListener.volume`。

## 3. 非目标

- 不支持语音、环境音、歌单、随机播放或队列播放。
- 不支持 Mixer Snapshot、对话压低背景音乐或动态混音状态。
- 不接入现有战斗、角色或 UI 的具体音效触发点。
- 不提供音频波形预览、批量导入或独立编辑器窗口。
- 不新增测试文件或测试代码。
- 不修改任何 `.controller` 文件。

## 4. 总体架构

### 4.1 SoundManager

`SoundManager` 继承 `SingletonManager<SoundManager>`，在首个场景加载前自动创建并标记为 `DontDestroyOnLoad`。它是业务层唯一入口，只负责编排各内部组件，不直接承载全部资源、播放和混音逻辑。

职责包括：

- 初始化声音目录、BGM 播放器、SFX 对象池、缓存和音量控制器。
- 暴露稳定的播放、停止、状态查询、音量和缓存清理接口。
- 监听场景切换并清理全部 SFX。
- 驱动加载中请求、淡化和跟随目标实例的运行时更新。

所有公共和私有函数均按项目约束添加简体中文用途注释。

### 4.2 SoundCatalog

`SoundCatalog` 是声音定义的唯一真相来源，使用 `ScriptableObject` 保存全局配置和声音条目。

全局字段：

- `AudioMixer` 引用。
- BGM 和 SFX 的 `AudioMixerGroup` 直接引用。
- 默认 BGM 交叉淡化时长。
- SFX 初始对象池容量。

目录资产使用固定 Addressables 地址 `Data/SoundCatalog.asset`。`SoundManager` 初始化时通过该唯一地址加载目录，运行期间不释放目录资产。

每个 `SoundDefinition` 包含：

- `SoundId`：唯一的强类型枚举值。
- `SoundCategory`：`Bgm` 或 `Sfx`。
- `AssetReferenceT<AudioClip>`：音频 Addressables 引用。
- 基础音量。
- 最小和最大音调，固定音调时两者相等。
- SFX 最大并发数。
- 3D 最小距离、最大距离和衰减模式。

目录初始化时建立只读字典。重复 ID、空资源引用、非正数并发上限、非法音量或距离范围均属于配置错误，直接终止声音框架初始化并输出明确错误。

### 4.3 BgmPlayer

`BgmPlayer` 独占两个路由到 BGM Mixer Group 的 `AudioSource`。切换音乐时，新播放源从静音淡入，旧播放源同步淡出，完成后停止并清空旧播放源。

行为约束：

- BGM 默认循环。
- 重复请求当前 BGM 时不重新开始，也不创建新句柄。
- 新的 BGM 请求会取消尚未开始播放的旧 BGM 请求，当前正在播放的音乐保持到最新请求加载完成。
- 停止 BGM 可立即停止或按指定时长淡出。
- 新的切换请求会以当前实际音量为起点重新建立淡化过程，不叠加多个淡化任务。

### 4.4 SfxPool

`SfxPool` 独占 SFX `AudioSource` 实例。对象池按目录配置预创建，容量不足时按需扩展；声音 ID 的并发上限是扩展前置条件，达到上限时拒绝新请求，不抢占已有实例。

每个池实例记录当前句柄、声音 ID、播放模式和可选跟随目标。支持：

- 2D 播放：`spatialBlend` 为 `0`。
- 固定位置 3D 播放：实例放置到指定世界坐标。
- 跟随对象 3D 播放：播放期间同步目标位置，目标销毁后正常结束并回收。

回收时必须停止播放、清空 `AudioClip`、解除跟随目标、恢复默认 Transform 和音频参数，再返回空闲队列。

### 4.5 AudioClipCache

`AudioClipCache` 独占 Addressables 加载句柄。一个音频地址只有一份缓存记录，同一资源的并发加载请求共享同一个异步操作。

缓存记录跟踪：

- Addressables 加载句柄。
- 加载状态和失败异常。
- 待播放请求引用数。
- 活跃播放实例引用数。

`ReleaseUnusedClips` 只释放没有加载任务、待播放请求或活跃播放实例的资源。跨场景切换不会自动释放缓存。

### 4.6 SoundPlaybackHandle

播放句柄由声音 ID、实例序号和版本组成，避免池实例复用后旧句柄控制到新播放。句柄不直接暴露 `AudioSource`。

状态集合：

- `Loading`：等待目录或音频资源加载。
- `Playing`：正在播放。
- `FadingOut`：正在淡出。
- `Completed`：已经开始播放，之后自然结束或被停止。
- `Canceled`：在实际播放前被取消。
- `Failed`：配置或资源加载失败。

完成、取消和失败均为终态。

### 4.7 AudioVolumeController

`AudioVolumeController` 是用户音量的唯一写入入口，控制 `AudioMixer` 暴露参数：

- `MasterVolume`
- `BgmVolume`
- `SfxVolume`

外部统一使用 `0` 到 `1` 的线性值。控制器将正值转换为分贝，`0` 映射到 Mixer 静音下限。三路值分别写入 `PlayerPrefs`，框架初始化时恢复。

声音定义和单次播放参数只决定播放源自身音量；用户设置只由 Mixer 决定，禁止在播放源上重复乘入用户音量。

## 5. 公共接口

首版提供以下语义明确的入口：

```csharp
SoundPlaybackHandle PlayBgm(SoundId id, float fadeSeconds);
SoundPlaybackHandle PlaySfx2D(SoundId id);
SoundPlaybackHandle PlaySfxAt(SoundId id, Vector3 position);
SoundPlaybackHandle PlaySfxFollow(SoundId id, Transform target);
void Stop(SoundPlaybackHandle handle, float fadeSeconds = 0f);
int StopAll(SoundId id, float fadeSeconds = 0f);
SoundPlaybackState GetState(SoundPlaybackHandle handle);
void ReleaseUnusedClips();
```

音量接口分别提供 Master、BGM、SFX 的读取和写入。业务层不得直接访问框架创建的 `AudioSource`、Addressables 加载句柄或 Mixer 参数名。

`PlayBgm` 只接受 BGM 定义，三个 SFX 入口只接受 SFX 定义。类型不匹配不会隐式转换。

## 6. 播放数据流

### 6.1 SFX

1. 调用方提交声音 ID 和空间参数。
2. `SoundManager` 查询目录并检查声音类型和并发上限。
3. 创建 `Loading` 句柄并立刻占用该声音 ID 的并发名额。
4. `AudioClipCache` 获取或共享异步加载操作。
5. 加载期间调用 `Stop` 时，句柄进入 `Canceled`，释放并发名额和待播放引用。
6. 加载成功后从对象池获取实例，应用定义参数并开始播放，句柄进入 `Playing`。
7. 自然结束、主动停止、场景切换或跟随目标销毁后回收实例；已经开始播放的句柄进入 `Completed`。
8. 加载失败时释放并发名额，句柄进入 `Failed`。

加载中的请求计入并发数量，避免同一帧大量请求在资源就绪后同时突破上限。

### 6.2 BGM

1. 调用方提交 BGM ID 和淡化时长。
2. 框架立即返回 `Loading` 句柄并异步获取音频。
3. 加载完成后选择非当前播放源，设置新音频并开始淡入。
4. 当前播放源同时淡出；淡化完成后停止并释放其活跃引用。
5. 如果加载期间取消，新曲不播放，当前 BGM 不受影响。
6. 如果新曲加载失败，当前 BGM 继续播放，失败句柄进入 `Failed`。
7. 加载期间收到另一个 BGM 请求时，旧请求进入 `Canceled`，只处理最新请求。

## 7. 场景生命周期

`SoundManager` 在 `SceneManager.activeSceneChanged` 表示的活动场景变化时执行以下固定规则；仅加载附加场景但不改变活动场景时不触发清理：

- 保留当前 BGM、BGM 淡化状态和已缓存资源。
- 取消全部仍在加载的 SFX 请求。
- 停止并回收全部已开始播放的 SFX。
- 清空所有 SFX 并发计数和跟随目标引用。

场景切换不会隐式切换音乐。新场景需要新 BGM 时，由场景业务显式调用 `PlayBgm`。

## 8. 设置面板接入

现有单个总音量滑块替换为 Master、BGM、SFX 三个滑块。面板打开时从 `SoundManager` 读取当前值，值变化时调用对应设置接口。

删除设置面板中的以下旧职责：

- 直接读写 `AudioListener.volume`。
- 自行维护总音量 `PlayerPrefs` 键。
- 在程序集加载后应用音量的静态初始化方法。

全屏设置保持现状，不属于声音框架职责。

## 9. 错误处理

- 播放请求中的未知声音 ID、声音类型不匹配或无效淡化时长：立即返回 `Failed` 句柄并记录包含声音 ID 的错误。
- Addressables 加载失败：保留原始异常信息，记录声音 ID 和资源地址，不进行静默重试或替代资源降级。
- 目录配置非法：声音框架初始化失败，不使用部分可用目录继续运行。
- 已进入终态或版本不匹配的句柄再次停止：返回无操作结果，不影响池中复用后的其他播放。
- 缓存清理遇到仍被引用的音频：保留该记录，不中断其他资源的正常释放。

框架不吞没异常，也不通过隐式默认值掩盖错误配置。

## 10. 实现范围

首版实现包括：

- 声音 ID、类别、定义、目录和运行时状态类型。
- `SoundManager` 及其 BGM、SFX、缓存、句柄和音量组件。
- `AudioMixer` 及 Master、BGM、SFX 分组和暴露参数。
- 一份可供后续填充实际音频资源的 `SoundCatalog` 配置资产。
- 设置面板三路音量控制接入。

首版不修改具体战斗、角色或 UI 音效调用点，也不要求提供实际 BGM/SFX 音频内容。

## 11. 验收

按项目约束不新增测试文件或测试代码。实现完成后执行：

- 使用 `$CLI compile unity` 验证 Unity 编译。
- 确认没有修改任何 `.controller` 文件。
- 运行态验证首次异步加载、加载期间取消和重复资源共享加载。
- 运行态验证 BGM 循环、重复请求和交叉淡入淡出。
- 运行态验证 2D、固定位置 3D、跟随对象 3D SFX。
- 运行态验证按声音 ID 并发限制和 `AudioSource` 回收复用。
- 运行态验证场景切换保留 BGM 并清理全部 SFX。
- 运行态验证 Master、BGM、SFX 三路音量保存和恢复。
- 运行态验证只释放无引用缓存。

如当前配置资产没有实际音频内容，运行态验收使用临时配置完成，但不把临时音频或测试代码纳入提交。
