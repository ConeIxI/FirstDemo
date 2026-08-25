using System;

namespace Game.Battle.Combat.Config
{
    public enum SkillType
    {
        NormalAttack,
        WeaponSkill,
        EnemySkill
    }

    public enum SkillHitWeight
    {
        Light = 0,
        Heavy = 1
    }

    public interface ICombatSkillConfig
    {
        SkillType SkillType { get; }
        SkillHitWeight HitWeight { get; }
        int BattleSpiritGainOnHit { get; }
        CombatHitConfig HitConfig { get; }
        InterruptConfig InterruptConfig { get; }
    }

    [Serializable]
    public class CombatHitConfig
    {
        public float attackMultiplier;
        public int stabilityDamage;
        public int parryStabilityRestore = 10;
        public bool canBeBlocked = true;
        /// <summary>技能被弹反后是否中断自身招式并播放被弹反动画；所有技能都能被弹反窗口拦截。</summary>
        public bool canBeParried = true;
        public float hitStopTime;
        /// <summary>受伤或格挡时沿命中方向后退的距离，弹反和无敌不会使用。</summary>
        public float moveBackDistance;
        public string hitReactionName = "GetHit";
    }

    [Serializable]
    public class InterruptConfig
    {
        /// <summary>本次命中是否具备打断目标当前动作的能力。</summary>
        public bool canInterrupt;

        /// <summary>本次命中的打断等级；等级低于目标抗打断等级时无法打断。</summary>
        public int interruptLevel;

        /// <summary>释放该技能期间，自身是否可以被敌方命中打断。</summary>
        public bool canBeInterrupted = true;

        /// <summary>释放该技能期间，自身抵抗打断的等级。</summary>
        public int interruptResistLevel;

        /// <summary>本次命中是否允许打断处于防御状态的目标。</summary>
        public bool canInterruptDefence;
    }
}
