using System;
using Game.Battle.Skill.Common;
using Game.Common;

namespace Game.Battle.Skill.Effects
{
    [Serializable]
    public sealed class CombatEffectConfig : IConfig
    {
        public string effectId;
        public string path;
        public CombatEffectAttachment attachment;
        public string socketName;
        public bool follow;
        public Vec3 position;
        public Vec3 rotation;
        public Vec3 scale;
        public CombatEffectOrientation orientation;
        public CombatEffectRecycleMode recycleMode;
        public float duration;
        public CombatEffectConcurrency concurrency;
        public string channel;
    }
}
