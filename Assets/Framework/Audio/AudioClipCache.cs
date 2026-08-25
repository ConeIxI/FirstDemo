using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.Exceptions;

namespace GameMain2.Framework.Audio
{
    /// <summary>
    /// 表示一次音频缓存引用，并在生命周期结束时归还对应引用。
    /// </summary>
    internal sealed class AudioClipLease : IDisposable
    {
        private readonly AudioClipCache cache;
        private readonly string assetGuid;
        private bool isActive;
        private bool isDisposed;

        public AudioClip Clip { get; }

        /// <summary>
        /// 创建初始计入待播放引用的音频租约。
        /// </summary>
        internal AudioClipLease(AudioClipCache cache, string assetGuid, AudioClip clip)
        {
            this.cache = cache;
            this.assetGuid = assetGuid;
            Clip = clip;
        }

        /// <summary>
        /// 将当前租约从待播放引用迁移为活跃引用。
        /// </summary>
        public void PromoteToActive()
        {
            if (isDisposed || isActive)
            {
                throw new InvalidOperationException("音频租约不能重复提升或在释放后提升。");
            }

            cache.PromoteToActive(assetGuid);
            isActive = true;
        }

        /// <summary>
        /// 按租约当前阶段仅归还一次缓存引用。
        /// </summary>
        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            if (isActive)
            {
                cache.ReleaseActiveReference(assetGuid);
                return;
            }

            cache.ReleasePendingReference(assetGuid);
        }
    }

    /// <summary>
    /// 音频专用加载通道，按 Addressables 资源 GUID 共享加载句柄及其引用计数。
    /// 该缓存保留独立于 ResourceManager 的生命周期，因为声音播放需要租约、活跃引用和延迟释放。
    /// </summary>
    internal sealed class AudioClipCache
    {
        private sealed class CacheEntry
        {
            public AsyncOperationHandle<AudioClip> Handle { get; }
            public Task<AudioClip> LoadTask { get; }
            public int PendingReferenceCount { get; set; }
            public int ActiveReferenceCount { get; set; }

            /// <summary>
            /// 创建持有唯一 Addressables 加载句柄的缓存条目。
            /// </summary>
            public CacheEntry(AsyncOperationHandle<AudioClip> handle)
            {
                Handle = handle;
                LoadTask = handle.Task;
            }
        }

        private readonly Dictionary<string, CacheEntry> entries = new Dictionary<string, CacheEntry>();
        private bool isReleased;

        /// <summary>
        /// 获取指定声音的待播放租约，并与同一资源共享加载任务。
        /// </summary>
        public async Task<AudioClipLease> AcquireAsync(SoundDefinition definition)
        {
            if (isReleased)
            {
                throw new InvalidOperationException("音频缓存已释放，不能继续加载资源。");
            }

            string assetGuid = definition.Clip.AssetGUID;
            if (!entries.TryGetValue(assetGuid, out CacheEntry entry))
            {
                AsyncOperationHandle<AudioClip> handle = Addressables.LoadAssetAsync<AudioClip>(definition.Clip);
                entry = new CacheEntry(handle);
                entries.Add(assetGuid, entry);
            }

            entry.PendingReferenceCount++;
            AudioClip clip;
            try
            {
                clip = await entry.LoadTask;
            }
            catch (Exception exception)
            {
                entry.PendingReferenceCount--;
                ReleaseFailedEntryIfUnused(assetGuid, entry);
                throw new OperationException($"加载音频资源 {assetGuid} 失败。", exception);
            }

            if (entry.Handle.Status != AsyncOperationStatus.Succeeded)
            {
                Exception exception = entry.Handle.OperationException;
                entry.PendingReferenceCount--;
                ReleaseFailedEntryIfUnused(assetGuid, entry);
                throw new OperationException($"加载音频资源 {assetGuid} 失败。", exception);
            }

            if (isReleased)
            {
                throw new InvalidOperationException("音频缓存已释放，不能继续创建租约。");
            }

            return new AudioClipLease(this, assetGuid, clip);
        }

        /// <summary>
        /// 释放加载完成且不再被待播放或活跃租约引用的缓存条目。
        /// </summary>
        public void ReleaseUnused()
        {
            if (isReleased)
            {
                return;
            }

            List<string> unusedAssetGuids = new List<string>();
            foreach (KeyValuePair<string, CacheEntry> pair in entries)
            {
                CacheEntry entry = pair.Value;
                if (entry.Handle.IsDone && entry.PendingReferenceCount == 0 && entry.ActiveReferenceCount == 0)
                {
                    unusedAssetGuids.Add(pair.Key);
                }
            }

            foreach (string assetGuid in unusedAssetGuids)
            {
                ReleaseEntry(assetGuid, entries[assetGuid]);
            }
        }

        /// <summary>
        /// 释放全部缓存加载句柄，调用前必须清理全部播放实例。
        /// </summary>
        public void ReleaseAll()
        {
            if (isReleased)
            {
                return;
            }

            isReleased = true;
            foreach (CacheEntry entry in entries.Values)
            {
                Addressables.Release(entry.Handle);
            }

            entries.Clear();
        }

        /// <summary>
        /// 将待播放引用转为活跃引用。
        /// </summary>
        internal void PromoteToActive(string assetGuid)
        {
            if (isReleased)
            {
                throw new InvalidOperationException("音频缓存已释放，不能提升租约引用。");
            }

            CacheEntry entry = entries[assetGuid];
            entry.PendingReferenceCount--;
            entry.ActiveReferenceCount++;
        }

        /// <summary>
        /// 归还一份待播放引用。
        /// </summary>
        internal void ReleasePendingReference(string assetGuid)
        {
            if (isReleased)
            {
                return;
            }

            entries[assetGuid].PendingReferenceCount--;
        }

        /// <summary>
        /// 归还一份活跃引用。
        /// </summary>
        internal void ReleaseActiveReference(string assetGuid)
        {
            if (isReleased)
            {
                return;
            }

            entries[assetGuid].ActiveReferenceCount--;
        }

        /// <summary>
        /// 在加载失败且无引用时释放对应的失败句柄。
        /// </summary>
        private void ReleaseFailedEntryIfUnused(string assetGuid, CacheEntry entry)
        {
            if (isReleased)
            {
                return;
            }

            if (entry.PendingReferenceCount == 0 && entry.ActiveReferenceCount == 0)
            {
                ReleaseEntry(assetGuid, entry);
            }
        }

        /// <summary>
        /// 从缓存移除条目并释放其 Addressables 加载句柄。
        /// </summary>
        private void ReleaseEntry(string assetGuid, CacheEntry entry)
        {
            entries.Remove(assetGuid);
            Addressables.Release(entry.Handle);
        }
    }
}
