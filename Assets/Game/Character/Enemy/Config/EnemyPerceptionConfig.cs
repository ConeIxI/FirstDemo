using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Game.Character.Enemy.Config
{
    [Serializable]
    public sealed class EnemyPerceptionConfig
    {
        public float range = 8f;
        public float soundRange = 6f;
        public float angle = 120f;
        public float closeAwarenessRange = 2.5f;
        public float loseSightGraceTime = 0.5f;
        [FormerlySerializedAs("targetMemoryTime")] public float alertMemoryDuration = 4f;
        [FormerlySerializedAs("searchWaitTime")] public float searchObservationDuration = 1f;
        public float searchRadius = 4f;
        public int searchPointCount = 3;
        public LayerMask targetMask;
        public LayerMask obstacleMask;

        /// <summary>兼容旧代码读取目标记忆时间，实际转发到警戒记忆时间。</summary>
        public float targetMemoryTime
        {
            get { return alertMemoryDuration; }
            set { alertMemoryDuration = value; }
        }

        /// <summary>兼容旧代码读取搜索等待时间，实际转发到搜索观察时间。</summary>
        public float searchWaitTime
        {
            get { return searchObservationDuration; }
            set { searchObservationDuration = value; }
        }
    }
}
