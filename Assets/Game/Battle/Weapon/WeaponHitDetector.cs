using System.Collections.Generic;
using Game.Battle.Ability;
using UnityEngine;

namespace Game.Battle.Weapon
{
    public class WeaponHitDetector : MonoBehaviour
    {
        private const string MissingColliderError = "WeaponHitDetector 缺少 Collider，组件已禁用。";
        private const string MissingRigidbodyError = "WeaponHitDetector 缺少 Rigidbody，组件已禁用。";
        private const string MissingSourceError =
            "WeaponHitDetector 缺少命中来源 CombatAbilitySystem，组件已禁用。";

        protected readonly List<GameObject> m_hitObjects = new List<GameObject>();

        private Collider m_collider;
        private CombatAbilitySystem m_source;

        /// <summary>初始化武器碰撞依赖，配置缺失时明确报错并禁用组件。</summary>
        private void Awake()
        {
            InitializeComponents();
        }

        /// <summary>清空兼容旧攻击链保留的本地命中记录。</summary>
        public void ClearHitList()
        {
            m_hitObjects.Clear();
        }

        /// <summary>把武器持续触发的碰撞转发给通用命中上报入口。</summary>
        private void OnTriggerStay(Collider other)
        {
            ReportCollision(other);
        }

        /// <summary>启用或禁用当前武器碰撞体。</summary>
        public void EnableCollider(bool enable)
        {
            if (!InitializeComponents())
            {
                return;
            }

            m_collider.enabled = enable;
        }

        /// <summary>绑定负责技能窗口和命中结算的来源能力系统。</summary>
        public void BindSource(CombatAbilitySystem source)
        {
            if (source == null)
            {
                Debug.LogError(MissingSourceError, this);
                enabled = false;
                return;
            }

            m_source = source;
        }

        /// <summary>把碰撞体解析为父级战斗目标，并交由来源能力系统统一结算。</summary>
        protected void ReportCollision(Collider other)
        {
            CombatAbilitySystem target = other.GetComponentInParent<CombatAbilitySystem>();
            Vector3 hitPoint = other.ClosestPoint(transform.position);
            m_source.ReportHit(target, hitPoint);
        }

        /// <summary>解析并校验武器碰撞所需组件，缺失时禁用检测器。</summary>
        private bool InitializeComponents()
        {
            if (m_collider == null)
            {
                m_collider = GetComponent<Collider>();
            }

            if (m_collider == null)
            {
                Debug.LogError(MissingColliderError, this);
                enabled = false;
                return false;
            }

            if (GetComponent<Rigidbody>() == null)
            {
                Debug.LogError(MissingRigidbodyError, this);
                enabled = false;
                return false;
            }

            return true;
        }

    }
}
