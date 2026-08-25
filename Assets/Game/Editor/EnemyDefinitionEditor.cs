using System.Collections.Generic;
using Game.Character.Enemy.Config;
using UnityEditor;
using UnityEngine;

namespace Game.Character.Enemy.Editor
{
    [CustomEditor(typeof(EnemyDefinition))]
    [CanEditMultipleObjects]
    public sealed class EnemyDefinitionEditor : UnityEditor.Editor
    {
        private static readonly GUIContent ScriptLabel = new GUIContent("脚本");
        private static readonly GUIContent ArraySizeLabel = new GUIContent("数量");

        private static readonly string[] TopLevelPropertyNames =
        {
            "enemyId",
            "displayName",
            "behaviorTreeAsset",
            "movementConfig",
            "perceptionConfig",
            "animationConfig",
            "combatConfig",
            "lifeConfig",
            "attributeConfig",
            "decisionProfile",
            "dropItems",
        };

        private static readonly Dictionary<string, string> ChineseLabels = new Dictionary<string, string>
        {
            { "enemyId", "敌人ID" },
            { "displayName", "显示名称" },
            { "behaviorTreeAsset", "行为树资源" },
            { "movementConfig", "移动配置" },
            { "perceptionConfig", "感知配置" },
            { "animationConfig", "动画配置" },
            { "combatConfig", "战斗配置" },
            { "lifeConfig", "生命配置" },
            { "attributeConfig", "属性配置" },
            { "decisionProfile", "决策配置" },
            { "dropItems", "掉落物配置" },
            { "itemType", "物品类型" },
            { "itemId", "物品ID" },
            { "count", "数量" },
            { "dropChance", "掉落概率" },
            { "moveSpeed", "移动速度" },
            { "rotateSpeed", "转向速度" },
            { "attackRotateSpeed", "攻击转向速度" },
            { "stoppingDistance", "停止距离" },
            { "navMeshSampleDistance", "导航采样距离" },
            { "patrolWaitDuration", "巡逻停留时间" },
            { "range", "感知范围" },
            { "angle", "感知角度" },
            { "closeAwarenessRange", "近身感知范围" },
            { "loseSightGraceTime", "丢失视野宽限时间" },
            { "alertMemoryDuration", "警戒记忆时间" },
            { "searchObservationDuration", "搜索观察时间" },
            { "searchRadius", "搜索半径" },
            { "searchPointCount", "搜索点数量" },
            { "targetMask", "目标层" },
            { "obstacleMask", "障碍层" },
            { "idleAnimation", "待机动画" },
            { "combatIdleAnimation", "战斗待机动画" },
            { "combatIdleMoveLeftAnimation", "战斗待机左移" },
            { "combatIdleMoveRightAnimation", "战斗待机右移" },
            { "enterCombatAnimation", "进入战斗动画" },
            { "exitCombatAnimation", "退出战斗动画" },
            { "turnAnimation", "转身动画" },
            { "alertMoveAnimation", "警戒移动动画" },
            { "moveAnimation", "移动动画" },
            { "runAnimation", "奔跑动画" },
            { "defenseAnimation", "防御动画" },
            { "defenseHitAnimation", "防御受击动画" },
            { "retreatAnimation", "后撤动画" },
            { "getHitAnimation", "受击动画" },
            { "defenseBreakAnimation", "失衡破防动画" },
            { "unbalanceStartAnimation", "失衡开始动画" },
            { "unbalanceStartTrigger", "失衡开始Trigger" },
            { "unbalanceLoopAnimation", "失衡循环动画" },
            { "unbalanceEndAnimation", "失衡结束动画" },
            { "unbalanceEndTrigger", "失衡结束Trigger" },
            { "unbalanceLoopDuration", "失衡循环时长" },
            { "deadAnimation", "死亡动画" },
            { "transitionDuration", "过渡时长" },
            { "basicAttacks", "近距离攻击池" },
            { "approachAttacks", "进身攻击池" },
            { "pursuitAttacks", "追击攻击池" },
            { "retreatAttacks", "远离攻击池" },
            { "closeAttackPoolWeight", "近距离攻击池权重" },
            { "retreatAttackPoolWeight", "远离攻击池权重" },
            { "retreatWeightBonusAfterCloseAttack", "近战后远离权重加成" },
            { "retreatWeightBonusLimit", "远离权重加成上限" },
            { "resetRetreatBonusAfterRetreat", "远离后重置加成" },
            { "counterAttack", "反击" },
            { "counterBlockThreshold", "反击格挡次数" },
            { "comboBranches", "组合分支" },
            { "combatEnterRange", "战斗进入范围" },
            { "chaseRange", "追击范围" },
            { "combatMemoryDuration", "战斗记忆时间" },
            { "canInterruptAttack", "可被打断攻击" },
            { "rememberTargetOnHit", "受击记住目标" },
            { "allowUnbalanceReaction", "允许失衡反应" },
            { "allowDeathReaction", "允许死亡反应" },
            { "maxHealth", "最大生命" },
            { "maxStability", "最大稳定值" },
            { "attack", "攻击力" },
            { "defense", "防御力" },
            { "moveSpeedMultiplier", "移速倍率" },
            { "perceptionMultiplier", "感知倍率" },
            { "attackDesire", "攻击欲望" },
            { "defenseRate", "防御率" },
            { "defenseDuration", "防御持续时间" },
            { "attackDecisionCooldown", "攻击决策冷却" },
            { "attackWeightCompensationPerMiss", "攻击权重未选补偿" },
            { "attackWeightGuaranteeMissCount", "攻击权重保底次数" },
            { "dodgeRate", "闪避率" },
            { "dodgeCooldown", "闪避冷却" },
            { "lowStabilityThreshold", "低稳定值阈值" },
            { "skillId", "技能ID" },
            { "animationName", "动画名" },
            { "weight", "权重" },
            { "enableAttackDistanceCheck", "启用攻击距离检测" },
            { "startSkillId", "起始技能ID" },
            { "sequenceSkillIds", "后续技能ID" },
            { "probability", "分支概率" },
        };

        /// <summary>绘制敌人定义资产的中文 Inspector。</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawReadonlyScriptField();
            DrawTopLevelProperties();

            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>绘制只读脚本字段，保持 Unity 默认 Inspector 的基础信息。</summary>
        private void DrawReadonlyScriptField()
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"), ScriptLabel);
            }
        }

        /// <summary>按配置结构顺序绘制顶层字段，避免默认英文字段名泄漏。</summary>
        private void DrawTopLevelProperties()
        {
            for (int i = 0; i < TopLevelPropertyNames.Length; i++)
            {
                SerializedProperty property = serializedObject.FindProperty(TopLevelPropertyNames[i]);
                DrawProperty(property, GetFieldLabel(property.name));
            }
        }

        /// <summary>根据字段类型分发绘制逻辑，数组和嵌套配置会继续中文化子项。</summary>
        private static void DrawProperty(SerializedProperty property, string label)
        {
            if (property.isArray && property.propertyType != SerializedPropertyType.String)
            {
                DrawArrayProperty(property, label);
                return;
            }

            if (property.propertyType == SerializedPropertyType.Generic && property.hasVisibleChildren)
            {
                DrawGenericProperty(property, label);
                return;
            }

            EditorGUILayout.PropertyField(property, new GUIContent(label), true);
        }

        /// <summary>绘制数组字段，统一把 Size 和 Element 标签替换为中文。</summary>
        private static void DrawArrayProperty(SerializedProperty property, string label)
        {
            property.isExpanded = EditorGUILayout.Foldout(property.isExpanded, new GUIContent(label), true);
            if (!property.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            EditorGUILayout.PropertyField(property.FindPropertyRelative("Array.size"), ArraySizeLabel);
            for (int i = 0; i < property.arraySize; i++)
            {
                SerializedProperty element = property.GetArrayElementAtIndex(i);
                DrawProperty(element, GetElementLabel(i));
            }
            EditorGUI.indentLevel--;
        }

        /// <summary>绘制嵌套配置对象，展开后递归绘制全部子字段。</summary>
        private static void DrawGenericProperty(SerializedProperty property, string label)
        {
            property.isExpanded = EditorGUILayout.Foldout(property.isExpanded, new GUIContent(label), true);
            if (!property.isExpanded)
            {
                return;
            }

            EditorGUI.indentLevel++;
            DrawVisibleChildren(property);
            EditorGUI.indentLevel--;
        }

        /// <summary>遍历并绘制当前序列化属性的直接子字段。</summary>
        private static void DrawVisibleChildren(SerializedProperty property)
        {
            SerializedProperty iterator = property.Copy();
            SerializedProperty endProperty = iterator.GetEndProperty();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, endProperty))
            {
                DrawProperty(iterator.Copy(), GetFieldLabel(iterator.name));
                enterChildren = false;
            }
        }

        /// <summary>获取字段中文显示名，未配置字段沿用 Unity 的美化名称。</summary>
        private static string GetFieldLabel(string fieldName)
        {
            if (ChineseLabels.TryGetValue(fieldName, out string label))
            {
                return label;
            }

            return ObjectNames.NicifyVariableName(fieldName);
        }

        /// <summary>获取数组元素中文显示名，序号从一开始便于策划查看。</summary>
        private static string GetElementLabel(int index)
        {
            return $"第 {index + 1} 项";
        }
    }
}
