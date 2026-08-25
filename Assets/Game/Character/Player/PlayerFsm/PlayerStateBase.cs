using Game.Battle.Ability;
using Game.Character.Equipment;
using Game.Character.Player.Execution;
using Game.Character.Player.PlayerFsm;
using GameMain2.Framework.Core.FSM;
using GameMain2.Framework.Manager;
using GameMain2.Scripts.Character;

namespace Game.Character
{
    public abstract class PlayerStateBase : FsmStateBase<PlayerStateMachine>
    {
        private const float RunningAttackSpeedThreshold = 2f;

        /// <summary>尝试根据 1/2/3 键进入武器技能。</summary>
        protected bool TryStartWeaponSkill(FsmBase<PlayerStateMachine> fsm)
        {
            int slotIndex = InputManager.Instance.GetPressedWeaponSkillSlot();
            return TryStartWeaponSkill(fsm, slotIndex);
        }

        /// <summary>尝试按指定技能槽位进入武器技能状态。</summary>
        protected bool TryStartWeaponSkill(FsmBase<PlayerStateMachine> fsm, int slotIndex)
        {
            if (slotIndex < 0)
            {
                return false;
            }

            WeaponData activeWeapon = GetActiveWeapon(fsm);
            int[] weaponSkillIds = activeWeapon == null ? null : activeWeapon.GetWeaponSkillIds();
            if (weaponSkillIds == null || slotIndex >= weaponSkillIds.Length || weaponSkillIds[slotIndex] <= 0)
            {
                return false;
            }

            return TryEnterSkillState(fsm, activeWeapon, weaponSkillIds[slotIndex]);
        }

        /// <summary>尝试进入当前武器的普通攻击首段。</summary>
        protected bool TryStartNormalAttack(FsmBase<PlayerStateMachine> fsm)
        {
            WeaponData activeWeapon = GetActiveWeapon(fsm);
            if (TryStartRunningAttack(fsm, activeWeapon))
            {
                return true;
            }

            int[] normalAttackSkillIds = activeWeapon == null ? null : activeWeapon.GetNormalAttackSkillIds();
            if (normalAttackSkillIds == null || normalAttackSkillIds.Length == 0 || normalAttackSkillIds[0] <= 0)
            {
                return false;
            }

            return TryEnterAttackState(fsm, activeWeapon, normalAttackSkillIds[0]);
        }

        /// <summary>普攻击键优先尝试处决失衡目标；无目标时允许后续普通攻击继续处理。</summary>
        protected ExecutionStartResult TryStartExecution(FsmBase<PlayerStateMachine> fsm)
        {
            if (fsm.Owner.ExecutionController == null || fsm.Owner.LockOnManager == null)
            {
                return ExecutionStartResult.NotFound;
            }

            WeaponData activeWeapon = GetActiveWeapon(fsm);
            float executionRange = fsm.Owner.PlayerController.DefaultAttackRange;
            if (fsm.Owner.LockOnManager.TryGetLockedExecutionTarget(executionRange, out ExecutionTarget lockedTarget))
            {
                return fsm.Owner.ExecutionController.TryStartExecution(lockedTarget, activeWeapon);
            }

            if (fsm.Owner.LockOnManager.TryFindNearestExecutionTarget(executionRange, out ExecutionTarget nearestTarget))
            {
                return fsm.Owner.ExecutionController.TryStartExecution(nearestTarget, activeWeapon);
            }

            return ExecutionStartResult.NotFound;
        }

        /// <summary>获取当前武器普通攻击首段技能 ID，供连段末尾回到首段使用。</summary>
        protected int GetFirstNormalAttackSkillId(FsmBase<PlayerStateMachine> fsm)
        {
            WeaponData activeWeapon = GetActiveWeapon(fsm);
            int[] normalAttackSkillIds = activeWeapon == null ? null : activeWeapon.GetNormalAttackSkillIds();
            if (normalAttackSkillIds == null || normalAttackSkillIds.Length == 0 || normalAttackSkillIds[0] <= 0)
            {
                return 0;
            }

            return normalAttackSkillIds[0];
        }

        /// <summary>尝试释放当前武器配置的防御反击技能，供弹反成功后的 ParryState 使用。</summary>
        protected bool TryStartDefenceCounterAttack(FsmBase<PlayerStateMachine> fsm)
        {
            WeaponData activeWeapon = GetActiveWeapon(fsm);
            if (activeWeapon == null)
            {
                return false;
            }

            int defenceCounterSkillId = activeWeapon.GetDefenceCounterSkillId();
            return TryEnterSkillState(fsm, activeWeapon, defenceCounterSkillId);
        }

        /// <summary>玩家真实水平速度达到阈值时，优先尝试释放当前武器的奔跑攻击。</summary>
        private bool TryStartRunningAttack(FsmBase<PlayerStateMachine> fsm, WeaponData activeWeapon)
        {
            if (!HasRunningAttackSpeed(fsm) || activeWeapon == null)
            {
                return false;
            }

            int runningAttackSkillId = activeWeapon.GetRunningAttackSkillId();
            if (runningAttackSkillId <= 0)
            {
                return false;
            }

            return TryEnterSkillState(fsm, activeWeapon, runningAttackSkillId);
        }

        /// <summary>判断玩家上一帧真实水平速度是否达到奔跑攻击触发速度；锁定状态下禁用奔跑攻击。</summary>
        private bool HasRunningAttackSpeed(FsmBase<PlayerStateMachine> fsm)
        {
            if (fsm.Owner.LockOnManager != null && fsm.Owner.LockOnManager.IsLockedOn)
            {
                return false;
            }

            PlayerController playerController = fsm.Owner.PlayerController;
            return playerController != null
                && playerController.GetCurrentHorizontalSpeed() >= RunningAttackSpeedThreshold;
        }

        /// <summary>按玩家通用优先级处理技能、普攻、换武器和离地检测。</summary>
        protected bool TryHandleGroundedActionInput(FsmBase<PlayerStateMachine> fsm)
        {
            if (TryHandleCombatActionInput(fsm))
            {
                return true;
            }

            if (TrySwitchWeapon(fsm))
            {
                return true;
            }

            return TryEnterAirDownIfUngrounded(fsm);
        }

        /// <summary>处理玩家通用战斗动作输入，武器技能优先于普通攻击。</summary>
        protected bool TryHandleCombatActionInput(FsmBase<PlayerStateMachine> fsm)
        {
            if (TryStartWeaponSkill(fsm))
            {
                return true;
            }

            if (!InputManager.Instance.IsAttackKeyPressed())
            {
                return false;
            }

            ExecutionStartResult executionResult = TryStartExecution(fsm);
            if (executionResult == ExecutionStartResult.Started)
            {
                fsm.ChangeState<ExecutionState>();
                return true;
            }

            if (executionResult == ExecutionStartResult.Failed)
            {
                return true;
            }

            TryStartNormalAttack(fsm);
            return true;
        }

        /// <summary>处理换武器输入并把合法请求交给玩家战斗姿态流程。</summary>
        protected bool TrySwitchWeapon(FsmBase<PlayerStateMachine> fsm)
        {
            if (!InputManager.Instance.IsWeaponSwitchKeyPressed())
            {
                return false;
            }

            fsm.Owner.RequestWeaponSwitch();
            return true;
        }

        /// <summary>处理防御输入并回到 Locomotion，由 Locomotion 统一接管防御姿态。</summary>
        protected bool TryStartDefence(FsmBase<PlayerStateMachine> fsm)
        {
            if (!fsm.Owner.CanDefend || !InputManager.Instance.IsDefenseKeyPressed())
            {
                return false;
            }

            fsm.ChangeState<LocomotionState>();
            return true;
        }

        /// <summary>检测玩家离地并切换到下落状态。</summary>
        protected bool TryEnterAirDownIfUngrounded(FsmBase<PlayerStateMachine> fsm)
        {
            if (fsm.Owner.PlayerController == null || fsm.Owner.PlayerController.IsGrounded())
            {
                return false;
            }

            fsm.ChangeState<AirDownState>();
            return true;
        }

        /// <summary>处理闪避输入并切换到闪避状态；没有移动输入或冷却未结束时忽略本次闪避请求。</summary>
        protected bool TryStartRoll(FsmBase<PlayerStateMachine> fsm)
        {
            if (!InputManager.Instance.IsRollPressed() || !HasDodgeMoveInput() || !fsm.Owner.CanStartDodge())
            {
                return false;
            }

            fsm.ChangeState<DodgeState>();
            return true;
        }

        /// <summary>检测玩家是否正在输入移动方向，供闪避触发和闪避预输入共用。</summary>
        protected bool HasDodgeMoveInput()
        {
            return InputManager.Instance.GetMoveDirectionRaw().sqrMagnitude > 0f;
        }

        /// <summary>获取玩家当前装备的武器。</summary>
        private WeaponData GetActiveWeapon(FsmBase<PlayerStateMachine> fsm)
        {
            EquipmentManager equipmentManager =
                fsm.Owner.PlayerController == null ? null : fsm.Owner.PlayerController.EquipmentManager;
            return equipmentManager == null ? null : equipmentManager.ActiveWeapon;
        }

        /// <summary>写入武器类型和技能 ID，直接进入战斗后切换到普通攻击状态。</summary>
        private bool TryEnterAttackState(FsmBase<PlayerStateMachine> fsm, WeaponData activeWeapon, int skillId)
        {
            if (activeWeapon == null || skillId <= 0)
            {
                return false;
            }

            fsm.Owner.EnterCombatImmediately();
            fsm.Owner.RefreshCombatActivity();
            fsm.Owner.SetCombatActionRequest(activeWeapon.weaponType, skillId);
            fsm.ChangeState<AttackState>();
            return true;
        }

        /// <summary>预检成功后直接进入战斗，写入技能数据并切换到武器技能状态。</summary>
        private bool TryEnterSkillState(FsmBase<PlayerStateMachine> fsm, WeaponData activeWeapon, int skillId)
        {
            if (activeWeapon == null || skillId <= 0)
            {
                return false;
            }

            if (!CanEnterWeaponSkillState(fsm, activeWeapon, skillId))
            {
                return false;
            }

            fsm.Owner.EnterCombatImmediately();
            fsm.Owner.RefreshCombatActivity();
            fsm.Owner.SetCombatActionRequest(activeWeapon.weaponType, skillId);
            fsm.ChangeState<SkillState>();
            return true;
        }

        /// <summary>切换技能状态前用能力系统执行非消耗式释放预检。</summary>
        private bool CanEnterWeaponSkillState(FsmBase<PlayerStateMachine> fsm, WeaponData activeWeapon, int skillId)
        {
            if (fsm == null || fsm.Owner == null || fsm.Owner.PlayerController == null || activeWeapon == null)
            {
                return false;
            }

            PlayerSkillManager skillManager = fsm.Owner.PlayerController.SkillManager;
            CombatAbilitySystem abilitySystem = fsm.Owner.PlayerController.AbilitySystem;
            if (skillManager == null || abilitySystem == null || !skillManager.HasSkill(skillId))
            {
                return false;
            }

            var skillConfig = ConfigManager.Instance.GetPlayerSkillConfig(activeWeapon.weaponType, skillId);
            if (skillConfig == null)
            {
                return false;
            }

            AbilityActivationResult activationResult = abilitySystem.CanActivate(skillConfig);
            if (activationResult == AbilityActivationResult.Success)
            {
                return true;
            }

            // 决策窗口内切换武器技能时，当前攻击 Ability 会在状态切换 Exit 中取消，新技能随后在 Enter 中重新激活。
            return activationResult == AbilityActivationResult.AlreadyActive
                && fsm.Owner.isAttackDecisionWindowOpen;
        }
    }
}
