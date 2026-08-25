using Game.Battle.Ability;
using Game.Battle.Skill.Common;
using Game.Battle.Weapon;
using Game.Character.Enemy.Config;
using Game.Character.Equipment;
using GameMain2.Framework.Manager;
using UnityEngine;

namespace Game.Character.Enemy.Components
{
    public sealed class EnemyCombatComponent : MonoBehaviour
    {
        private const string MissingAbilitySystemError =
            "EnemyCombatComponent 缺少同一 GameObject 上的 CombatAbilitySystem，组件已禁用。";
        private const string MissingWeaponHandlerError =
            "EnemyCombatComponent 缺少同一 GameObject 上的 WeaponHandler，组件已禁用。";
        private const string MissingHitDetectorError =
            "EnemyCombatComponent 缺少可用的敌人武器命中检测器，组件已禁用。";

        [SerializeField] private float combatEnterRange = 4f;
        [SerializeField] private CombatAbilitySystem abilitySystem;
        [SerializeField] private WeaponHandler weaponHandler;
        private bool canInterruptAttack;
        private float chaseRange = 6f;
        private bool configurationErrorLogged;
        private int pendingDefenseHitCount;
        private bool comboAdvanceRequested;
        private int m_executionLockCount;
        private WeaponHitDetector[] weaponHitDetectors = new WeaponHitDetector[0];
        public bool IsActing { get; private set; }
        public bool IsDefending { get; private set; }
        public bool CanInterruptAttack => canInterruptAttack;
        public bool IsExecutionLocked => m_executionLockCount > 0;
        public float CombatEnterRange => combatEnterRange;
        public float ChaseRange => chaseRange;

        /// <summary>唤醒时绑定统一能力系统和武器管线。</summary>
        private void Awake()
        {
            ResolveRuntimeReferences();
        }

        /// <summary>从敌人定义加载战斗进入范围、追击范围和动作中断规则。</summary>
        public void ApplyConfig(EnemyCombatConfig config)
        {
            combatEnterRange = config.combatEnterRange;
            chaseRange = config.chaseRange;
            canInterruptAttack = config.canInterruptAttack;
        }

        /// <summary>判断目标是否进入战斗决策范围，进入后才允许攻击、防御和战斗待机分支接管。</summary>
        public bool IsInCombatRange(Transform target)
        {
            if (target == null)
            {
                return false;
            }

            return Vector3.Distance(target.position, transform.position) <= combatEnterRange;
        }

        /// <summary>进入防御状态，防御开始时清空旧格挡命中，后续由行为树按动画生命周期显式结束。</summary>
        public void StartDefense()
        {
            if (IsExecutionLocked)
            {
                return;
            }

            if (IsActing)
            {
                InterruptAction();
            }

            pendingDefenseHitCount = 0;
            IsDefending = true;
            if (abilitySystem == null)
            {
                TryGetComponent(out abilitySystem);
            }

            if (abilitySystem != null)
            {
                abilitySystem.AddTag(CombatTag.Defending);
            }
        }

        /// <summary>结束防御状态，清空未消费格挡命中，并移除防御期间写入的战斗标签。</summary>
        public void StopDefense()
        {
            IsDefending = false;
            pendingDefenseHitCount = 0;
            if (abilitySystem == null)
            {
                TryGetComponent(out abilitySystem);
            }

            if (abilitySystem != null)
            {
                abilitySystem.RemoveTag(CombatTag.Defending);
            }
        }

        /// <summary>进入处决锁定，关闭攻击、防御和武器命中体。</summary>
        public void PushExecutionLock()
        {
            m_executionLockCount++;
            StopDefense();
            InterruptAction();
        }

        /// <summary>释放处决锁定，允许行为树重新请求战斗动作。</summary>
        public void PopExecutionLock()
        {
            m_executionLockCount--;
            if (m_executionLockCount < 0)
            {
                Debug.LogError("敌人战斗处决锁释放次数超过获取次数。", this);
                m_executionLockCount = 0;
            }
        }

        /// <summary>记录一次防御格挡命中，由防御行为节点逐次消费并驱动受击动画。</summary>
        public void RequestDefenseHitReaction()
        {
            if (!IsDefending)
            {
                return;
            }

            pendingDefenseHitCount++;
        }

        /// <summary>消费一次待处理防御格挡命中，保证每次格挡只触发一次防御受击动画。</summary>
        public bool ConsumeDefenseHitReaction()
        {
            if (pendingDefenseHitCount <= 0)
            {
                return false;
            }

            pendingDefenseHitCount--;
            return true;
        }

        /// <summary>尝试启动普通攻击技能，并记录动作状态。</summary>
        public bool TryStartAttack(int skillId)
        {
            return TryStartAttack(skillId, out _);
        }

        /// <summary>尝试启动普通攻击技能，并返回本次成功启动的技能配置。</summary>
        public bool TryStartAttack(int skillId, out SkillConfig config)
        {
            return TryCast(skillId, out config);
        }

        /// <summary>尝试启动独立技能，并记录动作状态。</summary>
        public bool TryStartSkill(int skillId)
        {
            return TryStartSkill(skillId, out _);
        }

        /// <summary>尝试启动独立技能，并返回本次成功启动的技能配置。</summary>
        public bool TryStartSkill(int skillId, out SkillConfig config)
        {
            return TryCast(skillId, out config);
        }

        /// <summary>结束当前动作并取消统一能力系统中的当前技能。</summary>
        public void EndAction()
        {
            IsActing = false;
            if (ResolveRuntimeReferences())
            {
                abilitySystem.CancelActiveAbility();
            }
        }

        /// <summary>中断当前攻击，先关闭武器命中窗口再取消当前技能。</summary>
        public void InterruptAction()
        {
            DisableWeaponHit();
            EndAction();
        }

        /// <summary>仅在当前配置允许时中断攻击，供普通动画切换使用。</summary>
        public bool TryInterruptAction()
        {
            if (IsActing && !canInterruptAttack)
            {
                return false;
            }

            InterruptAction();
            return true;
        }

        /// <summary>打开敌人全部武器命中体，并开始当前技能命中窗口。</summary>
        public void EnableWeaponHit()
        {
            if (!ResolveRuntimeReferences())
            {
                return;
            }

            if (weaponHitDetectors.Length == 0)
            {
                DisableWithError(MissingHitDetectorError);
                return;
            }

            abilitySystem.BeginHitWindow();
            for (int i = 0; i < weaponHitDetectors.Length; i++)
            {
                weaponHitDetectors[i].ClearHitList();
                weaponHitDetectors[i].EnableCollider(true);
            }
        }

        /// <summary>关闭敌人全部武器命中体，并结束当前技能命中窗口。</summary>
        public void DisableWeaponHit()
        {
            if (ResolveRuntimeReferences())
            {
                for (int i = 0; i < weaponHitDetectors.Length; i++)
                {
                    weaponHitDetectors[i].EnableCollider(false);
                }

                abilitySystem.EndHitWindow();
            }
        }

        /// <summary>由动画事件直接调用，打开一次提前衔接下一段组合攻击的窗口。</summary>
        public void OpenComboWindow()
        {
            comboAdvanceRequested = true;
        }

        /// <summary>清理动画事件留下的提前连招请求，生成新攻击意图前调用以避免旧标记串到下一次攻击。</summary>
        public void ClearComboAdvanceRequest()
        {
            comboAdvanceRequested = false;
        }

        /// <summary>消费提前连招请求，保证一次动画事件只驱动一次组合推进。</summary>
        public bool ConsumeComboAdvanceRequest()
        {
            if (!comboAdvanceRequested)
            {
                return false;
            }

            comboAdvanceRequested = false;
            return true;
        }

        /// <summary>读取敌人技能配置，并通过统一能力系统尝试激活。</summary>
        private bool TryCast(int skillId, out SkillConfig config)
        {
            config = null;
            if (IsExecutionLocked)
            {
                return false;
            }

            if (IsDefending)
            {
                return false;
            }

            if (!ResolveRuntimeReferences())
            {
                return false;
            }

            config = ConfigManager.Instance.GetSkillConfig(skillId);
            if (config == null
                || abilitySystem.TryActivate(config) != AbilityActivationResult.Success)
            {
                config = null;
                return false;
            }

            IsActing = true;
            return true;
        }

        /// <summary>查找现有战斗依赖并刷新敌人全部武器命中检测器。</summary>
        private bool ResolveRuntimeReferences()
        {
            if (abilitySystem == null)
            {
                TryGetComponent(out abilitySystem);
            }

            if (weaponHandler == null)
            {
                TryGetComponent(out weaponHandler);
            }

            if (abilitySystem == null)
            {
                DisableWithError(MissingAbilitySystemError);
                return false;
            }

            if (weaponHandler == null)
            {
                DisableWithError(MissingWeaponHandlerError);
                return false;
            }

            if (weaponHandler.GetActiveHitDetector() == null)
            {
                RefreshWeaponHitDetectors();
                if (weaponHitDetectors.Length > 0)
                {
                    weaponHandler.SetActiveHitDetector(weaponHitDetectors[0]);
                }
            }
            else
            {
                RefreshWeaponHitDetectors();
            }

            return true;
        }

        /// <summary>刷新敌人子级全部武器命中检测器，并绑定当前能力系统作为命中来源。</summary>
        private void RefreshWeaponHitDetectors()
        {
            weaponHitDetectors = GetComponentsInChildren<WeaponHitDetector>(true);
            for (int i = 0; i < weaponHitDetectors.Length; i++)
            {
                weaponHitDetectors[i].BindSource(abilitySystem);
                weaponHitDetectors[i].EnableCollider(false);
                weaponHitDetectors[i].ClearHitList();
            }
        }

        /// <summary>记录一次依赖配置错误并禁用组件。</summary>
        private void DisableWithError(string message)
        {
            if (!configurationErrorLogged)
            {
                Debug.LogError(message, this);
                configurationErrorLogged = true;
            }

            enabled = false;
        }
    }
}
