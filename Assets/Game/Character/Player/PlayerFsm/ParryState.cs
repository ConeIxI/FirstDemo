using Game.Battle.Ability;
using GameMain2.Framework.Core.FSM;
using GameMain2.Framework.Manager;
using GameMain2.Scripts.Character;

namespace Game.Character.Player.PlayerFsm
{
    public sealed class ParryState : PlayerStateBase
    {
        private const string AnimationName = "Parry";
        private const float FallbackExitDuration = 0.35f;
        private float m_elapsedTime;
        private bool m_canAcceptCounterAttackInput;
        /// <summary>进入弹反成功状态，播放 Parry 动画并清理防御标签。</summary>
        public override void Enter(FsmBase<PlayerStateMachine> fsm)
        {
            fsm.Owner.CurState = PlayerState.Parry;
            m_elapsedTime = 0f;
            m_canAcceptCounterAttackInput = false;
            ClearDefenceTags(fsm);
            fsm.Owner.ApplyDefenceAnimatorState(false);
            fsm.Owner.TryCrossFadeInFixedTime(AnimationName);
        }

        /// <summary>弹反动画期间按普攻释放防御反击技能，动画结束后回到 Locomotion。</summary>
        public override void Update(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
            m_elapsedTime += UnityEngine.Mathf.Max(0f, deltaTime);
            if (!m_canAcceptCounterAttackInput)
            {
                m_canAcceptCounterAttackInput = true;
            }
            else
            {
                bool counterInputPressed = IsCounterAttackInputPressed();
                if (counterInputPressed && TryStartCounterAttack(fsm))
                {
                    return;
                }
            }

            if (fsm.Owner.IsPlayingAnimation(AnimationName, out float time))
            {
                if (time < 1f)
                {
                    return;
                }
            }
            else if (m_elapsedTime < FallbackExitDuration)
            {
                return;
            }

            fsm.ChangeState<LocomotionState>();
        }

        /// <summary>退出弹反状态时清理计时，防御标签由进入状态时立即关闭。</summary>
        public override void Exit(FsmBase<PlayerStateMachine> fsm)
        {
            m_elapsedTime = 0f;
            m_canAcceptCounterAttackInput = false;
        }

        /// <summary>读取防御反击输入。</summary>
        private bool IsCounterAttackInputPressed()
        {
            return InputManager.Instance.IsAttackKeyPressed();
        }

        /// <summary>尝试释放防御反击。</summary>
        private bool TryStartCounterAttack(FsmBase<PlayerStateMachine> fsm)
        {
            return TryStartDefenceCounterAttack(fsm);
        }

        /// <summary>清理成功弹反后不应继续保留的防御和弹反窗口标签。</summary>
        private static void ClearDefenceTags(FsmBase<PlayerStateMachine> fsm)
        {
            CombatAbilitySystem abilitySystem = fsm.Owner.PlayerController.AbilitySystem;
            abilitySystem.RemoveTag(CombatTag.Defending);
            abilitySystem.RemoveTimedTag(CombatTag.ParryWindow);
        }

    }
}
