using System;

namespace Game.Character.Enemy.Config
{
    [Serializable]
    public sealed class EnemyAttributeConfig
    {
        public int maxHealth = 100;
        public int maxStability = 100;
        public int attack = 10;
        public int defense;
        public float moveSpeedMultiplier = 1f;
        public float perceptionMultiplier = 1f;
    }
}
