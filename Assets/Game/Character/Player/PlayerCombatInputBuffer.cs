namespace Game.Character.Player
{
    public enum PlayerCombatBufferedInputType
    {
        None,
        NormalAttack,
        WeaponSkill,
        Roll
    }

    public class PlayerCombatInputBuffer
    {
        private const int InvalidSkillSlotIndex = -1;

        private PlayerCombatBufferedInputType m_inputType = PlayerCombatBufferedInputType.None;
        private int m_skillSlotIndex = InvalidSkillSlotIndex;
        private float m_expireTime;

        /// <summary>
        /// 清理当前缓存的战斗输入，避免旧输入在后续决策窗口中被误消费。
        /// </summary>
        public void Clear()
        {
            m_inputType = PlayerCombatBufferedInputType.None;
            m_skillSlotIndex = InvalidSkillSlotIndex;
            m_expireTime = 0f;
        }

        /// <summary>
        /// 记录一次普攻预输入，并用保留时长计算过期时间。
        /// </summary>
        public void RecordNormalAttack(float currentTime, float bufferSeconds)
        {
            m_inputType = PlayerCombatBufferedInputType.NormalAttack;
            m_skillSlotIndex = InvalidSkillSlotIndex;
            m_expireTime = currentTime + bufferSeconds;
        }

        /// <summary>
        /// 记录一次武器技能预输入，新的技能输入会覆盖旧输入。
        /// </summary>
        public void RecordWeaponSkill(int slotIndex, float currentTime, float bufferSeconds)
        {
            m_inputType = PlayerCombatBufferedInputType.WeaponSkill;
            m_skillSlotIndex = slotIndex;
            m_expireTime = currentTime + bufferSeconds;
        }

        /// <summary>
        /// 记录一次翻滚预输入，新的翻滚输入会覆盖旧输入。
        /// </summary>
        public void RecordRoll(float currentTime, float bufferSeconds)
        {
            m_inputType = PlayerCombatBufferedInputType.Roll;
            m_skillSlotIndex = InvalidSkillSlotIndex;
            m_expireTime = currentTime + bufferSeconds;
        }

        /// <summary>
        /// 尝试消费有效期内的普攻预输入，成功后立即清空缓存。
        /// </summary>
        public bool TryConsumeNormalAttack(float currentTime)
        {
            if (!HasBufferedInput(currentTime) || m_inputType != PlayerCombatBufferedInputType.NormalAttack)
            {
                return false;
            }

            Clear();
            return true;
        }

        /// <summary>
        /// 尝试消费有效期内的武器技能预输入，成功后返回技能槽位并清空缓存。
        /// </summary>
        public bool TryConsumeWeaponSkill(float currentTime, out int slotIndex)
        {
            slotIndex = InvalidSkillSlotIndex;
            if (!HasBufferedInput(currentTime) || m_inputType != PlayerCombatBufferedInputType.WeaponSkill)
            {
                return false;
            }

            slotIndex = m_skillSlotIndex;
            Clear();
            return true;
        }

        /// <summary>
        /// 尝试消费有效期内的翻滚预输入，成功后立即清空缓存。
        /// </summary>
        public bool TryConsumeRoll(float currentTime)
        {
            if (!HasBufferedInput(currentTime) || m_inputType != PlayerCombatBufferedInputType.Roll)
            {
                return false;
            }

            Clear();
            return true;
        }

        /// <summary>
        /// 判断当前是否存在仍在有效期内的预输入；过期输入会被同步清理。
        /// </summary>
        public bool HasBufferedInput(float currentTime)
        {
            if (m_inputType == PlayerCombatBufferedInputType.None)
            {
                return false;
            }

            if (currentTime > m_expireTime)
            {
                Clear();
                return false;
            }

            return true;
        }
    }
}
