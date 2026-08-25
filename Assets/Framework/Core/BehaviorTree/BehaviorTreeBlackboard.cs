using System.Collections.Generic;
using UnityEngine;

namespace GameMain2.Framework.Core.BehaviorTree
{
    /// <summary>保存行为树节点之间共享的运行时数据。</summary>
    public sealed class BehaviorTreeBlackboard
    {
        private readonly Dictionary<string, object> values = new Dictionary<string, object>();

        /// <summary>写入或覆盖布尔值。</summary>
        public void SetBool(string key, bool value)
        {
            values[key] = value;
        }

        /// <summary>尝试读取布尔值，类型不匹配时返回 false。</summary>
        public bool TryGetBool(string key, out bool value)
        {
            return TryGetValue(key, out value);
        }

        /// <summary>写入或覆盖整数值。</summary>
        public void SetInt(string key, int value)
        {
            values[key] = value;
        }

        /// <summary>尝试读取整数值，类型不匹配时返回 false。</summary>
        public bool TryGetInt(string key, out int value)
        {
            return TryGetValue(key, out value);
        }

        /// <summary>写入或覆盖浮点值。</summary>
        public void SetFloat(string key, float value)
        {
            values[key] = value;
        }

        /// <summary>尝试读取浮点值，类型不匹配时返回 false。</summary>
        public bool TryGetFloat(string key, out float value)
        {
            return TryGetValue(key, out value);
        }

        /// <summary>写入或覆盖三维向量值。</summary>
        public void SetVector3(string key, Vector3 value)
        {
            values[key] = value;
        }

        /// <summary>尝试读取三维向量值，类型不匹配时返回 false。</summary>
        public bool TryGetVector3(string key, out Vector3 value)
        {
            return TryGetValue(key, out value);
        }

        /// <summary>写入或覆盖对象引用。</summary>
        public void SetObject<T>(string key, T value) where T : class
        {
            values[key] = value;
        }

        /// <summary>尝试读取指定类型的对象引用，类型不匹配时返回 false。</summary>
        public bool TryGetObject<T>(string key, out T value) where T : class
        {
            return TryGetValue(key, out value);
        }

        /// <summary>移除指定键对应的数据，存在并移除成功时返回 true。</summary>
        public bool Remove(string key)
        {
            return values.Remove(key);
        }

        /// <summary>清空黑板中的所有数据。</summary>
        public void Clear()
        {
            values.Clear();
        }

        /// <summary>按强类型读取黑板值，统一处理缺失或类型不匹配的情况。</summary>
        private bool TryGetValue<T>(string key, out T value)
        {
            if (values.TryGetValue(key, out object rawValue) && rawValue is T typedValue)
            {
                value = typedValue;
                return true;
            }

            value = default;
            return false;
        }
    }
}
