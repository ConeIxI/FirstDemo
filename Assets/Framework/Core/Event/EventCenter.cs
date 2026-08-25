using System;
using System.Collections.Generic;

namespace GameMain2.Framework.Core
{
    /// <summary>
    /// EventCenter 类是用于管理游戏内事件的中心类，它继承自 SingletonManager&lt;EventCenter&gt; 以确保整个应用程序中只有一个实例存在。
    /// 该类提供了订阅、取消订阅和触发事件的方法，允许开发者通过事件 ID 来管理和响应特定事件。
    /// </summary>
    public sealed partial class EventCenter : SingletonManager<EventCenter>
    {
        private Dictionary<int, EventHandler<EventArgsBase>> m_EventHandlers;
        private Queue<Event> m_EventQueue;


        /// <summary>
        /// 初始化事件中心，创建事件处理程序字典和事件队列。
        /// </summary>
        protected override void Awake()
        {
            base.Awake();
            if (!IsSingletonInstance)
            {
                return;
            }

            EnsureRuntimeState();
        }

        /// <summary>
        /// 将事件处理程序订阅到特定事件 ID。
        /// </summary>
        /// <param name="id">事件的唯一标识符。</param>
        /// <param name="eventHandler">事件触发时要调用的事件处理程序。它不应为 null。</param>
        public void Subscribe(int id, EventHandler<EventArgsBase> eventHandler)
        {
            if (eventHandler == null)
                return;
            EnsureRuntimeState();
            if (m_EventHandlers.ContainsKey(id))
            {
                m_EventHandlers[id] += eventHandler;
            }
            else
            {
                m_EventHandlers.Add(id, eventHandler);
            }
            
        }

        /// <summary>
        /// 从特定事件ID取消订阅事件处理程序。
        /// </summary>
        /// <param name="id">要取消订阅的事件的唯一标识符。</param>
        /// <param name="eventHandler">要从事件中移除的事件处理程序。它不应为 null。</param>
        public void UnSubscribe(int id, EventHandler<EventArgsBase> eventHandler)
        {
            if (eventHandler == null)
                return;
            if (m_EventHandlers == null)
                return;
            if (m_EventHandlers.ContainsKey(id))
            {
                m_EventHandlers[id] -= eventHandler;
            }
        }

        public static bool TryUnSubscribe(int id, EventHandler<EventArgsBase> eventHandler)
        {
            if (!TryGetInstance(out EventCenter eventCenter))
            {
                return false;
            }

            eventCenter.UnSubscribe(id, eventHandler);
            return true;
        }

        /// <summary>返回主菜单时清空所有运行期事件订阅和待分发事件，保留事件中心单例本身。</summary>
        public void ResetRuntimeStateForMainMenu()
        {
            EnsureRuntimeState();
            m_EventHandlers.Clear();
            m_EventQueue.Clear();
        }

        /// <summary>
        /// 触发指定事件，将事件添加到事件队列中。
        /// </summary>
        /// <param name="sender">触发事件的对象。</param>
        /// <param name="e">与事件相关的数据。它不应为 null。</param>
        public void Fire(object sender, EventArgsBase e)
        {
            EnsureRuntimeState();

            lock (m_EventQueue)
            {
                m_EventQueue.Enqueue(new Event(sender,e));
            }
        }

        /// <summary>
        /// 触发指定事件，并调用关联的事件处理程序。
        /// </summary>
        /// <param name="e">包含事件发送者和事件参数的对象。</param>
        /// <param name="eventHandler">要触发的事件处理程序。它不应为 null。</param>
        private void TriggerEvent(Event e, EventHandler<EventArgsBase> eventHandler)
        {
            if (eventHandler != null)
            {
                eventHandler(e.Sender, e.EventArgs);
            }
        }

        /// <summary>
        /// 确保事件中心运行时容器已创建，避免 BeforeSceneLoad 阶段订阅早于 Awake 初始化。
        /// </summary>
        private void EnsureRuntimeState()
        {
            if (m_EventHandlers == null)
            {
                m_EventHandlers = new Dictionary<int, EventHandler<EventArgsBase>>();
            }

            if (m_EventQueue == null)
            {
                m_EventQueue = new Queue<Event>();
            }
        }

        /// <summary>
        /// 更新事件队列，处理所有待处理的事件。
        /// 该方法会从事件队列中取出每个事件，并根据事件ID查找对应的事件处理程序来触发事件。
        /// </summary>
        private void Update()
        {
            if (m_EventQueue == null || m_EventHandlers == null)
            {
                return;
            }

            while (m_EventQueue.Count > 0)
            {
                Event e;
                lock (m_EventQueue)
                {
                    e = m_EventQueue.Dequeue();
                }

                if (m_EventHandlers.ContainsKey(e.EventArgs.Id))
                {
                    TriggerEvent(e,m_EventHandlers[e.EventArgs.Id]);
                }
            }
        }

        protected override void OnDestroy()
        {
            m_EventHandlers?.Clear();
            m_EventQueue?.Clear();
            base.OnDestroy();
        }
    }
}
