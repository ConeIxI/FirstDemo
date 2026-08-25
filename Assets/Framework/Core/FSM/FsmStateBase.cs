namespace GameMain2.Framework.Core.FSM
{
     public abstract class FsmStateBase<T>
     {
         public abstract void Enter(FsmBase<T> fsm);

         public abstract void Update(FsmBase<T> fsm, float deltaTime);

         public abstract void Exit(FsmBase<T> fsm);

     }
}