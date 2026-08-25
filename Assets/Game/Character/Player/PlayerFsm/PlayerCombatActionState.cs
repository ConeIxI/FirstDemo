using Game.Battle.Ability;
using Game.Battle.Combat.Config;
using Game.Battle.Skill.Common;
using GameMain2.Framework.Core;
using GameMain2.Framework.Core.FSM;
using GameMain2.Framework.Manager;
using GameMain2.Game.EventArgs;
using GameMain2.Scripts.Character;
using UnityEngine;

namespace Game.Character.Player.PlayerFsm
{
    public abstract class PlayerCombatActionState : PlayerStateBase
    {
        private const float NormalAttackInputBufferSeconds = 0.22f;
        private const float WeaponSkillInputBufferSeconds = 0.18f;
        private const float RollInputBufferSeconds = 0.12f;

        private readonly PlayerCombatInputBuffer m_inputBuffer = new PlayerCombatInputBuffer();
        private SkillConfig m_skillConfig;

        protected SkillConfig SkillConfig
        {
            get { return m_skillConfig; }
        }

        /// <summary>
        /// 进入玩家战斗动作状态，统一完成技能配置读取、释放校验、战斗动作标记和动画播放。
        /// </summary>
        public override void Enter(FsmBase<PlayerStateMachine> fsm)
        {
            // fsm.Owner.PlayerController.useGravity = false;
            fsm.Owner.CurState = PlayerState.Attack;
            fsm.Owner.ResetAttackDecisionWindow();
            m_inputBuffer.Clear();
            m_skillConfig = ResolveSkillConfig(fsm);
            if (m_skillConfig == null || !IsExpectedSkillType(m_skillConfig.SkillType))
            {
                fsm.ChangeState<LocomotionState>();
                return;
            }

            PlayerSkillManager skillManager = fsm.Owner.PlayerController.SkillManager;
            CombatAbilitySystem abilitySystem = fsm.Owner.PlayerController.AbilitySystem;

            if (abilitySystem == null || skillManager == null || !skillManager.HasSkill(m_skillConfig.skillId))
            {
                fsm.ChangeState<LocomotionState>();
                return;
            }

            if (abilitySystem.TryActivate(m_skillConfig) != AbilityActivationResult.Success)
            {
                fsm.ChangeState<LocomotionState>();
                return;
            }

            if (fsm.Owner.LockOnManager != null)
            {
                fsm.Owner.LockOnManager.TurnToCurrentTarget();
            }

            fsm.Owner.CrossFadeInFixedTime(m_skillConfig.skillAnimationName);
            EventCenter.Instance.Subscribe(PlayerRootMotionEventArgs.EventId, OnAnimtorMove);
        }

        /// <summary>
        /// 更新玩家战斗动作状态，等待动作动画播放并把具体状态的连段/结束规则交给子类。
        /// </summary>
        public override void Update(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
            if (m_skillConfig == null)
            {
                fsm.ChangeState<LocomotionState>();
                return;
            }

            RecordBufferedCombatInput(fsm);
            if (fsm.Owner.LockOnManager != null)
            {
                fsm.Owner.LockOnManager.TurnToCurrentTarget();
            }

            if (!fsm.Owner.IsPlayingAnimation(m_skillConfig.skillAnimationName, out float animProgress))
            {
                return;
            }

            if (IsAttackDecisionWindowOpen(fsm))
            {
                if (TryConsumeBufferedWeaponSkill(fsm))
                {
                    return;
                }

                if (TryConsumeBufferedRoll(fsm))
                {
                    fsm.ChangeState<DodgeState>();
                    return;
                }

                UpdateCombatAction(fsm, animProgress);
                if (TryStartRoll(fsm))
                {
                    return;
                }

                if (TryStartDefence(fsm))
                {
                    return;
                }

                return;
            }

            UpdateCombatAction(fsm, animProgress);
            
        }

        /// <summary>
        /// 退出玩家战斗动作状态，取消当前技能并释放战斗动作标记和根运动订阅。
        /// </summary>
        public override void Exit(FsmBase<PlayerStateMachine> fsm)
        {
            fsm.Owner.PlayerController.useGravity = true;
            fsm.Owner.ResetAttackDecisionWindow();
            if (fsm.Owner.PlayerController != null)
            {
                CombatAbilitySystem abilitySystem = fsm.Owner.PlayerController.AbilitySystem;
                if (abilitySystem != null)
                {
                    abilitySystem.CancelActiveAbility();
                }
            }

            fsm.Owner.DisableWeaponCollider();

            EventCenter.TryUnSubscribe(PlayerRootMotionEventArgs.EventId, OnAnimtorMove);
        }

        /// <summary>
        /// 子类提供要释放的技能配置，普通攻击和武器技能可以读取不同入口数据。
        /// </summary>
        protected abstract SkillConfig ResolveSkillConfig(FsmBase<PlayerStateMachine> fsm);

        /// <summary>
        /// 子类声明自己允许处理的技能类型，防止普通攻击状态误执行武器技能。
        /// </summary>
        protected abstract bool IsExpectedSkillType(SkillType skillType);

        /// <summary>
        /// 子类处理动画播放中的特定规则，例如普通攻击连段或技能结束。
        /// </summary>
        protected abstract void UpdateCombatAction(FsmBase<PlayerStateMachine> fsm, float animProgress);

        /// <summary>
        /// 子类处理 root motion 位移，普通攻击和技能可以使用不同移动规则。
        /// </summary>
        protected abstract void ApplyRootMotion(PlayerStateMachine stateMachine, PlayerRootMotionEventArgs eventArgs);

        /// <summary>
        /// 尝试消费普攻预输入，供普通攻击状态在连段窗口中切换下一段。
        /// </summary>
        protected bool TryConsumeBufferedNormalAttack()
        {
            return m_inputBuffer.TryConsumeNormalAttack(Time.time);
        }

        /// <summary>
        /// 尝试消费闪避预输入，供后摇决策窗口打开且冷却结束时切换到闪避状态。
        /// </summary>
        protected bool TryConsumeBufferedRoll(FsmBase<PlayerStateMachine> fsm)
        {
            return fsm.Owner.CanStartDodge() && m_inputBuffer.TryConsumeRoll(Time.time);
        }

        /// <summary>
        /// 判断当前攻击是否已经进入后摇决策窗口，窗口开启后才允许防御、闪避、技能和连段取消。
        /// </summary>
        protected bool IsAttackDecisionWindowOpen(FsmBase<PlayerStateMachine> fsm)
        {
            return fsm != null && fsm.Owner != null && fsm.Owner.isAttackDecisionWindowOpen;
        }

        /// <summary>
        /// 从状态机强类型动作请求中读取武器类型和技能 ID，并返回对应配置。
        /// </summary>
        protected SkillConfig ResolvePlayerSkillConfig(FsmBase<PlayerStateMachine> fsm)
        {
            PlayerCombatActionRequest request = fsm.Owner.CombatActionRequest;
            if (!request.IsValid)
            {
                return null;
            }

            return ConfigManager.Instance.GetPlayerSkillConfig(request.WeaponType, request.SkillId);
        }

        /// <summary>
        /// 记录攻击和武器技能输入；同帧同时存在时优先保留更明确的武器技能意图。
        /// </summary>
        private void RecordBufferedCombatInput(FsmBase<PlayerStateMachine> fsm)
        {
            int weaponSkillSlot = InputManager.Instance.GetPressedWeaponSkillSlot();
            if (weaponSkillSlot >= 0)
            {
                m_inputBuffer.RecordWeaponSkill(weaponSkillSlot, Time.time, WeaponSkillInputBufferSeconds);
                return;
            }

            if (InputManager.Instance.IsRollPressed() && HasDodgeMoveInput() && fsm.Owner.CanStartDodge())
            {
                m_inputBuffer.RecordRoll(Time.time, RollInputBufferSeconds);
                return;
            }

            if (InputManager.Instance.IsAttackKeyPressed())
            {
                m_inputBuffer.RecordNormalAttack(Time.time, NormalAttackInputBufferSeconds);
            }
        }

        /// <summary>
        /// 决策窗口打开后优先消费武器技能预输入；无效槽位会被丢弃，不在窗口内反复重试。
        /// </summary>
        private bool TryConsumeBufferedWeaponSkill(FsmBase<PlayerStateMachine> fsm)
        {
            if (!m_inputBuffer.TryConsumeWeaponSkill(Time.time, out int slotIndex))
            {
                return false;
            }

            return TryStartWeaponSkill(fsm, slotIndex);
        }

        /// <summary>
        /// 接收动画根运动事件，并把位移交给玩家控制器执行。
        /// </summary>
        private void OnAnimtorMove(object sender, EventArgsBase e)
        {
            PlayerStateMachine stateMachine = sender as PlayerStateMachine;
            if (stateMachine == null || stateMachine.PlayerController == null)
            {
                return;
            }

            PlayerRootMotionEventArgs eventArgs = (PlayerRootMotionEventArgs)e;
            ApplyRootMotion(stateMachine, eventArgs);
        }
    }
}
