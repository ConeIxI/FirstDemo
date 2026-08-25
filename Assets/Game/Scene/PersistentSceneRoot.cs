using System;
using Game.Character.Equipment;
using GameMain2.Scripts.Character;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameMain2.Scripts.UI
{
    public sealed class PersistentSceneRoot : MonoBehaviour
    {
        private static PersistentSceneRoot s_instance;

        [SerializeField] private Transform playerRoot;
        [SerializeField] private string mainSceneSpawnName = "MainPlayerSpawn";
        [SerializeField] private string bossSceneSpawnName = "BossPlayerSpawn";

        /// <summary>初始化跨场景根节点，保留 Gobal 下的玩家和全局对象。</summary>
        private void Awake()
        {
            if (s_instance != null && s_instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_instance = this;
            DontDestroyOnLoad(gameObject);
            EnsurePlayerRoot();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        /// <summary>销毁根节点时清理场景加载回调和单例引用。</summary>
        private void OnDestroy()
        {
            if (s_instance != this)
            {
                return;
            }

            SceneManager.sceneLoaded -= OnSceneLoaded;
            s_instance = null;
        }

        /// <summary>进入战斗类场景后把持久化玩家移动到当前场景的出生点。</summary>
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == SceneNames.BattleScene)
            {
                MovePlayerToSpawn(scene, mainSceneSpawnName);
            }
            else if (scene.name == SceneNames.BossScene)
            {
                MovePlayerToSpawn(scene, bossSceneSpawnName);
            }

            if (IsGameplayScene(scene.name))
            {
                ResetNormalCameraBehindPlayer();
            }
        }

        /// <summary>确保持久化根节点能找到需要跨场景保留的玩家对象。</summary>
        private void EnsurePlayerRoot()
        {
            if (playerRoot == null)
            {
                playerRoot = transform.Find("Player");
            }

            if (playerRoot == null)
            {
                throw new InvalidOperationException($"{name} 缺少 Player 子对象，无法跨场景保留玩家。");
            }
        }

        /// <summary>把玩家移动到指定出生点，避免 CharacterController 阻挡位置写入。</summary>
        private void MovePlayerToSpawn(Scene scene, string spawnName)
        {
            Transform spawnPoint = FindSpawnPoint(scene, spawnName);
            CharacterController characterController = playerRoot.GetComponent<CharacterController>();
            if (characterController != null)
            {
                characterController.enabled = false;
            }

            playerRoot.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);

            if (characterController != null)
            {
                characterController.enabled = true;
            }
        }

        /// <summary>场景加载完成后把普通相机重新放到玩家背后，避免沿用旧场景相机状态。</summary>
        private void ResetNormalCameraBehindPlayer()
        {
            if (!playerRoot.TryGetComponent(out LockOnManager lockOnManager))
            {
                throw new InvalidOperationException($"{playerRoot.name} 缺少 LockOnManager，无法初始化 NormalCamera。");
            }

            lockOnManager.ResetNormalCameraBehindPlayer();
        }

        /// <summary>把死亡重开快照恢复到新玩家，只同步装备、药水和装备属性。</summary>
        public void ApplyPlayerRestartSnapshot(PlayerRestartSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return;
            }

            EnsurePlayerRoot();
            if (!playerRoot.TryGetComponent(out EquipmentManager equipmentManager))
            {
                throw new InvalidOperationException($"{playerRoot.name} 缺少 EquipmentManager，无法恢复死亡重开装备快照。");
            }

            BagInventoryManager inventory = BagInventoryManager.Instance;
            inventory.ApplyRestartSnapshot(snapshot);
            inventory.ApplyEquipmentSlotsToPlayer(equipmentManager, snapshot.ActiveWeaponIndex);
            UIManager.Instance.RefreshBattleHudEquipmentSlots();
        }

        /// <summary>判断当前场景是否需要按战斗玩家出生点重置位置和相机。</summary>
        private static bool IsGameplayScene(string sceneName)
        {
            return sceneName == SceneNames.BattleScene || sceneName == SceneNames.BossScene;
        }

        /// <summary>在指定场景中查找玩家出生点。</summary>
        private Transform FindSpawnPoint(Scene scene, string spawnName)
        {
            GameObject[] rootObjects = scene.GetRootGameObjects();
            for (int i = 0; i < rootObjects.Length; i++)
            {
                Transform[] transforms = rootObjects[i].GetComponentsInChildren<Transform>(true);
                for (int j = 0; j < transforms.Length; j++)
                {
                    if (transforms[j].name == spawnName)
                    {
                        return transforms[j];
                    }
                }
            }

            throw new InvalidOperationException($"{scene.name} 缺少 {spawnName} 出生点。");
        }
    }
}
