using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree.Assets
{
    /// <summary>定义把子节点终态统一转换为成功的 AlwaysSuccess 装饰节点资产。</summary>
    [CreateAssetMenu(fileName = "AlwaysSuccessNode", menuName = "Game/Behavior Tree/Always Success Node")]
    public sealed class AlwaysSuccessNodeAsset : DecoratorNodeAsset
    {
        /// <summary>创建 AlwaysSuccess 运行时节点实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new AlwaysSuccessNode(this, CreateRuntimeChild());
        }

        private sealed class AlwaysSuccessNode : BehaviorTreeNode
        {
            private readonly BehaviorTreeNode child;

            /// <summary>绑定来源资产和运行时子节点。</summary>
            public AlwaysSuccessNode(BehaviorTreeNodeAsset asset, BehaviorTreeNode child)
                : base(asset)
            {
                this.child = child;
            }

            /// <summary>执行子节点，并将非运行中的终态统一转换为成功。</summary>
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

                return BehaviorTreeStatus.Success;
            }

            /// <summary>重置 AlwaysSuccess 的运行时子节点。</summary>
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
