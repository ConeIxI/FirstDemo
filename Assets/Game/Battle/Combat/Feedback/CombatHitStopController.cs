using System.Collections;
using Game.Battle.Ability;
using Game.Battle.Combat.Config;
using UnityEngine;

namespace Game.Battle.Combat.Feedback
{
    [ExecuteAlways]
    public sealed class CombatHitStopController : MonoBehaviour
    {
        private const string RuntimeObjectName = "[CombatHitStopController]";
        private const float FrozenTimeEpsilon = 0.0001f;
        private const float MaxNormalHitStopTime = 0.08f;
        private const float MaxHeavyHitStopTime = 0.12f;
        private const float MaxCollisionHitStopTime = 0.06f;
        private static CombatHitStopController s_instance;

        private Coroutine m_runningRoutine;
        private float m_originalTimeScale = 1f;
        private float m_originalFixedDeltaTime = 0.02f;
        private bool m_ownsActiveStop;
        /// <summary>判断战斗事件是否应该播放命中停顿。</summary>
        public static bool ShouldPlayHitStop(CombatEvent combatEvent)
        {
            if (combatEvent == null
                || combatEvent.Skill == null
                || combatEvent.Skill.hitConfig == null
                || combatEvent.Skill.hitConfig.hitStopTime <= 0f)
            {
                return false;
            }

            // 死亡结果由状态/动画优先处理，表现层不再叠加命中停顿。
            if (combatEvent.TargetDead)
            {
                return false;
            }

            switch (combatEvent.Type)
            {
                case CombatEventType.Hit:
                case CombatEventType.Blocked:
                case CombatEventType.Parried:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>解析可执行的停顿时长，并沿用既有命中类型上限。</summary>
        public static float ResolveDuration(CombatEvent combatEvent)
        {
            if (!ShouldPlayHitStop(combatEvent))
            {
                return 0f;
            }

            float maxDuration = ResolveMaxDuration(combatEvent);
            return Mathf.Clamp(combatEvent.Skill.hitConfig.hitStopTime, 0f, maxDuration);
        }

        /// <summary>请求播放命中停顿；无效事件会被直接忽略。</summary>
        public static void Play(CombatEvent combatEvent)
        {
            float duration = ResolveDuration(combatEvent);
            if (duration <= 0f)
            {
                return;
            }

            CleanupInactiveInstance();
            if (Time.timeScale <= 0f && (s_instance == null || !s_instance.m_ownsActiveStop))
            {
                return;
            }

            EnsureInstance().StartHitStop(duration);
        }

        /// <summary>根据事件分支和命中强度解析既有停顿时长上限。</summary>
        private static float ResolveMaxDuration(CombatEvent combatEvent)
        {
            if (combatEvent.Type == CombatEventType.Blocked || combatEvent.Type == CombatEventType.Parried)
            {
                return MaxCollisionHitStopTime;
            }

            bool isHeavyHit = combatEvent.Skill.HitWeight == SkillHitWeight.Heavy;
            return isHeavyHit ? MaxHeavyHitStopTime : MaxNormalHitStopTime;
        }

        /// <summary>外部暂停系统接管前取消当前命中停顿，避免停顿结束后反向解暂停。</summary>
        public static void CancelActiveStopForExternalPause()
        {
            CleanupInactiveInstance();
            if (s_instance == null || !s_instance.m_ownsActiveStop)
            {
                return;
            }

            s_instance.StopCurrentStop();
        }

        /// <summary>创建或复用运行时控制器，集中管理全局时间缩放。</summary>
        private static CombatHitStopController EnsureInstance()
        {
            if (s_instance != null)
            {
                return s_instance;
            }

            GameObject go = new GameObject(RuntimeObjectName);
            if (Application.isPlaying)
            {
                DontDestroyOnLoad(go);
            }

            s_instance = go.AddComponent<CombatHitStopController>();
            return s_instance;
        }

        /// <summary>清理已失效的静态实例，避免复用禁用中的控制器。</summary>
        private static void CleanupInactiveInstance()
        {
            if (s_instance != null && !s_instance.isActiveAndEnabled)
            {
                s_instance.CleanupActiveStop();
            }
        }

        /// <summary>控制器被禁用时恢复自己持有的停顿，避免全局时间被永久冻结。</summary>
        private void OnDisable()
        {
            CleanupActiveStop();
        }

        /// <summary>控制器被销毁时恢复自己持有的停顿，并清理静态实例引用。</summary>
        private void OnDestroy()
        {
            CleanupActiveStop();
        }

        /// <summary>启动新的停顿，若已有停顿正在执行则先恢复时间再重启。</summary>
        private void StartHitStop(float duration)
        {
            StopCurrentStop();

            m_runningRoutine = StartCoroutine(PlayRoutine(duration));
        }

        /// <summary>使用真实时间等待，避免 Time.timeScale 为 0 后无法恢复。</summary>
        private IEnumerator PlayRoutine(float duration)
        {
            CaptureAndFreezeTime();

            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            while (stopwatch.Elapsed.TotalSeconds < duration)
            {
                yield return null;
            }

            RestoreTimeScale();
            m_runningRoutine = null;
        }

        /// <summary>记录当前时间参数并冻结时间，后续只由本控制器恢复。</summary>
        private void CaptureAndFreezeTime()
        {
            m_originalTimeScale = Time.timeScale;
            m_originalFixedDeltaTime = Time.fixedDeltaTime;
            m_ownsActiveStop = true;
            Time.timeScale = 0f;
            Time.fixedDeltaTime = 0f;
        }

        /// <summary>停止当前停顿流程，并恢复本控制器持有的时间参数。</summary>
        private void StopCurrentStop()
        {
            if (m_runningRoutine != null)
            {
                StopCoroutine(m_runningRoutine);
                m_runningRoutine = null;
            }

            RestoreTimeScale();
        }

        /// <summary>停止当前协程并恢复本控制器持有的时间停顿。</summary>
        private void CleanupActiveStop()
        {
            if (m_runningRoutine != null)
            {
                StopCoroutine(m_runningRoutine);
                m_runningRoutine = null;
            }

            RestoreTimeScale();
            if (s_instance == this)
            {
                s_instance = null;
            }
        }

        /// <summary>恢复停顿前的时间参数。</summary>
        private void RestoreTimeScale()
        {
            if (!m_ownsActiveStop)
            {
                return;
            }

            if (IsTimeScaleFrozenByController())
            {
                Time.timeScale = m_originalTimeScale;
            }

            if (IsFixedDeltaTimeFrozenByController())
            {
                Time.fixedDeltaTime = m_originalFixedDeltaTime;
            }

            m_ownsActiveStop = false;
        }

        /// <summary>判断当前 timeScale 是否仍是本控制器写入的冻结值。</summary>
        private static bool IsTimeScaleFrozenByController()
        {
            return Time.timeScale <= FrozenTimeEpsilon;
        }

        /// <summary>判断当前 fixedDeltaTime 是否仍是本控制器写入的冻结值。</summary>
        private static bool IsFixedDeltaTimeFrozenByController()
        {
            return Time.fixedDeltaTime <= FrozenTimeEpsilon;
        }
    }
}
