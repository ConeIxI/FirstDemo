using Game.Character.Enemy.Config;
using Game.Battle.Ability;
using UnityEngine;
using UnityEngine.AI;

namespace Game.Character.Enemy.Components
{
    public sealed class EnemyMovementComponent : MonoBehaviour, ICombatMotion
    {
        [SerializeField] private UnityEngine.CharacterController controller;
        [SerializeField] private NavMeshAgent agent;
        [SerializeField] private bool useNavMeshAgent;
        [SerializeField] private bool useGravity = true;
        [SerializeField] private float moveSpeed = 2f;
        [SerializeField] private float rotateSpeed = 4f;
        [SerializeField] private float attackRotateSpeed = 4f;
        [SerializeField] private float navMeshSampleDistance = 2f;
        [SerializeField] private float stoppingDistance = 1.1f;

        private Vector3 currentDestination;
        private int m_executionLockCount;
        private bool useRootMotionNavigation;

        public bool HasDestination { get; private set; }
        public bool HasNavMeshAgent => useNavMeshAgent && agent != null && agent.enabled;
        public bool UseNavMeshAgent => useNavMeshAgent;
        public bool IsExecutionLocked => m_executionLockCount > 0;
        public float StoppingDistance => stoppingDistance;
        private bool CanUseAgent => HasNavMeshAgent && agent.isOnNavMesh;

        /// <summary>唤醒时缓存可选移动组件，并让 NavMeshAgent 由本组件驱动位移。</summary>
        private void Awake()
        {
            ResolveMovementComponents();
        }

        // 从敌人定义加载移动数值，Inspector 仅保留移动组件引用。
        public void ApplyConfig(EnemyMovementConfig config)
        {
            moveSpeed = config.moveSpeed;
            rotateSpeed = config.rotateSpeed;
            attackRotateSpeed = config.attackRotateSpeed;
            navMeshSampleDistance = config.navMeshSampleDistance;
            stoppingDistance = config.stoppingDistance;

            ResolveMovementComponents();
            if (agent != null)
            {
                agent.stoppingDistance = stoppingDistance;
            }
        }

        /// <summary>按帧朝移动目标转向并处理手动重力，非 RootMotion 模式下按配置速度推进水平位移。</summary>
        public void Tick(float deltaTime)
        {
            ResolveMovementComponents();

            if (IsExecutionLocked)
            {
                return;
            }

            if (HasDestination)
            {
                Vector3 lookTarget = CanUseAgent && agent.hasPath
                    ? agent.steeringTarget
                    : currentDestination;
                LookAt(lookTarget);

                if (!useRootMotionNavigation && CanUseAgent)
                {
                    MoveWithAgent(deltaTime);
                }
                else if (!useRootMotionNavigation)
                {
                    MoveTowardsDestination(deltaTime);
                }
            }

            if (useGravity && controller != null)
            {
                Vector3 gravityDisplacement = new Vector3(0f, -9.8f, 0f) * deltaTime;
                if (Application.isPlaying)
                {
                    controller.Move(gravityDisplacement);
                }
                else
                {
                    transform.position += gravityDisplacement;
                }
            }
        }

        /// <summary>移动到指定 Transform 位置，空目标时不改变当前目的地。</summary>
        public void MoveTo(Transform target)
        {
            if (target == null)
            {
                return;
            }

            MoveTo(target.position);
        }

        /// <summary>向远离目标的方向移动指定距离，用于后撤行为。</summary>
        public void MoveAwayFrom(Transform target, float distance)
        {
            if (target == null)
            {
                return;
            }

            Vector3 direction = transform.position - target.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = -transform.forward;
            }

            Vector3 destination = transform.position + direction.normalized * distance;
            MoveTo(destination);
        }

        /// <summary>记录移动目的地，并优先请求 NavMesh 路径，否则改用直线移动。</summary>
        public void MoveTo(Vector3 position)
        {
            ResolveMovementComponents();

            if (CanUseAgent)
            {
                if (TrySetAgentDestination(position))
                {
                    return;
                }

                agent.ResetPath();
            }

            SetDirectDestination(position);
        }

        /// <summary>停止当前移动并清理目的地标记。</summary>
        public void Stop()
        {
            ResolveMovementComponents();
            HasDestination = false;
            useRootMotionNavigation = false;

            if (!CanUseAgent)
            {
                return;
            }

            agent.isStopped = true;
            agent.ResetPath();
        }

        /// <summary>进入处决锁定，立即停止当前寻路和位移。</summary>
        public void PushExecutionLock()
        {
            m_executionLockCount++;
            Stop();
        }

        /// <summary>释放处决锁定，后续由 AI 根据黑板状态重新下发移动意图。</summary>
        public void PopExecutionLock()
        {
            m_executionLockCount--;
            if (m_executionLockCount < 0)
            {
                Debug.LogError("敌人移动处决锁释放次数超过获取次数。", this);
                m_executionLockCount = 0;
            }
        }

        /// <summary>施加不受当前寻路目标影响的水平位移，用于受击等外部移动效果。</summary>
        public void ApplyExternalDisplacement(Vector3 displacement)
        {
            Stop();
            displacement.y = 0f;
            if (displacement.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            if (controller != null && Application.isPlaying)
            {
                controller.Move(displacement);
                if (CanUseAgent)
                {
                    agent.nextPosition = transform.position;
                }

                return;
            }

            transform.position += displacement;
        }

        /// <summary>让敌人沿 Y 轴朝向目标点。</summary>
        public void LookAt(Vector3 target)
        {
            LookAt(target, rotateSpeed);
        }

        /// <summary>让敌人在攻击动作期间按攻击转向速度朝向目标点。</summary>
        public void LookAtForAttack(Vector3 target)
        {
            LookAt(target, attackRotateSpeed);
        }

        /// <summary>按指定转向速度让敌人沿 Y 轴朝向目标点。</summary>
        private void LookAt(Vector3 target, float rotationSpeed)
        {
            // const float turnAnimationAngle = 150f;

            Vector3 direction = target - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Vector3 normalizedDirection = direction.normalized;
            // float angle = Vector3.Angle(transform.forward, normalizedDirection);
            // if (angle >= turnAnimationAngle && TryGetComponent(out EnemyAnimationComponent animation))
            // {
            //     animation.TryPlay("Turn");
            //     return;
            // }

            Quaternion rotation = Quaternion.LookRotation(normalizedDirection, Vector3.up);
            transform.rotation = Quaternion.Lerp(transform.rotation, rotation, rotationSpeed * Time.deltaTime);
        }

        /// <summary>让敌人沿 Y 轴瞬间朝向目标点，不使用插值或转身动画。</summary>
        public void LookAtInstant(Vector3 target)
        {
            Vector3 direction = target - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        /// <summary>判断当前位置是否到达指定目标点。</summary>
        public bool HasReached(Vector3 position, float distance)
        {
            ResolveMovementComponents();

            if (CanUseAgent)
            {
                if (agent.pathPending)
                {
                    return false;
                }

                float reachDistance = Mathf.Max(distance, agent.stoppingDistance);
                return agent.remainingDistance <= reachDistance;
            }

            Vector3 offset = position - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude <= distance * distance;
        }

        /// <summary>设置寻路移动是否消耗动画 RootMotion 步幅，开启后 Tick 不再按配置速度直接推进。</summary>
        public void SetRootMotionNavigationEnabled(bool enabled)
        {
            useRootMotionNavigation = enabled;
        }

        /// <summary>消耗移动动画的 RootMotion 步幅，并沿当前 NavMesh 路径方向推动 CharacterController。</summary>
        public void MoveByRootMotion(Vector3 rootMotionDelta)
        {
            ResolveMovementComponents();
            if (!HasDestination || IsExecutionLocked)
            {
                return;
            }

            Vector3 move = CanUseAgent
                ? GetAgentMoveDirection()
                : currentDestination - transform.position;
            MoveWithRootMotionStep(move, currentDestination, rootMotionDelta);

            if (CanUseAgent)
            {
                agent.nextPosition = transform.position;
            }
        }

        /// <summary>补齐移动依赖组件，并保持 NavMeshAgent 由 CharacterController 驱动。</summary>
        private void ResolveMovementComponents()
        {
            if (controller == null)
            {
                TryGetComponent(out controller);
            }

            if (agent == null)
            {
                TryGetComponent(out agent);
            }

            if (!useNavMeshAgent)
            {
                if (agent != null && agent.enabled && agent.isOnNavMesh && (agent.hasPath || agent.pathPending))
                {
                    agent.isStopped = true;
                    agent.ResetPath();
                }

                return;
            }

            if (agent != null && controller != null)
            {
                agent.updatePosition = false;
                agent.updateRotation = false;
                agent.nextPosition = transform.position;
            }
        }

        /// <summary>采样目标点附近的 NavMesh 位置，失败时返回原始点。</summary>
        public bool SampleNavMesh(Vector3 position, out Vector3 sampledPosition)
        {
            int areaMask = agent != null ? agent.areaMask : NavMesh.AllAreas;
            if (NavMesh.SamplePosition(position, out NavMeshHit hit, navMeshSampleDistance, areaMask))
            {
                sampledPosition = hit.position;
                return true;
            }

            sampledPosition = position;
            return false;
        }

        /// <summary>尝试为 NavMeshAgent 设置采样后的目的地。</summary>
        private bool TrySetAgentDestination(Vector3 position)
        {
            if (!SampleNavMesh(position, out Vector3 sampledPosition))
            {
                return false;
            }

            currentDestination = sampledPosition;
            HasDestination = true;
            agent.isStopped = false;
            agent.speed = moveSpeed;
            if (!agent.SetDestination(sampledPosition))
            {
                return false;
            }

            return true;
        }

        /// <summary>在没有可用 NavMeshAgent 时记录直线移动目的地。</summary>
        private void SetDirectDestination(Vector3 position)
        {
            currentDestination = position;
            HasDestination = true;
        }

        /// <summary>使用 NavMeshAgent 的期望方向按配置速度驱动 CharacterController，并同步代理位置。</summary>
        private void MoveWithAgent(float deltaTime)
        {
            if (controller == null)
            {
                return;
            }

            Vector3 move = GetAgentMoveDirection();
            MoveWithConfiguredSpeed(move, currentDestination, deltaTime);
            agent.nextPosition = transform.position;
        }

        /// <summary>读取 NavMeshAgent 当前建议方向，期望速度为空时退回下一个转向点。</summary>
        private Vector3 GetAgentMoveDirection()
        {
            Vector3 move = agent.desiredVelocity;
            if (move.sqrMagnitude <= 0.0001f && agent.hasPath)
            {
                move = agent.steeringTarget - transform.position;
            }

            return move;
        }

        /// <summary>使用 CharacterController 持续朝直线目的地移动。</summary>
        private void MoveTowardsDestination(float deltaTime)
        {
            if (!HasDestination || controller == null)
            {
                return;
            }

            MoveWithConfiguredSpeed(currentDestination - transform.position, currentDestination, deltaTime);
        }

        /// <summary>使用配置速度计算本帧距离，并沿指定方向推动 CharacterController。</summary>
        private void MoveWithConfiguredSpeed(Vector3 move, Vector3 lookTarget, float deltaTime)
        {
            MoveAlongDirection(move, lookTarget, moveSpeed * deltaTime);
        }

        /// <summary>使用 RootMotion 水平步幅计算本帧距离，并沿指定方向推动 CharacterController。</summary>
        private void MoveWithRootMotionStep(Vector3 move, Vector3 lookTarget, Vector3 rootMotionDelta)
        {
            rootMotionDelta.y = 0f;
            MoveAlongDirection(move, lookTarget, rootMotionDelta.magnitude);
        }

        /// <summary>使用 CharacterController 按指定距离移动，并朝向移动目标。</summary>
        private void MoveAlongDirection(Vector3 move, Vector3 lookTarget, float distance)
        {
            move.y = 0f;
            if (move.sqrMagnitude <= 0.0001f || distance <= 0.0001f)
            {
                return;
            }

            LookAt(lookTarget);
            Vector3 displacement = move.normalized * distance;
            if (Application.isPlaying)
            {
                controller.Move(displacement);
            }
            else
            {
                transform.position += displacement;
            }
        }
    }
}
