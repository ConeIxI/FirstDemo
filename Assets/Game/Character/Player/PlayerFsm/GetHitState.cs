using Game.Character;
using GameMain2.Framework.Core.FSM;
using GameMain2.Scripts.Character;

namespace Game.Character.Player.PlayerFsm
{
    public class GetHitState : PlayerStateBase
    {
        private const string DefaultAnimationName = "GetHit";
        private const float DefaultFallbackDelay = 0.1f;
        private const float FallbackExitDuration = 0.5f;
        private string m_animationName = DefaultAnimationName;
        private float m_elapsedTime;
        private bool m_hasTriedDefaultFallback;

        /// <summary>
        /// 进入玩家受击状态，并播放本次战斗结算指定的受击动画。
        /// </summary>
        public override void Enter(FsmBase<PlayerStateMachine> fsm)
        {
            CharacterStateMachine.PendingHitReaction reaction = fsm.Owner.ConsumePendingHitReaction();
            fsm.Owner.ApplyHitReactionBlendTreeParameters(
                reaction.IsCombat,
                reaction.HitWeight,
                reaction.HitDirection);
            m_animationName = DefaultAnimationName;
            m_elapsedTime = 0f;
            m_hasTriedDefaultFallback = true;
            fsm.Owner.TryCrossFadeInFixedTime(m_animationName);
        }

        /// <summary>
        /// 受击动画播放结束后回到待机状态。
        /// </summary>
        public override void Update(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
            m_elapsedTime += deltaTime;
            if (fsm.Owner.IsPlayingAnimation(m_animationName, out float time))
            {
                if (time >= 1f)
                {
                    fsm.ChangeState<LocomotionState>();
                }

                return;
            }

            if (!m_hasTriedDefaultFallback && m_elapsedTime >= DefaultFallbackDelay)
            {
                m_hasTriedDefaultFallback = true;
                m_animationName = DefaultAnimationName;
                if (fsm.Owner.TryCrossFadeInFixedTime(m_animationName))
                {
                    m_elapsedTime = 0f;
                    return;
                }
            }

            if (m_elapsedTime >= FallbackExitDuration)
            {
                fsm.ChangeState<LocomotionState>();
            }
        }

        /// <summary>
        /// 退出玩家受击状态时恢复默认动画名。
        /// </summary>
        public override void Exit(FsmBase<PlayerStateMachine> fsm)
        {
            m_animationName = DefaultAnimationName;
            m_elapsedTime = 0f;
            m_hasTriedDefaultFallback = false;
        }
    }
}
