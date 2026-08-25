using System.Collections.Generic;
using Game.Character.Equipment;
using UnityEngine;

namespace GameMain2.Scripts.Character
{
    public class PlayerSkillManager : MonoBehaviour
    {
        private const string MissingPlayerControllerError =
            "PlayerSkillManager 缺少同一 GameObject 上的 PlayerController，组件已禁用。";
        private const string MissingAbilitySystemError =
            "PlayerSkillManager 缺少玩家 CombatAbilitySystem，组件已禁用。";

        private readonly HashSet<int> m_availableSkillIds = new HashSet<int>();
        private PlayerController m_playerController;

        /// <summary>解析玩家控制器与能力系统，错误配置明确禁用组件。</summary>
        private void Awake()
        {
            m_playerController = GetComponent<PlayerController>();
            if (m_playerController == null)
            {
                Debug.LogError(MissingPlayerControllerError, this);
                enabled = false;
                return;
            }

            if (m_playerController.AbilitySystem == null)
            {
                Debug.LogError(MissingAbilitySystemError, this);
                enabled = false;
            }
        }

        /// <summary>根据当前武器同步普通攻击和武器技能 ID。</summary>
        public void LoadSkillsForWeapon(WeaponData weaponData)
        {
            ClearSkills();
            if (weaponData == null)
            {
                return;
            }

            foreach (int skillId in weaponData.EnumerateAllSkillIds())
            {
                m_availableSkillIds.Add(skillId);
            }
        }

        /// <summary>取消当前能力并清空武器提供的全部技能 ID。</summary>
        public void ClearSkills()
        {
            if (m_playerController != null && m_playerController.AbilitySystem != null)
            {
                m_playerController.AbilitySystem.CancelActiveAbility();
            }

            m_availableSkillIds.Clear();
        }

        /// <summary>判断当前武器是否提供指定技能 ID。</summary>
        public bool HasSkill(int skillId)
        {
            return m_availableSkillIds.Contains(skillId);
        }
    }
}
