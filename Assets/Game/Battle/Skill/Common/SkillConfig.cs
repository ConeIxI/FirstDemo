using System;
using Game.Battle.Ability;
using Game.Battle.Combat.Config;
using Game.Common;
using UnityEngine;

namespace Game.Battle.Skill.Common
{
    [Serializable]
    public class SkillConfig : IConfig, ICombatSkillConfig
    {
        public int skillId;
        public string skillName;
        public string skillAnimationName;
        public float attackRange;
        public int comboNextSkillId;
        public SkillType skillType;
        public SkillHitWeight hitWeight;
        public int battleSpiritCost;
        public int battleSpiritGainOnHit;
        public CombatTag[] requiredTags;
        public CombatTag[] blockedTags;
        public CombatTag[] activeTags;
        public CombatHitConfig hitConfig;
        public InterruptConfig interruptConfig;
        public SkillEffectBinding[] attackEffects;
        public SkillEffectBinding[] onHitEffects;
        public SkillEffectBinding[] onBlockEffects;
        public SkillEffectBinding[] onParryEffects;
        public SkillAudioConfig skillAudioConfig;

        public SkillType SkillType
        {
            get { return skillType; }
        }

        public SkillHitWeight HitWeight
        {
            get { return hitWeight; }
        }

        public int BattleSpiritGainOnHit
        {
            get { return battleSpiritGainOnHit; }
        }

        public CombatHitConfig HitConfig
        {
            get { return hitConfig; }
        }

        public InterruptConfig InterruptConfig
        {
            get { return interruptConfig; }
        }
    }

    public static class SkillConfigDefaults
    {
        private const float DefaultPlayerNormalAttackMultiplier = 1f;
        private const int DefaultPlayerNormalAttackStabilityDamage = 10;
        private const int DefaultPlayerNormalAttackBattleSpiritGain = 8;
        private const float DefaultEnemyAttackMultiplier = 1.2f;
        private const int DefaultEnemyStabilityDamage = 15;
        private const int DefaultFinalComboInterruptLevel = 1;
        private const int DefaultEnemyFinalComboInterruptResistLevel = 99;

        /// <summary>补齐玩家技能的兼容默认值并归一化数组字段。</summary>
        public static void ApplyPlayerDefaults(SkillConfig config, SkillType skillType, bool isFinalCombo)
        {
            if (config == null)
            {
                return;
            }

            config.skillType = skillType;
            EnsureEffectArrays(config);
            EnsureTagArrays(config);
            EnsureHitConfig(config);
            bool createdInterruptConfig = EnsureInterruptConfig(config);

            if (skillType == SkillType.NormalAttack)
            {
                ApplyPositiveDefaultHitValues(config.hitConfig, DefaultPlayerNormalAttackMultiplier, DefaultPlayerNormalAttackStabilityDamage);
                if (config.battleSpiritGainOnHit <= 0)
                {
                    config.battleSpiritGainOnHit = DefaultPlayerNormalAttackBattleSpiritGain;
                }
            }

            if (isFinalCombo)
            {
                // 旧 JSON 没有打断配置时，最后一段普攻默认具备基础打断能力。
                if (createdInterruptConfig)
                {
                    config.interruptConfig.canInterrupt = true;
                }

                if (config.interruptConfig.interruptLevel <= 0)
                {
                    config.interruptConfig.interruptLevel = DefaultFinalComboInterruptLevel;
                }
            }
        }

        /// <summary>补齐敌人技能的兼容默认值并归一化数组字段。</summary>
        public static void ApplyEnemyDefaults(SkillConfig config)
        {
            if (config == null)
            {
                return;
            }

            config.skillType = SkillType.EnemySkill;
            EnsureEffectArrays(config);
            EnsureTagArrays(config);
            EnsureHitConfig(config);
            bool createdInterruptConfig = EnsureInterruptConfig(config);
            ApplyPositiveDefaultHitValues(config.hitConfig, DefaultEnemyAttackMultiplier, DefaultEnemyStabilityDamage);

            if (config.comboNextSkillId == 0)
            {
                // 旧敌人最终段缺省视为霸体，但保留 JSON 已显式写出的打断字段。
                if (createdInterruptConfig)
                {
                    config.interruptConfig.canBeInterrupted = false;
                }

                if (!config.interruptConfig.canBeInterrupted && config.interruptConfig.interruptResistLevel <= 0)
                {
                    config.interruptConfig.interruptResistLevel = DefaultEnemyFinalComboInterruptResistLevel;
                }
            }
        }

        /// <summary>把缺失的特效绑定数组统一转换为空数组。</summary>
        private static void EnsureEffectArrays(SkillConfig config)
        {
            if (config.attackEffects == null)
            {
                config.attackEffects = new SkillEffectBinding[0];
            }

            if (config.onHitEffects == null)
            {
                config.onHitEffects = new SkillEffectBinding[0];
            }

            if (config.onBlockEffects == null)
            {
                config.onBlockEffects = new SkillEffectBinding[0];
            }

            if (config.onParryEffects == null)
            {
                config.onParryEffects = new SkillEffectBinding[0];
            }
        }

        /// <summary>把缺失的标签数组统一转换为空数组。</summary>
        private static void EnsureTagArrays(SkillConfig config)
        {
            if (config.requiredTags == null)
            {
                config.requiredTags = new CombatTag[0];
            }

            if (config.blockedTags == null)
            {
                config.blockedTags = new CombatTag[0];
            }

            if (config.activeTags == null)
            {
                config.activeTags = new CombatTag[0];
            }
        }

        private static void EnsureHitConfig(SkillConfig config)
        {
            if (config.hitConfig == null)
            {
                config.hitConfig = new CombatHitConfig();
            }
        }

        private static bool EnsureInterruptConfig(SkillConfig config)
        {
            if (config.interruptConfig != null)
            {
                return false;
            }

            config.interruptConfig = new InterruptConfig();
            return true;
        }

        /// <summary>仅为未配置的零倍率和零稳定伤害补默认值，保留负数供配置校验拒绝。</summary>
        private static void ApplyPositiveDefaultHitValues(CombatHitConfig hitConfig, float attackMultiplier, int stabilityDamage)
        {
            if (hitConfig.attackMultiplier == 0f)
            {
                hitConfig.attackMultiplier = attackMultiplier;
            }

            if (hitConfig.stabilityDamage == 0)
            {
                hitConfig.stabilityDamage = stabilityDamage;
            }
        }
    }

    [Serializable]
    public class SkillEffectBinding
    {
        public string triggerKey;
        public string effectId;
        public CombatEffectAttachmentOverride attachmentOverride;
        public CombatEffectTransformOverride transformOverride;
    }

    [Serializable]
    public class CombatEffectAttachmentOverride
    {
        public bool overrideAttachment;
        public CombatEffectAttachment attachment;
        public bool overrideSocketName;
        public string socketName;
        public bool overrideFollow;
        public bool follow;
    }

    [Serializable]
    public class CombatEffectTransformOverride
    {
        public bool overridePosition;
        public Vec3 position;
        public bool overrideRotation;
        public Vec3 rotation;
        public bool overrideScale;
        public Vec3 scale;
        public bool overrideOrientation;
        public CombatEffectOrientation orientation;
        public bool overrideRecycleMode;
        public CombatEffectRecycleMode recycleMode;
        public bool overrideDuration;
        public float duration;
        public bool overrideConcurrency;
        public CombatEffectConcurrency concurrency;
        public bool overrideChannel;
        public string channel;
    }

    public enum CombatEffectAttachment
    {
        WorldHitPoint,
        SourceRoot,
        SourceSocket,
        TargetSocket,
        TargetPreloadedEffect
    }

    public enum CombatEffectOrientation
    {
        ConfigRotation,
        SourceForward,
        HitDirection
    }

    public enum CombatEffectRecycleMode
    {
        ParticleComplete,
        FixedDuration,
        ManualStop
    }

    public enum CombatEffectConcurrency
    {
        Stack,
        UniqueChannel
    }

    [Serializable]
    public class SkillAudioConfig
    {
        public string castSfxPath;
        public string hitSfxPath;
    }

    [Serializable]
    public struct Vec3
    {
        public float x;
        public float y;
        public float z;

        /// <summary>把配置向量转换为 Unity 向量。</summary>
        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }
    }
}
