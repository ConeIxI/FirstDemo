using Game.Battle.Ability;
using Game.Battle.Combat.Config;
using Game.Battle.Skill.Common;
using Game.Battle.Weapon;
using Game.Character.Enemy.AI;
using Game.Character.Enemy.Config;
using Game.Character.Enemy.Core;
using Game.Character.Enemy.Events;
using GameMain2.Framework.Core;
using GameMain2.Scripts.UI;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace Game.Character.Enemy.Components
{
    public sealed class EnemyLifeComponent : MonoBehaviour
    {
        private const string MissingAbilitySystemError =
            "EnemyLifeComponent 缺少同一 GameObject 上的 CombatAbilitySystem，组件已禁用。";

        [SerializeField] private CombatAbilitySystem abilitySystem;
        private EnemyBlackboard blackboard;
        private EnemyPerceptionComponent perception;
        private EnemyCombatComponent combat;
        private EnemyAnimationComponent enemyAnimation;
        private bool rememberTargetOnHit = true;
        private bool allowUnbalanceReaction = true;
        private bool allowDeathReaction = true;
        private bool isCombatEventSubscribed;
        private EnemyDefinition definition;
        private Transform ownerTransform;
        private EnemyDropComponent dropComponent;
        private bool deathHandled;
        private bool bossVictoryPanelShown;
        private const string BossVictoryEnemyId = "GreatSwordBoss";
        private const string ExecutionGetUpAnimationName = "GetUp";

        /// <summary>敌人是否已经完成死亡处理，用于外部停止死亡后的运行时逻辑。</summary>
        public bool IsDead => deathHandled;

        /// <summary>缓存目标记忆和统一能力系统依赖。</summary>
        private void Awake()
        {
            TryGetComponent(out perception);
            TryGetComponent(out combat);
            TryGetComponent(out enemyAnimation);
            TryGetComponent(out dropComponent);
            if (abilitySystem == null)
            {
                TryGetComponent(out abilitySystem);
            }

            if (abilitySystem == null)
            {
                Debug.LogError(MissingAbilitySystemError, this);
                enabled = false;
            }
        }

        /// <summary>启用时订阅统一战斗结算事件。</summary>
        private void OnEnable()
        {
            SubscribeCombatEvent();
        }

        /// <summary>禁用时解除统一战斗结算事件订阅。</summary>
        private void OnDisable()
        {
            UnsubscribeCombatEvent();
        }

        /// <summary>绑定敌人黑板，生命反应会把事实写入该黑板。</summary>
        public void Bind(EnemyBlackboard value)
        {
            blackboard = value;
        }

        /// <summary>从敌人定义加载受击、失衡和死亡反应规则。</summary>
        public void ApplyConfig(EnemyLifeConfig config)
        {
            rememberTargetOnHit = config.rememberTargetOnHit;
            allowUnbalanceReaction = config.allowUnbalanceReaction;
            allowDeathReaction = config.allowDeathReaction;
        }

        /// <summary>绑定死亡事件所需的敌人定义和实例 Transform。</summary>
        public void BindDeathContext(EnemyDefinition value, Transform owner)
        {
            definition = value;
            ownerTransform = owner;
        }

        /// <summary>把命中来源合并为唯一追踪目标，供敌人后续追击。</summary>
        public void HandleAttacked(Transform target)
        {
            if (blackboard == null)
            {
                return;
            }

            CompleteCombatStanceIfAttackedDuringEnterCombat();
            if (!rememberTargetOnHit)
            {
                return;
            }

            if (target == null)
            {
                return;
            }

            bool wasAlertActive = blackboard.HasAlertMemory || blackboard.IsAlertExitPending;
            bool isInCombatRange = IsTargetInCombatRange(target);
            blackboard.RecordPlayerAttack(
                target,
                wasAlertActive,
                isInCombatRange,
                GetCombatMemoryDuration(),
                GetAlertMemoryDuration());
            if (blackboard.HasCombatTarget)
            {
                SetCombatState(true);
            }

            blackboard.SetTargetVisible(perception != null && perception.CanSee(target));
        }

        /// <summary>同步生命事件判定出的战斗状态到黑板和 Animator，保证受击 BlendTree 立即读取正确状态。</summary>
        private void SetCombatState(bool isInCombat)
        {
            blackboard.SetCombatState(isInCombat);
            EnemyAnimationComponent resolvedAnimation = GetAnimationComponent();
            if (resolvedAnimation != null)
            {
                resolvedAnimation.SetCombatStateParameter(isInCombat);
            }
        }

        /// <summary>判断攻击者是否处于战斗范围，组件未完成初始化时回退到定义配置。</summary>
        private bool IsTargetInCombatRange(Transform target)
        {
            if (combat != null && combat.IsInCombatRange(target))
            {
                return true;
            }

            if (definition == null || definition.CombatConfig == null || target == null)
            {
                return false;
            }

            Transform origin = ownerTransform != null ? ownerTransform : transform;
            return Vector3.Distance(origin.position, target.position) <= definition.CombatConfig.combatEnterRange;
        }
        /// <summary>读取战斗记忆时长，定义未绑定时使用默认值保持受击入口可用。</summary>
        private float GetCombatMemoryDuration()
        {
            return definition != null && definition.CombatConfig != null
                ? definition.CombatConfig.combatMemoryDuration
                : 4f;
        }

        /// <summary>读取警戒记忆时长，定义未绑定时使用默认值保持受击入口可用。</summary>
        private float GetAlertMemoryDuration()
        {
            return definition != null && definition.PerceptionConfig != null
                ? definition.PerceptionConfig.alertMemoryDuration
                : 4f;
        }

        /// <summary>拔剑动画被受击打断时，直接确认已拔剑并切到手持武器，避免恢复后重复播放拔剑。</summary>
        private void CompleteCombatStanceIfAttackedDuringEnterCombat()
        {
            if (blackboard.HasCombatStance || definition == null || definition.AnimationConfig == null)
            {
                return;
            }

            EnemyAnimationComponent resolvedAnimation = GetAnimationComponent();
            string enterCombatAnimation = definition.AnimationConfig.enterCombatAnimation;
            if (resolvedAnimation == null || !resolvedAnimation.IsPlaying(enterCombatAnimation, out _))
            {
                return;
            }

            blackboard.SetCombatStance(true);
            EnemyAgent agent = GetComponent<EnemyAgent>();
            if (agent != null)
            {
                agent.ShowAllWeaponsInHand();
            }
        }

        /// <summary>处理受击反应，记录唯一目标和待播放动画。</summary>
        public void HandleHitReaction(
            string animationName,
            SkillHitWeight hitWeight,
            EnemyHitDirection hitDirection,
            Transform target)
        {
            if (blackboard == null)
            {
                return;
            }

            HandleAttacked(target);
            if (blackboard.IsDead
                || blackboard.IsUnbalanced
                || blackboard.HasGetUpReaction
                || blackboard.IsGetUpReactionInProgress
                || blackboard.CurrentIntent == EnemyCombatIntent.Retreat)
            {
                return;
            }

            blackboard.SetHitReaction(animationName, hitWeight, hitDirection);
        }

        /// <summary>处理失衡反应，记录唯一目标并标记失衡。</summary>
        public void HandleUnbalance(Transform target)
        {
            if (!allowUnbalanceReaction || blackboard == null)
            {
                return;
            }

            HandleAttacked(target);
            // 格挡命中也可能直接打空稳定值；进入失衡前必须清掉防御标签，避免后续一直被判定为防御中。
            if (combat != null)
            {
                combat.StopDefense();
            }

            blackboard.ClearHitReactionState();
            blackboard.SetUnbalanced(true);
        }

        /// <summary>处决结束时让存活敌人退出失衡状态并请求播放不可被受击表现打断的起身动画。</summary>
        public void CompleteExecutionRecovery()
        {
            if (blackboard == null || blackboard.IsDead || deathHandled)
            {
                return;
            }

            EnemyAttributeComponent attribute = GetComponent<EnemyAttributeComponent>();
            if (attribute != null && attribute.IsDead)
            {
                return;
            }

            if (attribute != null && attribute.IsUnbalanced)
            {
                attribute.RestoreStability(attribute.MaxStability);
            }

            blackboard.SetUnbalanced(false);
            blackboard.SetGetUpReaction(ExecutionGetUpAnimationName);
        }

        /// <summary>处理死亡反应，首次死亡时写入黑板、触发掉落并发布死亡事件。</summary>
        public void HandleDeath()
        {
            HandleDeath(SkillHitWeight.Light);
        }

        /// <summary>处理死亡反应，并记录造成死亡这一击的轻重击类型供死亡动画 BlendTree 使用。</summary>
        public void HandleDeath(SkillHitWeight hitWeight)
        {
            if (deathHandled)
            {
                return;
            }

            deathHandled = true;
            if (allowDeathReaction && blackboard != null)
            {
                blackboard.SetDeathReactionParameters(blackboard.IsInCombatState, hitWeight);
                blackboard.SetDead(true);
            }

            Transform eventTransform = ownerTransform != null ? ownerTransform : transform;
            Vector3 deathPosition = eventTransform.position;
            DisableGameplayCollisionOnDeath();
            if (dropComponent == null)
            {
                TryGetComponent(out dropComponent);
            }

            if (dropComponent != null)
            {
                dropComponent.SpawnDrops(deathPosition);
            }

            EventCenter.Instance.Fire(
                this,
                new EnemyDeadEventArgs(definition, eventTransform, deathPosition));
        }

        /// <summary>死亡动画播放完毕后尝试显示 Boss 击杀胜利面板。</summary>
        public void CompleteDeathAnimation()
        {
            TryShowBossVictoryPanel();
        }

        /// <summary>Boss 场景击杀指定 Boss 后打开胜利面板，避免普通敌人死亡或重复通知误触发。</summary>
        private void TryShowBossVictoryPanel()
        {
            if (bossVictoryPanelShown)
            {
                return;
            }

            if (SceneManager.GetActiveScene().name != SceneNames.BossScene)
            {
                return;
            }

            if (definition == null || definition.EnemyId != BossVictoryEnemyId)
            {
                return;
            }

            bossVictoryPanelShown = true;
            UIManager.Instance.ShowVictoryPanel();
        }

        /// <summary>死亡后关闭敌人的玩法碰撞、导航和武器命中体，保留模型与死亡动画显示。</summary>
        private void DisableGameplayCollisionOnDeath()
        {
            CharacterController[] characterControllers = GetComponentsInChildren<CharacterController>(true);
            for (int i = 0; i < characterControllers.Length; i++)
            {
                characterControllers[i].enabled = false;
            }

            NavMeshAgent[] agents = GetComponentsInChildren<NavMeshAgent>(true);
            for (int i = 0; i < agents.Length; i++)
            {
                agents[i].enabled = false;
            }

            WeaponHitDetector[] hitDetectors = GetComponentsInChildren<WeaponHitDetector>(true);
            for (int i = 0; i < hitDetectors.Length; i++)
            {
                hitDetectors[i].EnableCollider(false);
                hitDetectors[i].enabled = false;
            }

            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                colliders[i].enabled = false;
            }
        }

        /// <summary>按敌人作为攻击来源或受击目标分别消费统一战斗事件。</summary>
        private void OnCombatEvent(object sender, EventArgsBase eventArgs)
        {
            CombatEvent combatEvent = eventArgs as CombatEvent;
            if (combatEvent == null)
            {
                return;
            }

            if (combatEvent.Source == abilitySystem)
            {
                HandleSourceCombatEvent(combatEvent);
                return;
            }

            if (combatEvent.Target != abilitySystem)
            {
                return;
            }

            if (combatEvent.TargetDead)
            {
                SkillHitWeight hitWeight = combatEvent.Skill != null
                    ? combatEvent.Skill.HitWeight
                    : SkillHitWeight.Heavy;
                HandleDeath(hitWeight);
                return;
            }

            Transform attacker = combatEvent.Source != null ? combatEvent.Source.transform : null;
            if (combatEvent.TargetUnbalanced)
            {
                HandleUnbalance(attacker);
                return;
            }

            if (combatEvent.Type == CombatEventType.Blocked)
            {
                HandleBlocked(attacker);
                return;
            }

            if (combatEvent.TargetShouldReact)
            {
                SkillConfig skill = combatEvent.Skill;
                HandleHitReaction(
                    skill.hitConfig.hitReactionName,
                    skill.HitWeight,
                    ResolveHitDirection(combatEvent, attacker),
                    attacker);
            }
        }

        /// <summary>处理敌人攻击被目标弹反后的反馈，技能配置决定普通弹反是否中断当前招式。</summary>
        private void HandleSourceCombatEvent(CombatEvent combatEvent)
        {
            if (combatEvent.Type != CombatEventType.Parried)
            {
                return;
            }

            Transform parryTarget = combatEvent.Target != null ? combatEvent.Target.transform : null;
            HandleAttacked(parryTarget);

            if (combatEvent.SourceUnbalanced)
            {
                if (combat != null)
                {
                    combat.InterruptAction();
                }

                HandleUnbalance(parryTarget);
                return;
            }

            if (combatEvent.Skill.hitConfig.canBeParried)
            {
                if (combat != null)
                {
                    combat.InterruptAction();
                }

                QueueDefenseBreakOnly();
            }
        }

        /// <summary>记录普通弹反中断后的破防动画请求，由中断执行器等待动画完整播放。</summary>
        private void QueueDefenseBreakOnly()
        {
            if (blackboard == null)
            {
                return;
            }

            blackboard.SetDefenseBreakReaction(GetDefenseBreakAnimation());
        }

        /// <summary>懒加载敌人动画组件，允许运行时按任意组件添加顺序初始化。</summary>
        private EnemyAnimationComponent GetAnimationComponent()
        {
            if (enemyAnimation == null)
            {
                TryGetComponent(out enemyAnimation);
            }

            return enemyAnimation;
        }

        /// <summary>读取破防入口动画名，定义未绑定时使用约定状态名。</summary>
        private string GetDefenseBreakAnimation()
        {
            return definition != null && definition.AnimationConfig != null
                ? definition.AnimationConfig.defenseBreakAnimation
                : "DefenseBreak";
        }

        /// <summary>根据攻击来源相对敌人的水平朝向解析受击 BlendTree 方向值。</summary>
        private EnemyHitDirection ResolveHitDirection(CombatEvent combatEvent, Transform attacker)
        {
            Vector3 sourceDirection = ResolveHitSourceDirection(combatEvent, attacker);
            sourceDirection.y = 0f;
            if (sourceDirection.sqrMagnitude <= 0.0001f)
            {
                return EnemyHitDirection.Front;
            }

            Vector3 localDirection = transform.InverseTransformDirection(sourceDirection.normalized);
            float absX = Mathf.Abs(localDirection.x);
            float absZ = Mathf.Abs(localDirection.z);
            if (absZ >= absX)
            {
                return localDirection.z >= 0f
                    ? EnemyHitDirection.Front
                    : EnemyHitDirection.Back;
            }

            return localDirection.x >= 0f
                ? EnemyHitDirection.Right
                : EnemyHitDirection.Left;
        }

        /// <summary>优先使用攻击者位置，缺失时回退到命中点或命中方向推导攻击来源。</summary>
        private Vector3 ResolveHitSourceDirection(CombatEvent combatEvent, Transform attacker)
        {
            if (attacker != null)
            {
                return attacker.position - transform.position;
            }

            Vector3 hitPointOffset = combatEvent.HitPoint - transform.position;
            if (hitPointOffset.sqrMagnitude > 0.0001f)
            {
                return hitPointOffset;
            }

            return -combatEvent.HitDirection;
        }

        /// <summary>处理格挡命中事件，记录攻击者并请求防御节点播放防御受击动画。</summary>
        private void HandleBlocked(Transform attacker)
        {
            HandleAttacked(attacker);
            if (combat != null)
            {
                combat.RequestDefenseHitReaction();
            }
        }

        /// <summary>在具备能力系统时注册一次战斗事件处理器。</summary>
        private void SubscribeCombatEvent()
        {
            if (isCombatEventSubscribed || abilitySystem == null)
            {
                return;
            }

            EventCenter.Instance.Subscribe(CombatEvent.EventId, OnCombatEvent);
            isCombatEventSubscribed = true;
        }

        /// <summary>解除当前战斗事件处理器，避免禁用后继续写黑板。</summary>
        private void UnsubscribeCombatEvent()
        {
            if (!isCombatEventSubscribed)
            {
                return;
            }

            EventCenter.TryUnSubscribe(CombatEvent.EventId, OnCombatEvent);
            isCombatEventSubscribed = false;
        }

    }
}
