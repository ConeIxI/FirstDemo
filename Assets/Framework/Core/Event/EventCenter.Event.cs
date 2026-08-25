namespace GameMain2.Framework.Core
{
    public sealed partial class EventCenter
    {
        protected sealed class Event
        {
            private object m_Sender;
            private EventArgsBase m_EventArgs;

            public object Sender
            {
                get
                {
                    return m_Sender;
                }
                set
                {
                    m_Sender = value;
                }
            }

            public EventArgsBase EventArgs
            {
                get
                {
                    return m_EventArgs;
                }
                set
                {
                    m_EventArgs = value;
                }
            }

            public Event()
            {
                m_Sender = null;
                m_EventArgs = null;
            }

            public Event(object sender, EventArgsBase eventArgs)
            {
                m_Sender = sender;
                m_EventArgs = eventArgs;
            }

        }
    }
}