using System;
using GameMain2.Scripts.UI;
using UnityEngine;

namespace Game.Character.Enemy.Config
{
    [Serializable]
    public sealed class EnemyDropItemConfig
    {
        public BagItemType itemType = BagItemType.Consumable;
        public int itemId = 1;
        public int count = 1;
        [Range(0f, 1f)] public float dropChance = 1f;
    }
}
