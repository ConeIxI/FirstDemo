using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace GameMain2.Framework.Audio
{
    /// <summary>
    /// 管理可复用的音效播放实例，并负责其播放、淡出与回收生命周期。
    /// </summary>
    internal sealed class SfxPool
    {
        private sealed class SfxVoice
        {
            public int Id;
            public uint Version;
            public AudioSource AudioSource;
            public SoundPlaybackHandle Handle;
            public AudioClipLease Lease;
            public SoundSpatialMode Mode;
            public Transform FollowTarget;
            public float FadeStartVolume;
            public float FadeDuration;
            public float FadeElapsed;
        }

        private readonly Transform host;
        private readonly AudioMixerGroup sfxGroup;
        private readonly Action<SoundPlaybackHandle> onVoiceRecycled;
        private readonly Stack<SfxVoice> availableVoices = new Stack<SfxVoice>();
        private readonly List<SfxVoice> activeVoices = new List<SfxVoice>();

        /// <summary>
        /// 创建指定容量的禁用音效实例，作为后续播放的对象池。
        /// </summary>
        public SfxPool(Transform host, AudioMixerGroup sfxGroup, int initialCapacity, Action<SoundPlaybackHandle> onVoiceRecycled)
        {
            this.host = host;
            this.sfxGroup = sfxGroup;
            this.onVoiceRecycled = onVoiceRecycled;

            for (int index = 0; index < initialCapacity; index++)
            {
                availableVoices.Push(CreateVoice());
            }
        }

        /// <summary>
        /// 使用一个池实例按指定空间模式播放已激活租约中的音效。
        /// </summary>
        public void Play(
            SoundPlaybackHandle handle,
            SoundDefinition definition,
            AudioClipLease lease,
            SoundSpatialMode mode,
            Vector3 position,
            Transform followTarget)
        {
            SfxVoice voice = TakeVoice();
            AudioSource source = voice.AudioSource;

            voice.Version++;
            voice.Handle = handle;
            voice.Lease = lease;
            voice.Mode = mode;
            voice.FollowTarget = mode == SoundSpatialMode.FollowTarget ? followTarget : null;
            source.clip = lease.Clip;
            source.volume = definition.BaseVolume;
            source.time = definition.StartTimeSeconds;
            source.pitch = UnityEngine.Random.Range(definition.PitchRange.x, definition.PitchRange.y);
            source.minDistance = definition.MinDistance;
            source.maxDistance = definition.MaxDistance;
            source.rolloffMode = definition.RolloffMode;

            if (mode == SoundSpatialMode.TwoDimensional)
            {
                source.spatialBlend = 0f;
                source.transform.position = Vector3.zero;
            }
            else if (mode == SoundSpatialMode.WorldPosition)
            {
                source.spatialBlend = 1f;
                source.transform.position = position;
            }
            else
            {
                source.spatialBlend = 1f;
                source.transform.position = followTarget.position;
            }

            source.gameObject.SetActive(true);
            lease.PromoteToActive();
            source.Play();
            handle.BindInstance(voice.Id, voice.Version);
            handle.MarkPlaying();
            activeVoices.Add(voice);
        }

        /// <summary>
        /// 更新跟随位置、淡出进度，并回收已结束的音效实例。
        /// </summary>
        public void Tick(float unscaledDeltaTime)
        {
            for (int index = activeVoices.Count - 1; index >= 0; index--)
            {
                SfxVoice voice = activeVoices[index];
                if (voice.Mode == SoundSpatialMode.FollowTarget)
                {
                    if (voice.FollowTarget == null)
                    {
                        RecycleVoice(voice);
                        continue;
                    }

                    voice.AudioSource.transform.position = voice.FollowTarget.position;
                }

                if (voice.Handle.State == SoundPlaybackState.FadingOut)
                {
                    voice.FadeElapsed += unscaledDeltaTime;
                    voice.AudioSource.volume = Mathf.Lerp(
                        voice.FadeStartVolume,
                        0f,
                        voice.FadeElapsed / voice.FadeDuration);

                    if (voice.FadeElapsed >= voice.FadeDuration)
                    {
                        RecycleVoice(voice);
                    }

                    continue;
                }

                if (!voice.AudioSource.isPlaying)
                {
                    RecycleVoice(voice);
                }
            }
        }

        /// <summary>
        /// 停止与指定实例标识和版本完全匹配的单次播放。
        /// </summary>
        public bool Stop(SoundPlaybackHandle handle, float fadeSeconds)
        {
            ValidateFadeSeconds(fadeSeconds);

            for (int index = activeVoices.Count - 1; index >= 0; index--)
            {
                SfxVoice voice = activeVoices[index];
                if (voice.Id != handle.InstanceId || voice.Version != handle.Version)
                {
                    continue;
                }

                StopVoice(voice, fadeSeconds);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 停止指定声音 ID 的全部活动播放，并返回受影响的实例数量。
        /// </summary>
        public int StopAll(SoundId id, float fadeSeconds)
        {
            ValidateFadeSeconds(fadeSeconds);

            int stoppedCount = 0;
            for (int index = activeVoices.Count - 1; index >= 0; index--)
            {
                SfxVoice voice = activeVoices[index];
                if (voice.Handle.SoundId != id)
                {
                    continue;
                }

                StopVoice(voice, fadeSeconds);
                stoppedCount++;
            }

            return stoppedCount;
        }

        /// <summary>
        /// 立即停止并回收全部活动或淡出中的音效实例。
        /// </summary>
        public void StopAllImmediate()
        {
            for (int index = activeVoices.Count - 1; index >= 0; index--)
            {
                RecycleVoice(activeVoices[index]);
            }
        }

        /// <summary>
        /// 创建一个默认状态的禁用音效播放对象。
        /// </summary>
        private SfxVoice CreateVoice()
        {
            GameObject voiceObject = new GameObject("SfxVoice");
            voiceObject.transform.SetParent(host, false);
            AudioSource source = voiceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.outputAudioMixerGroup = sfxGroup;
            voiceObject.SetActive(false);

            return new SfxVoice
            {
                Id = voiceObject.GetInstanceID(),
                AudioSource = source
            };
        }

        /// <summary>
        /// 从空闲池取得一个实例，不足时创建新的实例。
        /// </summary>
        private SfxVoice TakeVoice()
        {
            return availableVoices.Count > 0 ? availableVoices.Pop() : CreateVoice();
        }

        /// <summary>
        /// 根据淡出时长立即回收或将实例转入淡出状态。
        /// </summary>
        private void StopVoice(SfxVoice voice, float fadeSeconds)
        {
            if (fadeSeconds == 0f)
            {
                RecycleVoice(voice);
                return;
            }

            voice.FadeStartVolume = voice.AudioSource.volume;
            voice.FadeDuration = fadeSeconds;
            voice.FadeElapsed = 0f;
            if (voice.Handle.State == SoundPlaybackState.Playing)
            {
                voice.Handle.MarkFadingOut();
            }
        }

        /// <summary>
        /// 验证淡出时长必须为零或正数，避免负时长破坏淡出状态机。
        /// </summary>
        private static void ValidateFadeSeconds(float fadeSeconds)
        {
            if (fadeSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fadeSeconds), fadeSeconds, "淡出时长不能小于零。");
            }
        }

        /// <summary>
        /// 复位播放实例并通知外部对应句柄已经完成。
        /// </summary>
        private void RecycleVoice(SfxVoice voice)
        {
            activeVoices.Remove(voice);
            SoundPlaybackHandle handle = voice.Handle;
            AudioSource source = voice.AudioSource;
            source.Stop();
            source.clip = null;
            voice.Lease.Dispose();
            voice.Lease = null;
            voice.Handle = null;
            voice.FollowTarget = null;
            voice.Mode = SoundSpatialMode.TwoDimensional;
            voice.FadeStartVolume = 0f;
            voice.FadeDuration = 0f;
            voice.FadeElapsed = 0f;
            source.transform.SetParent(host, false);
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.identity;
            source.transform.localScale = Vector3.one;
            source.volume = 1f;
            source.pitch = 1f;
            source.spatialBlend = 0f;
            source.minDistance = 1f;
            source.maxDistance = 500f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.gameObject.SetActive(false);
            handle.MarkCompleted();
            onVoiceRecycled(handle);
            availableVoices.Push(voice);
        }
    }
}
