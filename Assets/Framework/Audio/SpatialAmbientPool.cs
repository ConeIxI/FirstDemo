using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace GameMain2.Framework.Audio
{
    /// <summary>
    /// 管理空间环境音播放实例，负责 3D 循环播放、跟随、淡入淡出和资源回收。
    /// </summary>
    internal sealed class SpatialAmbientPool
    {
        private sealed class SpatialAmbientVoice
        {
            public int Id;
            public uint Version;
            public AudioSource AudioSource;
            public SoundPlaybackHandle Handle;
            public AudioClipLease Lease;
            public SoundSpatialMode Mode;
            public Transform FollowTarget;
            public float FadeStartVolume;
            public float FadeTargetVolume;
            public float FadeDuration;
            public float FadeElapsed;
        }

        private readonly Transform host;
        private readonly AudioMixerGroup ambientGroup;
        private readonly Stack<SpatialAmbientVoice> availableVoices = new Stack<SpatialAmbientVoice>();
        private readonly List<SpatialAmbientVoice> activeVoices = new List<SpatialAmbientVoice>();

        /// <summary>创建空间环境音对象池，并预创建指定数量的禁用播放实例。</summary>
        public SpatialAmbientPool(Transform host, AudioMixerGroup ambientGroup, int initialCapacity)
        {
            this.host = host;
            this.ambientGroup = ambientGroup;

            for (int index = 0; index < initialCapacity; index++)
            {
                availableVoices.Push(CreateVoice());
            }
        }

        /// <summary>按世界坐标或跟随目标播放一个 3D 循环环境音。</summary>
        public void Play(
            SoundPlaybackHandle handle,
            SoundDefinition definition,
            AudioClipLease lease,
            SoundSpatialMode mode,
            Vector3 position,
            Transform followTarget,
            float fadeSeconds)
        {
            ValidateFadeSeconds(fadeSeconds);

            SpatialAmbientVoice voice = TakeVoice();
            AudioSource source = voice.AudioSource;

            voice.Version++;
            voice.Handle = handle;
            voice.Lease = lease;
            voice.Mode = mode;
            voice.FollowTarget = mode == SoundSpatialMode.FollowTarget ? followTarget : null;
            voice.FadeStartVolume = 0f;
            voice.FadeTargetVolume = definition.BaseVolume;
            voice.FadeDuration = fadeSeconds;
            voice.FadeElapsed = 0f;

            source.clip = lease.Clip;
            source.volume = fadeSeconds == 0f ? definition.BaseVolume : 0f;
            source.time = definition.StartTimeSeconds;
            source.pitch = UnityEngine.Random.Range(definition.PitchRange.x, definition.PitchRange.y);
            source.spatialBlend = 1f;
            source.minDistance = definition.MinDistance;
            source.maxDistance = definition.MaxDistance;
            source.rolloffMode = definition.RolloffMode;
            source.transform.position = mode == SoundSpatialMode.FollowTarget ? followTarget.position : position;

            source.gameObject.SetActive(true);
            lease.PromoteToActive();
            source.Play();
            handle.BindInstance(voice.Id, voice.Version);
            handle.MarkPlaying();
            activeVoices.Add(voice);
        }

        /// <summary>推进跟随位置和淡入淡出状态，回收异常停止的空间环境音。</summary>
        public void Tick(float unscaledDeltaTime)
        {
            for (int index = activeVoices.Count - 1; index >= 0; index--)
            {
                SpatialAmbientVoice voice = activeVoices[index];
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
                    TickFadeOut(voice, unscaledDeltaTime);
                    continue;
                }

                TickFadeIn(voice, unscaledDeltaTime);
                if (!voice.AudioSource.isPlaying)
                {
                    RecycleVoice(voice);
                }
            }
        }

        /// <summary>停止指定播放句柄对应的空间环境音。</summary>
        public bool Stop(SoundPlaybackHandle handle, float fadeSeconds)
        {
            ValidateFadeSeconds(fadeSeconds);

            for (int index = activeVoices.Count - 1; index >= 0; index--)
            {
                SpatialAmbientVoice voice = activeVoices[index];
                if (voice.Id != handle.InstanceId || voice.Version != handle.Version)
                {
                    continue;
                }

                StopVoice(voice, fadeSeconds);
                return true;
            }

            return false;
        }

        /// <summary>停止指定声音 ID 的全部空间环境音，并返回受影响实例数量。</summary>
        public int StopAll(SoundId id, float fadeSeconds)
        {
            ValidateFadeSeconds(fadeSeconds);

            int stoppedCount = 0;
            for (int index = activeVoices.Count - 1; index >= 0; index--)
            {
                SpatialAmbientVoice voice = activeVoices[index];
                if (voice.Handle.SoundId != id)
                {
                    continue;
                }

                StopVoice(voice, fadeSeconds);
                stoppedCount++;
            }

            return stoppedCount;
        }

        /// <summary>立即停止并回收全部空间环境音播放实例。</summary>
        public void StopAllImmediate()
        {
            for (int index = activeVoices.Count - 1; index >= 0; index--)
            {
                RecycleVoice(activeVoices[index]);
            }
        }

        /// <summary>创建一个默认禁用的 3D 循环环境音播放实例。</summary>
        private SpatialAmbientVoice CreateVoice()
        {
            GameObject voiceObject = new GameObject("SpatialAmbientVoice");
            voiceObject.transform.SetParent(host, false);
            AudioSource source = voiceObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 1f;
            source.outputAudioMixerGroup = ambientGroup;
            voiceObject.SetActive(false);

            return new SpatialAmbientVoice
            {
                Id = voiceObject.GetInstanceID(),
                AudioSource = source
            };
        }

        /// <summary>从对象池取得一个空间环境音实例，池为空时创建新实例。</summary>
        private SpatialAmbientVoice TakeVoice()
        {
            return availableVoices.Count > 0 ? availableVoices.Pop() : CreateVoice();
        }

        /// <summary>推进淡入，直到音量达到声音定义的基础音量。</summary>
        private void TickFadeIn(SpatialAmbientVoice voice, float unscaledDeltaTime)
        {
            if (voice.FadeElapsed >= voice.FadeDuration)
            {
                return;
            }

            voice.FadeElapsed = Mathf.Min(voice.FadeDuration, voice.FadeElapsed + unscaledDeltaTime);
            float progress = voice.FadeDuration == 0f ? 1f : voice.FadeElapsed / voice.FadeDuration;
            voice.AudioSource.volume = Mathf.Lerp(voice.FadeStartVolume, voice.FadeTargetVolume, progress);
        }

        /// <summary>推进淡出，淡出结束后回收空间环境音实例。</summary>
        private void TickFadeOut(SpatialAmbientVoice voice, float unscaledDeltaTime)
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
        }

        /// <summary>根据淡出时长立即停止或进入淡出状态。</summary>
        private void StopVoice(SpatialAmbientVoice voice, float fadeSeconds)
        {
            if (fadeSeconds == 0f)
            {
                RecycleVoice(voice);
                return;
            }

            voice.FadeStartVolume = voice.AudioSource.volume;
            voice.FadeTargetVolume = 0f;
            voice.FadeDuration = fadeSeconds;
            voice.FadeElapsed = 0f;
            if (voice.Handle.State == SoundPlaybackState.Playing)
            {
                voice.Handle.MarkFadingOut();
            }
        }

        /// <summary>校验淡出时长必须为零或正数。</summary>
        private static void ValidateFadeSeconds(float fadeSeconds)
        {
            if (fadeSeconds < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(fadeSeconds), fadeSeconds, "淡出时长不能小于零。");
            }
        }

        /// <summary>复位播放实例并释放音频租约。</summary>
        private void RecycleVoice(SpatialAmbientVoice voice)
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
            voice.Mode = SoundSpatialMode.WorldPosition;
            voice.FadeStartVolume = 0f;
            voice.FadeTargetVolume = 0f;
            voice.FadeDuration = 0f;
            voice.FadeElapsed = 0f;
            source.transform.SetParent(host, false);
            source.transform.localPosition = Vector3.zero;
            source.transform.localRotation = Quaternion.identity;
            source.transform.localScale = Vector3.one;
            source.volume = 1f;
            source.pitch = 1f;
            source.spatialBlend = 1f;
            source.minDistance = 1f;
            source.maxDistance = 500f;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.gameObject.SetActive(false);
            handle.MarkCompleted();
            availableVoices.Push(voice);
        }
    }
}
