using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree.Assets
{
    /// <summary>提供装饰节点资产的单子节点配置和运行时创建逻辑。</summary>
    public abstract class DecoratorNodeAsset : BehaviorTreeNodeAsset
    {
        [SerializeField]
        private BehaviorTreeNodeAsset child;

        /// <summary>获取当前装饰节点配置的子节点。</summary>
        public BehaviorTreeNodeAsset Child
        {
            get { return child; }
        }

        /// <summary>设置装饰节点的子节点，供测试和编辑器辅助流程使用。</summary>
        public void SetChild(BehaviorTreeNodeAsset value)
        {
            child = value;
        }

        /// <summary>创建装饰节点的运行时子节点，缺少子节点时输出警告并返回空。</summary>
        protected BehaviorTreeNode CreateRuntimeChild()
        {
            if (child == null)
            {
                Debug.LogWarning("行为树装饰节点缺少子节点。");
                return null;
            }

            return child.CreateRuntimeNode();
        }
    }
}
