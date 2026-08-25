using System;
using Game.Common;

namespace Game.Battle.Buff
{
    [Serializable]
    public sealed class CombatBuffConfig : IConfig
    {
        public int buffId;
        public string buffName;
        public CombatBuffType type;
        public float duration;
        public int flatValue;
        public float percentValue;
        public float tickInterval;
        public int tickValue;
        public string activeEffectId;
    }
}
