using UnityEngine;

namespace Game.Character
{
    public class CharacterController :MonoBehaviour
    {
        [SerializeField]
        protected UnityEngine.CharacterController controller;
        [SerializeField]
        protected Transform model;
        // [SerializeField]
        // private PlayerSkillManager skillManager;
        
        
        #region 配置
        public float RotateSpeed = 5;
        public float gravity = -9.8f;

        public bool useGravity = true;

        //跳跃高度
        public float jumpHeight = 1;
    
        //空中移动速度
        public float airMoveSpeed = 1f;

        private Vector3 m_lastSpeedSamplePosition;
        private float m_currentHorizontalSpeed;
        private bool m_hasSpeedSample;
    
        #endregion


        public Transform Model
        {
            get => model;
        }
        // public PlayerSkillManager SkillManager
        // {
        //     get => skillManager;
        // }

        /// <summary>按帧处理角色重力，缩放时间暂停时不推进角色位移。</summary>
        private void Update()
        {
            ProcessGravity();
        }

        /// <summary>每帧结束时刷新角色真实水平移动速度。</summary>
        protected virtual void LateUpdate()
        {
            RefreshCurrentHorizontalSpeed();
        }
        
        
        /// <summary>通过 CharacterController 执行位移。</summary>
        public void Move(Vector3 moveDir)
        {
            if (!this || controller == null)
                return;

            if (moveDir.sqrMagnitude != 0)
                controller.Move( moveDir);
        }

        /// <summary>获取角色上一帧真实水平移动速度，单位为米/秒。</summary>
        public float GetCurrentHorizontalSpeed()
        {
            return m_currentHorizontalSpeed;
        }

        /// <summary>根据 Transform 的实际位置差计算水平速度，覆盖 Root Motion 和外部位移。</summary>
        private void RefreshCurrentHorizontalSpeed()
        {
            if (!m_hasSpeedSample)
            {
                m_lastSpeedSamplePosition = transform.position;
                m_hasSpeedSample = true;
                return;
            }

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f)
            {
                m_lastSpeedSamplePosition = transform.position;
                return;
            }

            Vector3 delta = transform.position - m_lastSpeedSamplePosition;
            delta.y = 0f;
            m_currentHorizontalSpeed = delta.magnitude / deltaTime;
            m_lastSpeedSamplePosition = transform.position;
        }
    

        /// <summary>按缩放时间施加重力，暂停或停帧时避免零位移刷新 CharacterController 接地状态。</summary>
        public void ProcessGravity()
        {
            if (!this || controller == null)
                return;

            float deltaTime = Time.deltaTime;
            if (useGravity && deltaTime > 0f)
            {
                controller.Move(new Vector3(0,gravity * deltaTime,0));
            }
        }

        public virtual void Rotate(Vector3 targetDir)
        { 
            
        }

        public virtual void RotateInstantly(Quaternion quaternion)
        {
            
        }

        public bool IsGrounded()
        {
            if (!this || controller == null)
                return false;

            return controller.isGrounded;
        }

        /// <summary>设置 Unity CharacterController 的碰撞检测能力，供技能位移期间临时穿过碰撞体。</summary>
        public void SetControllerCollisionEnabled(bool isEnabled)
        {
            if (!this || controller == null)
                return;

            controller.detectCollisions = isEnabled;
        }
    }
}
