using Game.Battle.Ability;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 战斗 HUD 左上角状态视图，负责玩家资源条、名称和武器信息显示。
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public sealed class BattleHudStatusView : MonoBehaviour
    {
        private const float ResourceFlashDuration = 0.22f;

        private static readonly Color HealthBaseColor = new Color(0.78f, 0.16f, 0.16f, 1f);
        private static readonly Color StabilityBaseColor = new Color(0.18f, 0.62f, 0.34f, 1f);
        private static readonly Color BattleSpiritBaseColor = new Color(0.84f, 0.52f, 0.18f, 1f);
        private static readonly Color ResourceGainFlashColor = new Color(1f, 0.92f, 0.35f, 1f);
        private static readonly Color ResourceLossFlashColor = new Color(1f, 0.35f, 0.28f, 1f);

        [SerializeField] private Image healthFill;
        [SerializeField] private Image stabilityFill;
        [SerializeField] private Image battleSpiritFill;
        [SerializeField] private TextMeshProUGUI healthLabel;
        [SerializeField] private TextMeshProUGUI stabilityLabel;
        [SerializeField] private TextMeshProUGUI battleSpiritLabel;

        private int m_lastHealth = -1;
        private int m_lastStability = -1;
        private int m_lastBattleSpirit = -1;
        private float m_healthFlashTimer;
        private float m_stabilityFlashTimer;
        private float m_battleSpiritFlashTimer;
        private Color m_healthFlashColor = HealthBaseColor;
        private Color m_stabilityFlashColor = StabilityBaseColor;
        private Color m_battleSpiritFlashColor = BattleSpiritBaseColor;

        /// <summary>
        /// 初始化状态视图反馈状态，显示结构完全由 prefab 提前配置。
        /// </summary>
        public void Init()
        {
            ResetFeedbackTracking();
        }

        /// <summary>
        /// 显示状态视图。
        /// </summary>
        public void Show()
        {
            gameObject.SetActive(true);
        }

        /// <summary>
        /// 隐藏状态视图。
        /// </summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 释放状态视图，目前没有事件订阅需要解绑。
        /// </summary>
        public void Dispose()
        {
        }

        /// <summary>
        /// 主动刷新三条资源条，通常在 HUD 打开或重新绑定玩家时调用。
        /// </summary>
        public void RefreshAll(CombatAttributeSet attributes)
        {
            if (attributes == null)
            {
                return;
            }

            RefreshHealth(new CombatAttributeChanged(CombatAttributeType.Health, attributes.Health, attributes.MaxHealth, 0));
            RefreshStability(new CombatAttributeChanged(CombatAttributeType.Stability, attributes.Stability, attributes.MaxStability, 0));
            RefreshBattleSpirit(new CombatAttributeChanged(CombatAttributeType.BattleSpirit, attributes.BattleSpirit, attributes.MaxBattleSpirit, 0));
        }

        /// <summary>
        /// 根据属性变化刷新对应资源条。
        /// </summary>
        public void Refresh(CombatAttributeChanged change)
        {
            switch (change.Type)
            {
                case CombatAttributeType.Health:
                    RefreshHealth(change);
                    break;
                case CombatAttributeType.Stability:
                    RefreshStability(change);
                    break;
                case CombatAttributeType.BattleSpirit:
                    RefreshBattleSpirit(change);
                    break;
            }
        }

        /// <summary>
        /// 推进三条资源的闪烁计时。
        /// </summary>
        public void TickFlash()
        {
            TickFlash(healthFill, HealthBaseColor, ref m_healthFlashTimer, m_healthFlashColor);
            TickFlash(stabilityFill, StabilityBaseColor, ref m_stabilityFlashTimer, m_stabilityFlashColor);
            TickFlash(battleSpiritFill, BattleSpiritBaseColor, ref m_battleSpiritFlashTimer, m_battleSpiritFlashColor);
        }

        /// <summary>
        /// 重置三条资源的历史值和闪烁反馈。
        /// </summary>
        public void ResetFeedbackTracking()
        {
            ResetTrackedResource(HealthBaseColor, ref m_lastHealth, ref m_healthFlashTimer, ref m_healthFlashColor);
            ResetTrackedResource(StabilityBaseColor, ref m_lastStability, ref m_stabilityFlashTimer, ref m_stabilityFlashColor);
            ResetTrackedResource(BattleSpiritBaseColor, ref m_lastBattleSpirit, ref m_battleSpiritFlashTimer, ref m_battleSpiritFlashColor);
        }

        /// <summary>
        /// 刷新生命条并更新生命变化反馈。
        /// </summary>
        private void RefreshHealth(CombatAttributeChanged change)
        {
            RefreshTrackedBar(healthFill, healthLabel, "生命", change, ref m_lastHealth,
                ref m_healthFlashTimer, ref m_healthFlashColor, HealthBaseColor, true);
        }

        /// <summary>
        /// 刷新稳定条并更新稳定变化反馈。
        /// </summary>
        private void RefreshStability(CombatAttributeChanged change)
        {
            RefreshTrackedBar(stabilityFill, stabilityLabel, "稳定", change, ref m_lastStability,
                ref m_stabilityFlashTimer, ref m_stabilityFlashColor, StabilityBaseColor, false);
        }

        /// <summary>
        /// 刷新战意条并更新战意变化反馈。
        /// </summary>
        private void RefreshBattleSpirit(CombatAttributeChanged change)
        {
            RefreshTrackedBar(battleSpiritFill, battleSpiritLabel, "战意", change, ref m_lastBattleSpirit,
                ref m_battleSpiritFlashTimer, ref m_battleSpiritFlashColor, BattleSpiritBaseColor, true);
        }

        /// <summary>
        /// 刷新玩家资源条，检测数值增减并按配置驱动短暂颜色闪烁。
        /// </summary>
        private static void RefreshTrackedBar(
            Image fill,
            TextMeshProUGUI label,
            string title,
            CombatAttributeChanged change,
            ref int lastCurrent,
            ref float flashTimer,
            ref Color flashColor,
            Color baseColor,
            bool flashWhenIncrease)
        {
            if (fill == null)
            {
                ResetTrackedResource(baseColor, ref lastCurrent, ref flashTimer, ref flashColor);
                RefreshBar(fill, label, title, change.Current, change.Max, baseColor);
                return;
            }

            if (lastCurrent < 0)
            {
                lastCurrent = change.Current;
                flashTimer = 0f;
                flashColor = baseColor;
            }
            else if (change.Current != lastCurrent)
            {
                bool isIncrease = change.Current > lastCurrent;
                lastCurrent = change.Current;
                if (isIncrease && !flashWhenIncrease)
                {
                    flashTimer = 0f;
                    flashColor = baseColor;
                }
                else
                {
                    flashTimer = ResourceFlashDuration;
                    flashColor = isIncrease ? ResourceGainFlashColor : ResourceLossFlashColor;
                }
            }

            Color resolvedColor = ResolveFeedbackColor(baseColor, flashColor, flashTimer);
            RefreshBar(fill, label, title, change.Current, change.Max, resolvedColor);
        }

        /// <summary>
        /// 推进单条资源的闪烁计时并更新填充颜色。
        /// </summary>
        private static void TickFlash(Image fill, Color baseColor, ref float flashTimer, Color flashColor)
        {
            if (flashTimer <= 0f)
            {
                return;
            }

            flashTimer = Mathf.Max(0f, flashTimer - Time.unscaledDeltaTime);
            if (fill != null)
            {
                fill.color = ResolveFeedbackColor(baseColor, flashColor, flashTimer);
            }
        }

        /// <summary>
        /// 重置单个资源条的历史值与闪烁状态。
        /// </summary>
        private static void ResetTrackedResource(Color baseColor, ref int lastCurrent, ref float flashTimer, ref Color flashColor)
        {
            lastCurrent = -1;
            flashTimer = 0f;
            flashColor = baseColor;
        }

        /// <summary>
        /// 根据反馈剩余时间计算资源条颜色。
        /// </summary>
        private static Color ResolveFeedbackColor(Color baseColor, Color flashColor, float flashTimer)
        {
            if (flashTimer <= 0f)
            {
                return baseColor;
            }

            float t = Mathf.Clamp01(flashTimer / ResourceFlashDuration);
            return Color.Lerp(baseColor, flashColor, t);
        }

        /// <summary>
        /// 写入资源条填充比例、颜色和标签文本。
        /// </summary>
        private static void RefreshBar(Image fill, TextMeshProUGUI label, string title, int current, int max, Color fillColor)
        {
            if (fill != null)
            {
                if (fill.type != Image.Type.Filled)
                {
                    fill.type = Image.Type.Filled;
                    fill.fillMethod = Image.FillMethod.Horizontal;
                    fill.fillOrigin = 0;
                }

                fill.fillAmount = max <= 0 ? 0f : Mathf.Clamp01((float)current / max);
                fill.color = fillColor;
            }

            if (label != null)
            {
                label.text = title + " " + current + " / " + max;
            }
        }
    }
}
