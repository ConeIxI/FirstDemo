using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree.Assets
{
    public abstract class BehaviorTreeNodeAsset : ScriptableObject
    {
        [SerializeField]
        private string nodeName;

        /// <summary>节点显示名称，未配置时使用 Unity 资产名。</summary>
        public string NodeName
        {
            get { return string.IsNullOrEmpty(nodeName) ? name : nodeName; }
        }

        /// <summary>创建当前节点资产对应的独立运行时节点实例。</summary>
        public abstract BehaviorTreeNode CreateRuntimeNode();
    }
}
