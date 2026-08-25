using System;

namespace Game.Character.Enemy.Config
{
    [Serializable]
    public sealed class EnemyComboBranchConfig
    {
        public int startSkillId;
        public int[] sequenceSkillIds = new int[0];
        public float probability = 1f;

        /// <summary>创建空组合分支配置，供 Unity 序列化使用。</summary>
        public EnemyComboBranchConfig()
        {
        }

        /// <summary>创建指定起始技能、后续技能序列和分支概率的组合配置。</summary>
        public EnemyComboBranchConfig(int startSkillId, int[] sequenceSkillIds, float probability)
        {
            this.startSkillId = startSkillId;
            this.sequenceSkillIds = sequenceSkillIds;
            this.probability = probability;
        }
    }
}
