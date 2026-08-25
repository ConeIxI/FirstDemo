using UnityEngine;
using UnityEngine.Playables;

namespace Game.Timeline.Execution
{
    public sealed class ExecutionTransformMixer : PlayableBehaviour
    {
        /// <summary>汇总本帧活跃 Clip 的姿态结果，并且只向玩家 Transform 写入一次。</summary>
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            ExecutionTransformTarget binding = playerData as ExecutionTransformTarget;
            if (binding == null)
            {
                return;
            }

            int inputCount = playable.GetInputCount();
            float totalWeight = 0f;
            Vector3 blendedPosition = Vector3.zero;
            Quaternion blendedRotation = Quaternion.identity;
            bool hasPose = false;

            for (int i = 0; i < inputCount; i++)
            {
                float inputWeight = playable.GetInputWeight(i);
                if (inputWeight <= 0f)
                {
                    continue;
                }

                ScriptPlayable<ExecutionTransformBehaviour> inputPlayable =
                    (ScriptPlayable<ExecutionTransformBehaviour>)playable.GetInput(i);
                ExecutionTransformBehaviour behaviour = inputPlayable.GetBehaviour();
                if (!behaviour.TryEvaluatePose(inputPlayable, binding, out Vector3 position, out Quaternion rotation))
                {
                    continue;
                }

                blendedPosition += position * inputWeight;
                blendedRotation = hasPose
                    ? Quaternion.SlerpUnclamped(blendedRotation, rotation, inputWeight / (totalWeight + inputWeight))
                    : rotation;
                totalWeight += inputWeight;
                hasPose = true;
            }

            if (!hasPose || totalWeight <= 0f)
            {
                return;
            }

            binding.ApplyWorldPose(blendedPosition / totalWeight, blendedRotation);
        }
    }
}
