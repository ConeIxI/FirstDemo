using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree
{
    /// <summary>保存行为树单次执行所需的基础运行时信息。</summary>
    public sealed class BehaviorTreeContext
    {
        /// <summary>创建行为树上下文，并从拥有者对象缓存 Transform。</summary>
        public BehaviorTreeContext(GameObject owner, BehaviorTreeBlackboard blackboard)
        {
            Owner = owner;
            Transform = owner.transform;
            Blackboard = blackboard;
        }

        /// <summary>行为树所属的游戏对象。</summary>
        public GameObject Owner { get; }

        /// <summary>行为树所属对象的 Transform。</summary>
        public Transform Transform { get; }

        /// <summary>行为树节点共享的黑板数据。</summary>
        public BehaviorTreeBlackboard Blackboard { get; }

        /// <summary>本轮行为树更新使用的时间步长。</summary>
        public float DeltaTime { get; set; }
    }
}
