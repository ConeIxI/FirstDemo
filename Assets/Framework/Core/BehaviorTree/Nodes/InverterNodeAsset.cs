using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree.Assets
{
    /// <summary>定义反转子节点成功和失败结果的 Inverter 装饰节点资产。</summary>
    [CreateAssetMenu(fileName = "InverterNode", menuName = "Game/Behavior Tree/Inverter Node")]
    public sealed class InverterNodeAsset : DecoratorNodeAsset
    {
        /// <summary>创建 Inverter 运行时节点实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new InverterNode(this, CreateRuntimeChild());
        }

        private sealed class InverterNode : BehaviorTreeNode
        {
            private readonly BehaviorTreeNode child;

            /// <summary>绑定来源资产和运行时子节点。</summary>
            public InverterNode(BehaviorTreeNodeAsset asset, BehaviorTreeNode child)
                : base(asset)
            {
                this.child = child;
            }

            /// <summary>执行子节点，并按 Inverter 语义反转成功和失败状态。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                if (child == null)
                {
                    return BehaviorTreeStatus.Failure;
                }

                BehaviorTreeStatus status = child.Tick(context);
                if (status == BehaviorTreeStatus.Success)
                {
                    return BehaviorTreeStatus.Failure;
                }

                if (status == BehaviorTreeStatus.Failure)
                {
                    return BehaviorTreeStatus.Success;
                }

                return BehaviorTreeStatus.Running;
            }

            /// <summary>重置 Inverter 的运行时子节点。</summary>
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
