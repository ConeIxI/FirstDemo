using System;
using UnityEngine.Serialization;

namespace Game.Character.Enemy.Config
{
    [Serializable]
    public sealed class EnemyLifeConfig
    {
        [FormerlySerializedAs("rememberAttackerOnHit")]
        public bool rememberTargetOnHit = true;
        public bool allowUnbalanceReaction = true;
        public bool allowDeathReaction = true;
    }
}
