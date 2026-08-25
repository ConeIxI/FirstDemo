using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Game.Timeline.Execution
{
    public sealed class ExecutionDamageMarker : Marker, INotification, INotificationOptionProvider
    {
        private static readonly PropertyName MarkerId = new PropertyName(nameof(ExecutionDamageMarker));

        public PropertyName id => MarkerId;
        public NotificationFlags flags => NotificationFlags.TriggerOnce;
    }
}
