using System;

namespace GameMain2.Framework.Core
{
    public abstract class EventArgsBase : EventArgs
    {
        public abstract int Id
        {
            get;
        } 
    }
}