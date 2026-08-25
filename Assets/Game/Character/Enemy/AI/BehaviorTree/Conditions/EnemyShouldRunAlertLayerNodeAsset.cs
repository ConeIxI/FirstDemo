using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEngine;

namespace Game.Character.Enemy.AI.BehaviorTree
{
    [CreateAssetMenu(menuName = "Game/Enemy/Behavior Tree/Should Run Alert Layer")]
    public sealed class EnemyShouldRunAlertLayerNodeAsset : ConditionNodeAsset
    {
        /// <summary>判断警戒层是否仍需运行：有警戒记忆或正在执行退出握手。</summary>
        protected override bool Evaluate(BehaviorTreeContext context)
        {
            return EnemyBehaviorTreeUtility.TryGetController(context, out AIController controller)
                && (controller.Blackboard.HasAlertMemory || controller.Blackboard.IsAlertExitPending);
        }
    }
}
