using Cinemachine;
using Game.Battle.Ability;
using Game.Battle.Skill.Common;
using Game.Battle.Skill.Effects;
using Game.Character.Enemy.Components;
using Game.Character.Equipment;
using Game.Timeline.Execution;
using GameMain2.Scripts.Character;
using GameMain2.Scripts.UI;
using UnityEngine;
using UnityEngine.Playables;

namespace Game.Character.Player.Execution
{
    public sealed class PlayerExecutionController : MonoBehaviour, INotificationReceiver
    {
        private const string MissingWeaponError = "处决需要当前武器，但玩家没有可用武器。";
        private const string MissingTimelineError = "当前武器未配置专属处决 Timeline：";
        private const string MissingExecutionCameraError = "处决需要配置场景中的处决虚拟相机。";

        [SerializeField] private PlayerStateMachine stateMachine;
        [SerializeField] private PlayableDirector director;
        [SerializeField] private ExecutionTransformTarget transformTarget;
        [SerializeField] private CinemachineBrain cinemachineBrain;
        [SerializeField] private CinemachineVirtualCameraBase executionVirtualCamera;

        private ExecutionTarget m_target;
        private WeaponData m_weapon;
        private bool m_isPlaying;
        private bool m_damageResolved;
        private bool m_playerInputBlocked;
        private bool m_playerInvincible;
        private bool m_enemyLocked;

        public bool IsPlaying => m_isPlaying;

        /// <summary>初始化处决运行时依赖，缺失的 Director 和 Transform 绑定组件会挂到玩家对象上。</summary>
        private void Awake()
        {
            if (stateMachine == null)
            {
                TryGetComponent(out stateMachine);
            }

            if (director == null)
            {
                director = GetComponent<PlayableDirector>();
                if (director == null)
                {
                    director = gameObject.AddComponent<PlayableDirector>();
                }
            }

            if (transformTarget == null)
            {
                transformTarget = GetComponent<ExecutionTransformTarget>();
                if (transformTarget == null)
                {
                    transformTarget = gameObject.AddComponent<ExecutionTransformTarget>();
                }
            }

            if (cinemachineBrain == null && Camera.main != null)
            {
                cinemachineBrain = Camera.main.GetComponent<CinemachineBrain>();
            }

            director.playOnAwake = false;
            director.timeUpdateMode = DirectorUpdateMode.GameTime;
            director.stopped += OnDirectorStopped;
        }

        /// <summary>组件禁用时收束可能残留的处决状态。</summary>
        private void OnDisable()
        {
            CleanupExecution();
        }

        /// <summary>销毁时解除 Director 事件并收束可能残留的处决状态。</summary>
        private void OnDestroy()
        {
            if (director != null)
            {
                director.stopped -= OnDirectorStopped;
            }

            CleanupExecution();
        }

        /// <summary>尝试按当前武器和目标启动处决，配置缺失时直接报错并消费输入。</summary>
        public ExecutionStartResult TryStartExecution(ExecutionTarget target, WeaponData weapon)
        {
            if (m_isPlaying)
            {
                return ExecutionStartResult.Failed;
            }

            if (!target.IsValidUnbalancedTarget())
            {
                return ExecutionStartResult.NotFound;
            }

            if (weapon == null)
            {
                Debug.LogError(MissingWeaponError, this);
                return ExecutionStartResult.Failed;
            }

            PlayableAsset timeline = weapon.GetExecutionTimeline();
            if (timeline == null)
            {
                Debug.LogError(MissingTimelineError + weapon.weaponType, weapon);
                return ExecutionStartResult.Failed;
            }

            if (executionVirtualCamera == null)
            {
                Debug.LogError(MissingExecutionCameraError, this);
                return ExecutionStartResult.Failed;
            }

            m_target = target;
            m_weapon = weapon;
            m_damageResolved = false;
            m_isPlaying = true;

            stateMachine.EnterCombatImmediately();
            stateMachine.RefreshCombatActivity();
            LockPlayer();
            LockEnemy();
            BindAndPlayTimeline(timeline);
            return ExecutionStartResult.Started;
        }

        /// <summary>接收处决 Timeline 自定义通知，分发伤害结算和特效播放。</summary>
        public void OnNotify(Playable origin, INotification notification, object context)
        {
            if (notification is ExecutionDamageMarker)
            {
                ResolveExecutionDamageSignal();
                return;
            }

            if (notification is ExecutionEffectMarker effectMarker)
            {
                PlayExecutionEffect(effectMarker);
            }
        }

        /// <summary>供 Timeline SignalReceiver 直接调用，在处决中段只结算一次百分比伤害。</summary>
        public void ResolveExecutionDamageSignal()
        {
            if (m_damageResolved || !m_isPlaying)
            {
                return;
            }

            m_damageResolved = true;
            int damage = Mathf.CeilToInt(m_target.Attribute.MaxHealth * m_weapon.GetExecutionMaxHealthDamagePercent());
            stateMachine.PlayerController.AbilitySystem.ReportExecutionDamage(
                m_target.AbilitySystem,
                damage,
                m_target.Root.position);
        }

        /// <summary>按当前处决双方上下文播放 Timeline 标记指定的特效。</summary>
        private void PlayExecutionEffect(ExecutionEffectMarker marker)
        {
            if (!m_isPlaying)
            {
                return;
            }

            SkillEffectBinding binding = CreateExecutionEffectBinding(marker);
            Vector3 hitDirection = m_target.Root.position - stateMachine.PlayerController.transform.position;
            CombatEffectPlayContext context = CombatEffectPlayContext.ForExecution(
                binding,
                stateMachine.PlayerController.AbilitySystem,
                m_target.AbilitySystem,
                m_target.Root.position,
                hitDirection,
                this);
            CombatEffectService.Instance.Play(context);
        }

        /// <summary>把 Timeline 特效标记转换为通用战斗特效绑定。</summary>
        private static SkillEffectBinding CreateExecutionEffectBinding(ExecutionEffectMarker marker)
        {
            return new SkillEffectBinding
            {
                triggerKey = nameof(ExecutionEffectMarker),
                effectId = marker.EffectId,
                attachmentOverride = marker.AttachmentOverride,
                transformOverride = marker.TransformOverride,
            };
        }

        /// <summary>给玩家添加无敌标签并阻断除暂停外的玩法输入。</summary>
        private void LockPlayer()
        {
            CombatAbilitySystem abilitySystem = stateMachine.PlayerController.AbilitySystem;
            abilitySystem.AddTag(CombatTag.Invincible);
            m_playerInvincible = true;
            UIManager.Instance.PushGameplayInputBlock();
            m_playerInputBlocked = true;
        }

        /// <summary>锁住敌人 AI、移动和战斗动作，避免处决期间被普通逻辑覆盖。</summary>
        private void LockEnemy()
        {
            m_target.AIController.PushExecutionLock();
            m_target.Movement?.PushExecutionLock();
            m_target.Combat?.PushExecutionLock();
            m_enemyLocked = true;
        }

        /// <summary>绑定 Timeline 所需对象并从头播放处决。</summary>
        private void BindAndPlayTimeline(PlayableAsset timeline)
        {
            director.playableAsset = timeline;
            transformTarget.Bind(transform, stateMachine.PlayerController.Model, m_target.Root);
            ExecutionTimelineBinder.Bind(
                director,
                stateMachine.GetComponentInChildren<Animator>(),
                m_target.Animator,
                transformTarget,
                cinemachineBrain,
                executionVirtualCamera);
            director.time = 0d;
            director.RebuildGraph();
            director.Play();
        }

        /// <summary>Director 自然停止或异常停止时统一清理处决状态。</summary>
        private void OnDirectorStopped(PlayableDirector stoppedDirector)
        {
            if (stoppedDirector == director)
            {
                CleanupExecution();
            }
        }

        /// <summary>幂等清理玩家无敌、输入锁、敌人锁、处决特效和 Timeline Transform 绑定。</summary>
        private void CleanupExecution()
        {
            if (!m_isPlaying && !m_playerInvincible && !m_playerInputBlocked && !m_enemyLocked)
            {
                return;
            }

            if (m_playerInvincible && stateMachine != null && stateMachine.PlayerController != null)
            {
                stateMachine.PlayerController.AbilitySystem.RemoveTag(CombatTag.Invincible);
                m_playerInvincible = false;
            }

            if (m_playerInputBlocked)
            {
                UIManager.Instance.PopGameplayInputBlock();
                m_playerInputBlocked = false;
            }

            CompleteEnemyExecutionRecovery();
            StopExecutionEffects();

            if (m_enemyLocked)
            {
                if (m_target.AIController != null)
                {
                    m_target.AIController.PopExecutionLock();
                }

                m_target.Movement?.PopExecutionLock();
                m_target.Combat?.PopExecutionLock();
                m_enemyLocked = false;
            }

            if (transformTarget != null)
            {
                transformTarget.Clear();
            }

            m_target = default;
            m_weapon = null;
            m_damageResolved = false;
            m_isPlaying = false;
        }

        /// <summary>回收当前处决流程播放的所有持续或未结束特效。</summary>
        private void StopExecutionEffects()
        {
            if (CombatEffectService.Instance != null)
            {
                CombatEffectService.Instance.StopOwner(this);
            }
        }

        /// <summary>处决结束时让存活敌人退出失衡并请求播放起身动画，死亡敌人由死亡分支接管。</summary>
        private void CompleteEnemyExecutionRecovery()
        {
            if (!m_enemyLocked || m_target.Agent == null)
            {
                return;
            }

            EnemyLifeComponent life = m_target.Agent.GetComponent<EnemyLifeComponent>();
            if (life != null)
            {
                life.CompleteExecutionRecovery();
            }
        }
    }
}
