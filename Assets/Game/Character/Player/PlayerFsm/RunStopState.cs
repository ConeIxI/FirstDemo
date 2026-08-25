using GameMain2.Framework.Core.FSM;
using GameMain2.Scripts.Character;
using UnityEngine;

namespace Game.Character.Player.PlayerFsm
{
    public class RunStopState : PlayerStateBase
    {

        /// <summary>进入急停状态并播放急停动画。</summary>
        public override void Enter(FsmBase<PlayerStateMachine> fsm)
        {
            fsm.Owner.CurState = PlayerState.RunStop;
            fsm.Owner.CrossFadeInFixedTime("RunStop");
        }

        /// <summary>急停过程中优先处理有效闪避输入，急停动画结束后回到移动状态。</summary>
        public override void Update(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
            if (TryStartRoll(fsm))
            {
                return;
            }

            if (fsm.Owner.IsPlayingAnimation("RunStop", out float animProgress))
            {
                if (animProgress >= 1)
                {
                    fsm.ChangeState<LocomotionState>();
                }
            }
        }

        /// <summary>退出急停状态时无需额外清理。</summary>
        public override void Exit(FsmBase<PlayerStateMachine> fsm)
        {
            
        }
    }
}
