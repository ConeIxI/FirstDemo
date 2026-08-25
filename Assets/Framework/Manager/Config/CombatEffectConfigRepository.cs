using System;
using System.Collections.Generic;
using System.Linq;
using Game.Battle.Skill.Common;
using Game.Battle.Skill.Effects;
using Newtonsoft.Json;
using UnityEngine;

namespace GameMain2.Framework.Manager
{
    /// <summary>
    /// 公共战斗特效配置仓库，负责读取、校验和查询特效配置。
    /// </summary>
    internal sealed class CombatEffectConfigRepository
    {
        private readonly Dictionary<string, CombatEffectConfig> m_configs = new Dictionary<string, CombatEffectConfig>();

        /// <summary>从 Addressables 配置文件加载全部公共战斗特效配置。</summary>
        public void Load(ResourceManager resourceManager)
        {
            TextAsset configAsset = resourceManager.LoadAsset<TextAsset>("Data/CombatEffectConfig.json");
            if (configAsset == null)
            {
                throw new Exception("未找到战斗特效配置文件：Data/CombatEffectConfig.json");
            }

            CombatEffectConfig[] configs = JsonConvert.DeserializeObject<CombatEffectConfig[]>(configAsset.text);
            if (configs == null)
            {
                throw new Exception("战斗特效配置文件解析失败：Data/CombatEffectConfig.json");
            }

            m_configs.Clear();
            for (int i = 0; i < configs.Length; i++)
            {
                AddConfig(configs[i]);
            }
        }

        /// <summary>按特效 Id 查询公共战斗特效配置。</summary>
        public CombatEffectConfig GetConfig(string effectId)
        {
            if (!m_configs.ContainsKey(effectId))
            {
                throw new Exception($"未找到战斗特效配置：{effectId}");
            }

            return m_configs[effectId];
        }

        /// <summary>返回全部公共战斗特效配置快照。</summary>
        public CombatEffectConfig[] GetConfigs()
        {
            return m_configs.Values.ToArray();
        }

        /// <summary>校验并加入单个特效配置，重复 Id 直接失败。</summary>
        private void AddConfig(CombatEffectConfig config)
        {
            ValidateConfig(config);
            if (m_configs.ContainsKey(config.effectId))
            {
                throw new Exception($"战斗特效配置存在重复Id：{config.effectId}");
            }

            m_configs.Add(config.effectId, config);
        }

        /// <summary>校验单个公共战斗特效配置的基础字段。</summary>
        private static void ValidateConfig(CombatEffectConfig config)
        {
            if (config == null)
            {
                throw new Exception("战斗特效配置存在空配置项");
            }

            if (string.IsNullOrEmpty(config.effectId))
            {
                throw new Exception("战斗特效配置存在空 effectId");
            }

            if (RequiresPrefabPath(config.attachment) && string.IsNullOrEmpty(config.path))
            {
                throw new Exception($"战斗特效{config.effectId}缺少 Prefab 路径");
            }

            if (RequiresSocketName(config.attachment) && string.IsNullOrEmpty(config.socketName))
            {
                throw new Exception($"战斗特效{config.effectId}缺少挂点名称");
            }

            if ((config.recycleMode == CombatEffectRecycleMode.ManualStop
                    || config.concurrency == CombatEffectConcurrency.UniqueChannel)
                && string.IsNullOrEmpty(config.channel))
            {
                throw new Exception($"战斗特效{config.effectId}缺少唯一通道名称");
            }

            if (config.recycleMode == CombatEffectRecycleMode.FixedDuration && config.duration <= 0f)
            {
                throw new Exception($"战斗特效{config.effectId}固定时长必须大于零");
            }
        }

        /// <summary>判断当前挂载模式是否需要通过 Prefab 路径动态生成特效。</summary>
        private static bool RequiresPrefabPath(CombatEffectAttachment attachment)
        {
            return attachment != CombatEffectAttachment.TargetPreloadedEffect;
        }

        /// <summary>判断当前挂载模式是否需要配置目标或来源层级里的子物体名称。</summary>
        private static bool RequiresSocketName(CombatEffectAttachment attachment)
        {
            return attachment == CombatEffectAttachment.SourceSocket
                || attachment == CombatEffectAttachment.TargetSocket
                || attachment == CombatEffectAttachment.TargetPreloadedEffect;
        }
    }
}
