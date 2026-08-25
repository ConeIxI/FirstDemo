using GameMain2.Framework.Core.FSM;
using GameMain2.Scripts.Character;

namespace Game.Character.Player.PlayerFsm
{
    public sealed class ItemGetState : PlayerStateBase
    {
        private const string ItemGetAnimation = "ItemGet";
        private bool m_hasStartedAnimation;
        private bool m_hasCompletedPickup;

        /// <summary>进入拾取状态，播放 ItemGet 动画并等待动画完成后结算拾取。</summary>
        public override void Enter(FsmBase<PlayerStateMachine> fsm)
        {
            fsm.Owner.CurState = PlayerState.ItemGet;
            m_hasStartedAnimation = false;
            m_hasCompletedPickup = false;
            if (!fsm.Owner.TryCrossFadeInFixedTime(ItemGetAnimation))
            {
                CompletePickup(fsm);
                fsm.ChangeState<LocomotionState>();
            }
        }

        /// <summary>等待 ItemGet 动画结束，结束后结算地面物品并回到 Locomotion。</summary>
        public override void Update(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
            if (fsm.Owner.IsPlayingAnimation(ItemGetAnimation, out float animProgress))
            {
                m_hasStartedAnimation = true;
                if (animProgress >= 1f)
                {
                    CompletePickup(fsm);
                    fsm.ChangeState<LocomotionState>();
                }

                return;
            }

            if (m_hasStartedAnimation)
            {
                CompletePickup(fsm);
                fsm.ChangeState<LocomotionState>();
            }
        }

        /// <summary>退出拾取状态时取消未完成的拾取请求。</summary>
        public override void Exit(FsmBase<PlayerStateMachine> fsm)
        {
            if (!m_hasCompletedPickup)
            {
                fsm.Owner.CancelPendingItemGet();
            }

            m_hasStartedAnimation = false;
            m_hasCompletedPickup = false;
        }

        /// <summary>结算一次待拾取地面物品，防止动画状态多次触发重复拾取。</summary>
        private void CompletePickup(FsmBase<PlayerStateMachine> fsm)
        {
            if (m_hasCompletedPickup)
            {
                return;
            }

            m_hasCompletedPickup = true;
            fsm.Owner.CompletePendingItemGet();
        }
    }
}
