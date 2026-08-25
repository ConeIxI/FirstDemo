using Game.Character.Enemy.Core;
using UnityEngine;

namespace Game.Character.Enemy.Components
{
    public sealed class EnemyMemoryComponent : MonoBehaviour
    {
        private EnemyBlackboard blackboard;

        public Transform Attacker => blackboard != null ? blackboard.CombatTarget : null;
        public Vector3 LastAttackerPosition => blackboard != null ? blackboard.LastKnownPosition : default;
        public float LastAttackerTime => 0f;
        public bool HasAttackerMemory => blackboard != null && (blackboard.HasCombatTarget || blackboard.HasAlertMemory);

        /// <summary>绑定敌人黑板，旧记忆组件仅作为兼容入口转写统一目标。</summary>
        public void Bind(EnemyBlackboard value)
        {
            blackboard = value;
        }

        /// <summary>兼容旧受击入口，把攻击者写入唯一追踪目标。</summary>
        public void RememberAttacker(Transform value)
        {
            if (blackboard == null || value == null)
            {
                return;
            }

            blackboard.RememberTarget(value);
            blackboard.SetTargetVisible(false);
            blackboard.SetSearching(false);
        }

        /// <summary>兼容旧刷新入口，目标仍一致时刷新统一战斗记忆。</summary>
        public void RefreshAttackerMemory(Transform value)
        {
            if (blackboard != null && blackboard.Target == value)
            {
                blackboard.RememberTarget(value);
            }
        }

        /// <summary>目标记忆由 AIController 推进统一黑板，此旧入口不再维护第二套状态。</summary>
        public void TickMemory()
        {
        }
    }
}
