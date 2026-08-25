using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.Exceptions;
using UnityEngine.SceneManagement;

namespace GameMain2.Framework.Audio
{
    /// <summary>
    /// 组装声音目录、缓存与播放组件，提供全局声音播放入口。
    /// 声音目录和音频片段属于音频专用资源生命周期，保留独立 Addressables 句柄以支持缓存租约释放。
    /// </summary>
    public sealed class SoundManager : global::SingletonManager<SoundManager>
    {
        private const string SoundCatalogAddress = "Data/SoundCatalog.asset";
        private const float DefaultAmbientFadeSeconds = 1f;

        private readonly Dictionary<SoundId, int> sfxConcurrentCounts = new Dictionary<SoundId, int>();
        private readonly HashSet<SoundPlaybackHandle> loadingSfxHandles = new HashSet<SoundPlaybackHandle>();
        private readonly HashSet<SoundPlaybackHandle> loadingSpatialAmbientHandles = new HashSet<SoundPlaybackHandle>();
        private readonly HashSet<SoundPlaybackHandle> reservedSfxHandles = new HashSet<SoundPlaybackHandle>();
        private readonly AudioClipCache clipCache = new AudioClipCache();

        private IReadOnlyDictionary<SoundId, SoundDefinition> soundLookup;
        private AudioVolumeController volumeController;
        private BgmPlayer bgmPlayer;
        private BgmPlayer ambientPlayer;
        private SpatialAmbientPool spatialAmbientPool;
        private SfxPool sfxPool;
        private SoundPlaybackHandle pendingBgmHandle;
        private SoundPlaybackHandle pendingAmbientHandle;
        private Task initializeTask;
        private Exception initializationException;
        private float defaultBgmFadeSeconds;
        private AsyncOperationHandle<SoundCatalog> catalogHandle;
        private bool hasCatalogHandle;
        private bool isDestroyed;
        private long nextRequestId;

        /// <summary>
        /// 在首个场景加载前确保声音管理器被创建。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeBeforeSceneLoad()
        {
            SoundManager ignored = Instance;
        }

        /// <summary>
        /// 获取主混音通道的线性音量。
        /// </summary>
        public float MasterVolume => volumeController.MasterVolume;

        /// <summary>
        /// 获取背景音乐混音通道的线性音量。
        /// </summary>
        public float BgmVolume => volumeController.BgmVolume;

        /// <summary>
        /// 获取音效混音通道的线性音量。
        /// </summary>
        public float SfxVolume => volumeController.SfxVolume;

        /// <summary>
        /// 获取环境音混音通道的线性音量。
        /// </summary>
        public float AmbientVolume => volumeController.AmbientVolume;

        /// <summary>
        /// 初始化真实单例的音量控制、场景通知与异步目录加载。
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            if (!IsSingletonInstance)
            {
                return;
            }

            EnsureRuntimeInitialized();
        }

        /// <summary>确保声音管理器的混音控制、场景订阅和声音目录加载任务已经启动。</summary>
        private void EnsureRuntimeInitialized()
        {
            if (initializeTask != null)
            {
                return;
            }

            volumeController = new AudioVolumeController();
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            initializeTask = InitializeAsync();
        }

        /// <summary>
        /// 使用目录配置初始化音量控制和三个播放组件。
        /// </summary>
        private async Task InitializeAsync()
        {
            try
            {
                catalogHandle = Addressables.LoadAssetAsync<SoundCatalog>(SoundCatalogAddress);
                hasCatalogHandle = true;
                SoundCatalog catalog = await catalogHandle.Task;
                if (catalogHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    throw new OperationException("加载声音目录失败。", catalogHandle.OperationException);
                }

                if (isDestroyed)
                {
                    return;
                }

                soundLookup = catalog.BuildLookup();
                defaultBgmFadeSeconds = catalog.DefaultBgmFadeSeconds;
                volumeController.AttachMixer(catalog.AudioMixer);
                bgmPlayer = new BgmPlayer(transform, catalog.BgmGroup);
                ambientPlayer = new BgmPlayer(transform, catalog.AmbientGroup, "AmbientVoice");
                spatialAmbientPool = new SpatialAmbientPool(transform, catalog.AmbientGroup, catalog.InitialSfxPoolSize);
                sfxPool = new SfxPool(transform, catalog.SfxGroup, catalog.InitialSfxPoolSize, ReleaseSfxReservation);
            }
            catch (Exception exception)
            {
                initializationException = exception;
                Debug.LogException(exception);
            }
        }

        /// <summary>
        /// 请求播放背景音乐并返回可观察的播放句柄。
        /// </summary>
        public SoundPlaybackHandle PlayBgm(SoundId id, float fadeSeconds)
        {
            EnsureRuntimeInitialized();
            ValidateFadeSeconds(fadeSeconds);
            ValidateDefaultBgmFadeSeconds();
            if (bgmPlayer != null && bgmPlayer.IsCurrent(id))
            {
                return bgmPlayer.CurrentHandle;
            }

            if (pendingBgmHandle != null && pendingBgmHandle.State == SoundPlaybackState.Loading)
            {
                if (pendingBgmHandle.SoundId == id)
                {
                    return pendingBgmHandle;
                }

                pendingBgmHandle.MarkCanceled();
            }

            SoundPlaybackHandle handle = CreateHandle(id, SoundCategory.Bgm);
            pendingBgmHandle = handle;
            _ = PlayBgmAsync(handle, fadeSeconds);
            return handle;
        }

        /// <summary>判断指定 BGM 是否已经处于播放中或等待播放中，用于外部避免重复请求。</summary>
        public bool IsBgmActive(SoundId id)
        {
            if (bgmPlayer != null && bgmPlayer.IsCurrent(id))
            {
                return true;
            }

            return pendingBgmHandle != null
                && pendingBgmHandle.SoundId == id
                && pendingBgmHandle.State == SoundPlaybackState.Loading;
        }

        /// <summary>使用默认淡入时长播放环境音，环境音使用独立循环通道，不占用 BGM。</summary>
        public SoundPlaybackHandle PlayAmbient(SoundId id)
        {
            return PlayAmbient(id, DefaultAmbientFadeSeconds);
        }

        /// <summary>请求播放环境音并返回可观察的播放句柄，环境音使用独立循环通道，不占用 BGM。</summary>
        public SoundPlaybackHandle PlayAmbient(SoundId id, float fadeSeconds)
        {
            EnsureRuntimeInitialized();
            ValidateFadeSeconds(fadeSeconds);
            if (ambientPlayer != null && ambientPlayer.IsCurrent(id))
            {
                return ambientPlayer.CurrentHandle;
            }

            if (pendingAmbientHandle != null && pendingAmbientHandle.State == SoundPlaybackState.Loading)
            {
                if (pendingAmbientHandle.SoundId == id)
                {
                    return pendingAmbientHandle;
                }

                pendingAmbientHandle.MarkCanceled();
            }

            SoundPlaybackHandle handle = CreateHandle(id, SoundCategory.Ambient);
            pendingAmbientHandle = handle;
            _ = PlayAmbientAsync(handle, fadeSeconds);
            return handle;
        }

        /// <summary>停止当前环境音播放或加载请求。</summary>
        public void StopAmbient(float fadeSeconds = 0f)
        {
            ValidateFadeSeconds(fadeSeconds);
            if (pendingAmbientHandle != null && pendingAmbientHandle.State == SoundPlaybackState.Loading)
            {
                pendingAmbientHandle.MarkCanceled();
                pendingAmbientHandle = null;
            }

            SoundPlaybackHandle currentHandle = ambientPlayer == null ? null : ambientPlayer.CurrentHandle;
            if (currentHandle != null && !currentHandle.IsTerminal)
            {
                ambientPlayer.Stop(currentHandle, fadeSeconds);
            }
        }

        /// <summary>停止指定声音 ID 的环境音播放或加载请求，并返回受影响数量。</summary>
        public int StopAmbient(SoundId id, float fadeSeconds = 0f)
        {
            ValidateFadeSeconds(fadeSeconds);
            int stoppedCount = 0;
            if (pendingAmbientHandle != null
                && pendingAmbientHandle.SoundId == id
                && pendingAmbientHandle.State == SoundPlaybackState.Loading)
            {
                pendingAmbientHandle.MarkCanceled();
                pendingAmbientHandle = null;
                stoppedCount++;
            }

            if (ambientPlayer != null)
            {
                stoppedCount += ambientPlayer.StopAll(id, fadeSeconds);
            }

            return stoppedCount;
        }

        /// <summary>使用默认淡入时长在指定世界坐标播放 3D 空间环境音。</summary>
        public SoundPlaybackHandle PlaySpatialAmbientAt(SoundId id, Vector3 position)
        {
            return PlaySpatialAmbientAt(id, position, DefaultAmbientFadeSeconds);
        }

        /// <summary>在指定世界坐标播放 3D 空间环境音，使用声音定义中的距离衰减配置。</summary>
        public SoundPlaybackHandle PlaySpatialAmbientAt(SoundId id, Vector3 position, float fadeSeconds)
        {
            return PlaySpatialAmbient(id, SoundSpatialMode.WorldPosition, position, null, fadeSeconds);
        }

        /// <summary>使用默认淡入时长播放跟随目标的 3D 空间环境音。</summary>
        public SoundPlaybackHandle PlaySpatialAmbientFollow(SoundId id, Transform target)
        {
            return PlaySpatialAmbientFollow(id, target, DefaultAmbientFadeSeconds);
        }

        /// <summary>播放跟随目标的 3D 空间环境音，播放期间每帧同步目标位置。</summary>
        public SoundPlaybackHandle PlaySpatialAmbientFollow(SoundId id, Transform target, float fadeSeconds)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return PlaySpatialAmbient(id, SoundSpatialMode.FollowTarget, Vector3.zero, target, fadeSeconds);
        }

        /// <summary>停止指定空间环境音播放句柄。</summary>
        public void StopSpatialAmbient(SoundPlaybackHandle handle, float fadeSeconds = 0f)
        {
            Stop(handle, fadeSeconds);
        }

        /// <summary>停止指定声音 ID 的全部空间环境音播放或加载请求，并返回受影响数量。</summary>
        public int StopSpatialAmbient(SoundId id, float fadeSeconds = 0f)
        {
            ValidateFadeSeconds(fadeSeconds);
            int stoppedCount = 0;
            List<SoundPlaybackHandle> handlesToCancel = new List<SoundPlaybackHandle>();
            foreach (SoundPlaybackHandle handle in loadingSpatialAmbientHandles)
            {
                if (handle.SoundId == id)
                {
                    handlesToCancel.Add(handle);
                }
            }

            foreach (SoundPlaybackHandle handle in handlesToCancel)
            {
                handle.MarkCanceled();
                loadingSpatialAmbientHandles.Remove(handle);
                stoppedCount++;
            }

            if (spatialAmbientPool != null)
            {
                stoppedCount += spatialAmbientPool.StopAll(id, fadeSeconds);
            }

            return stoppedCount;
        }

        /// <summary>
        /// 请求以二维方式播放音效并返回可观察的播放句柄。
        /// </summary>
        public SoundPlaybackHandle PlaySfx2D(SoundId id)
        {
            return PlaySfx(id, SoundSpatialMode.TwoDimensional, Vector3.zero, null);
        }

        /// <summary>
        /// 请求在指定世界坐标播放音效并返回可观察的播放句柄。
        /// </summary>
        public SoundPlaybackHandle PlaySfxAt(SoundId id, Vector3 position)
        {
            return PlaySfx(id, SoundSpatialMode.WorldPosition, position, null);
        }

        /// <summary>
        /// 请求跟随指定目标播放音效并返回可观察的播放句柄。
        /// </summary>
        public SoundPlaybackHandle PlaySfxFollow(SoundId id, Transform target)
        {
            if (target == null)
            {
                throw new ArgumentNullException(nameof(target));
            }

            return PlaySfx(id, SoundSpatialMode.FollowTarget, Vector3.zero, target);
        }

        /// <summary>
        /// 停止指定播放句柄对应的加载请求或活动播放。
        /// </summary>
        public void Stop(SoundPlaybackHandle handle, float fadeSeconds = 0f)
        {
            if (handle == null)
            {
                throw new ArgumentNullException(nameof(handle));
            }

            ValidateFadeSeconds(fadeSeconds);
            if (handle.State == SoundPlaybackState.Loading)
            {
                handle.MarkCanceled();
                if (handle.Category == SoundCategory.Bgm && pendingBgmHandle == handle)
                {
                    pendingBgmHandle = null;
                }
                else if (handle.Category == SoundCategory.Ambient && pendingAmbientHandle == handle)
                {
                    pendingAmbientHandle = null;
                }
                else if (handle.Category == SoundCategory.Ambient)
                {
                    loadingSpatialAmbientHandles.Remove(handle);
                }
                else
                {
                    loadingSfxHandles.Remove(handle);
                    ReleaseSfxReservation(handle);
                }

                return;
            }

            if (handle.Category == SoundCategory.Bgm)
            {
                bgmPlayer?.Stop(handle, fadeSeconds);
                return;
            }

            if (handle.Category == SoundCategory.Ambient)
            {
                if (spatialAmbientPool != null && spatialAmbientPool.Stop(handle, fadeSeconds))
                {
                    return;
                }

                ambientPlayer?.Stop(handle, fadeSeconds);
                return;
            }

            sfxPool?.Stop(handle, fadeSeconds);
        }

        /// <summary>
        /// 停止指定声音 ID 的全部加载请求和活动播放，并返回受影响数量。
        /// </summary>
        public int StopAll(SoundId id, float fadeSeconds = 0f)
        {
            ValidateFadeSeconds(fadeSeconds);
            int stoppedCount = 0;
            List<SoundPlaybackHandle> handlesToCancel = new List<SoundPlaybackHandle>();
            foreach (SoundPlaybackHandle handle in loadingSfxHandles)
            {
                if (handle.SoundId == id)
                {
                    handlesToCancel.Add(handle);
                }
            }

            foreach (SoundPlaybackHandle handle in loadingSpatialAmbientHandles)
            {
                if (handle.SoundId == id)
                {
                    handlesToCancel.Add(handle);
                }
            }

            foreach (SoundPlaybackHandle handle in handlesToCancel)
            {
                handle.MarkCanceled();
                loadingSfxHandles.Remove(handle);
                loadingSpatialAmbientHandles.Remove(handle);
                ReleaseSfxReservation(handle);
                stoppedCount++;
            }

            if (pendingBgmHandle != null
                && pendingBgmHandle.SoundId == id
                && pendingBgmHandle.State == SoundPlaybackState.Loading)
            {
                pendingBgmHandle.MarkCanceled();
                pendingBgmHandle = null;
                stoppedCount++;
            }

            if (pendingAmbientHandle != null
                && pendingAmbientHandle.SoundId == id
                && pendingAmbientHandle.State == SoundPlaybackState.Loading)
            {
                pendingAmbientHandle.MarkCanceled();
                pendingAmbientHandle = null;
                stoppedCount++;
            }

            if (bgmPlayer != null)
            {
                stoppedCount += bgmPlayer.StopAll(id, fadeSeconds);
            }

            if (ambientPlayer != null)
            {
                stoppedCount += ambientPlayer.StopAll(id, fadeSeconds);
            }

            if (spatialAmbientPool != null)
            {
                stoppedCount += spatialAmbientPool.StopAll(id, fadeSeconds);
            }

            if (sfxPool != null)
            {
                stoppedCount += sfxPool.StopAll(id, fadeSeconds);
            }

            return stoppedCount;
        }

        /// <summary>
        /// 获取指定播放句柄的当前状态。
        /// </summary>
        public SoundPlaybackState GetState(SoundPlaybackHandle handle)
        {
            if (handle == null)
            {
                throw new ArgumentNullException(nameof(handle));
            }

            return handle.State;
        }

        /// <summary>
        /// 释放当前没有待播放或活跃引用的音频缓存资源。
        /// </summary>
        public void ReleaseUnusedClips()
        {
            clipCache.ReleaseUnused();
        }

        /// <summary>返回主菜单时停止全部运行期声音请求和播放实例，保留声音管理器与目录缓存。</summary>
        public void ResetRuntimeStateForMainMenu()
        {
            CancelPendingRequests();
            bgmPlayer?.StopAllImmediate();
            ambientPlayer?.StopAllImmediate();
            spatialAmbientPool?.StopAllImmediate();
            sfxPool?.StopAllImmediate();
            sfxConcurrentCounts.Clear();
            reservedSfxHandles.Clear();
            loadingSfxHandles.Clear();
            loadingSpatialAmbientHandles.Clear();
            clipCache.ReleaseUnused();
        }

        /// <summary>
        /// 设置主混音通道的线性音量。
        /// </summary>
        public void SetMasterVolume(float value)
        {
            volumeController.SetMasterVolume(value);
        }

        /// <summary>
        /// 设置背景音乐混音通道的线性音量。
        /// </summary>
        public void SetBgmVolume(float value)
        {
            volumeController.SetBgmVolume(value);
        }

        /// <summary>
        /// 设置环境音混音通道的线性音量。
        /// </summary>
        public void SetAmbientVolume(float value)
        {
            volumeController.SetAmbientVolume(value);
        }

        /// <summary>
        /// 设置音效混音通道的线性音量。
        /// </summary>
        public void SetSfxVolume(float value)
        {
            volumeController.SetSfxVolume(value);
        }

        /// <summary>
        /// 使用非缩放时间推进背景音乐、环境音和音效播放状态。
        /// </summary>
        private void Update()
        {
            bgmPlayer?.Tick(Time.unscaledDeltaTime);
            ambientPlayer?.Tick(Time.unscaledDeltaTime);
            spatialAmbientPool?.Tick(Time.unscaledDeltaTime);
            sfxPool?.Tick(Time.unscaledDeltaTime);
        }

        /// <summary>
        /// 清理真实单例持有的订阅、播放实例和 Addressables 资源。
        /// </summary>
        protected override void OnDestroy()
        {
            if (IsSingletonInstance)
            {
                isDestroyed = true;
                SceneManager.activeSceneChanged -= OnActiveSceneChanged;
                CancelPendingRequests();
                bgmPlayer?.Dispose();
                ambientPlayer?.Dispose();
                spatialAmbientPool?.StopAllImmediate();
                sfxPool?.StopAllImmediate();
                clipCache.ReleaseAll();
                if (hasCatalogHandle)
                {
                    Addressables.Release(catalogHandle);
                    hasCatalogHandle = false;
                }
            }

            base.OnDestroy();
        }

        /// <summary>
        /// 异步加载背景音乐资源并仅允许最新请求执行交叉淡化。
        /// </summary>
        private async Task PlayBgmAsync(SoundPlaybackHandle handle, float fadeSeconds)
        {
            AudioClipLease lease = null;
            try
            {
                await initializeTask;
                ThrowIfInitializationFailed();
                if (handle.IsTerminal)
                {
                    return;
                }

                SoundDefinition definition = GetDefinition(id: handle.SoundId, category: SoundCategory.Bgm);
                lease = await clipCache.AcquireAsync(definition);
                if (handle.IsTerminal || pendingBgmHandle != handle)
                {
                    lease.Dispose();
                    lease = null;
                    return;
                }

                bgmPlayer.CrossfadeTo(handle, definition, lease, fadeSeconds);
                lease = null;
                pendingBgmHandle = null;
            }
            catch (Exception exception)
            {
                lease?.Dispose();
                if (handle.State == SoundPlaybackState.Loading)
                {
                    handle.MarkFailed();
                }

                if (pendingBgmHandle == handle)
                {
                    pendingBgmHandle = null;
                }

                Debug.LogException(new InvalidOperationException($"播放背景音乐失败，ID：{handle.SoundId}。", exception));
            }
        }

        /// <summary>
        /// 异步加载环境音资源并仅允许最新请求执行交叉淡化。
        /// </summary>
        private async Task PlayAmbientAsync(SoundPlaybackHandle handle, float fadeSeconds)
        {
            AudioClipLease lease = null;
            try
            {
                await initializeTask;
                ThrowIfInitializationFailed();
                if (handle.IsTerminal)
                {
                    return;
                }

                SoundDefinition definition = GetDefinition(id: handle.SoundId, category: SoundCategory.Ambient);
                lease = await clipCache.AcquireAsync(definition);
                if (handle.IsTerminal || pendingAmbientHandle != handle)
                {
                    lease.Dispose();
                    lease = null;
                    return;
                }

                ambientPlayer.CrossfadeTo(handle, definition, lease, fadeSeconds);
                lease = null;
                pendingAmbientHandle = null;
            }
            catch (Exception exception)
            {
                lease?.Dispose();
                if (handle.State == SoundPlaybackState.Loading)
                {
                    handle.MarkFailed();
                }

                if (pendingAmbientHandle == handle)
                {
                    pendingAmbientHandle = null;
                }

                Debug.LogException(new InvalidOperationException($"播放环境音失败，ID：{handle.SoundId}。", exception));
            }
        }

        /// <summary>创建空间环境音播放请求，并启动异步加载流程。</summary>
        private SoundPlaybackHandle PlaySpatialAmbient(
            SoundId id,
            SoundSpatialMode mode,
            Vector3 position,
            Transform target,
            float fadeSeconds)
        {
            EnsureRuntimeInitialized();
            ValidateFadeSeconds(fadeSeconds);
            SoundPlaybackHandle handle = CreateHandle(id, SoundCategory.Ambient);
            loadingSpatialAmbientHandles.Add(handle);
            _ = PlaySpatialAmbientAsync(handle, mode, position, target, fadeSeconds);
            return handle;
        }

        /// <summary>异步加载空间环境音资源，并在仍有效时交给 3D 环境音池播放。</summary>
        private async Task PlaySpatialAmbientAsync(
            SoundPlaybackHandle handle,
            SoundSpatialMode mode,
            Vector3 position,
            Transform target,
            float fadeSeconds)
        {
            AudioClipLease lease = null;
            try
            {
                await initializeTask;
                ThrowIfInitializationFailed();
                if (handle.IsTerminal)
                {
                    return;
                }

                SoundDefinition definition = GetDefinition(id: handle.SoundId, category: SoundCategory.Ambient);
                lease = await clipCache.AcquireAsync(definition);
                if (handle.IsTerminal)
                {
                    lease.Dispose();
                    lease = null;
                    return;
                }

                spatialAmbientPool.Play(handle, definition, lease, mode, position, target, fadeSeconds);
                lease = null;
            }
            catch (Exception exception)
            {
                lease?.Dispose();
                if (handle.State == SoundPlaybackState.Loading)
                {
                    handle.MarkFailed();
                }

                Debug.LogException(new InvalidOperationException($"播放空间环境音失败，ID：{handle.SoundId}。", exception));
            }
            finally
            {
                loadingSpatialAmbientHandles.Remove(handle);
            }
        }

        /// <summary>
        /// 创建音效请求并启动其异步加载与播放流程。
        /// </summary>
        private SoundPlaybackHandle PlaySfx(SoundId id, SoundSpatialMode mode, Vector3 position, Transform target)
        {
            EnsureRuntimeInitialized();
            SoundPlaybackHandle handle = CreateHandle(id, SoundCategory.Sfx);
            loadingSfxHandles.Add(handle);
            _ = PlaySfxAsync(handle, mode, position, target);
            return handle;
        }

        /// <summary>
        /// 异步获取音效租约并在二次终态检查后交给音效池播放。
        /// </summary>
        private async Task PlaySfxAsync(SoundPlaybackHandle handle, SoundSpatialMode mode, Vector3 position, Transform target)
        {
            AudioClipLease lease = null;
            string assetGuid = string.Empty;
            try
            {
                await initializeTask;
                ThrowIfInitializationFailed();
                if (handle.IsTerminal)
                {
                    return;
                }

                SoundDefinition definition = GetDefinition(id: handle.SoundId, category: SoundCategory.Sfx);
                assetGuid = definition.Clip.AssetGUID;
                ReserveSfx(handle, definition);
                lease = await clipCache.AcquireAsync(definition);
                if (handle.IsTerminal)
                {
                    lease.Dispose();
                    lease = null;
                    return;
                }

                sfxPool.Play(handle, definition, lease, mode, position, target);
                lease = null;
            }
            catch (Exception exception)
            {
                lease?.Dispose();
                if (handle.State == SoundPlaybackState.Loading)
                {
                    handle.MarkFailed();
                }

                Debug.LogException(new InvalidOperationException(
                    $"播放音效失败，ID：{handle.SoundId}，资源 GUID：{assetGuid}。",
                    exception));
            }
            finally
            {
                loadingSfxHandles.Remove(handle);
                if (handle.State != SoundPlaybackState.Playing && handle.State != SoundPlaybackState.FadingOut)
                {
                    ReleaseSfxReservation(handle);
                }
            }
        }

        /// <summary>
        /// 为音效请求占用一个并发名额，超过配置上限时立即失败。
        /// </summary>
        private void ReserveSfx(SoundPlaybackHandle handle, SoundDefinition definition)
        {
            sfxConcurrentCounts.TryGetValue(handle.SoundId, out int currentCount);
            if (currentCount >= definition.MaxConcurrent)
            {
                throw new InvalidOperationException($"音效 {handle.SoundId} 已达到最大并发数 {definition.MaxConcurrent}。");
            }

            sfxConcurrentCounts[handle.SoundId] = currentCount + 1;
            reservedSfxHandles.Add(handle);
        }

        /// <summary>
        /// 仅在句柄实际占用并发名额时归还一次对应计数。
        /// </summary>
        private void ReleaseSfxReservation(SoundPlaybackHandle handle)
        {
            if (!reservedSfxHandles.Remove(handle))
            {
                return;
            }

            int currentCount = sfxConcurrentCounts[handle.SoundId] - 1;
            if (currentCount == 0)
            {
                sfxConcurrentCounts.Remove(handle.SoundId);
                return;
            }

            sfxConcurrentCounts[handle.SoundId] = currentCount;
        }

        /// <summary>
        /// 响应活动场景变化，取消加载中的环境音和音效，并立即回收场景相关播放实例。
        /// </summary>
        private void OnActiveSceneChanged(Scene previousScene, Scene nextScene)
        {
            if (pendingAmbientHandle != null && pendingAmbientHandle.State == SoundPlaybackState.Loading)
            {
                pendingAmbientHandle.MarkCanceled();
            }

            pendingAmbientHandle = null;
            ambientPlayer?.StopAllImmediate();

            List<SoundPlaybackHandle> spatialAmbientHandlesToCancel = new List<SoundPlaybackHandle>(loadingSpatialAmbientHandles);
            foreach (SoundPlaybackHandle handle in spatialAmbientHandlesToCancel)
            {
                if (handle.State == SoundPlaybackState.Loading)
                {
                    handle.MarkCanceled();
                }

                loadingSpatialAmbientHandles.Remove(handle);
            }

            spatialAmbientPool?.StopAllImmediate();

            List<SoundPlaybackHandle> handlesToCancel = new List<SoundPlaybackHandle>(loadingSfxHandles);
            foreach (SoundPlaybackHandle handle in handlesToCancel)
            {
                if (handle.State == SoundPlaybackState.Loading)
                {
                    handle.MarkCanceled();
                }

                loadingSfxHandles.Remove(handle);
                ReleaseSfxReservation(handle);
            }

            sfxPool?.StopAllImmediate();
            sfxConcurrentCounts.Clear();
            reservedSfxHandles.Clear();
        }

        /// <summary>
        /// 取消仍在加载中的背景音乐、环境音和音效请求。
        /// </summary>
        private void CancelPendingRequests()
        {
            if (pendingBgmHandle != null && pendingBgmHandle.State == SoundPlaybackState.Loading)
            {
                pendingBgmHandle.MarkCanceled();
            }

            pendingBgmHandle = null;
            if (pendingAmbientHandle != null && pendingAmbientHandle.State == SoundPlaybackState.Loading)
            {
                pendingAmbientHandle.MarkCanceled();
            }

            pendingAmbientHandle = null;
            List<SoundPlaybackHandle> spatialAmbientHandlesToCancel = new List<SoundPlaybackHandle>(loadingSpatialAmbientHandles);
            foreach (SoundPlaybackHandle handle in spatialAmbientHandlesToCancel)
            {
                if (handle.State == SoundPlaybackState.Loading)
                {
                    handle.MarkCanceled();
                }
            }

            loadingSpatialAmbientHandles.Clear();
            List<SoundPlaybackHandle> handlesToCancel = new List<SoundPlaybackHandle>(loadingSfxHandles);
            foreach (SoundPlaybackHandle handle in handlesToCancel)
            {
                if (handle.State == SoundPlaybackState.Loading)
                {
                    handle.MarkCanceled();
                }

                ReleaseSfxReservation(handle);
            }

            loadingSfxHandles.Clear();
        }

        /// <summary>
        /// 根据声音 ID 获取并验证指定类别的目录定义。
        /// </summary>
        private SoundDefinition GetDefinition(SoundId id, SoundCategory category)
        {
            if (!soundLookup.TryGetValue(id, out SoundDefinition definition))
            {
                throw new KeyNotFoundException($"声音目录未找到 ID 为 {id} 的定义。");
            }

            if (definition.Category != category)
            {
                throw new InvalidOperationException($"声音 {id} 不是 {category} 类型。");
            }

            return definition;
        }

        /// <summary>
        /// 在初始化异常已记录时向播放请求传播原始失败原因。
        /// </summary>
        private void ThrowIfInitializationFailed()
        {
            if (initializationException != null)
            {
                throw new InvalidOperationException("声音系统初始化失败。", initializationException);
            }
        }

        /// <summary>
        /// 创建具有单调递增请求编号的加载中播放句柄。
        /// </summary>
        private SoundPlaybackHandle CreateHandle(SoundId id, SoundCategory category)
        {
            nextRequestId++;
            return new SoundPlaybackHandle(nextRequestId, id, category);
        }

        /// <summary>
        /// 验证淡化时长必须为零或正数。
        /// </summary>
        private static void ValidateFadeSeconds(float fadeSeconds)
        {
            if (fadeSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fadeSeconds), fadeSeconds, "淡化时长不能小于零。");
            }
        }

        /// <summary>
        /// 校验目录默认淡化时长，确保后续默认播放策略不会使用无效配置。
        /// </summary>
        private void ValidateDefaultBgmFadeSeconds()
        {
            if (initializeTask != null && initializeTask.IsCompleted && defaultBgmFadeSeconds < 0f)
            {
                throw new InvalidOperationException("背景音乐默认淡化时长不能小于零。");
            }
        }
    }
}
