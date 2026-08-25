using Game.Battle.Skill.Common;
using GameMain2.Framework.Core;
using UnityEngine;

namespace Game.Battle.Ability
{
    public enum CombatEventType
    {
        Invincible,
        Parried,
        Blocked,
        Hit
    }

    public sealed class CombatEvent : EventArgsBase
    {
        public static readonly int EventId = typeof(CombatEvent).GetHashCode();

        public override int Id => EventId;
        public CombatEventType Type { get; }
        public CombatAbilitySystem Source { get; }
        public CombatAbilitySystem Target { get; }
        public SkillConfig Skill { get; }
        public int TargetHealthDamage { get; }
        public int TargetStabilityDamage { get; }
        public int SourceStabilityDamage { get; }
        public int SourceBattleSpiritGain { get; }
        public bool TargetInterrupted { get; }
        public bool TargetShouldReact { get; }
        public bool TargetUnbalanced { get; }
        public bool SourceUnbalanced { get; }
        public bool TargetDead { get; }
        public Vector3 HitPoint { get; }
        public Vector3 HitDirection { get; }

        /// <summary>创建一次不可变的命中结算事件。</summary>
        public CombatEvent(
            CombatEventType type,
            CombatAbilitySystem source,
            CombatAbilitySystem target,
            SkillConfig skill,
            int targetHealthDamage,
            int targetStabilityDamage,
            int sourceStabilityDamage,
            int sourceBattleSpiritGain,
            bool targetInterrupted,
            bool targetShouldReact,
            bool targetUnbalanced,
            bool sourceUnbalanced,
            bool targetDead,
            Vector3 hitPoint,
            Vector3 hitDirection)
        {
            Type = type;
            Source = source;
            Target = target;
            Skill = skill;
            TargetHealthDamage = targetHealthDamage;
            TargetStabilityDamage = targetStabilityDamage;
            SourceStabilityDamage = sourceStabilityDamage;
            SourceBattleSpiritGain = sourceBattleSpiritGain;
            TargetInterrupted = targetInterrupted;
            TargetShouldReact = targetShouldReact;
            TargetUnbalanced = targetUnbalanced;
            SourceUnbalanced = sourceUnbalanced;
            TargetDead = targetDead;
            HitPoint = hitPoint;
            HitDirection = hitDirection;
        }
    }
}
