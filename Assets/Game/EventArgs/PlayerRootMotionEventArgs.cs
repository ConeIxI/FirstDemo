using GameMain2.Framework.Core;
using UnityEngine;

namespace GameMain2.Game.EventArgs
{
    public class PlayerRootMotionEventArgs : EventArgsBase
    {
        public static int EventId = typeof(PlayerRootMotionEventArgs).GetHashCode();

        private Vector3 m_Position;
        private Quaternion m_Quaternion;

        public override int Id
        {
            get
            {
                return EventId;
            }
        }

        public Vector3 Position
        {
            get => m_Position;
        }

        public Quaternion Quaternion
        {
            get => m_Quaternion;
        }

        public PlayerRootMotionEventArgs()
        {
            m_Position = Vector3.zero;
            m_Quaternion = Quaternion.identity;
        }

        public PlayerRootMotionEventArgs(Vector3 position, Quaternion quaternion)
        {
            m_Position = position;
            m_Quaternion = quaternion;
        }
    }
}