using System;
using Game.Common;

namespace Game.Config.Item
{
    /// <summary>
    /// 物品配置公共字段。分类子类用于承载后续武器、装备、消耗品的专属配置。
    /// </summary>
    [Serializable]
    public class ItemConfigBase : IConfig
    {
        public int id;
        public string name;
        public string iconAddress;
        public string prefabAddress;
    }

    [Serializable]
    public class EquipmentItemConfig : ItemConfigBase
    {
        public string objectName;
    }

    [Serializable]
    public class WeaponItemConfig : EquipmentItemConfig
    {
        public const int SkillIconCount = 3;

        public int attack;
        public string[] skillIconAddresses;
    }

    [Serializable]
    public class DefenseEquipmentItemConfig : EquipmentItemConfig
    {
        public int minDefense;
        public int maxDefense;
    }

    [Serializable]
    public class HelmetItemConfig : DefenseEquipmentItemConfig
    {
    }

    [Serializable]
    public class ArmorItemConfig : DefenseEquipmentItemConfig
    {
    }

    [Serializable]
    public class LeggingsItemConfig : DefenseEquipmentItemConfig
    {
    }

    [Serializable]
    public class GlovesItemConfig : DefenseEquipmentItemConfig
    {
    }

    [Serializable]
    public class ConsumableItemConfig : ItemConfigBase
    {
        public int buffId;
    }
}
