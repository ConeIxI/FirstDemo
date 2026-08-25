using System;
using Cinemachine;
using Game.Character.Enemy.AI;
using Game.Character.Enemy.Components;
using GameMain2.Framework.Audio;
using GameMain2.Scripts.Character;
using GameMain2.Scripts.UI;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Game.Timeline.Boss
{
    [RequireComponent(typeof(PlayableDirector))]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class BossTimelineTriggerPlayer : MonoBehaviour
    {
        private const string PlayerTag = "Player";
        private const float BossBattleBgmFadeSeconds = 1f;

        [SerializeField] private float cameraBlendDuration = 1f;
        [SerializeField] private CinemachineBlendDefinition.Style cameraBlendStyle =
            CinemachineBlendDefinition.Style.EaseInOut;
        [SerializeField] private AIController bossAIController;
        [SerializeField] private EnemyAttributeComponent bossAttribute;

        private PlayableDirector director;
        private BoxCollider triggerCollider;
        private Transform playerTarget;
        private bool hasPlayed;
        private bool gameplayInputBlocked;
        private CinemachineBrain timelineBrain;
        private CinemachineBlendDefinition previousBrainBlend;
        private bool brainBlendOverridden;

        /// <summary>初始化 Boss Timeline 触发依赖，确保触发器开启并关闭自动播放。</summary>
        private void Awake()
        {
            director = GetComponent<PlayableDirector>();
            triggerCollider = GetComponent<BoxCollider>();

            if (director == null)
            {
                throw new InvalidOperationException($"{name} 缺少 PlayableDirector，无法播放 Boss Timeline。");
            }

            if (triggerCollider == null)
            {
                throw new InvalidOperationException($"{name} 缺少 BoxCollider，无法触发 Boss Timeline。");
            }

            triggerCollider.isTrigger = true;
            director.playOnAwake = false;
            ResolveBossAIController();
            ResolveBossAttributeComponent();
            director.stopped += OnDirectorStopped;
        }

        /// <summary>组件禁用时释放 Timeline 输入锁，避免场景切换或对象隐藏后残留阻断。</summary>
        private void OnDisable()
        {
            ReleaseGameplayInputBlock();
            RestoreBrainBlend();
        }

        /// <summary>销毁时解除 Director 事件并释放 Timeline 输入锁。</summary>
        private void OnDestroy()
        {
            if (director != null)
            {
                director.stopped -= OnDirectorStopped;
            }

            ReleaseGameplayInputBlock();
            RestoreBrainBlend();
        }

        /// <summary>玩家进入 Boss Timeline 触发范围时，只播放一次 Boss Timeline。</summary>
        private void OnTriggerEnter(Collider other)
        {
            if (hasPlayed || !IsPlayerCollider(other))
            {
                return;
            }

            playerTarget = ResolvePlayerTarget(other);
            PlayTimeline();
        }

        /// <summary>绑定 Cinemachine Brain 后，从头播放 Boss Timeline。</summary>
        private void PlayTimeline()
        {
            hasPlayed = true;
            CinemachineBrain brain = GetMainCameraBrain();
            ApplyTimelineClipEase();
            BindCinemachineTracks(brain);
            OverrideBrainBlend(brain);
            director.time = 0d;
            director.RebuildGraph();
            BlockGameplayInput();
            director.Play();
        }

        /// <summary>Director 停止播放时释放玩法输入，让玩家恢复操作。</summary>
        private void OnDirectorStopped(PlayableDirector stoppedDirector)
        {
            if (stoppedDirector == director)
            {
                ReleaseGameplayInputBlock();
                RestoreBrainBlend();
                SetBossTargetToPlayer();
                ShowBossHealthPanel();
                PlayBossBattleBgm();
            }
        }

        /// <summary>解析 Boss 的 AI 控制器，未显式配置时使用场景中的 Boss AI。</summary>
        private void ResolveBossAIController()
        {
            if (bossAIController != null)
            {
                return;
            }

            bossAIController = FindObjectOfType<AIController>();
            if (bossAIController == null)
            {
                throw new InvalidOperationException($"{name} 找不到 Boss 的 AIController，无法在 Timeline 结束后设置目标。");
            }
        }

        /// <summary>解析 Boss 属性组件，供 Timeline 结束后打开 Boss 血条面板。</summary>
        private void ResolveBossAttributeComponent()
        {
            if (bossAttribute != null)
            {
                return;
            }

            bossAttribute = bossAIController.GetComponent<EnemyAttributeComponent>();
            if (bossAttribute == null)
            {
                bossAttribute = bossAIController.GetComponentInChildren<EnemyAttributeComponent>(true);
            }

            if (bossAttribute == null)
            {
                bossAttribute = bossAIController.GetComponentInParent<EnemyAttributeComponent>();
            }

            if (bossAttribute == null)
            {
                throw new InvalidOperationException($"{bossAIController.name} 缺少 EnemyAttributeComponent，无法显示 Boss 血条。");
            }
        }

        /// <summary>从触发碰撞体解析玩家根节点，保证 Boss 黑板记录的是玩家主体。</summary>
        private static Transform ResolvePlayerTarget(Collider other)
        {
            PlayerStateMachine playerStateMachine = other.GetComponentInParent<PlayerStateMachine>();
            return playerStateMachine != null ? playerStateMachine.transform : other.transform;
        }

        /// <summary>Boss Timeline 播放结束后，把 Boss 战斗目标切到触发 Timeline 的玩家。</summary>
        private void SetBossTargetToPlayer()
        {
            if (playerTarget == null)
            {
                throw new InvalidOperationException($"{name} 播放结束时缺少玩家目标，无法设置 Boss 目标。");
            }

            bossAIController.Blackboard.RememberTarget(playerTarget);
            bossAIController.Blackboard.SetTargetVisible(true);
            bossAIController.Blackboard.SetSearching(false);
        }

        /// <summary>Boss Timeline 播放完进入正式战斗时打开 Boss 血条面板。</summary>
        private void ShowBossHealthPanel()
        {
            UIManager.Instance.ShowBossHealth(bossAttribute, GetBossDisplayName());
        }

        /// <summary>Boss Timeline 播放完进入正式战斗时切换到 Boss 战背景音乐。</summary>
        private static void PlayBossBattleBgm()
        {
            SoundManager.Instance.PlayBgm(SoundId.BossBattleBgm, BossBattleBgmFadeSeconds);
        }

        /// <summary>读取 Boss 配置显示名，配置缺失时回退到场景对象名。</summary>
        private string GetBossDisplayName()
        {
            string displayName = bossAIController.Definition == null ? null : bossAIController.Definition.DisplayName;
            return string.IsNullOrWhiteSpace(displayName) ? bossAIController.name : displayName;
        }

        /// <summary>给 Boss Timeline 的 Cinemachine 镜头补默认淡入淡出，避免第一帧直接硬切。</summary>
        private void ApplyTimelineClipEase()
        {
            if (cameraBlendDuration <= 0f)
            {
                return;
            }

            TimelineAsset timeline = GetTimelineAsset();
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                CinemachineTrack cinemachineTrack = track as CinemachineTrack;
                if (cinemachineTrack == null)
                {
                    continue;
                }

                ApplyTrackClipEase(cinemachineTrack);
            }
        }

        /// <summary>给指定 CinemachineTrack 上未配置过渡的镜头片段补默认淡入淡出时长。</summary>
        private void ApplyTrackClipEase(CinemachineTrack track)
        {
            foreach (TimelineClip clip in track.GetClips())
            {
                if (clip.asset is not CinemachineShot)
                {
                    continue;
                }

                if (clip.easeInDuration <= 0d)
                {
                    clip.easeInDuration = cameraBlendDuration;
                }

                if (clip.easeOutDuration <= 0d)
                {
                    clip.easeOutDuration = cameraBlendDuration;
                }
            }
        }

        /// <summary>播放 Boss Timeline 期间临时改写 Brain 默认混合，结束后会恢复原配置。</summary>
        private void OverrideBrainBlend(CinemachineBrain brain)
        {
            if (cameraBlendDuration <= 0f)
            {
                return;
            }

            if (brainBlendOverridden)
            {
                return;
            }

            timelineBrain = brain;
            previousBrainBlend = brain.m_DefaultBlend;
            brain.m_DefaultBlend = new CinemachineBlendDefinition(cameraBlendStyle, cameraBlendDuration);
            brainBlendOverridden = true;
        }

        /// <summary>恢复玩家主相机 Brain 原本的默认混合配置。</summary>
        private void RestoreBrainBlend()
        {
            if (!brainBlendOverridden)
            {
                return;
            }

            timelineBrain.m_DefaultBlend = previousBrainBlend;
            timelineBrain = null;
            brainBlendOverridden = false;
        }

        /// <summary>播放 Boss Timeline 前阻断移动、攻击、锁定和视角输入，暂停快捷键仍由 UIManager 响应。</summary>
        private void BlockGameplayInput()
        {
            if (gameplayInputBlocked)
            {
                return;
            }

            UIManager.Instance.PushGameplayInputBlock();
            gameplayInputBlocked = true;
        }

        /// <summary>Boss Timeline 停止或组件退出时释放玩法输入阻断。</summary>
        private void ReleaseGameplayInputBlock()
        {
            if (!gameplayInputBlocked)
            {
                return;
            }

            UIManager.Instance.PopGameplayInputBlock();
            gameplayInputBlocked = false;
        }

        /// <summary>把 Timeline 中所有 CinemachineTrack 绑定到玩家主相机的 CinemachineBrain。</summary>
        private void BindCinemachineTracks(CinemachineBrain brain)
        {
            TimelineAsset timeline = GetTimelineAsset();
            bool boundTrack = false;

            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                CinemachineTrack cinemachineTrack = track as CinemachineTrack;
                if (cinemachineTrack == null)
                {
                    continue;
                }

                director.SetGenericBinding(cinemachineTrack, brain);
                boundTrack = true;
            }

            if (!boundTrack)
            {
                throw new InvalidOperationException($"{name} 的 Timeline 缺少 CinemachineTrack，无法绑定 Boss 相机轨道。");
            }
        }

        /// <summary>获取当前 PlayableDirector 的 TimelineAsset，配置错误时立即失败。</summary>
        private TimelineAsset GetTimelineAsset()
        {
            TimelineAsset timeline = director.playableAsset as TimelineAsset;
            if (timeline == null)
            {
                throw new InvalidOperationException($"{name} 的 PlayableDirector 必须绑定 TimelineAsset。");
            }

            return timeline;
        }

        /// <summary>获取玩家主相机上的 CinemachineBrain，缺失时立即暴露场景配置问题。</summary>
        private CinemachineBrain GetMainCameraBrain()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                throw new InvalidOperationException("Boss Timeline 播放前找不到 MainCamera。");
            }

            CinemachineBrain brain = mainCamera.GetComponent<CinemachineBrain>();
            if (brain == null)
            {
                throw new InvalidOperationException($"{mainCamera.name} 缺少 CinemachineBrain，无法绑定 Boss Timeline。");
            }

            return brain;
        }

        /// <summary>判断进入触发器的碰撞体是否属于玩家。</summary>
        private static bool IsPlayerCollider(Collider other)
        {
            return other != null && other.CompareTag(PlayerTag);
        }
    }
}
