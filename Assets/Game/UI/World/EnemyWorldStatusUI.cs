using System.Collections;
using Game.Battle.Ability;
using Game.Character.Enemy.AI;
using Game.Character.Enemy.Components;
using UnityEngine;
using UnityEngine.UI;

namespace GameMain2.Scripts.UI
{
    public sealed class EnemyWorldStatusUI : MonoBehaviour
    {
        [SerializeField] private EnemyAttributeComponent attribute;
        [SerializeField] private AIController aiController;
        [SerializeField] private Image hpBar;
        [SerializeField] private Image mpBar;
        [SerializeField] private float fadeInDuration = 0.15f;
        [SerializeField] private float fadeOutDuration = 0.25f;

        private EnemyAttributeComponent m_boundAttribute;
        private Canvas[] m_canvases;
        private CanvasGroup[] m_canvasGroups;
        private Camera m_mainCamera;
        private float m_fadeAlpha;

        /// <summary>唤醒时缓存子 Canvas 和已有 CanvasGroup，后续只驱动透明度与渲染开关。</summary>
        private void Awake()
        {
            m_canvases = GetComponentsInChildren<Canvas>(true);
            m_canvasGroups = GetComponentsInChildren<CanvasGroup>(true);
            m_fadeAlpha = m_canvasGroups.Length > 0 ? m_canvasGroups[0].alpha : 0f;
        }

        /// <summary>启用时订阅敌人属性变化，并立即刷新当前显示。</summary>
        private void OnEnable()
        {
            BindAttribute();
            RefreshAll();
            SnapFadeToTargetState();
        }

        /// <summary>等待敌人初始化完成后一帧，再同步一次初始血量和稳定值。</summary>
        private IEnumerator Start()
        {
            yield return null;
            RefreshAll();
        }

        /// <summary>禁用时解除属性事件订阅，避免重复回调。</summary>
        private void OnDisable()
        {
            UnbindAttribute();
        }

        /// <summary>所有敌人逻辑更新后，根据当前目标记忆同步血条显隐和朝向。</summary>
        private void LateUpdate()
        {
            RefreshVisibilityAndFacing();
        }

        /// <summary>根据敌人是否仍以玩家为目标，同步 UI 显隐并在显示时朝向主摄像机。</summary>
        private void RefreshVisibilityAndFacing()
        {
            bool hasRememberedTarget = HasRememberedTarget();
            TickFade(hasRememberedTarget);
            if (m_fadeAlpha > 0f)
            {
                FaceMainCamera();
            }
        }

        /// <summary>判断敌人仍存活且黑板中仍保留玩家目标，包括当前可见和记忆中的目标。</summary>
        private bool HasRememberedTarget()
        {
            return attribute != null
                && !attribute.IsDead
                && aiController != null
                && aiController.Blackboard != null
                && aiController.Blackboard.Target != null;
        }

        /// <summary>启用时按当前目标状态直接同步淡入淡出值，避免开局无目标时闪一下。</summary>
        private void SnapFadeToTargetState()
        {
            m_fadeAlpha = HasRememberedTarget() ? 1f : 0f;
            ApplyFadeAlpha();
            SetCanvasesEnabled(m_fadeAlpha > 0f);
        }

        /// <summary>每帧将 CanvasGroup 透明度向目标显示状态推进。</summary>
        private void TickFade(bool shouldShow)
        {
            if (shouldShow)
            {
                SetCanvasesEnabled(true);
            }

            float targetAlpha = shouldShow ? 1f : 0f;
            float duration = shouldShow ? fadeInDuration : fadeOutDuration;
            if (duration <= 0f)
            {
                m_fadeAlpha = targetAlpha;
            }
            else
            {
                m_fadeAlpha = Mathf.MoveTowards(m_fadeAlpha, targetAlpha, Time.deltaTime / duration);
            }

            ApplyFadeAlpha();

            if (!shouldShow && m_fadeAlpha <= 0f)
            {
                SetCanvasesEnabled(false);
            }
        }

        /// <summary>把当前淡入淡出值写入已有 CanvasGroup。</summary>
        private void ApplyFadeAlpha()
        {
            for (int i = 0; i < m_canvasGroups.Length; i++)
            {
                m_canvasGroups[i].alpha = m_fadeAlpha;
            }
        }

        /// <summary>切换血条 UI 的 Canvas 渲染状态，保留脚本继续运行以便重新显示。</summary>
        private void SetCanvasesEnabled(bool isEnabled)
        {
            for (int i = 0; i < m_canvases.Length; i++)
            {
                m_canvases[i].enabled = isEnabled;
            }
        }

        /// <summary>绑定当前敌人属性事件。</summary>
        private void BindAttribute()
        {
            if (m_boundAttribute == attribute)
            {
                return;
            }

            UnbindAttribute();
            if (attribute != null)
            {
                attribute.AttributeChanged += OnAttributeChanged;
                m_boundAttribute = attribute;
            }
        }

        /// <summary>解除当前敌人属性事件绑定。</summary>
        private void UnbindAttribute()
        {
            if (m_boundAttribute != null)
            {
                m_boundAttribute.AttributeChanged -= OnAttributeChanged;
                m_boundAttribute = null;
            }
        }

        /// <summary>根据敌人当前属性刷新全部资源条。</summary>
        private void RefreshAll()
        {
            if (attribute == null)
            {
                return;
            }

            RefreshHealth(attribute.Health, attribute.MaxHealth);
            RefreshStability(attribute.Stability, attribute.MaxStability);
        }

        /// <summary>敌人发现玩家时，只旋转子 Canvas，使血条正面朝向主摄像机。</summary>
        private void FaceMainCamera()
        {
            if (m_mainCamera == null)
            {
                m_mainCamera = Camera.main;
            }

            if (m_mainCamera == null)
            {
                return;
            }

            for (int i = 0; i < m_canvases.Length; i++)
            {
                Transform canvasTransform = m_canvases[i].transform;
                Vector3 direction = m_mainCamera.transform.position - canvasTransform.position;
                if (direction.sqrMagnitude <= 0.0001f)
                {
                    continue;
                }

                canvasTransform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
        }

        /// <summary>收到属性变化事件时，只刷新发生变化的资源条。</summary>
        private void OnAttributeChanged(CombatAttributeChanged change)
        {
            switch (change.Type)
            {
                case CombatAttributeType.Health:
                    RefreshHealth(change.Current, change.Max);
                    break;
                case CombatAttributeType.Stability:
                    RefreshStability(change.Current, change.Max);
                    break;
            }
        }

        /// <summary>刷新血量条比例。</summary>
        private void RefreshHealth(int current, int max)
        {
            SetBarAmount(hpBar, current, max);
        }

        /// <summary>刷新稳定值条比例。</summary>
        private void RefreshStability(int current, int max)
        {
            SetBarAmount(mpBar, current, max);
        }

        /// <summary>把当前值换算成 0 到 1 的比例，并写入 Image.fillAmount。</summary>
        private static void SetBarAmount(Image bar, int current, int max)
        {
            if (bar == null)
            {
                return;
            }

            bar.fillAmount = max <= 0 ? 0f : Mathf.Clamp01((float)current / max);
        }
    }
}
