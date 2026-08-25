using UnityEngine;

namespace GameMain2.Scripts.UI
{
    [RequireComponent(typeof(RectTransform))]
    public abstract class UIPanelBase : MonoBehaviour
    {
        private CanvasGroup m_canvasGroup;

        public UIType Type { get; private set; }
        public UIManager Owner { get; private set; }

        public virtual UILayer Layer
        {
            get { return UILayer.Normal; }
        }

        protected virtual void Awake()
        {
            m_canvasGroup = GetComponent<CanvasGroup>();
            if (m_canvasGroup == null)
            {
                m_canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            UIElementFactory.Stretch(GetComponent<RectTransform>());
        }

        internal void Bind(UIType type, UIManager owner)
        {
            Type = type;
            Owner = owner;
        }

        public virtual void OnOpen(object userData)
        {
            SetVisible(true);
        }

        public virtual void OnClose()
        {
            SetVisible(false);
        }

        protected void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
            if (m_canvasGroup == null)
            {
                m_canvasGroup = GetComponent<CanvasGroup>();
            }

            if (m_canvasGroup != null)
            {
                m_canvasGroup.alpha = visible ? 1f : 0f;
                m_canvasGroup.interactable = visible;
                m_canvasGroup.blocksRaycasts = visible;
            }
        }
    }
}
