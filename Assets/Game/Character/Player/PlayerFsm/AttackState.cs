using Game.Battle.Combat.Config;
using Game.Battle.Skill.Common;
using Game.Character.Player.Execution;
using GameMain2.Framework.Core.FSM;
using GameMain2.Game.EventArgs;
using GameMain2.Scripts.Character;
using UnityEngine;

namespace Game.Character.Player.PlayerFsm
{
    public class AttackState : PlayerCombatActionState
    {
        /// <summary>
        /// 普通攻击状态从 FSM 数据中读取当前攻击段配置。
        /// </summary>
        protected override SkillConfig ResolveSkillConfig(FsmBase<PlayerStateMachine> fsm)
        {
            return ResolvePlayerSkillConfig(fsm);
        }

        /// <summary>
        /// 普通攻击状态只允许处理 NormalAttack，武器技能必须进入 SkillState。
        /// </summary>
        protected override bool IsExpectedSkillType(SkillType skillType)
        {
            return skillType == SkillType.NormalAttack;
        }

        /// <summary>
        /// 普通攻击动画期间允许在可切换窗口进入下一段连段，最后一段再次按攻击则回到首段。
        /// </summary>
        protected override void UpdateCombatAction(FsmBase<PlayerStateMachine> fsm, float animProgress)
        {
            if (IsAttackDecisionWindowOpen(fsm) && TryConsumeBufferedNormalAttack())
            {
                // 连段续段同样要保持处决优先级，避免失衡敌人在连段期间被普通攻击续段吞掉输入。
                ExecutionStartResult executionResult = TryStartExecution(fsm);
                if (executionResult == ExecutionStartResult.Started)
                {
                    fsm.ChangeState<ExecutionState>();
                    return;
                }

                if (executionResult == ExecutionStartResult.Failed)
                {
                    return;
                }

                int nextSkillId = SkillConfig.comboNextSkillId != 0
                    ? SkillConfig.comboNextSkillId
                    : GetFirstNormalAttackSkillId(fsm);
                if (nextSkillId > 0)
                {
                    fsm.Owner.UpdateCombatActionSkillId(nextSkillId);
                    fsm.ChangeState<AttackState>();
                    return;
                }
            }

            if (animProgress >= 1f)
            {
                fsm.ChangeState<LocomotionState>();
            }
        }

        /// <summary>
        /// 普通攻击只使用动画水平位移，并保持原有固定下压，避免普通攻击动画抬升影响贴地手感。
        /// </summary>
        protected override void ApplyRootMotion(PlayerStateMachine stateMachine, PlayerRootMotionEventArgs eventArgs)
        {
            stateMachine.PlayerController.Move(new Vector3(eventArgs.Position.x, -1f, eventArgs.Position.z));
        }
    }
}
