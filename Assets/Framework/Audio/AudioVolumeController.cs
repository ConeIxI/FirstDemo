using System;
using UnityEngine;
using UnityEngine.Audio;

namespace GameMain2.Framework.Audio
{
    /// <summary>
    /// 管理主音量、背景音乐、环境音和音效四路混音参数的持久化与应用。
    /// </summary>
    public sealed class AudioVolumeController
    {
        private const string MasterVolumePreferenceKey = "Audio_MasterVolume";
        private const string BgmVolumePreferenceKey = "Audio_BgmVolume";
        private const string AmbientVolumePreferenceKey = "Audio_AmbientVolume";
        private const string SfxVolumePreferenceKey = "Audio_SfxVolume";
        private const string MasterVolumeParameterName = "MasterVolume";
        private const string BgmVolumeParameterName = "BgmVolume";
        private const string AmbientVolumeParameterName = "AmbientVolume";
        private const string SfxVolumeParameterName = "SfxVolume";

        private AudioMixer mixer;
        private float masterVolume;
        private float bgmVolume;
        private float ambientVolume;
        private float sfxVolume;

        /// <summary>
        /// 获取主混音通道的线性音量。
        /// </summary>
        public float MasterVolume => masterVolume;

        /// <summary>
        /// 获取背景音乐混音通道的线性音量。
        /// </summary>
        public float BgmVolume => bgmVolume;

        /// <summary>
        /// 获取音效混音通道的线性音量。
        /// </summary>
        public float SfxVolume => sfxVolume;

        /// <summary>
        /// 获取环境音混音通道的线性音量。
        /// </summary>
        public float AmbientVolume => ambientVolume;

        /// <summary>
        /// 从本地偏好设置读取四路音量，缺省值均为一。
        /// </summary>
        public AudioVolumeController()
        {
            masterVolume = PlayerPrefs.GetFloat(MasterVolumePreferenceKey, 1f);
            bgmVolume = PlayerPrefs.GetFloat(BgmVolumePreferenceKey, 1f);
            ambientVolume = PlayerPrefs.GetFloat(AmbientVolumePreferenceKey, 1f);
            sfxVolume = PlayerPrefs.GetFloat(SfxVolumePreferenceKey, 1f);
        }

        /// <summary>
        /// 绑定混音器并立即应用当前保存的四路音量。
        /// </summary>
        public void AttachMixer(AudioMixer mixer)
        {
            this.mixer = mixer;
            ApplyMixerVolume(MasterVolumeParameterName, masterVolume);
            ApplyMixerVolume(BgmVolumeParameterName, bgmVolume);
            ApplyMixerVolume(AmbientVolumeParameterName, ambientVolume);
            ApplyMixerVolume(SfxVolumeParameterName, sfxVolume);
        }

        /// <summary>
        /// 设置主混音通道音量并保存到本地偏好设置。
        /// </summary>
        public void SetMasterVolume(float value)
        {
            ValidateVolume(value);
            masterVolume = value;
            ApplyMixerVolume(MasterVolumeParameterName, value);
            PlayerPrefs.SetFloat(MasterVolumePreferenceKey, value);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 设置背景音乐混音通道音量并保存到本地偏好设置。
        /// </summary>
        public void SetBgmVolume(float value)
        {
            ValidateVolume(value);
            bgmVolume = value;
            ApplyMixerVolume(BgmVolumeParameterName, value);
            PlayerPrefs.SetFloat(BgmVolumePreferenceKey, value);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 设置环境音混音通道音量并保存到本地偏好设置。
        /// </summary>
        public void SetAmbientVolume(float value)
        {
            ValidateVolume(value);
            ambientVolume = value;
            ApplyMixerVolume(AmbientVolumeParameterName, value);
            PlayerPrefs.SetFloat(AmbientVolumePreferenceKey, value);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 设置音效混音通道音量并保存到本地偏好设置。
        /// </summary>
        public void SetSfxVolume(float value)
        {
            ValidateVolume(value);
            sfxVolume = value;
            ApplyMixerVolume(SfxVolumeParameterName, value);
            PlayerPrefs.SetFloat(SfxVolumePreferenceKey, value);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 校验线性音量值位于有效区间内。
        /// </summary>
        private static void ValidateVolume(float value)
        {
            if (float.IsNaN(value) || value < 0f || value > 1f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "音量必须位于 0 到 1 之间。");
            }
        }

        /// <summary>
        /// 将线性音量转换为分贝后写入已绑定的混音器参数。
        /// </summary>
        private void ApplyMixerVolume(string parameterName, float value)
        {
            if (mixer != null)
            {
                mixer.SetFloat(parameterName, ConvertToDecibels(value));
            }
        }

        /// <summary>
        /// 将线性音量转换为 Audio Mixer 使用的分贝值。
        /// </summary>
        private static float ConvertToDecibels(float value)
        {
            return value == 0f ? -80f : Mathf.Log10(value) * 20f;
        }
    }
}
