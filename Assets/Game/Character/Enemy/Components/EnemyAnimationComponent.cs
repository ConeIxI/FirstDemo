using Game.Battle.Combat.Config;
using Game.Character.Enemy.Config;
using Game.Character.Enemy.Core;
using Game.Character.Equipment;
using GameMain2.Framework.Audio;
using UnityEngine;

namespace Game.Character.Enemy.Components
{
    public sealed class EnemyAnimationComponent : MonoBehaviour
    {
        private const string MissingCombatError =
            "EnemyAnimationComponent 缺少同一 GameObject 上的 EnemyCombatComponent，组件已禁用。";
        private const string MissingWeaponHandlerError =
            "EnemyAnimationComponent 缺少同一 GameObject 上的 WeaponHandler，组件已禁用。";

        [SerializeField] private Animator animator;
        [SerializeField] private EnemyCombatComponent combat;
        [SerializeField] private WeaponHandler weaponHandler;
        [SerializeField] private EnemyMovementComponent movement;
        private const string HitCombatParameterName = "IsCombat";
        private const string HitTypeParameterName = "HitType";
        private const string HitDirectionParameterName = "Dircetion";
        private float transitionDuration = EnemyAnimationConfig.DefaultTransitionDuration;
        private bool suppressRootMotion;
        /// <summary>唤醒时缓存动画器和战斗组件引用。</summary>
        private void Awake()
        {
            if (animator == null)
            {
                TryGetComponent(out animator);
            }

            if (combat == null)
            {
                TryGetComponent(out combat);
            }

            if (weaponHandler == null)
            {
                TryGetComponent(out weaponHandler);
            }

            if (movement == null)
            {
                TryGetComponent(out movement);
            }

            if (combat == null)
            {
                Debug.LogError(MissingCombatError, this);
                enabled = false;
            }
            else if (weaponHandler == null)
            {
                Debug.LogError(MissingWeaponHandlerError, this);
                enabled = false;
            }
        }

        /// <summary>直接在基础层播放指定动画状态。</summary>
        public void Play(string stateName)
        {
            TryPlay(stateName);
        }

        /// <summary>切换移动动画根位移重定向状态，让 NavMesh 决定方向并由 CharacterController 执行位移。</summary>
        public void SetRootMotionSuppressed(bool suppressed)
        {
            suppressRootMotion = suppressed;
            if (movement == null)
            {
                TryGetComponent(out movement);
            }

            if (movement != null)
            {
                movement.SetRootMotionNavigationEnabled(suppressed);
            }
        }

        /// <summary>统一接管 Animator 根位移，寻路移动时把动画步幅交给移动组件沿 NavMesh 路径消耗。</summary>
        private void OnAnimatorMove()
        {
            if (animator == null)
            {
                return;
            }

            if (suppressRootMotion)
            {
                if (movement != null)
                {
                    movement.MoveByRootMotion(animator.deltaPosition);
                }

                return;
            }

            animator.ApplyBuiltinRootMotion();
        }

        /// <summary>从敌人定义读取动画过渡时长。</summary>
        public void ApplyConfig(EnemyAnimationConfig config)
        {
            transitionDuration = config.transitionDuration;
        }

        /// <summary>设置指定动画层权重，新增受代码控制的层级时扩展 EnemyAnimationLayer。</summary>
        public bool SetLayerWeight(EnemyAnimationLayer layer, float weight)
        {
            if (animator == null)
            {
                return false;
            }

            int layerIndex = GetLayerIndex(layer);
            if (layerIndex < 0)
            {
                return false;
            }

            animator.SetLayerWeight(layerIndex, weight);
            return true;
        }

        /// <summary>设置 Animator 浮点参数，用于驱动 BlendTree 选择离散动画。</summary>
        public bool SetFloat(string parameterName, float value)
        {
            if (string.IsNullOrEmpty(parameterName))
            {
                return false;
            }

            if (animator == null)
            {
                return false;
            }

            animator.SetFloat(parameterName, value);
            return true;
        }

        /// <summary>设置 Animator 触发器参数，用于交给动画状态机推进一次性过渡。</summary>
        public bool SetTrigger(string parameterName)
        {
            if (string.IsNullOrEmpty(parameterName))
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

        /// <summary>写入受击 BlendTree 的战斗状态、轻重击类型和受击方向参数。</summary>
        public void SetHitReactionParameters(
            bool isInCombat,
            SkillHitWeight hitWeight,
            EnemyHitDirection hitDirection)
        {
            SetCombatStateParameter(isInCombat);
            SetFloat(HitTypeParameterName, (float)hitWeight);
            SetFloat(HitDirectionParameterName, (float)hitDirection);
        }

        /// <summary>写入死亡 BlendTree 的战斗状态和轻重击类型参数。</summary>
        public void SetDeathParameters(bool isInCombat, SkillHitWeight hitWeight)
        {
            SetCombatStateParameter(isInCombat);
            SetFloat(HitTypeParameterName, (float)hitWeight);
        }

        /// <summary>同步敌人是否处于战斗状态的 Animator 参数，供所有共用 IsCombat 的 BlendTree 使用。</summary>
        public void SetCombatStateParameter(bool isInCombat)
        {
            SetFloat(HitCombatParameterName, isInCombat ? 1f : 0f);
        }

        /// <summary>以固定时长在指定动画层淡入动画状态，必要时可强制从头重播。</summary>
        public bool TryPlay(
            string stateName,
            EnemyAnimationLayer layer = EnemyAnimationLayer.Base,
            bool interruptCurrentAction = true,
            bool forceRestart = false)
        {
            if (string.IsNullOrEmpty(stateName))
            {
                return false;
            }

            if (animator == null)
            {
                return false;
            }

            int layerIndex = GetLayerIndex(layer);
            if (layerIndex < 0)
            {
                return false;
            }

            SetRootMotionSuppressed(false);

            if (!forceRestart && IsStateActiveOrTransitioningTo(stateName, layerIndex, layer))
            {
                return true;
            }

            if (interruptCurrentAction && combat != null)
            {
                if (!combat.TryInterruptAction())
                {
                    return false;
                }
            }

            if (forceRestart)
            {
                animator.CrossFadeInFixedTime(stateName, transitionDuration, layerIndex, 0f);
            }
            else
            {
                animator.CrossFadeInFixedTime(stateName, transitionDuration, layerIndex);
            }

            return true;
        }

        /// <summary>判断指定动画层上的目标状态是否已播放或正在淡入。</summary>
        private bool IsStateActiveOrTransitioningTo(
            string stateName,
            int layerIndex,
            EnemyAnimationLayer layer)
        {
            if (animator.GetCurrentAnimatorStateInfo(layerIndex).IsName(stateName))
            {
                return true;
            }

            return animator.IsInTransition(layerIndex)
                && animator.GetNextAnimatorStateInfo(layerIndex).IsName(stateName);
        }

        /// <summary>动画层枚举值直接对应 Animator 层索引。</summary>
        private static int GetLayerIndex(EnemyAnimationLayer layer)
        {
            return (int)layer;
        }

        /// <summary>判断指定动画层当前或淡入目标动画是否匹配，并输出归一化进度。</summary>
        public bool IsPlaying(
            string stateName,
            out float normalizedTime,
            EnemyAnimationLayer layer = EnemyAnimationLayer.Base)
        {
            normalizedTime = 0f;
            if (string.IsNullOrEmpty(stateName))
            {
                return false;
            }

            if (animator == null)
            {
                return false;
            }

            int layerIndex = GetLayerIndex(layer);
            if (layerIndex < 0)
            {
                return false;
            }

            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(layerIndex);
            if (stateInfo.IsName(stateName))
            {
                normalizedTime = stateInfo.normalizedTime;
                return true;
            }

            if (animator.IsInTransition(layerIndex))
            {
                AnimatorStateInfo nextStateInfo = animator.GetNextAnimatorStateInfo(layerIndex);
                if (nextStateInfo.IsName(stateName))
                {
                    normalizedTime = nextStateInfo.normalizedTime;
                    return true;
                }
            }

            return false;
        }
        /// <summary>处理字符串形式的攻击动画事件，并交给敌人战斗组件开关命中体。</summary>
        public void HandleAnimationEvent(string eventName)
        {
            if (combat == null)
            {
                return;
            }

            if (eventName == "EnableWeaponHit")
            {
                combat.EnableWeaponHit();
            }
            else if (eventName == "DisableWeaponHit")
            {
                combat.DisableWeaponHit();
            }
        }

        /// <summary>动画事件入口：按当前敌人位置播放一次音效。</summary>
        public void PlaySfx(int soundId)
        {
            SoundManager.Instance.PlaySfxAt((SoundId)soundId, transform.position);
        }

        /// <summary>脚步动画事件入口：按当前敌人位置播放一次脚步音效。</summary>
        public void PlayFootstepSfx(int soundId)
        {
            PlaySfx(soundId);
        }
    }
}
