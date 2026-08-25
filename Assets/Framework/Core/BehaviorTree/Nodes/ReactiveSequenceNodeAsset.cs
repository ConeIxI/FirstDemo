using System.Collections.Generic;
using GameMain2.Framework.Core.BehaviorTree.Runtime;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree.Assets
{
    /// <summary>定义每帧从首个 Guard 重新评估的响应式序列节点资产。</summary>
    [CreateAssetMenu(fileName = "ReactiveSequenceNode", menuName = "Game/Behavior Tree/Reactive Sequence Node")]
    public sealed class ReactiveSequenceNodeAsset : CompositeNodeAsset
    {
        /// <summary>创建响应式序列的独立运行时节点实例。</summary>
        public override BehaviorTreeNode CreateRuntimeNode()
        {
            return new ReactiveSequenceNode(this, CreateRuntimeChildren());
        }

        private sealed class ReactiveSequenceNode : BehaviorTreeNode
        {
            private readonly List<BehaviorTreeNode> children;
            private bool isLayerActive;

            /// <summary>绑定来源资产及其独立运行时子节点列表。</summary>
            public ReactiveSequenceNode(BehaviorTreeNodeAsset asset, List<BehaviorTreeNode> children)
                : base(asset)
            {
                this.children = children;
            }

            /// <summary>每帧从首个子节点执行，Guard 失败时清理后续节点的运行状态。</summary>
            public override BehaviorTreeStatus Tick(BehaviorTreeContext context)
            {
                for (int i = 0; i < children.Count; i++)
                {
                    BehaviorTreeStatus status = children[i].Tick(context);
                    if (status == BehaviorTreeStatus.Failure)
                    {
                        ResetFollowingChildren(i);
                        return BehaviorTreeStatus.Failure;
                    }

                    if (status == BehaviorTreeStatus.Running)
                    {
                        return BehaviorTreeStatus.Running;
                    }
                }

                Reset();
                return BehaviorTreeStatus.Success;
            }

            /// <summary>进入组合层时通知直接子节点，让层内动作可以绑定一次性进入表现。</summary>
            public override void OnLayerEnter(BehaviorTreeContext context)
            {
                if (isLayerActive)
                {
                    return;
                }

                isLayerActive = true;
                for (int i = 0; i < children.Count; i++)
                {
                    children[i].OnLayerEnter(context);
                }
            }

            /// <summary>退出组合层时通知直接子节点，让层内动作可以收束一次性表现。</summary>
            public override void OnLayerExit()
            {
                if (!isLayerActive)
                {
                    return;
                }

                for (int i = 0; i < children.Count; i++)
                {
                    children[i].OnLayerExit();
                }

                isLayerActive = false;
            }

            /// <summary>重置所有运行时子节点，使序列回到初始状态。</summary>
            public override void Reset()
            {
                OnLayerExit();
                for (int i = 0; i < children.Count; i++)
                {
                    children[i].Reset();
                }
            }

            /// <summary>重置发生失败的 Guard 之后的所有子节点。</summary>
            private void ResetFollowingChildren(int guardIndex)
            {
                for (int i = guardIndex + 1; i < children.Count; i++)
                {
                    children[i].Reset();
                }
            }
        }
    }
}
