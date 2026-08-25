using Game.Character.Common;

namespace Game.Character
{
    /// <summary>
    /// 玩家战斗动作请求，替代 FSM 字符串数据传递武器类型和技能 ID。
    /// </summary>
    public readonly struct PlayerCombatActionRequest
    {
        public WeaponType WeaponType { get; }
        public int SkillId { get; }
        public bool IsValid => WeaponType != WeaponType.None && SkillId > 0;

        /// <summary>创建一次玩家战斗动作请求。</summary>
        public PlayerCombatActionRequest(WeaponType weaponType, int skillId)
        {
            WeaponType = weaponType;
            SkillId = skillId;
        }

        /// <summary>保留当前武器类型并切换到新的技能 ID，供普通攻击连段使用。</summary>
        public PlayerCombatActionRequest WithSkillId(int skillId)
        {
            return new PlayerCombatActionRequest(WeaponType, skillId);
        }
    }
}
