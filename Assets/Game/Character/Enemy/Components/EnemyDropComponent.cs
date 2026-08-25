using System;
using System.Collections.Generic;
using Game.Character.Enemy.Config;
using Game.Config.Item;
using Game.World.Drop;
using GameMain2.Framework.Manager;
using GameMain2.Scripts.UI;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Game.Character.Enemy.Components
{
    public sealed class EnemyDropComponent : MonoBehaviour
    {
        private const string WorldDropItemAddress = "WorldDropItem";

        private EnemyDropItemConfig[] dropItems = new EnemyDropItemConfig[0];
        private bool hasSpawnedDrops;

        /// <summary>从敌人定义应用掉落配置，并重置本次死亡掉落状态。</summary>
        public void ApplyConfig(EnemyDropItemConfig[] value)
        {
            dropItems = value ?? new EnemyDropItemConfig[0];
            hasSpawnedDrops = false;
        }

        /// <summary>按当前掉落配置和随机值抽取命中的掉落项。</summary>
        public List<EnemyDropItemConfig> RollDropItems(Func<float> randomValueProvider)
        {
            List<EnemyDropItemConfig> results = new List<EnemyDropItemConfig>();
            for (int i = 0; i < dropItems.Length; i++)
            {
                EnemyDropItemConfig item = dropItems[i];
                if (item != null && randomValueProvider() <= item.dropChance)
                {
                    results.Add(item);
                }
            }

            return results;
        }

        /// <summary>在指定位置生成本次死亡抽中的地面掉落物，同一敌人只生成一次。</summary>
        public void SpawnDrops(Vector3 position)
        {
            if (hasSpawnedDrops)
            {
                return;
            }

            hasSpawnedDrops = true;
            List<EnemyDropItemConfig> results = RollDropItems(() => UnityEngine.Random.value);
            if (results.Count > 0)
            {
                SpawnDropAsync(CreateDropStacks(results), position);
            }
        }

        /// <summary>异步实例化唯一的地面掉落物 Prefab，并在实例生成后写入批量物品数据。</summary>
        private void SpawnDropAsync(DropItemStack[] items, Vector3 position)
        {
            AsyncOperationHandle<GameObject> handle = Addressables.InstantiateAsync(
                WorldDropItemAddress,
                position,
                Quaternion.identity);
            handle.Completed += operation => OnDropInstantiated(operation, items);
        }

        /// <summary>处理 Addressables 实例化完成结果，并配置地面掉落物组件。</summary>
        private static void OnDropInstantiated(
            AsyncOperationHandle<GameObject> handle,
            DropItemStack[] items)
        {
            if (handle.Status != AsyncOperationStatus.Succeeded || handle.Result == null)
            {
                return;
            }

            WorldDropItem worldDropItem = handle.Result.GetComponent<WorldDropItem>();
            if (worldDropItem != null)
            {
                worldDropItem.Initialize(items);
            }
        }

        /// <summary>把敌人掉落配置转换为地面掉落物持有的批量道具数据。</summary>
        private static DropItemStack[] CreateDropStacks(IReadOnlyList<EnemyDropItemConfig> results)
        {
            DropItemStack[] items = new DropItemStack[results.Count];
            for (int i = 0; i < results.Count; i++)
            {
                EnemyDropItemConfig result = results[i];
                items[i] = new DropItemStack(
                    result.itemType,
                    result.itemId,
                    result.count,
                    RollDroppedDefense(result));
            }

            return items;
        }

        /// <summary>根据防具配置上下限随机本次掉落的实例防御力，非防具固定为 0。</summary>
        private static int RollDroppedDefense(EnemyDropItemConfig item)
        {
            DefenseEquipmentItemConfig config = GetDefenseEquipmentConfig(item.itemType, item.itemId);
            return config == null ? 0 : UnityEngine.Random.Range(config.minDefense, config.maxDefense + 1);
        }

        /// <summary>按背包物品分类读取对应防具配置，武器和消耗品不参与防御随机。</summary>
        private static DefenseEquipmentItemConfig GetDefenseEquipmentConfig(BagItemType itemType, int itemId)
        {
            switch (itemType)
            {
                case BagItemType.Helmet:
                    return ConfigManager.Instance.GetHelmetItemConfig(itemId);
                case BagItemType.Armor:
                    return ConfigManager.Instance.GetArmorItemConfig(itemId);
                case BagItemType.Leggings:
                    return ConfigManager.Instance.GetLeggingsItemConfig(itemId);
                case BagItemType.Gloves:
                    return ConfigManager.Instance.GetGlovesItemConfig(itemId);
                default:
                    return null;
            }
        }
    }
}
