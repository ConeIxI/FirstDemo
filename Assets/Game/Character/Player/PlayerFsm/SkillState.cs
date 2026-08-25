using Game.Battle.Combat.Config;
using Game.Battle.Skill.Common;
using GameMain2.Framework.Core.FSM;
using GameMain2.Game.EventArgs;
using GameMain2.Scripts.Character;
using UnityEngine;

namespace Game.Character.Player.PlayerFsm
{
    public class SkillState : PlayerCombatActionState
    {
        /// <summary>进入武器技能状态，技能启动成功后关闭 CharacterController 碰撞检测。</summary>
        public override void Enter(FsmBase<PlayerStateMachine> fsm)
        {
            base.Enter(fsm);
            if (fsm.CurState == this)
            {
                fsm.Owner.PlayerController.SetControllerCollisionEnabled(false);
            }
        }

        /// <summary>退出武器技能状态时恢复 CharacterController 碰撞检测。</summary>
        public override void Exit(FsmBase<PlayerStateMachine> fsm)
        {
            fsm.Owner.PlayerController.SetControllerCollisionEnabled(true);
            base.Exit(fsm);
        }

        /// <summary>
        /// 武器技能状态从 FSM 数据中读取当前技能槽配置。
        /// </summary>
        protected override SkillConfig ResolveSkillConfig(FsmBase<PlayerStateMachine> fsm)
        {
            return ResolvePlayerSkillConfig(fsm);
        }

        /// <summary>
        /// 武器技能状态只允许处理 WeaponSkill，避免技能误走普通攻击状态。
        /// </summary>
        protected override bool IsExpectedSkillType(SkillType skillType)
        {
            return skillType == SkillType.WeaponSkill;
        }

        /// <summary>
        /// 武器技能不响应普通攻击连段输入，动画播放到结束窗口后回到待机。
        /// </summary>
        protected override void UpdateCombatAction(FsmBase<PlayerStateMachine> fsm, float animProgress)
        {
            if (animProgress >= 1f)
            {
                fsm.ChangeState<LocomotionState>();
            }
        }

        /// <summary>
        /// 武器技能完整使用动画 root motion 的位移和旋转，保留技能自身的突进、纵向和转向表现。
        /// </summary>
        protected override void ApplyRootMotion(PlayerStateMachine stateMachine, PlayerRootMotionEventArgs eventArgs)
        {
            if (eventArgs.Quaternion != Quaternion.identity && stateMachine.PlayerController.Model != null)
            {
                stateMachine.PlayerController.RotateInstantly(stateMachine.PlayerController.Model.rotation * eventArgs.Quaternion);
            }

            stateMachine.PlayerController.Move(eventArgs.Position);
        }
    }
}
