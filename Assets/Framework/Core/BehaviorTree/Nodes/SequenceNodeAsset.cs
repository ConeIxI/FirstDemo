using System.Collections.Generic;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree.Assets
{
    /// <summary>定义按顺序要求所有子节点成功的 Sequence 组合节点资产。</summary>
    [CreateAssetMenu(fileName = "SequenceNode", menuName = "Game/Behavior Tree/Sequence Node")]
    public sealed class SequenceNodeAsset : CompositeNodeAsset
    {
        /// <summary>创建 Sequence 运行时节点实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new SequenceNode(this, CreateRuntimeChildren());
        }

        private sealed class SequenceNode : BehaviorTreeNode
        {
            private readonly List<BehaviorTreeNode> children;
            private int currentIndex;

            /// <summary>绑定来源资产和运行时子节点列表。</summary>
            public SequenceNode(BehaviorTreeNodeAsset asset, List<BehaviorTreeNode> children)
                : base(asset)
            {
                this.children = children;
            }

            /// <summary>按 Sequence 语义从当前子节点开始执行，直到失败、运行中或全部成功。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                while (currentIndex < children.Count)
                {
                    BehaviorTreeStatus status = children[currentIndex].Tick(context);
                    if (status == BehaviorTreeStatus.Failure)
                    {
                        Reset();
                        return BehaviorTreeStatus.Failure;
                    }

                    if (status == BehaviorTreeStatus.Running)
                    {
                        return BehaviorTreeStatus.Running;
                    }

                    currentIndex++;
                }

                Reset();
                return BehaviorTreeStatus.Success;
            }

            /// <summary>重置 Sequence 当前索引，并重置所有运行时子节点。</summary>
            public override void Reset()
            {
                currentIndex = 0;
                for (int i = 0; i < children.Count; i++)
                {
                    children[i].Reset();
                }
            }
        }
    }
}
