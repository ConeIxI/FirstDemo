using System;
using System.Collections.Generic;
using Game.Battle.Ability;
using Game.Battle.Skill.Effects;
using GameMain2.Framework.Manager;
using UnityEngine;

namespace Game.Battle.Buff
{
    public sealed class CombatBuffController : MonoBehaviour
    {
        private const string MissingAttributesError = "CombatBuffController 缺少 ICombatAttributes，组件已禁用。";
        private readonly Dictionary<int, ActiveCombatBuff> m_activeBuffs = new Dictionary<int, ActiveCombatBuff>();
        private readonly List<int> m_removeBuffer = new List<int>();
        private ICombatAttributes m_attributes;
        private CombatAbilitySystem m_abilitySystem;
        private Func<int, CombatBuffConfig> m_configResolver;

        /// <summary>初始化 Buff 控制器依赖。</summary>
        private void Awake()
        {
            InitializeDependencies();
        }

        /// <summary>组件重新启用时恢复仍在持续中的 Buff 特效。</summary>
        private void OnEnable()
        {
            RestartBuffEffects();
        }

        /// <summary>组件禁用时停止当前对象名下的 Buff 持续特效。</summary>
        private void OnDisable()
        {
            StopBuffEffects();
        }

        /// <summary>按帧推进 Buff 生命周期。</summary>
        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>添加 Buff；同 Id 已存在时只刷新持续时间。</summary>
        public bool AddBuff(int buffId)
        {
            CombatBuffConfig config = ResolveConfig(buffId);
            if (config == null)
            {
                Debug.LogError($"未找到 Buff 配置：{buffId}");
                return false;
            }

            ActiveCombatBuff activeBuff;
            if (m_activeBuffs.TryGetValue(buffId, out activeBuff))
            {
                activeBuff.Refresh();
                return true;
            }

            PlayBuffEffect(buffId, config);
            m_activeBuffs.Add(buffId, new ActiveCombatBuff(config));
            return true;
        }

        /// <summary>移除指定 Buff，返回是否真的移除。</summary>
        public bool RemoveBuff(int buffId)
        {
            return RemoveActiveBuff(buffId);
        }

        /// <summary>检查当前对象是否持有指定 Buff。</summary>
        public bool HasBuff(int buffId)
        {
            return m_activeBuffs.ContainsKey(buffId);
        }

        /// <summary>清空当前对象全部 Buff。</summary>
        public void ClearBuffs()
        {
            StopBuffEffects();
            m_activeBuffs.Clear();
            m_removeBuffer.Clear();
        }

        /// <summary>计算 Buff 修正后的攻击力。</summary>
        public int CalculateAttack(int baseAttack)
        {
            return CalculateModifiedAttribute(baseAttack, CombatBuffType.AttackModifier);
        }

        /// <summary>计算 Buff 修正后的防御力。</summary>
        public int CalculateDefense(int baseDefense)
        {
            return CalculateModifiedAttribute(baseDefense, CombatBuffType.DefenseModifier);
        }

        /// <summary>按传入时间推进 Buff 计时和持续生命效果。</summary>
        public void Tick(float deltaTime)
        {
            m_removeBuffer.Clear();
            foreach (KeyValuePair<int, ActiveCombatBuff> pair in m_activeBuffs)
            {
                ActiveCombatBuff activeBuff = pair.Value;
                activeBuff.Tick(deltaTime, m_attributes);
                if (activeBuff.IsExpired)
                {
                    m_removeBuffer.Add(pair.Key);
                }
            }

            for (int i = 0; i < m_removeBuffer.Count; i++)
            {
                RemoveActiveBuff(m_removeBuffer[i]);
            }
        }

        /// <summary>解析同对象属性组件，配置查询在真正添加 Buff 时懒加载。</summary>
        private void InitializeDependencies()
        {
            m_attributes = GetComponent<ICombatAttributes>();
            m_abilitySystem = GetComponent<CombatAbilitySystem>();
            if (m_attributes == null)
            {
                Debug.LogError(MissingAttributesError);
                enabled = false;
                return;
            }
        }

        /// <summary>查询 Buff 配置。</summary>
        private CombatBuffConfig ResolveConfig(int buffId)
        {
            if (m_configResolver == null)
            {
                m_configResolver = ConfigManager.Instance.GetBuffConfig;
            }

            return m_configResolver(buffId);
        }

        /// <summary>移除运行时 Buff，并同步停止该 Buff 的持续特效。</summary>
        private bool RemoveActiveBuff(int buffId)
        {
            ActiveCombatBuff activeBuff;
            if (!m_activeBuffs.TryGetValue(buffId, out activeBuff))
            {
                return false;
            }

            StopBuffEffect(buffId, activeBuff.Config);
            m_activeBuffs.Remove(buffId);
            return true;
        }

        /// <summary>按 Buff 配置播放持续特效，空特效配置保持纯数值 Buff 行为。</summary>
        private void PlayBuffEffect(int buffId, CombatBuffConfig config)
        {
            if (string.IsNullOrEmpty(config.activeEffectId))
            {
                return;
            }

            if (m_abilitySystem == null)
            {
                throw new InvalidOperationException($"Buff{buffId}配置了持续特效但对象缺少 CombatAbilitySystem");
            }

            if (CombatEffectService.Instance == null)
            {
                throw new InvalidOperationException($"Buff{buffId}配置了持续特效但场景缺少 CombatEffectService");
            }

            CombatEffectPlayContext context = CombatEffectPlayContext.ForBuff(
                config.activeEffectId,
                ResolveBuffEffectChannel(buffId),
                m_abilitySystem,
                this);
            CombatEffectService.Instance.Play(context);
        }

        /// <summary>停止指定 Buff 的持续特效通道。</summary>
        private void StopBuffEffect(int buffId, CombatBuffConfig config)
        {
            if (string.IsNullOrEmpty(config.activeEffectId) || CombatEffectService.Instance == null)
            {
                return;
            }

            CombatEffectService.Instance.StopOwnerChannel(this, ResolveBuffEffectChannel(buffId));
        }

        /// <summary>停止当前对象名下所有 Buff 持续特效。</summary>
        private void StopBuffEffects()
        {
            foreach (KeyValuePair<int, ActiveCombatBuff> pair in m_activeBuffs)
            {
                StopBuffEffect(pair.Key, pair.Value.Config);
            }
        }

        /// <summary>恢复当前仍存在的 Buff 持续特效，供对象重新启用时重新挂载表现。</summary>
        private void RestartBuffEffects()
        {
            foreach (KeyValuePair<int, ActiveCombatBuff> pair in m_activeBuffs)
            {
                PlayBuffEffect(pair.Key, pair.Value.Config);
            }
        }

        /// <summary>生成 Buff 持续特效独占通道名，保证同对象同 Buff 只有一个循环特效。</summary>
        private static string ResolveBuffEffectChannel(int buffId)
        {
            return $"Buff_{buffId}";
        }

        /// <summary>按 Buff 类型计算固定值和百分比修正后的属性。</summary>
        private int CalculateModifiedAttribute(int baseValue, CombatBuffType type)
        {
            int flatBonus = 0;
            float percentBonus = 0f;
            foreach (ActiveCombatBuff activeBuff in m_activeBuffs.Values)
            {
                if (activeBuff.Config.type != type)
                {
                    continue;
                }

                flatBonus += activeBuff.Config.flatValue;
                percentBonus += activeBuff.Config.percentValue;
            }

            return Mathf.Max(0, Mathf.RoundToInt(baseValue * (1f + percentBonus) + flatBonus));
        }

        private sealed class ActiveCombatBuff
        {
            public CombatBuffConfig Config { get; }
            public bool IsExpired => RemainingTime <= 0f;
            private float RemainingTime { get; set; }
            private float TickRemainingTime { get; set; }

            /// <summary>创建运行时 Buff，并等待一个 Tick 间隔后首次触发生命效果。</summary>
            public ActiveCombatBuff(CombatBuffConfig config)
            {
                Config = config;
                Refresh();
            }

            /// <summary>刷新持续时间和 Tick 等待时间。</summary>
            public void Refresh()
            {
                RemainingTime = Config.duration;
                TickRemainingTime = Config.tickInterval;
            }

            /// <summary>推进运行时 Buff 时间并触发生命 Tick。</summary>
            public void Tick(float deltaTime, ICombatAttributes attributes)
            {
                RemainingTime -= deltaTime;
                if (Config.type != CombatBuffType.HealthRegen && Config.type != CombatBuffType.HealthDamage)
                {
                    return;
                }

                TickRemainingTime -= deltaTime;
                while (TickRemainingTime <= 0f && RemainingTime >= 0f)
                {
                    ApplyHealthTick(attributes);
                    TickRemainingTime += Config.tickInterval;
                }
            }

            /// <summary>按 Buff 类型执行一次生命恢复或扣除。</summary>
            private void ApplyHealthTick(ICombatAttributes attributes)
            {
                if (Config.type == CombatBuffType.HealthRegen)
                {
                    attributes.RestoreHealth(Config.tickValue);
                }
                else if (Config.type == CombatBuffType.HealthDamage)
                {
                    attributes.ApplyHealthDamage(Config.tickValue);
                }
            }
        }
    }
}
