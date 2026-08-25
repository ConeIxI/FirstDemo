using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Game.Timeline.Execution
{
    public sealed class ExecutionTransformClip : PlayableAsset, ITimelineClipAsset
    {
        [SerializeField] private Vector3 localPosition;
        [SerializeField] private Vector3 localEulerAngles;
        [SerializeField] private AnimationCurve positionCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private AnimationCurve rotationCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        public ClipCaps clipCaps => ClipCaps.Blending;

        /// <summary>创建处决对位 Playable，并把 Clip 配置写入运行时 Behaviour。</summary>
        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            ScriptPlayable<ExecutionTransformBehaviour> playable = ScriptPlayable<ExecutionTransformBehaviour>.Create(graph);
            ExecutionTransformBehaviour behaviour = playable.GetBehaviour();
            behaviour.LocalPosition = localPosition;
            behaviour.LocalEulerAngles = localEulerAngles;
            behaviour.PositionCurve = positionCurve;
            behaviour.RotationCurve = rotationCurve;
            return playable;
        }
    }
}
