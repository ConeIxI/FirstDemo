using TMPro;
using UnityEngine;
using UnityEngine.UI;
using GameMain2.Framework.Audio;

namespace GameMain2.Scripts.UI
{
    [UIPanel(UIType.Settings, UILayer.Popup, blockGameplayInput: true)]
    public sealed class SettingsPanel : UIPanelBase
    {
        private const string FullScreenKey = "UI_FullScreen";

        [SerializeField] private Slider masterVolumeSlider;
        [SerializeField] private Slider bgmVolumeSlider;
        [SerializeField] private Slider sfxVolumeSlider;
        [SerializeField] private Toggle fullScreenToggle;
        [SerializeField] private Button closeButton;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        /// <summary>
        /// 启动时仅恢复显示设置，不再直接写入旧的全局音量。
        /// </summary>
        private static void ApplySavedDisplaySettingsOnStart()
        {
            if (PlayerPrefs.HasKey(FullScreenKey))
            {
                Screen.fullScreen = PlayerPrefs.GetInt(FullScreenKey, Screen.fullScreen ? 1 : 0) == 1;
            }
        }

        /// <summary>
        /// 初始化设置面板的默认视图并缓存控件引用。
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            EnsureDefaultView();
            CacheControls();
        }

        /// <summary>
        /// 打开面板时刷新三路音量与全屏状态，并绑定事件。
        /// </summary>
        public override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            RefreshValues();
            BindControls();
        }

        /// <summary>
        /// 关闭面板前解除所有临时事件绑定。
        /// </summary>
        public override void OnClose()
        {
            UnbindControls();
            base.OnClose();
        }

        /// <summary>
        /// 从声音管理器和系统状态刷新界面控件值。
        /// </summary>
        private void RefreshValues()
        {
            SoundManager soundManager = SoundManager.Instance;
            if (masterVolumeSlider != null)
            {
                masterVolumeSlider.SetValueWithoutNotify(soundManager.MasterVolume);
            }

            if (bgmVolumeSlider != null)
            {
                bgmVolumeSlider.SetValueWithoutNotify(soundManager.BgmVolume);
            }

            if (sfxVolumeSlider != null)
            {
                sfxVolumeSlider.SetValueWithoutNotify(soundManager.SfxVolume);
            }

            if (fullScreenToggle != null)
            {
                fullScreenToggle.SetIsOnWithoutNotify(Screen.fullScreen);
            }
        }

        /// <summary>
        /// 绑定三路音量、全屏和关闭按钮事件。
        /// </summary>
        private void BindControls()
        {
            if (masterVolumeSlider != null)
            {
                masterVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);
                masterVolumeSlider.onValueChanged.AddListener(OnMasterVolumeChanged);
            }

            if (bgmVolumeSlider != null)
            {
                bgmVolumeSlider.onValueChanged.RemoveListener(OnBgmVolumeChanged);
                bgmVolumeSlider.onValueChanged.AddListener(OnBgmVolumeChanged);
            }

            if (sfxVolumeSlider != null)
            {
                sfxVolumeSlider.onValueChanged.RemoveListener(OnSfxVolumeChanged);
                sfxVolumeSlider.onValueChanged.AddListener(OnSfxVolumeChanged);
            }

            if (fullScreenToggle != null)
            {
                fullScreenToggle.onValueChanged.RemoveListener(OnFullScreenChanged);
                fullScreenToggle.onValueChanged.AddListener(OnFullScreenChanged);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(OnCloseClicked);
                closeButton.onClick.AddListener(OnCloseClicked);
            }
        }

        /// <summary>
        /// 解除三路音量、全屏和关闭按钮事件，避免重复触发。
        /// </summary>
        private void UnbindControls()
        {
            if (masterVolumeSlider != null)
            {
                masterVolumeSlider.onValueChanged.RemoveListener(OnMasterVolumeChanged);
            }

            if (bgmVolumeSlider != null)
            {
                bgmVolumeSlider.onValueChanged.RemoveListener(OnBgmVolumeChanged);
            }

            if (sfxVolumeSlider != null)
            {
                sfxVolumeSlider.onValueChanged.RemoveListener(OnSfxVolumeChanged);
            }

            if (fullScreenToggle != null)
            {
                fullScreenToggle.onValueChanged.RemoveListener(OnFullScreenChanged);
            }

            if (closeButton != null)
            {
                closeButton.onClick.RemoveListener(OnCloseClicked);
            }
        }

        /// <summary>
        /// 写入主音量并同步到声音管理器。
        /// </summary>
        private void OnMasterVolumeChanged(float value)
        {
            SoundManager.Instance.SetMasterVolume(value);
        }

        /// <summary>
        /// 写入背景音乐音量并同步到声音管理器。
        /// </summary>
        private void OnBgmVolumeChanged(float value)
        {
            SoundManager.Instance.SetBgmVolume(value);
        }

        /// <summary>
        /// 写入音效音量并同步到声音管理器。
        /// </summary>
        private void OnSfxVolumeChanged(float value)
        {
            SoundManager.Instance.SetSfxVolume(value);
        }

        /// <summary>
        /// 写入全屏状态并持久化用户选择。
        /// </summary>
        private void OnFullScreenChanged(bool value)
        {
            Screen.fullScreen = value;
            PlayerPrefs.SetInt(FullScreenKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 请求 UI 管理器关闭设置面板。
        /// </summary>
        private void OnCloseClicked()
        {
            UIManager.Instance.ClosePanel(UIType.Settings);
        }

        /// <summary>
        /// 在预制体为空时创建设置面板的默认控件层级。
        /// </summary>
        private void EnsureDefaultView()
        {
            if (transform.childCount > 0)
            {
                return;
            }

            Image overlay = UIElementFactory.CreateImage("Overlay", transform, new Color(0f, 0f, 0f, 0.35f));
            UIElementFactory.Stretch(overlay.rectTransform);

            RectTransform card = UIElementFactory.CreateRect("SettingsCard", transform);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = new Vector2(560f, 420f);
            Image cardImage = card.gameObject.AddComponent<Image>();
            cardImage.color = UIElementFactory.BlockColor;

            TextMeshProUGUI title = UIElementFactory.CreateText("Title", card, "设置", 40, TextAlignmentOptions.Center, UIElementFactory.TextColor);
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -62f), new Vector2(460f, 60f));

            TextMeshProUGUI masterVolumeLabel = UIElementFactory.CreateText("MasterVolumeLabel", card, "主音量", 22, TextAlignmentOptions.Left, UIElementFactory.TextColor);
            SetRect(masterVolumeLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-120f, -140f), new Vector2(160f, 34f));

            masterVolumeSlider = UIElementFactory.CreateSlider("MasterVolumeSlider", card);
            SetRect(masterVolumeSlider.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(80f, -140f), new Vector2(300f, 28f));

            TextMeshProUGUI bgmVolumeLabel = UIElementFactory.CreateText("BgmVolumeLabel", card, "音乐", 22, TextAlignmentOptions.Left, UIElementFactory.TextColor);
            SetRect(bgmVolumeLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-120f, -205f), new Vector2(160f, 34f));

            bgmVolumeSlider = UIElementFactory.CreateSlider("BgmVolumeSlider", card);
            SetRect(bgmVolumeSlider.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(80f, -205f), new Vector2(300f, 28f));

            TextMeshProUGUI sfxVolumeLabel = UIElementFactory.CreateText("SfxVolumeLabel", card, "音效", 22, TextAlignmentOptions.Left, UIElementFactory.TextColor);
            SetRect(sfxVolumeLabel.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(-120f, -270f), new Vector2(160f, 34f));

            sfxVolumeSlider = UIElementFactory.CreateSlider("SfxVolumeSlider", card);
            SetRect(sfxVolumeSlider.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(80f, -270f), new Vector2(300f, 28f));

            fullScreenToggle = UIElementFactory.CreateToggle("FullScreenToggle", card, "全屏");
            SetRect(fullScreenToggle.GetComponent<RectTransform>(), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -340f), new Vector2(360f, 36f));

            closeButton = UIElementFactory.CreateButton("CloseButton", card, "关闭");
            SetRect(closeButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 68f), new Vector2(280f, 54f));
        }

        /// <summary>
        /// 从既有预制体层级缓存三路音量、全屏和关闭控件。
        /// </summary>
        private void CacheControls()
        {
            if (masterVolumeSlider == null)
            {
                masterVolumeSlider = transform.Find("SettingsCard/MasterVolumeSlider")?.GetComponent<Slider>();
            }

            if (bgmVolumeSlider == null)
            {
                bgmVolumeSlider = transform.Find("SettingsCard/BgmVolumeSlider")?.GetComponent<Slider>();
            }

            if (sfxVolumeSlider == null)
            {
                sfxVolumeSlider = transform.Find("SettingsCard/SfxVolumeSlider")?.GetComponent<Slider>();
            }

            if (fullScreenToggle == null)
            {
                fullScreenToggle = transform.Find("SettingsCard/FullScreenToggle")?.GetComponent<Toggle>();
            }

            if (closeButton == null)
            {
                closeButton = transform.Find("SettingsCard/CloseButton")?.GetComponent<Button>();
            }
        }

        /// <summary>
        /// 设置矩形控件的锚点、位置和尺寸。
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
