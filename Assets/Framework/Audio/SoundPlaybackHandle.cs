using System;

namespace GameMain2.Framework.Audio
{
    /// <summary>
    /// 表示单次声音播放请求的可观察状态。
    /// </summary>
    public sealed class SoundPlaybackHandle
    {
        public long RequestId { get; }
        public SoundId SoundId { get; }
        public SoundCategory Category { get; }
        public SoundPlaybackState State { get; private set; }
        internal int InstanceId { get; private set; }
        internal uint Version { get; private set; }
        internal bool IsTerminal => State == SoundPlaybackState.Completed
                                    || State == SoundPlaybackState.Canceled
                                    || State == SoundPlaybackState.Failed;

        /// <summary>
        /// 创建处于加载状态的声音播放请求。
        /// </summary>
        internal SoundPlaybackHandle(long requestId, SoundId soundId, SoundCategory category)
        {
            RequestId = requestId;
            SoundId = soundId;
            Category = category;
            State = SoundPlaybackState.Loading;
        }

        /// <summary>
        /// 绑定承载本次播放的池实例标识和版本。
        /// </summary>
        internal void BindInstance(int instanceId, uint version)
        {
            InstanceId = instanceId;
            Version = version;
        }

        /// <summary>
        /// 将加载中的请求迁移为播放中。
        /// </summary>
        internal void MarkPlaying()
        {
            TransitionTo(SoundPlaybackState.Loading, SoundPlaybackState.Playing);
        }

        /// <summary>
        /// 将播放中的请求迁移为淡出中。
        /// </summary>
        internal void MarkFadingOut()
        {
            TransitionTo(SoundPlaybackState.Playing, SoundPlaybackState.FadingOut);
        }

        /// <summary>
        /// 将播放中或淡出中的请求迁移为已完成。
        /// </summary>
        internal void MarkCompleted()
        {
            if (State != SoundPlaybackState.Playing && State != SoundPlaybackState.FadingOut)
            {
                throw new InvalidOperationException($"声音请求 {RequestId} 不能从 {State} 迁移到 {SoundPlaybackState.Completed}。");
            }

            State = SoundPlaybackState.Completed;
        }

        /// <summary>
        /// 将加载中的请求迁移为已取消。
        /// </summary>
        internal void MarkCanceled()
        {
            TransitionTo(SoundPlaybackState.Loading, SoundPlaybackState.Canceled);
        }

        /// <summary>
        /// 将加载中的请求迁移为失败。
        /// </summary>
        internal void MarkFailed()
        {
            TransitionTo(SoundPlaybackState.Loading, SoundPlaybackState.Failed);
        }

        /// <summary>
        /// 校验并执行指定来源状态的迁移。
        /// </summary>
        private void TransitionTo(SoundPlaybackState expectedState, SoundPlaybackState targetState)
        {
            if (State != expectedState)
            {
                throw new InvalidOperationException($"声音请求 {RequestId} 不能从 {State} 迁移到 {targetState}。");
            }

            State = targetState;
        }
    }
}
