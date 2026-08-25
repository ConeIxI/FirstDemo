using System;
using UnityEngine;
using UnityEngine.Audio;

namespace GameMain2.Framework.Audio
{
    /// <summary>
    /// 使用两个固定播放槽管理二维循环声音的交叉淡化与资源生命周期。
    /// </summary>
    internal sealed class BgmPlayer : IDisposable
    {
        private sealed class Slot
        {
            public int InstanceId;
            public uint Version;
            public AudioSource Source;
            public SoundPlaybackHandle Handle;
            public AudioClipLease Lease;
            public float FadeStartVolume;
            public float FadeTargetVolume;
            public float FadeDuration;
            public float FadeElapsed;
        }

        private readonly Slot[] slots;
        private int currentSlotIndex = -1;
        private int latestLiveSlotIndex = -1;
        private bool isDisposed;

        /// <summary>
        /// 获取最新请求播放的循环声音句柄。
        /// </summary>
        public SoundPlaybackHandle CurrentHandle => currentSlotIndex < 0 ? null : slots[currentSlotIndex].Handle;

        /// <summary>
        /// 创建两个固定的二维循环背景音乐播放槽。
        /// </summary>
        public BgmPlayer(Transform host, AudioMixerGroup bgmGroup)
            : this(host, bgmGroup, "BgmVoice")
        {
        }

        /// <summary>
        /// 创建两个固定的二维循环播放槽，并使用指定名称前缀区分声音通道。
        /// </summary>
        public BgmPlayer(Transform host, AudioMixerGroup mixerGroup, string voiceNamePrefix)
        {
            slots = new[]
            {
                CreateSlot(host, mixerGroup, $"{voiceNamePrefix}0"),
                CreateSlot(host, mixerGroup, $"{voiceNamePrefix}1")
            };
        }

        /// <summary>
        /// 判断指定声音是否仍是当前且未进入终态的循环声音。
        /// </summary>
        public bool IsCurrent(SoundId id)
        {
            SoundPlaybackHandle handle = CurrentHandle;
            return handle != null && handle.SoundId == id && !handle.IsTerminal;
        }

        /// <summary>
        /// 播放新的循环声音，并将较新的现有循环声音淡出为旧声音。
        /// </summary>
        public void CrossfadeTo(
            SoundPlaybackHandle handle,
            SoundDefinition definition,
            AudioClipLease lease,
            float fadeSeconds)
        {
            ValidateFadeSeconds(fadeSeconds);

            int oldSlotIndex = FindNewestLiveSlotIndex();
            if (oldSlotIndex >= 0)
            {
                int olderSlotIndex = 1 - oldSlotIndex;
                if (slots[olderSlotIndex].Handle != null)
                {
                    CompleteSlot(olderSlotIndex);
                }
            }

            int newSlotIndex = oldSlotIndex < 0 ? 0 : 1 - oldSlotIndex;
            Slot newSlot = slots[newSlotIndex];
            AudioSource source = newSlot.Source;
            newSlot.Version++;
            newSlot.Handle = handle;
            newSlot.Lease = lease;
            newSlot.FadeStartVolume = 0f;
            newSlot.FadeTargetVolume = definition.BaseVolume;
            newSlot.FadeDuration = fadeSeconds;
            newSlot.FadeElapsed = 0f;
            source.clip = lease.Clip;
            source.volume = 0f;
            source.time = definition.StartTimeSeconds;
            lease.PromoteToActive();
            source.Play();
            handle.BindInstance(newSlot.InstanceId, newSlot.Version);
            handle.MarkPlaying();

            currentSlotIndex = newSlotIndex;
            latestLiveSlotIndex = newSlotIndex;
            if (oldSlotIndex >= 0)
            {
                BeginFade(slots[oldSlotIndex], 0f, fadeSeconds);
            }

            if (fadeSeconds == 0f)
            {
                CompleteImmediateFades();
            }
        }

        /// <summary>
        /// 停止与实例标识和版本完全匹配的循环声音。
        /// </summary>
        public bool Stop(SoundPlaybackHandle handle, float fadeSeconds)
        {
            ValidateFadeSeconds(fadeSeconds);

            for (int index = 0; index < slots.Length; index++)
            {
                Slot slot = slots[index];
                if (slot.Handle == null
                    || slot.InstanceId != handle.InstanceId
                    || slot.Version != handle.Version)
                {
                    continue;
                }

                if (currentSlotIndex == index)
                {
                    currentSlotIndex = -1;
                }

                if (fadeSeconds == 0f)
                {
                    CompleteSlot(index);
                }
                else
                {
                    BeginFade(slot, 0f, fadeSeconds);
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// 停止指定声音 ID 的所有循环声音，并返回受影响的播放槽数量。
        /// </summary>
        public int StopAll(SoundId id, float fadeSeconds)
        {
            ValidateFadeSeconds(fadeSeconds);

            int stoppedCount = 0;
            for (int index = 0; index < slots.Length; index++)
            {
                Slot slot = slots[index];
                if (slot.Handle == null || slot.Handle.SoundId != id)
                {
                    continue;
                }

                if (currentSlotIndex == index)
                {
                    currentSlotIndex = -1;
                }

                if (fadeSeconds == 0f)
                {
                    CompleteSlot(index);
                }
                else
                {
                    BeginFade(slot, 0f, fadeSeconds);
                }

                stoppedCount++;
            }

            return stoppedCount;
        }

        /// <summary>立即停止全部循环声音播放槽，但保留播放器对象供后续场景继续复用。</summary>
        public void StopAllImmediate()
        {
            for (int index = 0; index < slots.Length; index++)
            {
                if (slots[index].Handle != null)
                {
                    CompleteSlot(index);
                }
            }

            currentSlotIndex = -1;
            latestLiveSlotIndex = -1;
        }

        /// <summary>
        /// 使用非缩放时间推进两个播放槽的淡化进度。
        /// </summary>
        public void Tick(float unscaledDeltaTime)
        {
            for (int index = 0; index < slots.Length; index++)
            {
                Slot slot = slots[index];
                if (slot.Handle == null || slot.FadeElapsed >= slot.FadeDuration)
                {
                    continue;
                }

                slot.FadeElapsed = Mathf.Min(slot.FadeDuration, slot.FadeElapsed + unscaledDeltaTime);
                float progress = slot.FadeElapsed / slot.FadeDuration;
                slot.Source.volume = Mathf.Lerp(slot.FadeStartVolume, slot.FadeTargetVolume, progress);

                if (slot.FadeElapsed >= slot.FadeDuration && slot.FadeTargetVolume == 0f)
                {
                    CompleteSlot(index);
                }
            }
        }

        /// <summary>
        /// 立即释放两个循环声音槽，并销毁对应播放对象。
        /// </summary>
        public void Dispose()
        {
            if (isDisposed)
            {
                return;
            }

            isDisposed = true;
            for (int index = 0; index < slots.Length; index++)
            {
                if (slots[index].Handle != null)
                {
                    CompleteSlot(index);
                }

                UnityEngine.Object.Destroy(slots[index].Source.gameObject);
            }

            currentSlotIndex = -1;
            latestLiveSlotIndex = -1;
        }

        /// <summary>
        /// 创建一个已配置混音器路由的循环声音播放槽。
        /// </summary>
        private static Slot CreateSlot(Transform host, AudioMixerGroup bgmGroup, string name)
        {
            GameObject slotObject = new GameObject(name);
            slotObject.transform.SetParent(host, false);
            AudioSource source = slotObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.outputAudioMixerGroup = bgmGroup;

            return new Slot
            {
                InstanceId = slotObject.GetInstanceID(),
                Source = source
            };
        }

        /// <summary>
        /// 获取最近的存活播放槽，逻辑当前状态清空后仍保留最新淡出槽的顺序。
        /// </summary>
        private int FindNewestLiveSlotIndex()
        {
            if (latestLiveSlotIndex >= 0 && slots[latestLiveSlotIndex].Handle != null)
            {
                return latestLiveSlotIndex;
            }

            for (int index = 0; index < slots.Length; index++)
            {
                if (slots[index].Handle != null)
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// 从当前音量开始向目标音量重新启动淡化，并仅在首次淡出时迁移状态。
        /// </summary>
        private static void BeginFade(Slot slot, float targetVolume, float fadeSeconds)
        {
            slot.FadeStartVolume = slot.Source.volume;
            slot.FadeTargetVolume = targetVolume;
            slot.FadeDuration = fadeSeconds;
            slot.FadeElapsed = 0f;
            if (targetVolume == 0f && slot.Handle.State == SoundPlaybackState.Playing)
            {
                slot.Handle.MarkFadingOut();
            }
        }

        /// <summary>
        /// 完成立即交叉淡化中的音量设置，并回收已淡出到零的播放槽。
        /// </summary>
        private void CompleteImmediateFades()
        {
            for (int index = 0; index < slots.Length; index++)
            {
                Slot slot = slots[index];
                if (slot.Handle == null || slot.FadeDuration != 0f)
                {
                    continue;
                }

                slot.Source.volume = slot.FadeTargetVolume;
                if (slot.FadeTargetVolume == 0f)
                {
                    CompleteSlot(index);
                }
            }
        }

        /// <summary>
        /// 停止播放槽、归还租约，并将关联句柄标记为已完成。
        /// </summary>
        private void CompleteSlot(int slotIndex)
        {
            Slot slot = slots[slotIndex];
            SoundPlaybackHandle handle = slot.Handle;
            slot.Source.Stop();
            slot.Source.clip = null;
            slot.Source.volume = 0f;
            slot.Lease.Dispose();
            slot.Handle = null;
            slot.Lease = null;
            slot.FadeStartVolume = 0f;
            slot.FadeTargetVolume = 0f;
            slot.FadeDuration = 0f;
            slot.FadeElapsed = 0f;
            if (currentSlotIndex == slotIndex)
            {
                currentSlotIndex = -1;
            }
            if (latestLiveSlotIndex == slotIndex)
            {
                latestLiveSlotIndex = FindOtherLiveSlotIndex(slotIndex);
            }

            handle.MarkCompleted();
        }

        /// <summary>
        /// 查找指定槽位之外仍存活的播放槽。
        /// </summary>
        private int FindOtherLiveSlotIndex(int excludedSlotIndex)
        {
            for (int index = 0; index < slots.Length; index++)
            {
                if (index != excludedSlotIndex && slots[index].Handle != null)
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// 验证淡化时长为零或正数。
        /// </summary>
        private static void ValidateFadeSeconds(float fadeSeconds)
        {
            if (fadeSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fadeSeconds), fadeSeconds, "淡化时长不能小于零。");
            }
        }
    }
}
