using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    /// <summary>
    /// 拾取成功提示面板，负责道具提示的排队、合并和播放动效。
    /// </summary>
    [UIPanel(UIType.PickupTip, UILayer.Toast)]
    public sealed class PickupTipPanel : UIPanelBase
    {
        private const float TotalDuration = 4f;
        private const float SlideDuration = 0.25f;
        private const float FadeDuration = 0.55f;
        private const float StayDuration = TotalDuration - SlideDuration - FadeDuration;

        private static readonly Vector2 HiddenPosition = new Vector2(520f, 0f);
        private static readonly Vector2 VisiblePosition = new Vector2(-72f, 0f);

        [SerializeField] private RectTransform cardRect;
        [SerializeField] private CanvasGroup cardCanvasGroup;
        [SerializeField] private Image iconImage;
        [SerializeField] private TextMeshProUGUI nameText;
        [SerializeField] private TextMeshProUGUI countText;

        private readonly List<PickupTipData> m_waitingTips = new List<PickupTipData>();
        private Coroutine m_playCoroutine;
        private bool m_isPlaying;

        /// <summary>
        /// 初始化运行时兜底视图并缓存显示控件。
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            CacheControls();
            ValidateControls();
            ResetView();
        }

        /// <summary>
        /// 接收新的拾取提示数据，当前播放中时只进入等待队列。
        /// </summary>
        public override void OnOpen(object userData)
        {
            base.OnOpen(userData);

            PickupTipData data = (PickupTipData)userData;
            if (m_isPlaying)
            {
                EnqueueOrMerge(data);
                return;
            }

            m_playCoroutine = StartCoroutine(PlayRoutine(data));
        }

        /// <summary>
        /// 关闭面板时停止播放并清空等待队列。
        /// </summary>
        public override void OnClose()
        {
            if (m_playCoroutine != null)
            {
                StopCoroutine(m_playCoroutine);
                m_playCoroutine = null;
            }

            m_waitingTips.Clear();
            m_isPlaying = false;
            ResetView();
            base.OnClose();
        }

        /// <summary>
        /// 缓存预制体中的拾取提示控件引用。
        /// </summary>
        private void CacheControls()
        {
            if (cardRect == null)
            {
                cardRect = transform.Find("PickupTipCard") as RectTransform;
            }

            if (cardCanvasGroup == null && cardRect != null)
            {
                cardCanvasGroup = cardRect.GetComponent<CanvasGroup>();
            }

            if (iconImage == null)
            {
                iconImage = transform.Find("PickupTipCard/IconFrame/Icon")?.GetComponent<Image>();
            }

            if (nameText == null)
            {
                nameText = transform.Find("PickupTipCard/Name")?.GetComponent<TextMeshProUGUI>();
            }

            if (countText == null)
            {
                countText = transform.Find("PickupTipCard/CountBox/Count")?.GetComponent<TextMeshProUGUI>();
            }
        }

        /// <summary>
        /// 校验预制体必须提供完整控件引用，避免静默退回代码生成 UI。
        /// </summary>
        private void ValidateControls()
        {
            if (cardRect == null
                || cardCanvasGroup == null
                || iconImage == null
                || nameText == null
                || countText == null)
            {
                throw new Exception("PickupTipPanel 预制体控件引用不完整，请检查 UI/PickupTipPanel。");
            }
        }

        /// <summary>
        /// 把新提示加入等待队列，队列中已有相同道具时只累计数量。
        /// </summary>
        private void EnqueueOrMerge(PickupTipData data)
        {
            for (int i = 0; i < m_waitingTips.Count; i++)
            {
                PickupTipData waitingTip = m_waitingTips[i];
                if (waitingTip.IsSameItem(data))
                {
                    waitingTip.AddCount(data.Count);
                    return;
                }
            }

            m_waitingTips.Add(data);
        }

        /// <summary>
        /// 顺序播放当前提示和等待队列中的后续提示。
        /// </summary>
        private IEnumerator PlayRoutine(PickupTipData firstData)
        {
            m_isPlaying = true;
            PickupTipData currentData = firstData;

            while (currentData != null)
            {
                BindData(currentData);
                yield return SlideInRoutine();
                yield return new WaitForSecondsRealtime(StayDuration);
                yield return FadeOutRoutine();

                if (m_waitingTips.Count == 0)
                {
                    currentData = null;
                    continue;
                }

                currentData = m_waitingTips[0];
                m_waitingTips.RemoveAt(0);
            }

            m_isPlaying = false;
            m_playCoroutine = null;
            Owner.ClosePanel(UIType.PickupTip);
        }

        /// <summary>
        /// 把拾取提示数据写入图标、名称和数量控件。
        /// </summary>
        private void BindData(PickupTipData data)
        {
            if (iconImage != null)
            {
                iconImage.sprite = data.Icon;
                iconImage.color = data.Icon == null ? new Color(0.23f, 0.26f, 0.30f, 1f) : Color.white;
            }

            if (nameText != null)
            {
                nameText.text = data.Name;
            }

            if (countText != null)
            {
                countText.text = $"x{data.Count}";
            }
        }

        /// <summary>
        /// 播放从屏幕右侧滑入到目标位置的动效。
        /// </summary>
        private IEnumerator SlideInRoutine()
        {
            SetCardAlpha(1f);
            float elapsed = 0f;
            while (elapsed < SlideDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / SlideDuration);
                SetCardPosition(Vector2.Lerp(HiddenPosition, VisiblePosition, EaseOut(progress)));
                yield return null;
            }

            SetCardPosition(VisiblePosition);
        }

        /// <summary>
        /// 播放原地淡出动效。
        /// </summary>
        private IEnumerator FadeOutRoutine()
        {
            SetCardPosition(VisiblePosition);
            float elapsed = 0f;
            while (elapsed < FadeDuration)
            {
                elapsed += Time.unscaledDeltaTime;
                float progress = Mathf.Clamp01(elapsed / FadeDuration);
                SetCardAlpha(1f - progress);
                yield return null;
            }

            SetCardAlpha(0f);
            SetCardPosition(HiddenPosition);
        }

        /// <summary>
        /// 重置面板到下一次播放前的隐藏状态。
        /// </summary>
        private void ResetView()
        {
            SetCardAlpha(0f);
            SetCardPosition(HiddenPosition);
        }

        /// <summary>
        /// 写入卡片透明度。
        /// </summary>
        private void SetCardAlpha(float alpha)
        {
            if (cardCanvasGroup != null)
            {
                cardCanvasGroup.alpha = alpha;
            }
        }

        /// <summary>
        /// 写入卡片锚点位置。
        /// </summary>
        private void SetCardPosition(Vector2 position)
        {
            if (cardRect != null)
            {
                cardRect.anchoredPosition = position;
            }
        }

        /// <summary>
        /// 计算滑入阶段的缓出进度。
        /// </summary>
        private static float EaseOut(float progress)
        {
            return 1f - (1f - progress) * (1f - progress);
        }
    }
}
