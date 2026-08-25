using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    [UIPanel(UIType.Toast, UILayer.Toast)]
    public sealed class ToastPanel : UIPanelBase
    {
        [SerializeField] private TextMeshProUGUI messageText;
        private Coroutine m_hideCoroutine;

        protected override void Awake()
        {
            base.Awake();
            EnsureDefaultView();
            CacheControls();
        }

        public override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            ToastData data = userData as ToastData ?? new ToastData(userData as string, 2f);
            if (messageText != null)
            {
                messageText.text = data.Message;
            }

            if (m_hideCoroutine != null)
            {
                StopCoroutine(m_hideCoroutine);
            }

            m_hideCoroutine = StartCoroutine(HideAfterDelay(data.Duration));
        }

        public override void OnClose()
        {
            if (m_hideCoroutine != null)
            {
                StopCoroutine(m_hideCoroutine);
                m_hideCoroutine = null;
            }

            base.OnClose();
        }

        private IEnumerator HideAfterDelay(float duration)
        {
            yield return new WaitForSecondsRealtime(duration);
            UIManager.Instance.ClosePanel(UIType.Toast);
            m_hideCoroutine = null;
        }

        private void EnsureDefaultView()
        {
            if (transform.childCount > 0)
            {
                return;
            }

            RectTransform card = UIElementFactory.CreateRect("ToastCard", transform);
            card.anchorMin = new Vector2(0.5f, 1f);
            card.anchorMax = new Vector2(0.5f, 1f);
            card.pivot = new Vector2(0.5f, 1f);
            card.anchoredPosition = new Vector2(0f, -72f);
            card.sizeDelta = new Vector2(520f, 64f);
            Image image = card.gameObject.AddComponent<Image>();
            image.color = new Color(0.08f, 0.10f, 0.12f, 0.92f);

            messageText = UIElementFactory.CreateText("Message", card, string.Empty, 22, TextAlignmentOptions.Center, UIElementFactory.TextColor);
            UIElementFactory.Stretch(messageText.rectTransform);
            messageText.rectTransform.offsetMin = new Vector2(24f, 0f);
            messageText.rectTransform.offsetMax = new Vector2(-24f, 0f);
        }

        private void CacheControls()
        {
            if (messageText == null)
            {
                messageText = transform.Find("ToastCard/Message")?.GetComponent<TextMeshProUGUI>();
            }
        }
    }
}
