using Game.Battle.Skill.Common;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Game.Timeline.Execution
{
    /// <summary>处决 Timeline 的特效触发标记，用于在精确时间点播放战斗特效。</summary>
    public sealed class ExecutionEffectMarker : Marker, INotification, INotificationOptionProvider
    {
        private static readonly PropertyName MarkerId = new PropertyName(nameof(ExecutionEffectMarker));

        [SerializeField] private string effectId;
        [SerializeField] private CombatEffectAttachmentOverride attachmentOverride;
        [SerializeField] private CombatEffectTransformOverride transformOverride;

        /// <summary>获取 Timeline 通知 ID，供 PlayableDirector 分发到接收器。</summary>
        public PropertyName id => MarkerId;

        /// <summary>限制同一个处决播放周期内该标记只触发一次。</summary>
        public NotificationFlags flags => NotificationFlags.TriggerOnce;

        /// <summary>获取战斗特效配置 ID。</summary>
        public string EffectId => effectId;

        /// <summary>获取挂点覆盖配置，未配置时使用特效表默认挂点。</summary>
        public CombatEffectAttachmentOverride AttachmentOverride => attachmentOverride;

        /// <summary>获取变换覆盖配置，未配置时使用特效表默认变换。</summary>
        public CombatEffectTransformOverride TransformOverride => transformOverride;
    }
}
