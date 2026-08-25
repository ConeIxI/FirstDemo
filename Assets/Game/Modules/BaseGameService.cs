namespace GameMain2.Scripts.Modules
{
    /// <summary>
    /// 长生命周期功能服务基类，用于统一业务系统的初始化、存档加载和清理入口。
    /// </summary>
    public abstract class BaseGameService
    {
        /// <summary>
        /// 初始化功能服务的运行期依赖和事件订阅。
        /// </summary>
        public virtual void Init()
        {
        }

        /// <summary>
        /// 加载功能服务持有的模型数据。
        /// </summary>
        public virtual void LoadModel()
        {
        }

        /// <summary>
        /// 保存功能服务持有的模型数据。
        /// </summary>
        public virtual void SaveModel()
        {
        }

        /// <summary>
        /// 清理功能服务持有的模型数据和运行期状态。
        /// </summary>
        public virtual void ClearModel()
        {
        }
    }
}
