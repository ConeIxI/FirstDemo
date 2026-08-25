using System;
using GameMain2.Framework.Manager;
using GameMain2.Scripts.UI;
using UnityEngine;

namespace GameMain2.Scripts.Modules.Bag
{
    /// <summary>
    /// 背包物品数据工厂，负责按配置创建运行时物品并加载展示图标。
    /// </summary>
    internal static class BagItemFactory
    {
        /// <summary>创建背包物品数据，保存实例防御值并根据配置表中的图标地址同步加载 UI 图标。</summary>
        public static BagItemData Create(int id, BagItemType itemType, int bagIndex, int count, int defense)
        {
            BagItemData item = new BagItemData(id, itemType, bagIndex, count, defense: defense);
            item.Icon = LoadItemIcon(item);
            item.SkillIcons = LoadSkillIcons(item);
            return item;
        }

        /// <summary>按物品配置中的 Addressables 地址加载图标；空地址表示当前物品使用文字占位。</summary>
        private static Sprite LoadItemIcon(BagItemData item)
        {
            string iconAddress = item.IconAddress;
            if (string.IsNullOrWhiteSpace(iconAddress))
            {
                return null;
            }

            Sprite icon = ResourceManager.Instance.LoadAsset<Sprite>(iconAddress);
            if (icon == null)
            {
                throw new Exception($"物品图标加载失败：{item.Name}({item.ItemType}/{item.Id})，地址：{iconAddress}");
            }

            return icon;
        }

        /// <summary>按武器配置同步加载三个技能图标；空地址保留为空图标。</summary>
        private static Sprite[] LoadSkillIcons(BagItemData item)
        {
            string[] iconAddresses = item.SkillIconAddresses;
            Sprite[] icons = new Sprite[iconAddresses.Length];
            for (int i = 0; i < iconAddresses.Length; i++)
            {
                string iconAddress = iconAddresses[i];
                if (string.IsNullOrWhiteSpace(iconAddress))
                {
                    continue;
                }

                Sprite icon = ResourceManager.Instance.LoadAsset<Sprite>(iconAddress);
                if (icon == null)
                {
                    throw new Exception(
                        $"武器技能图标加载失败：{item.Name}({item.Id})，槽位：{i + 1}，地址：{iconAddress}");
                }

                icons[i] = icon;
            }

            return icons;
        }
    }
}
