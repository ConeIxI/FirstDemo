using System;
using System.Collections.Generic;
using System.Linq;
using Game.Config.Item;
using Newtonsoft.Json;
using UnityEngine;

namespace GameMain2.Framework.Manager
{
    /// <summary>
    /// 物品配置仓库，负责加载、校验和查询武器、防具与消耗品配置。
    /// </summary>
    internal sealed class ItemConfigRepository
    {
        private Dictionary<int, WeaponItemConfig> m_weaponItemConfigs = new Dictionary<int, WeaponItemConfig>();
        private Dictionary<int, HelmetItemConfig> m_helmetItemConfigs = new Dictionary<int, HelmetItemConfig>();
        private Dictionary<int, ArmorItemConfig> m_armorItemConfigs = new Dictionary<int, ArmorItemConfig>();
        private Dictionary<int, LeggingsItemConfig> m_leggingsItemConfigs = new Dictionary<int, LeggingsItemConfig>();
        private Dictionary<int, GlovesItemConfig> m_glovesItemConfigs = new Dictionary<int, GlovesItemConfig>();
        private Dictionary<int, ConsumableItemConfig> m_consumableItemConfigs = new Dictionary<int, ConsumableItemConfig>();

        /// <summary>加载全部物品配置分类，成功后替换旧缓存。</summary>
        public void LoadAll(ResourceManager resourceManager)
        {
            m_weaponItemConfigs = LoadItemConfigs<WeaponItemConfig>(resourceManager, "Data/ItemConfig/WeaponItemConfig.json", "武器");
            m_helmetItemConfigs = LoadItemConfigs<HelmetItemConfig>(resourceManager, "Data/ItemConfig/HelmetItemConfig.json", "头盔");
            m_armorItemConfigs = LoadItemConfigs<ArmorItemConfig>(resourceManager, "Data/ItemConfig/ArmorItemConfig.json", "胸甲");
            m_leggingsItemConfigs = LoadItemConfigs<LeggingsItemConfig>(resourceManager, "Data/ItemConfig/LeggingsItemConfig.json", "护腿");
            m_glovesItemConfigs = LoadItemConfigs<GlovesItemConfig>(resourceManager, "Data/ItemConfig/GlovesItemConfig.json", "臂铠");
            m_consumableItemConfigs = LoadItemConfigs<ConsumableItemConfig>(resourceManager, "Data/ItemConfig/ConsumableItemConfig.json", "消耗品");
        }

        /// <summary>按 Id 查询武器配置。</summary>
        public WeaponItemConfig GetWeaponItemConfig(int id)
        {
            return GetItemConfig(m_weaponItemConfigs, id, "武器");
        }

        /// <summary>返回全部武器配置快照。</summary>
        public WeaponItemConfig[] GetWeaponItemConfigs()
        {
            return m_weaponItemConfigs.Values.ToArray();
        }

        /// <summary>按 Id 查询头盔配置。</summary>
        public HelmetItemConfig GetHelmetItemConfig(int id)
        {
            return GetItemConfig(m_helmetItemConfigs, id, "头盔");
        }

        /// <summary>返回全部头盔配置快照。</summary>
        public HelmetItemConfig[] GetHelmetItemConfigs()
        {
            return m_helmetItemConfigs.Values.ToArray();
        }

        /// <summary>按 Id 查询胸甲配置。</summary>
        public ArmorItemConfig GetArmorItemConfig(int id)
        {
            return GetItemConfig(m_armorItemConfigs, id, "胸甲");
        }

        /// <summary>返回全部胸甲配置快照。</summary>
        public ArmorItemConfig[] GetArmorItemConfigs()
        {
            return m_armorItemConfigs.Values.ToArray();
        }

        /// <summary>按 Id 查询护腿配置。</summary>
        public LeggingsItemConfig GetLeggingsItemConfig(int id)
        {
            return GetItemConfig(m_leggingsItemConfigs, id, "护腿");
        }

        /// <summary>返回全部护腿配置快照。</summary>
        public LeggingsItemConfig[] GetLeggingsItemConfigs()
        {
            return m_leggingsItemConfigs.Values.ToArray();
        }

        /// <summary>按 Id 查询臂铠配置。</summary>
        public GlovesItemConfig GetGlovesItemConfig(int id)
        {
            return GetItemConfig(m_glovesItemConfigs, id, "臂铠");
        }

        /// <summary>返回全部臂铠配置快照。</summary>
        public GlovesItemConfig[] GetGlovesItemConfigs()
        {
            return m_glovesItemConfigs.Values.ToArray();
        }

        /// <summary>按 Id 查询消耗品配置。</summary>
        public ConsumableItemConfig GetConsumableItemConfig(int id)
        {
            return GetItemConfig(m_consumableItemConfigs, id, "消耗品");
        }

        /// <summary>返回全部消耗品配置快照。</summary>
        public ConsumableItemConfig[] GetConsumableItemConfigs()
        {
            return m_consumableItemConfigs.Values.ToArray();
        }

        /// <summary>加载指定分类的物品配置，并校验公共字段与装备专属字段。</summary>
        private static Dictionary<int, T> LoadItemConfigs<T>(ResourceManager resourceManager, string path, string categoryName)
            where T : ItemConfigBase
        {
            TextAsset itemConfig = resourceManager.LoadAsset<TextAsset>(path);
            if (itemConfig == null)
            {
                throw new Exception($"未找到{categoryName}配置文件：{path}");
            }

            T[] configs = JsonConvert.DeserializeObject<T[]>(itemConfig.text);
            if (configs == null)
            {
                throw new Exception($"{categoryName}配置文件解析失败：{path}");
            }

            Dictionary<int, T> result = new Dictionary<int, T>();
            for (int i = 0; i < configs.Length; i++)
            {
                T config = configs[i];
                ValidateItemConfig(config, result, categoryName, path);
                result.Add(config.id, config);
            }

            return result;
        }

        /// <summary>校验单个物品配置的公共字段和分类专属字段。</summary>
        private static void ValidateItemConfig<T>(T config, Dictionary<int, T> result, string categoryName, string path)
            where T : ItemConfigBase
        {
            if (config == null)
            {
                throw new Exception($"{categoryName}配置存在空配置项：{path}");
            }

            if (config.id <= 0)
            {
                throw new Exception($"{categoryName}配置存在非法Id：{config.id}");
            }

            if (result.ContainsKey(config.id))
            {
                throw new Exception($"{categoryName}配置存在重复Id：{config.id}");
            }

            if (config is WeaponItemConfig weaponConfig)
            {
                ValidateWeaponItemConfig(weaponConfig);
            }

            if (config is DefenseEquipmentItemConfig defenseEquipmentConfig)
            {
                ValidateDefenseEquipmentItemConfig(defenseEquipmentConfig, categoryName);
            }
        }

        /// <summary>校验武器必须为三个技能槽提供一一对应的图标地址。</summary>
        private static void ValidateWeaponItemConfig(WeaponItemConfig config)
        {
            if (config.skillIconAddresses == null
                || config.skillIconAddresses.Length != WeaponItemConfig.SkillIconCount)
            {
                throw new Exception(
                    $"武器配置技能图标地址数量必须为{WeaponItemConfig.SkillIconCount}，Id：{config.id}");
            }
        }

        /// <summary>校验防具防御力上下限合法，避免掉落随机范围无效。</summary>
        private static void ValidateDefenseEquipmentItemConfig(DefenseEquipmentItemConfig config, string categoryName)
        {
            if (config.minDefense < 0)
            {
                throw new Exception($"{categoryName}配置防御下限不能为负数，Id：{config.id}");
            }

            if (config.maxDefense < config.minDefense)
            {
                throw new Exception($"{categoryName}配置防御上限不能小于下限，Id：{config.id}");
            }
        }

        /// <summary>从指定配置字典中读取物品配置，缺失时直接抛出明确错误。</summary>
        private static T GetItemConfig<T>(Dictionary<int, T> configs, int id, string categoryName)
            where T : ItemConfigBase
        {
            if (!configs.TryGetValue(id, out T config))
            {
                throw new Exception($"未找到{categoryName}配置，Id：{id}");
            }

            return config;
        }
    }
}
