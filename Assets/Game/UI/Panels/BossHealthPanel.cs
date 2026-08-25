using System;
using Game.Battle.Ability;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    [UIPanel(UIType.BossHealth, UILayer.Normal, "UI/BossHealthPanel")]
    public sealed class BossHealthPanel : UIPanelBase
    {
        [SerializeField] private TextMeshProUGUI bossNameText;
        [SerializeField] private Image healthFillImage;
        [SerializeField] private Image stabilityFillImage;

        private ICombatAttributes m_attributes;

        /// <summary>初始化 Boss 血条面板并缓存预制体中的 UI 引用。</summary>
        protected override void Awake()
        {
            base.Awake();
            CacheControls();
        }

        /// <summary>打开 Boss 血条时绑定 Boss 属性并立即刷新当前生命值和稳定值。</summary>
        public override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            BossHealthPanelData data = userData as BossHealthPanelData;
            if (data == null || data.Attributes == null)
            {
                throw new InvalidOperationException("BossHealthPanel 打开时缺少 BossHealthPanelData 或 Boss 属性。");
            }

            Bind(data.Attributes);
            SetBossName(data.BossName);
            RefreshAll();
        }

        /// <summary>关闭 Boss 血条时解除属性事件订阅，避免继续监听旧 Boss。</summary>
        public override void OnClose()
        {
            Unbind();
            base.OnClose();
        }

        /// <summary>缓存 prefab 中的文本和血条图片引用。</summary>
        private void CacheControls()
        {
            if (bossNameText == null)
            {
                bossNameText = transform.Find("Root/BossNameText")?.GetComponent<TextMeshProUGUI>();
            }

            if (healthFillImage == null)
            {
                healthFillImage = transform.Find("Root/HealthBarBackground/HealthBarFill")?.GetComponent<Image>();
            }

            if (stabilityFillImage == null)
            {
                stabilityFillImage = transform.Find("Root/StabilityBarBackground/StabilityBarFill")?.GetComponent<Image>();
            }
        }

        /// <summary>绑定 Boss 战斗属性事件，同一属性源不会重复订阅。</summary>
        private void Bind(ICombatAttributes attributes)
        {
            if (m_attributes == attributes)
            {
                return;
            }

            Unbind();
            m_attributes = attributes;
            m_attributes.AttributeChanged += OnAttributeChanged;
        }

        /// <summary>解除当前 Boss 战斗属性事件订阅。</summary>
        private void Unbind()
        {
            if (m_attributes == null)
            {
                return;
            }

            m_attributes.AttributeChanged -= OnAttributeChanged;
            m_attributes = null;
        }

        /// <summary>刷新 Boss 名称显示，未配置名称时使用默认标题。</summary>
        private void SetBossName(string bossName)
        {
            if (bossNameText != null)
            {
                bossNameText.text = string.IsNullOrWhiteSpace(bossName) ? "Boss" : bossName;
            }
        }

        /// <summary>按当前 Boss 属性刷新全部血条显示。</summary>
        private void RefreshAll()
        {
            RefreshHealth(m_attributes.Health, m_attributes.MaxHealth);
            RefreshStability(m_attributes.Stability, m_attributes.MaxStability);
        }

        /// <summary>收到 Boss 属性变化时刷新对应资源条，并在死亡后关闭面板。</summary>
        private void OnAttributeChanged(CombatAttributeChanged change)
        {
            switch (change.Type)
            {
                case CombatAttributeType.Health:
                    RefreshHealth(change.Current, change.Max);
                    if (change.Current <= 0)
                    {
                        Owner.ClosePanel(UIType.BossHealth);
                    }
                    break;
                case CombatAttributeType.Stability:
                    RefreshStability(change.Current, change.Max);
                    break;
            }
        }

        /// <summary>刷新 Boss 生命条比例，不显示具体数值。</summary>
        private void RefreshHealth(int current, int max)
        {
            SetBarAmount(healthFillImage, current, max);
        }

        /// <summary>刷新 Boss 稳定值条比例，不显示具体数值。</summary>
        private void RefreshStability(int current, int max)
        {
            SetBarAmount(stabilityFillImage, current, max);
        }

        /// <summary>把资源当前值换算为 0 到 1 的比例并写入图片填充量。</summary>
        private static void SetBarAmount(Image fillImage, int current, int max)
        {
            if (fillImage == null)
            {
                return;
            }

            fillImage.fillAmount = max <= 0 ? 0f : Mathf.Clamp01((float)current / max);
        }
    }

    public sealed class BossHealthPanelData
    {
        public ICombatAttributes Attributes { get; }
        public string BossName { get; }

        /// <summary>创建 Boss 血条打开参数，包含属性源和显示名称。</summary>
        public BossHealthPanelData(ICombatAttributes attributes, string bossName)
        {
            Attributes = attributes;
            BossName = bossName;
        }
    }
}
