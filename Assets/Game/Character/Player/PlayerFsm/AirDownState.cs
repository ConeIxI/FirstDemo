using GameMain2.Framework.Core;
using GameMain2.Framework.Core.FSM;
using GameMain2.Framework.Manager;
using GameMain2.Game.EventArgs;
using GameMain2.Scripts.Character;
using UnityEngine;

namespace Game.Character.Player.PlayerFsm
{
    public class AirDownState : PlayerStateBase
    {
        /// <summary>
        /// 当角色距离地面高度超过这个阈值，这可以播放落地动画；小于这个阈值则不播放
        /// </summary>
        private float m_landCheckHeight  = 3f;

        /// <summary>
        /// 标识是需要播放落地动画。
        /// 当角色从空中状态转换到地面状态时，此标志用于控制是否触发落地动画。
        /// </summary>
        private bool m_isLandAnimation;

        /// <summary>
        /// 用于调整角色位置偏移量的高度值，通常在进行地面检测时使用。
        /// 此值定义了从角色当前位置向上或向下偏移的距离，以确保更准确地检测到地面或其他物体。
        /// </summary>
        private float m_offestHeight = 0.5f;

        
        private enum AirState
        {
            Air,
            ground
        }

        private string m_jumpEnd = "";

        private AirState m_state;

        private bool m_IsMove;

        public override void Enter(FsmBase<PlayerStateMachine> fsm)
        {
            fsm.Owner.CurState = PlayerState.AirDown;
            fsm.Owner.CrossFadeInFixedTime("JumpLoop");
            EventCenter.Instance.Subscribe(PlayerRootMotionEventArgs.EventId,OnAnimtorDown);
            Transform playerTransform = fsm.Owner.gameObject.transform;
            m_isLandAnimation = !Physics.Raycast(playerTransform.position + new Vector3(0, m_offestHeight, 0),
                -playerTransform.up, m_landCheckHeight + m_offestHeight);
            m_state = AirState.Air;
        }

        public override void Update(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
            switch (m_state)
            {
                case AirState.Air:
                    AirMoveOnUpdate(fsm);
                    if (fsm.Owner.PlayerController.IsGrounded())
                    {
                        m_state = AirState.ground;
                        if (m_isLandAnimation)
                        {
                            fsm.Owner.CrossFadeInFixedTime("JumpEnd2");
                            m_jumpEnd = "JumpEnd2";
                            m_IsMove = false;
                        }
                        else
                        {
                            fsm.Owner.CrossFadeInFixedTime("JumpEnd1");
                            m_jumpEnd = "JumpEnd1";
                            m_IsMove = true;
                        }
                    }
                    break;
                case AirState.ground:
                    if (m_IsMove)
                        AirMoveOnUpdate(fsm);
                    if (fsm.Owner.IsPlayingAnimation(m_jumpEnd, out float animProgress))
                    {
                        if (animProgress >= 1f)
                        {
                            fsm.ChangeState<LocomotionState>();
                        }
                    }
                    break;
            }
        }

        public override void Exit(FsmBase<PlayerStateMachine> fsm)
        {
            EventCenter.TryUnSubscribe(PlayerRootMotionEventArgs.EventId,OnAnimtorDown);
            if (fsm.Owner != null && fsm.Owner.PlayerController != null)
            {
                fsm.Owner.PlayerController.useGravity = true;
            }
        }
        
        private void OnAnimtorDown(object sender, EventArgsBase e)
        {
            PlayerStateMachine s = sender as PlayerStateMachine;
            if (s == null || s.PlayerController == null)
            {
                return;
            }

            PlayerController playerMovement = s.PlayerController;
            PlayerRootMotionEventArgs eventArgs = (PlayerRootMotionEventArgs)e;
            playerMovement.Move(new Vector3(eventArgs.Position.x,eventArgs.Position.y,eventArgs.Position.z));
        }
        
        private void AirMoveOnUpdate(FsmBase<PlayerStateMachine> fsm)
        {
            //玩家在跳跃动画中，可以进行的操作
            Vector2 input = InputManager.Instance.GetMoveDirection();
            PlayerController playerController = fsm.Owner.PlayerController;
            if (input.sqrMagnitude != 0)
            {
                //旋转模型，朝向摄像机视角方向
                float y = Camera.main.transform.eulerAngles.y;
                Vector3 targetDir = Quaternion.Euler(new Vector3(0, y, 0)) * new Vector3(input.x, 0, input.y);
                playerController.Rotate(targetDir);
                    
                //空中移动方向
                targetDir = Camera.main.transform.rotation * (new Vector3(input.x, 0, input.y) *
                                                              Time.deltaTime * playerController.airMoveSpeed);
                playerController.Move(targetDir);
            }
        }
    }
}
