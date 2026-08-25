using UnityEngine;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    [UIPanel(UIType.Death, UILayer.Popup, blockGameplayInput: true)]
    public sealed class DeathPanel : UIPanelBase
    {
        [SerializeField] private Button mainMenuButton;
        [SerializeField] private Button restartButton;

        /// <summary>初始化死亡面板组件并缓存预制体按钮引用。</summary>
        protected override void Awake()
        {
            base.Awake();
            CacheControls();
        }

        /// <summary>打开死亡面板时绑定按钮点击事件。</summary>
        public override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            BindButtons();
        }

        /// <summary>关闭死亡面板时解绑按钮点击事件。</summary>
        public override void OnClose()
        {
            UnbindButtons();
            base.OnClose();
        }

        /// <summary>为死亡面板按钮绑定点击回调。</summary>
        private void BindButtons()
        {
            mainMenuButton.onClick.RemoveListener(OnMainMenuClicked);
            mainMenuButton.onClick.AddListener(OnMainMenuClicked);
            restartButton.onClick.RemoveListener(OnRestartClicked);
            restartButton.onClick.AddListener(OnRestartClicked);
        }

        /// <summary>为死亡面板按钮解绑点击回调。</summary>
        private void UnbindButtons()
        {
            if (mainMenuButton != null)
            {
                mainMenuButton.onClick.RemoveListener(OnMainMenuClicked);
            }

            if (restartButton != null)
            {
                restartButton.onClick.RemoveListener(OnRestartClicked);
            }
        }

        /// <summary>点击返回主菜单按钮后进入主菜单场景。</summary>
        private void OnMainMenuClicked()
        {
            Time.timeScale = 1f;
            SceneFlowManager.Instance.ReturnToMainMenu();
        }

        /// <summary>点击重新开始按钮后重建 Gobal 并重新加载当前场景。</summary>
        private void OnRestartClicked()
        {
            Time.timeScale = 1f;
            SceneFlowManager.Instance.RestartCurrentScene();
        }

        /// <summary>缓存死亡面板预制体内的按钮引用。</summary>
        private void CacheControls()
        {
            if (restartButton == null)
            {
                restartButton = transform.Find("DeathCard/RestartButton")?.GetComponent<Button>();
            }

            if (mainMenuButton == null)
            {
                mainMenuButton = transform.Find("DeathCard/MainMenuButton")?.GetComponent<Button>();
            }
        }
    }
}
