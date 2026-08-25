using System.Collections.Generic;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree.Assets
{
    /// <summary>定义按顺序选择第一个成功子节点的 Selector 组合节点资产。</summary>
    [CreateAssetMenu(fileName = "SelectorNode", menuName = "Game/Behavior Tree/Selector Node")]
    public sealed class SelectorNodeAsset : CompositeNodeAsset
    {
        /// <summary>创建 Selector 运行时节点实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new SelectorNode(this, CreateRuntimeChildren());
        }

        private sealed class SelectorNode : BehaviorTreeNode
        {
            private readonly List<BehaviorTreeNode> children;
            private int currentIndex;

            /// <summary>绑定来源资产和运行时子节点列表。</summary>
            public SelectorNode(BehaviorTreeNodeAsset asset, List<BehaviorTreeNode> children)
                : base(asset)
            {
                this.children = children;
            }

            /// <summary>按 Selector 语义从当前子节点开始执行，直到成功、运行中或全部失败。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                while (currentIndex < children.Count)
                {
                    BehaviorTreeStatus status = children[currentIndex].Tick(context);
                    if (status == BehaviorTreeStatus.Success)
                    {
                        Reset();
                        return BehaviorTreeStatus.Success;
                    }

                    if (status == BehaviorTreeStatus.Running)
                    {
                        return BehaviorTreeStatus.Running;
                    }

                    currentIndex++;
                }

                Reset();
                return BehaviorTreeStatus.Failure;
            }

            /// <summary>重置 Selector 当前索引，并重置所有运行时子节点。</summary>
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
