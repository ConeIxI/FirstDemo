using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree.Assets
{
    /// <summary>定义把子节点终态统一转换为失败的 AlwaysFailure 装饰节点资产。</summary>
    [CreateAssetMenu(fileName = "AlwaysFailureNode", menuName = "Game/Behavior Tree/Always Failure Node")]
    public sealed class AlwaysFailureNodeAsset : DecoratorNodeAsset
    {
        /// <summary>创建 AlwaysFailure 运行时节点实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new AlwaysFailureNode(this, CreateRuntimeChild());
        }

        private sealed class AlwaysFailureNode : BehaviorTreeNode
        {
            private readonly BehaviorTreeNode child;

            /// <summary>绑定来源资产和运行时子节点。</summary>
            public AlwaysFailureNode(BehaviorTreeNodeAsset asset, BehaviorTreeNode child)
                : base(asset)
            {
                this.child = child;
            }

            /// <summary>执行子节点，并将非运行中的终态统一转换为失败。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (child == null)
                {
                    return BehaviorTreeStatus.Failure;
                }

                BehaviorTreeStatus status = child.Tick(context);
                if (status == BehaviorTreeStatus.Running)
                {
                    return BehaviorTreeStatus.Running;
                }

                return BehaviorTreeStatus.Failure;
            }

            /// <summary>重置 AlwaysFailure 的运行时子节点。</summary>
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
