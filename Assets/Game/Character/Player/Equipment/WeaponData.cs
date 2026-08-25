using System;
using System.Collections.Generic;
using Game.Character.Common;
using UnityEngine;
using UnityEngine.Playables;

namespace Game.Character.Equipment
{
    /// <summary>
    /// 用于存储武器相关信息的数据类。此类包含了武器的类型、动画控制器、持握骨骼名、技能ID列表配置等信息，。
    /// </summary>
    public class WeaponData : MonoBehaviour
    {
        /// <summary>
        /// 武器类型
        /// </summary>
        public WeaponType weaponType;
        
        /// <summary>
        /// 该武器类型的动画覆盖
        /// </summary>
        public AnimatorOverrideController animatorOverride;
        

        /// <summary>
        /// 普通攻击技能组，通常配置为该武器的轻攻击连段。
        /// </summary>
        public int[] normalAttackSkillIds;

        /// <summary>
        /// 武器技能槽，固定对应 1/2/3 三个技能输入。
        /// </summary>
        public int[] weaponSkillIds;

        /// <summary>
        /// 奔跑攻击技能 ID，仅作为武器数据配置入口，具体触发逻辑由状态机决定。
        /// </summary>
        public int runningAttackSkillId;

        /// <summary>
        /// 防御反击技能 ID，仅作为武器数据配置入口，具体触发逻辑由状态机决定。
        /// </summary>
        public int defenceCounterSkillId;

        [Header("处决配置")]
        [SerializeField] private PlayableAsset executionTimeline;
        [SerializeField, Range(0f, 1f)] private float executionMaxHealthDamagePercent = 0.35f;

        /// <summary>
        /// 获取普通攻击技能组；新字段未配置时回退到旧版 skillIds。
        /// </summary>
        public int[] GetNormalAttackSkillIds()
        {
            return normalAttackSkillIds;
        }

        /// <summary>
        /// 获取武器技能槽配置；第 0/1/2 位分别对应 1/2/3 键。
        /// </summary>
        public int[] GetWeaponSkillIds()
        {
            return weaponSkillIds;
        }

        /// <summary>
        /// 获取奔跑攻击技能 ID。
        /// </summary>
        public int GetRunningAttackSkillId()
        {
            return runningAttackSkillId;
        }

        /// <summary>
        /// 获取防御反击技能 ID。
        /// </summary>
        public int GetDefenceCounterSkillId()
        {
            return defenceCounterSkillId;
        }

        /// <summary>获取当前武器专属处决 Timeline，缺失时由处决控制器直接报错。</summary>
        public PlayableAsset GetExecutionTimeline()
        {
            return executionTimeline;
        }

        /// <summary>获取处决伤害占目标最大生命值的百分比。</summary>
        public float GetExecutionMaxHealthDamagePercent()
        {
            return executionMaxHealthDamagePercent;
        }

        /// <summary>
        /// 遍历该武器所有可用技能 ID，供玩家技能管理器同步可释放列表。
        /// </summary>
        public IEnumerable<int> EnumerateAllSkillIds()
        {
            foreach (int skillId in EnumerateSkillIds(normalAttackSkillIds))
            {
                yield return skillId;
            }

            foreach (int skillId in EnumerateSkillIds(weaponSkillIds))
            {
                yield return skillId;
            }

            if (runningAttackSkillId > 0)
            {
                yield return runningAttackSkillId;
            }

            if (defenceCounterSkillId > 0)
            {
                yield return defenceCounterSkillId;
            }
        }

        /// <summary>
        /// 过滤空数组和非法技能 ID，避免运行时加载无效技能。
        /// </summary>
        private IEnumerable<int> EnumerateSkillIds(int[] ids)
        {
            if (ids == null)
            {
                yield break;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                if (ids[i] > 0)
                {
                    yield return ids[i];
                }
            }
        }

    }
}
