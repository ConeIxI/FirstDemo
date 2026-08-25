using UnityEngine;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    [UIPanel(UIType.Victory, UILayer.Popup, blockGameplayInput: true)]
    public sealed class VictoryPanel : UIPanelBase
    {
        [SerializeField] private Button mainMenuButton;

        /// <summary>初始化胜利面板组件并缓存预制体按钮引用。</summary>
        protected override void Awake()
        {
            base.Awake();
            CacheControls();
        }

        /// <summary>打开胜利面板时绑定返回主菜单按钮。</summary>
        public override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            BindButtons();
        }

        /// <summary>关闭胜利面板时解绑按钮点击事件。</summary>
        public override void OnClose()
        {
            UnbindButtons();
            base.OnClose();
        }

        /// <summary>为胜利面板按钮绑定点击回调。</summary>
        private void BindButtons()
        {
            mainMenuButton.onClick.RemoveListener(OnMainMenuClicked);
            mainMenuButton.onClick.AddListener(OnMainMenuClicked);
        }

        /// <summary>为胜利面板按钮解绑点击回调。</summary>
        private void UnbindButtons()
        {
            if (mainMenuButton != null)
            {
                mainMenuButton.onClick.RemoveListener(OnMainMenuClicked);
            }
        }

        /// <summary>点击返回主菜单按钮后恢复时间流速并进入主菜单场景。</summary>
        private void OnMainMenuClicked()
        {
            Time.timeScale = 1f;
            SceneFlowManager.Instance.ReturnToMainMenu();
        }

        /// <summary>缓存胜利面板预制体内的返回主菜单按钮引用。</summary>
        private void CacheControls()
        {
            if (mainMenuButton == null)
            {
                mainMenuButton = transform.Find("VictoryCard/MainMenuButton")?.GetComponent<Button>();
            }
        }
    }
}
