using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree
{
    [CreateAssetMenu(fileName = "BehaviorTree", menuName = "GameMain2/Behavior Tree/Behavior Tree")]
    public sealed class BehaviorTreeAsset : ScriptableObject
    {
        [SerializeField]
        private BehaviorTreeNodeAsset root;

        /// <summary>行为树入口节点资产。</summary>
        public BehaviorTreeNodeAsset Root
        {
            get { return root; }
        }

        /// <summary>设置行为树入口节点资产，供编辑器工具或测试构建树结构。</summary>
        public void SetRoot(BehaviorTreeNodeAsset rootNode)
        {
            root = rootNode;
        }
    }
}
