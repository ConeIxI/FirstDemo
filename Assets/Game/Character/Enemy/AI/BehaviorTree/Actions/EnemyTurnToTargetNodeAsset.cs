using Game.Character.Enemy.Components;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Turn To Target")]
    public sealed class EnemyTurnToTargetNodeAsset : ActionNodeAsset
    {
        private const string TurnAngleParameterName = "turnAngle";
        private const float RightAngle = 90f;
        private const float RightBackAngle = 135f;
        private const float BackAngle = 180f;
        private const float LeftBackAngle = -135f;
        private const float LeftAngle = -90f;
        private static readonly float[] TurnAngles =
        {
            RightAngle,
            RightBackAngle,
            BackAngle,
            LeftBackAngle,
            LeftAngle
        };

        /// <summary>创建转身动作运行时节点，启动 Turn BlendTree 后等待动画播放完成。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new EnemyTurnToTargetNode(this);
        }

        /// <summary>资产层不直接执行，转身动作由运行时节点读取目标角度并处理。</summary>
        protected override BehaviorTreeStatus Execute(BehaviorTreeContext context)
        {
            return BehaviorTreeStatus.Failure;
        }

        private sealed class EnemyTurnToTargetNode : BehaviorTreeNode
        {
            private bool hasStarted;
            private string activeTurnAnimation;

            /// <summary>绑定转身动作资产，运行时状态保存在节点实例中。</summary>
            public EnemyTurnToTargetNode(EnemyTurnToTargetNodeAsset asset)
                : base(asset)
            {
            }

            /// <summary>首次进入时设置 turnAngle 并播放 Turn，后续等待 Root Motion 转身动画播完。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (!EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                    || !controller.Blackboard.HasCombatTarget)
                {
                    Reset();
                    return BehaviorTreeStatus.Failure;
                }

                EnemyMovementComponent movement = controller.Context != null ? controller.Context.Movement : null;
                if (movement == null)
                {
                    Debug.LogError("转身动作缺少 EnemyMovementComponent，无法停止位移。", controller);
                    Reset();
                    return BehaviorTreeStatus.Failure;
                }

                if (!hasStarted)
                {
                    return StartTurn(controller, movement);
                }

                return IsTurnAnimationFinished(controller)
                    ? BehaviorTreeStatus.Success
                    : BehaviorTreeStatus.Running;
            }

            /// <summary>清理本次转身播放状态，允许下次进入重新计算角度档位。</summary>
            public override void Reset()
            {
                hasStarted = false;
                activeTurnAnimation = null;
            }

            /// <summary>启动转身动画，按目标相对角度写入 Turn BlendTree 的离散参数。</summary>
            private BehaviorTreeStatus StartTurn(AIController controller, EnemyMovementComponent movement)
            {
                EnemyAnimationComponent animation = controller.Context != null ? controller.Context.Animation : null;
                if (animation == null || controller.Definition == null)
                {
                    Reset();
                    return BehaviorTreeStatus.Failure;
                }

                movement.Stop();
                activeTurnAnimation = controller.Definition.AnimationConfig.turnAnimation;
                animation.SetFloat(TurnAngleParameterName, SelectTurnParameter(controller));
                if (!animation.TryPlay(activeTurnAnimation, forceRestart: true))
                {
                    Reset();
                    return BehaviorTreeStatus.Failure;
                }

                hasStarted = true;
                return BehaviorTreeStatus.Running;
            }

            /// <summary>按玩家相对敌人的水平有符号角选择最近的转身动画档位。</summary>
            private static float SelectTurnParameter(AIController controller)
            {
                float signedAngle = CalculateSignedTargetAngle(controller);
                float bestDelta = float.MaxValue;
                int bestIndex = 0;
                for (int i = 0; i < TurnAngles.Length; i++)
                {
                    float delta = Mathf.Abs(Mathf.DeltaAngle(signedAngle, TurnAngles[i]));
                    if (delta < bestDelta)
                    {
                        bestDelta = delta;
                        bestIndex = i;
                    }
                }

                return bestIndex;
            }

            /// <summary>计算战斗目标相对敌人当前前方的水平有符号角，右侧为正、左侧为负。</summary>
            private static float CalculateSignedTargetAngle(AIController controller)
            {
                Vector3 direction = controller.Blackboard.CombatTarget.position - controller.transform.position;
                direction.y = 0f;
                if (direction.sqrMagnitude <= 0.0001f)
                {
                    return BackAngle;
                }

                Vector3 forward = controller.transform.forward;
                forward.y = 0f;
                return Vector3.SignedAngle(forward.normalized, direction.normalized, Vector3.up);
            }

            /// <summary>等待 Turn 动画播放到末尾，Root Motion 负责实际旋转。</summary>
            private bool IsTurnAnimationFinished(AIController controller)
            {
                EnemyAnimationComponent animation = controller.Context != null ? controller.Context.Animation : null;
                if (animation == null || string.IsNullOrEmpty(activeTurnAnimation))
                {
                    return true;
                }

                return !animation.IsPlaying(activeTurnAnimation, out float normalizedTime)
                    || normalizedTime >= 1f;
            }
        }
    }
}