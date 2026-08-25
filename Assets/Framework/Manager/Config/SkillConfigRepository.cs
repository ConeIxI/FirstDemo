using System;
using System.Collections.Generic;
using System.Linq;
using Game.Battle.Ability;
using Game.Battle.Combat.Config;
using Game.Battle.Skill.Common;
using Game.Character.Common;
using Newtonsoft.Json;
using UnityEngine;

namespace GameMain2.Framework.Manager
{
    /// <summary>
    /// 技能配置仓库，负责加载敌人与玩家技能配置并校验连段引用。
    /// </summary>
    internal sealed class SkillConfigRepository
    {
        private readonly Dictionary<int, SkillConfig> m_enemySkillConfigs = new Dictionary<int, SkillConfig>();
        private readonly Dictionary<WeaponType, Dictionary<int, SkillConfig>> m_playerSkillConfigs =
            new Dictionary<WeaponType, Dictionary<int, SkillConfig>>();

        /// <summary>加载全部技能配置，先清空旧数据再写入新配置。</summary>
        public void LoadAll(ResourceManager resourceManager)
        {
            m_enemySkillConfigs.Clear();
            m_playerSkillConfigs.Clear();
            LoadPlayerSkillConfigs(resourceManager);
            LoadEnemySkillConfigs(resourceManager);
        }

        /// <summary>按技能 Id 查询敌人技能配置。</summary>
        public SkillConfig GetEnemySkillConfig(int id)
        {
            if (!m_enemySkillConfigs.TryGetValue(id, out SkillConfig config))
            {
                throw new Exception($"未找到该技能({id})配置");
            }

            return config;
        }

        /// <summary>按武器类型和技能 Id 查询玩家技能配置。</summary>
        public SkillConfig GetPlayerSkillConfig(WeaponType type, int id)
        {
            if (!m_playerSkillConfigs.TryGetValue(type, out Dictionary<int, SkillConfig> configs)
                || !configs.TryGetValue(id, out SkillConfig config))
            {
                throw new Exception($"未找到该技能({id})配置");
            }

            return config;
        }

        /// <summary>返回全部敌人技能配置快照。</summary>
        public SkillConfig[] GetEnemySkillConfigs()
        {
            return m_enemySkillConfigs.Values.ToArray();
        }

        /// <summary>加载、归一化并校验全部敌人技能配置。</summary>
        private void LoadEnemySkillConfigs(ResourceManager resourceManager)
        {
            TextAsset enemySkillConfig = resourceManager.LoadAsset<TextAsset>("Data/EnemySkillConfig.json");
            SkillConfig[] enemyConfigs = JsonConvert.DeserializeObject<SkillConfig[]>(enemySkillConfig.text);

            for (int i = 0; i < enemyConfigs.Length; i++)
            {
                SkillConfig config = enemyConfigs[i];
                SkillConfigDefaults.ApplyEnemyDefaults(config);
                ValidateSkillConfig(config);
                m_enemySkillConfigs.Add(config.skillId, config);
            }

            ValidateComboReferences(m_enemySkillConfigs);
        }

        /// <summary>加载玩家武器技能配置，并保留 JSON 中声明的技能类型。</summary>
        private void LoadPlayerSkillConfigs(ResourceManager resourceManager)
        {
            Dictionary<WeaponType, string> paths = new Dictionary<WeaponType, string>();
            paths.Add(WeaponType.SingleSword, "Data/WeaponConfig/SingleSwordSkillConfig.json");
            paths.Add(WeaponType.Spear, "Data/WeaponConfig/SpearSkillConfig.json");

            foreach (KeyValuePair<WeaponType, string> path in paths)
            {
                TextAsset skillConfig = resourceManager.LoadAsset<TextAsset>(path.Value);
                SkillConfig[] configs = JsonConvert.DeserializeObject<SkillConfig[]>(skillConfig.text);
                Dictionary<int, SkillConfig> weaponSkillConfigs = new Dictionary<int, SkillConfig>();
                for (int i = 0; i < configs.Length; i++)
                {
                    SkillConfig config = configs[i];
                    SkillType configuredSkillType = config.skillType;
                    bool isFinalNormalCombo = configuredSkillType == SkillType.NormalAttack && config.comboNextSkillId == 0;
                    SkillConfigDefaults.ApplyPlayerDefaults(config, configuredSkillType, isFinalNormalCombo);
                    ValidateSkillConfig(config);
                    weaponSkillConfigs.Add(config.skillId, config);
                }

                ValidateComboReferences(weaponSkillConfigs);
                m_playerSkillConfigs.Add(path.Key, weaponSkillConfigs);
            }
        }

        /// <summary>校验单个技能的基础数值和标签约束。</summary>
        private static void ValidateSkillConfig(SkillConfig config)
        {
            if (config.skillId <= 0)
            {
                throw new Exception("技能Id必须大于零");
            }

            if (config.attackRange <= 0f)
            {
                throw new Exception($"技能{config.skillId}攻击范围必须大于零");
            }

            if (config.battleSpiritCost < 0)
            {
                throw new Exception($"技能{config.skillId}战意消耗不能为负数");
            }

            if (config.hitConfig.attackMultiplier < 0f || config.hitConfig.stabilityDamage < 0)
            {
                throw new Exception($"技能{config.skillId}伤害不能为负数");
            }

            if (config.activeTags.Contains(CombatTag.Dead))
            {
                throw new Exception($"技能{config.skillId}不能激活死亡标签");
            }

            if (config.requiredTags.Intersect(config.blockedTags).Any())
            {
                throw new Exception($"技能{config.skillId}的必需标签和阻止标签不能重复");
            }
        }

        /// <summary>校验每个非零连段后继 Id 都存在于同一技能集合。</summary>
        private static void ValidateComboReferences(Dictionary<int, SkillConfig> configs)
        {
            foreach (SkillConfig config in configs.Values)
            {
                if (config.comboNextSkillId != 0 && !configs.ContainsKey(config.comboNextSkillId))
                {
                    throw new Exception($"技能{config.skillId}的后续连段技能{config.comboNextSkillId}不存在");
                }
            }
        }
    }
}
