using System;
using UnityEngine;

namespace Game.Character.Equipment
{
    [Serializable]
    public sealed class PlayerWeaponAppearanceEntry
    {
        [SerializeField] private string m_objectName;
        [SerializeField] private WeaponData m_weaponData;
        [SerializeField] private GameObject m_handObject;
        [SerializeField] private GameObject m_sheathedObject;

        public string ObjectName => m_objectName;
        public WeaponData WeaponData => m_weaponData;
        public GameObject HandObject => m_handObject;
        public GameObject SheathedObject => m_sheathedObject;
        public bool IsComplete => !string.IsNullOrWhiteSpace(m_objectName)
            && m_weaponData != null
            && m_handObject != null
            && m_sheathedObject != null;

        /// <summary>创建测试或编辑器配置使用的共享武器表现项。</summary>
        public PlayerWeaponAppearanceEntry(
            string objectName,
            WeaponData weaponData,
            GameObject handObject,
            GameObject sheathedObject)
        {
            m_objectName = objectName;
            m_weaponData = weaponData;
            m_handObject = handObject;
            m_sheathedObject = sheathedObject;
        }

        /// <summary>隐藏该武器的手持和收纳对象。</summary>
        public void Hide()
        {
            m_handObject.SetActive(false);
            m_sheathedObject.SetActive(false);
        }

        /// <summary>把该武器切到手持表现。</summary>
        public void ShowInHand()
        {
            m_sheathedObject.SetActive(false);
            m_handObject.SetActive(true);
        }

        /// <summary>把该武器切到收纳表现。</summary>
        public void ShowSheathed()
        {
            m_handObject.SetActive(false);
            m_sheathedObject.SetActive(true);
        }
    }
}
