using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree
{
    public sealed class BehaviorTreeRunner : MonoBehaviour
    {
        private BehaviorTreeAsset tree;

        private BehaviorTreeContext context;
        private BehaviorTreeNode rootNode;
        private bool isInitialized;

        /// <summary>Runner 是否已经创建当前行为树的运行时根节点。</summary>
        public bool IsInitialized
        {
            get { return isInitialized; }
        }

        /// <summary>Unity 启动回调，提前尝试初始化行为树运行时状态。</summary>
        public void Start()
        {
            Initialize();
        }

        /// <summary>推进一帧行为树；缺少 Root 或 Owner 时返回失败。</summary>
        public BehaviorTreeStatus Tick(float deltaTime)
        {
            if (!isInitialized && !Initialize())
            {
                return BehaviorTreeStatus.Failure;
            }

            if (context == null || context.Owner == null)
            {
                isInitialized = false;
                return BehaviorTreeStatus.Failure;
            }

            context.DeltaTime = deltaTime;
            return rootNode.Tick(context);
        }

        /// <summary>重置当前运行时根节点，并清除初始化状态以便下次 Tick 重新创建。</summary>
        public void Reset()
        {
            if (rootNode != null)
            {
                rootNode.Reset();
            }

            context = null;
            rootNode = null;
            isInitialized = false;
        }

        /// <summary>切换 Runner 使用的行为树资产，并丢弃旧运行时状态。</summary>
        public void SetTree(BehaviorTreeAsset behaviorTree)
        {
            Reset();
            tree = behaviorTree;
        }

        /// <summary>按当前行为树资产创建独立运行时根节点和上下文。</summary>
        private bool Initialize()
        {
            if (tree == null || tree.Root == null || gameObject == null)
            {
                isInitialized = false;
                return false;
            }

            context = new BehaviorTreeContext(gameObject, new BehaviorTreeBlackboard());
            rootNode = tree.Root.CreateRuntimeNode();
            isInitialized = rootNode != null;
            return isInitialized;
        }
    }
}
