using Game.Battle.Ability;
using Game.Battle.Skill.Common;
using Game.Character.Enemy.AI.BehaviorTree;
using Game.Character.Enemy.Components;
using Game.Character.Enemy.AI.Combat;
using Game.Character.Enemy.Config;
using Game.Character.Enemy.Core;
using Game.Character.Enemy.Events;
using GameMain2.Framework.Core;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Game.EventArgs;
using GameMain2.Framework.Manager;
using UnityEngine;

namespace Game.Character.Enemy.AI
{
    public sealed class AIController : MonoBehaviour
    {
        [SerializeField] private BehaviorTreeRunner behaviorTreeRunner;

        private EnemyStateContext context;
        private EnemyDefinition definition;
        private EnemyCombatDecisionController combatDecision;
        private EnemyBlackboard observedBlackboard;
        private int m_executionLockCount;
        public EnemyBlackboard Blackboard { get; private set; } = new EnemyBlackboard();
        public EnemyStateContext Context => context;
        public EnemyDefinition Definition => definition;
        public EnemyCombatDecisionController CombatDecision => combatDecision;
        public EnemyDecisionProfile DecisionProfile => definition != null ? definition.DecisionProfile : null;
        public bool IsExecutionLocked => m_executionLockCount > 0;
        public Vector3 StartupHomePosition { get; private set; }
        public Quaternion StartupHomeRotation { get; private set; } = Quaternion.identity;
        public Vector3 NormalOriginPosition { get; private set; }
        public Quaternion NormalOriginRotation { get; private set; } = Quaternion.identity;
        public float PatrolWaitDuration => definition != null && definition.MovementConfig != null
            ? definition.MovementConfig.patrolWaitDuration
            : 2f;
        public float SearchObservationDuration => definition != null && definition.PerceptionConfig != null
            ? definition.PerceptionConfig.searchObservationDuration
            : 1f;

        // 返回场景敌人实例配置的巡逻路线。
        public Transform[] PatrolRoute
        {
            get
            {
                EnemyAgent agent = context != null ? context.Agent as EnemyAgent : null;
                return agent != null ? agent.PatrolRoute : new Transform[0];
            }
        }

        // 判断当前敌人是否拥有可用巡逻路线。
        public bool HasPatrolRoute => PatrolRoute.Length > 0;

        /// <summary>进入处决锁定，停止普通感知和战斗决策刷新。</summary>
        public void PushExecutionLock()
        {
            m_executionLockCount++;
        }

        /// <summary>释放处决锁定，允许敌人恢复失衡剩余时间或死亡分支。</summary>
        public void PopExecutionLock()
        {
            m_executionLockCount--;
            if (m_executionLockCount < 0)
            {
                Debug.LogError("敌人处决锁释放次数超过获取次数。", this);
                m_executionLockCount = 0;
            }
        }

        /// <summary>启用时订阅玩家输入和黑板目标变更，确保运行期目标锁定可被外部感知。</summary>
        private void OnEnable()
        {
            BindBlackboardTargetChangedEvent(Blackboard);
            if (Application.isPlaying)
            {
                EventCenter.Instance.Subscribe(PlayerAttackInputEventArgs.EventId, OnPlayerAttackInput);
            }
        }

        /// <summary>禁用时释放当前战斗目标并解除订阅，避免玩家残留被敌人锁定的状态。</summary>
        private void OnDisable()
        {
            if (Application.isPlaying)
            {
                ReleaseCombatTargetForDisable();
                EventCenter.TryUnSubscribe(PlayerAttackInputEventArgs.EventId, OnPlayerAttackInput);
            }

            UnbindBlackboardTargetChangedEvent();
        }

        /// <summary>启动敌人 AI，绑定定义、组件上下文、行为树资源和黑板目标变更转发。</summary>
        public void StartAI(EnemyAgent agent, EnemyDefinition enemyDefinition)
        {
            BindBlackboardTargetChangedEvent(Blackboard);
            definition = enemyDefinition;
            combatDecision = CreateCombatDecision(definition);
            RecordNormalHome(agent);
            if (behaviorTreeRunner == null)
            {
                TryGetComponent(out behaviorTreeRunner);
            }

            EnemyMovementComponent movement = GetComponent<EnemyMovementComponent>();
            EnemyPerceptionComponent perception = GetComponent<EnemyPerceptionComponent>();
            EnemyAnimationComponent animation = GetComponent<EnemyAnimationComponent>();
            EnemyCombatComponent combat = GetComponent<EnemyCombatComponent>();
            EnemyLifeComponent life = GetComponent<EnemyLifeComponent>();
            EnemyMemoryComponent memory = GetComponent<EnemyMemoryComponent>();
            EnemyDropComponent drop = GetComponent<EnemyDropComponent>();
            EnemyAttributeComponent attribute = GetComponent<EnemyAttributeComponent>();

            if (definition != null)
            {
                if (movement != null)
                {
                    movement.ApplyConfig(definition.MovementConfig);
                }

                if (perception != null)
                {
                    perception.ApplyConfig(definition.PerceptionConfig);
                }

                if (animation != null)
                {
                    animation.ApplyConfig(definition.AnimationConfig);
                }

                if (combat != null)
                {
                    combat.ApplyConfig(definition.CombatConfig);
                }

                if (life != null)
                {
                    life.ApplyConfig(definition.LifeConfig);
                    life.BindDeathContext(definition, transform);
                }

                if (drop != null)
                {
                    drop.ApplyConfig(definition.DropItems);
                }
            }

            if (perception != null)
            {
                perception.Bind(Blackboard);
            }

            if (life != null)
            {
                life.Bind(Blackboard);
            }

            if (memory != null)
            {
                memory.Bind(Blackboard);
            }

            if (attribute != null)
            {
                attribute.LoadFromDefinition(definition);
            }

            context = new EnemyStateContext(agent, Blackboard, movement, perception, animation, combat, life, attribute, combatDecision);
            SyncCombatDecisionFacts();
            if (behaviorTreeRunner != null && definition != null)
            {
                behaviorTreeRunner.SetTree(definition.BehaviorTreeAsset);
            }
        }

        /// <summary>绑定黑板目标变更事件，同一黑板不会重复绑定。</summary>
        private void BindBlackboardTargetChangedEvent(EnemyBlackboard blackboard)
        {
            if (observedBlackboard == blackboard)
            {
                return;
            }

            UnbindBlackboardTargetChangedEvent();
            observedBlackboard = blackboard;
            if (observedBlackboard != null)
            {
                observedBlackboard.CombatTargetChanged += OnBlackboardCombatTargetChanged;
            }
        }

        /// <summary>解除当前黑板目标变更事件，避免禁用或替换黑板后继续转发旧事件。</summary>
        private void UnbindBlackboardTargetChangedEvent()
        {
            if (observedBlackboard == null)
            {
                return;
            }

            observedBlackboard.CombatTargetChanged -= OnBlackboardCombatTargetChanged;
            observedBlackboard = null;
        }

        /// <summary>将黑板的战斗目标变更转发为全局事件，附带当前敌人的 Transform。</summary>
        private void OnBlackboardCombatTargetChanged(object sender, EnemyCombatTargetChangedEventArgs eventArgs)
        {
            if (eventArgs == null || !EventCenter.TryGetInstance(out EventCenter eventCenter))
            {
                return;
            }

            eventCenter.Fire(this, eventArgs.WithEnemyTransform(transform));
        }

        /// <summary>敌人禁用时清空战斗目标，通过黑板变更事件通知外部解除锁定。</summary>
        private void ReleaseCombatTargetForDisable()
        {
            if (Blackboard == null || !Blackboard.HasCombatTarget)
            {
                return;
            }

            Blackboard.ForgetTarget();
        }

        // 更新感知黑板事实后推进行为树，由叶子节点直接执行敌人行为。
        public void TickAI(float deltaTime)
        {
            if (IsExecutionLocked)
            {
                if (behaviorTreeRunner != null)
                {
                    behaviorTreeRunner.Tick(deltaTime);
                }

                return;
            }

            if (IsSelfDead())
            {
                ReleaseCombatTargetForDeadSelf();
                if (Blackboard.IsDead && behaviorTreeRunner != null)
                {
                    behaviorTreeRunner.Tick(deltaTime);
                }

                return;
            }

            if (context != null && context.Perception != null)
            {
                Transform visibleTarget = context.Perception.ScanVisibleTarget();
                if (visibleTarget != null)
                {
                    if (Blackboard.CombatTarget != null
                        && Blackboard.CombatTarget != visibleTarget
                        && combatDecision != null)
                    {
                        combatDecision.ResetAttackSelectionHistory();
                    }

                    bool isInCombatRange = context.Combat != null
                        && context.Combat.IsInCombatRange(visibleTarget);
                    Blackboard.ObserveTarget(
                        visibleTarget,
                        isInCombatRange,
                        GetCombatMemoryDuration(),
                        GetAlertMemoryDuration());
                    Blackboard.SetTargetVisible(true);
                }
                else
                {
                    Blackboard.SetTargetVisible(false);
                    if (!Blackboard.HasCombatTarget)
                    {
                        Transform soundTarget = context.Perception.ScanSoundTarget();
                        if (soundTarget != null)
                        {
                            Blackboard.ObserveTarget(
                                soundTarget,
                                false,
                                GetCombatMemoryDuration(),
                                GetAlertMemoryDuration());
                        }
                    }
                }
            }

            ReleaseDeadCombatTarget();
            Blackboard.TickMemories(deltaTime);
            RefreshDecisionFacts();

            if (behaviorTreeRunner != null)
            {
                behaviorTreeRunner.Tick(deltaTime);
            }
        }

        /// <summary>判断敌人自身是否已经死亡，死亡后不再执行感知和目标写入。</summary>
        private bool IsSelfDead()
        {
            if (Blackboard.IsDead)
            {
                return true;
            }

            EnemyAttributeComponent attribute = context != null ? context.Attribute : null;
            return attribute != null && attribute.IsDead;
        }

        /// <summary>死亡敌人释放当前战斗目标，避免继续通知玩家进入战斗。</summary>
        private void ReleaseCombatTargetForDeadSelf()
        {
            if (!Blackboard.HasCombatTarget)
            {
                return;
            }

            if (combatDecision != null)
            {
                combatDecision.ResetAttack();
            }

            Blackboard.ForgetTarget();
        }

        /// <summary>玩家死亡后按目标丢失处理，保留最后位置交给警戒层搜索。</summary>
        private void ReleaseDeadCombatTarget()
        {
            Transform deadTarget = Blackboard.CombatTarget;
            if (!IsDeadCombatTarget(deadTarget))
            {
                return;
            }

            if (context != null && context.Movement != null)
            {
                context.Movement.Stop();
            }

            if (context != null && context.Combat != null && context.Combat.IsActing)
            {
                context.Combat.InterruptAction();
            }

            combatDecision.ResetAttack();
            Blackboard.ClearAttackIntent();
            Blackboard.ClearCombatTarget(deadTarget.position, GetAlertMemoryDuration());
            Blackboard.SetCombatIntent(EnemyCombatIntent.None);
            SyncCombatDecisionFacts();
        }

        /// <summary>判断当前锁定目标是否已经死亡，兼容属性死亡和死亡标签两种来源。</summary>
        private static bool IsDeadCombatTarget(Transform target)
        {
            if (target == null)
            {
                return false;
            }

            CombatAbilitySystem abilitySystem = target.GetComponentInParent<CombatAbilitySystem>();
            if (abilitySystem == null)
            {
                return false;
            }

            ICombatAttributes attributes = abilitySystem.Attributes;
            return abilitySystem.HasTag(CombatTag.Dead)
                || (attributes != null && attributes.IsDead);
        }

        // 刷新行为树决策所需的距离事实，避免多个节点重复计算目标距离。
        private void RefreshDecisionFacts()
        {
            if (Blackboard.CombatTarget == null || context == null)
            {
                if (combatDecision != null)
                {
                    combatDecision.ResetAttackSelectionHistory();
                }

                Blackboard.SetTargetDistanceFacts(0f, false, false);
                return;
            }

            Transform combatTarget = Blackboard.CombatTarget;
            float distance = Vector3.Distance(transform.position, combatTarget.position);
            bool isInCombatRange = context.Combat != null
                && context.Combat.IsInCombatRange(combatTarget);
            bool isInChaseRange = context.Combat != null
                && distance <= context.Combat.ChaseRange;
            Blackboard.SetTargetDistanceFacts(distance, isInCombatRange, isInChaseRange);
            SyncCombatDecisionFacts();
        }

        // 响应玩家默认攻击输入事件，只有决策器允许的阶段才写入黑板反应事实。
        private void OnPlayerAttackInput(object sender, EventArgsBase eventArgs)
        {
            PlayerAttackInputEventArgs attackInput = eventArgs as PlayerAttackInputEventArgs;
            HandlePlayerAttackInput(attackInput, Random.value);
        }

        // 计算当前距离、稳定值和前方事实，并把玩家输入交给战斗决策器。
        private void HandlePlayerAttackInput(PlayerAttackInputEventArgs eventArgs, float randomValue)
        {
            if (eventArgs == null || eventArgs.Player == null || combatDecision == null)
            {
                return;
            }

            EnemyCombatReaction reaction = combatDecision.TryHandlePlayerAttackInput(
                Time.time,
                GetStabilityRatio(),
                Vector3.Distance(transform.position, eventArgs.Player.position),
                eventArgs.DefaultAttackRange,
                IsPlayerInFront(eventArgs.Player),
                randomValue);
            if (reaction == EnemyCombatReaction.None)
            {
                return;
            }

            SyncCombatDecisionFacts();
        }

        // 把战斗决策器当前状态同步到黑板，供行为树条件节点读取。
        private void SyncCombatDecisionFacts()
        {
            EnemyBehaviorTreeUtility.SyncCombatDecisionFacts(this);
        }

        /// <summary>创建使用真实技能范围目录的战斗决策器，定义缺失时返回空决策器引用。</summary>
        private EnemyCombatDecisionController CreateCombatDecision(EnemyDefinition enemyDefinition)
        {
            if (enemyDefinition == null)
            {
                return null;
            }

            EnemyAttackCatalog catalog = EnemyAttackCatalog.Create(enemyDefinition.CombatConfig, ResolveEnemySkill);
            return new EnemyCombatDecisionController(
                enemyDefinition.CombatConfig,
                enemyDefinition.DecisionProfile,
                catalog);
        }

        /// <summary>从全局配置管理器读取敌人技能。</summary>
        private SkillConfig ResolveEnemySkill(int skillId)
        {
            return ConfigManager.Instance.GetSkillConfig(skillId);
        }

        // 读取稳定值比例，缺少属性组件时按满稳定处理，避免误触发低稳定值闪避。
        private float GetStabilityRatio()
        {
            EnemyAttributeComponent attribute = context != null ? context.Attribute : null;
            if (attribute == null || attribute.MaxStability <= 0)
            {
                return 1f;
            }

            return (float)attribute.Stability / attribute.MaxStability;
        }

        // 使用水平点积判断玩家是否位于敌人前方半区。
        private bool IsPlayerInFront(Transform player)
        {
            Vector3 direction = player.position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return true;
            }

            Vector3 forward = transform.forward;
            forward.y = 0f;
            return Vector3.Dot(forward.normalized, direction.normalized) >= 0f;
        }

        // 读取当前敌人定义中的战斗记忆时长，定义缺失时使用黑板默认流程的安全值。
        private float GetCombatMemoryDuration()
        {
            return definition != null && definition.CombatConfig != null
                ? definition.CombatConfig.combatMemoryDuration
                : 4f;
        }

        // 读取当前敌人定义中的警戒记忆时长，定义缺失时使用黑板默认流程的安全值。
        private float GetAlertMemoryDuration()
        {
            return definition != null && definition.PerceptionConfig != null
                ? definition.PerceptionConfig.alertMemoryDuration
                : 4f;
        }

        // 记录正常层返程使用的启动原点和巡逻原点，避免后续按当前位置反推。
        private void RecordNormalHome(EnemyAgent agent)
        {
            StartupHomePosition = transform.position;
            StartupHomeRotation = transform.rotation;

            Transform[] route = agent != null ? agent.PatrolRoute : new Transform[0];
            if (route.Length > 0 && route[0] != null)
            {
                NormalOriginPosition = route[0].position;
                NormalOriginRotation = route[0].rotation;
                return;
            }

            NormalOriginPosition = StartupHomePosition;
            NormalOriginRotation = StartupHomeRotation;
        }
    }
}
