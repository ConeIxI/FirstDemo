using Game.Battle.Ability;
using Game.Character.Equipment;
using UnityEngine;
using CharacterController = Game.Character.CharacterController;

namespace GameMain2.Scripts.Character
{
    public class PlayerController : CharacterController, ICombatMotion
    {
        private const string MissingAbilitySystemError =
            "PlayerController 缺少同一 GameObject 上的 CombatAbilitySystem，组件已禁用。";

        [SerializeField] private PlayerSkillManager skillManager;
        [SerializeField] private CombatAbilitySystem abilitySystem;
        [SerializeField] private float defaultAttackRange = 2f;

        public EquipmentManager EquipmentManager;
        public WeaponHandler WeaponHandler;
        public Transform rightHandHolder;

        public PlayerSkillManager SkillManager => skillManager;
        public float DefaultAttackRange => defaultAttackRange;
        public CombatAbilitySystem AbilitySystem => abilitySystem != null
            ? abilitySystem
            : GetComponent<CombatAbilitySystem>();

        /// <summary>解析玩家能力系统并初始化鼠标锁定状态。</summary>
        private void Awake()
        {
            abilitySystem = AbilitySystem;

            if (abilitySystem == null)
            {
                Debug.LogError(MissingAbilitySystemError, this);
                enabled = false;
                return;
            }

            Cursor.lockState = CursorLockMode.Locked;
        }

        /// <summary>按帧处理玩家重力。</summary>
        private void Update()
        {
            ProcessGravity();
        }

        /// <summary>将玩家模型平滑转向目标方向。</summary>
        public override void Rotate(Vector3 targetDir)
        {
            Vector3 horizontalDirection = new Vector3(targetDir.x, 0, targetDir.z);
            if (horizontalDirection.sqrMagnitude < 0.0001f)
            {
                return;
            }

            model.rotation = Quaternion.Lerp(
                model.rotation,
                Quaternion.LookRotation(horizontalDirection),
                Time.deltaTime * RotateSpeed);
        }

        /// <summary>立即把玩家模型转到指定旋转。</summary>
        public override void RotateInstantly(Quaternion quaternion)
        {
            model.rotation = quaternion;
        }

        /// <summary>应用战斗产生的外部位移。</summary>
        public void ApplyExternalDisplacement(Vector3 offset)
        {
            Move(offset);
        }
    }
}
