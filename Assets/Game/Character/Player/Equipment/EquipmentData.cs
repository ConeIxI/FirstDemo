using Game.Character.Common;
using UnityEngine;

namespace Game.Character.Equipment
{

    [CreateAssetMenu(menuName = "EquipmentConfig/Equipment")]
    public class EquipmentData: ScriptableObject
    {
        /// <summary>
        /// 装备类型
        /// </summary>
        public EquipmentType equipmentType;
        
        /// <summary>
        /// 装备名称
        /// </summary>
        public string equipmentName;
        
        /// <summary>
        /// 模型Addressable路径
        /// </summary>
        public string modelPath;

    }
}