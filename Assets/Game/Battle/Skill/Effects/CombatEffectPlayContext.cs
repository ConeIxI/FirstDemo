using Game.Battle.Ability;
using Game.Battle.Skill.Common;
using UnityEngine;

namespace Game.Battle.Skill.Effects
{
    public sealed class CombatEffectPlayContext
    {
        public SkillConfig Skill { get; private set; }
        public SkillEffectBinding Binding { get; private set; }
        public CombatAbilitySystem Source { get; private set; }
        public CombatAbilitySystem Target { get; private set; }
        public Vector3 HitPoint { get; private set; }
        public Vector3 HitDirection { get; private set; }
        public Object Owner { get; private set; }
        private string CustomContextName { get; set; }

        /// <summary>获取用于错误信息和调试输出的特效来源名称。</summary>
        public string ContextName => string.IsNullOrEmpty(CustomContextName) ? ResolveDefaultContextName() : CustomContextName;

        /// <summary>创建动画事件触发的攻击动作特效上下文。</summary>
        public static CombatEffectPlayContext ForAttack(SkillConfig skill, SkillEffectBinding binding, CombatAbilitySystem source, Object owner)
        {
            return new CombatEffectPlayContext(skill, binding, source, null, source.transform.position, source.transform.forward, owner);
        }

        /// <summary>创建战斗结算事件触发的命中或受伤特效上下文。</summary>
        public static CombatEffectPlayContext ForCombatEvent(CombatEvent combatEvent, SkillEffectBinding binding)
        {
            return new CombatEffectPlayContext(
                combatEvent.Skill,
                binding,
                combatEvent.Source,
                combatEvent.Target,
                combatEvent.HitPoint,
                combatEvent.HitDirection,
                combatEvent.Source);
        }

        /// <summary>创建处决 Timeline 标记触发的特效上下文。</summary>
        public static CombatEffectPlayContext ForExecution(
            SkillEffectBinding binding,
            CombatAbilitySystem source,
            CombatAbilitySystem target,
            Vector3 hitPoint,
            Vector3 hitDirection,
            Object owner)
        {
            return new CombatEffectPlayContext(null, binding, source, target, hitPoint, hitDirection, owner);
        }

        /// <summary>创建 Buff 持续特效上下文，强制使用手动停止通道等待 Buff 生命周期回收。</summary>
        public static CombatEffectPlayContext ForBuff(
            string effectId,
            string channel,
            CombatAbilitySystem ownerAbilitySystem,
            Object owner)
        {
            SkillEffectBinding binding = new SkillEffectBinding
            {
                effectId = effectId,
                transformOverride = new CombatEffectTransformOverride
                {
                    overrideRecycleMode = true,
                    recycleMode = CombatEffectRecycleMode.ManualStop,
                    overrideConcurrency = true,
                    concurrency = CombatEffectConcurrency.UniqueChannel,
                    overrideChannel = true,
                    channel = channel
                }
            };

            return new CombatEffectPlayContext(
                null,
                binding,
                ownerAbilitySystem,
                ownerAbilitySystem,
                ownerAbilitySystem.transform.position,
                ownerAbilitySystem.transform.forward,
                owner,
                $"Buff{effectId}");
        }

        /// <summary>保存一次特效播放所需的完整上下文。</summary>
        private CombatEffectPlayContext(
            SkillConfig skill,
            SkillEffectBinding binding,
            CombatAbilitySystem source,
            CombatAbilitySystem target,
            Vector3 hitPoint,
            Vector3 hitDirection,
            Object owner,
            string customContextName = null)
        {
            Skill = skill;
            Binding = binding;
            Source = source;
            Target = target;
            HitPoint = hitPoint;
            HitDirection = hitDirection;
            Owner = owner;
            CustomContextName = customContextName;
        }

        /// <summary>按原有技能或处决来源生成默认上下文名称。</summary>
        private string ResolveDefaultContextName()
        {
            return Skill == null ? "处决" : $"技能{Skill.skillId}";
        }
    }
}
