using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace GameMain2.Framework.Audio
{
    [CreateAssetMenu(fileName = "SoundCatalog", menuName = "Game/Audio/Sound Catalog")]
    public sealed class SoundCatalog : ScriptableObject
    {
        [SerializeField] private AudioMixer audioMixer;
        [SerializeField] private AudioMixerGroup bgmGroup;
        [SerializeField] private AudioMixerGroup sfxGroup;
        [SerializeField] private AudioMixerGroup ambientGroup;
        [SerializeField, Min(0f)] private float defaultBgmFadeSeconds = 1f;
        [SerializeField, Min(1)] private int initialSfxPoolSize = 16;
        [SerializeField] private List<SoundDefinition> sounds = new List<SoundDefinition>();

        /// <summary>
        /// 获取声音系统使用的混音器。
        /// </summary>
        public AudioMixer AudioMixer => audioMixer;

        /// <summary>
        /// 获取背景音乐播放使用的混音组。
        /// </summary>
        public AudioMixerGroup BgmGroup => bgmGroup;

        /// <summary>
        /// 获取音效播放使用的混音组。
        /// </summary>
        public AudioMixerGroup SfxGroup => sfxGroup;

        /// <summary>
        /// 获取环境音播放使用的混音组。
        /// </summary>
        public AudioMixerGroup AmbientGroup => ambientGroup;

        /// <summary>
        /// 获取背景音乐默认淡化时长。
        /// </summary>
        public float DefaultBgmFadeSeconds => defaultBgmFadeSeconds;

        /// <summary>
        /// 获取音效池的初始容量。
        /// </summary>
        public int InitialSfxPoolSize => initialSfxPoolSize;

        /// <summary>
        /// 校验目录配置并构建以声音 ID 为键的只读查询表。
        /// </summary>
        public IReadOnlyDictionary<SoundId, SoundDefinition> BuildLookup()
        {
            if (audioMixer == null)
            {
                throw new InvalidOperationException("声音目录未配置 Audio Mixer。");
            }

            if (bgmGroup == null)
            {
                throw new InvalidOperationException("声音目录未配置 BGM 混音组。");
            }

            if (sfxGroup == null)
            {
                throw new InvalidOperationException("声音目录未配置 SFX 混音组。");
            }

            if (ambientGroup == null)
            {
                throw new InvalidOperationException("声音目录未配置 Ambient 混音组。");
            }

            if (defaultBgmFadeSeconds < 0f)
            {
                throw new InvalidOperationException("默认 BGM 淡化时长不能小于零。");
            }

            if (initialSfxPoolSize < 1)
            {
                throw new InvalidOperationException("初始 SFX 池容量必须至少为一。");
            }

            Dictionary<SoundId, SoundDefinition> lookup = new Dictionary<SoundId, SoundDefinition>();
            foreach (SoundDefinition sound in sounds)
            {
                sound.Validate();
                lookup.Add(sound.Id, sound);
            }

            return lookup;
        }
    }
}
