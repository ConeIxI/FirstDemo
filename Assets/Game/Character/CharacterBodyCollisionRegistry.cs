using System.Collections.Generic;
using UnityEngine;

namespace Game.Character
{
    public static class CharacterBodyCollisionRegistry
    {
        private const float BodyPadding = 0.02f;
        private const float VerticalOverlapPadding = 0.05f;
        private const float MinimumSqrMagnitude = 0.0001f;

        private static readonly List<UnityEngine.CharacterController> Controllers =
            new List<UnityEngine.CharacterController>();

        /// <summary>
        /// 注册角色主体控制器，并关闭已注册角色之间的 Unity 原生碰撞，避免 CharacterController 把对方当台阶爬上去。
        /// </summary>
        public static void Register(UnityEngine.CharacterController controller)
        {
            if (controller == null
                || !controller.enabled
                || !controller.gameObject.activeInHierarchy
                || Controllers.Contains(controller))
            {
                return;
            }

            CleanupInvalidControllers();
            for (int i = 0; i < Controllers.Count; i++)
            {
                UnityEngine.CharacterController other = Controllers[i];
                if (other != null && other.enabled && other.gameObject.activeInHierarchy)
                {
                    Physics.IgnoreCollision(controller, other, true);
                }
            }

            Controllers.Add(controller);
        }

        /// <summary>
        /// 注销角色主体控制器，让后续水平阻挡计算不再引用已失效对象。
        /// </summary>
        public static void Unregister(UnityEngine.CharacterController controller)
        {
            if (controller == null)
            {
                return;
            }

            Controllers.Remove(controller);
        }

        /// <summary>
        /// 解析角色本帧位移，保留 Y 轴重力/跳跃，只裁剪 XZ 平面里会挤进其他角色身体的移动。
        /// </summary>
        public static Vector3 ResolveDisplacement(UnityEngine.CharacterController mover, Vector3 displacement)
        {
            if (mover == null || !mover.detectCollisions || displacement.sqrMagnitude <= MinimumSqrMagnitude)
            {
                return displacement;
            }

            Vector3 horizontal = new Vector3(displacement.x, 0f, displacement.z);
            if (horizontal.sqrMagnitude <= MinimumSqrMagnitude)
            {
                return displacement;
            }

            Vector3 resolvedHorizontal = ResolveHorizontalDisplacement(mover, horizontal);
            return new Vector3(resolvedHorizontal.x, displacement.y, resolvedHorizontal.z);
        }

        /// <summary>
        /// 逐个检查其他角色主体，移除会造成水平重叠或穿越的位移分量。
        /// </summary>
        private static Vector3 ResolveHorizontalDisplacement(UnityEngine.CharacterController mover, Vector3 horizontal)
        {
            CleanupInvalidControllers();
            Vector2 move = new Vector2(horizontal.x, horizontal.z);
            for (int i = 0; i < Controllers.Count; i++)
            {
                UnityEngine.CharacterController other = Controllers[i];
                if (!CanBlock(mover, other))
                {
                    continue;
                }

                move = ResolveAgainstController(mover, other, move);
                if (move.sqrMagnitude <= MinimumSqrMagnitude)
                {
                    return Vector3.zero;
                }
            }

            return new Vector3(move.x, 0f, move.y);
        }

        /// <summary>
        /// 判断两个角色主体是否需要参与水平阻挡计算。
        /// </summary>
        private static bool CanBlock(UnityEngine.CharacterController mover, UnityEngine.CharacterController other)
        {
            return other != null
                   && other != mover
                   && other.enabled
                   && other.detectCollisions
                   && other.gameObject.activeInHierarchy
                   && HasVerticalOverlap(mover, other);
        }

        /// <summary>
        /// 根据两个角色胶囊的垂直范围判断是否处在同一可阻挡高度层。
        /// </summary>
        private static bool HasVerticalOverlap(
            UnityEngine.CharacterController first,
            UnityEngine.CharacterController second)
        {
            GetVerticalRange(first, out float firstBottom, out float firstTop);
            GetVerticalRange(second, out float secondBottom, out float secondTop);
            return firstTop >= secondBottom - VerticalOverlapPadding
                   && secondTop >= firstBottom - VerticalOverlapPadding;
        }

        /// <summary>
        /// 按世界坐标读取角色胶囊的垂直底部和顶部。
        /// </summary>
        private static void GetVerticalRange(
            UnityEngine.CharacterController controller,
            out float bottom,
            out float top)
        {
            Vector3 center = controller.transform.TransformPoint(controller.center);
            float height = Mathf.Abs(controller.height * controller.transform.lossyScale.y);
            float halfHeight = height * 0.5f;
            bottom = center.y - halfHeight;
            top = center.y + halfHeight;
        }

        /// <summary>
        /// 解析移动角色相对单个阻挡角色的 XZ 位移，支持靠近时滑动，禁止穿身。
        /// </summary>
        private static Vector2 ResolveAgainstController(
            UnityEngine.CharacterController mover,
            UnityEngine.CharacterController other,
            Vector2 move)
        {
            if (move.sqrMagnitude <= MinimumSqrMagnitude)
            {
                return move;
            }

            Vector2 moverCenter = GetHorizontalCenter(mover);
            Vector2 otherCenter = GetHorizontalCenter(other);
            Vector2 fromOther = moverCenter - otherCenter;
            float minDistance = GetHorizontalRadius(mover) + GetHorizontalRadius(other) + BodyPadding;
            float minSqrDistance = minDistance * minDistance;

            if (fromOther.sqrMagnitude < minSqrDistance)
            {
                return RemoveInwardMovement(move, fromOther, move);
            }

            return ResolveSweepMovement(moverCenter, otherCenter, move, minDistance);
        }

        /// <summary>
        /// 检查本帧线段位移是否会穿过其他角色圆形主体，会穿过时截断并保留切向滑动。
        /// </summary>
        private static Vector2 ResolveSweepMovement(
            Vector2 moverCenter,
            Vector2 otherCenter,
            Vector2 move,
            float minDistance)
        {
            Vector2 fromOther = moverCenter - otherCenter;
            float a = Vector2.Dot(move, move);
            float b = 2f * Vector2.Dot(fromOther, move);
            float c = Vector2.Dot(fromOther, fromOther) - minDistance * minDistance;
            if (a <= MinimumSqrMagnitude || b >= 0f)
            {
                return move;
            }

            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f)
            {
                return move;
            }

            float hitTime = (-b - Mathf.Sqrt(discriminant)) / (2f * a);
            if (hitTime < 0f || hitTime > 1f)
            {
                return move;
            }

            float safeHitTime = Mathf.Max(0f, hitTime - BodyPadding / Mathf.Sqrt(a));
            Vector2 hitMove = move * safeHitTime;
            Vector2 remainingMove = move * (1f - safeHitTime);
            Vector2 normal = ResolveNormal(moverCenter + hitMove - otherCenter, move);
            Vector2 slideMove = RemoveInwardMovement(remainingMove, normal, remainingMove);
            return hitMove + slideMove;
        }

        /// <summary>
        /// 移除朝阻挡角色内部推进的位移分量，保留远离和切向滑动。
        /// </summary>
        private static Vector2 RemoveInwardMovement(Vector2 move, Vector2 fromOther, Vector2 fallbackMove)
        {
            Vector2 normal = ResolveNormal(fromOther, fallbackMove);
            float inwardAmount = Vector2.Dot(move, -normal);
            if (inwardAmount <= 0f)
            {
                return move;
            }

            return move + normal * inwardAmount;
        }

        /// <summary>
        /// 读取 CharacterController 在 XZ 平面的世界中心点。
        /// </summary>
        private static Vector2 GetHorizontalCenter(UnityEngine.CharacterController controller)
        {
            Vector3 center = controller.transform.TransformPoint(controller.center);
            return new Vector2(center.x, center.z);
        }

        /// <summary>
        /// 读取 CharacterController 在 XZ 平面的世界半径，兼容角色缩放。
        /// </summary>
        private static float GetHorizontalRadius(UnityEngine.CharacterController controller)
        {
            Vector3 scale = controller.transform.lossyScale;
            float horizontalScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            return controller.radius * horizontalScale;
        }

        /// <summary>
        /// 计算从阻挡角色指向移动角色的法线，重合时使用移动反方向作为稳定回退。
        /// </summary>
        private static Vector2 ResolveNormal(Vector2 fromOther, Vector2 fallbackMove)
        {
            if (fromOther.sqrMagnitude > MinimumSqrMagnitude)
            {
                return fromOther.normalized;
            }

            if (fallbackMove.sqrMagnitude > MinimumSqrMagnitude)
            {
                return -fallbackMove.normalized;
            }

            return Vector2.right;
        }

        /// <summary>
        /// 清理已经被销毁或失效的控制器引用，避免静态列表跨场景残留。
        /// </summary>
        private static void CleanupInvalidControllers()
        {
            for (int i = Controllers.Count - 1; i >= 0; i--)
            {
                UnityEngine.CharacterController controller = Controllers[i];
                if (controller == null)
                {
                    Controllers.RemoveAt(i);
                }
            }
        }
    }
}
