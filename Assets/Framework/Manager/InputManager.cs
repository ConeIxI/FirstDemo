using Cinemachine;
using GameMain2.Scripts.UI;
using UnityEngine;

namespace GameMain2.Framework.Manager
{
    public class InputManager : SingletonManager<InputManager>
    {
        private const KeyCode RollKey = KeyCode.Space;

        private static CinemachineCore.AxisInputDelegate s_defaultCinemachineInput;
        private static bool s_cinemachineInputInstalled;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InstallCinemachineInputAxis()
        {
            if (s_cinemachineInputInstalled)
            {
                return;
            }

            s_defaultCinemachineInput = CinemachineCore.GetInputAxis;
            CinemachineCore.GetInputAxis = GetCinemachineInputAxis;
            s_cinemachineInputInstalled = true;
        }

        /// <summary>
        /// 获取玩家的移动方向。
        /// </summary>
        /// <returns>返回一个表示移动方向的Vector2对象，该向量已经归一化。</returns>
        public Vector2 GetMoveDirection()
        {
            if (IsGameplayInputBlocked())
            {
                return Vector2.zero;
            }

            float x = Input.GetAxis("Horizontal");
            float y = Input.GetAxis("Vertical");

            return new Vector2(x, y);
        }

        public Vector2 GetMoveDirectionRaw()
        {
            if (IsGameplayInputBlocked())
            {
                return Vector2.zero;
            }

            float x = Input.GetAxisRaw("Horizontal");
            float y = Input.GetAxisRaw("Vertical");
            return new Vector2(x, y);
        }

        /// <summary>
        /// 按下leftShift，用于奔跑和行走切换
        /// </summary>
        /// <returns></returns>
        public bool IsRunKeyPressed()
        {
            if (IsGameplayInputBlocked())
            {
                return false;
            }

            return Input.GetKey(KeyCode.LeftShift);
        }

        /// <summary>
        /// 检查本帧是否按下闪避键，当前闪避键为空格。
        /// </summary>
        public bool IsRollPressed()
        {
            if (IsGameplayInputBlocked())
            {
                return false;
            }

            return Input.GetKeyDown(RollKey);
        }

        public bool IsAttackKeyPressed()
        {
            if (IsGameplayInputBlocked())
            {
                return false;
            }

            return Input.GetMouseButtonDown(0);
        }

        /// <summary>
        /// 获取本帧按下的武器技能槽位；Q/E/R 分别对应槽位 0/1/2。
        /// </summary>
        public int GetPressedWeaponSkillSlot()
        {
            if (IsGameplayInputBlocked())
            {
                return -1;
            }

            if (Input.GetKeyDown(KeyCode.Q))
            {
                return 0;
            }

            if (Input.GetKeyDown(KeyCode.E))
            {
                return 1;
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                return 2;
            }

            return -1;
        }

        /// <summary>
        /// 获取本帧按下的消耗品槽位；1/2/3/4 和小键盘 1/2/3/4 分别对应槽位 0/1/2/3。
        /// </summary>
        public int GetPressedConsumableSlot()
        {
            if (IsGameplayInputBlocked())
            {
                return -1;
            }

            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Keypad1))
            {
                return 0;
            }

            if (Input.GetKeyDown(KeyCode.Alpha2) || Input.GetKeyDown(KeyCode.Keypad2))
            {
                return 1;
            }

            if (Input.GetKeyDown(KeyCode.Alpha3) || Input.GetKeyDown(KeyCode.Keypad3))
            {
                return 2;
            }

            if (Input.GetKeyDown(KeyCode.Alpha4) || Input.GetKeyDown(KeyCode.Keypad4))
            {
                return 3;
            }

            return -1;
        }

        public bool IsDefenseKeyPressed()
        {
            if (IsGameplayInputBlocked())
            {
                return false;
            }

            return Input.GetMouseButton(1);
        }

        public bool IsWeaponSwitchKeyPressed()
        {
            if (IsGameplayInputBlocked())
            {
                return false;
            }

            return Input.GetKeyDown(KeyCode.Tab);
        }

        /// <summary>
        /// UI 快捷键统一入口，具体键位由 UI 面板声明。
        /// </summary>
        public bool IsKeyPressed(KeyCode key)
        {
            return Input.GetKeyDown(key);
        }

        /// <summary>
        /// 鼠标中键，用于锁定/解锁目标
        /// </summary>
        public bool IsLockOnPressed()
        {
            if (IsGameplayInputBlocked())
            {
                return false;
            }

            return Input.GetMouseButtonDown(2);
        }

        /// <summary>
        /// 鼠标滚轮增量，正值向上滚动，负值向下滚动
        /// </summary>
        public float GetScrollDelta()
        {
            if (IsGameplayInputBlocked())
            {
                return 0f;
            }

            return Input.GetAxis("Mouse ScrollWheel");
        }

        private static bool IsGameplayInputBlocked()
        {
            return UIManager.Instance.IsGameplayInputBlocked();
        }

        private static float GetCinemachineInputAxis(string axisName)
        {
            // Cinemachine POV 会直接读取 Mouse X/Y，背包等交互 UI 打开时需要同步阻断视角输入。
            if (IsGameplayInputBlocked())
            {
                return 0f;
            }

            return s_defaultCinemachineInput == null ? Input.GetAxis(axisName) : s_defaultCinemachineInput(axisName);
        }
    }
}
