using System;
using System.Collections;
using UnityEngine;

namespace GameMain2.Framework.Audio
{
    /// <summary>
    /// 空间环境音播放组件，挂到场景物体后按物体位置播放带距离衰减的循环环境音。
    /// </summary>
    public sealed class SpatialAmbientSoundPlayer : MonoBehaviour
    {
        [SerializeField] private SoundId ambientSoundId = SoundId.BirdieEnv;
        [SerializeField, Min(0f)] private float fadeSeconds = 1f;
        [SerializeField] private bool playOnEnable = true;
        [SerializeField] private bool stopOnDisable = true;
        [SerializeField] private bool followTransform = true;

        private SoundPlaybackHandle playbackHandle;
        private Coroutine delayedPlayCoroutine;

        /// <summary>组件启用时延后一帧播放空间环境音，避开场景切换清理。</summary>
        private void OnEnable()
        {
            if (playOnEnable)
            {
                RequestDelayedPlay();
            }
        }

        /// <summary>组件禁用时取消未执行播放请求，并按配置淡出停止空间环境音。</summary>
        private void OnDisable()
        {
            CancelDelayedPlay();
            if (stopOnDisable)
            {
                Stop();
            }
        }

        /// <summary>按当前配置播放空间环境音，默认跟随本组件 Transform。</summary>
        public void Play()
        {
            CancelDelayedPlay();
            ValidateAmbientSoundId();
            playbackHandle = followTransform
                ? SoundManager.Instance.PlaySpatialAmbientFollow(ambientSoundId, transform, fadeSeconds)
                : SoundManager.Instance.PlaySpatialAmbientAt(ambientSoundId, transform.position, fadeSeconds);
        }

        /// <summary>停止本组件发起的空间环境音请求或活动播放。</summary>
        public void Stop()
        {
            if (playbackHandle == null)
            {
                return;
            }

            if (SoundManager.TryGetInstance(out SoundManager soundManager))
            {
                soundManager.StopSpatialAmbient(playbackHandle, fadeSeconds);
            }

            playbackHandle = null;
        }

        /// <summary>登记下一帧播放请求，确保场景切换清理完成后再播放。</summary>
        private void RequestDelayedPlay()
        {
            CancelDelayedPlay();
            delayedPlayCoroutine = StartCoroutine(PlayAfterSceneActivation());
        }

        /// <summary>取消尚未执行的延迟播放请求。</summary>
        private void CancelDelayedPlay()
        {
            if (delayedPlayCoroutine == null)
            {
                return;
            }

            StopCoroutine(delayedPlayCoroutine);
            delayedPlayCoroutine = null;
        }

        /// <summary>等待一帧后发起播放，避免 pending 请求被场景切换回调取消。</summary>
        private IEnumerator PlayAfterSceneActivation()
        {
            yield return null;
            delayedPlayCoroutine = null;
            Play();
        }

        /// <summary>校验空间环境音 ID 必须有效。</summary>
        private void ValidateAmbientSoundId()
        {
            if (ambientSoundId == SoundId.None)
            {
                throw new InvalidOperationException($"{name} 未配置空间环境音 SoundId。");
            }
        }
    }
}
