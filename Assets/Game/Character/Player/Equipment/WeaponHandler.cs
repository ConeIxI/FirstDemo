using Game.Battle.Ability;
using Game.Battle.Weapon;
using UnityEngine;

namespace Game.Character.Equipment
{
    public class WeaponHandler : MonoBehaviour
    {
        private const string MissingAbilitySystemError =
            "WeaponHandler 缺少同一 GameObject 上的 CombatAbilitySystem，组件已禁用。";

        private CombatAbilitySystem m_abilitySystem;
        private WeaponHitDetector m_weaponHitDetector;

        /// <summary>解析同对象的战斗能力系统，缺失时明确报错并禁用组件。</summary>
        private void Awake()
        {
            EnsureAbilitySystemBound();
        }

        /// <summary>返回当前装备并参与命中检测的武器检测器。</summary>
        public WeaponHitDetector GetActiveHitDetector()
        {
            return m_weaponHitDetector;
        }

        /// <summary>切换当前武器检测器，并把统一能力系统绑定为命中来源。</summary>
        public void SetActiveHitDetector(WeaponHitDetector hitDetector)
        {
            if (hitDetector != null && !EnsureAbilitySystemBound())
            {
                return;
            }

            if (m_weaponHitDetector != null && m_weaponHitDetector != hitDetector)
            {
                m_weaponHitDetector.EnableCollider(false);
            }

            m_weaponHitDetector = hitDetector;
            if (m_weaponHitDetector != null)
            {
                m_weaponHitDetector.BindSource(m_abilitySystem);
                m_weaponHitDetector.EnableCollider(false);
                m_weaponHitDetector.ClearHitList();
            }
        }

        /// <summary>打开本段攻击的能力与武器命中窗口。</summary>
        public void OpenHitWindow()
        {
            m_abilitySystem.BeginHitWindow();
            m_weaponHitDetector.ClearHitList();
            m_weaponHitDetector.EnableCollider(true);
        }

        /// <summary>关闭武器碰撞体，并结束能力系统的当前命中窗口。</summary>
        public void CloseHitWindow()
        {
            if (m_weaponHitDetector != null)
            {
                m_weaponHitDetector.EnableCollider(false);
            }

            if (m_abilitySystem != null)
            {
                m_abilitySystem.EndHitWindow();
            }
        }

        /// <summary>卸下当前武器并关闭其命中检测。</summary>
        public void RemoveWeapon()
        {
            SetActiveHitDetector(null);
        }

        /// <summary>从武器对象解析检测器并设为当前命中检测器。</summary>
        public void ApplyWeapon(WeaponData weapon)
        {
            if (weapon == null)
            {
                RemoveWeapon();
                return;
            }

            WeaponHitDetector hitDetector = weapon.GetComponentInChildren<WeaponHitDetector>(true);
            SetActiveHitDetector(hitDetector);
        }

        /// <summary>确保公开武器入口不依赖 Awake 执行顺序，也能取得同对象的能力系统。</summary>
        private bool EnsureAbilitySystemBound()
        {
            if (m_abilitySystem != null)
            {
                return true;
            }

            BindAbilitySystem(GetComponent<CombatAbilitySystem>());
            return m_abilitySystem != null;
        }

        /// <summary>统一绑定能力系统，空依赖按错误配置处理并禁用组件。</summary>
        private void BindAbilitySystem(CombatAbilitySystem abilitySystem)
        {
            if (abilitySystem == null)
            {
                Debug.LogError(MissingAbilitySystemError, this);
                enabled = false;
                return;
            }

            m_abilitySystem = abilitySystem;
        }

    }
}
