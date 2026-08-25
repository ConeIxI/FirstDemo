using Game.Battle.Ability;
using Game.Battle.Skill.Common;
using UnityEngine;

namespace Game.Battle.Skill.Effects
{
    [RequireComponent(typeof(CombatAbilitySystem))]
    public sealed class CharacterEffectController : MonoBehaviour
    {
        private CombatAbilitySystem m_abilitySystem;

        /// <summary>缓存角色能力系统依赖。</summary>
        private void Awake()
        {
            m_abilitySystem = GetComponent<CombatAbilitySystem>();
        }

        /// <summary>动画事件入口：按触发标识播放当前技能的攻击动作特效。</summary>
        public void PlayAttackEffect(string triggerKey)
        {
            SkillConfig skill = m_abilitySystem.CurrentSkill;
            if (skill == null)
            {
                return;
            }

            for (int i = 0; i < skill.attackEffects.Length; i++)
            {
                SkillEffectBinding binding = skill.attackEffects[i];
                if (binding.triggerKey != triggerKey)
                {
                    continue;
                }

                CombatEffectPlayContext context = CombatEffectPlayContext.ForAttack(skill, binding, m_abilitySystem, this);
                CombatEffectService.Instance.Play(context);
            }
        }

        /// <summary>动画事件入口：停止当前角色指定通道的持续攻击特效。</summary>
        public void StopAttackEffect(string channel)
        {
            CombatEffectService.Instance.StopOwnerChannel(this, channel);
        }

        /// <summary>角色禁用时清理当前角色名下的活动特效。</summary>
        private void OnDisable()
        {
            if (CombatEffectService.Instance != null)
            {
                CombatEffectService.Instance.StopOwner(this);
            }
        }
    }
}
