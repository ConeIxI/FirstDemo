using System;
using System.Collections.Generic;
using Game.Battle.Buff;
using Newtonsoft.Json;
using UnityEngine;

namespace GameMain2.Framework.Manager
{
    /// <summary>
    /// Buff 配置仓库，负责读取、校验和查询战斗 Buff 配置。
    /// </summary>
    internal sealed class BuffConfigRepository
    {
        private readonly Dictionary<int, CombatBuffConfig> m_configs = new Dictionary<int, CombatBuffConfig>();

        /// <summary>从 Addressables 配置文件加载全部 Buff 配置。</summary>
        public void Load(ResourceManager resourceManager)
        {
            TextAsset buffConfigAsset = resourceManager.LoadAsset<TextAsset>("Data/BuffConfig.json");
            if (buffConfigAsset == null)
            {
                throw new Exception("未找到 Buff 配置文件：Data/BuffConfig.json");
            }

            CombatBuffConfig[] configs = JsonConvert.DeserializeObject<CombatBuffConfig[]>(buffConfigAsset.text);
            if (configs == null)
            {
                throw new Exception("Buff 配置文件解析失败：Data/BuffConfig.json");
            }

            m_configs.Clear();
            for (int i = 0; i < configs.Length; i++)
            {
                AddConfig(configs[i]);
            }
        }

        /// <summary>按 BuffId 查询 Buff 配置，缺失时返回 null 供调用方软失败。</summary>
        public CombatBuffConfig GetConfig(int id)
        {
            CombatBuffConfig config;
            m_configs.TryGetValue(id, out config);
            return config;
        }

        /// <summary>校验并加入单个 Buff 配置，重复 Id 直接失败。</summary>
        private void AddConfig(CombatBuffConfig config)
        {
            ValidateConfig(config);
            if (m_configs.ContainsKey(config.buffId))
            {
                throw new Exception($"Buff 配置存在重复Id：{config.buffId}");
            }

            m_configs.Add(config.buffId, config);
        }

        /// <summary>校验单个 Buff 配置的基础字段。</summary>
        private static void ValidateConfig(CombatBuffConfig config)
        {
            if (config == null)
            {
                throw new Exception("Buff 配置存在空配置项");
            }

            if (config.buffId <= 0)
            {
                throw new Exception($"Buff 配置存在非法Id：{config.buffId}");
            }

            if (config.duration <= 0f)
            {
                throw new Exception($"Buff{config.buffId}持续时间必须大于零");
            }

            if ((config.type == CombatBuffType.HealthRegen || config.type == CombatBuffType.HealthDamage)
                && (config.tickInterval <= 0f || config.tickValue <= 0))
            {
                throw new Exception($"Buff{config.buffId}持续生命效果必须配置正数 Tick 间隔和数值");
            }
        }
    }
}
