using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace GameMain2.Framework.Core.FSM
{
    /// <summary>
    /// 表示实现有限状态机（FSM）的基类，具有表示FSM所有者的泛型类型参数。
    /// </summary>
    /// <typeparam name="T">本FSM管理的owner类型.</typeparam>
    public class FsmBase<T>
    {
        private T m_Owner;

        private Dictionary<Type, FsmStateBase<T>> m_States;

        private Dictionary<string,object> m_Datas;
        
        private FsmStateBase<T> m_CurState;

        public T Owner
        {
            get => m_Owner;
        }

        public FsmStateBase<T> CurState
        {
            get
            {
                return m_CurState;
            }
        }

        public FsmBase(T owner,FsmStateBase<T>[] states) : this(owner)
        {
            if (states == null)
            {
                throw new Exception("states is null");
            }
            
            foreach (var state in states)
            {
                AddState(state);
            }
        }

        public FsmBase(T owner)
        {
            if (owner == null)
            {
                throw new Exception("owner is null");
            }
            m_Owner = owner;
            m_States = new Dictionary<Type, FsmStateBase<T>>();
            m_Datas = new Dictionary<string, object>();
            m_CurState = null;
        }

        public void AddState(FsmStateBase<T> state)
        {
            if (state == null)
            {
                throw new Exception("state is null");
            }

            if (!m_States.ContainsKey(state.GetType()))
            {
                m_States.Add(state.GetType(), state);
            }
        }

        public void DeleteState(FsmStateBase<T> state)
        {
            if (state == null)
            {
                throw new Exception("state is null");
            }
            DeleteState(state.GetType());
        }

        public void DeleteState(Type type)
        {
            if (type == null)
            {
                throw new Exception("type is null");
            }
            
            if (m_States.ContainsKey(type))
            {
                m_States.Remove(type);
            }
        }

        /// <summary>
        /// 设置FSM的初始状态。此方法接收一个状态实例，并将其设置为当前状态，同时调用该状态的进入方法。
        /// </summary>
        /// <param name="state">要设置为初始状态的状态实例，必须是FsmStateBase&lt;T&gt;的一个实例。</param>
        /// <exception cref="Exception">如果提供的状态实例为空，则抛出异常。</exception>
        public void SetStartState(FsmStateBase<T> state)
        {
            if (state == null)
            {
                throw new Exception("state is null");
            }
            SetStartState(state.GetType());
        }

        public void SetStartState(Type type)
        {
            if (type == null)
            {
                throw new Exception("type is null");
            }
            
            if (!m_States.ContainsKey(type))
            {
                throw new Exception(string.Format("{0} can not be found", type.Name));
            }
            
            m_CurState = m_States[type];
            m_CurState.Enter(this);
        }

        /// <summary>
        /// 将当前状态更改为指定的状态类型。此方法首先使当前状态退出，然后将当前状态设置为指定的新状态，并调用新状态的进入方法。
        /// </summary>
        /// <typeparam name="T">要切换到的目标状态的类型，必须继承自FsmStateBase&lt;T&gt;。</typeparam>
        /// <exception cref="Exception">如果当前状态为空或目标状态在状态字典中找不到，则抛出异常。</exception>
        public void ChangeState<State>() where State : FsmStateBase<T>
        {
            if (m_CurState == null)
            {
                throw new Exception("CurrentState is null");
            }

            if (!m_States.ContainsKey(typeof(State)))
            {
                // throw new Exception(string.Format("{0} can not be found", typeof(State).Name));
                return;
            }
            
            m_CurState.Exit(this);

            m_CurState = m_States[typeof(State)];
            
            m_CurState.Enter(this);
        }

        // 按运行时类型切换状态，供数据驱动 AI 将配置状态映射到 FSM 状态类。
        public void ChangeState(Type type)
        {
            if (type == null)
            {
                throw new Exception("type is null");
            }

            if (m_CurState == null)
            {
                throw new Exception("CurrentState is null");
            }

            if (!m_States.ContainsKey(type))
            {
                return;
            }

            m_CurState.Exit(this);
            m_CurState = m_States[type];
            m_CurState.Enter(this);
        }

        /// <summary>
        /// 更新当前状态。如果存在当前状态，则调用其Update方法。
        /// </summary>
        public void Update(float deltaTime)
        {
            if (m_CurState != null)
                m_CurState.Update(this,deltaTime);
        }

        public void Shutdown()
        {
            if (m_CurState != null)
            {
                m_CurState.Exit(this);
                m_CurState = null;
            }

            m_States.Clear();
            m_Datas.Clear();
        }

        public void SetData(string name, object data)
        {
            m_Datas[name] = data;
        }

        public object GetData(string name)
        {
            if (m_Datas.ContainsKey(name))
            {
                return m_Datas[name];
            }
            return null;
        }
    }
}
