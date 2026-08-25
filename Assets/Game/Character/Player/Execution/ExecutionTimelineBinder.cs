using System;
using System.Collections.Generic;
using Cinemachine;
using Game.Timeline.Execution;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Game.Character.Player.Execution
{
    public static class ExecutionTimelineBinder
    {
        private const string PlayerTrackKey = "Player";
        private const string EnemyTrackKey = "Enemy";

        /// <summary>按 Track 类型和名称约定绑定本次处决 Timeline 的运行时对象，并注入场景处决相机。</summary>
        public static void Bind(
            PlayableDirector director,
            Animator playerAnimator,
            Animator enemyAnimator,
            ExecutionTransformTarget transformTarget,
            CinemachineBrain cinemachineBrain,
            CinemachineVirtualCameraBase executionVirtualCamera)
        {
            TimelineAsset timeline = director.playableAsset as TimelineAsset;
            if (timeline == null)
            {
                Debug.LogError("处决 Timeline 资源必须是 TimelineAsset。", director);
                return;
            }

            bool playerAnimationBound = false;
            bool enemyAnimationBound = false;
            List<AnimationTrack> unnamedAnimationTracks = new List<AnimationTrack>();

            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track is ExecutionTransformTrack)
                {
                    director.SetGenericBinding(track, transformTarget);
                    continue;
                }

                if (track is CinemachineTrack)
                {
                    BindCinemachineTrack(director, (CinemachineTrack)track, cinemachineBrain, executionVirtualCamera);
                    continue;
                }

                AnimationTrack animationTrack = track as AnimationTrack;
                if (animationTrack == null)
                {
                    continue;
                }

                if (!playerAnimationBound && TrackNameContains(animationTrack, PlayerTrackKey))
                {
                    BindAnimationTrack(director, animationTrack, playerAnimator);
                    playerAnimationBound = true;
                    continue;
                }

                if (!enemyAnimationBound && TrackNameContains(animationTrack, EnemyTrackKey))
                {
                    BindAnimationTrack(director, animationTrack, enemyAnimator);
                    enemyAnimationBound = true;
                    continue;
                }

                unnamedAnimationTracks.Add(animationTrack);
            }

            BindUnnamedAnimationTracks(
                director,
                unnamedAnimationTracks,
                playerAnimator,
                enemyAnimator,
                playerAnimationBound,
                enemyAnimationBound);
        }

        /// <summary>绑定 Cinemachine 轨道到主相机 Brain，并把每个 Shot 的 ExposedReference 指向处决虚拟相机。</summary>
        private static void BindCinemachineTrack(
            PlayableDirector director,
            CinemachineTrack track,
            CinemachineBrain cinemachineBrain,
            CinemachineVirtualCameraBase executionVirtualCamera)
        {
            director.SetGenericBinding(track, cinemachineBrain);
            foreach (TimelineClip clip in track.GetClips())
            {
                CinemachineShot shot = clip.asset as CinemachineShot;
                if (shot == null)
                {
                    continue;
                }

                director.SetReferenceValue(shot.VirtualCamera.exposedName, executionVirtualCamera);
            }
        }

        /// <summary>判断 Timeline 轨道名是否包含指定关键字，忽略大小写方便资源命名。</summary>
        private static bool TrackNameContains(TrackAsset track, string key)
        {
            return !string.IsNullOrEmpty(track.name)
                && track.name.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>为默认命名的动画轨道按顺序补绑定，第一条绑定玩家，第二条绑定敌人。</summary>
        private static void BindUnnamedAnimationTracks(
            PlayableDirector director,
            List<AnimationTrack> tracks,
            Animator playerAnimator,
            Animator enemyAnimator,
            bool playerAnimationBound,
            bool enemyAnimationBound)
        {
            int nextTrackIndex = 0;
            if (!playerAnimationBound && nextTrackIndex < tracks.Count)
            {
                BindAnimationTrack(director, tracks[nextTrackIndex], playerAnimator);
                nextTrackIndex++;
            }

            if (!enemyAnimationBound && nextTrackIndex < tracks.Count)
            {
                BindAnimationTrack(director, tracks[nextTrackIndex], enemyAnimator);
                nextTrackIndex++;
            }

            if (nextTrackIndex < tracks.Count)
            {
                Debug.LogWarning("处决 Timeline 存在超过两个未命名 AnimationTrack，请把额外轨道命名为 Player 或 Enemy 后再绑定。", director);
            }
        }

        /// <summary>绑定动画轨道并使用场景偏移，避免运行时绑定角色被 Timeline 拉向轨道原点。</summary>
        private static void BindAnimationTrack(PlayableDirector director, AnimationTrack track, Animator animator)
        {
            track.trackOffset = TrackOffset.ApplySceneOffsets;
            director.SetGenericBinding(track, animator);
        }
    }
}
