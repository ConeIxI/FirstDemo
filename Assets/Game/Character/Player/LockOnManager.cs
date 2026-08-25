using System;
using System.Collections.Generic;
using Cinemachine;
using Game.Battle.Ability;
using Game.Character.Enemy.AI;
using Game.Character.Enemy.Components;
using Game.Character.Enemy.Core;
using Game.Character.Player.Execution;
using GameMain2.Framework.Manager;
using GameMain2.Scripts.UI;
using TMPro;
using UnityEngine;

namespace GameMain2.Scripts.Character
{
 public class LockOnManager : MonoBehaviour
    {
        private const string CameraTargetName = "CameraTarget";

        [Header("检测参数")]
        [SerializeField] private float lockOnRange = 15f;
        [SerializeField] private float autoUnlockRange = 18f;
        [SerializeField] private float lockOnFovDot = 0f;   // 0 = 摄像机前方 90° 内
        [SerializeField] private LayerMask enemyLayer;

        [Header("摄像机")]
        [SerializeField] private CinemachineVirtualCamera[] virtualCameras;
        [SerializeField] private Transform playerHeadTransform;  // 解锁后 LookAt 恢复的目标
        [SerializeField] private CinemachineTargetGroup lockOnTargetGroup;
        [SerializeField] private CinemachineBrain brain;

        [Header("光圈指示器")]
        [SerializeField] private GameObject lockOnRingUI;
        [SerializeField] private float ringHeadOffset = 2.0f;
        [SerializeField] private Vector2 ringSize = new Vector2(88f, 88f);
        [SerializeField] private float ringScale = 0.65f;

        [Header("锁定移动速度")]
        [SerializeField] private float lockOnMoveSpeed = 4f;

        // ── 公开状态 ────────────────────────────────────────────────
        public bool IsLockedOn { get; private set; }
        public Transform CurrentTarget { get; private set; }
        public float LockOnMoveSpeed => lockOnMoveSpeed;

        // ── 私有 ────────────────────────────────────────────────────

        private CinemachinePOV _pov;
        private RectTransform _lockOnRingRect;
        private bool _lockCameraRaised;
        private EnemyAttributeComponent _currentTargetAttribute;
        private CombatAbilitySystem _playerAbilitySystem;

        // ────────────────────────────────────────────────────────────

        private void Awake()
        {
            // EnableCameraControl();
            ResolvePlayerAbilitySystem();
            EnsureLockOnRing();
            SetLockOnRingVisible(false);
        }

        private void Update()
        {
            if (IsPlayerDead())
            {
                if (IsLockedOn)
                {
                    Unlock();
                }

                return;
            }

            HandleInput();

            if (IsLockedOn)
            {
                CheckAutoUnlock();
            }
        }

        private void LateUpdate()
        {
            if (!IsLockedOn)
            {
                SetLockOnRingVisible(false);
                return;
            }

            if (CurrentTarget == null)
            {
                Unlock();
                return;
            }

            UpdateLockOnRing();
        }

        private void UpdateLockOnRing()
        {
            if (!EnsureLockOnRing())
            {
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                SetLockOnRingVisible(false);
                return;
            }

            Vector3 screenPos = mainCamera.WorldToScreenPoint(CurrentTarget.position + Vector3.up * ringHeadOffset);
            if (screenPos.z < 0f)
            {
                SetLockOnRingVisible(false);
                return;
            }

            SetLockOnRingVisible(true);
            _lockOnRingRect.position = screenPos;
        }

        /// <summary>确保锁定光圈实例和 RectTransform 可用，并同步到正确 UI 层级。</summary>
        private bool EnsureLockOnRing()
        {
            if (lockOnRingUI == null)
            {
                CreateRuntimeLockOnRing();
            }

            if (lockOnRingUI == null)
            {
                return false;
            }

            if (_lockOnRingRect == null)
            {
                _lockOnRingRect = lockOnRingUI.GetComponent<RectTransform>();
            }

            EnsureLockOnRingParent();
            ApplyLockOnRingScale();

            return _lockOnRingRect != null;
        }

        /// <summary>确保锁定光圈挂在 HUD 层级内，避免盖住背包、暂停等面板。</summary>
        private void EnsureLockOnRingParent()
        {
            if (_lockOnRingRect == null)
            {
                return;
            }

            Canvas canvas = _lockOnRingRect.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return;
            }

            Transform targetParent = FindLockOnRingParent(canvas);
            if (_lockOnRingRect.parent == targetParent)
            {
                return;
            }

            _lockOnRingRect.SetParent(targetParent, false);
            if (targetParent.GetComponent<BattleHudPanel>() != null)
            {
                _lockOnRingRect.SetAsLastSibling();
            }
            else
            {
                _lockOnRingRect.SetAsFirstSibling();
            }
        }

        /// <summary>按配置缩小锁定光圈，兼容场景内已有 UI 和运行时兜底 UI。</summary>
        private void ApplyLockOnRingScale()
        {
            if (_lockOnRingRect != null)
            {
                _lockOnRingRect.localScale = new Vector3(ringScale, ringScale, 1f);
            }
        }

        /// <summary>运行时创建锁定光圈兜底 UI，并挂到 HUD 或普通 UI 层级内。</summary>
        private void CreateRuntimeLockOnRing()
        {
            Canvas canvas = FindLockOnCanvas();
            if (canvas == null)
            {
                GameObject canvasGo = new GameObject("RuntimeLockOnCanvas", typeof(RectTransform));
                canvas = canvasGo.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 999;
            }

            GameObject ringGo = new GameObject("LockOnRingUI", typeof(RectTransform));
            Transform parent = FindLockOnRingParent(canvas);
            ringGo.transform.SetParent(parent, false);
            if (parent.GetComponent<BattleHudPanel>() != null)
            {
                ringGo.transform.SetAsLastSibling();
            }
            else
            {
                ringGo.transform.SetAsFirstSibling();
            }

            RectTransform rect = ringGo.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = ringSize;

            TextMeshProUGUI ringText = ringGo.AddComponent<TextMeshProUGUI>();
            ringText.text = "O";
            ringText.fontSize = 72f;
            ringText.alignment = TextAlignmentOptions.Center;
            ringText.color = new Color(1f, 0.28f, 0.18f, 0.9f);
            ringText.raycastTarget = false;

            lockOnRingUI = ringGo;
            _lockOnRingRect = rect;
        }

        /// <summary>查找锁定光圈应挂载的 HUD 父节点，优先跟随 BattleHud，其次落到 Normal 层。</summary>
        private static Transform FindLockOnRingParent(Canvas canvas)
        {
            BattleHudPanel battleHud = FindObjectOfType<BattleHudPanel>();
            if (battleHud != null)
            {
                return battleHud.transform;
            }

            Transform normalLayer = canvas.transform.Find("Normal");
            return normalLayer != null ? normalLayer : canvas.transform;
        }

        /// <summary>查找锁定 UI 可用的全局 Canvas，优先复用 UIManager 创建的 GlobalUICanvas。</summary>
        private static Canvas FindLockOnCanvas()
        {
            GameObject globalCanvas = GameObject.Find("GlobalUICanvas");
            if (globalCanvas != null && globalCanvas.TryGetComponent(out Canvas canvas))
            {
                return canvas;
            }

            Canvas[] canvases = FindObjectsOfType<Canvas>();
            for (int i = 0; i < canvases.Length; i++)
            {
                if (canvases[i].renderMode == RenderMode.ScreenSpaceOverlay)
                {
                    return canvases[i];
                }
            }

            return canvases.Length > 0 ? canvases[0] : null;
        }

        private void SetLockOnRingVisible(bool visible)
        {
            if (lockOnRingUI != null && lockOnRingUI.activeSelf != visible)
            {
                lockOnRingUI.SetActive(visible);
            }
        }

        // ── 输入处理 ─────────────────────────────────────────────────

        private void HandleInput()
        {
            if (InputManager.Instance.IsLockOnPressed())
            {
                if (IsLockedOn)
                    Unlock();
                else
                    TryLockOn();
                return;
            }


            // if (IsLockedOn)
            // {
            //     float scroll = InputManager.Instance.GetScrollDelta();
            //     if (scroll > 0f) SwitchTarget(1);
            //     else if (scroll < 0f) SwitchTarget(-1);
            // }
        }

        // ── 公开方法 ─────────────────────────────────────────────────

        /// <summary>检测并锁定最佳目标</summary>
        public void TryLockOn()
        {
            if (IsPlayerDead())
            {
                return;
            }

            Transform target = FindBestTarget();
            if (target != null)
            {
                LockOnTo(target);
                LockCameraEnable();
            }
        }

        /// <summary>解除锁定，恢复摄像机和销毁光圈</summary>
        public void Unlock()
        {
            IsLockedOn = false;
            CurrentTarget = null;
            _currentTargetAttribute = null;
            SetLockOnRingVisible(false);

            if (lockOnTargetGroup != null && playerHeadTransform != null)
            {
                lockOnTargetGroup.m_Targets = new CinemachineTargetGroup.Target[]
                {
                    new CinemachineTargetGroup.Target { target = playerHeadTransform, weight = 1, radius = 0.5f }
                };
            }

            LockCameraUnEnable();
        }

        /// <summary>场景加载后重置普通相机，让初始视角稳定落在玩家后方。</summary>
        public void ResetNormalCameraBehindPlayer()
        {
            ResetLockStateForSceneLoad();

            CinemachineVirtualCamera normalCamera = GetNormalCamera();
            CinemachineFramingTransposer framingTransposer =
                normalCamera.GetCinemachineComponent<CinemachineFramingTransposer>();
            if (framingTransposer == null)
            {
                throw new InvalidOperationException("NormalCamera 缺少 CinemachineFramingTransposer，无法按玩家后方初始化相机。");
            }

            Transform followTarget = normalCamera.Follow != null ? normalCamera.Follow : transform;
            Vector3 targetPoint = followTarget.position + followTarget.TransformVector(framingTransposer.m_TrackedObjectOffset);
            Vector3 playerForward = GetHorizontalPlayerForward();
            Vector3 cameraPosition = targetPoint - playerForward * framingTransposer.m_CameraDistance;
            Quaternion cameraRotation = Quaternion.LookRotation(targetPoint - cameraPosition, Vector3.up);

            // ForceCameraPosition 会同步 POV 轴和 FramingTransposer 缓存，避免下一帧把视角拉回旧方向。
            normalCamera.ForceCameraPosition(cameraPosition, cameraRotation);
            normalCamera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
        }

        /// <summary>切换到下一个 / 上一个目标。direction: +1 向右，-1 向左</summary>
        public void SwitchTarget(int direction)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return;
            }

            List<Transform> candidates = GetValidTargets();
            if (candidates.Count == 0) { Unlock(); return; }

            // 按屏幕 X 坐标从左到右排序
            candidates.Sort((a, b) =>
            {
                float ax = mainCamera.WorldToScreenPoint(a.position).x;
                float bx = mainCamera.WorldToScreenPoint(b.position).x;
                return ax.CompareTo(bx);
            });

            int currentIndex = candidates.IndexOf(CurrentTarget);
            int nextIndex = (currentIndex + direction + candidates.Count) % candidates.Count;
            LockOnTo(candidates[nextIndex]);
        }

        // ── 私有方法 ─────────────────────────────────────────────────

        private void HandleInput_LockOn() { } // reserved

        /// <summary>
        /// 检查是否需要自动解锁。如果当前锁定的目标无效或超出设定的解锁范围，则会尝试切换到新的目标或直接解除锁定。
        /// </summary>
        private void CheckAutoUnlock()
        {
            // 目标死亡：直接解除锁定，避免玩家继续锁住尸体。
            if (IsCurrentTargetDead())
            {
                Unlock();
                return;
            }

            // 目标失效：尝试切换，否则解锁
            if (CurrentTarget == null || !CurrentTarget.gameObject.activeInHierarchy)
            {
                TryAutoSwitch();
                return;
            }

            // 超出范围：解锁
            if (Vector3.Distance(transform.position, CurrentTarget.position) > autoUnlockRange)
                Unlock();
        }

        private void TryAutoSwitch()
        {
            List<Transform> candidates = GetValidTargets();
            if (candidates.Count > 0)
                LockOnTo(candidates[0]);   // GetValidTargets 已按屏幕中心距离排序
            else
                Unlock();
        }

        private void LockOnTo(Transform target)
        {
            if (target == null || IsPlayerDead())
            {
                return;
            }

            EnemyAttributeComponent targetAttribute =
                ResolveLockOnTargetAttribute(target);
            if (IsAttributeDead(targetAttribute))
            {
                return;
            }

            IsLockedOn = true;
            CurrentTarget = target;
            _currentTargetAttribute = targetAttribute;

            // 切换摄像机 LookAt
            if (lockOnTargetGroup != null && playerHeadTransform != null)
            {
                lockOnTargetGroup.m_Targets = new CinemachineTargetGroup.Target[]
                {
                    new CinemachineTargetGroup.Target { target = playerHeadTransform, weight = 1f, radius = 0f },
                    new CinemachineTargetGroup.Target { target = CurrentTarget, weight = 2, radius = 0f }
                };
                
            }
        }


        private Transform FindBestTarget()
        {
            List<Transform> candidates = GetValidTargets();
            return candidates.Count > 0 ? candidates[0] : null;
        }

        /// <summary>
        /// 返回所有满足条件的目标，按与屏幕中心的距离升序排列。
        /// 条件：范围内 + 摄像机前方 + 无遮挡
        /// </summary>
        private List<Transform> GetValidTargets()
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                return new List<Transform>();
            }

            Collider[] colliders = Physics.OverlapSphere(transform.position, lockOnRange, enemyLayer);
            List<Transform> lockOnTargets = CollectLockOnTargets(colliders);
            Vector2 screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            var results = new List<(Transform t, float screenDist)>();

            foreach (Transform target in lockOnTargets)
            {
                Vector3 dir = (target.position - transform.position).normalized;

                // 视野过滤：必须在摄像机前方（dot > lockOnFovDot）
                if (Vector3.Dot(mainCamera.transform.forward, dir) < lockOnFovDot)
                    continue;

                // 视线遮挡：从玩家胸口到敌人胸口做射线，~enemyLayer 检测障碍物
                // 注意：需要确保玩家自身不在 enemyLayer 上，否则射线会命中自身
                if (Physics.Linecast(
                    transform.position + Vector3.up * 1.5f,
                    target.position,
                    ~enemyLayer))
                    continue;

                Vector2 screenPos = mainCamera.WorldToScreenPoint(target.position);
                float dist = Vector2.Distance(screenPos, screenCenter);
                results.Add((target, dist));
            }

            results.Sort((a, b) => a.screenDist.CompareTo(b.screenDist));

            var list = new List<Transform>();
            foreach (var r in results) list.Add(r.t);
            return list;
        }

        /// <summary>将敌人碰撞体解析为根节点下唯一的 CameraTarget 锁定点。</summary>
        private static List<Transform> CollectLockOnTargets(Collider[] colliders)
        {
            var targets = new List<Transform>();
            var uniqueTargets = new HashSet<Transform>();

            foreach (Collider collider in colliders)
            {
                EnemyAgent enemyAgent = collider.GetComponentInParent<EnemyAgent>();
                if (enemyAgent == null || IsEnemyDead(enemyAgent))
                {
                    continue;
                }

                Transform target = enemyAgent.transform.Find(CameraTargetName);
                if (target == null || !target.gameObject.activeInHierarchy || !uniqueTargets.Add(target))
                {
                    continue;
                }

                targets.Add(target);
            }

            return targets;
        }

        
        /// <summary>判断当前缓存的锁定目标是否已经死亡，避免每帧查找组件。</summary>
        private bool IsCurrentTargetDead()
        {
            return CurrentTarget != null && IsAttributeDead(_currentTargetAttribute);
        }

        /// <summary>锁定目标时解析一次敌人属性组件，后续死亡检查直接读缓存。</summary>
        private static EnemyAttributeComponent ResolveLockOnTargetAttribute(
            Transform target)
        {
            if (target == null)
            {
                return null;
            }

            return target.GetComponentInParent<EnemyAttributeComponent>();
        }

        /// <summary>判断已解析的敌人属性组件是否表示死亡。</summary>
        private static bool IsAttributeDead(
            EnemyAttributeComponent attribute)
        {
            return attribute != null && attribute.IsDead;
        }

        /// <summary>判断敌人根对象是否已经死亡，供锁定候选过滤使用。</summary>
        private static bool IsEnemyDead(EnemyAgent enemyAgent)
        {
            return enemyAgent.TryGetComponent(
                out EnemyAttributeComponent attribute)
                && attribute.IsDead;
        }

        /// <summary>缓存玩家自身能力系统，用于死亡后立即解除锁定。</summary>
        private void ResolvePlayerAbilitySystem()
        {
            if (_playerAbilitySystem == null)
            {
                TryGetComponent(out _playerAbilitySystem);
            }
        }

        /// <summary>判断玩家是否已经死亡，死亡后禁止保持或重新进入锁定。</summary>
        private bool IsPlayerDead()
        {
            ResolvePlayerAbilitySystem();
            if (_playerAbilitySystem == null)
            {
                return false;
            }

            ICombatAttributes attributes = _playerAbilitySystem.Attributes;
            return _playerAbilitySystem.HasTag(CombatTag.Dead)
                || (attributes != null && attributes.IsDead);
        }

        /// <summary>进入锁定状态时提高锁定相机优先级，让 Cinemachine 切到锁定构图。</summary>
        private void LockCameraEnable()
        {
            if (_lockCameraRaised || virtualCameras == null || virtualCameras.Length <= 1 || virtualCameras[1] == null)
            {
                return;
            }

            virtualCameras[1].Priority += 20;
            _lockCameraRaised = true;
        }

        /// <summary>退出锁定状态时先同步普通相机视角，再恢复普通相机控制。</summary>
        private void LockCameraUnEnable()
        {
            if (!_lockCameraRaised || virtualCameras == null || virtualCameras.Length <= 1 || virtualCameras[1] == null)
            {
                return;
            }

            SyncNormalCameraToLockCamera();
            virtualCameras[1].Priority -= 20;
            _lockCameraRaised = false;
        }

        /// <summary>把锁定相机最终视角写回普通相机，避免解除锁定时视角跳变。</summary>
        private void SyncNormalCameraToLockCamera()
        {
            if (virtualCameras[0] == null)
            {
                return;
            }

            CameraState lockCameraState = virtualCameras[1].State;
            virtualCameras[0].ForceCameraPosition(lockCameraState.FinalPosition, lockCameraState.FinalOrientation);
            virtualCameras[0].transform.SetPositionAndRotation(lockCameraState.FinalPosition, lockCameraState.FinalOrientation);
        }

        /// <summary>获取普通虚拟相机，缺失时立即暴露配置错误。</summary>
        private CinemachineVirtualCamera GetNormalCamera()
        {
            if (virtualCameras == null || virtualCameras.Length == 0 || virtualCameras[0] == null)
            {
                throw new InvalidOperationException("LockOnManager 缺少 NormalCamera 引用，无法初始化普通相机。");
            }

            return virtualCameras[0];
        }

        /// <summary>场景切换后清理旧锁定状态，不把旧锁定相机视角同步回普通相机。</summary>
        private void ResetLockStateForSceneLoad()
        {
            IsLockedOn = false;
            CurrentTarget = null;
            _currentTargetAttribute = null;
            SetLockOnRingVisible(false);

            if (_lockCameraRaised && virtualCameras != null && virtualCameras.Length > 1 && virtualCameras[1] != null)
            {
                virtualCameras[1].Priority -= 20;
                _lockCameraRaised = false;
            }
        }

        /// <summary>获取玩家水平朝向，忽略坡度造成的上下分量。</summary>
        private Vector3 GetHorizontalPlayerForward()
        {
            Vector3 playerForward = transform.forward;
            playerForward.y = 0f;
            playerForward.Normalize();
            return playerForward;
        }

        /// <summary>锁定目标有效时，让玩家模型朝向当前锁定目标。</summary>
        public void TurnToCurrentTarget()
        {
            if (!IsLockedOn || CurrentTarget == null) return;
            PlayerController controller = GetComponent<PlayerController>();
            if (controller == null) return;

            Vector3 dir = CurrentTarget.position - transform.position;
            controller.Rotate(dir.normalized);
        }

        /// <summary>优先从当前锁定对象解析可处决敌人。</summary>
        public bool TryGetLockedExecutionTarget(float executionRange, out ExecutionTarget target)
        {
            target = default;
            if (!IsLockedOn || CurrentTarget == null)
            {
                return false;
            }

            if (Vector3.Distance(transform.position, CurrentTarget.position) > executionRange)
            {
                return false;
            }

            return TryBuildExecutionTarget(CurrentTarget, out target) && target.IsValidUnbalancedTarget();
        }

        /// <summary>未锁定时按处决范围直接查找最近失衡敌人，不复用摄像机锁定筛选。</summary>
        public bool TryFindNearestExecutionTarget(float executionRange, out ExecutionTarget target)
        {
            target = default;
            Collider[] colliders = Physics.OverlapSphere(transform.position, executionRange, enemyLayer);
            HashSet<EnemyAgent> uniqueAgents = new HashSet<EnemyAgent>();
            float bestDistance = float.PositiveInfinity;

            for (int i = 0; i < colliders.Length; i++)
            {
                EnemyAgent agent = colliders[i].GetComponentInParent<EnemyAgent>();
                if (agent == null || !uniqueAgents.Add(agent))
                {
                    continue;
                }

                if (!TryBuildExecutionTarget(agent.transform, out ExecutionTarget candidateTarget)
                    || !candidateTarget.IsValidUnbalancedTarget())
                {
                    continue;
                }

                float distance = Vector3.Distance(transform.position, candidateTarget.Root.position);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                target = candidateTarget;
            }

            return target.Agent != null;
        }

        /// <summary>把锁定点或敌人子节点解析为处决目标组件集合。</summary>
        private static bool TryBuildExecutionTarget(Transform source, out ExecutionTarget target)
        {
            target = default;
            if (source == null)
            {
                return false;
            }

            EnemyAgent agent = source.GetComponentInParent<EnemyAgent>();
            if (agent == null)
            {
                return false;
            }

            Transform root = agent.transform;
            target = new ExecutionTarget(
                agent,
                root,
                root.GetComponentInChildren<Animator>(),
                root.GetComponent<AIController>(),
                root.GetComponent<EnemyMovementComponent>(),
                root.GetComponent<EnemyCombatComponent>(),
                root.GetComponent<EnemyAttributeComponent>(),
                root.GetComponent<CombatAbilitySystem>());
            return true;
        }
    }
}
