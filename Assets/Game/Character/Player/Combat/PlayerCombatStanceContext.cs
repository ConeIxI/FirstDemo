using System.Collections.Generic;

namespace Game.Character.Player.Combat
{
    public enum PlayerCombatTransitionPhase
    {
        None,
        EnteringCombat,
        ExitingCombat,
        SwitchingWeaponExit,
        SwitchingWeaponEnter
    }

    public enum PlayerCombatAnimationCompletion
    {
        None,
        CombatEntered,
        CombatExited,
        SwitchWeaponAndEnter,
        WeaponSwitchCompleted
    }

    public readonly struct PlayerCombatTransitionOutcome
    {
        public bool IsCombat { get; }
        public bool ShouldSwitchWeapon { get; }
        public int TargetWeaponIndex { get; }

        /// <summary>创建一次动画中断后的最终结算结果。</summary>
        public PlayerCombatTransitionOutcome(bool isCombat, bool shouldSwitchWeapon, int targetWeaponIndex)
        {
            IsCombat = isCombat;
            ShouldSwitchWeapon = shouldSwitchWeapon;
            TargetWeaponIndex = targetWeaponIndex;
        }
    }

    public sealed class PlayerCombatStanceContext
    {
        public const float AutoSheathDelay = 3f;

        private readonly HashSet<int> m_targetingEnemyIds = new HashSet<int>();
        private float m_idleElapsed;

        public bool IsCombat { get; private set; }
        public bool HasTargetingEnemy => m_targetingEnemyIds.Count > 0;
        public PlayerCombatTransitionPhase Phase { get; private set; }
        public int SourceWeaponIndex { get; private set; } = -1;
        public int TargetWeaponIndex { get; private set; } = -1;

        /// <summary>写入或移除一个敌人的锁定事实，并在新增锁定时刷新战斗活动。</summary>
        public void SetEnemyTargeting(int enemyId, bool isTargeting)
        {
            if (isTargeting)
            {
                if (m_targetingEnemyIds.Add(enemyId))
                {
                    RefreshCombatActivity();
                }

                return;
            }

            m_targetingEnemyIds.Remove(enemyId);
        }

        /// <summary>请求播放普通拔刀动画；已有战斗姿态或过渡时拒绝重复请求。</summary>
        public bool RequestEnterCombatAnimation()
        {
            if (IsCombat || Phase != PlayerCombatTransitionPhase.None)
            {
                return false;
            }

            Phase = PlayerCombatTransitionPhase.EnteringCombat;
            return true;
        }

        /// <summary>直接进入战斗并清理普通进入或退出过渡。</summary>
        public void EnterCombatImmediately()
        {
            IsCombat = true;
            Phase = PlayerCombatTransitionPhase.None;
            SourceWeaponIndex = -1;
            TargetWeaponIndex = -1;
            RefreshCombatActivity();
        }

        /// <summary>没有可播放武器时直接退出战斗并清理过渡。</summary>
        public void ExitCombatImmediately()
        {
            IsCombat = false;
            Phase = PlayerCombatTransitionPhase.None;
            SourceWeaponIndex = -1;
            TargetWeaponIndex = -1;
            RefreshCombatActivity();
        }

        /// <summary>请求播放普通收刀动画。</summary>
        public bool RequestExitCombatAnimation()
        {
            if (!IsCombat || Phase != PlayerCombatTransitionPhase.None)
            {
                return false;
            }

            Phase = PlayerCombatTransitionPhase.ExitingCombat;
            return true;
        }

        /// <summary>请求战斗中的两段式换武器。</summary>
        public bool RequestWeaponSwitch(int sourceWeaponIndex, int targetWeaponIndex)
        {
            if (!IsCombat || Phase != PlayerCombatTransitionPhase.None
                || sourceWeaponIndex < 0 || targetWeaponIndex < 0
                || sourceWeaponIndex == targetWeaponIndex)
            {
                return false;
            }

            SourceWeaponIndex = sourceWeaponIndex;
            TargetWeaponIndex = targetWeaponIndex;
            Phase = PlayerCombatTransitionPhase.SwitchingWeaponExit;
            RefreshCombatActivity();
            return true;
        }

        /// <summary>在满足条件时推进自动收刀计时，并在达到三秒时返回 true。</summary>
        public bool TickAutoSheath(float deltaTime, bool isLocomotion)
        {
            if (!IsCombat || !isLocomotion || HasTargetingEnemy
                || Phase != PlayerCombatTransitionPhase.None)
            {
                return false;
            }

            m_idleElapsed += deltaTime;
            return m_idleElapsed >= AutoSheathDelay;
        }

        /// <summary>刷新战斗活动并将自动收刀计时归零。</summary>
        public void RefreshCombatActivity()
        {
            m_idleElapsed = 0f;
        }

        /// <summary>完成当前动画阶段，并返回状态机需要执行的后续动作。</summary>
        public PlayerCombatAnimationCompletion CompleteAnimationPhase()
        {
            switch (Phase)
            {
                case PlayerCombatTransitionPhase.EnteringCombat:
                    IsCombat = true;
                    ClearTransition();
                    return PlayerCombatAnimationCompletion.CombatEntered;
                case PlayerCombatTransitionPhase.ExitingCombat:
                    IsCombat = false;
                    ClearTransition();
                    return PlayerCombatAnimationCompletion.CombatExited;
                case PlayerCombatTransitionPhase.SwitchingWeaponExit:
                    Phase = PlayerCombatTransitionPhase.SwitchingWeaponEnter;
                    return PlayerCombatAnimationCompletion.SwitchWeaponAndEnter;
                case PlayerCombatTransitionPhase.SwitchingWeaponEnter:
                    ClearTransition();
                    return PlayerCombatAnimationCompletion.WeaponSwitchCompleted;
                default:
                    return PlayerCombatAnimationCompletion.None;
            }
        }

        /// <summary>将任意未完成过渡结算到确定终点，并返回是否需要切换目标武器。</summary>
        public PlayerCombatTransitionOutcome SettleInterruptedTransition()
        {
            bool shouldSwitchWeapon = Phase == PlayerCombatTransitionPhase.SwitchingWeaponExit
                || Phase == PlayerCombatTransitionPhase.SwitchingWeaponEnter;
            int targetWeaponIndex = shouldSwitchWeapon ? TargetWeaponIndex : -1;

            if (Phase == PlayerCombatTransitionPhase.EnteringCombat || shouldSwitchWeapon)
            {
                IsCombat = true;
            }
            else if (Phase == PlayerCombatTransitionPhase.ExitingCombat)
            {
                IsCombat = false;
            }

            ClearTransition();
            return new PlayerCombatTransitionOutcome(IsCombat, shouldSwitchWeapon, targetWeaponIndex);
        }

        /// <summary>清理过渡槽位和阶段，不改变稳定战斗姿态。</summary>
        private void ClearTransition()
        {
            Phase = PlayerCombatTransitionPhase.None;
            SourceWeaponIndex = -1;
            TargetWeaponIndex = -1;
        }
    }
}
