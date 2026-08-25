using GameMain2.Scripts.UI;
using UnityEngine;

namespace Game.World.Teleport
{
    public sealed class WorldTeleportItem : MonoBehaviour
    {
        private const string PlayerTag = "Player";

        [SerializeField] private Collider teleportTrigger;
        [SerializeField] private GameObject tipsUI;
        [SerializeField] private string targetSceneName = SceneNames.BossScene;

        private int playerTriggerCount;
        private bool teleportRequested;

        /// <summary>初始化传送触发器，确保传送物能检测玩家靠近。</summary>
        private void Awake()
        {
            if (teleportTrigger == null)
            {
                teleportTrigger = GetComponent<Collider>();
            }

            if (teleportTrigger == null)
            {
                teleportTrigger = gameObject.AddComponent<BoxCollider>();
            }

            teleportTrigger.isTrigger = true;
            SetTipsVisible(false);
        }

        /// <summary>玩家在传送范围内按下 F 时发起场景传送。</summary>
        private void Update()
        {
            if (playerTriggerCount > 0 && !teleportRequested && Input.GetKeyDown(KeyCode.F))
            {
                TryTeleport();
            }
        }

        /// <summary>记录玩家进入传送范围并显示交互提示。</summary>
        private void OnTriggerEnter(Collider other)
        {
            if (IsPlayerCollider(other))
            {
                playerTriggerCount++;
                SetTipsVisible(true);
            }
        }

        /// <summary>记录玩家离开传送范围，范围内没有玩家时隐藏交互提示。</summary>
        private void OnTriggerExit(Collider other)
        {
            if (IsPlayerCollider(other))
            {
                playerTriggerCount = Mathf.Max(0, playerTriggerCount - 1);
                if (playerTriggerCount == 0)
                {
                    SetTipsVisible(false);
                }
            }
        }

        /// <summary>通过统一场景流程管理器加载目标场景。</summary>
        private void TryTeleport()
        {
            teleportRequested = true;
            SetTipsVisible(false);
            SceneFlowManager.Instance.LoadScene(targetSceneName);
        }

        /// <summary>判断进入触发器的对象是否为玩家。</summary>
        private static bool IsPlayerCollider(Collider other)
        {
            return other != null && other.CompareTag(PlayerTag);
        }

        /// <summary>按玩家是否处于传送范围内显示或隐藏传送提示。</summary>
        private void SetTipsVisible(bool visible)
        {
            if (tipsUI != null && tipsUI.activeSelf != visible)
            {
                tipsUI.SetActive(visible);
            }
        }
    }
}
