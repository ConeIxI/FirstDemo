using GameMain2.Framework.Core.BehaviorTree.Runtime;

namespace GameMain2.Framework.Core.BehaviorTree.Assets
{
    /// <summary>提供条件叶子节点资产的基础行为。</summary>
    public abstract class ConditionNodeAsset : BehaviorTreeNodeAsset
    {
        /// <summary>创建条件节点默认运行时实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new DefaultConditionNode(this);
        }

        /// <summary>评估条件是否成立，true 映射为成功，false 映射为失败。</summary>
        protected abstract bool Evaluate(BehaviorTreeContext context);

        private sealed class DefaultConditionNode : BehaviorTreeNode
        {
            private readonly ConditionNodeAsset asset;

            /// <summary>绑定条件节点资产，供 Tick 时调用条件评估逻辑。</summary>
            public DefaultConditionNode(ConditionNodeAsset asset)
                : base(asset)
            {
                this.asset = asset;
            }

            /// <summary>执行条件评估，并将布尔结果转换为行为树状态。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                return asset.Evaluate(context) ? BehaviorTreeStatus.Success : BehaviorTreeStatus.Failure;
            }

            /// <summary>条件节点无运行时状态，重置时不执行额外逻辑。</summary>
            public override void Reset()
            {
            }
        }
    }
}
