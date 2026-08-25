using System;

namespace Game.Character.Enemy.Config
{
    [Serializable]
    public sealed class EnemyAnimationConfig
    {
        public const float DefaultTransitionDuration = 0.1f;

        public string idleAnimation = "Idle";
        public string combatIdleAnimation = "CombatIdle";
        public string combatIdleMoveLeftAnimation = "MoveLeft";
        public string combatIdleMoveRightAnimation = "MoveRight";
        public string enterCombatAnimation = "EnterCombat";
        public string exitCombatAnimation = "ExitCombat";
        public string turnAnimation = "Turn";
        public string alertMoveAnimation = "AlertMove";
        public string moveAnimation = "Move";
        public string runAnimation = "Run";
        public string defenseAnimation = "Defense";
        public string defenseHitAnimation = "DefenseHit";
        public string retreatAnimation = "Retreat";
        public string getHitAnimation = "GetHit";
        public string defenseBreakAnimation = "DefenseBreak";
        public string unbalanceStartAnimation = "UnbalanceStart";
        public string unbalanceStartTrigger = "UnbalanceStart";
        public string unbalanceLoopAnimation = "UnbalanceLoop";
        public string unbalanceEndAnimation = "UnbalanceEnd";
        public string unbalanceEndTrigger = "UnbalanceEnd";
        public float unbalanceLoopDuration = 3f;
        public string deadAnimation = "Dead";
        public float transitionDuration = DefaultTransitionDuration;
    }
}
