using Game.Battle.Ability;
using GameMain2.Framework.Core;
using GameMain2.Framework.Core.FSM;
using GameMain2.Framework.Manager;
using GameMain2.Game.EventArgs;
using GameMain2.Scripts.Character;
using Game.Character.Player.Combat;
using UnityEngine;

namespace Game.Character.Player.PlayerFsm
{
    public class LocomotionState : PlayerStateBase
    {
        private const string LocomotionAnimation = "Locomotion";
        private const string ItemDrinkAnimation = "ItemDrink";
        private const string IsLockParameter = "IsLock";
        private const string SpeedParameter = "ForwardSpeed";
        private const float NormalMoveState = 0f;
        private const float LockMoveState = 1f;
        private const float GroundStickDistance = 1f;
        private const float SlowRunAnimatorSpeed = 2f;
        private const float FastRunAnimatorSpeed = 4f;

        private const float ParryWindowDelayTime = 0.05f;
        private const float ParryWindowTime = 0.25f;

        private bool m_hasMoveInput;
        private bool m_isDefending;
        private bool m_isParryWindowPending;
        private bool m_isItemDrinkActive;
        private bool m_hasStartedItemDrinkAnimation;
        private float m_defenceElapsedTime;
        private PlayerCombatTransitionPhase m_startedPhase;

        /// <summary>进入统一移动状态，播放 Locomotion 并开始接收移动 Root Motion。</summary>
        public override void Enter(FsmBase<PlayerStateMachine> fsm)
        {
            fsm.Owner.CurState = PlayerState.Locomotion;
            fsm.Owner.CrossFadeInFixedTime(LocomotionAnimation);
            ResetLocomotionParameters(fsm);
            EventCenter.Instance.Subscribe(PlayerRootMotionEventArgs.EventId, OnAnimatorMove);
            m_hasMoveInput = false;
            m_isDefending = false;
            m_isParryWindowPending = false;
            m_isItemDrinkActive = false;
            m_hasStartedItemDrinkAnimation = false;
            m_defenceElapsedTime = 0f;
            fsm.Owner.ApplyDefenceAnimatorState(false);
        }

        /// <summary>统一处理待机、普通移动和锁定移动输入，并驱动 Locomotion BlendTree 参数。</summary>
        public override void Update(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
            bool isItemDrinkActive = UpdateItemDrink(fsm);
            bool isSwitchingWeapon = IsWeaponSwitchTransition(fsm.Owner.CombatTransitionPhase);
            if (!isItemDrinkActive && !isSwitchingWeapon && TryStartConsumableUse(fsm))
            {
                isItemDrinkActive = true;
            }

            if (!isItemDrinkActive && !isSwitchingWeapon && TryHandleGroundedActionInput(fsm))
            {
                return;
            }

            if (!isItemDrinkActive)
            {
                UpdateDefenceInput(fsm, deltaTime);
            }

            if (!isItemDrinkActive && fsm.Owner.HasTargetingEnemy)
            {
                fsm.Owner.RequestEnterCombatAnimation();
            }

            if (!isItemDrinkActive)
            {
                fsm.Owner.TickAutoSheath(deltaTime);
                UpdateCombatTransition(fsm);
            }

            Vector2 move = InputManager.Instance.GetMoveDirection();
            Vector2 moveRaw = InputManager.Instance.GetMoveDirectionRaw();
            m_hasMoveInput = move.sqrMagnitude > 0f || moveRaw.sqrMagnitude > 0f;
            UpdateLocomotionParameters(fsm, move, moveRaw);
            UpdateFacing(fsm, move, moveRaw);

            if (!isItemDrinkActive && !isSwitchingWeapon && TryStartRoll(fsm))
            {
                return;
            }
        }

        /// <summary>退出统一移动状态时释放 Root Motion 订阅并清除移动输入缓存。</summary>
        public override void Exit(FsmBase<PlayerStateMachine> fsm)
        {
            fsm.Owner.SettleCombatTransitionOnLocomotionExit();
            SetDefenceActive(fsm, false);
            fsm.Owner.ApplyDefenceAnimatorState(false);
            EventCenter.TryUnSubscribe(PlayerRootMotionEventArgs.EventId, OnAnimatorMove);
            m_hasMoveInput = false;
            m_isDefending = false;
            m_isParryWindowPending = false;
            ClearItemDrinkRuntime(fsm);
            m_defenceElapsedTime = 0f;
            m_startedPhase = PlayerCombatTransitionPhase.None;
        }

        /// <summary>检测消耗品快捷键，槽位有效时在 Locomotion 内播放饮用动画。</summary>
        private bool TryStartConsumableUse(FsmBase<PlayerStateMachine> fsm)
        {
            int slotIndex = InputManager.Instance.GetPressedConsumableSlot();
            if (!fsm.Owner.TryPrepareConsumableUse(slotIndex))
            {
                return false;
            }

            StartItemDrink(fsm);
            return true;
        }

        /// <summary>启动饮用子流程，复用 ArmsLayer 播放 ItemDrink，实际消耗品结算等待动画事件触发。</summary>
        private void StartItemDrink(FsmBase<PlayerStateMachine> fsm)
        {
            SetDefenceActive(fsm, false);
            fsm.Owner.SettleCombatTransitionOnLocomotionExit();
            m_startedPhase = PlayerCombatTransitionPhase.None;
            m_hasMoveInput = false;
            m_isItemDrinkActive = false;
            m_hasStartedItemDrinkAnimation = false;
            fsm.Owner.CurState = PlayerState.ItemDrink;

            if (!fsm.Owner.TryCrossFadeInFixedTime(ItemDrinkAnimation, layer: fsm.Owner.ArmsLayerIndex))
            {
                FinishItemDrink(fsm);
                return;
            }

            m_isItemDrinkActive = true;
        }

        /// <summary>推进饮用动画完成检测，调用方据此阻断动作输入但继续刷新移动。</summary>
        private bool UpdateItemDrink(FsmBase<PlayerStateMachine> fsm)
        {
            if (!m_isItemDrinkActive)
            {
                return false;
            }

            if (fsm.Owner.IsPlayingAnimation(ItemDrinkAnimation, out float animProgress, fsm.Owner.ArmsLayerIndex))
            {
                m_hasStartedItemDrinkAnimation = true;
                if (animProgress >= 1f)
                {
                    FinishItemDrink(fsm);
                }

                return true;
            }

            if (m_hasStartedItemDrinkAnimation)
            {
                FinishItemDrink(fsm);
            }

            return true;
        }

        /// <summary>结束饮用子流程并把对外玩家状态恢复为 Locomotion。</summary>
        private void FinishItemDrink(FsmBase<PlayerStateMachine> fsm)
        {
            ClearItemDrinkRuntime(fsm);
            if (fsm.Owner.CurState == PlayerState.ItemDrink)
            {
                fsm.Owner.CurState = PlayerState.Locomotion;
            }
        }

        /// <summary>清理饮用缓存，避免切出 Locomotion 后留下待使用消耗品请求。</summary>
        private void ClearItemDrinkRuntime(FsmBase<PlayerStateMachine> fsm)
        {
            fsm.Owner.ClearPreparedConsumableUse();
            m_isItemDrinkActive = false;
            m_hasStartedItemDrinkAnimation = false;
        }

        /// <summary>推进 ArmsLayer 上的拔刀、收刀和战斗换武器动画。</summary>
        private void UpdateCombatTransition(FsmBase<PlayerStateMachine> fsm)
        {
            PlayerCombatTransitionPhase phase = fsm.Owner.CombatTransitionPhase;
            if (phase == PlayerCombatTransitionPhase.None)
            {
                m_startedPhase = PlayerCombatTransitionPhase.None;
                return;
            }

            string animationName = phase == PlayerCombatTransitionPhase.EnteringCombat
                || phase == PlayerCombatTransitionPhase.SwitchingWeaponEnter
                ? PlayerStateMachine.EnterCombatAnimationName
                : PlayerStateMachine.ExitCombatAnimationName;

            if (m_startedPhase != phase)
            {
                if (!fsm.Owner.TryCrossFadeInFixedTime(animationName, 0.1f, 0f, fsm.Owner.ArmsLayerIndex))
                {
                    fsm.Owner.CompleteCombatAnimationPhase();
                }
                else
                {
                    m_startedPhase = phase;
                    fsm.Owner.MarkCombatTransitionAnimationStarted(phase);
                }

                return;
            }

            if (!fsm.Owner.IsPlayingAnimation(animationName, out float progress, fsm.Owner.ArmsLayerIndex)
                || progress >= 1f)
            {
                fsm.Owner.CompleteCombatAnimationPhase();
                m_startedPhase = PlayerCombatTransitionPhase.None;
            }
        }

        /// <summary>判断当前阶段是否为战斗换武器的输入锁定阶段。</summary>
        private static bool IsWeaponSwitchTransition(PlayerCombatTransitionPhase phase)
        {
            return phase == PlayerCombatTransitionPhase.SwitchingWeaponExit
                || phase == PlayerCombatTransitionPhase.SwitchingWeaponEnter;
        }

        /// <summary>根据当前防御键状态开启或关闭 Locomotion 内的防御姿态，并推进弹反窗口延迟。</summary>
        private void UpdateDefenceInput(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
            bool shouldDefend = fsm.Owner.CanDefend && InputManager.Instance.IsDefenseKeyPressed();
            SetDefenceActive(fsm, shouldDefend);
            UpdateDelayedParryWindow(fsm, deltaTime);
        }

        /// <summary>切换防御姿态、IsDefence 参数目标值和战斗标签。</summary>
        private void SetDefenceActive(FsmBase<PlayerStateMachine> fsm, bool isDefending)
        {
            if (m_isDefending == isDefending)
            {
                return;
            }

            if (isDefending)
            {
                EnterDefence(fsm);
                return;
            }

            ExitDefence(fsm);
        }

        /// <summary>进入防御姿态，直接拔出武器并开启 IsDefence 参数和防御标签，弹反窗口延迟开启。</summary>
        private void EnterDefence(FsmBase<PlayerStateMachine> fsm)
        {
            m_isDefending = true;
            m_defenceElapsedTime = 0f;
            fsm.Owner.EnterCombatImmediately();
            fsm.Owner.RefreshCombatActivity();
            fsm.Owner.CurState = PlayerState.Defence;
            fsm.Owner.ApplyDefenceAnimatorState(true);

            CombatAbilitySystem abilitySystem = GetAbilitySystem(fsm);
            abilitySystem.AddTag(CombatTag.Defending);
            m_isParryWindowPending = !fsm.Owner.ConsumeSuppressNextDefenceParryWindow();
        }

        /// <summary>退出防御姿态，关闭 IsDefence 参数、防御标签和弹反窗口。</summary>
        private void ExitDefence(FsmBase<PlayerStateMachine> fsm)
        {
            m_isDefending = false;
            m_isParryWindowPending = false;
            m_defenceElapsedTime = 0f;
            if (fsm.Owner.CurState == PlayerState.Defence)
            {
                fsm.Owner.CurState = PlayerState.Locomotion;
            }

            fsm.Owner.ApplyDefenceAnimatorState(false);
            CombatAbilitySystem abilitySystem = GetAbilitySystem(fsm);
            abilitySystem.RemoveTag(CombatTag.Defending);
            abilitySystem.RemoveTimedTag(CombatTag.ParryWindow);
        }

        /// <summary>防御保持到达延迟时间后，再开启一次弹反窗口。</summary>
        private void UpdateDelayedParryWindow(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
            if (!m_isDefending || !m_isParryWindowPending)
            {
                return;
            }

            m_defenceElapsedTime += deltaTime;
            if (m_defenceElapsedTime < ParryWindowDelayTime)
            {
                return;
            }

            m_isParryWindowPending = false;
            GetAbilitySystem(fsm).AddTimedTag(CombatTag.ParryWindow, ParryWindowTime);
        }

        /// <summary>读取玩家能力系统，供防御姿态写入格挡和弹反标签。</summary>
        private static CombatAbilitySystem GetAbilitySystem(FsmBase<PlayerStateMachine> fsm)
        {
            return fsm.Owner.PlayerController.AbilitySystem;
        }

        /// <summary>接收 Locomotion 动画根运动，保持动画原始位移不受移动速度参数影响。</summary>
        private void OnAnimatorMove(object sender, EventArgsBase e)
        {
            if (!m_hasMoveInput)
            {
                return;
            }

            PlayerStateMachine stateMachine = sender as PlayerStateMachine;
            if (stateMachine == null || stateMachine.PlayerController == null)
            {
                return;
            }

            PlayerRootMotionEventArgs eventArgs = (PlayerRootMotionEventArgs)e;
            Vector3 motion = new Vector3(eventArgs.Position.x, -GroundStickDistance, eventArgs.Position.z);
            stateMachine.PlayerController.Move(motion);
        }

        /// <summary>根据锁定状态写入 Locomotion 移动参数；锁定移动时四个方向参数同步变化。</summary>
        private static void UpdateLocomotionParameters(FsmBase<PlayerStateMachine> fsm, Vector2 move, Vector2 moveRaw)
        {
            LockOnManager lockOnManager = fsm.Owner.LockOnManager;
            bool isLockedOn = lockOnManager != null && lockOnManager.IsLockedOn;
            if (!isLockedOn)
            {
                float animatorSpeed = ResolveAnimatorSpeed();
                fsm.Owner.SetFloat(IsLockParameter, NormalMoveState);
                fsm.Owner.SetFloat(SpeedParameter, Mathf.Clamp01(move.magnitude) * animatorSpeed,0.1f,Time.deltaTime);
                return;
            }

            fsm.Owner.SetFloat(IsLockParameter, LockMoveState);
            fsm.Owner.SetFloat(SpeedParameter, 0f);
        }

        /// <summary>根据 Shift 奔跑输入选择 Locomotion BlendTree 使用的动画速度参数。</summary>
        private static float ResolveAnimatorSpeed()
        {
            return InputManager.Instance.IsRunKeyPressed() ? FastRunAnimatorSpeed : SlowRunAnimatorSpeed;
        }

        /// <summary>普通移动按相机方向转身，锁定移动始终面向当前锁定目标。</summary>
        private static void UpdateFacing(FsmBase<PlayerStateMachine> fsm, Vector2 move, Vector2 moveRaw)
        {
            if (move.sqrMagnitude <= 0f && moveRaw.sqrMagnitude <= 0f)
            {
                return;
            }

            LockOnManager lockOnManager = fsm.Owner.LockOnManager;
            if (lockOnManager != null && lockOnManager.IsLockedOn)
            {
                lockOnManager.TurnToCurrentTarget();
                return;
            }

            RotateByCameraDirection(fsm, moveRaw.sqrMagnitude > 0f ? moveRaw : move);
        }

        /// <summary>按相机水平朝向修正移动输入，并让玩家模型朝向移动方向。</summary>
        private static void RotateByCameraDirection(FsmBase<PlayerStateMachine> fsm, Vector2 moveInput)
        {
            PlayerController playerController = fsm.Owner.PlayerController;
            Camera mainCamera = Camera.main;
            if (playerController == null || mainCamera == null)
            {
                return;
            }

            float y = mainCamera.transform.eulerAngles.y;
            Vector3 targetDir = Quaternion.Euler(new Vector3(0f, y, 0f)) * new Vector3(moveInput.x, 0f, moveInput.y).normalized;
            playerController.Rotate(targetDir.normalized);
        }

        /// <summary>重置 Locomotion BlendTree 参数，避免从其他状态返回时沿用旧移动方向。</summary>
        private static void ResetLocomotionParameters(FsmBase<PlayerStateMachine> fsm)
        {
            fsm.Owner.SetFloat(IsLockParameter, NormalMoveState);
            fsm.Owner.SetFloat(SpeedParameter, 0f);
        }
    }
}
