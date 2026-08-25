using Game.Battle.Skill.Common;
using UnityEngine;

namespace Game.Battle.Skill.Effects
{
    public sealed class CombatEffectInstanceHandle
    {
        public string Path { get; private set; }
        public string Channel { get; private set; }
        public Object Owner { get; private set; }
        public GameObject Instance { get; private set; }
        public CombatEffectRecycleMode RecycleMode { get; private set; }
        public float RemainingDuration { get; set; }

        /// <summary>初始化一个活动特效实例句柄。</summary>
        public CombatEffectInstanceHandle(
            string path,
            string channel,
            Object owner,
            GameObject instance,
            CombatEffectRecycleMode recycleMode,
            float remainingDuration)
        {
            Path = path;
            Channel = channel;
            Owner = owner;
            Instance = instance;
            RecycleMode = recycleMode;
            RemainingDuration = remainingDuration;
        }
    }
}
