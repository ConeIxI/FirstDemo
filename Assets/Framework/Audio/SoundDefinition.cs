using System;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace GameMain2.Framework.Audio
{
    [Serializable]
    public sealed class SoundDefinition
    {
        [SerializeField] private SoundId id;
        [SerializeField] private SoundCategory category;
        [SerializeField] private AssetReferenceT<AudioClip> clip;
        [SerializeField, Range(0f, 1f)] private float baseVolume = 1f;
        [SerializeField, Min(0f)] private float startTimeSeconds;
        [SerializeField] private Vector2 pitchRange = Vector2.one;
        [SerializeField, Min(1)] private int maxConcurrent = 1;
        [SerializeField, Min(0.01f)] private float minDistance = 1f;
        [SerializeField, Min(0.01f)] private float maxDistance = 20f;
        [SerializeField] private AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;

        public SoundId Id => id;
        public SoundCategory Category => category;
        public AssetReferenceT<AudioClip> Clip => clip;
        public float BaseVolume => baseVolume;
        public float StartTimeSeconds => startTimeSeconds;
        public Vector2 PitchRange => pitchRange;
        public int MaxConcurrent => maxConcurrent;
        public float MinDistance => minDistance;
        public float MaxDistance => maxDistance;
        public AudioRolloffMode RolloffMode => rolloffMode;

        /// <summary>
        /// 校验单条声音定义是否满足播放所需的配置约束。
        /// </summary>
        public void Validate()
        {
            if (id == SoundId.None)
            {
                throw new InvalidOperationException("声音定义未配置有效的声音 ID。");
            }

            if (clip == null || !clip.RuntimeKeyIsValid())
            {
                throw new InvalidOperationException($"声音 {id} 未配置有效的 Addressables 音频资源。");
            }

            if (baseVolume < 0f || baseVolume > 1f)
            {
                throw new InvalidOperationException($"声音 {id} 的基础音量必须位于 [0, 1]。");
            }

            if (startTimeSeconds < 0f)
            {
                throw new InvalidOperationException($"声音 {id} 的开始播放时间不能小于零。");
            }

            if (pitchRange.x <= 0f || pitchRange.y < pitchRange.x)
            {
                throw new InvalidOperationException($"声音 {id} 的音调范围无效。");
            }

            if (category != SoundCategory.Sfx)
            {
                return;
            }

            if (maxConcurrent <= 0)
            {
                throw new InvalidOperationException($"音效 {id} 的最大并发数必须大于零。");
            }

            if (minDistance <= 0f || maxDistance < minDistance)
            {
                throw new InvalidOperationException($"音效 {id} 的距离范围无效。");
            }
        }
    }
}
