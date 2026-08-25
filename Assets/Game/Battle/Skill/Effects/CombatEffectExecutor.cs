using Game.Battle.Ability;
using Game.Battle.Skill.Common;
using GameMain2.Scripts.Character;

namespace Game.Battle.Skill.Effects
{
    public static class CombatEffectExecutor
    {
        private const string PlayerDefenseHitEffectId = "PlayerDefenseHit";
        private static readonly SkillEffectBinding PlayerDefenseHitEffect = new SkillEffectBinding
        {
            effectId = PlayerDefenseHitEffectId
        };
        private static readonly SkillEffectBinding[] PlayerDefenseHitEffects = { PlayerDefenseHitEffect };

        /// <summary>根据战斗事件类型执行技能配置的命中、格挡或弹反效果。</summary>
        public static void Execute(CombatEvent combatEvent)
        {
            if (combatEvent == null || combatEvent.Skill == null)
            {
                return;
            }

            ExecuteSpawnEffects(combatEvent, ResolveEffects(combatEvent));
        }

        /// <summary>按战斗事件类型选择技能的对应特效绑定数组。</summary>
        private static SkillEffectBinding[] ResolveEffects(CombatEvent combatEvent)
        {
            switch (combatEvent.Type)
            {
                case CombatEventType.Hit:
                    return combatEvent.Skill.onHitEffects;
                case CombatEventType.Blocked:
                    return IsPlayerDefenseHit(combatEvent)
                        ? PlayerDefenseHitEffects
                        : combatEvent.Skill.onBlockEffects;
                case CombatEventType.Parried:
                    return combatEvent.Skill.onParryEffects;
                default:
                    return null;
            }
        }

        /// <summary>判断本次格挡是否命中玩家，玩家防御受击统一使用独立特效配置。</summary>
        private static bool IsPlayerDefenseHit(CombatEvent combatEvent)
        {
            return combatEvent.Target.GetComponent<PlayerController>() != null;
        }

        /// <summary>把全部有效特效绑定交给战斗特效服务播放。</summary>
        private static void ExecuteSpawnEffects(CombatEvent combatEvent, SkillEffectBinding[] effects)
        {
            if (effects == null)
            {
                return;
            }

            for (int i = 0; i < effects.Length; i++)
            {
                SkillEffectBinding effect = effects[i];
                if (effect == null || string.IsNullOrEmpty(effect.effectId))
                {
                    continue;
                }

                CombatEffectPlayContext context = CombatEffectPlayContext.ForCombatEvent(combatEvent, effect);
                CombatEffectService.Instance.Play(context);
            }
        }
    }
}
