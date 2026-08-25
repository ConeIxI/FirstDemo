using Game.Battle.Buff;
using Game.Battle.Skill.Common;
using Game.Battle.Skill.Effects;
using Game.Character.Common;
using Game.Config.Item;

namespace GameMain2.Framework.Manager
{
    public class ConfigManager : SingletonManager<ConfigManager>
    {
        private readonly SkillConfigRepository m_skillConfigs = new SkillConfigRepository();
        private readonly BuffConfigRepository m_buffConfigs = new BuffConfigRepository();
        private readonly CombatEffectConfigRepository m_combatEffectConfigs = new CombatEffectConfigRepository();
        private readonly ItemConfigRepository m_itemConfigs = new ItemConfigRepository();

        /// <summary>初始化配置管理器并加载全部静态配置。</summary>
        protected override void Awake()
        {
            base.Awake();
            if (!IsSingletonInstance)
            {
                return;
            }

            ResourceManager resourceManager = ResourceManager.Instance;
            m_skillConfigs.LoadAll(resourceManager);
            m_buffConfigs.Load(resourceManager);
            m_combatEffectConfigs.Load(resourceManager);
            m_itemConfigs.LoadAll(resourceManager);
        }

        /// <summary>按技能 Id 查询敌人技能配置。</summary>
        public SkillConfig GetSkillConfig(int id)
        {
            return m_skillConfigs.GetEnemySkillConfig(id);
        }

        /// <summary>按武器类型和技能 Id 查询玩家技能配置。</summary>
        public SkillConfig GetPlayerSkillConfig(WeaponType type, int id)
        {
            return m_skillConfigs.GetPlayerSkillConfig(type, id);
        }

        /// <summary>返回全部敌人技能配置快照。</summary>
        public SkillConfig[] GetSkillConfigs()
        {
            return m_skillConfigs.GetEnemySkillConfigs();
        }

        /// <summary>按特效 Id 查询公共战斗特效配置。</summary>
        public CombatEffectConfig GetCombatEffectConfig(string effectId)
        {
            return m_combatEffectConfigs.GetConfig(effectId);
        }

        /// <summary>返回全部公共战斗特效配置快照。</summary>
        public CombatEffectConfig[] GetCombatEffectConfigs()
        {
            return m_combatEffectConfigs.GetConfigs();
        }

        /// <summary>按 BuffId 查询 Buff 配置，缺失时返回 null 供调用方软失败。</summary>
        public CombatBuffConfig GetBuffConfig(int id)
        {
            return m_buffConfigs.GetConfig(id);
        }

        /// <summary>按 Id 查询武器配置。</summary>
        public WeaponItemConfig GetWeaponItemConfig(int id)
        {
            return m_itemConfigs.GetWeaponItemConfig(id);
        }

        /// <summary>返回全部武器配置快照。</summary>
        public WeaponItemConfig[] GetWeaponItemConfigs()
        {
            return m_itemConfigs.GetWeaponItemConfigs();
        }

        /// <summary>按 Id 查询头盔配置。</summary>
        public HelmetItemConfig GetHelmetItemConfig(int id)
        {
            return m_itemConfigs.GetHelmetItemConfig(id);
        }

        /// <summary>返回全部头盔配置快照。</summary>
        public HelmetItemConfig[] GetHelmetItemConfigs()
        {
            return m_itemConfigs.GetHelmetItemConfigs();
        }

        /// <summary>按 Id 查询胸甲配置。</summary>
        public ArmorItemConfig GetArmorItemConfig(int id)
        {
            return m_itemConfigs.GetArmorItemConfig(id);
        }

        /// <summary>返回全部胸甲配置快照。</summary>
        public ArmorItemConfig[] GetArmorItemConfigs()
        {
            return m_itemConfigs.GetArmorItemConfigs();
        }

        /// <summary>按 Id 查询护腿配置。</summary>
        public LeggingsItemConfig GetLeggingsItemConfig(int id)
        {
            return m_itemConfigs.GetLeggingsItemConfig(id);
        }

        /// <summary>返回全部护腿配置快照。</summary>
        public LeggingsItemConfig[] GetLeggingsItemConfigs()
        {
            return m_itemConfigs.GetLeggingsItemConfigs();
        }

        /// <summary>按 Id 查询臂铠配置。</summary>
        public GlovesItemConfig GetGlovesItemConfig(int id)
        {
            return m_itemConfigs.GetGlovesItemConfig(id);
        }

        /// <summary>返回全部臂铠配置快照。</summary>
        public GlovesItemConfig[] GetGlovesItemConfigs()
        {
            return m_itemConfigs.GetGlovesItemConfigs();
        }

        /// <summary>按 Id 查询消耗品配置。</summary>
        public ConsumableItemConfig GetConsumableItemConfig(int id)
        {
            return m_itemConfigs.GetConsumableItemConfig(id);
        }

        /// <summary>返回全部消耗品配置快照。</summary>
        public ConsumableItemConfig[] GetConsumableItemConfigs()
        {
            return m_itemConfigs.GetConsumableItemConfigs();
        }
    }
}
