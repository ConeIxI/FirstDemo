# 敌人唯一锁定点设计

## 目标

玩家发起锁定检测时，敌人碰撞体只负责确认命中了哪个敌人。实际锁定目标统一使用该敌人根节点下名为 `CameraTarget` 的直接子对象，避免一个敌人的多个碰撞体生成多个锁定候选。

## 目标解析

1. `Physics.OverlapSphere` 继续按 `enemyLayer` 收集敌人碰撞体。
2. 从每个碰撞体向父级查找 `EnemyAgent`，以其 Transform 作为敌人根节点。
3. 在敌人根节点的直接子对象中查找 `CameraTarget`。
4. 缺少 `EnemyAgent` 或 `CameraTarget` 的碰撞体不参与锁定。
5. 使用 `CameraTarget` Transform 去重，同一敌人的多个碰撞体只产生一个候选目标。

## 锁定行为

视野过滤、遮挡检测、屏幕中心距离排序、`CurrentTarget`、Cinemachine TargetGroup、锁定光圈和玩家朝向全部使用 `CameraTarget` 的位置。自动解锁仍沿用现有距离和目标激活状态规则。

## 兼容边界

- `CameraTarget` 必须是带有 `EnemyAgent` 的敌人根节点的直接子对象，名称区分大小写。
- 不新增自动回退到碰撞体的逻辑，资源配置错误时该敌人不可锁定。
- 不修改鼠标滚轮切换目标、锁定距离、相机权重或敌人资源结构。

## 验证

- EditMode 测试验证：子碰撞体能够解析到敌人根节点的 `CameraTarget`。
- EditMode 测试验证：同一敌人的多个碰撞体只返回一个候选锁定点。
- EditMode 测试验证：缺少 `EnemyAgent` 或 `CameraTarget` 时不会返回候选目标。
- 使用 `$CLI compile unity` 完成 Unity 编译验证。

