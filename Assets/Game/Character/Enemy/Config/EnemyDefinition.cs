using GameMain2.Framework.Core.BehaviorTree;
using UnityEngine;

namespace Game.Character.Enemy.Config
{
    [CreateAssetMenu(fileName = "EnemyDefinition", menuName = "Game/Enemy/Enemy Definition")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        [SerializeField] private string enemyId;
        [SerializeField] private string displayName;
        [SerializeField] private BehaviorTreeAsset behaviorTreeAsset;
        [SerializeField] private EnemyMovementConfig movementConfig = new EnemyMovementConfig();
        [SerializeField] private EnemyPerceptionConfig perceptionConfig = new EnemyPerceptionConfig();
        [SerializeField] private EnemyAnimationConfig animationConfig = new EnemyAnimationConfig();
        [SerializeField] private EnemyCombatConfig combatConfig = new EnemyCombatConfig();
        [SerializeField] private EnemyLifeConfig lifeConfig = new EnemyLifeConfig();
        [SerializeField] private EnemyAttributeConfig attributeConfig = new EnemyAttributeConfig();
        [SerializeField] private EnemyDecisionProfile decisionProfile = new EnemyDecisionProfile();
        [SerializeField] private EnemyDropItemConfig[] dropItems = new EnemyDropItemConfig[0];

        public string EnemyId => enemyId;
        public string DisplayName => displayName;
        public BehaviorTreeAsset BehaviorTreeAsset => behaviorTreeAsset;
        public EnemyMovementConfig MovementConfig => movementConfig;
        public EnemyPerceptionConfig PerceptionConfig => perceptionConfig;
        public EnemyAnimationConfig AnimationConfig => animationConfig;
        public EnemyCombatConfig CombatConfig => combatConfig;
        public EnemyLifeConfig LifeConfig => lifeConfig;
        public EnemyAttributeConfig AttributeConfig => attributeConfig;
        public EnemyDecisionProfile DecisionProfile => decisionProfile;
        public EnemyDropItemConfig[] DropItems => dropItems;

#if UNITY_EDITOR
        // 设置敌人配置唯一 Id，供编辑器工具或测试构造定义。
        public void SetEnemyId(string value)
        {
            enemyId = value;
        }

        // 设置敌人显示名称，供编辑器工具或测试构造定义。
        public void SetDisplayName(string value)
        {
            displayName = value;
        }

        // 设置敌人行为树资产引用，供编辑器工具或测试构造定义。
        public void SetBehaviorTreeAsset(BehaviorTreeAsset value)
        {
            behaviorTreeAsset = value;
        }

        // 设置敌人移动配置，供编辑器工具或测试构造定义。
        public void SetMovementConfig(EnemyMovementConfig value)
        {
            movementConfig = value;
        }

        // 设置敌人感知配置，供编辑器工具或测试构造定义。
        public void SetPerceptionConfig(EnemyPerceptionConfig value)
        {
            perceptionConfig = value;
        }

        // 设置敌人动画配置，供编辑器工具或测试构造定义。
        public void SetAnimationConfig(EnemyAnimationConfig value)
        {
            animationConfig = value;
        }

        // 设置敌人战斗配置，供编辑器工具或测试构造定义。
        public void SetCombatConfig(EnemyCombatConfig value)
        {
            combatConfig = value;
        }

        // 设置敌人生命配置，供编辑器工具或测试构造定义。
        public void SetLifeConfig(EnemyLifeConfig value)
        {
            lifeConfig = value;
        }

        // 设置敌人属性配置，供编辑器工具或测试构造定义。
        public void SetAttributeConfig(EnemyAttributeConfig value)
        {
            attributeConfig = value;
        }

        // 设置敌人决策配置，供编辑器工具或测试构造定义。
        public void SetDecisionProfile(EnemyDecisionProfile value)
        {
            decisionProfile = value;
        }

        /// <summary>设置敌人掉落配置，供编辑器工具或测试构造定义。</summary>
        public void SetDropItems(EnemyDropItemConfig[] value)
        {
            dropItems = value;
        }

#endif
    }
}
