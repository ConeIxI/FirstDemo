using System.Collections.Generic;
using Game.Battle.Combat.Feedback;
using Game.Battle.Combat.Config;
using Game.Battle.Skill.Common;
using Game.Battle.Skill.Effects;
using GameMain2.Framework.Audio;
using GameMain2.Framework.Core;
using UnityEngine;

namespace Game.Battle.Ability
{
    public sealed class CombatAbilitySystem : MonoBehaviour
    {
        private const string MissingAttributesProviderError = "CombatAbilitySystem 缺少属性提供者，组件已禁用。";
        private const string InvalidAttributesProviderError = "CombatAbilitySystem 的属性提供者必须实现 ICombatAttributes，组件已禁用。";
        private const string MissingPlayerResourceError = "玩家 CombatAbilitySystem 的属性提供者必须实现 ICombatResource，组件已禁用。";
        private const string MissingMotionError = "目标 CombatAbilitySystem 缺少 ICombatMotion，组件已禁用。";
        private const float MinimumHealthDamageRatio = 0.05f;
        private const float DefendingStabilityDamageRatio = 0.5f;

        [SerializeField] private CombatFaction faction;
        [SerializeField] private MonoBehaviour attributesProvider;
        [SerializeField] private float stabilityRecoveryDelay = 5f;
        [SerializeField] private float stabilityRecoveryPerSecond = 20f;

        private readonly HashSet<CombatTag> m_permanentTags = new HashSet<CombatTag>();
        private readonly HashSet<CombatTag> m_activeAbilityTags = new HashSet<CombatTag>();
        private readonly Dictionary<CombatTag, float> m_timedTags = new Dictionary<CombatTag, float>();
        private readonly List<CombatTag> m_timedTagBuffer = new List<CombatTag>();
        private readonly HashSet<CombatAbilitySystem> m_resolvedTargets = new HashSet<CombatAbilitySystem>();
        private ICombatAttributes m_attributes;
        private ICombatResource m_resource;
        private float m_stabilityRecoveryDelayRemaining;
        private float m_stabilityRecoveryAccumulator;
        public CombatFaction Faction { get; private set; }
        public ICombatAttributes Attributes => m_attributes;
        public SkillConfig CurrentSkill { get; private set; }
        public bool IsAbilityActive => CurrentSkill != null;
        public bool IsHitWindowOpen { get; private set; }

        /// <summary>从序列化组件解析战斗属性与玩家资源依赖。</summary>
        private void Awake()
        {
            InitializeDependencies();
        }

        /// <summary>按帧推进限时标签和稳定值恢复。</summary>
        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>禁用时取消当前技能并清除全部限时标签。</summary>
        private void OnDisable()
        {
            CancelActiveAbility();
            ClearTimedTags();
        }

        /// <summary>销毁时解除属性变化事件订阅。</summary>
        private void OnDestroy()
        {
            if (m_attributes != null)
            {
                m_attributes.AttributeChanged -= OnAttributeChanged;
            }
        }

        /// <summary>判断当前是否持有指定战斗标签。</summary>
        public bool HasTag(CombatTag tag)
        {
            return m_permanentTags.Contains(tag) || m_activeAbilityTags.Contains(tag) || m_timedTags.ContainsKey(tag);
        }

        /// <summary>添加一个持续存在的战斗标签。</summary>
        public void AddTag(CombatTag tag)
        {
            m_permanentTags.Add(tag);
        }

        /// <summary>仅移除指定标签的永久来源。</summary>
        public void RemoveTag(CombatTag tag)
        {
            m_permanentTags.Remove(tag);
        }

        /// <summary>仅移除指定标签的限时来源。</summary>
        public void RemoveTimedTag(CombatTag tag)
        {
            m_timedTags.Remove(tag);
        }

        /// <summary>添加或刷新一个按秒计时的战斗标签。</summary>
        public void AddTimedTag(CombatTag tag, float duration)
        {
            m_timedTags[tag] = duration;
        }

        /// <summary>检查技能是否满足生存、标签、占用和资源条件。</summary>
        public AbilityActivationResult CanActivate(SkillConfig config)
        {
            if (m_attributes.IsDead || HasTag(CombatTag.Dead))
            {
                return AbilityActivationResult.Dead;
            }

            if (m_attributes.IsUnbalanced || HasTag(CombatTag.Unbalanced))
            {
                return AbilityActivationResult.Unbalanced;
            }

            if (IsAbilityActive)
            {
                return AbilityActivationResult.AlreadyActive;
            }

            if (!HasRequiredTags(config.requiredTags) || HasAnyTag(config.blockedTags))
            {
                return AbilityActivationResult.BlockedByTag;
            }

            if (Faction == CombatFaction.Player && (m_resource == null || m_resource.BattleSpirit < config.battleSpiritCost))
            {
                return AbilityActivationResult.InsufficientResource;
            }

            return AbilityActivationResult.Success;
        }

        /// <summary>校验并激活技能，成功后扣除玩家战意和应用活动标签。</summary>
        public AbilityActivationResult TryActivate(SkillConfig config)
        {
            AbilityActivationResult result = CanActivate(config);
            if (result != AbilityActivationResult.Success)
            {
                return result;
            }

            if (Faction == CombatFaction.Player && !m_resource.TryConsumeBattleSpirit(config.battleSpiritCost))
            {
                return AbilityActivationResult.InsufficientResource;
            }

            CurrentSkill = config;
            for (int i = 0; i < config.activeTags.Length; i++)
            {
                m_activeAbilityTags.Add(config.activeTags[i]);
            }

            return AbilityActivationResult.Success;
        }

        /// <summary>取消当前技能，移除其新增标签并关闭命中窗口。</summary>
        public void CancelActiveAbility()
        {
            m_activeAbilityTags.Clear();
            CurrentSkill = null;
            EndHitWindow();
        }

        /// <summary>打开当前技能的命中窗口。</summary>
        public void BeginHitWindow()
        {
            m_resolvedTargets.Clear();
            IsHitWindowOpen = true;
        }

        /// <summary>关闭当前技能的命中窗口并清除已结算目标。</summary>
        public void EndHitWindow()
        {
            IsHitWindowOpen = false;
            m_resolvedTargets.Clear();
        }

        /// <summary>对指定目标执行一次固定顺序的命中结算并发布结果。</summary>
        public void ReportHit(CombatAbilitySystem target, Vector3 hitPoint)
        {
            if (!CanResolveTarget(target))
            {
                return;
            }

            m_resolvedTargets.Add(target);

            bool targetInvincible = target.HasTag(CombatTag.Invincible);
            bool targetDefending = target.HasTag(CombatTag.Defending);
            bool targetParryWindow = target.HasTag(CombatTag.ParryWindow);
            bool canParry = CanParry(target);
            bool canBlock = CanBlock(target);
            int sourceStabilityBefore = m_attributes.Stability;
            int targetStabilityBefore = target.m_attributes.Stability;

            CombatEvent result;
            if (targetInvincible)
            {
                result = ResolveInvincible(target, hitPoint);
            }
            else if (canParry)
            {
                result = ResolveParry(target, hitPoint);
            }
            else if (canBlock)
            {
                result = ResolveBlock(target, hitPoint);
            }
            else
            {
                result = ResolveNormalHit(target, hitPoint);
            }

            EventCenter.Instance.Fire(this, result);
            CombatEffectExecutor.Execute(result);
            PlayCombatSound(result);
            CombatHitStopController.Play(result);
            ApplyMoveBack(result);
        }

        /// <summary>执行处决百分比伤害，不走普通攻击倍率和防御抵扣，但继续发布统一战斗事件。</summary>
        public void ReportExecutionDamage(CombatAbilitySystem target, int healthDamage, Vector3 hitPoint)
        {
            if (target == null || target == this || target.m_attributes == null || target.m_attributes.IsDead)
            {
                return;
            }

            if (Faction == target.Faction)
            {
                return;
            }

            int targetHealthDamage = target.m_attributes.ApplyHealthDamage(healthDamage);
            bool targetDead = target.m_attributes.IsDead;
            if (targetDead)
            {
                target.CancelActiveAbility();
            }

            CombatEvent result = CreateCombatEvent(
                CombatEventType.Hit,
                target,
                null,
                hitPoint,
                targetHealthDamage,
                targetStabilityDamage: 0,
                sourceStabilityDamage: 0,
                sourceBattleSpiritGain: 0,
                targetInterrupted: false,
                targetShouldReact: false,
                targetUnbalanced: false,
                sourceUnbalanced: false,
                targetDead: targetDead);

            EventCenter.Instance.Fire(this, result);
            CombatEffectExecutor.Execute(result);
            PlayCombatSound(result);
            CombatHitStopController.Play(result);
        }
        
        /// <summary>仅为普通命中或格挡事件通过目标 ICombatMotion 施加配置击退。</summary>
        private static void ApplyMoveBack(CombatEvent combatEvent)
        {
            if (combatEvent == null
                || combatEvent.Target == null
                || combatEvent.Skill == null
                || combatEvent.Skill.hitConfig == null
                || combatEvent.Skill.hitConfig.moveBackDistance <= 0f
                || (combatEvent.Type != CombatEventType.Hit && combatEvent.Type != CombatEventType.Blocked))
            {
                return;
            }

            ICombatMotion motion = combatEvent.Target.GetComponent<ICombatMotion>();
            if (motion == null)
            {
                Debug.LogError(MissingMotionError, combatEvent.Target);
                combatEvent.Target.enabled = false;
                return;
            }

            Vector3 direction = combatEvent.HitDirection;
            direction.y = 0f;
            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            motion.ApplyExternalDisplacement(direction.normalized * combatEvent.Skill.hitConfig.moveBackDistance);
        }

        /// <summary>根据战斗结算结果播放普通受击、防御受击或玩家弹反音效。</summary>
        private static void PlayCombatSound(CombatEvent combatEvent)
        {
            if (combatEvent.Type == CombatEventType.Parried && combatEvent.Target.Faction == CombatFaction.Player)
            {
                SoundManager.Instance.PlaySfx2D(SoundId.Parry);
            }
            else if (combatEvent.Type == CombatEventType.Blocked)
            {
                SoundManager.Instance.PlaySfx2D(SoundId.Defence);
            }
            else if (combatEvent.Type == CombatEventType.Hit && combatEvent.TargetHealthDamage > 0)
            {
                SoundManager.Instance.PlaySfx2D(SoundId.Hit);
            }
        }

        /// <summary>推进限时标签和稳定值恢复计时。</summary>
        public void Tick(float deltaTime)
        {
            TickTimedTags(deltaTime);
            TickStabilityRecovery(deltaTime);
        }

        /// <summary>重新开始稳定值恢复等待并清空不足一格的恢复累计。</summary>
        public void ResetStabilityRecovery()
        {
            m_stabilityRecoveryDelayRemaining = stabilityRecoveryDelay;
            m_stabilityRecoveryAccumulator = 0f;
        }

        /// <summary>校验序列化依赖并在通过后绑定战斗属性与资源。</summary>
        private void InitializeDependencies()
        {
            if (attributesProvider == null)
            {
                DisableWithError(MissingAttributesProviderError);
                return;
            }

            ICombatAttributes attributes = attributesProvider as ICombatAttributes;
            if (attributes == null)
            {
                DisableWithError(InvalidAttributesProviderError);
                return;
            }

            ICombatResource resource = attributesProvider as ICombatResource;
            if (faction == CombatFaction.Player && resource == null)
            {
                DisableWithError(MissingPlayerResourceError);
                return;
            }

            SetDependencies(faction, attributes, resource);
        }

        /// <summary>记录依赖配置错误并禁用能力系统。</summary>
        private void DisableWithError(string message)
        {
            Debug.LogError(message, this);
            enabled = false;
        }

        /// <summary>替换运行时依赖并维护属性变化事件订阅。</summary>
        private void SetDependencies(CombatFaction combatFaction, ICombatAttributes attributes, ICombatResource resource)
        {
            if (m_attributes != null)
            {
                m_attributes.AttributeChanged -= OnAttributeChanged;
            }

            Faction = combatFaction;
            m_attributes = attributes;
            m_resource = resource ?? attributes as ICombatResource;

            if (m_attributes != null)
            {
                m_attributes.AttributeChanged += OnAttributeChanged;
            }
        }

        /// <summary>检查当前命中窗口是否允许结算指定目标。</summary>
        private bool CanResolveTarget(CombatAbilitySystem target)
        {
            return CurrentSkill != null
                && IsHitWindowOpen
                && m_attributes != null
                && target != null
                && target != this
                && target.m_attributes != null
                && !target.m_attributes.IsDead
                && !target.HasTag(CombatTag.Dead)
                && Faction != target.Faction
                && !m_resolvedTargets.Contains(target);
        }

        /// <summary>检查目标是否处于弹反窗口，技能配置只控制被弹反后的攻击中断反应。</summary>
        private bool CanParry(CombatAbilitySystem target)
        {
            return target.HasTag(CombatTag.ParryWindow);
        }

        /// <summary>检查当前技能能否被目标的防御状态格挡。</summary>
        private bool CanBlock(CombatAbilitySystem target)
        {
            return target.HasTag(CombatTag.Defending) && CurrentSkill.hitConfig.canBeBlocked;
        }

        /// <summary>结算无敌分支，仅产生命中结果而不改变属性。</summary>
        private CombatEvent ResolveInvincible(CombatAbilitySystem target, Vector3 hitPoint)
        {
            return CreateCombatEvent(
                CombatEventType.Invincible,
                target,
                CurrentSkill,
                hitPoint,
                targetHealthDamage: 0,
                targetStabilityDamage: 0,
                sourceStabilityDamage: 0,
                sourceBattleSpiritGain: 0,
                targetInterrupted: false,
                targetShouldReact: false,
                targetUnbalanced: false,
                sourceUnbalanced: false,
                targetDead: false);
        }

        /// <summary>结算弹反分支，恢复目标稳定并对攻击来源施加稳定伤害。</summary>
        private CombatEvent ResolveParry(CombatAbilitySystem target, Vector3 hitPoint)
        {
            SkillConfig skill = CurrentSkill;
            target.m_attributes.RestoreStability(skill.hitConfig.parryStabilityRestore);
            int sourceStabilityDamage = m_attributes.ApplyStabilityDamage(skill.hitConfig.stabilityDamage);
            bool sourceUnbalanced = m_attributes.IsUnbalanced;
            if (sourceUnbalanced)
            {
                CancelActiveAbility();
            }

            return CreateCombatEvent(
                CombatEventType.Parried,
                target,
                skill,
                hitPoint,
                targetHealthDamage: 0,
                targetStabilityDamage: 0,
                sourceStabilityDamage: sourceStabilityDamage,
                sourceBattleSpiritGain: 0,
                targetInterrupted: false,
                targetShouldReact: false,
                targetUnbalanced: false,
                sourceUnbalanced: sourceUnbalanced,
                targetDead: false);
        }

        /// <summary>结算格挡分支，仅对目标施加稳定伤害。</summary>
        private CombatEvent ResolveBlock(CombatAbilitySystem target, Vector3 hitPoint)
        {
            SkillConfig skill = CurrentSkill;
            int defendingStabilityDamage = Mathf.RoundToInt(skill.hitConfig.stabilityDamage * DefendingStabilityDamageRatio);
            int targetStabilityDamage = target.m_attributes.ApplyStabilityDamage(defendingStabilityDamage);
            bool targetUnbalanced = target.m_attributes.IsUnbalanced;
            if (targetUnbalanced)
            {
                target.CancelActiveAbility();
            }

            return CreateCombatEvent(
                CombatEventType.Blocked,
                target,
                skill,
                hitPoint,
                targetHealthDamage: 0,
                targetStabilityDamage: targetStabilityDamage,
                sourceStabilityDamage: 0,
                sourceBattleSpiritGain: 0,
                targetInterrupted: false,
                targetShouldReact: false,
                targetUnbalanced: targetUnbalanced,
                sourceUnbalanced: false,
                targetDead: false);
        }

        /// <summary>结算普通命中，按死亡、失衡、打断、普通受击的优先级确定状态。</summary>
        private CombatEvent ResolveNormalHit(CombatAbilitySystem target, Vector3 hitPoint)
        {
            SkillConfig skill = CurrentSkill;
            // 防御力只抵扣生命伤害，稳定值伤害仍由技能配置独立决定。
            int rawHealthDamage = Mathf.Max(0, Mathf.RoundToInt(m_attributes.Attack * skill.hitConfig.attackMultiplier));
            int minimumHealthDamage = Mathf.CeilToInt(rawHealthDamage * MinimumHealthDamageRatio);
            int attackDamage = Mathf.Max(rawHealthDamage - target.m_attributes.Defense, minimumHealthDamage);
            int targetHealthDamage = target.m_attributes.ApplyHealthDamage(attackDamage);
            int targetStabilityDamage = target.m_attributes.ApplyStabilityDamage(skill.hitConfig.stabilityDamage);
            int sourceBattleSpiritGain = GainBattleSpirit(skill);

            bool targetDead = target.m_attributes.IsDead;
            bool targetUnbalanced = !targetDead && target.m_attributes.IsUnbalanced;
            bool targetInterrupted = !targetDead && !targetUnbalanced && CanInterruptTarget(target, skill);
            bool targetShouldReact = !targetDead && !targetUnbalanced && ShouldPlayNormalHitReaction(target);

            // 状态反应必须只消费最高优先级结果，避免死亡同时进入失衡或受击。
            if (targetDead)
            {
                target.CancelActiveAbility();
            }
            else if (targetUnbalanced)
            {
                target.CancelActiveAbility();
            }
            else if (targetInterrupted)
            {
                target.CancelActiveAbility();
            }

            return CreateCombatEvent(
                CombatEventType.Hit,
                target,
                skill,
                hitPoint,
                targetHealthDamage,
                targetStabilityDamage,
                sourceStabilityDamage: 0,
                sourceBattleSpiritGain: sourceBattleSpiritGain,
                targetInterrupted: targetInterrupted,
                targetShouldReact: targetShouldReact,
                targetUnbalanced: targetUnbalanced,
                sourceUnbalanced: false,
                targetDead: targetDead);
        }

        /// <summary>检查来源技能的打断配置是否压过目标当前技能的抗打断配置。</summary>
        private static bool CanInterruptTarget(CombatAbilitySystem target, SkillConfig sourceSkill)
        {
            InterruptConfig sourceInterrupt = sourceSkill.interruptConfig;
            if (sourceInterrupt == null || !sourceInterrupt.canInterrupt || target.CurrentSkill == null)
            {
                return false;
            }

            if (target.HasTag(CombatTag.Defending) && !sourceInterrupt.canInterruptDefence)
            {
                return false;
            }

            InterruptConfig targetInterrupt = target.CurrentSkill.interruptConfig;
            bool canBeInterrupted = targetInterrupt == null || targetInterrupt.canBeInterrupted;
            int interruptResistLevel = targetInterrupt != null ? targetInterrupt.interruptResistLevel : 0;
            return canBeInterrupted && sourceInterrupt.interruptLevel >= interruptResistLevel;
        }

        /// <summary>检查目标当前技能是否允许进入普通受击状态。</summary>
        private static bool ShouldPlayNormalHitReaction(CombatAbilitySystem target)
        {
            return target.CurrentSkill == null
                || target.CurrentSkill.interruptConfig == null
                || target.CurrentSkill.interruptConfig.canBeInterrupted;
        }

        /// <summary>普通攻击命中时为攻击来源增加配置的战意。</summary>
        private int GainBattleSpirit(SkillConfig skill)
        {
            if (skill.skillType != SkillType.NormalAttack || m_resource == null)
            {
                return 0;
            }

            return m_resource.AddBattleSpirit(skill.battleSpiritGainOnHit);
        }

        /// <summary>统一构建只读命中事件并计算命中方向。</summary>
        private CombatEvent CreateCombatEvent(
            CombatEventType type,
            CombatAbilitySystem target,
            SkillConfig skill,
            Vector3 hitPoint,
            int targetHealthDamage,
            int targetStabilityDamage,
            int sourceStabilityDamage,
            int sourceBattleSpiritGain,
            bool targetInterrupted,
            bool targetShouldReact,
            bool targetUnbalanced,
            bool sourceUnbalanced,
            bool targetDead)
        {
            Vector3 hitDirection = (target.transform.position - transform.position).normalized;
            return new CombatEvent(
                type,
                this,
                target,
                skill,
                targetHealthDamage,
                targetStabilityDamage,
                sourceStabilityDamage,
                sourceBattleSpiritGain,
                targetInterrupted,
                targetShouldReact,
                targetUnbalanced,
                sourceUnbalanced,
                targetDead,
                hitPoint,
                hitDirection);
        }

        /// <summary>检查全部必需标签是否都已存在。</summary>
        private bool HasRequiredTags(CombatTag[] requiredTags)
        {
            for (int i = 0; i < requiredTags.Length; i++)
            {
                if (!HasTag(requiredTags[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>检查标签集合中是否至少有一个当前标签。</summary>
        private bool HasAnyTag(CombatTag[] tags)
        {
            for (int i = 0; i < tags.Length; i++)
            {
                if (HasTag(tags[i]))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>递减限时标签并移除到期项。</summary>
        private void TickTimedTags(float deltaTime)
        {
            m_timedTagBuffer.Clear();
            foreach (CombatTag tag in m_timedTags.Keys)
            {
                m_timedTagBuffer.Add(tag);
            }

            for (int i = 0; i < m_timedTagBuffer.Count; i++)
            {
                CombatTag tag = m_timedTagBuffer[i];
                float remaining = m_timedTags[tag] - deltaTime;
                if (remaining <= 0f)
                {
                    m_timedTags.Remove(tag);
                }
                else
                {
                    m_timedTags[tag] = remaining;
                }
            }
        }

        /// <summary>在等待结束后按每秒恢复量恢复稳定值，失衡状态期间不自动恢复。</summary>
        private void TickStabilityRecovery(float deltaTime)
        {
            if (m_attributes == null
                || m_attributes.IsDead
                || HasTag(CombatTag.Unbalanced)
                || (Faction == CombatFaction.Enemy && m_attributes.IsUnbalanced)
                || m_attributes.Stability >= m_attributes.MaxStability)
            {
                m_stabilityRecoveryAccumulator = 0f;
                return;
            }

            float recoveryTime = deltaTime;
            if (m_stabilityRecoveryDelayRemaining > 0f)
            {
                float consumedDelay = Mathf.Min(m_stabilityRecoveryDelayRemaining, recoveryTime);
                m_stabilityRecoveryDelayRemaining -= consumedDelay;
                recoveryTime -= consumedDelay;
            }

            if (recoveryTime <= 0f)
            {
                return;
            }

            m_stabilityRecoveryAccumulator += recoveryTime * stabilityRecoveryPerSecond;
            int requestedRecovery = Mathf.FloorToInt(m_stabilityRecoveryAccumulator);
            if (requestedRecovery <= 0)
            {
                return;
            }

            int restored = m_attributes.RestoreStability(requestedRecovery);
            m_stabilityRecoveryAccumulator -= restored;
        }

        /// <summary>稳定值受到实际伤害时重新开始恢复等待。</summary>
        private void OnAttributeChanged(CombatAttributeChanged change)
        {
            if (change.Type == CombatAttributeType.Stability && change.Delta < 0)
            {
                ResetStabilityRecovery();
            }
        }

        /// <summary>移除全部限时标签并清空计时记录。</summary>
        private void ClearTimedTags()
        {
            m_timedTags.Clear();
            m_timedTagBuffer.Clear();
        }

    }
}
