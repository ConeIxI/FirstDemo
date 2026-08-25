using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Game.Timeline.Execution
{
    [TrackColor(0.25f, 0.6f, 1f)]
    [TrackClipType(typeof(ExecutionTransformClip))]
    [TrackBindingType(typeof(ExecutionTransformTarget))]
    public sealed class ExecutionTransformTrack : TrackAsset
    {
        /// <summary>创建处决 Transform 轨道 Mixer，由 Mixer 统一决定本帧是否写入玩家根节点。</summary>
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            return ScriptPlayable<ExecutionTransformMixer>.Create(graph, inputCount);
        }
    }
}
