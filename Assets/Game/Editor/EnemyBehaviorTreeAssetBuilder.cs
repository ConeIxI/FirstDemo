using Game.Character.Enemy.AI.BehaviorTree;
using Game.Character.Enemy.Config;
using GameMain2.Framework.Core.BehaviorTree;
using GameMain2.Framework.Core.BehaviorTree.Assets;
using UnityEditor;
using UnityEngine;

namespace Game.Character.Enemy.Editor
{
    public static class EnemyBehaviorTreeAssetBuilder
    {
        private const string CommonPath =
            "Assets/Game/Character/Enemy/Config/BehaviorTrees/Common";
        private const string ElitePath =
            "Assets/Game/Character/Enemy/Config/BehaviorTrees/SwordAndShieldEliteEnemy";
        private const string EliteDefinitionPath =
            "Assets/Game/Character/Enemy/Config/Definitions/SwordAndShieldEliteEnemyDefinition.asset";

        /// <summary>重建普通敌人共用战斗行为树，并迁移普通敌人定义到攻击计划配置。</summary>
        [MenuItem("Tools/Enemy AI/Rebuild Common Combat Tree")]
        public static void RebuildCommonCombatTree()
        {
            RebuildCommonRootAndCombatLayer();
            MigrateOrdinaryEnemyDefinitions();
            DeleteLegacyDistanceAssets();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>重建剑盾精英敌人的独立战斗行为树，并写入精英专属攻击计划配置。</summary>
        [MenuItem("Tools/Enemy AI/Rebuild Sword And Shield Elite Tree")]
        public static void RebuildSwordAndShieldEliteTree()
        {
            RebuildEliteRootAndCombatLayer();
            MigrateSwordAndShieldEliteDefinition();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        /// <summary>重连通用根节点、战斗层和战斗计划选择器的子节点。</summary>
        private static void RebuildCommonRootAndCombatLayer()
        {
            ReactivePrioritySelectorNodeAsset root = LoadRequired<ReactivePrioritySelectorNodeAsset>(
                CommonPath + "/Root/RootLayerSelector.asset");
            ReactiveSequenceNodeAsset combatLayer = LoadRequired<ReactiveSequenceNodeAsset>(
                CommonPath + "/Sequence/CombatLayer.asset");
            ReactiveSequenceNodeAsset alertLayer = LoadRequired<ReactiveSequenceNodeAsset>(
                CommonPath + "/Sequence/AlertLayer.asset");
            ReactiveSequenceNodeAsset normalLayer = LoadRequired<ReactiveSequenceNodeAsset>(
                CommonPath + "/Sequence/NormalLayer.asset");
            RepeatForeverNodeAsset repeatCombat = LoadRequired<RepeatForeverNodeAsset>(
                CommonPath + "/Decorator/RepeatCombatDistance.asset");
            ReactivePrioritySelectorNodeAsset combatSelector = LoadRequired<ReactivePrioritySelectorNodeAsset>(
                CommonPath + "/Selector/CombatDistanceSelector.asset");
            CompositeNodeAsset attackSequence = LoadRequired<CompositeNodeAsset>(
                CommonPath + "/Sequence/AttackSequence.asset");

            root.SetChildren(
                LoadRequired<EnemyInterruptExecutorNodeAsset>(CommonPath + "/Action/InterruptExecutor.asset"),
                combatLayer,
                alertLayer,
                normalLayer);
            combatLayer.SetChildren(
                LoadRequired<EnemyHasCombatTargetNodeAsset>(CommonPath + "/Condition/HasCombatTarget.asset"),
                LoadRequired<EnemyEnsureCombatStanceNodeAsset>(CommonPath + "/Action/EnsureCombatStance.asset"),
                repeatCombat);
            repeatCombat.SetChild(combatSelector);
            combatSelector.SetChildren(
                LoadRequired<SequenceNodeAsset>(CommonPath + "/Sequence/TurnToTargetSequence.asset"),
                LoadRequired<SequenceNodeAsset>(CommonPath + "/Sequence/DodgeDecisionSequence.asset"),
                LoadRequired<SequenceNodeAsset>(CommonPath + "/Sequence/DefenseDecisionSequence.asset"),
                attackSequence,
                LoadRequired<ReactiveSequenceNodeAsset>(CommonPath + "/Sequence/CombatHoldSequence.asset"));
            RebuildDecisionSequences(CommonPath + "/Sequence", attackSequence);

            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(combatLayer);
            EditorUtility.SetDirty(repeatCombat);
            EditorUtility.SetDirty(combatSelector);
            EditorUtility.SetDirty(attackSequence);
        }

        /// <summary>重建战斗选择器内的转身、闪避、防御、攻击和保持分支。</summary>
        private static void RebuildDecisionSequences(string sequencePath, CompositeNodeAsset attackSequence)
        {
            SequenceNodeAsset turnToTarget = LoadRequired<SequenceNodeAsset>(
                sequencePath + "/TurnToTargetSequence.asset");
            SequenceNodeAsset dodgeDecision = LoadRequired<SequenceNodeAsset>(
                sequencePath + "/DodgeDecisionSequence.asset");
            SequenceNodeAsset defenseDecision = LoadRequired<SequenceNodeAsset>(
                sequencePath + "/DefenseDecisionSequence.asset");
            ReactiveSequenceNodeAsset combatHold = LoadRequired<ReactiveSequenceNodeAsset>(
                sequencePath + "/CombatHoldSequence.asset");

            turnToTarget.SetChildren(
                LoadRequired<EnemyIsTargetBehindNodeAsset>(CommonPath + "/Condition/IsTargetBehind.asset"),
                LoadRequired<EnemyTurnToTargetNodeAsset>(CommonPath + "/Action/TurnToTarget.asset"));
            dodgeDecision.SetChildren(
                LoadRequired<EnemyHasCombatReactionNodeAsset>(CommonPath + "/Condition/HasDodgeReaction.asset"),
                LoadRequired<EnemyDodgeNodeAsset>(CommonPath + "/Action/Dodge.asset"));
            defenseDecision.SetChildren(
                LoadRequired<EnemyHasCombatReactionNodeAsset>(CommonPath + "/Condition/HasDefenseReaction.asset"),
                LoadRequired<EnemyDefenseNodeAsset>(CommonPath + "/Action/Defense.asset"));
            attackSequence.SetChildren(
                LoadRequired<EnemyHasAttackIntentNodeAsset>(CommonPath + "/Condition/HasAttackIntent.asset"),
                LoadOrCreatePrepareAttackPlan(),
                LoadRequired<EnemyAttackFlowNodeAsset>(CommonPath + "/Action/AttackFlow.asset"));
            combatHold.SetChildren(
                LoadRequired<EnemyGenerateAttackIntentNodeAsset>(CommonPath + "/Action/GenerateAttackIntent.asset"),
                LoadRequired<EnemySetIntentNodeAsset>(CommonPath + "/Action/SetIntentCombatIdle.asset"));

            EditorUtility.SetDirty(turnToTarget);
            EditorUtility.SetDirty(dodgeDecision);
            EditorUtility.SetDirty(defenseDecision);
            EditorUtility.SetDirty(combatHold);
        }

        /// <summary>创建并重连剑盾精英专用根节点和战斗计划组合节点。</summary>
        private static void RebuildEliteRootAndCombatLayer()
        {
            EnsureEliteFolders();

            ReactivePrioritySelectorNodeAsset root = LoadOrCreate<ReactivePrioritySelectorNodeAsset>(
                ElitePath + "/RootLayerSelector.asset", "RootLayerSelector");
            ReactiveSequenceNodeAsset combatLayer = LoadOrCreate<ReactiveSequenceNodeAsset>(
                ElitePath + "/Combat/CombatLayer.asset", "CombatLayer");
            RepeatForeverNodeAsset repeatCombat = LoadOrCreate<RepeatForeverNodeAsset>(
                ElitePath + "/Combat/RepeatCombatPlan.asset", "RepeatCombatPlan");
            ReactivePrioritySelectorNodeAsset combatSelector = LoadOrCreate<ReactivePrioritySelectorNodeAsset>(
                ElitePath + "/Combat/CombatPlanSelector.asset", "CombatPlanSelector");
            SequenceNodeAsset turnToTarget = LoadOrCreate<SequenceNodeAsset>(
                ElitePath + "/Combat/TurnToTargetSequence.asset", "TurnToTargetSequence");
            SequenceNodeAsset dodgeDecision = LoadOrCreate<SequenceNodeAsset>(
                ElitePath + "/Combat/DodgeDecisionSequence.asset", "DodgeDecisionSequence");
            SequenceNodeAsset defenseDecision = LoadOrCreate<SequenceNodeAsset>(
                ElitePath + "/Combat/DefenseDecisionSequence.asset", "DefenseDecisionSequence");
            SequenceNodeAsset attackSequence = LoadOrCreate<SequenceNodeAsset>(
                ElitePath + "/Combat/AttackSequence.asset", "AttackSequence");
            ReactiveSequenceNodeAsset combatHold = LoadOrCreate<ReactiveSequenceNodeAsset>(
                ElitePath + "/Combat/CombatHoldSequence.asset", "CombatHoldSequence");
            BehaviorTreeAsset tree = LoadOrCreate<BehaviorTreeAsset>(
                ElitePath + "/SwordAndShieldEliteEnemyBehaviorTree.asset", "SwordAndShieldEliteEnemyBehaviorTree");

            root.SetChildren(
                LoadRequired<EnemyInterruptExecutorNodeAsset>(CommonPath + "/Action/InterruptExecutor.asset"),
                combatLayer,
                LoadRequired<ReactiveSequenceNodeAsset>(CommonPath + "/Sequence/AlertLayer.asset"),
                LoadRequired<ReactiveSequenceNodeAsset>(CommonPath + "/Sequence/NormalLayer.asset"));
            combatLayer.SetChildren(
                LoadRequired<EnemyHasCombatTargetNodeAsset>(CommonPath + "/Condition/HasCombatTarget.asset"),
                LoadRequired<EnemyEnsureCombatStanceNodeAsset>(CommonPath + "/Action/EnsureCombatStance.asset"),
                repeatCombat);
            repeatCombat.SetChild(combatSelector);
            combatSelector.SetChildren(turnToTarget, dodgeDecision, defenseDecision, attackSequence, combatHold);
            RebuildDecisionSequences(ElitePath + "/Combat", attackSequence);
            tree.SetRoot(root);

            EditorUtility.SetDirty(root);
            EditorUtility.SetDirty(combatLayer);
            EditorUtility.SetDirty(repeatCombat);
            EditorUtility.SetDirty(combatSelector);
            EditorUtility.SetDirty(attackSequence);
            EditorUtility.SetDirty(tree);
        }

        /// <summary>确保剑盾精英行为树目录存在，供构建器创建独立组合节点。</summary>
        private static void EnsureEliteFolders()
        {
            EnsureFolder("Assets/Game/Character/Enemy/Config/BehaviorTrees", "SwordAndShieldEliteEnemy");
            EnsureFolder(ElitePath, "Combat");
        }

        /// <summary>存在时跳过，不存在时创建指定 Unity 资源目录。</summary>
        private static void EnsureFolder(string parentPath, string folderName)
        {
            string path = parentPath + "/" + folderName;
            if (!AssetDatabase.IsValidFolder(path))
            {
                AssetDatabase.CreateFolder(parentPath, folderName);
            }
        }

        /// <summary>迁移剑盾精英定义到确认后的九技能攻击计划配置。</summary>
        private static void MigrateSwordAndShieldEliteDefinition()
        {
            EnemyDefinition definition = LoadRequired<EnemyDefinition>(EliteDefinitionPath);
            EnemyCombatConfig oldConfig = definition.CombatConfig;

            EnemyCombatConfig newConfig = new EnemyCombatConfig
            {
                basicAttacks = new[]
                {
                    new EnemyAttackConfig(20301, "Attack1", 1f),
                    new EnemyAttackConfig(20302, "Attack2", 0.7f),
                    new EnemyAttackConfig(20303, "Attack3", 0.8f),
                    new EnemyAttackConfig(20304, "Attack4", 0.7f),
                    new EnemyAttackConfig(20305, "Attack5", 0.5f),
                    new EnemyAttackConfig(20306, "Retreat", 0.4f)
                },
                approachAttacks = new[]
                {
                    new EnemyAttackConfig(20307, "Attack5", 1f)
                },
                pursuitAttacks = new[]
                {
                    new EnemyAttackConfig(20308, "Attack4", 1f)
                },
                counterAttack = new EnemyAttackConfig(20309, "Attack3", 1f),
                comboBranches = new[]
                {
                    new EnemyComboBranchConfig(20302, new[] { 20303, 20304, 20305, 20306 }, 1f)
                },
                counterBlockThreshold = 2,
                combatEnterRange = oldConfig.combatEnterRange,
                chaseRange = oldConfig.chaseRange,
                combatMemoryDuration = oldConfig.combatMemoryDuration,
                canInterruptAttack = oldConfig.canInterruptAttack
            };

            definition.SetEnemyId("SwordAndShieldElite");
            definition.SetDisplayName("SwordAndShieldElite");
            definition.SetBehaviorTreeAsset(LoadRequired<BehaviorTreeAsset>(
                ElitePath + "/SwordAndShieldEliteEnemyBehaviorTree.asset"));
            definition.SetCombatConfig(newConfig);
            EditorUtility.SetDirty(definition);
        }

        /// <summary>读取或创建通用攻击计划准备动作资产，保持资产路径稳定。</summary>
        private static EnemyPrepareAttackPlanNodeAsset LoadOrCreatePrepareAttackPlan()
        {
            const string path = CommonPath + "/Action/PrepareAttackPlan.asset";
            EnemyPrepareAttackPlanNodeAsset asset =
                AssetDatabase.LoadAssetAtPath<EnemyPrepareAttackPlanNodeAsset>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<EnemyPrepareAttackPlanNodeAsset>();
            asset.name = "PrepareAttackPlan";
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>把三个普通敌人定义迁移到基础攻击池、空进身/追击池和空反击配置。</summary>
        private static void MigrateOrdinaryEnemyDefinitions()
        {
            MigrateOrdinaryEnemyDefinition(
                "Assets/Game/Character/Enemy/Config/Definitions/GreatSwordEnemyDefinition.asset");
            MigrateOrdinaryEnemyDefinition(
                "Assets/Game/Character/Enemy/Config/Definitions/SpearEenemyDefinition.asset");
            MigrateOrdinaryEnemyDefinition(
                "Assets/Game/Character/Enemy/Config/Definitions/SwordAndShieldEnemyDefinition.asset");
        }

        /// <summary>重写单个普通敌人定义的战斗配置，只保留当前普通攻击和组合数据。</summary>
        private static void MigrateOrdinaryEnemyDefinition(string path)
        {
            EnemyDefinition definition = LoadRequired<EnemyDefinition>(path);
            EnemyCombatConfig oldConfig = definition.CombatConfig;
            EnemyCombatConfig newConfig = new EnemyCombatConfig
            {
                basicAttacks = CopyAttacks(oldConfig.basicAttacks),
                approachAttacks = new EnemyAttackConfig[0],
                pursuitAttacks = new EnemyAttackConfig[0],
                counterAttack = new EnemyAttackConfig(),
                comboBranches = CopyCombos(oldConfig.comboBranches),
                counterBlockThreshold = 2,
                combatEnterRange = oldConfig.combatEnterRange,
                chaseRange = oldConfig.chaseRange,
                combatMemoryDuration = oldConfig.combatMemoryDuration,
                canInterruptAttack = oldConfig.canInterruptAttack
            };

            definition.SetCombatConfig(newConfig);
            EditorUtility.SetDirty(definition);
        }

        /// <summary>复制攻击配置数组，避免迁移时继续持有旧序列化对象。</summary>
        private static EnemyAttackConfig[] CopyAttacks(EnemyAttackConfig[] attacks)
        {
            if (attacks == null || attacks.Length == 0)
            {
                return new EnemyAttackConfig[0];
            }

            EnemyAttackConfig[] result = new EnemyAttackConfig[attacks.Length];
            for (int i = 0; i < attacks.Length; i++)
            {
                EnemyAttackConfig attack = attacks[i];
                result[i] = new EnemyAttackConfig(attack.skillId, attack.animationName, attack.weight)
                {
                    enableAttackDistanceCheck = attack.enableAttackDistanceCheck
                };
            }

            return result;
        }

        /// <summary>复制组合分支配置数组，保持普通敌人的既有连招路线。</summary>
        private static EnemyComboBranchConfig[] CopyCombos(EnemyComboBranchConfig[] combos)
        {
            if (combos == null || combos.Length == 0)
            {
                return new EnemyComboBranchConfig[0];
            }

            EnemyComboBranchConfig[] result = new EnemyComboBranchConfig[combos.Length];
            for (int i = 0; i < combos.Length; i++)
            {
                EnemyComboBranchConfig combo = combos[i];
                int[] sequence = combo.sequenceSkillIds != null
                    ? (int[])combo.sequenceSkillIds.Clone()
                    : new int[0];
                result[i] = new EnemyComboBranchConfig(combo.startSkillId, sequence, combo.probability);
            }

            return result;
        }

        /// <summary>删除旧统一攻击范围分支资产，防止通用树再次接回旧距离结构。</summary>
        private static void DeleteLegacyDistanceAssets()
        {
            DeleteAssetIfExists(CommonPath + "/Sequence/ChaseSequence.asset");
            DeleteAssetIfExists(CommonPath + "/Sequence/InAttackRangeSequence.asset");
            DeleteAssetIfExists(CommonPath + "/Condition/IsInAttackRange.asset");
            DeleteAssetIfExists(CommonPath + "/Condition/IsOutsideAttackRange.asset");
        }

        /// <summary>存在时删除指定资产，不存在则保持幂等。</summary>
        private static void DeleteAssetIfExists(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }
        }

        /// <summary>读取必需资产，缺失时立即失败，避免构建出半连接行为树。</summary>
        private static T LoadRequired<T>(string path)
            where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
            {
                throw new System.InvalidOperationException("缺少必需行为树资产：" + path);
            }

            return asset;
        }

        /// <summary>读取或创建指定类型资产；路径已有其他类型资产时立即失败。</summary>
        private static T LoadOrCreate<T>(string path, string assetName)
            where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                asset.name = assetName;
                EditorUtility.SetDirty(asset);
                return asset;
            }

            Object existingAsset = AssetDatabase.LoadAssetAtPath<Object>(path);
            if (existingAsset != null)
            {
                throw new System.InvalidOperationException("行为树资产类型不匹配：" + path);
            }

            asset = ScriptableObject.CreateInstance<T>();
            asset.name = assetName;
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
