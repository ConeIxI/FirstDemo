using System;
using System.Collections;
using UnityEngine;

namespace GameMain2.Framework.Audio
{
    /// <summary>
    /// 场景环境音播放组件，挂到场景或预制体后按生命周期播放和停止环境音。
    /// </summary>
    public sealed class AmbientSoundPlayer : MonoBehaviour
    {
        [SerializeField] private SoundId ambientSoundId = SoundId.BirdieEnv;
        [SerializeField, Min(0f)] private float fadeSeconds = 1f;
        [SerializeField] private bool playOnEnable = true;
        [SerializeField] private bool stopOnDisable = true;

        private SoundPlaybackHandle playbackHandle;
        private Coroutine delayedPlayCoroutine;

        /// <summary>组件启用时延后一帧请求播放环境音，避开场景切换阶段的声音清理。</summary>
        private void OnEnable()
        {
            if (playOnEnable)
            {
                RequestDelayedPlay();
            }
        }

        /// <summary>组件禁用或所在场景卸载时按配置淡出停止环境音。</summary>
        private void OnDisable()
        {
            CancelDelayedPlay();
            if (stopOnDisable)
            {
                Stop();
            }
        }

        /// <summary>播放当前配置的环境音，同一声音已在播放时由声音管理器复用当前请求。</summary>
        public void Play()
        {
            CancelDelayedPlay();
            ValidateAmbientSoundId();
            playbackHandle = SoundManager.Instance.PlayAmbient(ambientSoundId, fadeSeconds);
        }

        /// <summary>停止当前配置的环境音，优先停止本组件持有的播放请求。</summary>
        public void Stop()
        {
            if (!SoundManager.TryGetInstance(out SoundManager soundManager))
            {
                playbackHandle = null;
                return;
            }

            if (playbackHandle != null)
            {
                soundManager.Stop(playbackHandle, fadeSeconds);
                playbackHandle = null;
                return;
            }

            soundManager.StopAmbient(ambientSoundId, fadeSeconds);
        }

        /// <summary>登记下一帧播放请求，避免场景加载期间发出的 pending 环境音被场景切换回调取消。</summary>
        private void RequestDelayedPlay()
        {
            CancelDelayedPlay();
            delayedPlayCoroutine = StartCoroutine(PlayAfterSceneActivation());
        }

        /// <summary>取消尚未执行的延迟播放请求，防止禁用后的对象继续触发环境音。</summary>
        private void CancelDelayedPlay()
        {
            if (delayedPlayCoroutine == null)
            {
                return;
            }

            StopCoroutine(delayedPlayCoroutine);
            delayedPlayCoroutine = null;
        }

        /// <summary>等待一帧让场景切换清理完成，再发起环境音播放。</summary>
        private IEnumerator PlayAfterSceneActivation()
        {
            yield return null;
            delayedPlayCoroutine = null;
            Play();
        }

        /// <summary>校验环境音配置，避免预制体或场景对象缺少声音 ID 时静默失败。</summary>
        private void ValidateAmbientSoundId()
        {
            if (ambientSoundId == SoundId.None)
            {
                throw new InvalidOperationException($"{name} 未配置环境音 SoundId。");
            }
        }
    }
}
