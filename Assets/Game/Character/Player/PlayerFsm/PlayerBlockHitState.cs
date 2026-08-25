using Game.Battle.Ability;
using GameMain2.Framework.Core.FSM;
using GameMain2.Framework.Manager;
using GameMain2.Scripts.Character;

namespace Game.Character.Player.PlayerFsm
{
    public class PlayerBlockHitState : PlayerStateBase
    {
        private const string AnimationName = "DefenceHit";
        private const float DefaultFallbackExitDuration = 0.35f;

        private float m_elapsedTime;

        /// <summary>进入格挡受击状态，统一播放单一格挡受击动画；后退表现由动画或 Root Motion 承担。</summary>
        public override void Enter(FsmBase<PlayerStateMachine> fsm)
        {
            fsm.Owner.CurState = PlayerState.Defence;
            SetDefendingTag(fsm, true);
            m_elapsedTime = 0f;
            fsm.Owner.TryCrossFadeInFixedTime(AnimationName);
        }

        /// <summary>更新格挡受击退出逻辑，动画结束后回到 Locomotion 继续读取防御键。</summary>
        public override void Update(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
            m_elapsedTime += UnityEngine.Mathf.Max(0f, deltaTime);

            if (fsm.Owner.IsPlayingAnimation(AnimationName, out float time))
            {
                if (time < 1f)
                {
                    return;
                }
            }
            else if (m_elapsedTime < DefaultFallbackExitDuration)
            {
                return;
            }

            if (InputManager.Instance.IsDefenseKeyPressed())
            {
                fsm.Owner.SuppressNextDefenceParryWindow();
                fsm.ChangeState<LocomotionState>();
                return;
            }

            fsm.ChangeState<LocomotionState>();
        }

        /// <summary>退出格挡受击状态时清理一次性动画缓存。</summary>
        public override void Exit(FsmBase<PlayerStateMachine> fsm)
        {
            SetDefendingTag(fsm, false);
            m_elapsedTime = 0f;
        }

        /// <summary>格挡受击期间维持真实防御标签，让连续命中仍按格挡结算。</summary>
        private static void SetDefendingTag(FsmBase<PlayerStateMachine> fsm, bool isDefending)
        {
            CombatAbilitySystem abilitySystem = fsm.Owner.PlayerController.AbilitySystem;
            if (isDefending)
            {
                abilitySystem.AddTag(CombatTag.Defending);
                return;
            }

            abilitySystem.RemoveTag(CombatTag.Defending);
            abilitySystem.RemoveTimedTag(CombatTag.ParryWindow);
        }
    }
}
