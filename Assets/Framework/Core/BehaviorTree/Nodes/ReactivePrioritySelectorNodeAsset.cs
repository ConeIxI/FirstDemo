using System.Collections.Generic;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree.Assets
{
    /// <summary>定义每帧从最高优先级重新评估并可抢占运行节点的响应式选择器资产。</summary>
    [CreateAssetMenu(fileName = "ReactivePrioritySelectorNode", menuName = "Game/Behavior Tree/Reactive Priority Selector Node")]
    public sealed class ReactivePrioritySelectorNodeAsset : CompositeNodeAsset
    {
        /// <summary>创建响应式优先级选择器的独立运行时节点实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new ReactivePrioritySelectorNode(this, CreateRuntimeChildren());
        }

        private sealed class ReactivePrioritySelectorNode : BehaviorTreeNode
        {
            private readonly List<BehaviorTreeNode> children;
            private int runningChildIndex = -1;

            /// <summary>绑定来源资产及其独立运行时子节点列表。</summary>
            public ReactivePrioritySelectorNode(BehaviorTreeNodeAsset asset, List<BehaviorTreeNode> children)
                : base(asset)
            {
                this.children = children;
            }

            /// <summary>每帧从最高优先级开始评估，并在高优先级节点运行时抢占旧运行节点。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                for (int i = 0; i < children.Count; i++)
                {
                    BehaviorTreeStatus status = children[i].Tick(context);
                    if (status == BehaviorTreeStatus.Success)
                    {
                        Reset();
                        return BehaviorTreeStatus.Success;
                    }

                    if (status == BehaviorTreeStatus.Running)
                    {
                        if (runningChildIndex >= 0 && runningChildIndex != i)
                        {
                            children[runningChildIndex].OnLayerExit();
                            children[runningChildIndex].Reset();
                        }

                        if (runningChildIndex != i)
                        {
                            runningChildIndex = i;
                            children[runningChildIndex].OnLayerEnter(context);
                        }

                        return BehaviorTreeStatus.Running;
                    }

                    if (runningChildIndex == i)
                    {
                        children[runningChildIndex].OnLayerExit();
                        runningChildIndex = -1;
                    }
                }

                Reset();
                return BehaviorTreeStatus.Failure;
            }

            /// <summary>重置运行索引和所有运行时子节点。</summary>
            public override void Reset()
            {
                if (runningChildIndex >= 0)
                {
                    children[runningChildIndex].OnLayerExit();
                }

                runningChildIndex = -1;
                for (int i = 0; i < children.Count; i++)
                {
                    children[i].Reset();
                }
            }
        }
    }
}
