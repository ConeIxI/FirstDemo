
using Game.Battle.Ability;
using GameMain2.Framework.Core;
using GameMain2.Framework.Core.FSM;
using GameMain2.Framework.Manager;
using GameMain2.Game.EventArgs;
using GameMain2.Scripts.Character;
using UnityEngine;

namespace Game.Character.Player.PlayerFsm
{
    public class DodgeState : PlayerStateBase
    {
        private const string DodgeAnimation = "Dodge";
        private const string HorizontalBlendParameter = "DodgeHorizontalSpeed";
        private const string VerticalBlendParameter = "DodgeVerticalSpeed";
        private const float DodgeInvincibleTime = 0.45f;
        private const float GroundStickDistance = 1f;

        private Camera m_mainCamera;
        private Vector3 m_targetDirection;

        /// <summary>进入闪避状态时刷新无敌时间，记录转向或 Dodge BlendTree 方向并播放闪避动画。</summary>
        public override void Enter(FsmBase<PlayerStateMachine> fsm)
        {
            fsm.Owner.CurState = PlayerState.Dodge;
            fsm.Owner.MarkDodgeStarted();
            RefreshDodgeInvincible(fsm.Owner);
            UpdateDodgeDirection(fsm, InputManager.Instance.GetMoveDirectionRaw());
            fsm.Owner.CrossFadeInFixedTime(DodgeAnimation);
            EventCenter.Instance.Subscribe(PlayerRootMotionEventArgs.EventId, OnAnimatorMove);
        }

        /// <summary>闪避状态下等待当前闪避结束，取消连续闪避并回到 Locomotion。</summary>
        public override void Update(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
            if (fsm.Owner.IsPlayingAnimation(DodgeAnimation, out float animProgress))
            {
                if (animProgress >= 0.85f)
                {
                    fsm.ChangeState<LocomotionState>();
                }
            }
        }

        /// <summary>退出闪避状态时释放 Root Motion 订阅，限时无敌由能力系统自行到期。</summary>
        public override void Exit(FsmBase<PlayerStateMachine> fsm)
        {
            EventCenter.TryUnSubscribe(PlayerRootMotionEventArgs.EventId, OnAnimatorMove);
            m_targetDirection = Vector3.zero;
        }

        /// <summary>接收 Dodge 动画根运动，必要时先把角色瞬时转向本次闪避方向。</summary>
        private void OnAnimatorMove(object sender, EventArgsBase e)
        {
            PlayerStateMachine stateMachine = sender as PlayerStateMachine;
            if (stateMachine == null || stateMachine.PlayerController == null)
            {
                return;
            }

            PlayerRootMotionEventArgs eventArgs = (PlayerRootMotionEventArgs)e;
            if (m_targetDirection != Vector3.zero)
            {
                stateMachine.PlayerController.RotateInstantly(Quaternion.LookRotation(m_targetDirection));
                m_targetDirection = Vector3.zero;
            }

            Vector3 motion = new Vector3(eventArgs.Position.x, -GroundStickDistance, eventArgs.Position.z);
            stateMachine.PlayerController.Move(motion);
        }

        /// <summary>刷新本次闪避无敌时间，提供短暂无敌窗口。</summary>
        private void RefreshDodgeInvincible(PlayerStateMachine owner)
        {
            if (owner == null || owner.PlayerController == null)
            {
                return;
            }

            CombatAbilitySystem abilitySystem = owner.PlayerController.AbilitySystem;
            if (abilitySystem == null)
            {
                return;
            }

            abilitySystem.AddTimedTag(CombatTag.Invincible, DodgeInvincibleTime);
        }

        /// <summary>更新本次闪避方向；非锁定只转向，锁定时一次性写入 Dodge BlendTree 参数。</summary>
        private void UpdateDodgeDirection(FsmBase<PlayerStateMachine> fsm, Vector2 moveRaw)
        {
            LockOnManager lockOnManager = fsm.Owner.LockOnManager;
            bool isLockedOn = lockOnManager != null && lockOnManager.IsLockedOn;
            if (!isLockedOn)
            {
                m_targetDirection = ResolveDodgeFacingDirection(moveRaw);
                return;
            }

            SetDodgeBlend(fsm, moveRaw);
            m_targetDirection = Vector3.zero;
        }

        /// <summary>写入本次锁定闪避使用的 Dodge BlendTree x/y 参数。</summary>
        private static void SetDodgeBlend(FsmBase<PlayerStateMachine> fsm, Vector2 moveRaw)
        {
            fsm.Owner.SetFloat(HorizontalBlendParameter, moveRaw.x);
            fsm.Owner.SetFloat(VerticalBlendParameter, moveRaw.y);
        }

        /// <summary>非锁定闪避沿相机修正后的移动方向转身，无输入时保留当前朝向。</summary>
        private Vector3 ResolveDodgeFacingDirection(Vector2 moveInput)
        {
            if (moveInput.sqrMagnitude == 0f)
            {
                return Vector3.zero;
            }

            Camera mainCamera = GetMainCamera();
            if (mainCamera == null)
            {
                return Vector3.zero;
            }

            float y = mainCamera.transform.eulerAngles.y;
            return Quaternion.Euler(new Vector3(0f, y, 0f)) * new Vector3(moveInput.x, 0f, moveInput.y).normalized;
        }

        /// <summary>缓存主相机引用，避免每次解析闪避方向时重复触发 Camera.main 查找。</summary>
        private Camera GetMainCamera()
        {
            if (m_mainCamera == null)
            {
                m_mainCamera = Camera.main;
            }

            return m_mainCamera;
        }
    }
}
