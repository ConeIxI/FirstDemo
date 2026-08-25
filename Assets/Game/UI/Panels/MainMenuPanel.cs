using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    [UIPanel(UIType.MainMenu)]
    public sealed class MainMenuPanel : UIPanelBase
    {
        [SerializeField] private Button startGameButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button quitButton;

        /// <summary>
        /// 初始化主菜单默认视图并缓存按钮引用。
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            EnsureDefaultView();
            CacheControls();
        }

        /// <summary>
        /// 打开主菜单时绑定按钮点击事件。
        /// </summary>
        public override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            BindButtons();
        }

        /// <summary>
        /// 关闭主菜单时解绑按钮点击事件。
        /// </summary>
        public override void OnClose()
        {
            UnbindButtons();
            base.OnClose();
        }

        /// <summary>
        /// 为主菜单按钮绑定点击回调。
        /// </summary>
        private void BindButtons()
        {
            if (startGameButton != null)
            {
                startGameButton.onClick.RemoveListener(OnStartGameClicked);
                startGameButton.onClick.AddListener(OnStartGameClicked);
            }

            if (settingsButton != null)
            {
                settingsButton.onClick.RemoveListener(OnSettingsClicked);
                settingsButton.onClick.AddListener(OnSettingsClicked);
            }

            if (quitButton != null)
            {
                quitButton.onClick.RemoveListener(OnQuitClicked);
                quitButton.onClick.AddListener(OnQuitClicked);
            }
        }

        /// <summary>
        /// 为主菜单按钮解绑点击回调。
        /// </summary>
        private void UnbindButtons()
        {
            if (startGameButton != null)
            {
                startGameButton.onClick.RemoveListener(OnStartGameClicked);
            }

            if (settingsButton != null)
            {
                settingsButton.onClick.RemoveListener(OnSettingsClicked);
            }

            if (quitButton != null)
            {
                quitButton.onClick.RemoveListener(OnQuitClicked);
            }
        }

        /// <summary>
        /// 点击开始游戏按钮后进入战斗场景。
        /// </summary>
        private void OnStartGameClicked()
        {
            SceneFlowManager.Instance.LoadScene(SceneNames.BattleScene);
        }

        /// <summary>
        /// 点击设置按钮后打开设置面板。
        /// </summary>
        private void OnSettingsClicked()
        {
            UIManager.Instance.ShowSettings();
        }

        /// <summary>
        /// 点击退出按钮后弹出退出确认框。
        /// </summary>
        private void OnQuitClicked()
        {
            UIManager.Instance.ShowConfirm("退出游戏", "确定要退出游戏吗？", UIManager.Instance.QuitGame);
        }

        /// <summary>
        /// 在空白预制体下运行时生成主菜单默认结构。
        /// </summary>
        private void EnsureDefaultView()
        {
            if (transform.childCount > 0)
            {
                return;
            }

            Image background = UIElementFactory.CreateImage("Background", transform, UIElementFactory.PanelColor);
            UIElementFactory.Stretch(background.rectTransform);

            RectTransform card = UIElementFactory.CreateRect("MenuCard", transform);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = new Vector2(560f, 560f);

            Image cardImage = card.gameObject.AddComponent<Image>();
            cardImage.color = UIElementFactory.BlockColor;

            TextMeshProUGUI title = UIElementFactory.CreateText(
                "Title",
                card,
                "First Game Demo",
                48,
                TextAlignmentOptions.Center,
                UIElementFactory.TextColor);
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -86f), new Vector2(500f, 80f));

            TextMeshProUGUI subtitle = UIElementFactory.CreateText(
                "Subtitle",
                card,
                "UI 系统第一版",
                24,
                TextAlignmentOptions.Center,
                UIElementFactory.MutedTextColor);
            SetRect(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -145f), new Vector2(500f, 44f));

            startGameButton = UIElementFactory.CreateButton("StartGameButton", card, "开始游戏");
            settingsButton = UIElementFactory.CreateButton("SettingsButton", card, "设置");
            quitButton = UIElementFactory.CreateButton("QuitButton", card, "退出游戏");

            SetButton(startGameButton, -240f);
            SetButton(settingsButton, -318f);
            SetButton(quitButton, -396f);
        }

        /// <summary>
        /// 在运行时或编辑器复用场景内已存在的按钮引用。
        /// </summary>
        private void CacheControls()
        {
            if (startGameButton == null)
            {
                startGameButton = transform.Find("MenuCard/StartGameButton")?.GetComponent<Button>();
            }

            if (settingsButton == null)
            {
                settingsButton = transform.Find("MenuCard/SettingsButton")?.GetComponent<Button>();
            }

            if (quitButton == null)
            {
                quitButton = transform.Find("MenuCard/QuitButton")?.GetComponent<Button>();
            }
        }

        /// <summary>
        /// 统一设置主菜单按钮的锚点和尺寸。
        /// </summary>
        private static void SetButton(Button button, float y)
        {
            if (button == null)
            {
                return;
            }

            SetRect(
                button.GetComponent<RectTransform>(),
                new Vector2(0.5f, 1f),
                new Vector2(0.5f, 1f),
                new Vector2(0f, y),
                new Vector2(360f, 58f));
        }

        /// <summary>
        /// 统一设置运行时生成控件的 RectTransform 参数。
        /// </summary>
        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }
    }
}
