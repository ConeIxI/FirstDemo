using System.Collections.Generic;
using GameMain2.Framework.Manager;
using UnityEngine;

namespace Game.Battle.Skill.Effects
{
    internal sealed class CombatEffectPool
    {
        private readonly Transform m_root;
        private readonly Dictionary<string, Stack<GameObject>> m_instances = new Dictionary<string, Stack<GameObject>>();

        /// <summary>创建由战斗特效服务独占的对象池。</summary>
        public CombatEffectPool(Transform root)
        {
            m_root = root;
        }

        /// <summary>按 Prefab 路径取出一个可播放实例。</summary>
        public GameObject Spawn(string path)
        {
            Stack<GameObject> pool;
            if (m_instances.TryGetValue(path, out pool) && pool.Count > 0)
            {
                GameObject pooled = pool.Pop();
                pooled.SetActive(true);
                return pooled;
            }

            return ResourceManager.Instance.Instantiate(path);
        }

        /// <summary>停止粒子并把实例放回对应路径的池中。</summary>
        public void Despawn(CombatEffectInstanceHandle handle)
        {
            GameObject instance = handle.Instance;
            ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
            {
                particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            instance.transform.SetParent(m_root, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            instance.SetActive(false);

            Stack<GameObject> pool;
            if (!m_instances.TryGetValue(handle.Path, out pool))
            {
                pool = new Stack<GameObject>();
                m_instances.Add(handle.Path, pool);
            }

            pool.Push(instance);
        }
    }
}
