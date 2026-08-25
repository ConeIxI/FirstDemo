using Game.Character.Enemy.Components;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    public enum EnemyCombatMoveMode
    {
        Chase,
        Approach
    }

    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Combat Move")]
    public sealed class EnemyCombatMoveNodeAsset : ActionNodeAsset
    {
        [SerializeField] private EnemyCombatMoveMode mode;

        /// <summary>创建战斗移动运行时节点，移动时每帧读取实时战斗目标位置。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new EnemyCombatMoveNode(this);
        }

        /// <summary>默认动作入口不会被使用，战斗移动需要读取节点配置模式。</summary>
        protected override BehaviorTreeStatus Execute(BehaviorTreeContext context)
        {
            return BehaviorTreeStatus.Failure;
        }

        private sealed class EnemyCombatMoveNode : BehaviorTreeNode
        {
            private const float DestinationRefreshSqrThreshold = 0.0001f;

            private readonly EnemyCombatMoveNodeAsset asset;
            private bool hasActiveMoveDestination;
            private Vector3 activeMoveDestination;

            /// <summary>绑定战斗移动资产，供运行时读取 Chase 或 Approach 模式。</summary>
            public EnemyCombatMoveNode(EnemyCombatMoveNodeAsset asset)
                : base(asset)
            {
                this.asset = asset;
            }

            /// <summary>按移动模式接近实时战斗目标；目标失效或缺少移动组件时失败退出。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (!EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                    || controller.Blackboard.CombatTarget == null)
                {
                    return BehaviorTreeStatus.Failure;
                }

                EnemyMovementComponent movement = controller.Context != null ? controller.Context.Movement : null;
                if (movement == null)
                {
                    Debug.LogError("战斗移动缺少 EnemyMovementComponent，无法追击战斗目标。", controller);
                    return BehaviorTreeStatus.Failure;
                }

                Transform target = controller.Blackboard.CombatTarget;
                PlayMoveAnimation(controller, asset.mode);
                controller.Blackboard.SetCombatIntent(EnemyCombatIntent.Approach);
                MoveByNavMesh(controller, movement, target.position);
                return BehaviorTreeStatus.Running;
            }

            /// <summary>重置战斗移动目的地缓存，下一次进入时重新下发寻路目标。</summary>
            public override void Reset()
            {
                hasActiveMoveDestination = false;
                activeMoveDestination = default;
            }

            /// <summary>根据追击或接近模式播放跑步或普通移动动画。</summary>
            private static void PlayMoveAnimation(AIController controller, EnemyCombatMoveMode mode)
            {
                if (controller.Context == null || controller.Context.Animation == null)
                {
                    return;
                }

                string animationName = mode == EnemyCombatMoveMode.Chase
                    ? (controller.Definition != null ? controller.Definition.AnimationConfig.runAnimation : null)
                    : (controller.Definition != null ? controller.Definition.AnimationConfig.moveAnimation : null);
                controller.Context.Animation.TryPlay(animationName);
            }

            /// <summary>下发战斗移动寻路目标，并让移动动画 RootMotion 步幅沿 NavMesh 路径消耗。</summary>
            private void MoveByNavMesh(
                AIController controller,
                EnemyMovementComponent movement,
                Vector3 destination)
            {
                if (controller.Context != null && controller.Context.Animation != null)
                {
                    controller.Context.Animation.SetRootMotionSuppressed(true);
                }

                if (hasActiveMoveDestination
                    && (activeMoveDestination - destination).sqrMagnitude <= DestinationRefreshSqrThreshold)
                {
                    return;
                }

                hasActiveMoveDestination = true;
                activeMoveDestination = destination;
                movement.MoveTo(destination);
            }
        }
    }
}
