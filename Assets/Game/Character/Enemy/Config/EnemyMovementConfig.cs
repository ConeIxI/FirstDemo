using System;

namespace Game.Character.Enemy.Config
{
    [Serializable]
    public sealed class EnemyMovementConfig
    {
        public float moveSpeed = 2f;
        public float rotateSpeed = 4f;
        public float attackRotateSpeed = 4f;
        public float stoppingDistance = 1.1f;
        public float navMeshSampleDistance = 2f;
        public float patrolWaitDuration = 2f;
    }
}
