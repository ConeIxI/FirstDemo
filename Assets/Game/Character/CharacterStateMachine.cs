using System;
using System.Collections.Generic;
using Game.Battle.Combat.Config;
using Game.Character.Equipment;
using Game.Character.Player.PlayerFsm;
using GameMain2.Framework.Audio;
using GameMain2.Framework.Core;
using GameMain2.Framework.Core.FSM;
using GameMain2.Game.EventArgs;
using GameMain2.Scripts.Character;
using UnityEngine;

namespace Game.Character
{
    public abstract class CharacterStateMachine : MonoBehaviour
    {
        #region 配置

        public float moveAnimBlendSpeed = 2f;
        public float walkSpeed;//行走动画播放时的速度
        public float runSpeed;// 奔跑动画播放时的速度

        #endregion
        
        [SerializeField]
        protected Animator animator;

        
        public WeaponHandler weaponHandler;
        private RuntimeAnimatorController m_defaultAnimatorController;
        private const string DefaultHitReactionAnimation = "GetHit";
        private const string HitCombatParameterName = "IsCombat";
        private const string HitTypeParameterName = "HitType";
        private const string HitDirectionParameterName = "Dircetion";
        private PendingHitReaction m_pendingHitReaction;
        private PendingDeathReaction m_pendingDeathReaction;


        [HideInInspector]
        public bool isAttackDecisionWindowOpen;

        [HideInInspector]
        public bool isSkillCanSwitch;


        public PlayerState CurState { get; set; }

        public struct PendingHitReaction
        {
            public bool IsCombat;
            public SkillHitWeight HitWeight;
            public PlayerHitDirection HitDirection;
        }

        public struct PendingDeathReaction
        {
            public bool IsCombat;
            public SkillHitWeight HitWeight;
        }

        private void Start()
        {
            EnsureDefaultAnimatorController();
            if (animator != null)
            {
                animator.speed = walkSpeed;
            }
        }


        /// <summary>
        /// 以固定时间淡入到指定的动画。
        /// 此方法通过Animator组件在指定的时间内平滑地过渡到目标动画，同时可以设置偏移量和动画层。
        /// </summary>
        /// <param name="animationName">要淡入的目标动画名称。</param>
        /// <param name="duration">动画淡入的持续时间，默认为0.25秒。</param>
        /// <param name="offest">动画开始的偏移量，默认为0。</param>
        /// <param name="layer">动画所在的层，默认为0。</param>
        public void CrossFadeInFixedTime(string animationName, float duration = 0.25f, float offest = 0, int layer = 0)
        {
            TryCrossFadeInFixedTime(animationName, duration, offest, layer);
        }

        /// <summary>
        /// 安全尝试淡入指定动画，Animator 或控制器缺失时返回 false 而不是抛异常。
        /// </summary>
        public bool TryCrossFadeInFixedTime(string animationName, float duration = 0.25f, float offest = 0, int layer = 0)
        {
            EnsureDefaultAnimatorController();
            if (string.IsNullOrWhiteSpace(animationName) || animator == null || animator.runtimeAnimatorController == null)
            {
                return false;
            }

            if (layer < 0 || layer >= animator.layerCount)
            {
                return false;
            }

            animator.CrossFadeInFixedTime(animationName, duration, layer, offest);
            return true;
        }

        /// <summary>
        /// 检查指定动画状态是否存在，Animator 或控制器缺失时安全返回 false。
        /// </summary>
        public bool HasAnimationState(string animationName, int layer = 0)
        {
            EnsureDefaultAnimatorController();
            if (string.IsNullOrWhiteSpace(animationName) || animator == null || animator.runtimeAnimatorController == null)
            {
                return false;
            }

            if (layer < 0 || layer >= animator.layerCount)
            {
                return false;
            }

            int shortNameHash = Animator.StringToHash(animationName);
            if (animator.HasState(layer, shortNameHash))
            {
                return true;
            }

            string layerName = animator.GetLayerName(layer);
            if (string.IsNullOrWhiteSpace(layerName))
            {
                return false;
            }

            int fullNameHash = Animator.StringToHash($"{layerName}.{animationName}");
            return animator.HasState(layer, fullNameHash);
        }

        public void playAnimation(string animationName)
        {
            animator.Play(animationName);
        }


        /// <summary>
        /// 检查指定层上是否正在播放特定动画，并返回动画的进度。
        /// 此方法用于确定在给定的动画层中，特定名称的动画是否当前正在播放。如果动画正在播放，它还将输出该动画的归一化时间进度。
        /// </summary>
        /// <param name="animationName">要检查的目标动画名称。</param>
        /// <param name="animProgress">输出参数，表示目标动画的当前归一化时间进度（0到1之间）。</param>
        /// <param name="layer">动画所在的层，默认为0。</param>
        /// <returns>如果指定的动画正在播放，则返回true；否则返回false。</returns>
        public bool IsPlayingAnimation(string animationName, out float animProgress, int layer = 0)
        {
            animProgress = 0;
            EnsureDefaultAnimatorController();
            if (string.IsNullOrWhiteSpace(animationName) || animator == null || animator.runtimeAnimatorController == null)
            {
                return false;
            }

            if (layer < 0 || layer >= animator.layerCount)
            {
                return false;
            }

            AnimatorStateInfo animatorStateInfo = animator.GetCurrentAnimatorStateInfo(layer);
            bool isName = animatorStateInfo.IsName(animationName);
            if (!isName)
            {
                string layerName = animator.GetLayerName(layer);
                if (!string.IsNullOrWhiteSpace(layerName))
                {
                    isName = animatorStateInfo.IsName($"{layerName}.{animationName}");
                }
            }

            animProgress = isName ? animatorStateInfo.normalizedTime : 0;
            if (isName)
            {
                return true;
            }

            if (!animator.IsInTransition(layer))
            {
                return false;
            }

            AnimatorStateInfo nextAnimatorStateInfo = animator.GetNextAnimatorStateInfo(layer);
            bool isNextName = nextAnimatorStateInfo.IsName(animationName);
            if (!isNextName)
            {
                string layerName = animator.GetLayerName(layer);
                if (!string.IsNullOrWhiteSpace(layerName))
                {
                    isNextName = nextAnimatorStateInfo.IsName($"{layerName}.{animationName}");
                }
            }

            animProgress = isNextName ? nextAnimatorStateInfo.normalizedTime : 0;
            return isNextName;
        }

        /// <summary>
        /// 设置动画播放速度。
        /// 该方法允许你调整Animator组件的播放速度，从而影响所有动画的状态转换和播放速率。
        /// </summary>
        /// <param name="speed">动画播放的速度，默认为1.0f（即正常速度）。小于1.0f时减速，大于1.0f时加速。</param>
        public void SetSpeed(float speed = 1.0f)
        {
            animator.speed = speed;
        }

        /// <summary>
        /// 设置Animator组件中指定参数的浮点值。
        /// 该方法用于更新与动画相关的浮点型参数，从而影响动画状态机中的过渡或混合树。
        /// </summary>
        /// <param name="animName">要设置的动画参数名称。</param>
        /// <param name="value">给定参数的新值。</param>
        public void SetFloat(string animName, float value)
        {
            animator.SetFloat(animName, value);
        }

        /// <summary>
        /// 设置Animator组件中指定参数的浮点值。
        /// 该方法用于更新与动画相关的浮点型参数，从而影响动画状态机中的过渡或混合树。
        /// </summary>
        /// <param name="animName">要设置的动画参数名称</param>
        /// <param name="value">给定参数的新值。</param>
        /// <param name="dampTime">过渡时间</param>
        /// <param name="deltaTime">间隔时间</param>
        public void SetFloat(string animName, float value, float dampTime, float deltaTime)
        {
            animator.SetFloat(animName, value, dampTime, deltaTime);
        }

        public float GetFloat(string animName)
        {
            return animator.GetFloat(animName);
        }

        /// <summary>安全设置 Animator Trigger 参数，供状态机只触发过渡而不直接播放结束动画。</summary>
        public bool TrySetTrigger(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            if (animator == null)
            {
                return false;
            }

            animator.SetTrigger(parameterName);
            return true;
        }

        /// <summary>按名称查找 Animator 层，缺少 Animator 或控制器时返回 -1。</summary>
        public int GetAnimatorLayerIndex(string layerName)
        {
            EnsureDefaultAnimatorController();
            return animator == null || animator.runtimeAnimatorController == null
                ? -1
                : animator.GetLayerIndex(layerName);
        }

        /// <summary>检查 Animator 是否包含指定类型的参数。</summary>
        public bool HasAnimatorParameter(string parameterName, AnimatorControllerParameterType parameterType)
        {
            if (animator == null)
            {
                return false;
            }

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].name == parameterName && parameters[i].type == parameterType)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 开启武器碰撞并重置本次命中窗口，支持同一个技能多段命中同一目标。
        /// </summary>
        public virtual void EnableWeaponCollider()
        {
            WeaponHandler handler = ResolveWeaponHandler();
            if (handler == null)
            {
                return;
            }

            if (handler.GetActiveHitDetector() == null)
            {
                return;
            }

            handler.OpenHitWindow();
        }

        /// <summary>关闭武器命中窗口并结束本段能力命中结算。</summary>
        public void DisableWeaponCollider()
        {
            WeaponHandler handler = ResolveWeaponHandler();
            if (handler == null)
            {
                return;
            }

            handler.CloseHitWindow();
        }

        /// <summary>动画事件入口：按当前角色位置播放一次音效。</summary>
        public void PlaySfx(int soundId)
        {
            SoundManager.Instance.PlaySfxAt((SoundId)soundId, transform.position);
        }

        /// <summary>脚步动画事件入口：按当前角色位置播放一次脚步音效。</summary>
        public void PlayFootstepSfx(int soundId)
        {
            PlaySfx(soundId);
        }


        /// <summary>
        /// 攻击动画事件入口：打开后摇决策窗口，允许玩家选择闪避、防御、武器技能或普攻连段。
        /// </summary>
        public void AttackDecisionWindowOpen()
        {
            isAttackDecisionWindowOpen = true;
        }

        /// <summary>
        /// 兼容旧动画事件 SkillCanSwitch；旧事件打开窗口后，关闭事件只清旧标记，不提前关闭后摇决策窗口。
        /// </summary>
        public void SkillCanSwitch(int canSwitch)
        {
            isSkillCanSwitch = canSwitch != 0;
            if (isSkillCanSwitch)
            {
                isAttackDecisionWindowOpen = true;
            }
        }

        /// <summary>
        /// 退出攻击相关状态时统一清理后摇决策窗口，避免被打断或切状态后残留输入权限。
        /// </summary>
        public void ResetAttackDecisionWindow()
        {
            isAttackDecisionWindowOpen = false;
            isSkillCanSwitch = false;
        }

        /// <summary>缓存下一次普通受击 BlendTree 参数，后续由 GetHitState 消费。</summary>
        public void SetPendingHitReactionParameters(
            bool isCombat,
            SkillHitWeight hitWeight,
            PlayerHitDirection hitDirection)
        {
            m_pendingHitReaction = new PendingHitReaction
            {
                IsCombat = isCombat,
                HitWeight = hitWeight,
                HitDirection = hitDirection
            };
        }

        /// <summary>消费下一次普通受击 BlendTree 参数，消费后恢复非战斗轻击默认值。</summary>
        public PendingHitReaction ConsumePendingHitReaction()
        {
            PendingHitReaction reaction = m_pendingHitReaction;
            m_pendingHitReaction = default;
            return reaction;
        }

        /// <summary>缓存下一次死亡 BlendTree 参数，后续由 DeadState 消费。</summary>
        public void SetPendingDeathReactionParameters(bool isCombat, SkillHitWeight hitWeight)
        {
            m_pendingDeathReaction = new PendingDeathReaction
            {
                IsCombat = isCombat,
                HitWeight = hitWeight
            };
        }

        /// <summary>消费下一次死亡 BlendTree 参数，消费后恢复非战斗轻击默认值。</summary>
        public PendingDeathReaction ConsumePendingDeathReaction()
        {
            PendingDeathReaction reaction = m_pendingDeathReaction;
            m_pendingDeathReaction = default;
            return reaction;
        }

        /// <summary>写入玩家普通受击 BlendTree 参数，缺少 Animator 时只跳过写入不影响状态切换。</summary>
        public void ApplyHitReactionBlendTreeParameters(
            bool isCombat,
            SkillHitWeight hitWeight,
            PlayerHitDirection hitDirection)
        {
            TrySetFloat(HitCombatParameterName, isCombat ? 1f : 0f);
            TrySetFloat(HitTypeParameterName, (float)hitWeight);
            TrySetFloat(HitDirectionParameterName, (float)hitDirection);
        }

        /// <summary>写入玩家死亡 BlendTree 参数，死亡动画不使用受击方向。</summary>
        public void ApplyDeathBlendTreeParameters(bool isCombat, SkillHitWeight hitWeight)
        {
            TrySetFloat(HitCombatParameterName, isCombat ? 1f : 0f);
            TrySetFloat(HitTypeParameterName, (float)hitWeight);
        }

        /// <summary>安全写入 Animator float 参数，供可选 BlendTree 参数使用。</summary>
        private bool TrySetFloat(string parameterName, float value)
        {
            if (string.IsNullOrWhiteSpace(parameterName) || animator == null)
            {
                return false;
            }

            animator.SetFloat(parameterName, value);
            return true;
        }

        public void SwitchAnimatorController(WeaponData weaponData)
        {
            EnsureDefaultAnimatorController();
            if (animator == null || weaponData == null || weaponData.animatorOverride == null)
            {
                return;
            }

            animator.runtimeAnimatorController = weaponData.animatorOverride;
        }

        public void ResetAnimatorController()
        {
            EnsureDefaultAnimatorController();
            if (animator != null && m_defaultAnimatorController != null)
            {
                animator.runtimeAnimatorController = m_defaultAnimatorController;
            }
        }

        private void EnsureDefaultAnimatorController()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            if (animator != null && m_defaultAnimatorController == null)
            {
                m_defaultAnimatorController = animator.runtimeAnimatorController;
            }
        }

        private WeaponHandler ResolveWeaponHandler()
        {
            if (weaponHandler != null)
            {
                return weaponHandler;
            }

            weaponHandler = GetComponent<WeaponHandler>();
            if (weaponHandler != null)
            {
                return weaponHandler;
            }

            PlayerController playerController = GetComponentInParent<PlayerController>();
            if (playerController != null)
            {
                weaponHandler = playerController.WeaponHandler;
            }

            return weaponHandler;
        }

    }
}
