using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;


namespace GameMain2.Framework.Manager
{
    

    public class ResourceManager : SingletonManager<ResourceManager>
    {
        
        private readonly Dictionary<string,AsyncOperationHandle> m_loadHandles = new Dictionary<string,AsyncOperationHandle>();

        /// <summary>异步加载资源，只有成功结果会写入缓存，失败时释放句柄并抛出明确异常。</summary>
        public async Task<T> LoadAssetSync<T>(string path) where T : UnityEngine.Object
        {
            if (m_loadHandles.TryGetValue(path, out AsyncOperationHandle cachedHandle))
            {
                return cachedHandle.Result as T;
            }

            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(path);
            try
            {
                await handle.Task;
            }
            catch (Exception ex)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }

                throw new InvalidOperationException($"加载资源异常：{path}", ex);
            }

            if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
            {
                m_loadHandles.Add(path, handle);
                return handle.Result;
            }

            string errorMessage = handle.OperationException == null ? "未知原因" : handle.OperationException.Message;
            if (handle.IsValid())
            {
                Addressables.Release(handle);
            }

            throw new InvalidOperationException($"加载资源失败：{path}，错误：{errorMessage}");
        }

        /// <summary>同步加载资源，等待完成后只缓存成功句柄，失败时释放句柄并抛出明确异常。</summary>
        public T LoadAsset<T>(string path) where T : UnityEngine.Object
        {
            if (m_loadHandles.TryGetValue(path, out AsyncOperationHandle cachedHandle))
            {
                return cachedHandle.Result as T;
            }

            AsyncOperationHandle<T> handle = Addressables.LoadAssetAsync<T>(path);
            try
            {
                handle.WaitForCompletion();
            }
            catch (Exception ex)
            {
                if (handle.IsValid())
                {
                    Addressables.Release(handle);
                }

                throw new InvalidOperationException($"加载资源异常：{path}", ex);
            }

            if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
            {
                m_loadHandles.Add(path, handle);
                return handle.Result;
            }

            string errorMessage = handle.OperationException == null ? "未知原因" : handle.OperationException.Message;
            if (handle.IsValid())
            {
                Addressables.Release(handle);
            }

            throw new InvalidOperationException($"加载资源失败：{path}，错误：{errorMessage}");
        }

        /// <summary>在原点实例化指定路径的预制体资源。</summary>
        public GameObject Instantiate(string path)
        {
            return Instantiate(path, Vector3.zero, Quaternion.identity);
        }

        /// <summary>同步加载预制体后按指定位置、旋转和父节点实例化。</summary>
        public GameObject Instantiate(string path, Vector3 pos, Quaternion rot, Transform parentTransform = null)
        {
            GameObject prefab = LoadAsset<GameObject>(path);
            GameObject gameObject = Instantiate(prefab,pos,rot, parentTransform);
            return gameObject;
        }

        /// <summary>释放指定路径对应的缓存资源句柄。</summary>
        public void ReleaseAsset(string path)
        {
            if (m_loadHandles.TryGetValue(path, out AsyncOperationHandle handle))
            {
                Addressables.Release(handle);
                m_loadHandles.Remove(path);
            }
        }

        /// <summary>释放全部已缓存的资源句柄。</summary>
        public void ReleaseAll()
        {
            foreach (KeyValuePair<string, AsyncOperationHandle> handle in m_loadHandles)
            {
                Addressables.Release(handle.Value);
            }
            m_loadHandles.Clear();
        }

        /// <summary>管理器销毁时释放 Addressables 缓存资源。</summary>
        protected override void OnDestroy()
        {
            ReleaseAll();
            base.OnDestroy();
        }
    }
}
