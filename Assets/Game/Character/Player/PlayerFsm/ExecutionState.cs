using GameMain2.Framework.Core.FSM;
using GameMain2.Framework.Core;
using GameMain2.Game.EventArgs;
using GameMain2.Scripts.Character;
using UnityEngine;

namespace Game.Character.Player.PlayerFsm
{
    public sealed class ExecutionState : PlayerStateBase
    {
        /// <summary>进入处决状态，玩家保持战斗姿态并接管处决动画的 Root Motion 水平位移。</summary>
        public override void Enter(FsmBase<PlayerStateMachine> fsm)
        {
            fsm.Owner.CurState = PlayerState.Execution;
            fsm.Owner.PlayerController.useGravity = false;
            EventCenter.Instance.Subscribe(PlayerRootMotionEventArgs.EventId, OnAnimatorMove);
        }

        /// <summary>处决 Timeline 结束后回到 Locomotion，其他玩家操作由输入锁统一屏蔽。</summary>
        public override void Update(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
            if (fsm.Owner.ExecutionController == null || !fsm.Owner.ExecutionController.IsPlaying)
            {
                fsm.ChangeState<LocomotionState>();
            }
        }

        /// <summary>离开处决状态时取消 Root Motion 位移接管并恢复玩家重力。</summary>
        public override void Exit(FsmBase<PlayerStateMachine> fsm)
        {
            EventCenter.TryUnSubscribe(PlayerRootMotionEventArgs.EventId, OnAnimatorMove);
            fsm.Owner.PlayerController.useGravity = true;
        }

        /// <summary>处决动画只应用水平 Root Motion 位移，旋转继续由处决对位逻辑控制。</summary>
        private void OnAnimatorMove(object sender, EventArgsBase e)
        {
            PlayerStateMachine stateMachine = (PlayerStateMachine)sender;
            PlayerRootMotionEventArgs eventArgs = (PlayerRootMotionEventArgs)e;
            Vector3 deltaPosition = eventArgs.Position;
            stateMachine.PlayerController.Move(new Vector3(deltaPosition.x, 0f, deltaPosition.z));
        }
    }
}
