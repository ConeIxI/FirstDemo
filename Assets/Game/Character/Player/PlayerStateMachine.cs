using System.Collections.Generic;
using Game.Battle.Ability;
using Game.Battle.Buff;
using Game.Battle.Combat.Config;
using Game.Character;
using Game.Character.Enemy.Components;
using Game.Character.Enemy.Events;
using Game.Character.Equipment;
using Game.Character.Common;
using Game.Character.Player.Combat;
using Game.Character.Player.Execution;
using Game.Character.Player.PlayerFsm;
using Game.World.Drop;
using GameMain2.Framework.Audio;
using GameMain2.Framework.Core;
using GameMain2.Framework.Core.FSM;
using GameMain2.Framework.Manager;
using GameMain2.Game.EventArgs;
using GameMain2.Scripts.UI;
using UnityEngine;
using UnityEngine.SceneManagement;
using SkillState = Game.Character.Player.PlayerFsm.SkillState;

namespace GameMain2.Scripts.Character
{
    public class PlayerStateMachine : CharacterStateMachine
    {
        private const string MissingPlayerControllerError =
            "PlayerStateMachine 缺少同一 GameObject 上的 PlayerController，组件已禁用。";
        private const string MissingAbilitySystemError =
            "PlayerStateMachine 缺少玩家 CombatAbilitySystem，组件已禁用。";
        private const string MissingCombatAnimatorConfigurationError =
            "PlayerStateMachine 缺少 ArmsLayer 或 float 参数 IsCombat，拔刀收刀动画流程将直接结算。";
        private const string MissingCombatInterruptTriggerConfigurationError =
            "PlayerStateMachine 缺少 Trigger 参数 EnterCombat 或 ExitCombat，拔刀收刀被打断时无法补发最终结果。";
        private const string MissingDefenceAnimatorConfigurationError =
            "PlayerStateMachine 缺少 float 参数 IsDefence，玩家防御 Animator 参数不会生效。";
        private const string ArmsLayerName = "ArmsLayer";
        private const string CombatParameterName = "IsCombat";
        private const string DefenceParameterName = "IsDefence";
        private const string HorizontalSpeedParameter = "HorizontalSpeed";
        private const string VerticalSpeedParameter = "VerticalSpeed";
        private const string WalkHorizontalSpeedParameter = "WalkHorizontalSpeed";
        private const string WalkVerticalSpeedParameter = "WalkVerticalSpeed";
        private const float DefenceParameterDampTime = 0.1f;
        private const float DodgeCooldownTime = 1f;
        private const float MovementBlendDampTime = 0.1f;
        private const float LockMoveAnimatorSpeed = 4.3f;
        private const float WalkAnimatorSpeed = 1.6f;
        private const float BattleBgmFadeSeconds = 1f;
        private const float BattleBgmFadeOutSeconds = 1f;
        public const string EnterCombatAnimationName = "EnterCombat";
        public const string ExitCombatAnimationName = "ExitCombat";

        private FsmBase<PlayerStateMachine> m_AniFsm;
        private PlayerCombatActionRequest m_CombatActionRequest;
        private readonly PlayerCombatStanceContext m_combatStance = new PlayerCombatStanceContext();
        private PlayerCombatTransitionPhase m_startedCombatAnimationPhase;
        private int m_armsLayerIndex = -1;
        private bool m_hasCombatAnimatorConfiguration;
        private bool m_hasCombatInterruptTriggerConfiguration;
        private bool m_hasDefenceParameter;
        private bool m_isDefending;
        private float m_defenceParameterTarget;
        private float m_nextDodgeTime;
        private bool m_suppressNextDefenceParryWindow;
        private CombatBuffController m_buffController;
        private int m_pendingConsumableSlotIndex = -1;
        private WorldDropItem m_pendingPickupItem;
        private bool m_hasMoveInput;

        [SerializeField] private PlayerController playerController;
        [SerializeField] private PlayerExecutionController executionController;

        public LockOnManager LockOnManager;
        public PlayerController PlayerController => playerController;
        public PlayerExecutionController ExecutionController => executionController;
        public PlayerCombatActionRequest CombatActionRequest => m_CombatActionRequest;
        public bool IsCombat => HasActiveWeapon && m_combatStance.IsCombat;
        public bool CanDefend => HasActiveWeapon;
        public bool IsDefending => m_isDefending;
        public bool HasTargetingEnemy => m_combatStance.HasTargetingEnemy;
        public PlayerCombatTransitionPhase CombatTransitionPhase => m_combatStance.Phase;
        public int ArmsLayerIndex => m_armsLayerIndex;
        public PlayerState CurrentPlayerState => CurState;
        public bool HasMoveInput => m_hasMoveInput;
        private bool HasActiveWeapon => GetEquipmentManager()?.ActiveWeapon != null;

        /// <summary>判断玩家闪避冷却是否结束，供所有闪避入口统一校验。</summary>
        public bool CanStartDodge()
        {
            return Time.time >= m_nextDodgeTime;
        }

        /// <summary>记录本次闪避开始时间，并设置下一次可闪避的冷却时间点。</summary>
        public void MarkDodgeStarted()
        {
            m_nextDodgeTime = Time.time + DodgeCooldownTime;
        }

        /// <summary>创建玩家 FSM 并校验玩家战斗依赖。</summary>
        private void Awake()
        {
            m_AniFsm = new FsmBase<PlayerStateMachine>(this, GetPlayerStates());
            if (playerController == null)
            {
                playerController = GetComponent<PlayerController>();
            }

            if (executionController == null)
            {
                executionController = GetComponent<PlayerExecutionController>();
                if (executionController == null)
                {
                    executionController = gameObject.AddComponent<PlayerExecutionController>();
                }
            }

            if (playerController == null)
            {
                Debug.LogError(MissingPlayerControllerError, this);
                enabled = false;
                return;
            }

            EnsureBuffController();

            if (playerController.AbilitySystem == null)
            {
                Debug.LogError(MissingAbilitySystemError, this);
                enabled = false;
            }
        }

        /// <summary>启用时订阅统一战斗结算事件。</summary>
        private void OnEnable()
        {
            if (playerController != null && playerController.AbilitySystem != null)
            {
                EventCenter.Instance.Subscribe(CombatEvent.EventId, OnCombatEvent);
                EventCenter.Instance.Subscribe(
                    EnemyCombatTargetChangedEventArgs.EventId,
                    HandleEnemyCombatTargetChanged);
            }
        }

        /// <summary>禁用时解除统一战斗结算事件订阅。</summary>
        private void OnDisable()
        {
            EventCenter.TryUnSubscribe(CombatEvent.EventId, OnCombatEvent);
            EventCenter.TryUnSubscribe(
                EnemyCombatTargetChangedEventArgs.EventId,
                HandleEnemyCombatTargetChanged);
        }

        /// <summary>启动玩家 FSM 的待机状态。</summary>
        private void Start()
        {
            InitializeCombatAnimatorConfiguration();
            InitializeDefenceAnimatorConfiguration();
            ApplyCombatAnimatorParameter();
            ApplyDefenceAnimatorState(false);
            m_AniFsm.SetStartState(typeof(LocomotionState));
        }

        /// <summary>按帧推进玩家 FSM。</summary>
        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                return;
            }

            UpdateMovementBlendTreeParameters(deltaTime);
            UpdateDefenceAnimatorParameter(deltaTime);
            PublishDefaultAttackInputIfPressed();
            m_AniFsm.Update(deltaTime);
        }

        /// <summary>校验指定消耗品槽是否可用，并缓存给 ItemDrink 动画事件统一结算。</summary>
        public bool TryPrepareConsumableUse(int slotIndex)
        {
            if (slotIndex < 0)
            {
                return false;
            }

            BagInventoryManager inventory = BagInventoryManager.Instance;
            BagItemData item = inventory.GetItem(BagSlotType.Consumable, slotIndex);
            if (item == null || item.BuffId <= 0)
            {
                return false;
            }

            m_pendingConsumableSlotIndex = slotIndex;
            return true;
        }

        /// <summary>ItemDrink 动画事件入口：在指定帧添加 Buff 并扣除已缓存的消耗品。</summary>
        public void OnItemDrinkConsumableEvent()
        {
            TryApplyPreparedConsumableUse();
        }

        /// <summary>清理待使用消耗品槽位，避免状态被打断后留下过期请求。</summary>
        public void ClearPreparedConsumableUse()
        {
            m_pendingConsumableSlotIndex = -1;
        }

        /// <summary>执行已缓存的消耗品使用请求，成功添加 Buff 后扣除对应装备槽数量。</summary>
        public bool TryApplyPreparedConsumableUse()
        {
            int slotIndex = m_pendingConsumableSlotIndex;
            m_pendingConsumableSlotIndex = -1;
            if (slotIndex < 0)
            {
                return false;
            }

            BagInventoryManager inventory = BagInventoryManager.Instance;
            BagItemData item = inventory.GetItem(BagSlotType.Consumable, slotIndex);
            if (item == null || item.BuffId <= 0)
            {
                return false;
            }

            CombatBuffController buffController = EnsureBuffController();
            if (!buffController.AddBuff(item.BuffId))
            {
                return false;
            }

            SoundManager.Instance.PlaySfxAt(SoundId.Drink, transform.position);
            return inventory.TryConsumeEquipmentItem(BagSlotType.Consumable, slotIndex);
        }

        /// <summary>玩家死亡时清空身上的全部 Buff，并停止这些 Buff 关联的表现效果。</summary>
        public void ClearActiveBuffsForDeath()
        {
            m_buffController.ClearBuffs();
        }

        /// <summary>确保玩家同对象拥有 Buff 控制器，消耗品效果统一通过它写入属性修正。</summary>
        private CombatBuffController EnsureBuffController()
        {
            if (m_buffController == null)
            {
                m_buffController = GetComponent<CombatBuffController>();
                if (m_buffController == null)
                {
                    m_buffController = gameObject.AddComponent<CombatBuffController>();
                }
            }

            return m_buffController;
        }

        /// <summary>由地面掉落物请求进入拾取状态，成功后缓存本次待拾取对象。</summary>
        public bool TryStartItemGet(WorldDropItem pickupItem)
        {
            if (pickupItem == null || CurState != PlayerState.Locomotion)
            {
                return false;
            }

            m_pendingPickupItem = pickupItem;
            ChangeState<ItemGetState>();
            return true;
        }

        /// <summary>拾取动画完成时结算已缓存的地面掉落物拾取请求。</summary>
        public void CompletePendingItemGet()
        {
            if (m_pendingPickupItem == null)
            {
                return;
            }

            WorldDropItem pickupItem = m_pendingPickupItem;
            m_pendingPickupItem = null;
            pickupItem.RequestBagPickup();
        }

        /// <summary>拾取状态被打断时释放地面掉落物请求，允许玩家后续再次拾取。</summary>
        public void CancelPendingItemGet()
        {
            if (m_pendingPickupItem == null)
            {
                return;
            }

            WorldDropItem pickupItem = m_pendingPickupItem;
            m_pendingPickupItem = null;
            pickupItem.CancelPickupRequest();
        }

        /// <summary>每帧根据玩家移动键输入统一刷新移动和行走 BlendTree 参数。</summary>
        private void UpdateMovementBlendTreeParameters(float deltaTime)
        {
            Vector2 move = InputManager.Instance.GetMoveDirection();
            m_hasMoveInput = move.sqrMagnitude > 0.0001f;
            SetFloat(HorizontalSpeedParameter, move.x * LockMoveAnimatorSpeed, MovementBlendDampTime, deltaTime);
            SetFloat(VerticalSpeedParameter, move.y * LockMoveAnimatorSpeed, MovementBlendDampTime, deltaTime);
            SetFloat(WalkHorizontalSpeedParameter, move.x * WalkAnimatorSpeed, MovementBlendDampTime, deltaTime);
            SetFloat(WalkVerticalSpeedParameter, move.y * WalkAnimatorSpeed, MovementBlendDampTime, deltaTime);
        }

        /// <summary>标记下一次进入防御状态时不打开弹反窗口，用于格挡受击后保持防御的续接。</summary>
        public void SuppressNextDefenceParryWindow()
        {
            m_suppressNextDefenceParryWindow = true;
        }

        /// <summary>消费一次跳过防御弹反窗口的请求，保证只影响下一次 Locomotion 防御开启。</summary>
        public bool ConsumeSuppressNextDefenceParryWindow()
        {
            if (!m_suppressNextDefenceParryWindow)
            {
                return false;
            }

            m_suppressNextDefenceParryWindow = false;
            return true;
        }

        /// <summary>同步玩家防御状态，并设置 IsDefence 平滑过渡目标值。</summary>
        public void ApplyDefenceAnimatorState(bool isDefending)
        {
            m_isDefending = isDefending;
            float targetValue = isDefending ? 1f : 0f;
            m_defenceParameterTarget = targetValue;
        }

        /// <summary>销毁时关闭 FSM 并释放当前状态资源。</summary>
        private void OnDestroy()
        {
            if (m_AniFsm != null)
            {
                m_AniFsm.Shutdown();
                m_AniFsm = null;
            }
        }

        /// <summary>按视觉模型朝向换算动画根运动，并发布玩家根运动事件。</summary>
        private void OnAnimatorMove()
        {
            Quaternion directionOffset =
                playerController.Model.rotation *
                Quaternion.Inverse(animator.transform.rotation);
            Vector3 worldDeltaPosition = directionOffset * animator.deltaPosition;

            if (worldDeltaPosition != Vector3.zero || animator.deltaRotation != Quaternion.identity)
            {
                EventCenter.Instance.Fire(
                    this,
                    new GameMain2.Game.EventArgs.PlayerRootMotionEventArgs(
                        worldDeltaPosition,
                        animator.deltaRotation));
            }
        }

        /// <summary>切换到指定玩家状态。</summary>
        public void ChangeState<T>() where T : PlayerStateBase
        {
            m_AniFsm.ChangeState<T>();
        }

        /// <summary>检测本帧默认攻击按键，并在 FSM 更新前发布一次输入事件。</summary>
        private void PublishDefaultAttackInputIfPressed()
        {
            if (executionController != null && executionController.IsPlaying)
            {
                return;
            }

            if (Input.GetMouseButtonDown(0))
            {
                PublishDefaultAttackInput();
            }
        }

        /// <summary>发布玩家默认攻击输入事件，携带唯一攻击范围数据源。</summary>
        private void PublishDefaultAttackInput()
        {
            EventCenter.Instance.Fire(
                this,
                new PlayerAttackInputEventArgs(
                    playerController.transform,
                    playerController.DefaultAttackRange));
        }

        /// <summary>写入下一次玩家攻击或武器技能要使用的强类型动作请求。</summary>
        public void SetCombatActionRequest(WeaponType weaponType, int skillId)
        {
            m_CombatActionRequest = new PlayerCombatActionRequest(weaponType, skillId);
        }

        /// <summary>不播放拔刀动画并立即进入战斗，必要时结算正在进行的换武器。</summary>
        public void EnterCombatImmediately()
        {
            EquipmentManager equipmentManager = GetEquipmentManager();
            if (equipmentManager == null || equipmentManager.ActiveWeapon == null)
            {
                ForceExitCombatWithoutWeapon(equipmentManager);
                return;
            }

            PlayerCombatTransitionPhase interruptedPhase = m_combatStance.Phase;
            PlayerCombatTransitionOutcome outcome = m_combatStance.SettleInterruptedTransition();
            ApplyInterruptedCombatTransitionTrigger(interruptedPhase);
            if (outcome.ShouldSwitchWeapon && equipmentManager != null)
            {
                equipmentManager.ActivateWeapon(outcome.TargetWeaponIndex);
            }

            m_combatStance.EnterCombatImmediately();
            ApplyCombatAnimatorParameter();
            equipmentManager?.ApplyWeaponAppearance(true);
        }

        /// <summary>请求按当前姿态执行非战斗直接切换或战斗两段式切换。</summary>
        public bool RequestWeaponSwitch()
        {
            EquipmentManager equipmentManager = GetEquipmentManager();
            if (equipmentManager == null)
            {
                ForceExitCombatWithoutWeapon(equipmentManager);
                return false;
            }

            if (equipmentManager.ActiveWeapon == null)
            {
                ForceExitCombatWithoutWeapon(equipmentManager);
                return false;
            }

            int targetIndex = equipmentManager.GetNextEquippedWeaponIndex();
            if (targetIndex < 0 || targetIndex == equipmentManager.ActiveWeaponIndex)
            {
                return false;
            }

            if (!IsCombat)
            {
                bool activated = equipmentManager.ActivateWeapon(targetIndex);
                if (activated)
                {
                    equipmentManager.ApplyWeaponAppearance(false);
                }

                return activated;
            }

            return m_combatStance.RequestWeaponSwitch(equipmentManager.ActiveWeaponIndex, targetIndex);
        }

        /// <summary>完成当前 ArmsLayer 动画阶段并执行数据切换或稳定姿态同步。</summary>
        public void CompleteCombatAnimationPhase()
        {
            PlayerCombatAnimationCompletion completion = m_combatStance.CompleteAnimationPhase();
            ClearStartedCombatAnimationPhase();
            EquipmentManager equipmentManager = GetEquipmentManager();
            if (completion == PlayerCombatAnimationCompletion.SwitchWeaponAndEnter)
            {
                equipmentManager?.ActivateWeapon(m_combatStance.TargetWeaponIndex);
                if (equipmentManager == null || equipmentManager.ActiveWeapon == null)
                {
                    ForceExitCombatWithoutWeapon(equipmentManager);
                    return;
                }

                return;
            }

            if (equipmentManager == null || equipmentManager.ActiveWeapon == null)
            {
                ForceExitCombatWithoutWeapon(equipmentManager);
                return;
            }

            ApplyCombatAnimatorParameter();
            equipmentManager?.ApplyWeaponAppearance(IsCombat);
        }

        /// <summary>请求普通拔刀；没有当前武器时直接写入战斗姿态。</summary>
        public bool RequestEnterCombatAnimation()
        {
            EquipmentManager equipmentManager = GetEquipmentManager();
            if (equipmentManager == null || equipmentManager.ActiveWeapon == null)
            {
                ForceExitCombatWithoutWeapon(equipmentManager);
                return false;
            }

            return m_combatStance.RequestEnterCombatAnimation();
        }

        /// <summary>请求普通收刀；没有当前武器时直接退出战斗。</summary>
        public bool RequestExitCombatAnimation()
        {
            if (IsDefending)
            {
                return false;
            }

            EquipmentManager equipmentManager = GetEquipmentManager();
            if (equipmentManager == null || equipmentManager.ActiveWeapon == null)
            {
                m_combatStance.ExitCombatImmediately();
                ApplyCombatAnimatorParameter();
                equipmentManager?.ApplyWeaponAppearance(false);
                return false;
            }

            return m_combatStance.RequestExitCombatAnimation();
        }

        /// <summary>推进 Locomotion 下的自动收刀计时，达到三秒时创建收刀请求。</summary>
        public void TickAutoSheath(float deltaTime)
        {
            if (IsDefending)
            {
                return;
            }

            if (!HasActiveWeapon)
            {
                ForceExitCombatWithoutWeapon(GetEquipmentManager());
                return;
            }

            if (m_combatStance.TickAutoSheath(deltaTime, true))
            {
                RequestExitCombatAnimation();
            }
        }

        /// <summary>刷新攻击、技能或受击产生的战斗活动时间。</summary>
        public void RefreshCombatActivity()
        {
            m_combatStance.RefreshCombatActivity();
        }

        /// <summary>Locomotion 被其他 FSM 状态打断时结算到确定终点。</summary>
        public void SettleCombatTransitionOnLocomotionExit()
        {
            PlayerCombatTransitionPhase interruptedPhase = m_combatStance.Phase;
            PlayerCombatTransitionOutcome outcome = m_combatStance.SettleInterruptedTransition();
            ApplyInterruptedCombatTransitionTrigger(interruptedPhase);
            EquipmentManager equipmentManager = GetEquipmentManager();
            if (outcome.ShouldSwitchWeapon && equipmentManager != null)
            {
                equipmentManager.ActivateWeapon(outcome.TargetWeaponIndex);
            }

            if (equipmentManager == null || equipmentManager.ActiveWeapon == null)
            {
                ForceExitCombatWithoutWeapon(equipmentManager);
                return;
            }

            ApplyCombatAnimatorParameter();
            equipmentManager?.ApplyWeaponAppearance(outcome.IsCombat);
        }

        /// <summary>当玩家没有 active 武器时强制保持非战斗状态，保证 IsCombat 参数始终为 0。</summary>
        public void ForceExitCombatIfNoWeapon()
        {
            EquipmentManager equipmentManager = GetEquipmentManager();
            if (equipmentManager != null && equipmentManager.ActiveWeapon != null)
            {
                return;
            }

            ForceExitCombatWithoutWeapon(equipmentManager);
        }

        /// <summary>记录已经开始播放的拔刀或收刀阶段，后续只有被打断时才补发 Trigger。</summary>
        public void MarkCombatTransitionAnimationStarted(PlayerCombatTransitionPhase phase)
        {
            m_startedCombatAnimationPhase = phase;
        }

        /// <summary>EnterCombat 动画事件：把当前武器切到手持表现。</summary>
        public void OnEnterCombatWeaponEvent()
        {
            if (!CanApplyEnterCombatWeaponEvent())
            {
                return;
            }

            EquipmentManager equipmentManager = GetEquipmentManager();
            if (equipmentManager != null)
            {
                equipmentManager.ShowWeaponInHand(equipmentManager.ActiveWeaponIndex);
            }
        }

        /// <summary>ExitCombat 动画事件：把当前或源武器切到收纳表现。</summary>
        public void OnExitCombatWeaponEvent()
        {
            if (!CanApplyExitCombatWeaponEvent())
            {
                return;
            }

            EquipmentManager equipmentManager = GetEquipmentManager();
            if (equipmentManager == null)
            {
                return;
            }

            int slotIndex = m_combatStance.Phase == PlayerCombatTransitionPhase.SwitchingWeaponExit
                ? m_combatStance.SourceWeaponIndex
                : equipmentManager.ActiveWeaponIndex;
            equipmentManager.ShowWeaponSheathed(slotIndex);
        }

        /// <summary>更新当前动作请求的技能 ID，供普通攻击连段切换下一段。</summary>
        public void UpdateCombatActionSkillId(int skillId)
        {
            m_CombatActionRequest = m_CombatActionRequest.WithSkillId(skillId);
        }

        /// <summary>构建玩家 FSM 的全部状态实例。</summary>
        private PlayerStateBase[] GetPlayerStates()
        {
            List<PlayerStateBase> stateList = new List<PlayerStateBase>
            {
                new LocomotionState(),
                new RunStopState(),
                new AirDownState(),
                new DodgeState(),
                new AttackState(),
                new SkillState(),
                new GetHitState(),
                new PlayerBlockHitState(),
                new ParryState(),
                new ItemGetState(),
                new UnbalanceState(),
                new ExecutionState(),
                new DeadState()
            };
            return stateList.ToArray();
        }

        /// <summary>消费与玩家相关的战斗事件并按最高优先级切换状态。</summary>
        private void OnCombatEvent(object sender, EventArgsBase eventArgs)
        {
            CombatEvent combatEvent = eventArgs as CombatEvent;
            CombatAbilitySystem playerAbilitySystem = playerController.AbilitySystem;
            if (combatEvent == null)
            {
                return;
            }

            if (combatEvent.Target == playerAbilitySystem)
            {
                HandleTargetCombatEvent(combatEvent);
                return;
            }

            if (combatEvent.Source == playerAbilitySystem && combatEvent.SourceUnbalanced)
            {
                ChangeState<UnbalanceState>();
            }
        }

        /// <summary>按死亡、失衡、格挡、普通受击顺序处理玩家作为目标的事件。</summary>
        private void HandleTargetCombatEvent(CombatEvent combatEvent)
        {
            RefreshCombatActivity();
            if (m_combatStance.Phase == PlayerCombatTransitionPhase.ExitingCombat)
            {
                EnterCombatImmediately();
            }

            if (combatEvent.TargetDead)
            {
                SkillHitWeight hitWeight = combatEvent.Skill != null
                    ? combatEvent.Skill.HitWeight
                    : SkillHitWeight.Light;
                SetPendingDeathReactionParameters(IsCombat, hitWeight);
                ChangeState<DeadState>();
                SceneFlowManager.Instance.CacheCurrentPlayerRestartSnapshot();
                UIManager.Instance.ShowDeathPanel();
            }
            else if (CurState == PlayerState.Unbalance)
            {
                // 玩家失衡期间受击只保留属性数值变化，不重置失衡流程也不播放受击动画。
                return;
            }
            else if (combatEvent.TargetUnbalanced)
            {
                ChangeState<UnbalanceState>();
            }
            else if (combatEvent.Type == CombatEventType.Blocked)
            {
                ChangeState<PlayerBlockHitState>();
            }
            else if (combatEvent.Type == CombatEventType.Parried)
            {
                FaceParrySource(combatEvent);
                ChangeState<ParryState>();
            }
            else if (combatEvent.TargetShouldReact)
            {
                SkillHitWeight hitWeight = combatEvent.Skill != null
                    ? combatEvent.Skill.HitWeight
                    : SkillHitWeight.Light;
                SetPendingHitReactionParameters(IsCombat, hitWeight, ResolveHitDirection(combatEvent));
                ChangeState<GetHitState>();
            }
        }

        /// <summary>格挡成功进入弹反状态前，立即让玩家模型朝向攻击来源。</summary>
        private void FaceParrySource(CombatEvent combatEvent)
        {
            Vector3 sourceDirection = ResolveHitSourceDirection(combatEvent);
            sourceDirection.y = 0f;
            if (sourceDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            playerController.RotateInstantly(Quaternion.LookRotation(sourceDirection.normalized));
        }

        /// <summary>根据攻击来源相对玩家的水平朝向解析受击 BlendTree 方向值。</summary>
        private PlayerHitDirection ResolveHitDirection(CombatEvent combatEvent)
        {
            Vector3 sourceDirection = ResolveHitSourceDirection(combatEvent);
            sourceDirection.y = 0f;
            if (sourceDirection.sqrMagnitude <= 0.0001f)
            {
                return PlayerHitDirection.Front;
            }

            Vector3 localDirection = transform.InverseTransformDirection(sourceDirection.normalized);
            float absX = Mathf.Abs(localDirection.x);
            float absZ = Mathf.Abs(localDirection.z);
            if (absZ >= absX)
            {
                return localDirection.z >= 0f
                    ? PlayerHitDirection.Front
                    : PlayerHitDirection.Back;
            }

            return localDirection.x >= 0f
                ? PlayerHitDirection.Right
                : PlayerHitDirection.Left;
        }

        /// <summary>优先使用攻击者位置，缺失时回退到命中点或命中方向推导攻击来源。</summary>
        private Vector3 ResolveHitSourceDirection(CombatEvent combatEvent)
        {
            if (combatEvent.Source != null)
            {
                return combatEvent.Source.transform.position - transform.position;
            }

            Vector3 hitPointOffset = combatEvent.HitPoint - transform.position;
            if (hitPointOffset.sqrMagnitude > 0.0001f)
            {
                return hitPointOffset;
            }

            return -combatEvent.HitDirection;
        }

        /// <summary>初始化战斗 Animator 层与参数配置，缺少资源时允许流程直接结算。</summary>
        private void InitializeCombatAnimatorConfiguration()
        {
            m_armsLayerIndex = GetAnimatorLayerIndex(ArmsLayerName);
            m_hasCombatAnimatorConfiguration = m_armsLayerIndex >= 0
                && HasAnimatorParameter(CombatParameterName, AnimatorControllerParameterType.Float);
            m_hasCombatInterruptTriggerConfiguration = HasAnimatorParameter(
                    EnterCombatAnimationName,
                    AnimatorControllerParameterType.Trigger)
                && HasAnimatorParameter(
                    ExitCombatAnimationName,
                    AnimatorControllerParameterType.Trigger);
            if (!m_hasCombatAnimatorConfiguration)
            {
                Debug.LogError(MissingCombatAnimatorConfigurationError, this);
            }

            if (!m_hasCombatInterruptTriggerConfiguration)
            {
                Debug.LogError(MissingCombatInterruptTriggerConfigurationError, this);
            }
        }

        /// <summary>初始化防御 IsDefence 参数配置。</summary>
        private void InitializeDefenceAnimatorConfiguration()
        {
            m_hasDefenceParameter = HasAnimatorParameter(DefenceParameterName, AnimatorControllerParameterType.Float);
            if (!m_hasDefenceParameter)
            {
                Debug.LogError(MissingDefenceAnimatorConfigurationError, this);
            }
        }

        /// <summary>按固定阻尼时间推进 IsDefence 参数，避免防御混合值瞬间跳变。</summary>
        private void UpdateDefenceAnimatorParameter(float deltaTime)
        {
            if (!m_hasDefenceParameter)
            {
                return;
            }

            SetFloat(DefenceParameterName, m_defenceParameterTarget, DefenceParameterDampTime, deltaTime);
        }

        /// <summary>在 Animator 配置有效时同步稳定战斗参数。</summary>
        private void ApplyCombatAnimatorParameter()
        {
            if (m_hasCombatAnimatorConfiguration)
            {
                SetFloat(CombatParameterName, IsCombat ? 1f : 0f);
            }
        }

        /// <summary>清理战斗姿态并按无武器状态同步 Animator 和模型表现。</summary>
        private void ForceExitCombatWithoutWeapon(EquipmentManager equipmentManager)
        {
            PlayerCombatTransitionPhase interruptedPhase = m_combatStance.Phase;
            m_combatStance.ExitCombatImmediately();
            ApplyInterruptedCombatTransitionTrigger(interruptedPhase);
            ApplyCombatAnimatorParameter();
            equipmentManager?.ApplyWeaponAppearance(false);
        }

        /// <summary>拔刀或收刀被其他状态打断时，按被打断的动画阶段补发 Animator Trigger。</summary>
        private void ApplyInterruptedCombatTransitionTrigger(PlayerCombatTransitionPhase interruptedPhase)
        {
            if (interruptedPhase == PlayerCombatTransitionPhase.None
                || m_startedCombatAnimationPhase != interruptedPhase)
            {
                ClearStartedCombatAnimationPhase();
                return;
            }

            ClearStartedCombatAnimationPhase();
            if (!m_hasCombatInterruptTriggerConfiguration)
            {
                return;
            }

            bool interruptedEnterAnimation = interruptedPhase == PlayerCombatTransitionPhase.EnteringCombat
                || interruptedPhase == PlayerCombatTransitionPhase.SwitchingWeaponEnter;
            string activeTrigger = interruptedEnterAnimation ? EnterCombatAnimationName : ExitCombatAnimationName;
            string inactiveTrigger = interruptedEnterAnimation ? ExitCombatAnimationName : EnterCombatAnimationName;
            animator.ResetTrigger(inactiveTrigger);
            animator.SetTrigger(activeTrigger);
        }

        /// <summary>清理当前已启动的拔刀或收刀阶段标记，避免自然结束后误判为打断。</summary>
        private void ClearStartedCombatAnimationPhase()
        {
            m_startedCombatAnimationPhase = PlayerCombatTransitionPhase.None;
        }

        /// <summary>判断当前 EnterCombat 动画事件是否仍属于有效拔刀阶段，避免打断后的旧事件覆盖最终表现。</summary>
        private bool CanApplyEnterCombatWeaponEvent()
        {
            return m_combatStance.Phase == PlayerCombatTransitionPhase.EnteringCombat
                || m_combatStance.Phase == PlayerCombatTransitionPhase.SwitchingWeaponEnter;
        }

        /// <summary>判断当前 ExitCombat 动画事件是否仍属于有效收刀阶段，避免打断后的旧事件覆盖最终表现。</summary>
        private bool CanApplyExitCombatWeaponEvent()
        {
            return m_combatStance.Phase == PlayerCombatTransitionPhase.ExitingCombat
                || m_combatStance.Phase == PlayerCombatTransitionPhase.SwitchingWeaponExit;
        }

        /// <summary>根据敌人目标变化维护锁定集合，并触发玩家拔刀请求。</summary>
        private void HandleEnemyCombatTargetChanged(object sender, EventArgsBase eventArgs)
        {
            EnemyCombatTargetChangedEventArgs targetChanged = eventArgs as EnemyCombatTargetChangedEventArgs;
            if (targetChanged == null || targetChanged.EnemyTransform == null || playerController == null)
            {
                return;
            }

            Transform playerTransform = playerController.transform;
            int enemyId = targetChanged.EnemyTransform.GetInstanceID();
            bool wasTargetingPlayer = targetChanged.PreviousTarget == playerTransform;
            bool isTargetingPlayer = targetChanged.CurrentTarget == playerTransform;
            if (IsEnemyCombatSourceDead(targetChanged.EnemyTransform))
            {
                if (wasTargetingPlayer || isTargetingPlayer)
                {
                    m_combatStance.SetEnemyTargeting(enemyId, false);
                    TryFadeOutBattleBgmForEnemyTargetEnd();
                }

                return;
            }

            if (wasTargetingPlayer && !isTargetingPlayer)
            {
                m_combatStance.SetEnemyTargeting(enemyId, false);
                TryFadeOutBattleBgmForEnemyTargetEnd();
            }

            if (!isTargetingPlayer)
            {
                return;
            }

            m_combatStance.SetEnemyTargeting(enemyId, true);
            TryPlayBattleBgmForEnemyTarget();
            if (m_combatStance.Phase == PlayerCombatTransitionPhase.ExitingCombat)
            {
                SettleCombatTransitionOnLocomotionExit();
            }

            RequestEnterCombatAnimation();
        }

        /// <summary>玩家被普通战斗场景敌人锁定为战斗目标时播放战斗音乐，Boss 场景不播放普通战斗音乐。</summary>
        private static void TryPlayBattleBgmForEnemyTarget()
        {
            if (SceneManager.GetActiveScene().name != SceneNames.BattleScene)
            {
                return;
            }

            if (SoundManager.TryGetInstance(out SoundManager soundManager))
            {
                if (soundManager.IsBgmActive(SoundId.BattleBgm))
                {
                    return;
                }

                soundManager.PlayBgm(SoundId.BattleBgm, BattleBgmFadeSeconds);
            }
        }

        /// <summary>普通战斗场景下最后一个锁定玩家的敌人离开战斗时淡出战斗音乐。</summary>
        private void TryFadeOutBattleBgmForEnemyTargetEnd()
        {
            if (m_combatStance.HasTargetingEnemy || SceneManager.GetActiveScene().name != SceneNames.BattleScene)
            {
                return;
            }

            if (SoundManager.TryGetInstance(out SoundManager soundManager)
                && soundManager.IsBgmActive(SoundId.BattleBgm))
            {
                soundManager.StopAll(SoundId.BattleBgm, BattleBgmFadeOutSeconds);
            }
        }

        /// <summary>判断目标变化事件来源敌人是否已经死亡，死亡来源不能触发玩家拔刀。</summary>
        private static bool IsEnemyCombatSourceDead(Transform enemyTransform)
        {
            EnemyAttributeComponent attribute = enemyTransform.GetComponentInParent<EnemyAttributeComponent>();
            return attribute != null && attribute.IsDead;
        }

        /// <summary>读取玩家装备管理器。</summary>
        private EquipmentManager GetEquipmentManager()
        {
            return playerController == null ? null : playerController.EquipmentManager;
        }

    }
}
