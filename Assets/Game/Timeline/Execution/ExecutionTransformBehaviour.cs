using UnityEngine;
using UnityEngine.Playables;

namespace Game.Timeline.Execution
{
    public sealed class ExecutionTransformBehaviour : PlayableBehaviour
    {
        public Vector3 LocalPosition;
        public Vector3 LocalEulerAngles;
        public AnimationCurve PositionCurve;
        public AnimationCurve RotationCurve;

        private bool m_hasCapturedStartPose;
        private Vector3 m_startPosition;

        /// <summary>开始播放 Clip 时重置起始位置缓存，下一帧按玩家真实当前位置捕获。</summary>
        public override void OnBehaviourPlay(Playable playable, FrameData info)
        {
            m_hasCapturedStartPose = false;
        }

        /// <summary>按 Clip 标准化进度计算玩家根节点世界位置，并让玩家模型瞬间面向敌人。</summary>
        public bool TryEvaluatePose(
            Playable playable,
            ExecutionTransformTarget binding,
            out Vector3 position,
            out Quaternion rotation)
        {
            position = default;
            rotation = Quaternion.identity;
            if (binding == null || binding.ActorRoot == null || binding.ActorFacingRoot == null || binding.TargetRoot == null)
            {
                return false;
            }

            if (!m_hasCapturedStartPose)
            {
                m_startPosition = binding.ActorRoot.position;
                m_hasCapturedStartPose = true;
            }

            double duration = playable.GetDuration();
            float normalizedTime = duration <= 0d ? 1f : Mathf.Clamp01((float)(playable.GetTime() / duration));
            float positionProgress = PositionCurve == null ? normalizedTime : PositionCurve.Evaluate(normalizedTime);
            Vector3 targetPosition = binding.TargetRoot.TransformPoint(LocalPosition);
            // 处决对位只需要水平转向敌人，避免根节点高度差导致玩家出现俯仰旋转。
            Vector3 lookDirection = binding.TargetRoot.position - targetPosition;
            lookDirection.y = 0f;
            Quaternion targetRotation = Quaternion.LookRotation(lookDirection, Vector3.up) * Quaternion.Euler(LocalEulerAngles);

            position = Vector3.LerpUnclamped(m_startPosition, targetPosition, positionProgress);
            rotation = targetRotation;
            return true;
        }
    }
}
