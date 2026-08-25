using UnityEngine;

namespace Game.Timeline.Execution
{
    public sealed class ExecutionTransformTarget : MonoBehaviour
    {
        public Transform ActorRoot { get; private set; }
        public Transform ActorFacingRoot { get; private set; }
        public Transform TargetRoot { get; private set; }

        /// <summary>绑定本次处决对位需要写入的玩家根节点、玩家朝向节点和作为参考的敌人根节点。</summary>
        public void Bind(Transform actorRoot, Transform actorFacingRoot, Transform targetRoot)
        {
            ActorRoot = actorRoot;
            ActorFacingRoot = actorFacingRoot;
            TargetRoot = targetRoot;
        }

        /// <summary>清理处决对位绑定，Timeline 停止后不再写入 Transform。</summary>
        public void Clear()
        {
            ActorRoot = null;
            ActorFacingRoot = null;
            TargetRoot = null;
        }

        /// <summary>把计算出的世界位置写入玩家根节点，并把世界旋转写入玩家模型朝向节点。</summary>
        public void ApplyWorldPose(Vector3 worldPosition, Quaternion worldRotation)
        {
            ActorRoot.position = worldPosition;
            ActorFacingRoot.rotation = worldRotation;
        }
    }
}
