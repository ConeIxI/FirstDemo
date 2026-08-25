using System;
using System.Collections.Generic;
using GameMain2.Framework.Core;
using GameMain2.Scripts.Character;
using GameMain2.Scripts.UI;
using UnityEngine;

namespace Game.World.Drop
{
    public sealed class WorldDropItem : MonoBehaviour
    {
        private const string PlayerTag = "Player";

        [SerializeField] private Collider pickupTrigger;
        [SerializeField] private GameObject tipsUI;
        [SerializeField] private DropItemStack[] items = new DropItemStack[0];

        private int playerTriggerCount;
        private bool pickupRequested;
        private PlayerStateMachine playerStateMachineInRange;

        /// <summary>初始化拾取触发器，确保地面掉落物能检测玩家靠近。</summary>
        private void Awake()
        {
            if (pickupTrigger == null)
            {
                pickupTrigger = GetComponent<Collider>();
            }

            if (pickupTrigger == null)
            {
                pickupTrigger = gameObject.AddComponent<BoxCollider>();
            }

            pickupTrigger.isTrigger = true;
            SetTipsVisible(false);
        }

        /// <summary>玩家在附近时检测 F 键，并发起一次拾取请求。</summary>
        private void Update()
        {
            if (playerTriggerCount > 0 && !pickupRequested && Input.GetKeyDown(KeyCode.F))
            {
                TryPickUp();
            }
        }

        /// <summary>写入掉落物携带的背包物品数据。</summary>
        public void Initialize(BagItemType itemTypeValue, int itemIdValue, int countValue)
        {
            Initialize(new[] { new DropItemStack(itemTypeValue, itemIdValue, countValue) });
        }

        /// <summary>写入本地掉落物携带的批量背包物品数据。</summary>
        public void Initialize(IReadOnlyList<DropItemStack> itemValues)
        {
            items = CopyItems(itemValues);
        }

        /// <summary>记录玩家进入拾取范围。</summary>
        private void OnTriggerEnter(Collider other)
        {
            if (IsPlayerCollider(other))
            {
                playerTriggerCount++;
                CachePlayerStateMachine(other);
                SetTipsVisible(true);
            }
        }

        /// <summary>记录玩家离开拾取范围。</summary>
        private void OnTriggerExit(Collider other)
        {
            if (IsPlayerCollider(other))
            {
                playerTriggerCount = Mathf.Max(0, playerTriggerCount - 1);
                if (playerTriggerCount == 0)
                {
                    playerStateMachineInRange = null;
                    SetTipsVisible(false);
                }
            }
        }

        /// <summary>请求玩家进入拾取状态，拾取动画结束后再由玩家状态机结算背包写入。</summary>
        private void TryPickUp()
        {
            pickupRequested = true;
            if (playerStateMachineInRange == null || !playerStateMachineInRange.TryStartItemGet(this))
            {
                pickupRequested = false;
            }
        }

        /// <summary>由玩家拾取状态在动画结束时调用，通过事件请求背包接收掉落物。</summary>
        public void RequestBagPickup()
        {
            EnsureHasPickupItems();
            EventCenter.Instance.Fire(
                this,
                new DropItemPickupRequestEventArgs(items, OnPickupCompleted));
        }

        /// <summary>确保场景摆放或敌人掉落已经配置了至少一个可拾取道具。</summary>
        private void EnsureHasPickupItems()
        {
            if (items.Length == 0)
            {
                throw new InvalidOperationException($"地面拾取物 {name} 没有配置任何掉落数据。");
            }
        }

        /// <summary>复制掉落批次数据，避免后续外部列表变化影响地面掉落物。</summary>
        private static DropItemStack[] CopyItems(IReadOnlyList<DropItemStack> itemValues)
        {
            DropItemStack[] copiedItems = new DropItemStack[itemValues.Count];
            for (int i = 0; i < itemValues.Count; i++)
            {
                copiedItems[i] = itemValues[i];
            }

            return copiedItems;
        }

        /// <summary>玩家拾取状态被打断时清理请求标记，允许后续再次按键拾取。</summary>
        public void CancelPickupRequest()
        {
            pickupRequested = false;
        }

        /// <summary>背包处理完成后，成功则销毁地面掉落物，失败则允许玩家再次尝试。</summary>
        private void OnPickupCompleted(bool success)
        {
            pickupRequested = false;
            if (success)
            {
                Destroy(gameObject);
            }
            else if (playerTriggerCount > 0)
            {
                SetTipsVisible(true);
            }
        }

        /// <summary>判断进入触发器的对象是否为玩家。</summary>
        private static bool IsPlayerCollider(Collider other)
        {
            return other != null && other.CompareTag(PlayerTag);
        }

        /// <summary>缓存进入拾取范围的玩家状态机，供按键时切换拾取状态。</summary>
        private void CachePlayerStateMachine(Collider other)
        {
            playerStateMachineInRange = other.GetComponentInParent<PlayerStateMachine>();
        }

        /// <summary>按玩家是否处于拾取范围内显示或隐藏地面拾取提示。</summary>
        private void SetTipsVisible(bool visible)
        {
            if (tipsUI != null && tipsUI.activeSelf != visible)
            {
                tipsUI.SetActive(visible);
            }
        }
    }
}
