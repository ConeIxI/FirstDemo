using System.Collections.Generic;
using Game.Battle.Ability;
using Game.Character.Enemy.Config;
using Game.Character.Enemy.Core;
using GameMain2.Scripts.Character;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Character.Enemy.Components
{
    public enum EnemyPerceptionState
    {
        Visible,
        Remembered,
        Searching,
        Lost
    }

    public sealed class EnemyPerceptionComponent : MonoBehaviour
    {
        [SerializeField] private float range = 8f;
        [SerializeField] private LayerMask targetMask;
        [SerializeField] private float angle = 120f;
        [SerializeField] private float loseSightGraceTime = 0.5f;
        [SerializeField] private float searchRadius = 4f;
        [SerializeField] private int searchPointCount = 3;
        [SerializeField] private float closeAwarenessRange = 2.5f;
        [SerializeField] private float soundRange = 6f;
        [SerializeField] private LayerMask obstacleMask;

        private EnemyBlackboard blackboard;

        /// <summary>绑定敌人黑板，感知结果只写入该黑板。</summary>
        public void Bind(EnemyBlackboard value)
        {
            blackboard = value;
        }

        // 从敌人定义加载感知规则，Inspector 仅保留组件与场景引用。
        public void ApplyConfig(EnemyPerceptionConfig config)
        {
            range = config.range;
            targetMask = config.targetMask;
            angle = config.angle;
            loseSightGraceTime = config.loseSightGraceTime;
            searchRadius = config.searchRadius;
            searchPointCount = config.searchPointCount;
            closeAwarenessRange = config.closeAwarenessRange;
            soundRange = config.soundRange;
            obstacleMask = config.obstacleMask;
        }

        /// <summary>扫描视野内最近可见目标，只返回候选目标，不写入记忆事实。</summary>
        public Transform ScanVisibleTarget()
        {
            Collider[] colliders = Physics.OverlapSphere(transform.position, range, targetMask);
            Transform bestTarget = null;
            float bestDistance = float.MaxValue;

            foreach (Collider collider in colliders)
            {
                Transform target = collider.transform;
                if (!CanSee(target))
                {
                    continue;
                }

                float distance = (target.position - transform.position).sqrMagnitude;
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestTarget = target;
                bestDistance = distance;
            }

            return bestTarget;
        }

        /// <summary>兼容旧扫描入口，找到可见目标时转写统一黑板记忆。</summary>
        public Transform ScanTarget()
        {
            Transform target = ScanVisibleTarget();
            if (target != null && blackboard != null)
            {
                blackboard.RememberTarget(target);
                blackboard.SetTargetVisible(true);
                blackboard.SetSearching(false);
            }

            return target;
        }

        /// <summary>扫描声音范围内的玩家动作目标，只在玩家处于动作状态时返回目标。</summary>
        public Transform ScanSoundTarget()
        {
            Collider[] colliders = Physics.OverlapSphere(transform.position, soundRange, targetMask);
            foreach (Collider collider in colliders)
            {
                Transform target = collider.transform;
                if (CanHear(target))
                {
                    return target.GetComponentInParent<PlayerStateMachine>().transform;
                }
            }

            return null;
        }

        /// <summary>兼容旧受击入口，把目标写入黑板但不维护独立倒计时。</summary>
        public void RememberTarget(Transform target)
        {
            if (target == null || blackboard == null)
            {
                return;
            }

            blackboard.RememberTarget(target);
            blackboard.SetTargetVisible(CanSee(target));
            blackboard.SetSearching(false);
        }

        /// <summary>判断目标是否在视野角度、距离和遮挡条件内。</summary>
        public bool CanSee(Transform target)
        {
            if (!IsValidCombatTarget(target))
            {
                return false;
            }

            Vector3 offset = target.position - transform.position;
            if (offset.sqrMagnitude > range * range)
            {
                return false;
            }

            if (Vector3.Angle(offset.normalized, transform.forward) > angle / 2.0f)
            {
                return false;
            }

            if (obstacleMask.value != 0 && Physics.Linecast(transform.position, target.position, obstacleMask))
            {
                return false;
            }

            return true;
        }

        /// <summary>判断近距离目标是否可被感知，近感知不要求视野角度。</summary>
        public bool CanSenseNearby(Transform target, float minimumRange)
        {
            if (!IsValidCombatTarget(target))
            {
                return false;
            }

            float senseRange = Mathf.Max(closeAwarenessRange, minimumRange);
            Vector3 offset = target.position - transform.position;
            if (offset.sqrMagnitude > senseRange * senseRange)
            {
                return false;
            }

            if (obstacleMask.value != 0 && Physics.Linecast(transform.position, target.position, obstacleMask))
            {
                return false;
            }

            return true;
        }

        /// <summary>判断目标是否在声音范围内且当前正在执行会被感知的玩家动作。</summary>
        public bool CanHear(Transform target)
        {
            if (!IsValidCombatTarget(target))
            {
                return false;
            }

            if (!IsInSoundRange(target))
            {
                return false;
            }

            PlayerStateMachine playerStateMachine = target.GetComponentInParent<PlayerStateMachine>();
            if (playerStateMachine == null)
            {
                return false;
            }

            PlayerState currentState = playerStateMachine.CurrentPlayerState;
            // 武器技能状态复用 Attack 枚举，因此 Attack 同时覆盖普通攻击和技能释放。
            return (currentState == PlayerState.Locomotion && playerStateMachine.HasMoveInput)
                || currentState == PlayerState.Dodge
                || currentState == PlayerState.Attack;
        }

        /// <summary>兼容旧评估入口，只返回当前可见性判断，不推进独立记忆倒计时。</summary>
        public EnemyPerceptionState EvaluateTarget(float deltaTime)
        {
            return EvaluateTarget(deltaTime, false);
        }

        /// <summary>兼容旧评估入口，只同步可见性，不再维护搜索状态机。</summary>
        public EnemyPerceptionState EvaluateTarget(float deltaTime, bool reachedLastKnownPosition)
        {
            if (blackboard == null || !IsValidCombatTarget(blackboard.Target))
            {
                return EnemyPerceptionState.Lost;
            }

            Transform target = blackboard.Target;
            if (CanSee(target))
            {
                blackboard.SetTargetVisible(true);
                return EnemyPerceptionState.Visible;
            }

            blackboard.SetTargetVisible(false);
            return blackboard.HasAlertMemory ? EnemyPerceptionState.Remembered : EnemyPerceptionState.Lost;
        }

        /// <summary>判断目标是否处于声音感知范围内。</summary>
        private bool IsInSoundRange(Transform target)
        {
            Vector3 offset = target.position - transform.position;
            return offset.sqrMagnitude <= soundRange * soundRange;
        }

        /// <summary>判断目标是否仍可作为敌人感知对象，死亡目标不再参与锁定。</summary>
        private static bool IsValidCombatTarget(Transform target)
        {
            if (target == null || !target.gameObject.activeInHierarchy)
            {
                return false;
            }

            CombatAbilitySystem abilitySystem = target.GetComponentInParent<CombatAbilitySystem>();
            if (abilitySystem == null)
            {
                return true;
            }

            ICombatAttributes attributes = abilitySystem.Attributes;
            return !abilitySystem.HasTag(CombatTag.Dead)
                && (attributes == null || !attributes.IsDead);
        }

        /// <summary>兼容旧清理入口，直接清理统一黑板目标事实。</summary>
        public void ForgetTarget()
        {
            if (blackboard != null)
            {
                blackboard.ForgetTarget();
                blackboard.SetSearching(false);
            }
        }

        /// <summary>围绕最后已知位置生成搜索点，NavMesh 采样失败的候选点会被舍弃。</summary>
        public Vector3[] GenerateSearchPoints(Vector3 center)
        {
            List<Vector3> points = new List<Vector3>();
            int count = Mathf.Max(0, searchPointCount);
            int attempts = Mathf.Max(count * 6, count);
            for (int i = 0; i < attempts && points.Count < count; i++)
            {
                Vector2 offset = Random.insideUnitCircle * searchRadius;
                Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, searchRadius, NavMesh.AllAreas))
                {
                    points.Add(hit.position);
                }
            }

            if (points.Count == 0)
            {
                points.Add(center);
            }

            return points.ToArray();
        }
    }
}
