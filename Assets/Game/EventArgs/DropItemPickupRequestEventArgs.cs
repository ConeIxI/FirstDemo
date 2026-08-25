using System;
using GameMain2.Framework.Core;
using GameMain2.Scripts.UI;

namespace Game.World.Drop
{
    public sealed class DropItemPickupRequestEventArgs : EventArgsBase
    {
        public static readonly int EventId = typeof(DropItemPickupRequestEventArgs).GetHashCode();

        public readonly DropItemStack[] Items;
        public readonly BagItemType ItemType;
        public readonly int ItemId;
        public readonly int Count;
        private readonly Action<bool> onCompleted;

        public override int Id => EventId;

        /// <summary>创建拾取请求事件，并保存背包处理完成后的回调。</summary>
        public DropItemPickupRequestEventArgs(
            BagItemType itemType,
            int itemId,
            int count,
            Action<bool> onCompleted)
            : this(new[] { new DropItemStack(itemType, itemId, count) }, onCompleted)
        {
        }

        /// <summary>创建批量拾取请求事件，并保存背包处理完成后的回调。</summary>
        public DropItemPickupRequestEventArgs(
            DropItemStack[] items,
            Action<bool> onCompleted)
        {
            Items = items;
            ItemType = items[0].ItemType;
            ItemId = items[0].ItemId;
            Count = items[0].Count;
            this.onCompleted = onCompleted;
        }

        /// <summary>由背包系统通知本次拾取是否成功。</summary>
        public void Complete(bool success)
        {
            if (onCompleted != null)
            {
                onCompleted(success);
            }
        }
    }
}
