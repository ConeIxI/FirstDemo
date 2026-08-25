using GameMain2.Framework.Core.BehaviorTree.Assets;

namespace GameMain2.Framework.Core.BehaviorTree.Runtime
{
    public abstract class BehaviorTreeNode
    {
        /// <summary>绑定节点来源资产，便于运行时回溯配置。</summary>
        protected BehaviorTreeNode(BehaviorTreeNodeAsset asset)
        {
            Asset = asset;
        }

        /// <summary>创建该运行时节点的来源资产。</summary>
        public BehaviorTreeNodeAsset Asset { get; }

        /// <summary>执行当前节点的一帧逻辑并返回节点状态。</summary>
        public abstract BehaviorTreeStatus Tick(BehaviorTreeContext context);

        /// <summary>进入行为树层时调用；普通节点默认不处理层生命周期。</summary>
        public virtual void OnLayerEnter(BehaviorTreeContext context)
        {
        }

        /// <summary>退出行为树层时调用；普通节点默认不处理层生命周期。</summary>
        public virtual void OnLayerExit()
        {
        }

        /// <summary>重置当前节点的运行时状态。</summary>
        public abstract void Reset();
    }
}
