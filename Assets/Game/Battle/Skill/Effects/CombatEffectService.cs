using System.Collections;
using System.Collections.Generic;
using Game.Battle.Ability;
using Game.Battle.Skill.Common;
using GameMain2.Framework.Manager;
using UnityEngine;

namespace Game.Battle.Skill.Effects
{
    public sealed class CombatEffectService : MonoBehaviour
    {
        public static CombatEffectService Instance { get; private set; }

        private readonly List<CombatEffectInstanceHandle> m_activeInstances = new List<CombatEffectInstanceHandle>();
        private readonly List<CombatEffectInstanceHandle> m_recycleBuffer = new List<CombatEffectInstanceHandle>();
        private readonly Dictionary<Object, Dictionary<string, List<CombatEffectInstanceHandle>>> m_channelInstances =
            new Dictionary<Object, Dictionary<string, List<CombatEffectInstanceHandle>>>();

        private CombatEffectPool m_pool;

        /// <summary>初始化场景级战斗特效服务实例。</summary>
        private void Awake()
        {
            Instance = this;
            m_pool = new CombatEffectPool(transform);
        }

        /// <summary>更新固定时长和粒子完成类型的活动特效。</summary>
        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>播放一条技能特效绑定并返回活动实例句柄。</summary>
        public CombatEffectInstanceHandle Play(CombatEffectPlayContext context)
        {
            CombatEffectConfig config = ConfigManager.Instance.GetCombatEffectConfig(context.Binding.effectId);
            CombatEffectRuntimeData runtimeData = CreateRuntimeData(config, context.Binding);
            ValidateRuntimeData(runtimeData, context);

            if (runtimeData.attachment == CombatEffectAttachment.TargetPreloadedEffect)
            {
                return PlayPreloadedTargetEffect(runtimeData, context);
            }

            if (runtimeData.concurrency == CombatEffectConcurrency.UniqueChannel)
            {
                StopOwnerChannel(context.Owner, runtimeData.channel);
            }

            GameObject instance = m_pool.Spawn(runtimeData.path);
            ValidateSpawnedInstance(instance, runtimeData, context);
            ApplyTransform(instance, runtimeData, context);
            RestartParticles(instance);

            CombatEffectInstanceHandle handle = new CombatEffectInstanceHandle(
                runtimeData.path,
                runtimeData.channel,
                context.Owner,
                instance,
                runtimeData.recycleMode,
                runtimeData.duration);
            RegisterHandle(handle, runtimeData.concurrency);
            return handle;
        }

        /// <summary>停止指定所有者的指定通道特效。</summary>
        public void StopOwnerChannel(Object owner, string channel)
        {
            if (string.IsNullOrEmpty(channel))
            {
                return;
            }

            Dictionary<string, List<CombatEffectInstanceHandle>> ownerChannels;
            if (!m_channelInstances.TryGetValue(owner, out ownerChannels) || !ownerChannels.ContainsKey(channel))
            {
                return;
            }

            m_recycleBuffer.Clear();
            m_recycleBuffer.AddRange(ownerChannels[channel]);
            for (int i = 0; i < m_recycleBuffer.Count; i++)
            {
                Recycle(m_recycleBuffer[i]);
            }
        }

        /// <summary>回收指定所有者名下的所有活动特效。</summary>
        public void StopOwner(Object owner)
        {
            m_recycleBuffer.Clear();
            for (int i = 0; i < m_activeInstances.Count; i++)
            {
                if (m_activeInstances[i].Owner == owner)
                {
                    m_recycleBuffer.Add(m_activeInstances[i]);
                }
            }

            for (int i = 0; i < m_recycleBuffer.Count; i++)
            {
                Recycle(m_recycleBuffer[i]);
            }
        }

        /// <summary>递进活动特效生命周期并回收已结束实例。</summary>
        private void Tick(float deltaTime)
        {
            m_recycleBuffer.Clear();
            for (int i = 0; i < m_activeInstances.Count; i++)
            {
                CombatEffectInstanceHandle handle = m_activeInstances[i];
                if (handle.Instance == null)
                {
                    m_recycleBuffer.Add(handle);
                }
                else if (handle.RecycleMode == CombatEffectRecycleMode.FixedDuration)
                {
                    handle.RemainingDuration -= deltaTime;
                    if (handle.RemainingDuration <= 0f)
                    {
                        m_recycleBuffer.Add(handle);
                    }
                }
                else if (handle.RecycleMode == CombatEffectRecycleMode.ParticleComplete && IsParticleComplete(handle.Instance))
                {
                    m_recycleBuffer.Add(handle);
                }
            }

            for (int i = 0; i < m_recycleBuffer.Count; i++)
            {
                Recycle(m_recycleBuffer[i]);
            }
        }

        /// <summary>合并公共定义和技能局部覆盖。</summary>
        private static CombatEffectRuntimeData CreateRuntimeData(CombatEffectConfig config, SkillEffectBinding binding)
        {
            CombatEffectRuntimeData data = new CombatEffectRuntimeData();
            data.effectId = config.effectId;
            data.path = config.path;
            data.attachment = config.attachment;
            data.socketName = config.socketName;
            data.follow = config.follow;
            data.position = config.position.ToVector3();
            data.rotation = config.rotation.ToVector3();
            data.scale = config.scale.ToVector3();
            data.orientation = config.orientation;
            data.recycleMode = config.recycleMode;
            data.duration = config.duration;
            data.concurrency = config.concurrency;
            data.channel = config.channel;

            ApplyAttachmentOverride(data, binding.attachmentOverride);
            ApplyTransformOverride(data, binding.transformOverride);
            return data;
        }

        /// <summary>把挂载覆盖写入运行时播放数据。</summary>
        private static void ApplyAttachmentOverride(CombatEffectRuntimeData data, CombatEffectAttachmentOverride attachmentOverride)
        {
            if (attachmentOverride == null)
            {
                return;
            }

            if (attachmentOverride.overrideAttachment)
            {
                data.attachment = attachmentOverride.attachment;
            }

            if (attachmentOverride.overrideSocketName)
            {
                data.socketName = attachmentOverride.socketName;
            }

            if (attachmentOverride.overrideFollow)
            {
                data.follow = attachmentOverride.follow;
            }
        }

        /// <summary>把变换与生命周期覆盖写入运行时播放数据。</summary>
        private static void ApplyTransformOverride(CombatEffectRuntimeData data, CombatEffectTransformOverride transformOverride)
        {
            if (transformOverride == null)
            {
                return;
            }

            if (transformOverride.overridePosition)
            {
                data.position = transformOverride.position.ToVector3();
            }

            if (transformOverride.overrideRotation)
            {
                data.rotation = transformOverride.rotation.ToVector3();
            }

            if (transformOverride.overrideScale)
            {
                data.scale = transformOverride.scale.ToVector3();
            }

            if (transformOverride.overrideOrientation)
            {
                data.orientation = transformOverride.orientation;
            }

            if (transformOverride.overrideRecycleMode)
            {
                data.recycleMode = transformOverride.recycleMode;
            }

            if (transformOverride.overrideDuration)
            {
                data.duration = transformOverride.duration;
            }

            if (transformOverride.overrideConcurrency)
            {
                data.concurrency = transformOverride.concurrency;
            }

            if (transformOverride.overrideChannel)
            {
                data.channel = transformOverride.channel;
            }
        }

        /// <summary>校验运行时播放数据和上下文是否满足配置约束。</summary>
        private static void ValidateRuntimeData(CombatEffectRuntimeData data, CombatEffectPlayContext context)
        {
            if (RequiresPrefabPath(data.attachment) && string.IsNullOrEmpty(data.path))
            {
                throw new System.Exception($"{context.ContextName}特效{data.effectId}缺少 Prefab 路径");
            }

            if (RequiresSocketName(data.attachment)
                && string.IsNullOrEmpty(data.socketName))
            {
                throw new System.Exception($"{context.ContextName}特效{data.effectId}缺少挂点名称");
            }

            if ((data.recycleMode == CombatEffectRecycleMode.ManualStop || data.concurrency == CombatEffectConcurrency.UniqueChannel)
                && string.IsNullOrEmpty(data.channel))
            {
                throw new System.Exception($"{context.ContextName}特效{data.effectId}缺少通道名称");
            }

            if (data.recycleMode == CombatEffectRecycleMode.FixedDuration && data.duration <= 0f)
            {
                throw new System.Exception($"{context.ContextName}特效{data.effectId}固定时长必须大于零");
            }

            if ((data.attachment == CombatEffectAttachment.TargetSocket
                    || data.attachment == CombatEffectAttachment.TargetPreloadedEffect)
                && context.Target == null)
            {
                throw new System.Exception($"{context.ContextName}动作特效不能依赖受击者挂点");
            }
        }

        /// <summary>判断当前挂载模式是否需要通过 Prefab 路径动态生成特效。</summary>
        private static bool RequiresPrefabPath(CombatEffectAttachment attachment)
        {
            return attachment != CombatEffectAttachment.TargetPreloadedEffect;
        }

        /// <summary>判断当前挂载模式是否需要配置目标或来源层级里的子物体名称。</summary>
        private static bool RequiresSocketName(CombatEffectAttachment attachment)
        {
            return attachment == CombatEffectAttachment.SourceSocket
                || attachment == CombatEffectAttachment.TargetSocket
                || attachment == CombatEffectAttachment.TargetPreloadedEffect;
        }

        /// <summary>播放目标角色层级中已经预先挂好的特效对象。</summary>
        private CombatEffectInstanceHandle PlayPreloadedTargetEffect(CombatEffectRuntimeData data, CombatEffectPlayContext context)
        {
            GameObject instance = ResolvePreloadedTargetEffect(data, context);
            ValidatePreloadedInstance(instance, data, context);
            StartCoroutine(PlayPreloadedTargetEffectNextFrame(instance));
            return null;
        }

        /// <summary>延后一帧播放预挂特效，避开格挡/弹反状态切换同帧对武器层级的激活覆盖。</summary>
        private IEnumerator PlayPreloadedTargetEffectNextFrame(GameObject instance)
        {
            yield return null;
            ActivateParticleHierarchy(instance);
            StopPreloadedParticles(instance);
            instance.SetActive(true);
            RestartParticles(instance);
        }

        /// <summary>确保预挂特效根节点和粒子子节点处于激活状态，避免隐藏子节点无法播放。</summary>
        private static void ActivateParticleHierarchy(GameObject instance)
        {
            instance.SetActive(true);
            ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                particles[i].gameObject.SetActive(true);
            }
        }

        /// <summary>停止预挂特效对象下的所有粒子并清空当前可见粒子。</summary>
        private static void StopPreloadedParticles(GameObject instance)
        {
            ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        /// <summary>按配置名称在受击目标完整层级中查找预挂特效对象。</summary>
        private static GameObject ResolvePreloadedTargetEffect(CombatEffectRuntimeData data, CombatEffectPlayContext context)
        {
            Transform effect = context.Target.transform.Find(data.socketName);
            if (effect == null)
            {
                effect = FindChildRecursive(context.Target.transform, data.socketName);
            }

            if (effect == null)
            {
                throw new System.Exception($"{context.ContextName}特效{data.effectId}找不到预挂特效：{data.socketName}");
            }

            return effect.gameObject;
        }

        /// <summary>校验预挂特效对象至少包含一个可重播的粒子系统。</summary>
        private static void ValidatePreloadedInstance(GameObject instance, CombatEffectRuntimeData data, CombatEffectPlayContext context)
        {
            if (instance.GetComponentsInChildren<ParticleSystem>(true).Length == 0)
            {
                throw new System.Exception($"{context.ContextName}预挂特效{data.effectId}不含粒子系统：{data.socketName}");
            }
        }

        /// <summary>校验实例与粒子完成回收规则是否兼容。</summary>
        private static void ValidateSpawnedInstance(GameObject instance, CombatEffectRuntimeData data, CombatEffectPlayContext context)
        {
            if (data.recycleMode == CombatEffectRecycleMode.ParticleComplete
                && instance.GetComponentsInChildren<ParticleSystem>(true).Length == 0)
            {
                throw new System.Exception($"{context.ContextName}特效{data.effectId}使用粒子完成回收但 Prefab 不含粒子系统");
            }
        }

        /// <summary>把实例放置到目标挂点或世界命中点。</summary>
        private static void ApplyTransform(GameObject instance, CombatEffectRuntimeData data, CombatEffectPlayContext context)
        {
            Transform parent = ResolveParent(data, context);
            Quaternion rotation = ResolveRotation(data, context, parent);

            if (parent != null && data.follow)
            {
                instance.transform.SetParent(parent, false);
                instance.transform.localPosition = data.position;
                instance.transform.localRotation = Quaternion.Euler(data.rotation);
            }
            else if (parent != null)
            {
                instance.transform.SetParent(null, true);
                instance.transform.position = parent.position + rotation * data.position;
                instance.transform.rotation = rotation * Quaternion.Euler(data.rotation);
            }
            else
            {
                instance.transform.SetParent(null, true);
                instance.transform.position = context.HitPoint + rotation * data.position;
                instance.transform.rotation = rotation * Quaternion.Euler(data.rotation);
            }

            if (data.scale != Vector3.zero)
            {
                instance.transform.localScale = data.scale;
            }
        }

        /// <summary>解析当前播放数据所需的父挂点。</summary>
        private static Transform ResolveParent(CombatEffectRuntimeData data, CombatEffectPlayContext context)
        {
            if (data.attachment == CombatEffectAttachment.WorldHitPoint)
            {
                return null;
            }

            if (data.attachment == CombatEffectAttachment.SourceRoot)
            {
                return context.Source.transform;
            }

            CombatAbilitySystem owner = data.attachment == CombatEffectAttachment.SourceSocket ? context.Source : context.Target;
            Transform socket = owner.transform.Find(data.socketName);
            if (socket == null)
            {
                socket = FindChildRecursive(owner.transform, data.socketName);
            }

            if (socket == null)
            {
                throw new System.Exception($"{context.ContextName}特效{data.effectId}找不到挂点：{data.socketName}");
            }

            return socket;
        }

        /// <summary>在角色完整层级里递归查找指定名称的特效挂点。</summary>
        private static Transform FindChildRecursive(Transform root, string socketName)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == socketName)
                {
                    return child;
                }

                Transform socket = FindChildRecursive(child, socketName);
                if (socket != null)
                {
                    return socket;
                }
            }

            return null;
        }

        /// <summary>根据配置规则解析世界旋转。</summary>
        private static Quaternion ResolveRotation(CombatEffectRuntimeData data, CombatEffectPlayContext context, Transform parent)
        {
            if (data.orientation == CombatEffectOrientation.SourceForward)
            {
                return Quaternion.LookRotation(context.Source.transform.forward, Vector3.up);
            }

            if (data.orientation == CombatEffectOrientation.HitDirection)
            {
                return Quaternion.LookRotation(context.HitDirection, Vector3.up);
            }

            if (parent != null)
            {
                return parent.rotation;
            }

            return Quaternion.identity;
        }

        /// <summary>重新播放实例下所有粒子系统。</summary>
        private static void RestartParticles(GameObject instance)
        {
            ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                particles[i].Clear(true);
                particles[i].Play(true);
            }
        }

        /// <summary>判断实例下全部粒子系统是否都已结束。</summary>
        private static bool IsParticleComplete(GameObject instance)
        {
            ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
            if (particles.Length == 0)
            {
                throw new System.Exception($"粒子完成回收要求 Prefab 包含 ParticleSystem：{instance.name}");
            }

            for (int i = 0; i < particles.Length; i++)
            {
                if (particles[i].IsAlive(true))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>登记活动实例、唯一通道归属和手动停止通道归属。</summary>
        private void RegisterHandle(CombatEffectInstanceHandle handle, CombatEffectConcurrency concurrency)
        {
            m_activeInstances.Add(handle);
            if (concurrency != CombatEffectConcurrency.UniqueChannel && handle.RecycleMode != CombatEffectRecycleMode.ManualStop)
            {
                return;
            }

            Dictionary<string, List<CombatEffectInstanceHandle>> ownerChannels;
            if (!m_channelInstances.TryGetValue(handle.Owner, out ownerChannels))
            {
                ownerChannels = new Dictionary<string, List<CombatEffectInstanceHandle>>();
                m_channelInstances.Add(handle.Owner, ownerChannels);
            }

            List<CombatEffectInstanceHandle> channelHandles;
            if (!ownerChannels.TryGetValue(handle.Channel, out channelHandles))
            {
                channelHandles = new List<CombatEffectInstanceHandle>();
                ownerChannels.Add(handle.Channel, channelHandles);
            }

            channelHandles.Add(handle);
        }

        /// <summary>回收活动实例并清理通道记录。</summary>
        private void Recycle(CombatEffectInstanceHandle handle)
        {
            m_activeInstances.Remove(handle);

            if (string.IsNullOrEmpty(handle.Channel))
            {
                m_pool.Despawn(handle);
                return;
            }

            Dictionary<string, List<CombatEffectInstanceHandle>> ownerChannels;
            List<CombatEffectInstanceHandle> channelHandles;
            if (m_channelInstances.TryGetValue(handle.Owner, out ownerChannels)
                && ownerChannels.TryGetValue(handle.Channel, out channelHandles))
            {
                channelHandles.Remove(handle);
                if (channelHandles.Count == 0)
                {
                    ownerChannels.Remove(handle.Channel);
                }
            }

            if (handle.Instance != null)
            {
                m_pool.Despawn(handle);
            }
        }

        private sealed class CombatEffectRuntimeData
        {
            public string effectId;
            public string path;
            public CombatEffectAttachment attachment;
            public string socketName;
            public bool follow;
            public Vector3 position;
            public Vector3 rotation;
            public Vector3 scale;
            public CombatEffectOrientation orientation;
            public CombatEffectRecycleMode recycleMode;
            public float duration;
            public CombatEffectConcurrency concurrency;
            public string channel;
        }
    }
}
