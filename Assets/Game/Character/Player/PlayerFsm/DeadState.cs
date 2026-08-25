using Game.Battle.Ability;
using Game.Character;
using GameMain2.Framework.Core.FSM;
using GameMain2.Scripts.Character;

namespace Game.Character.Player.PlayerFsm
{
    public class DeadState : PlayerStateBase
    {
        /// <summary>
        /// 进入死亡状态时播放死亡动画，并清理攻击、格挡和无敌等战斗状态。
        /// </summary>
        public override void Enter(FsmBase<PlayerStateMachine> fsm)
        {
            fsm.Owner.CurState = PlayerState.Dead;
            fsm.Owner.DisableWeaponCollider();
            fsm.Owner.ClearActiveBuffsForDeath();

            CombatAbilitySystem abilitySystem = fsm.Owner.PlayerController.AbilitySystem;
            if (abilitySystem != null)
            {
                abilitySystem.CancelActiveAbility();
                abilitySystem.RemoveTag(CombatTag.Defending);
                abilitySystem.RemoveTimedTag(CombatTag.ParryWindow);
                abilitySystem.RemoveTimedTag(CombatTag.Invincible);
                abilitySystem.RemoveTag(CombatTag.Unbalanced);
                abilitySystem.AddTag(CombatTag.Dead);
            }

            CharacterStateMachine.PendingDeathReaction reaction = fsm.Owner.ConsumePendingDeathReaction();
            fsm.Owner.ApplyDeathBlendTreeParameters(reaction.IsCombat, reaction.HitWeight);
            fsm.Owner.CrossFadeInFixedTime("Dead");
        }

        /// <summary>
        /// 死亡状态不主动切换到其它状态，避免死亡动画被普通状态流程打断。
        /// </summary>
        public override void Update(FsmBase<PlayerStateMachine> fsm, float deltaTime)
        {
        }

        /// <summary>
        /// 退出死亡状态时暂不做额外处理，预留给复活逻辑接入。
        /// </summary>
        public override void Exit(FsmBase<PlayerStateMachine> fsm)
        {
        }
    }
}
