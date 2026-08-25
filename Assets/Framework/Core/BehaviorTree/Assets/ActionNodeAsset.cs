using GameMain2.Framework.Core.BehaviorTree.Runtime;

namespace GameMain2.Framework.Core.BehaviorTree.Assets
{
    /// <summary>提供动作叶子节点资产的基础行为。</summary>
    public abstract class ActionNodeAsset : BehaviorTreeNodeAsset
    {
        /// <summary>创建动作节点默认运行时实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new DefaultActionNode(this);
        }

        /// <summary>执行动作逻辑，并直接返回行为树状态。</summary>
        protected abstract BehaviorTreeStatus Execute(BehaviorTreeContext context);

        private sealed class DefaultActionNode : BehaviorTreeNode
        {
            private readonly ActionNodeAsset asset;

            /// <summary>绑定动作节点资产，供 Tick 时调用动作执行逻辑。</summary>
            public DefaultActionNode(ActionNodeAsset asset)
                : base(asset)
            {
                this.asset = asset;
            }

            /// <summary>执行动作逻辑，并原样返回动作给出的状态。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                return asset.Execute(context);
            }

            /// <summary>动作节点默认无运行时状态，重置时不执行额外逻辑。</summary>
            public override void Reset()
            {
            }
        }
    }
}
