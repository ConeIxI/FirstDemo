using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    [UIPanel(UIType.Loading, UILayer.Overlay)]
    public sealed class LoadingPanel : UIPanelBase
    {
        [SerializeField] private TextMeshProUGUI messageText;

        protected override void Awake()
        {
            base.Awake();
            EnsureDefaultView();
            CacheControls();
        }

        public override void OnOpen(object userData)
        {
            base.OnOpen(userData);
            if (messageText != null)
            {
                messageText.text = userData as string ?? "加载中...";
            }
        }

        private void EnsureDefaultView()
        {
            if (transform.childCount > 0)
            {
                return;
            }

            Image background = UIElementFactory.CreateImage("Background", transform, new Color(0.03f, 0.04f, 0.05f, 0.88f));
            UIElementFactory.Stretch(background.rectTransform);

            messageText = UIElementFactory.CreateText("Message", transform, "加载中...", 34, TextAlignmentOptions.Center, UIElementFactory.TextColor);
            messageText.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            messageText.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            messageText.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            messageText.rectTransform.anchoredPosition = Vector2.zero;
            messageText.rectTransform.sizeDelta = new Vector2(500f, 80f);
        }

        private void CacheControls()
        {
            if (messageText == null)
            {
                messageText = transform.Find("Message")?.GetComponent<TextMeshProUGUI>();
            }
        }
    }
}
