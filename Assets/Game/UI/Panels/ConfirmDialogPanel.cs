using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    [UIPanel(UIType.ConfirmDialog, UILayer.Popup, blockGameplayInput: true)]
    public sealed class ConfirmDialogPanel : UIPanelBase
    {
        [SerializeField] private TextMeshProUGUI titleText;
        [SerializeField] private TextMeshProUGUI messageText;
        [SerializeField] private TextMeshProUGUI confirmButtonText;
        [SerializeField] private TextMeshProUGUI cancelButtonText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private Button cancelButton;

        private UIConfirmData m_data;

        protected override void Awake()
        {
            base.Awake();
            EnsureDefaultView();
            CacheControls();
        }

        public override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            m_data = userData as UIConfirmData ?? new UIConfirmData("确认", string.Empty, null);
            RefreshText();
            BindButtons();
        }

        public override void OnClose()
        {
            UnbindButtons();
            base.OnClose();
        }

        private void RefreshText()
        {
            if (titleText != null)
            {
                titleText.text = m_data.Title;
            }

            if (messageText != null)
            {
                messageText.text = m_data.Message;
            }

            if (confirmButtonText != null)
            {
                confirmButtonText.text = m_data.ConfirmText;
            }

            if (cancelButtonText != null)
            {
                cancelButtonText.text = m_data.CancelText;
            }
        }

        private void BindButtons()
        {
            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(OnConfirmClicked);
                confirmButton.onClick.AddListener(OnConfirmClicked);
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.RemoveListener(OnCancelClicked);
                cancelButton.onClick.AddListener(OnCancelClicked);
            }
        }

        private void UnbindButtons()
        {
            if (confirmButton != null)
            {
                confirmButton.onClick.RemoveListener(OnConfirmClicked);
            }

            if (cancelButton != null)
            {
                cancelButton.onClick.RemoveListener(OnCancelClicked);
            }
        }

        private void OnConfirmClicked()
        {
            UIConfirmData data = m_data;
            UIManager.Instance.ClosePanel(UIType.ConfirmDialog);
            data?.OnConfirm?.Invoke();
        }

        private void OnCancelClicked()
        {
            UIConfirmData data = m_data;
            UIManager.Instance.ClosePanel(UIType.ConfirmDialog);
            data?.OnCancel?.Invoke();
        }

        private void EnsureDefaultView()
        {
            if (transform.childCount > 0)
            {
                return;
            }

            Image overlay = UIElementFactory.CreateImage("Overlay", transform, new Color(0f, 0f, 0f, 0.45f));
            UIElementFactory.Stretch(overlay.rectTransform);

            RectTransform card = UIElementFactory.CreateRect("DialogCard", transform);
            card.anchorMin = new Vector2(0.5f, 0.5f);
            card.anchorMax = new Vector2(0.5f, 0.5f);
            card.pivot = new Vector2(0.5f, 0.5f);
            card.anchoredPosition = Vector2.zero;
            card.sizeDelta = new Vector2(520f, 300f);
            Image cardImage = card.gameObject.AddComponent<Image>();
            cardImage.color = UIElementFactory.BlockColor;

            titleText = UIElementFactory.CreateText("Title", card, "确认", 34, TextAlignmentOptions.Center, UIElementFactory.TextColor);
            SetRect(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -54f), new Vector2(450f, 54f));

            messageText = UIElementFactory.CreateText("Message", card, string.Empty, 22, TextAlignmentOptions.Center, UIElementFactory.MutedTextColor);
            SetRect(messageText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 18f), new Vector2(430f, 96f));

            confirmButton = UIElementFactory.CreateButton("ConfirmButton", card, "确定");
            cancelButton = UIElementFactory.CreateButton("CancelButton", card, "取消");
            SetRect(confirmButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-105f, 56f), new Vector2(170f, 50f));
            SetRect(cancelButton.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(105f, 56f), new Vector2(170f, 50f));

            confirmButtonText = confirmButton.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            cancelButtonText = cancelButton.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
        }

        private void CacheControls()
        {
            if (titleText == null)
            {
                titleText = transform.Find("DialogCard/Title")?.GetComponent<TextMeshProUGUI>();
            }

            if (messageText == null)
            {
                messageText = transform.Find("DialogCard/Message")?.GetComponent<TextMeshProUGUI>();
            }

            if (confirmButton == null)
            {
                confirmButton = transform.Find("DialogCard/ConfirmButton")?.GetComponent<Button>();
            }

            if (cancelButton == null)
            {
                cancelButton = transform.Find("DialogCard/CancelButton")?.GetComponent<Button>();
            }

            if (confirmButtonText == null && confirmButton != null)
            {
                confirmButtonText = confirmButton.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            }

            if (cancelButtonText == null && cancelButton != null)
            {
                cancelButtonText = cancelButton.transform.Find("Label")?.GetComponent<TextMeshProUGUI>();
            }
        }

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
