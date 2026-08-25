using System.Collections.Generic;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree.Assets
{
    /// <summary>提供组合节点资产的共享子节点配置和运行时创建逻辑。</summary>
    public abstract class CompositeNodeAsset : BehaviorTreeNodeAsset
    {
        [SerializeField]
        private List<BehaviorTreeNodeAsset> children = new List<BehaviorTreeNodeAsset>();

        /// <summary>获取当前组合节点配置的只读子节点列表。</summary>
        public IReadOnlyList<BehaviorTreeNodeAsset> Children
        {
            get { return children; }
        }

        /// <summary>设置组合节点的子节点列表，供测试和编辑器辅助流程使用。</summary>
        public void SetChildren(params BehaviorTreeNodeAsset[] values)
        {
            children.Clear();
            if (values == null)
            {
                return;
            }

            children.AddRange(values);
        }

        /// <summary>为所有非空子节点创建独立运行时节点，空槽位会跳过并输出警告。</summary>
        protected List<BehaviorTreeNode> CreateRuntimeChildren()
        {
            List<BehaviorTreeNode> runtimeChildren = new List<BehaviorTreeNode>();
            for (int i = 0; i < children.Count; i++)
            {
                BehaviorTreeNodeAsset child = children[i];
                if (child == null)
                {
                    Debug.LogWarning("行为树组合节点包含空子节点，已跳过。");
                    continue;
                }

                runtimeChildren.Add(child.CreateRuntimeNode());
            }

            return runtimeChildren;
        }
    }
}
