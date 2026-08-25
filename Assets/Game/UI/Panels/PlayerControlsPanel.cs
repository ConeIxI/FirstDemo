using UnityEngine;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    [UIPanel(UIType.PlayerControls, UILayer.Popup, blockGameplayInput: true)]
    public sealed class PlayerControlsPanel : UIPanelBase
    {
        [SerializeField] private Button confirmButton;

        /// <summary>
        /// 初始化面板并缓存确认按钮引用。
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            CacheControls();
        }

        /// <summary>
        /// 打开按键提示面板时绑定确认按钮事件。
        /// </summary>
        public override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            BindControls();
        }

        /// <summary>
        /// 关闭按键提示面板时解除确认按钮事件。
        /// </summary>
        public override void OnClose()
        {
            UnbindControls();
            base.OnClose();
        }

        /// <summary>
        /// 从预制体层级缓存确认按钮引用。
        /// </summary>
        private void CacheControls()
        {
            if (confirmButton == null)
            {
                confirmButton = transform.Find("DialogCard/ConfirmButton")?.GetComponent<Button>();
            }
        }

        /// <summary>
        /// 绑定确认按钮点击事件。
        /// </summary>
        private void BindControls()
        {
            if (confirmButton == null)
            {
                return;
            }

            confirmButton.onClick.RemoveListener(OnConfirmClicked);
            confirmButton.onClick.AddListener(OnConfirmClicked);
        }

        /// <summary>
        /// 解除确认按钮点击事件，避免面板缓存后重复响应。
        /// </summary>
        private void UnbindControls()
        {
            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(OnConfirmClicked);
            }
        }

        /// <summary>
        /// 点击确认按钮后关闭按键提示面板。
        /// </summary>
        private void OnConfirmClicked()
        {
            UIManager.Instance.ClosePanel(UIType.PlayerControls);
        }
    }
}
