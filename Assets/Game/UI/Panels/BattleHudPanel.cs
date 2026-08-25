using Game.Battle.Ability;
using Game.Character.Equipment;
using TMPro;
using UnityEngine;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 战斗 HUD 面板，负责组合 HUD View、绑定玩家战斗属性并分发刷新事件。
    /// </summary>
    [UIPanel(UIType.BattleHud)]
    public sealed class BattleHudPanel : UIPanelBase
    {
        private const string MissingPlayerAbilitySystemError = "BattleHudPanel 未找到玩家 CombatAbilitySystem，组件已禁用。";
        private const string MissingPlayerAttributesError = "BattleHudPanel 未找到玩家 CombatAttributeSet，组件已禁用。";

        [SerializeField] private BattleHudStatusView statusView;
        [SerializeField] private BattleHudConsumableSlotsView consumableSlotsView;
        [SerializeField] private BattleHudSkillSlotsView playerSkillSlotsView;
        [SerializeField] private TextMeshProUGUI crosshairText;

        private CombatAbilitySystem m_playerAbilitySystem;
        private CombatAttributeSet m_playerAttributes;
        /// <summary>
        /// 打开 HUD 时初始化子 View 并绑定玩家属性事件。
        /// </summary>
        public override void OnOpen(object userData)
        {
            EnsureDefaultView();
            BindPlayerAttributes();
            base.OnOpen(userData);
        }

        /// <summary>
        /// 关闭 HUD 时解除玩家属性事件订阅并隐藏子 View。
        /// </summary>
        public override void OnClose()
        {
            UnbindPlayerAttributes();
            HideViews();
            base.OnClose();
        }

        /// <summary>
        /// 销毁 HUD 时释放子 View 生命周期。
        /// </summary>
        private void OnDestroy()
        {
            UnbindPlayerAttributes();
            DisposeViews();
        }

        /// <summary>
        /// 逐帧推进状态 View 的资源闪烁反馈，不轮询战斗属性。
        /// </summary>
        private void Update()
        {
            if (statusView != null)
            {
                statusView.TickFlash();
            }
        }

        /// <summary>
        /// 外部恢复玩家装备后刷新 HUD 中的武器和技能槽图标。
        /// </summary>
        public void RefreshEquipmentSlots()
        {
            BindPlayerAttributes();
            if (playerSkillSlotsView != null)
            {
                playerSkillSlotsView.RefreshCurrentWeapon();
            }
        }

        /// <summary>
        /// 确保 HUD 默认 View 引用和中心准星存在。
        /// </summary>
        private void EnsureDefaultView()
        {
            EnsureViews();
            ShowViews();
            EnsureCrosshair();
            DisableLegacyElement("ControlHint");
        }

        /// <summary>
        /// 查找或补齐三个 HUD 子 View。
        /// </summary>
        private void EnsureViews()
        {
            if (statusView == null)
            {
                statusView = FindOrCreateView<BattleHudStatusView>("StatusPanel");
            }

            if (consumableSlotsView == null)
            {
                consumableSlotsView = FindOrCreateView<BattleHudConsumableSlotsView>("ConsumableSlots");
            }

            if (playerSkillSlotsView == null)
            {
                playerSkillSlotsView = FindOrCreateView<BattleHudSkillSlotsView>("PlayerSkillSlots");
            }

            if (statusView != null)
            {
                statusView.Init();
            }

            if (consumableSlotsView != null)
            {
                consumableSlotsView.Init();
            }

            if (playerSkillSlotsView != null)
            {
                playerSkillSlotsView.Init();
            }
        }

        /// <summary>
        /// 显示全部 HUD 子 View。
        /// </summary>
        private void ShowViews()
        {
            if (statusView != null)
            {
                statusView.Show();
            }

            if (consumableSlotsView != null)
            {
                consumableSlotsView.Show();
            }

            if (playerSkillSlotsView != null)
            {
                playerSkillSlotsView.Show();
            }
        }

        /// <summary>
        /// 隐藏全部 HUD 子 View。
        /// </summary>
        private void HideViews()
        {
            if (statusView != null)
            {
                statusView.Hide();
            }

            if (consumableSlotsView != null)
            {
                consumableSlotsView.Hide();
            }

            if (playerSkillSlotsView != null)
            {
                playerSkillSlotsView.Hide();
            }
        }

        /// <summary>
        /// 释放全部 HUD 子 View。
        /// </summary>
        private void DisposeViews()
        {
            if (statusView != null)
            {
                statusView.Dispose();
            }

            if (consumableSlotsView != null)
            {
                consumableSlotsView.Dispose();
            }

            if (playerSkillSlotsView != null)
            {
                playerSkillSlotsView.Dispose();
            }
        }

        /// <summary>
        /// 查找指定名称的 View 节点，缺失时创建兜底节点并挂载 View 组件。
        /// </summary>
        private T FindOrCreateView<T>(string childName) where T : Component
        {
            Transform existing = transform.Find(childName);
            if (existing == null)
            {
                RectTransform rect = UIElementFactory.CreateRect(childName, transform);
                existing = rect;
            }

            T view = existing.GetComponent<T>();
            if (view == null)
            {
                view = existing.gameObject.AddComponent<T>();
            }

            return view;
        }

        /// <summary>
        /// 确保屏幕中心准星存在并设置布局。
        /// </summary>
        private void EnsureCrosshair()
        {
            if (crosshairText == null)
            {
                Transform existing = transform.Find("Crosshair");
                if (existing != null)
                {
                    crosshairText = existing.GetComponent<TextMeshProUGUI>();
                }
            }

            if (crosshairText == null)
            {
                crosshairText = UIElementFactory.CreateText(
                    "Crosshair",
                    transform,
                    "+",
                    32,
                    TextAlignmentOptions.Center,
                    new Color(1f, 1f, 1f, 0.72f));
            }

            crosshairText.text = "+";
            crosshairText.fontSize = 32;
            crosshairText.alignment = TextAlignmentOptions.Center;
            crosshairText.color = new Color(1f, 1f, 1f, 0.72f);
            crosshairText.raycastTarget = false;
            SetRect(crosshairText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(48f, 48f));
        }

        /// <summary>
        /// 停用旧版底部提示，避免和新槽位布局重叠。
        /// </summary>
        private void DisableLegacyElement(string name)
        {
            Transform legacy = transform.Find(name);
            if (legacy != null)
            {
                legacy.gameObject.SetActive(false);
            }
        }

        /// <summary>
        /// 绑定当前场景中的玩家能力系统和属性组件。
        /// </summary>
        private void BindPlayerAttributes()
        {
            UnbindPlayerAttributes();
            m_playerAbilitySystem = FindPlayerAbilitySystem();
            if (m_playerAbilitySystem == null)
            {
                DisableWithError(MissingPlayerAbilitySystemError);
                return;
            }

            m_playerAttributes = m_playerAbilitySystem.GetComponent<CombatAttributeSet>();
            if (m_playerAttributes == null)
            {
                DisableWithError(MissingPlayerAttributesError);
                return;
            }

            if (playerSkillSlotsView != null)
            {
                playerSkillSlotsView.BindEquipmentManager(m_playerAbilitySystem.GetComponent<EquipmentManager>());
            }

            if (statusView != null)
            {
                statusView.ResetFeedbackTracking();
                statusView.RefreshAll(m_playerAttributes);
            }

            m_playerAttributes.AttributeChanged += OnAttributeChanged;
        }

        /// <summary>
        /// 解除当前玩家属性事件并清空绑定引用。
        /// </summary>
        private void UnbindPlayerAttributes()
        {
            if (playerSkillSlotsView != null)
            {
                playerSkillSlotsView.BindEquipmentManager(null);
            }

            if (m_playerAttributes != null)
            {
                m_playerAttributes.AttributeChanged -= OnAttributeChanged;
            }

            m_playerAbilitySystem = null;
            m_playerAttributes = null;
        }

        /// <summary>
        /// 记录 HUD 配置错误并禁用逐帧更新。
        /// </summary>
        private void DisableWithError(string message)
        {
            Debug.LogError(message, this);
            enabled = false;
        }

        /// <summary>
        /// 根据属性变化分发给状态 View 刷新显示。
        /// </summary>
        private void OnAttributeChanged(CombatAttributeChanged change)
        {
            if (statusView != null)
            {
                statusView.Refresh(change);
            }
        }

        /// <summary>
        /// 从 Player 标签对象中查找玩家能力系统。
        /// </summary>
        private static CombatAbilitySystem FindPlayerAbilitySystem()
        {
            GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
            for (int i = 0; i < players.Length; i++)
            {
                GameObject player = players[i];
                if (player == null)
                {
                    continue;
                }

                CombatAbilitySystem abilitySystem = player.GetComponent<CombatAbilitySystem>();
                if (abilitySystem != null)
                {
                    return abilitySystem;
                }
            }

            return null;
        }

        /// <summary>
        /// 写入 UI 矩形的锚点、位置和尺寸。
        /// </summary>
        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }
    }
}
