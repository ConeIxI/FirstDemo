using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree.Assets
{
    /// <summary>定义在子节点成功后立即重置并持续运行的无限重复装饰节点资产。</summary>
    [CreateAssetMenu(fileName = "RepeatForeverNode", menuName = "Game/Behavior Tree/Repeat Forever Node")]
    public sealed class RepeatForeverNodeAsset : DecoratorNodeAsset
    {
        /// <summary>创建无限重复装饰节点的独立运行时实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new RepeatForeverNode(this, CreateRuntimeChild());
        }

        private sealed class RepeatForeverNode : BehaviorTreeNode
        {
            private readonly BehaviorTreeNode child;

            /// <summary>绑定来源资产及其独立运行时子节点。</summary>
            public RepeatForeverNode(BehaviorTreeNodeAsset asset, BehaviorTreeNode child)
                : base(asset)
            {
                this.child = child;
            }

            /// <summary>子节点成功后重置并保持运行，其他状态按原样返回。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (child == null)
                {
                    return BehaviorTreeStatus.Failure;
                }

                BehaviorTreeStatus status = child.Tick(context);
                if (status == BehaviorTreeStatus.Success)
                {
                    child.Reset();
                    return BehaviorTreeStatus.Running;
                }

                return status;
            }

            /// <summary>重置无限重复节点的运行时子节点。</summary>
            public override void Reset()
            {
                if (child != null)
                {
                    child.Reset();
                }
            }
        }
    }
}
