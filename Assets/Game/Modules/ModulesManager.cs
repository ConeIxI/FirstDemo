using System.Collections.Generic;
using GameMain2.Scripts.Modules.Bag;
using UnityEngine;

namespace GameMain2.Scripts.Modules
{
    /// <summary>
    /// 长生命周期功能系统的统一入口，负责创建并初始化各个 Logic。
    /// </summary>
    public static class ModulesManager
    {
        private static readonly List<BaseGameService> Services = new List<BaseGameService>();
        private static bool s_initialized;
        private static BagLogic s_bag;

        public static BagLogic Bag
        {
            get
            {
                Init();
                return s_bag;
            }
        }

        /// <summary>
        /// Unity 运行前创建功能层，确保 UI 未打开时也能接收拾取等业务事件。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            Init();
        }

        /// <summary>
        /// 幂等初始化所有长期功能服务，重复调用不会重复创建 Logic。
        /// </summary>
        public static void Init()
        {
            if (s_initialized)
            {
                return;
            }

            s_bag = AddService(new BagLogic());

            for (int i = 0; i < Services.Count; i++)
            {
                Services[i].Init();
            }

            for (int i = 0; i < Services.Count; i++)
            {
                Services[i].LoadModel();
            }

            s_initialized = true;
        }

        /// <summary>清空所有长生命周期功能服务的运行态，并重新创建基础业务入口。</summary>
        public static void ResetRuntimeState()
        {
            for (int i = 0; i < Services.Count; i++)
            {
                Services[i].ClearModel();
            }

            Services.Clear();
            s_bag = null;
            s_initialized = false;
            Init();
        }

        /// <summary>
        /// 注册功能服务并返回同一个实例，便于静态入口暴露强类型 Logic。
        /// </summary>
        private static T AddService<T>(T service) where T : BaseGameService
        {
            Services.Add(service);
            return service;
        }
    }
}
