using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    [UIPanel(UIType.Pause, UILayer.Popup, blockGameplayInput: true)]
    [UIShortcut(KeyCode.Escape, SceneNames.BattleScene, true, true)]
    [UIShortcut(KeyCode.Escape, SceneNames.BossScene, true, true)]
    public sealed class PausePanel : UIPanelBase
    {
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button mainMenuButton;
        [SerializeField] private Button quitButton;

        /// <summary>
        /// 初始化暂停菜单默认视图并缓存按钮引用。
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            EnsureDefaultView();
            CacheControls();
        }

        /// <summary>
        /// 打开暂停菜单时绑定按钮点击事件。
        /// </summary>
        public override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            BindButtons();
        }

        /// <summary>
        /// 关闭暂停菜单时解绑按钮点击事件。
        /// </summary>
        public override void OnClose()
        {
            UnbindButtons();
            base.OnClose();
        }

        /// <summary>
        /// 为暂停菜单按钮绑定点击回调。
        /// </summary>
        private void BindButtons()
        {
            if (resumeButton != null)
            {
                resumeButton.onClick.RemoveListener(OnResumeClicked);
                resumeButton.onClick.AddListener(OnResumeClicked);
            }

            if (settingsButton != null)
            {
                settingsButton.onClick.RemoveListener(OnSettingsClicked);
                settingsButton.onClick.AddListener(OnSettingsClicked);
            }

            if (mainMenuButton != null)
            {
                mainMenuButton.onClick.RemoveListener(OnMainMenuClicked);
                mainMenuButton.onClick.AddListener(OnMainMenuClicked);
            }

            if (quitButton != null)
            {
                quitButton.onClick.RemoveListener(OnQuitClicked);
                quitButton.onClick.AddListener(OnQuitClicked);
            }
        }

        /// <summary>
        /// 为暂停菜单按钮解绑点击回调。
        /// </summary>
        private void UnbindButtons()
        {
            if (resumeButton != null)
            {
                resumeButton.onClick.RemoveListener(OnResumeClicked);
            }

            if (settingsButton != null)
            {
                settingsButton.onClick.RemoveListener(OnSettingsClicked);
            }

            if (mainMenuButton != null)
            {
                mainMenuButton.onClick.RemoveListener(OnMainMenuClicked);
            }

            if (quitButton != null)
            {
                quitButton.onClick.RemoveListener(OnQuitClicked);
            }
        }

        /// <summary>
        /// 点击继续按钮后恢复游戏。
        /// </summary>
        private void OnResumeClicked()
        {
            UIManager.Instance.ResumeGame();
        }

        /// <summary>
        /// 点击设置按钮后打开设置面板。
        /// </summary>
        private void OnSettingsClicked()
        {
            UIManager.Instance.ShowSettings();
        }

        /// <summary>
        /// 点击返回主菜单按钮后弹出确认框并执行场景切换。
        /// </summary>
        private void OnMainMenuClicked()
        {
            UIManager.Instance.ShowConfirm("返回主菜单", "当前进度不会保存，确定返回主菜单吗？", SceneFlowManager.Instance.ReturnToMainMenu);
        }

        /// <summary>
        /// 点击退出按钮后弹出退出确认框。
        /// </summary>
        private void OnQuitClicked()
        {
            UIManager.Instance.ShowConfirm("退出游戏", "确定要退出游戏吗？", UIManager.Instance.QuitGame);
        }

        /// <summary>
        /// 在空白预制体下运行时生成暂停菜单默认结构。
        /// </summary>
        private void EnsureDefaultView()
        {
            if (transform.childCount > 0)
            {
                return;
            }

            Image overlay = UIElementFactory.CreateImage("Overlay", transform, new Color(0f, 0f, 0f, 0.48f));
            UIElementFactory.Stretch(overlay.rectTransform);

            RectTransform card = UIElementFactory.CreateRect("PauseCard", transform);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = new Vector2(460f, 500f);
            Image cardImage = card.gameObject.AddComponent<Image>();
            cardImage.color = UIElementFactory.BlockColor;

            TextMeshProUGUI title = UIElementFactory.CreateText("Title", card, "暂停", 44, TextAlignmentOptions.Center, UIElementFactory.TextColor);
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(400f, 64f));

            resumeButton = UIElementFactory.CreateButton("ResumeButton", card, "继续游戏");
            settingsButton = UIElementFactory.CreateButton("SettingsButton", card, "设置");
            mainMenuButton = UIElementFactory.CreateButton("MainMenuButton", card, "返回主菜单");
            quitButton = UIElementFactory.CreateButton("QuitButton", card, "退出游戏");

            SetButton(resumeButton, -165f);
            SetButton(settingsButton, -235f);
            SetButton(mainMenuButton, -305f);
            SetButton(quitButton, -375f);
        }

        /// <summary>
        /// 在运行时或编辑器复用场景内已存在的按钮引用。
        /// </summary>
        private void CacheControls()
        {
            if (resumeButton == null)
            {
                resumeButton = transform.Find("PauseCard/ResumeButton")?.GetComponent<Button>();
            }

            if (settingsButton == null)
            {
                settingsButton = transform.Find("PauseCard/SettingsButton")?.GetComponent<Button>();
            }

            if (mainMenuButton == null)
            {
                mainMenuButton = transform.Find("PauseCard/MainMenuButton")?.GetComponent<Button>();
            }

            if (quitButton == null)
            {
                quitButton = transform.Find("PauseCard/QuitButton")?.GetComponent<Button>();
            }
        }

        /// <summary>
        /// 统一设置暂停菜单按钮的锚点和尺寸。
        /// </summary>
        private static void SetButton(Button button, float y)
        {
            SetRect(button.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(320f, 54f));
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
