using Game.Battle.Ability;
using GameMain2.Framework.Core.FSM;
using GameMain2.Scripts.Character;

namespace Game.Character.Player.PlayerFsm
{
    public class UnbalanceState : PlayerStateBase
    {
        private const string DefenseBreakAnimationName = "DefenseBreak";
        private const string UnbalanceLoopAnimationName = "UnbalanceLoop";
        private const string UnbalanceEndAnimationName = "UnbalanceEnd";
        private const string UnbalanceEndTriggerName = "UnbalanceEnd";
        private const float UnbalanceLoopDuration = 0.8f;
        private bool m_hasEnteredUnbalanceLoop;
        private bool m_hasTriggeredUnbalanceEnd;
        private bool m_hasEnteredUnbalanceEnd;
        private float m_unbalanceLoopElapsedTime;

        /// <summary>进入玩家失衡状态，手动播放 DefenseBreak 入口动画但不设置 UnbalanceStart 参数。</summary>
        public override void Enter(FsmBase<PlayerStateMachine> fsm)
        {
            ResetRuntimeState();
            fsm.Owner.CurState = PlayerState.Unbalance;
            fsm.Owner.PlayerController.AbilitySystem.AddTag(CombatTag.Unbalanced);
            fsm.Owner.CrossFadeInFixedTime(DefenseBreakAnimationName);
        }

        /// <summary>等待动画状态机进入失衡循环，满 0.8 秒后触发 UnbalanceEnd 并在结束动画播完后退出。</summary>
        public override void Update(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
            if (!m_hasTriggeredUnbalanceEnd)
            {
                TickUnbalanceLoop(fsm.Owner, deltaTime);
                return;
            }

            if (HasUnbalanceEndFinished(fsm.Owner))
            {
                RestoreStabilityToFull(fsm.Owner);
                fsm.ChangeState<LocomotionState>();
            }
        }

        /// <summary>失衡循环播放期间累计时间，达到配置时长后只设置结束 Trigger。</summary>
        private void TickUnbalanceLoop(PlayerStateMachine player, float deltaTime)
        {
            if (!player.IsPlayingAnimation(UnbalanceLoopAnimationName, out _))
            {
                return;
            }

            if (!m_hasEnteredUnbalanceLoop)
            {
                m_hasEnteredUnbalanceLoop = true;
                m_unbalanceLoopElapsedTime = 0f;
            }

            m_unbalanceLoopElapsedTime += deltaTime;
            if (m_unbalanceLoopElapsedTime < UnbalanceLoopDuration)
            {
                return;
            }

            player.TrySetTrigger(UnbalanceEndTriggerName);
            m_hasTriggeredUnbalanceEnd = true;
        }

        /// <summary>确认 UnbalanceEnd 动画已进入并完整播放，避免刚触发过渡时提前退出。</summary>
        private bool HasUnbalanceEndFinished(PlayerStateMachine player)
        {
            if (!player.IsPlayingAnimation(UnbalanceEndAnimationName, out float normalizedTime))
            {
                return m_hasEnteredUnbalanceEnd;
            }

            m_hasEnteredUnbalanceEnd = true;
            return normalizedTime >= 1f;
        }

        /// <summary>玩家失衡动画自然结束时，将稳定值立刻恢复到上限。</summary>
        private void RestoreStabilityToFull(PlayerStateMachine player)
        {
            if (player == null || player.PlayerController == null)
            {
                return;
            }

            CombatAbilitySystem abilitySystem = player.PlayerController.AbilitySystem;
            if (abilitySystem == null || abilitySystem.Attributes == null)
            {
                return;
            }

            abilitySystem.Attributes.RestoreStability(abilitySystem.Attributes.MaxStability);
            abilitySystem.RemoveTag(CombatTag.Unbalanced);
        }

        /// <summary>重置本次失衡动画流程的运行时标记，避免重复进入状态时沿用旧计时。</summary>
        private void ResetRuntimeState()
        {
            m_hasEnteredUnbalanceLoop = false;
            m_hasTriggeredUnbalanceEnd = false;
            m_hasEnteredUnbalanceEnd = false;
            m_unbalanceLoopElapsedTime = 0f;
        }

        /// <summary>退出玩家失衡状态时清理本次动画流程标记。</summary>
        public override void Exit(FsmBase<PlayerStateMachine> fsm)
        {
            ResetRuntimeState();
        }
    }
}
