using System;

namespace Game.Character.Enemy.AI.Combat
{
    public enum EnemyAttackPlanType
    {
        Basic,
        Approach,
        Retreat,
        Pursuit,
        Counter
    }

    public enum EnemyAttackPreparationMode
    {
        Direct,
        Approach,
        Pursuit
    }

    public sealed class EnemyAttackPlan
    {
        private EnemyAttackRuntimeConfig[] comboRoute = new EnemyAttackRuntimeConfig[0];
        private int nextComboIndex;

        public EnemyAttackPlanType Type { get; }
        public EnemyAttackPreparationMode PreparationMode { get; }
        public EnemyAttackRuntimeConfig CurrentAttack { get; private set; }
        public float ReleaseDistance { get; private set; }
        public bool HasComboRoute => comboRoute.Length > 0;

        /// <summary>创建已选定技能和释放距离的攻击计划。</summary>
        public EnemyAttackPlan(
            EnemyAttackPlanType type,
            EnemyAttackPreparationMode preparationMode,
            EnemyAttackRuntimeConfig attack,
            float releaseDistance)
        {
            Type = type;
            PreparationMode = preparationMode;
            CurrentAttack = attack;
            ReleaseDistance = releaseDistance;
        }

        /// <summary>切换到锁定连招的下一段技能。</summary>
        public void SetCurrentAttack(EnemyAttackRuntimeConfig attack)
        {
            CurrentAttack = attack;
            ReleaseDistance = attack.AttackRange;
        }

        /// <summary>锁定本次攻击计划的连招路线，并从第一段后续技能开始等待衔接。</summary>
        public void SetComboRoute(EnemyAttackRuntimeConfig[] attacks)
        {
            if (attacks == null || attacks.Length == 0)
            {
                throw new InvalidOperationException("连招路线不能为空");
            }

            comboRoute = attacks;
            nextComboIndex = 0;
        }

        /// <summary>读取下一段待衔接技能，不推进路线索引。</summary>
        public bool TryPeekNextComboAttack(out EnemyAttackRuntimeConfig attack)
        {
            if (nextComboIndex >= comboRoute.Length)
            {
                attack = null;
                return false;
            }

            attack = comboRoute[nextComboIndex];
            return true;
        }

        /// <summary>把当前攻击切换到下一段连招技能，并推进路线索引。</summary>
        public void AdvanceToNextComboAttack()
        {
            if (!TryPeekNextComboAttack(out EnemyAttackRuntimeConfig attack))
            {
                throw new InvalidOperationException("没有可推进的下一段连招");
            }

            nextComboIndex++;
            SetCurrentAttack(attack);
        }
    }
}
