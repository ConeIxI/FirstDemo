using Game.Character.Enemy.AI;
using Game.Character.Enemy.Components;
using Game.Character.Enemy.Config;
using UnityEngine;

namespace Game.Character.Enemy.Core
{
    public sealed class EnemyAgent : MonoBehaviour
    {
        [SerializeField] private EnemyDefinition definition;
        [SerializeField] private AIController aiController;
        [SerializeField] private EnemyMovementComponent movement;
        [SerializeField] private EnemyPerceptionComponent perception;
        [SerializeField] private EnemyAnimationComponent animationComponent;
        [SerializeField] private EnemyCombatComponent combat;
        [SerializeField] private EnemyLifeComponent life;
        [SerializeField] private EnemyAttributeComponent attribute;
        [SerializeField] private Transform[] patrolRoute;
        [SerializeField] private GameObject[] Weapon_Hand;
        [SerializeField] private GameObject[] Weapon_Back;

        // 返回场景敌人实例配置的巡逻路线，允许直接拖入场景路点。
        public Transform[] PatrolRoute => patrolRoute != null ? (Transform[])patrolRoute.Clone() : new Transform[0];

        // 唤醒时收集同一对象上的敌人运行时组件引用，并把武器初始化为背负状态。
        private void Awake()
        {
            ResolveComponents();
            HideWeapons();
        }

        // 启动时校验定义并初始化 AI 控制器。
        private void Start()
        {
            EnemyDefinitionValidationResult validation = EnemyDefinitionValidator.Validate(definition);
            if (!validation.IsValid)
            {
                Debug.LogError("敌人定义无效：" + string.Join(", ", validation.Errors), this);
                return;
            }

            aiController.StartAI(this, definition);
        }

        // 每帧推进移动和 AI，死亡后只停移动 Tick，保留 AI 死亡分支播放死亡动画。
        private void Update()
        {
            float deltaTime = Time.deltaTime;
            if ((life == null || !life.IsDead) && movement != null)
            {
                movement.Tick(deltaTime);
            }

            if (aiController != null)
            {
                aiController.TickAI(deltaTime);
            }
        }

        // 补齐同一 GameObject 上缺失的组件引用，避免运行时重复查找。
        private void ResolveComponents()
        {
            if (aiController == null)
            {
                TryGetComponent(out aiController);
            }

            if (movement == null)
            {
                TryGetComponent(out movement);
            }

            if (perception == null)
            {
                TryGetComponent(out perception);
            }

            if (animationComponent == null)
            {
                TryGetComponent(out animationComponent);
            }

            if (combat == null)
            {
                TryGetComponent(out combat);
            }

            if (life == null)
            {
                TryGetComponent(out life);
            }

            if (attribute == null)
            {
                TryGetComponent(out attribute);
            }
        }

        // 兼容行为树兜底调用，把武器切到手持状态。
        public void ShowWeapons()
        {
            SetWeaponInHand();
        }

        // 拔剑动画被中断时直接确认全部武器已在手中，避免背部和手持武器同时残留。
        public void ShowAllWeaponsInHand()
        {
            SetWeaponsVisible(Weapon_Back, false);
            SetWeaponsVisible(Weapon_Hand, true);
        }

        // 兼容行为树兜底调用，把武器切到背负状态。
        public void HideWeapons()
        {
            SetWeaponOnBack();
        }

        // EnterCombat 动画事件入口：在拔出武器的关键帧按索引显示手持武器并隐藏背部武器。
        public void OnEnterCombatWeaponEvent(int weaponIndex = 0)
        {
            SetWeaponInHand(weaponIndex);
        }

        // ExitCombat 动画事件入口：在收起武器的关键帧按索引显示背部武器并隐藏手持武器。
        public void OnExitCombatWeaponEvent(int weaponIndex = 0)
        {
            SetWeaponOnBack(weaponIndex);
        }

        // 按索引切换到手持武器表现。
        private void SetWeaponInHand(int weaponIndex = 0)
        {
            SetWeaponVisible(Weapon_Back, weaponIndex, false);
            SetWeaponVisible(Weapon_Hand, weaponIndex, true);
        }

        // 按索引切换到背负武器表现。
        private void SetWeaponOnBack(int weaponIndex = 0)
        {
            SetWeaponVisible(Weapon_Hand, weaponIndex, false);
            SetWeaponVisible(Weapon_Back, weaponIndex, true);
        }

        // 设置指定索引的武器挂载点显隐，允许部分敌人暂时不配置某个索引挂载点。
        private static void SetWeaponVisible(GameObject[] weapons, int weaponIndex, bool visible)
        {
            GameObject weapon = GetWeaponByIndex(weapons, weaponIndex);
            if (weapon != null)
            {
                weapon.SetActive(visible);
            }
        }

        // 批量设置一组武器挂载点显隐，用于无动画事件索引的强制姿态修正。
        private static void SetWeaponsVisible(GameObject[] weapons, bool visible)
        {
            if (weapons == null)
            {
                return;
            }

            for (int i = 0; i < weapons.Length; i++)
            {
                if (weapons[i] != null)
                {
                    weapons[i].SetActive(visible);
                }
            }
        }

        // 按索引获取武器挂载点，未配置或索引不存在时保持原有空挂载点兼容行为。
        private static GameObject GetWeaponByIndex(GameObject[] weapons, int weaponIndex)
        {
            return weapons != null && weaponIndex >= 0 && weaponIndex < weapons.Length ? weapons[weaponIndex] : null;
        }

    }
}
